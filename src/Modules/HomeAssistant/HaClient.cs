using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>Something Home Assistant said no to, or a connection that didn't work, in words for the user.</summary>
public sealed class HaException(string message) : Exception(message);

/// <summary>
/// One connection to Home Assistant's WebSocket API (/api/websocket): signs in with a long-lived access token, then
/// sends commands (each with its id, answered by a "result") and hands on the events of its subscriptions. Its reading
/// runs on its own task; <see cref="Event"/> and <see cref="Closed"/> are raised there.
/// </summary>
public sealed class HaClient : IDisposable
{
    private readonly ClientWebSocket ws = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly CancellationTokenSource stop = new();
    private int nextId;
    private int closed;

    /// <summary>Home Assistant's version, from its greeting.</summary>
    public string Version { get; private set; } = "";

    /// <summary>A subscription's event (its id, and the message's "event"), on the reading task.</summary>
    public event Action<int, JsonElement>? Event;

    /// <summary>The connection ended (the reason), on the reading task. Raised once.</summary>
    public event Action<string>? Closed;

    private HaClient() { }

    /// <summary>Connects and signs in; throws <see cref="HaException"/> with what went wrong.</summary>
    public static async Task<HaClient> ConnectAsync(HaSettings settings, CancellationToken cancel = default)
    {
        var uri = settings.WebSocketUri() ?? throw new HaException(T("That isn't an address (e.g. http://homeassistant.local:8123)."));
        string token = settings.Token;
        if (token.Length == 0) throw new HaException(T("Fill in the access token."));
        var c = new HaClient();
        if (settings.IgnoreCertificate) c.ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        c.ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            try { await c.ws.ConnectAsync(uri, timeout.Token); }
            catch (WebSocketException ex) { throw new HaException(F("Couldn't connect to {0}: {1}", uri.Authority, (ex.InnerException ?? ex).Message)); }

            var hello = await c.ReadAsync(timeout.Token);
            if (Str(hello, "type") != "auth_required") throw new HaException(F("{0} doesn't look like Home Assistant.", uri.Authority));
            await c.WriteAsync(new Dictionary<string, object?> { ["type"] = "auth", ["access_token"] = token }, timeout.Token);
            var auth = await c.ReadAsync(timeout.Token);
            switch (Str(auth, "type"))
            {
                case "auth_ok": c.Version = Str(auth, "ha_version") ?? ""; break;
                case "auth_invalid": throw new HaException(T("Home Assistant refused the access token."));
                default: throw new HaException(T("Home Assistant didn't sign in."));
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { c.Dispose(); throw new HaException(F("No answer from {0}.", uri.Authority)); }
        catch { c.Dispose(); throw; }
        _ = Task.Run(c.ReadLoopAsync);
        return c;
    }

    /// <summary>Sends a command ({"type": …} and its fields) and waits for its result; throws <see cref="HaException"/> when it failed.</summary>
    public async Task<JsonElement> SendAsync(Dictionary<string, object?> command, TimeSpan? wait = null)
    {
        int id = Interlocked.Increment(ref nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        command["id"] = id;
        try
        {
            await WriteAsync(command, stop.Token);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(wait ?? TimeSpan.FromSeconds(20)));
            if (done != tcs.Task) throw new HaException(F("Home Assistant didn't answer ({0}).", command["type"]));
            return await tcs.Task;
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            throw new HaException(T("Not connected to Home Assistant."));
        }
        finally { pending.TryRemove(id, out _); }
    }

    public Task<JsonElement> CommandAsync(string type, params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?> { ["type"] = type };
        foreach (var (k, v) in fields) d[k] = v;
        return SendAsync(d);
    }

    /// <summary>Subscribes to an event type; returns the subscription's id (its events carry it).</summary>
    public async Task<int> SubscribeAsync(string eventType)
    {
        var d = new Dictionary<string, object?> { ["type"] = "subscribe_events", ["event_type"] = eventType };
        var task = SendAsync(d);
        int id = (int)d["id"]!;
        await task;
        return id;
    }

    public Task CallServiceAsync(string domain, string service, string entityId, Dictionary<string, object?>? data = null) =>
        SendAsync(new Dictionary<string, object?>
        {
            ["type"] = "call_service", ["domain"] = domain, ["service"] = service,
            ["service_data"] = data ?? new Dictionary<string, object?>(),
            ["target"] = new Dictionary<string, object?> { ["entity_id"] = entityId },
        });

    private async Task WriteAsync(Dictionary<string, object?> message, CancellationToken cancel)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sending.WaitAsync(cancel);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancel); }
        finally { sending.Release(); }
    }

    /// <summary>One whole message (it may come in several frames: the states of a big house are megabytes).</summary>
    private async Task<JsonElement> ReadAsync(CancellationToken cancel)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buffer, cancel);
            if (r.MessageType == WebSocketMessageType.Close) throw new HaException(ws.CloseStatusDescription is { Length: > 0 } d ? d : T("Home Assistant closed the connection."));
            ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) break;
        }
        using var doc = JsonDocument.Parse(ms.GetBuffer().AsMemory(0, (int)ms.Length));
        return doc.RootElement.Clone();
    }

    private async Task ReadLoopAsync()
    {
        string reason = T("The connection ended.");
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var msg = await ReadAsync(stop.Token);
                // several at once, when the server coalesces them
                if (msg.ValueKind == JsonValueKind.Array) foreach (var m in msg.EnumerateArray()) Handle(m);
                else Handle(msg);
            }
        }
        catch (HaException ex) { reason = ex.Message; }
        catch (OperationCanceledException) { reason = T("Disconnected."); }
        catch (Exception ex) { reason = ex.Message; }
        Close(reason);
    }

    private void Handle(JsonElement m)
    {
        int id = m.TryGetProperty("id", out var i) && i.TryGetInt32(out int n) ? n : 0;
        switch (Str(m, "type"))
        {
            case "result":
                if (!pending.TryGetValue(id, out var tcs)) return;
                if (m.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True)
                    tcs.TrySetResult(m.TryGetProperty("result", out var res) ? res : default);
                else
                {
                    string why = m.TryGetProperty("error", out var e) ? Str(e, "message") ?? Str(e, "code") ?? "" : "";
                    tcs.TrySetException(new HaException(why.Length > 0 ? why : T("Home Assistant refused it.")));
                }
                break;
            case "pong":
                if (pending.TryGetValue(id, out var p)) p.TrySetResult(default);
                break;
            case "event":
                if (m.TryGetProperty("event", out var ev))
                    try { Event?.Invoke(id, ev); }
                    catch (Exception ex) { Log.Here.Error("Home Assistant event", ex); }
                break;
        }
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        foreach (var p in pending.Values) p.TrySetException(new HaException(T("Not connected to Home Assistant.")));
        Closed?.Invoke(reason);
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public void Dispose()
    {
        stop.Cancel();
        try { if (ws.State == WebSocketState.Open) ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(500); }
        catch { }
        ws.Dispose();
        Close(T("Disconnected."));
    }
}
