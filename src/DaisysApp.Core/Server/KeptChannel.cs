using System.Text.Json.Nodes;
using DaisysServer.Protocol;

namespace DaisysApp.Server;

/// <summary>
/// A module's channel to its counterpart on Daisy's Server, kept open: opened once a user signs in there (Settings →
/// General; a module that allows anonymous ones: once there's a server), and again 5 seconds after it's lost, doubled
/// each time it's lost again up to 5 minutes (since 4.17.28; back to 5 s once one stayed open a minute), or at once
/// when it's set up again. Its events come on a background
/// thread, in order: <see cref="Opened"/> (send the module's first message there), each <see cref="Message"/>, then
/// <see cref="Lost"/>. <see cref="ServerLink.Keep"/> makes one; Dispose closes it.
/// </summary>
public sealed class KeptChannel : IDisposable
{
    private readonly string module, from;
    private readonly bool anonymous;
    private readonly CancellationTokenSource stop = new();
    /// <summary>Ends when the server's set up again, forgotten or connected (ServerLink.Retry).</summary>
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

    internal KeptChannel(string module, string from, bool anonymous)
    {
        this.module = module;
        this.from = from;
        this.anonymous = anonymous;
    }

    internal void Start()
    {
        ServerLink.Retry += Wake;
        _ = Task.Run(RunAsync);
    }

    private void Wake() => Interlocked.Exchange(ref changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private async Task RunAsync()
    {
        var ct = stop.Token;
        int wait = 5;
        while (!ct.IsCancellationRequested)
        {
            var next = changed.Task;
            if (!(anonymous ? ServerLink.Configured : ServerLink.SignedIn))
            {
                await Task.WhenAny(next, Task.Delay(Timeout.Infinite, ct));
                continue;
            }
            string why;
            long opened = long.MaxValue;
            try
            {
                var c = await ServerLink.OpenAsync(module, from, ct);
                opened = Environment.TickCount64;
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
            if (Environment.TickCount64 - opened > 60_000) wait = 5;
            Logging.Log.App.Info($"Daisy's Server: {module} channel: {why} (again in {wait} s at most)");
            Lost?.Invoke(why);
            next = changed.Task;
            await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(wait), ct));
            wait = Math.Min(wait * 2, 300);
        }
    }

    /// <summary>Sends to the server's module (dropped while it isn't open).</summary>
    public void Post(JsonNode message) => channel?.Post(message);

    public void Dispose()
    {
        ServerLink.Retry -= Wake;
        stop.Cancel();
        channel?.Dispose();
    }
}
