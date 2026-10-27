using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The built-in agent: a coding agent over any OpenAI-compatible chat API (Google Gemini, OpenAI, OpenRouter, Ollama
/// and LM Studio on this PC…). It streams the model's reply, and runs the tools the model asks for: Read, Write, Edit,
/// LS, Glob, Grep and PowerShell, in the session's folder, asking first as the session's permissions say. It speaks
/// Claude Code's stream-json (<see cref="IAgent"/>), so the tab shows it as it shows Claude Code.
/// <list type="bullet">
/// <item>The conversation is kept (%LOCALAPPDATA%\DaisysApp\AI sessions\&lt;id&gt;.json), so a tab picks up where it
/// left off, after a restart too.</item>
/// <item>A rate limit (429) or an overloaded server waits and tries again by itself (as long as the server says, or
/// longer each time, up to 15 minutes), so a session that hits its limit carries on when it can.</item>
/// </list>
/// </summary>
internal sealed class ApiAgent : IAgent
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private const int MaxSteps = 60, MaxOutput = 30_000;

    private readonly Provider provider;
    private readonly SessionInfo info;
    private readonly Dispatcher ui;
    private readonly Func<bool> skipPermissions;
    private readonly List<JsonObject> messages;
    private readonly Queue<(string Text, IReadOnlyList<ImageData>? Images)> queue = new();
    private readonly Dictionary<string, TaskCompletionSource<(bool Allow, string? Reason)>> asking = new();
    private CancellationTokenSource? turn;
    private string model;
    private string mode;
    private bool disposed, busy, noStreamOptions;
    private int requestIds;

    public event Action<JsonElement>? Message;
    // (it doesn't end on its own: there's no process)
    public event Action<string>? Exited { add { } remove { } }
    public bool Running => !disposed;
    public int? ContextWindow => provider.ContextWindow > 0 ? provider.ContextWindow : null;

    private ApiAgent(Provider provider, SessionInfo info, Dispatcher ui, Func<bool> skipPermissions)
    {
        this.provider = provider;
        this.info = info;
        this.ui = ui;
        this.skipPermissions = skipPermissions;
        model = info.Model.Length > 0 ? info.Model : provider.Model;
        mode = info.Mode;
        messages = Load(info.Id);
    }

    public static ApiAgent Start(Provider provider, SessionInfo info, Dispatcher ui, Func<bool> skipPermissions)
    {
        var a = new ApiAgent(provider, info, ui, skipPermissions);
        Logging.Log.Here.Info($"AI session {info.Id} started on {provider.Name} ({a.model}) in {info.Folder}");
        a.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["model"] = a.model, ["session_id"] = info.Id, ["cwd"] = info.Folder });
        return a;
    }

    // ---------------------------------------------------------------- what the tab says

    public void Send(string text, IReadOnlyList<ImageData>? images = null)
    {
        if (disposed) return;
        if (text.StartsWith("/rename ", StringComparison.OrdinalIgnoreCase))
        {
            string name = text[8..].Trim();
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "session_title_changed", ["title"] = name });
            Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["num_turns"] = 0, ["is_error"] = false });
            return;
        }
        if (text.Trim().Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            messages.Clear();
            Save();
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "compact_boundary" });
            Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["num_turns"] = 0, ["is_error"] = false });
            return;
        }
        lock (queue) queue.Enqueue((text, images));
        if (!busy) _ = PumpAsync();
    }

    public void Control(string subtype, Dictionary<string, object?>? fields)
    {
        switch (subtype)
        {
            case "interrupt": Interrupt(); break;
            case "set_model": model = fields?.GetValueOrDefault("model") as string is { Length: > 0 } m ? m : provider.Model; break;
            case "set_permission_mode": mode = fields?.GetValueOrDefault("mode") as string ?? mode; break;
        }
    }

    public void Interrupt()
    {
        turn?.Cancel();
        lock (asking) foreach (var t in asking.Values) t.TrySetResult((false, "Interrupted"));
    }

    public void Answer(string requestId, bool allow, JsonElement input, string? reason = null)
    {
        lock (asking)
            if (asking.Remove(requestId, out var t)) t.TrySetResult((allow, reason));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interrupt();
    }

    // ---------------------------------------------------------------- the turn

    private async Task PumpAsync()
    {
        busy = true;
        try
        {
            while (!disposed)
            {
                (string Text, IReadOnlyList<ImageData>? Images)? next;
                lock (queue) next = queue.Count > 0 ? queue.Dequeue() : null;
                if (next is not { } m) break;
                if (m.Text.Trim().Equals("/compact", StringComparison.OrdinalIgnoreCase)) await CompactAsync();
                else await TurnAsync(m.Text, m.Images);
            }
        }
        finally { busy = false; }
    }

    private async Task TurnAsync(string text, IReadOnlyList<ImageData>? images = null)
    {
        turn = new CancellationTokenSource();
        var ct = turn.Token;
        var started = DateTime.UtcNow;
        long promptTokens = 0, outputTokens = 0;
        int steps = 0;
        string? error = null;
        string last = "";
        if (messages.Count == 0 || messages[0]["role"]?.GetValue<string>() != "system") messages.Insert(0, SystemMessage());
        else messages[0] = SystemMessage();
        if (images is { Count: > 0 })
        {
            // (OpenAI's image parts, as data: URLs)
            var parts = new JsonArray();
            if (text.Length > 0) parts.Add(new JsonObject { ["type"] = "text", ["text"] = text });
            foreach (var i in images) parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = i.DataUrl } });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = parts });
        }
        else messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        try
        {
            while (steps < MaxSteps)
            {
                steps++;
                var reply = await CompleteAsync(ct);
                promptTokens += reply.Prompt;
                outputTokens += reply.Output;
                last = reply.Text;
                var assistant = new JsonObject { ["role"] = "assistant", ["content"] = reply.Text };
                if (reply.Calls.Count > 0)
                    assistant["tool_calls"] = new JsonArray(reply.Calls.Select(c => (JsonNode)new JsonObject
                    {
                        ["id"] = c.Id, ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                    }).ToArray());
                messages.Add(assistant);
                Save();
                if (reply.Calls.Count == 0) break;
                foreach (var call in reply.Calls)
                {
                    ct.ThrowIfCancellationRequested();
                    var (output, failed) = await RunToolAsync(call, ct);
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = output });
                    Emit(new JsonObject
                    {
                        ["type"] = "user",
                        ["message"] = new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call.Id, ["content"] = output, ["is_error"] = failed }),
                        },
                    });
                }
                Save();
            }
        }
        catch (OperationCanceledException) { error = "interrupted"; }
        catch (Exception ex)
        {
            error = ex.Message;
            Logging.Log.Here.Warn($"AI ({provider.Name}): {ex.Message}");
        }
        Save();
        var result = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = error == null ? "success" : error == "interrupted" ? "interrupted" : "error_during_execution",
            ["is_error"] = error != null && error != "interrupted",
            ["num_turns"] = steps,
            ["duration_ms"] = (DateTime.UtcNow - started).TotalMilliseconds,
            ["total_cost_usd"] = 0,
            ["usage"] = new JsonObject { ["input_tokens"] = promptTokens, ["output_tokens"] = outputTokens },
        };
        if (error != null && error != "interrupted") result["result"] = F("{0}: {1}", provider.Name, error);
        else if (error == "interrupted") result["result"] = T("Stopped.");
        Emit(result);
    }

    /// <summary>/compact: the conversation so far summarised by the model, and the summary kept in its place.</summary>
    private async Task CompactAsync()
    {
        turn = new CancellationTokenSource();
        string? error = null;
        Emit(new JsonObject { ["type"] = "system", ["subtype"] = "status", ["status"] = "compacting" });
        long before = Tokens();
        try
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Summarise our conversation so far for yourself, to carry on from: the task, what's been done (files changed, commands run, results), decisions, and what's left. Be thorough but brief. Don't use any tools." });
            var reply = await CompleteAsync(turn.Token, tools: false);
            messages.Clear();
            messages.Add(SystemMessage());
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = "(The earlier conversation, summarised.)\n\n" + reply.Text });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = "Understood: I'll carry on from there." });
            Save();
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "compact_boundary", ["compact_metadata"] = new JsonObject { ["pre_tokens"] = before, ["post_tokens"] = Tokens() } });
        }
        catch (Exception ex) { error = ex.Message; }
        Emit(new JsonObject { ["type"] = "result", ["subtype"] = error == null ? "success" : "error_during_execution", ["is_error"] = error != null, ["num_turns"] = 1, ["result"] = error });
    }

    /// <summary>About how many tokens the conversation is (four characters each), for the context ring after /compact.</summary>
    private long Tokens() => messages.Sum(m => (long)m.ToJsonString().Length) / 4;

    private JsonObject SystemMessage() => new()
    {
        ["role"] = "system",
        ["content"] = $"""
            You are a capable coding agent working for the user in Daisy's App on Windows.
            Your working folder is: {info.Folder}
            Today is {DateTime.Now:dddd d MMMM yyyy}.
            Use the tools to look around (LS, Glob, Grep, Read), to change files (Edit for changes to an existing file, Write for new ones) and to run PowerShell commands. Read a file before you edit it. Paths may be relative to the working folder.
            Work step by step until the task is done, then say briefly what you did. Use Markdown in your answers.
            If the working folder has a CLAUDE.md, it holds notes about the project and earlier sessions: read it before you start on a task.
            {provider.Instructions}
            """.Trim(),
    };

    // ---------------------------------------------------------------- the model

    private sealed record Call(string Id, string Name, string Arguments);
    private sealed record Reply(string Text, List<Call> Calls, long Prompt, long Output);

    /// <summary>One request, streamed: its text and tool calls drawn as they come. A rate limit waits and tries again.</summary>
    private async Task<Reply> CompleteAsync(CancellationToken ct, bool tools = true)
    {
        var wait = TimeSpan.FromSeconds(20);
        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + "/chat/completions");
            if (provider.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new("Bearer", key);
            var body = new JsonObject
            {
                ["model"] = model,
                ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
                ["stream"] = true,
            };
            if (tools) body["tools"] = Tools();
            if (!noStreamOptions) body["stream_options"] = new JsonObject { ["include_usage"] = true };
            if (provider.MaxTokens > 0) body["max_tokens"] = provider.MaxTokens;
            if (provider.Temperature is { } t) body["temperature"] = t;
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException ex) when (attempt < 4)
            {
                Logging.Log.Here.Warn($"AI ({provider.Name}): {ex.Message}; trying again");
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
                continue;
            }
            using (response)
            {
                if (response.IsSuccessStatusCode) return await ReadStreamAsync(response, ct);
                string detail = await response.Content.ReadAsStringAsync(ct);
                int code = (int)response.StatusCode;
                if (code == 400 && !noStreamOptions && detail.Contains("stream_options", StringComparison.OrdinalIgnoreCase)) { noStreamOptions = true; continue; }
                if (code is 429 or 503 or 529 or 502)
                {
                    // the limit: wait as long as the server says (Retry-After, or Gemini's retryDelay), or longer each time
                    var delay = RetryAfter(response, detail) ?? wait;
                    wait = TimeSpan.FromSeconds(Math.Min(900, wait.TotalSeconds * 2));
                    var until = DateTime.Now + delay;
                    Logging.Log.Here.Info($"AI ({provider.Name}): {code}, waiting until {until:T}");
                    Emit(new JsonObject { ["type"] = "system", ["subtype"] = "status", ["status"] = "waiting_for_limit", ["until"] = until.ToString("o"), ["detail"] = Short(detail) });
                    await Task.Delay(delay, ct);
                    Emit(new JsonObject { ["type"] = "system", ["subtype"] = "status", ["status"] = "requesting" });
                    continue;
                }
                throw new InvalidOperationException($"{code} {response.ReasonPhrase}: {Short(detail)}");
            }
        }
    }

    private static string Short(string s)
    {
        try
        {
            using var d = JsonDocument.Parse(s);
            var root = d.RootElement.ValueKind == JsonValueKind.Array && d.RootElement.GetArrayLength() > 0 ? d.RootElement[0] : d.RootElement;
            if (root.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() ?? s : e.ToString();
        }
        catch { }
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage r, string body)
    {
        if (r.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } d) return d + TimeSpan.FromSeconds(1);
            if (ra.Date is { } at) return at - DateTimeOffset.Now + TimeSpan.FromSeconds(1);
        }
        var m = Regex.Match(body, "\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)s\"");
        return m.Success ? TimeSpan.FromSeconds(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 1) : null;
    }

    /// <summary>The server-sent events: text (and thinking: reasoning fields, or &lt;think&gt; in the text), tool calls, usage.</summary>
    private async Task<Reply> ReadStreamAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string id = "msg_" + Guid.NewGuid().ToString("N")[..12];
        Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "message_start", ["message"] = new JsonObject { ["id"] = id, ["model"] = model } } });
        var text = new StringBuilder();
        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args, int Block)>();
        int block = -1, textBlock = -1, thinkBlock = -1, thinkChars = 0;
        bool inThink = false;
        long prompt = 0, output = 0;
        int Open(string type, JsonObject? extra = null)
        {
            block++;
            var cb = extra ?? new JsonObject();
            cb["type"] = type;
            Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_start", ["index"] = block, ["content_block"] = cb } });
            return block;
        }
        void Close(ref int b)
        {
            if (b < 0) return;
            Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_stop", ["index"] = b } });
            b = -1;
        }
        void Delta(int b, string kind, string field, string value) =>
            Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_delta", ["index"] = b, ["delta"] = new JsonObject { ["type"] = kind, [field] = value } } });
        void Think(string s)
        {
            if (s.Length == 0) return;
            if (thinkBlock < 0) { Close(ref textBlock); thinkBlock = Open("thinking"); }
            thinkChars += s.Length;
            Delta(thinkBlock, "thinking_delta", "thinking", s);
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "thinking_tokens", ["estimated_tokens"] = thinkChars / 4 });
        }
        void Say(string s)
        {
            // <think>…</think> in the text (some local models): the thinking, not the answer
            while (s.Length > 0)
            {
                if (inThink)
                {
                    int end = s.IndexOf("</think>", StringComparison.Ordinal);
                    if (end < 0) { Think(s); return; }
                    Think(s[..end]);
                    inThink = false;
                    s = s[(end + 8)..].TrimStart('\n', '\r');
                    continue;
                }
                int start = s.IndexOf("<think>", StringComparison.Ordinal);
                string plain = start < 0 ? s : s[..start];
                if (plain.Length > 0)
                {
                    if (textBlock < 0) { Close(ref thinkBlock); textBlock = Open("text"); }
                    text.Append(plain);
                    Delta(textBlock, "text_delta", "text", plain);
                }
                if (start < 0) return;
                inThink = true;
                s = s[(start + 7)..];
            }
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            JsonNode? chunk;
            try { chunk = JsonNode.Parse(data); } catch { continue; }
            if (chunk?["usage"] is JsonObject usage)
            {
                prompt = usage["prompt_tokens"]?.GetValue<long>() ?? prompt;
                output = usage["completion_tokens"]?.GetValue<long>() ?? output;
            }
            if (chunk?["choices"] is not JsonArray choices || choices.Count == 0) continue;
            var delta = choices[0]?["delta"];
            if (delta == null) continue;
            string? reasoning = (delta["reasoning_content"] ?? delta["reasoning"])?.GetValue<string>();
            if (!string.IsNullOrEmpty(reasoning)) Think(reasoning);
            if (delta["content"] is JsonValue c && c.TryGetValue<string>(out var content) && content.Length > 0) Say(content);
            if (delta["tool_calls"] is JsonArray tc)
                foreach (var t in tc)
                {
                    if (t == null) continue;
                    int index = t["index"]?.GetValue<int>() ?? calls.Count;
                    var fn = t["function"];
                    if (!calls.TryGetValue(index, out var call))
                    {
                        Close(ref textBlock);
                        Close(ref thinkBlock);
                        string callId = t["id"]?.GetValue<string>() is { Length: > 0 } s ? s : "call_" + Guid.NewGuid().ToString("N")[..10];
                        string name = fn?["name"]?.GetValue<string>() ?? "";
                        int b = Open("tool_use", new JsonObject { ["id"] = callId, ["name"] = name });
                        call = (callId, name, new StringBuilder(), b);
                    }
                    if (fn?["name"]?.GetValue<string>() is { Length: > 0 } n && call.Name.Length == 0) call.Name = n;
                    if (fn?["arguments"]?.GetValue<string>() is { Length: > 0 } args)
                    {
                        call.Args.Append(args);
                        Delta(call.Block, "input_json_delta", "partial_json", args);
                    }
                    calls[index] = call;
                }
        }
        Close(ref textBlock);
        Close(ref thinkBlock);
        foreach (var c in calls.Values) Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_stop", ["index"] = c.Block } });
        if (output == 0) output = (text.Length + thinkChars + calls.Values.Sum(c => c.Args.Length)) / 4;
        Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "message_delta", ["usage"] = new JsonObject { ["input_tokens"] = prompt, ["output_tokens"] = output } } });

        // the whole message: its tool calls with their input (the tab's cards)
        var content2 = new JsonArray();
        if (text.Length > 0) content2.Add(new JsonObject { ["type"] = "text", ["text"] = text.ToString() });
        var list = new List<Call>();
        foreach (var c in calls.Values)
        {
            string args = c.Args.Length > 0 ? c.Args.ToString() : "{}";
            JsonNode? input;
            try { input = JsonNode.Parse(args); } catch { input = new JsonObject { ["_raw"] = args }; }
            content2.Add(new JsonObject { ["type"] = "tool_use", ["id"] = c.Id, ["name"] = c.Name, ["input"] = input });
            list.Add(new Call(c.Id, c.Name, args));
        }
        Emit(new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["id"] = id, ["model"] = model, ["content"] = content2 } });
        return new Reply(text.ToString(), list, prompt, output);
    }

    // ---------------------------------------------------------------- the tools

    private static JsonArray Tools()
    {
        JsonObject Fn(string name, string description, JsonObject props, params string[] required) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()) },
            },
        };
        JsonObject S(string d) => new() { ["type"] = "string", ["description"] = d };
        JsonObject I(string d) => new() { ["type"] = "integer", ["description"] = d };
        return new JsonArray(
            Fn("Read", "Read a text file. Returns its lines numbered. Use offset and limit for long files.", new JsonObject { ["file_path"] = S("The file (absolute, or relative to the working folder)"), ["offset"] = I("The first line to read (1-based)"), ["limit"] = I("How many lines (default 2000)") }, "file_path"),
            Fn("Write", "Create a file, or replace a file's whole content.", new JsonObject { ["file_path"] = S("The file"), ["content"] = S("Its whole new content") }, "file_path", "content"),
            Fn("Edit", "Replace an exact piece of text in a file. old_string must appear exactly once (include enough around it), unless replace_all is true.", new JsonObject { ["file_path"] = S("The file"), ["old_string"] = S("The exact text to replace"), ["new_string"] = S("What it becomes"), ["replace_all"] = new JsonObject { ["type"] = "boolean", ["description"] = "Replace every occurrence" } }, "file_path", "old_string", "new_string"),
            Fn("LS", "List a folder's files and subfolders.", new JsonObject { ["path"] = S("The folder (default: the working folder)") }),
            Fn("Glob", "Find files by name pattern, e.g. **/*.cs or src/*.json.", new JsonObject { ["pattern"] = S("The glob pattern"), ["path"] = S("The folder to search (default: the working folder)") }, "pattern"),
            Fn("Grep", "Search files' contents with a regular expression. Returns file:line: text.", new JsonObject { ["pattern"] = S("The regular expression"), ["path"] = S("The folder or file (default: the working folder)"), ["glob"] = S("Only files matching this, e.g. *.cs") }, "pattern"),
            Fn("PowerShell", "Run a PowerShell command in the working folder and return its output.", new JsonObject { ["command"] = S("The command"), ["description"] = S("What it does, in a few words"), ["timeout_seconds"] = I("How long it may take (default 120, at most 600)") }, "command"));
    }

    private async Task<(string Output, bool Failed)> RunToolAsync(Call call, CancellationToken ct)
    {
        JsonElement input;
        try { using var d = JsonDocument.Parse(call.Arguments.Length > 0 ? call.Arguments : "{}"); input = d.RootElement.Clone(); }
        catch (Exception ex) { return ($"The arguments weren't valid JSON ({ex.Message}). Send the tool call again with valid JSON.", true); }
        var kind = Palette.KindOf(call.Name);
        bool changes = kind is Palette.ToolKind.Change or Palette.ToolKind.Run;
        if (changes && mode == "plan") return ("Plan mode: don't change anything or run commands yet. Describe your plan instead.", true);
        bool ask = !skipPermissions() && mode != "bypassPermissions" && (kind == Palette.ToolKind.Run ? true : kind == Palette.ToolKind.Change && mode != "acceptEdits");
        if (ask)
        {
            string rid = "req_" + Interlocked.Increment(ref requestIds);
            var wait = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (asking) asking[rid] = wait;
            Emit(new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = rid,
                ["request"] = new JsonObject { ["subtype"] = "can_use_tool", ["tool_name"] = call.Name, ["tool_use_id"] = call.Id, ["input"] = JsonNode.Parse(input.GetRawText()) },
            });
            using (ct.Register(() => wait.TrySetCanceled()))
            {
                var (allow, reason) = await wait.Task;
                if (!allow) return (reason ?? "The user said no to this.", true);
            }
        }
        try { return await Task.Run(() => Tool(call.Name, input, ct), ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (ex.Message, true); }
    }

    private string PathOf(JsonElement input, string name, string fallback = "")
    {
        string p = input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : fallback;
        if (p.Length == 0) p = info.Folder;
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(info.Folder, p));
    }

    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string n, int dflt) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;

    private (string, bool) Tool(string name, JsonElement input, CancellationToken ct)
    {
        switch (name)
        {
            case "Read":
                {
                    string path = PathOf(input, "file_path");
                    if (!File.Exists(path)) return ($"No such file: {path}", true);
                    var lines = File.ReadAllLines(path);
                    int from = Math.Max(1, Int(input, "offset", 1)), count = Math.Clamp(Int(input, "limit", 2000), 1, 5000);
                    var sb = new StringBuilder();
                    for (int i = from - 1; i < Math.Min(lines.Length, from - 1 + count); i++) sb.Append(i + 1).Append('\t').AppendLine(lines[i].Length > 2000 ? lines[i][..2000] + "…" : lines[i]);
                    if (from - 1 + count < lines.Length) sb.AppendLine($"… {lines.Length - (from - 1 + count)} more lines (use offset)");
                    return (Cap(sb.ToString()), false);
                }
            case "Write":
                {
                    string path = PathOf(input, "file_path");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    bool existed = File.Exists(path);
                    File.WriteAllText(path, Str(input, "content"));
                    return (existed ? $"Replaced {path}." : $"Created {path}.", false);
                }
            case "Edit":
                {
                    string path = PathOf(input, "file_path");
                    if (!File.Exists(path)) return ($"No such file: {path}", true);
                    string text = File.ReadAllText(path), old = Str(input, "old_string"), neu = Str(input, "new_string");
                    if (old.Length == 0) return ("old_string is empty.", true);
                    // the file's line endings, if the model wrote \n
                    if (!text.Contains(old) && text.Contains("\r\n") && old.Contains('\n') && !old.Contains("\r\n")) { old = old.Replace("\n", "\r\n"); neu = neu.Replace("\n", "\r\n"); }
                    int count = Regex.Matches(text, Regex.Escape(old)).Count;
                    bool all = input.TryGetProperty("replace_all", out var ra) && ra.ValueKind == JsonValueKind.True;
                    if (count == 0) return ("old_string wasn't found in the file. Read the file again and copy the text exactly.", true);
                    if (count > 1 && !all) return ($"old_string appears {count} times: include more around it so it's unique, or set replace_all.", true);
                    File.WriteAllText(path, all ? text.Replace(old, neu) : ReplaceOnce(text, old, neu));
                    return ($"Edited {path}{(all ? $" ({count} places)" : "")}.", false);
                }
            case "LS":
                {
                    string path = PathOf(input, "path");
                    if (!Directory.Exists(path)) return ($"No such folder: {path}", true);
                    var sb = new StringBuilder();
                    foreach (var d in Directory.GetDirectories(path).OrderBy(x => x)) sb.AppendLine(Path.GetFileName(d) + "\\");
                    foreach (var f in Directory.GetFiles(path).OrderBy(x => x)) sb.AppendLine($"{Path.GetFileName(f)}  ({new FileInfo(f).Length:N0} bytes)");
                    return (Cap(sb.Length > 0 ? sb.ToString() : "(empty)"), false);
                }
            case "Glob":
                {
                    string root = PathOf(input, "path");
                    var regex = GlobRegex(Str(input, "pattern"));
                    var found = Files(root, ct).Where(f => regex.IsMatch(Path.GetRelativePath(root, f).Replace('\\', '/'))).Take(500).ToList();
                    return (found.Count == 0 ? "No files match." : Cap(string.Join("\n", found)), false);
                }
            case "Grep":
                {
                    string root = PathOf(input, "path");
                    var regex = new Regex(Str(input, "pattern"), RegexOptions.Compiled, TimeSpan.FromSeconds(2));
                    string glob = Str(input, "glob");
                    var only = glob.Length > 0 ? GlobRegex(glob.Contains('/') ? glob : "**/" + glob) : null;
                    var files = File.Exists(root) ? new[] { root } : Files(root, ct);
                    var sb = new StringBuilder();
                    int hits = 0;
                    foreach (var f in files)
                    {
                        if (only != null && !only.IsMatch(Path.GetRelativePath(File.Exists(root) ? Path.GetDirectoryName(root)! : root, f).Replace('\\', '/'))) continue;
                        var fi = new FileInfo(f);
                        if (fi.Length > 2_000_000) continue;
                        int n = 0;
                        foreach (var line in File.ReadLines(f))
                        {
                            n++;
                            if (line.Contains('\0')) break; // binary
                            if (!regex.IsMatch(line)) continue;
                            sb.Append(f).Append(':').Append(n).Append(": ").AppendLine(line.Length > 300 ? line[..300] + "…" : line);
                            if (++hits >= 300) break;
                        }
                        if (hits >= 300) { sb.AppendLine("… (more matches)"); break; }
                    }
                    return (hits == 0 ? "No matches." : Cap(sb.ToString()), false);
                }
            case "PowerShell":
            case "Bash":
                return RunCommand(Str(input, "command"), Math.Clamp(Int(input, "timeout_seconds", 120), 5, 600), ct);
        }
        return ($"There's no tool called {name}. The tools are Read, Write, Edit, LS, Glob, Grep and PowerShell.", true);
    }

    private (string, bool) RunCommand(string command, int timeout, CancellationToken ct)
    {
        var info2 = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = info.Folder, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command }) info2.ArgumentList.Add(a);
        using var p = Process.Start(info2)!;
        var output = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) if (output.Length < MaxOutput * 2) output.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) if (output.Length < MaxOutput * 2) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        while (!p.WaitForExit(200))
        {
            if (ct.IsCancellationRequested || sw.Elapsed.TotalSeconds > timeout)
            {
                try { p.Kill(true); } catch { }
                if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
                lock (output) return (Cap(output.ToString()) + $"\n(stopped after {timeout} s)", true);
            }
        }
        p.WaitForExit();
        lock (output)
        {
            string text = output.ToString().TrimEnd();
            return ((text.Length > 0 ? Cap(text) : "(no output)") + (p.ExitCode != 0 ? $"\n(exit code {p.ExitCode})" : ""), p.ExitCode != 0);
        }
    }

    private static string ReplaceOnce(string text, string old, string neu)
    {
        int i = text.IndexOf(old, StringComparison.Ordinal);
        return text[..i] + neu + text[(i + old.Length)..];
    }

    private static string Cap(string s) => s.Length > MaxOutput ? s[..MaxOutput] + $"\n… ({s.Length - MaxOutput:N0} more characters)" : s;

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__", "dist", "artifacts" };

    private static IEnumerable<string> Files(string root, CancellationToken ct)
    {
        var dirs = new Stack<string>();
        dirs.Push(root);
        while (dirs.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string d = dirs.Pop();
            string[] files, subs;
            try { files = Directory.GetFiles(d); subs = Directory.GetDirectories(d); } catch { continue; }
            foreach (var f in files) yield return f;
            foreach (var s in subs) if (!Skip.Contains(Path.GetFileName(s))) dirs.Push(s);
        }
    }

    /// <summary>A glob (**, *, ?) as a regular expression over paths with /.</summary>
    private static Regex GlobRegex(string glob)
    {
        var sb = new StringBuilder("^");
        glob = glob.Replace('\\', '/');
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                sb.Append(i + 2 < glob.Length && glob[i + 2] == '/' ? "(?:.*/)?" : ".*");
                i += i + 2 < glob.Length && glob[i + 2] == '/' ? 2 : 1;
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(sb.Append('$').ToString(), RegexOptions.IgnoreCase);
    }

    // ---------------------------------------------------------------- talking to the tab

    private void Emit(JsonObject message)
    {
        JsonElement e;
        using (var d = JsonDocument.Parse(message.ToJsonString())) e = d.RootElement.Clone();
        ui.BeginInvoke(() => { if (!disposed || Str(e, "type") == "result") Message?.Invoke(e); });
    }

    // ---------------------------------------------------------------- kept between runs

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "AI sessions");
    private static string FileOf(string id) => Path.Combine(Folder, id + ".json");

    private static List<JsonObject> Load(string id)
    {
        try
        {
            if (File.Exists(FileOf(id)) && JsonNode.Parse(File.ReadAllText(FileOf(id)))?["messages"] is JsonArray a)
                return a.OfType<JsonObject>().Select(m => (JsonObject)m.DeepClone()).ToList();
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"AI session {id} couldn't be read: {ex.Message}"); }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var doc = new JsonObject
            {
                ["provider"] = provider.Name,
                ["model"] = model,
                ["folder"] = info.Folder,
                ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
            };
            File.WriteAllText(FileOf(info.Id), doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"AI session {info.Id} couldn't be saved: {ex.Message}"); }
    }

    /// <summary>The conversations kept for a folder (any provider), newest first: id, its first message, when.</summary>
    public static List<ClaudeCli.Saved> SavedSessions(string folder, int max = 30)
    {
        var result = new List<ClaudeCli.Saved>();
        try
        {
            if (!Directory.Exists(Folder)) return result;
            string want = Path.GetFullPath(folder).TrimEnd('\\');
            foreach (var file in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).Take(300))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file.FullName));
                    var r = doc.RootElement;
                    if (!string.Equals(Path.GetFullPath(Str(r, "folder") is { Length: > 0 } f ? f : "?").TrimEnd('\\'), want, StringComparison.OrdinalIgnoreCase)) continue;
                    string? first = null;
                    if (r.TryGetProperty("messages", out var ms) && ms.ValueKind == JsonValueKind.Array)
                        foreach (var m in ms.EnumerateArray())
                        {
                            if (Str(m, "role") != "user") continue;
                            first = m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array
                                ? c.EnumerateArray().Select(b => Str(b, "text")).FirstOrDefault(t => t.Length > 0)
                                : Str(m, "content");
                            if (first is { Length: > 0 }) break;
                        }
                    string label = SessionView.WithoutReadFirst(first ?? "") is { Length: > 0 } l ? l : Path.GetFileNameWithoutExtension(file.Name);
                    if (Str(r, "provider") is { Length: > 0 } who) label = who + ": " + label;
                    if (label.Length > 80) label = label[..80] + "…";
                    result.Add(new ClaudeCli.Saved(Path.GetFileNameWithoutExtension(file.Name), label.Replace('\n', ' '), file.LastWriteTime));
                    if (result.Count >= max) break;
                }
                catch { }
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Listing AI sessions: " + ex.Message); }
        return result;
    }

    /// <summary>Deletes a kept conversation.</summary>
    public static bool Exists(string id) => File.Exists(FileOf(id));

    public static void Delete(string id)
    {
        if (File.Exists(FileOf(id))) File.Delete(FileOf(id));
        Logging.Log.Here.Info($"AI session {id} deleted");
    }

    /// <summary>A saved conversation as Claude Code's transcript lines (user / assistant messages), for the tab's history.</summary>
    public static List<JsonElement> Transcript(string id)
    {
        var list = new List<JsonElement>();
        foreach (var m in Load(id))
        {
            string role = m["role"]?.GetValue<string>() ?? "";
            JsonObject? line = role switch
            {
                "user" => new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = m["content"]?.DeepClone() } },
                "tool" => new JsonObject
                {
                    ["type"] = "user",
                    ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = m["tool_call_id"]?.DeepClone(), ["content"] = m["content"]?.DeepClone() }) },
                },
                "assistant" => Assistant(m),
                _ => null,
            };
            if (line == null) continue;
            using var d = JsonDocument.Parse(line.ToJsonString());
            list.Add(d.RootElement.Clone());
        }
        return list;

        static JsonObject Assistant(JsonObject m)
        {
            var content = new JsonArray();
            if (m["content"]?.GetValue<string>() is { Length: > 0 } t) content.Add(new JsonObject { ["type"] = "text", ["text"] = t });
            if (m["tool_calls"] is JsonArray calls)
                foreach (var c in calls)
                {
                    string args = c?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                    JsonNode? input;
                    try { input = JsonNode.Parse(args); } catch { input = new JsonObject(); }
                    content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = c?["id"]?.DeepClone(), ["name"] = c?["function"]?["name"]?.DeepClone(), ["input"] = input });
                }
            return new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } };
        }
    }

    /// <summary>The models a provider offers (its /models).</summary>
    public static async Task<List<string>> ModelsAsync(Provider p, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl.TrimEnd('/') + "/models");
        if (p.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new("Bearer", key);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var r = await Http.SendAsync(request, cts.Token);
        string body = await r.Content.ReadAsStringAsync(cts.Token);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)r.StatusCode}: {Short(body)}");
        var list = new List<string>();
        if (JsonNode.Parse(body)?["data"] is JsonArray data)
            foreach (var m in data) if (m?["id"]?.GetValue<string>() is { } id) list.Add(id.StartsWith("models/") ? id[7..] : id);
        return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
