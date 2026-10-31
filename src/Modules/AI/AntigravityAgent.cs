using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>
/// A session on Google's Antigravity CLI (agy.exe), signed in with your Google account: it runs on your Google AI plan
/// (Pro, Ultra), not on API credits. agy is a coding agent of its own (it reads, edits and runs things itself), driven as
/// Claude Code is: "agy -p" with stream-json in and out, one process for the session, a line on its stdin per message.
/// What it prints (init, step_update for each response and tool, result) is turned into Claude Code's stream-json
/// (<see cref="IAgent"/>), so the tab draws it as it draws Claude Code: its tools under Claude Code's names (Read,
/// Write, Edit, PowerShell…) so the cards and the files changed work.
/// <list type="bullet">
/// <item>It can't ask before a tool here (headless, it refuses what needs asking: commands): Permissions' Don't ask
/// starts it with --dangerously-skip-permissions, Plan with --mode plan; a change of either, or of the model, restarts
/// it on the same conversation (--conversation) before the next message.</item>
/// <item>Stop ends the process; the next message starts it again on the same conversation.</item>
/// <item>The conversation is kept as ApiAgent's are (AI sessions\&lt;id&gt;.json, with agy's conversation id), so the
/// tab's history, Saved sessions and search work the same.</item>
/// </list>
/// </summary>
internal sealed class AntigravityAgent : IAgent
{
    private readonly Provider provider;
    private readonly SessionInfo info;
    private readonly Dispatcher ui;
    private readonly Func<bool> skipPermissions;
    private readonly string exe;
    private readonly List<JsonObject> messages;
    private readonly StringBuilder errors = new();
    private Process? process;
    private string? conversation;
    private string model, mode;
    private bool restart, disposed, turnOpen, turnSaid;
    private DateTime turnStarted;

    // the step being drawn (agy's step_index): a response's streamed text, a tool's card
    private int stepIndex = -1;
    private string? stepId;
    private readonly StringBuilder stepText = new();
    private bool textOpen;
    private readonly HashSet<string> toolsShown = new();

    public event Action<JsonElement>? Message;
    // (it doesn't end on its own: a process that ends is started again at the next message)
    public event Action<string>? Exited { add { } remove { } }
    public bool Running => !disposed;
    public int? ContextWindow => provider.ContextWindow > 0 ? provider.ContextWindow : null;

    private AntigravityAgent(string exe, Provider provider, SessionInfo info, Dispatcher ui, Func<bool> skipPermissions)
    {
        this.exe = exe;
        this.provider = provider;
        this.info = info;
        this.ui = ui;
        this.skipPermissions = skipPermissions;
        model = info.Model.Length > 0 ? info.Model : provider.Model;
        mode = info.Mode;
        (messages, conversation) = Load(info.Id);
    }

    /// <summary>agy.exe: where its installer puts it (%LOCALAPPDATA%\agy\bin), or on the PATH.</summary>
    public static string? FindExe()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { string p = Path.Combine(dir.Trim(), "agy.exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>The PowerShell line that installs it (Google's installer).</summary>
    public const string InstallCommand = "irm https://antigravity.google/cli/install.ps1 | iex";

    public static AntigravityAgent Start(string exe, Provider provider, SessionInfo info, Dispatcher ui, Func<bool> skipPermissions)
    {
        var a = new AntigravityAgent(exe, provider, info, ui, skipPermissions);
        Logging.Log.Here.Info($"AI session {info.Id} on Antigravity ({(a.model.Length > 0 ? a.model : "its default model")}) in {info.Folder}{(a.conversation != null ? ", conversation " + a.conversation : "")}");
        a.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["model"] = a.model, ["session_id"] = info.Id, ["cwd"] = info.Folder });
        return a;
    }

    // ---------------------------------------------------------------- what the tab says

    public void Send(string text, IReadOnlyList<ImageData>? images = null)
    {
        if (disposed) return;
        if (text.StartsWith("/rename ", StringComparison.OrdinalIgnoreCase))
        {
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "session_title_changed", ["title"] = text[8..].Trim() });
            Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["num_turns"] = 0, ["is_error"] = false });
            return;
        }
        if (text.Trim().Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            // (a new conversation of agy's from the next message)
            Stop();
            conversation = null;
            messages.Clear();
            Save();
            Emit(new JsonObject { ["type"] = "system", ["subtype"] = "compact_boundary" });
            Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["num_turns"] = 0, ["is_error"] = false });
            return;
        }
        // images: as files it can look at (view_file reads images)
        if (images is { Count: > 0 })
        {
            var sb = new StringBuilder(text);
            string dir = Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-images");
            Directory.CreateDirectory(dir);
            foreach (var i in images)
            {
                string ext = i.MediaType.Contains("png") ? ".png" : i.MediaType.Contains("gif") ? ".gif" : i.MediaType.Contains("webp") ? ".webp" : ".jpg";
                string path = Path.Combine(dir, Guid.NewGuid().ToString("N")[..12] + ext);
                try { File.WriteAllBytes(path, Convert.FromBase64String(i.Data)); sb.Append("\n\n[Image attached: ").Append(path).Append(']'); }
                catch (Exception ex) { Logging.Log.Here.Warn("Antigravity: an image couldn't be saved: " + ex.Message); }
            }
            text = sb.ToString();
        }
        if (restart && !turnOpen) Stop();
        restart = false;
        if (process == null && !Launch()) return;
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        Save();
        if (!turnOpen) { turnOpen = true; turnSaid = false; turnStarted = DateTime.UtcNow; }
        Write(new { @event = "user", message = new { role = "user", content = text } });
    }

    public void Control(string subtype, Dictionary<string, object?>? fields)
    {
        switch (subtype)
        {
            case "interrupt": Interrupt(); break;
            case "set_model":
                model = fields?.GetValueOrDefault("model") as string is { Length: > 0 } m ? m : provider.Model;
                restart = true;
                break;
            case "set_permission_mode":
                mode = fields?.GetValueOrDefault("mode") as string ?? mode;
                restart = true;
                break;
        }
    }

    /// <summary>Stop: agy has no way to be told mid-turn, so its process ends; the next message starts it again on the
    /// same conversation.</summary>
    public void Interrupt()
    {
        if (!turnOpen) return;
        Stop();
        EndStep();
        turnOpen = turnSaid = false;
        Save();
        Emit(new JsonObject { ["type"] = "result", ["subtype"] = "interrupted", ["is_error"] = false, ["num_turns"] = 1, ["result"] = T("Stopped.") });
    }

    // (it can't ask: see the class's notes)
    public void Answer(string requestId, bool allow, JsonElement input, string? reason = null) { }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        Save();
    }

    // ---------------------------------------------------------------- the process

    private bool Launch()
    {
        var args = new List<string> { "--input-format", "stream-json", "--output-format", "stream-json" };
        if (conversation != null) args.AddRange(new[] { "--conversation", conversation });
        if (model.Length > 0) args.AddRange(new[] { "--model", model });
        if (skipPermissions() || mode == "bypassPermissions") args.Add("--dangerously-skip-permissions");
        else if (mode == "plan") args.AddRange(new[] { "--mode", "plan" });
        else if (mode == "acceptEdits") args.AddRange(new[] { "--mode", "accept-edits" });
        else if (mode == "auto") args.AddRange(new[] { "--mode", "auto" });
        // (-p last, with an empty prompt: the messages come on stdin)
        args.AddRange(new[] { "-p", "" });
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = info.Folder,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) start.ArgumentList.Add(a);
        var p = new Process { StartInfo = start };
        try { p.Start(); }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Starting Antigravity", ex);
            Emit(new JsonObject { ["type"] = "result", ["subtype"] = "error_during_execution", ["is_error"] = true, ["num_turns"] = 0, ["result"] = F("Antigravity couldn't start: {0}", ex.Message) });
            return false;
        }
        ClaudeProcess.Job.Add(p);
        process = p;
        lock (errors) errors.Clear();
        Logging.Log.Here.Info($"Antigravity process {p.Id} started for session {info.Id}: {string.Join(' ', args.Where(a => a.Length > 0))}");
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 8000) errors.Remove(0, errors.Length - 8000); } };
        p.BeginErrorReadLine();
        new Thread(() => Read(p)) { IsBackground = true, Name = "Antigravity reader" }.Start();
        return true;
    }

    private void Stop()
    {
        var p = process;
        process = null;
        if (p == null) return;
        try { p.StandardInput.Close(); } catch { }
        try { if (!p.WaitForExit(300)) p.Kill(true); } catch { }
        p.Dispose();
    }

    private void Read(Process p)
    {
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonElement e;
                try { using var doc = JsonDocument.Parse(line); e = doc.RootElement.Clone(); }
                catch { continue; }
                ui.BeginInvoke(() => { if (!disposed && process == p) Handle(e); });
            }
        }
        catch { }
        ui.BeginInvoke(() => Ended(p));
    }

    /// <summary>Its process ended by itself (not stopped here): a turn it was in ends with what it said.</summary>
    private void Ended(Process p)
    {
        if (disposed || process != p) return;
        process = null;
        string tail;
        lock (errors) tail = errors.ToString().Trim();
        Logging.Log.Here.Info($"Antigravity process for session {info.Id} ended{(tail.Length > 0 ? ": " + tail : "")}");
        try { p.Dispose(); } catch { }
        if (!turnOpen) return;
        EndStep();
        turnOpen = false;
        Save();
        string why = tail.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? T("it closed");
        bool login = tail.Contains("auth", StringComparison.OrdinalIgnoreCase) || tail.Contains("sign in", StringComparison.OrdinalIgnoreCase) || tail.Contains("login", StringComparison.OrdinalIgnoreCase);
        Emit(new JsonObject
        {
            ["type"] = "result", ["subtype"] = "error_during_execution", ["is_error"] = true, ["num_turns"] = 0,
            ["result"] = login ? F("Antigravity isn't signed in: run agy in a terminal once and sign in with your Google account. ({0})", why) : F("Antigravity stopped: {0}", why),
        });
    }

    private void Write(object message)
    {
        if (process is not { } p) return;
        try
        {
            p.StandardInput.WriteLine(JsonSerializer.Serialize(message));
            p.StandardInput.Flush();
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Antigravity input: " + ex.Message); }
    }

    // ---------------------------------------------------------------- what it says, as Claude Code's stream-json

    private void Handle(JsonElement e)
    {
        switch (Str(e, "event"))
        {
            case "init":
                if (Str(e, "conversation_id") is { Length: > 0 } c && c != conversation) { conversation = c; Save(); }
                break;
            case "step_update" when e.TryGetProperty("step_update", out var s):
                Step(s);
                break;
            case "result" when e.TryGetProperty("result", out var r):
                Result(r);
                break;
        }
    }

    private void Step(JsonElement s)
    {
        int index = s.TryGetProperty("step_index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : -1;
        string state = Str(s, "state") ?? "", type = Str(s, "step_type") ?? "";
        bool done = state is "DONE" or "ERROR" or "CANCELED" or "CANCELLED";
        if (!turnOpen) { turnOpen = true; turnStarted = DateTime.UtcNow; }
        if (index != stepIndex) { EndStep(); stepIndex = index; }
        string id = $"agy_{(conversation ?? info.Id)[..8]}_{index}";
        switch (type)
        {
            case "agent_response":
                {
                    if (stepId == null)
                    {
                        stepId = id;
                        Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "message_start", ["message"] = new JsonObject { ["id"] = id, ["model"] = model } } });
                    }
                    if (Str(s, "text_delta") is { Length: > 0 } delta)
                    {
                        if (!textOpen)
                        {
                            textOpen = true;
                            Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_start", ["index"] = 0, ["content_block"] = new JsonObject { ["type"] = "text" } } });
                        }
                        stepText.Append(delta);
                        if (delta.Trim().Length > 0) turnSaid = true;
                        Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_delta", ["index"] = 0, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = delta } } });
                    }
                    if (done)
                    {
                        var u = s.TryGetProperty("usage", out var uu) ? uu : default;
                        Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "message_delta", ["usage"] = new JsonObject { ["input_tokens"] = Num(u, "input_tokens"), ["output_tokens"] = Num(u, "output_tokens") } } });
                        EndStep();
                    }
                    break;
                }
            case "tool":
                {
                    var tool = s.TryGetProperty("tool_info", out var ti) ? ti : default;
                    string name = Str(s, "tool_name") ?? Str(tool, "name") ?? "tool";
                    if (toolsShown.Add(id))
                    {
                        var (shown, input) = MapTool(name, tool.ValueKind == JsonValueKind.Object && tool.TryGetProperty("parameters", out var ps) ? ps : default);
                        Emit(new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["id"] = id + "_m", ["model"] = model, ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = shown, ["input"] = input }) } });
                        messages.Add(new JsonObject
                        {
                            ["role"] = "assistant", ["content"] = "",
                            ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = shown, ["arguments"] = input.ToJsonString() } }),
                        });
                    }
                    if (done)
                    {
                        bool failed = state != "DONE";
                        string output = Str(tool, "output")
                            ?? (tool.ValueKind == JsonValueKind.Object && tool.TryGetProperty("error", out var err) ? Str(err, "message") ?? err.ToString() : null)
                            ?? (failed ? T("It didn't work.") : "");
                        Emit(new JsonObject
                        {
                            ["type"] = "user",
                            ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = output, ["is_error"] = failed }) },
                        });
                        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = output });
                        stepIndex = -1;
                    }
                    break;
                }
        }
    }

    /// <summary>The response step drawn so far, finished: its text closed and the whole message sent (and kept).</summary>
    private void EndStep()
    {
        if (stepId != null)
        {
            if (textOpen) Emit(new JsonObject { ["type"] = "stream_event", ["event"] = new JsonObject { ["type"] = "content_block_stop", ["index"] = 0 } });
            var content = new JsonArray();
            if (stepText.Length > 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = stepText.ToString() });
            Emit(new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["id"] = stepId, ["model"] = model, ["content"] = content } });
            if (stepText.Length > 0) messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = stepText.ToString() });
        }
        stepId = null;
        textOpen = false;
        stepText.Clear();
        stepIndex = -1;
    }

    private void Result(JsonElement r)
    {
        EndStep();
        // (an answer it gave only in the result, none of it streamed)
        if (!turnSaid && Str(r, "response") is { } said && said.Trim().Length > 0)
        {
            string id = $"agy_{(conversation ?? info.Id)[..8]}_r{Guid.NewGuid():N}"[..40];
            Emit(new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["id"] = id, ["model"] = model, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = said }) } });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = said });
        }
        turnSaid = false;
        turnOpen = false;
        if (Str(r, "conversation_id") is { Length: > 0 } c) conversation = c;
        Save();
        bool ok = Str(r, "status") == "SUCCESS";
        if (r.TryGetProperty("denied_actions", out var denied) && denied.ValueKind == JsonValueKind.Array && denied.GetArrayLength() > 0)
        {
            var names = denied.EnumerateArray().Select(d => Str(d, "display_name") ?? Str(d, "action") ?? "?").Distinct();
            Emit(new JsonObject { ["type"] = "daisy_note", ["error"] = true, ["text"] = F("Antigravity can't ask first here, so it was refused: {0}. Permissions → Don't ask lets it.", string.Join(", ", names)) });
        }
        var u = r.TryGetProperty("usage", out var uu) ? uu : default;
        var result = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = ok ? "success" : "error_during_execution",
            ["is_error"] = !ok,
            ["num_turns"] = Num(r, "num_turns"),
            ["duration_ms"] = r.TryGetProperty("duration_seconds", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() * 1000 : (DateTime.UtcNow - turnStarted).TotalMilliseconds,
            ["total_cost_usd"] = 0,
            ["usage"] = new JsonObject { ["input_tokens"] = Num(u, "input_tokens"), ["output_tokens"] = Num(u, "output_tokens") },
        };
        if (!ok) result["result"] = F("Antigravity: {0}", Str(r, "error") is { Length: > 0 } why ? why : Str(r, "status") ?? "?");
        Emit(result);
    }

    /// <summary>agy's tool, under Claude Code's name for it (the card's icon, colour and summary, and the files changed),
    /// with its parameters as they are plus the ones the card reads (file_path, path, command, pattern, query, url).</summary>
    private static (string Name, JsonObject Input) MapTool(string name, JsonElement parameters)
    {
        string shown = name switch
        {
            "read_file" or "view_file" or "view_file_outline" or "view_code_item" => "Read",
            "write_file" or "write_to_file" => "Write",
            "replace_file_content" or "multi_replace_file_content" or "sed_file" or "edit_file" => "Edit",
            "notebook_edit" => "NotebookEdit",
            "run_command" or "send_command_input" => "PowerShell",
            "list_dir" => "LS",
            "grep_search" => "Grep",
            "find_by_name" => "Glob",
            "search_web" => "WebSearch",
            "read_url_content" or "open_browser_url" or "read_browser_page" => "WebFetch",
            "invoke_subagent" or "browser_subagent" => "Agent",
            _ => name,
        };
        var input = new JsonObject();
        if (parameters.ValueKind != JsonValueKind.Object) return (shown, input);
        string? path = null, command = null, pattern = null, url = null;
        foreach (var p in parameters.EnumerateObject())
        {
            input[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            if (p.Value.ValueKind != JsonValueKind.String) continue;
            string v = p.Value.GetString() ?? "", k = p.Name.ToLowerInvariant();
            if (path == null && (k.Contains("path") || k.Contains("file") || k.Contains("directory"))) path = v;
            else if (command == null && k.Contains("command")) command = v;
            else if (pattern == null && (k.Contains("query") || k.Contains("pattern"))) pattern = v;
            else if (url == null && k.Contains("url")) url = v;
        }
        if (path != null) input[shown is "LS" or "Grep" or "Glob" ? "path" : "file_path"] = path;
        if (command != null) input["command"] = command;
        if (pattern != null) input[shown == "WebSearch" ? "query" : "pattern"] = pattern;
        if (url != null) input["url"] = url;
        return (shown, input);
    }

    // ---------------------------------------------------------------- talking to the tab

    private void Emit(JsonObject message)
    {
        JsonElement e;
        using (var d = JsonDocument.Parse(message.ToJsonString())) e = d.RootElement.Clone();
        if (ui.CheckAccess()) { if (!disposed || Str(e, "type") == "result") Message?.Invoke(e); }
        else ui.BeginInvoke(() => { if (!disposed || Str(e, "type") == "result") Message?.Invoke(e); });
    }

    private static string? Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Num(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    // ---------------------------------------------------------------- kept between runs (as ApiAgent's)

    private static string FileOf(string id) => Path.Combine(ApiAgent.Folder, id + ".json");

    private static (List<JsonObject>, string?) Load(string id)
    {
        try
        {
            if (File.Exists(FileOf(id)) && JsonNode.Parse(File.ReadAllText(FileOf(id))) is JsonObject doc)
                return ((doc["messages"] as JsonArray)?.OfType<JsonObject>().Select(m => (JsonObject)m.DeepClone()).ToList() ?? new(),
                    doc["conversation"]?.GetValue<string>());
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"AI session {id} couldn't be read: {ex.Message}"); }
        return (new(), null);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(ApiAgent.Folder);
            var doc = new JsonObject
            {
                ["provider"] = provider.Name,
                ["model"] = model,
                ["folder"] = info.Folder,
                ["conversation"] = conversation,
                ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
            };
            File.WriteAllText(FileOf(info.Id), doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"AI session {info.Id} couldn't be saved: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- for the Providers window

    /// <summary>Its models (agy models: "id\tname" lines).</summary>
    public static async Task<List<string>> ModelsAsync(string exe) =>
        (await RunAsync(exe, TimeSpan.FromSeconds(60), "models")).Split('\n')
            .Select(l => l.Split('\t')[0].Trim()).Where(l => l.Length > 0 && !l.Contains(' ') && !l.EndsWith("...")).ToList();

    /// <summary>A one-word reply, to see it's signed in and answering.</summary>
    public static async Task<string> TestAsync(string exe, string model)
    {
        var args = new List<string> { "--output-format", "json" };
        if (model.Length > 0) args.AddRange(new[] { "--model", model });
        args.AddRange(new[] { "-p", "Reply with just the word OK." });
        string output = await RunAsync(exe, TimeSpan.FromSeconds(120), args.ToArray());
        string? answer = null, error = null;
        foreach (var line in output.Split('\n').Where(l => l.TrimStart().StartsWith('{')))
        {
            try
            {
                var n = JsonNode.Parse(line);
                var r = n?["result"] ?? n;
                answer ??= r?["response"]?.GetValue<string>();
                error ??= r?["error"]?.GetValue<string>();
            }
            catch { }
        }
        if (answer is not { Length: > 0 }) throw new InvalidOperationException(error ?? (output.Trim().Length > 0 ? output.Trim().Split('\n').Last() : T("no answer")));
        answer = answer.Trim();
        return answer.Length > 80 ? answer[..80] + "…" : answer;
    }

    private static async Task<string> RunAsync(string exe, TimeSpan timeout, params string[] args)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new TimeoutException(T("It didn't answer in time.")); }
        string text = await output;
        if (p.ExitCode != 0 && text.Trim().Length == 0) throw new InvalidOperationException((await error).Trim().Split('\n').LastOrDefault() ?? $"exit code {p.ExitCode}");
        return text;
    }
}
