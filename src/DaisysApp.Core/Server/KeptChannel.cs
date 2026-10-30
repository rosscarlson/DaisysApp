using System.Text.Json.Nodes;
using DaisysServer.Protocol;

namespace DaisysApp.Server;

/// <summary>
/// A module's channel to its counterpart on Daisy's Server, kept open: opened once the server is set up (Settings →
/// General), and again 5 seconds after it's lost (or at once when it's set up again). Its events come on a background
/// thread, in order: <see cref="Opened"/> (send the module's first message there), each <see cref="Message"/>, then
/// <see cref="Lost"/>. <see cref="ServerLink.Keep"/> makes one; Dispose closes it.
/// </summary>
public sealed class KeptChannel : IDisposable
{
    private readonly string module, from;
    private readonly CancellationTokenSource stop = new();
    /// <summary>Ends when the server's set up again, forgotten, connected or lost (ServerLink.Changed).</summary>
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile ServerChannel? channel;

    /// <summary>Opened (the user's key is <see cref="ServerChannel.DataKey"/>).</summary>
    public event Action<ServerChannel>? Opened;
    /// <summary>A message from the server's module.</summary>
    public event Action<ServerChannel, JsonObject>? Message;
    /// <summary>Closed, or couldn't be opened: why, worded for the user.</summary>
    public event Action<string>? Lost;

    /// <summary>Open now.</summary>
    public bool IsOpen => channel != null;

    internal KeptChannel(string module, string from)
    {
        this.module = module;
        this.from = from;
    }

    internal void Start()
    {
        ServerLink.Changed += Wake;
        _ = Task.Run(RunAsync);
    }

    private void Wake() => Interlocked.Exchange(ref changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private async Task RunAsync()
    {
        var ct = stop.Token;
        while (!ct.IsCancellationRequested)
        {
            var next = changed.Task;
            if (!ServerLink.Configured)
            {
                await Task.WhenAny(next, Task.Delay(Timeout.Infinite, ct));
                continue;
            }
            string why;
            try
            {
                var c = await ServerLink.OpenAsync(module, from, ct);
                channel = c;
                Opened?.Invoke(c);
                while (await c.ReadAsync(ct) is { } m) Message?.Invoke(c, m);
                why = (c.Ended.Length > 0 ? c.Ended : T("closed")).TrimEnd('.');
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { why = ex.Message.TrimEnd('.'); }
            channel?.Dispose();
            channel = null;
            if (ct.IsCancellationRequested) return;
            Logging.Log.App.Info($"Daisy's Server: {module} channel: {why}");
            Lost?.Invoke(why);
            next = changed.Task;
            await Task.WhenAny(next, Task.Delay(5000, ct));
        }
    }

    /// <summary>Sends to the server's module (dropped while it isn't open).</summary>
    public void Post(JsonNode message) => channel?.Post(message);

    public void Dispose()
    {
        ServerLink.Changed -= Wake;
        stop.Cancel();
        channel?.Dispose();
    }
}
