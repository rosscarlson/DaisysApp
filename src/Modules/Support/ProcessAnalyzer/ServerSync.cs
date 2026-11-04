using System.Text.Json;
using System.Text.Json.Nodes;
using DaisysApp.Logging;
using DaisysApp.Server;
using DaisysServer.Protocol;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// The process database kept in step with Daisy's Server's master database (its <c>processes</c> module; the protocol
/// is in this tool's README, for the server's PROTOCOL.md), when the server's set up (Settings → General) and Use
/// Daisy's Server is on. On joining it sends the version it has; the server answers with every change since (all of
/// it for a new client, which then takes the server's entries over its own), then this PC's own changes go up and come
/// back numbered, to every client. Not sealed: the server reads and merges it (programs' names and classifications,
/// nothing of the user's). Without the server everything stays local.
/// </summary>
internal sealed class ServerSync : IDisposable
{
    public const string Module = "processes";
    private readonly CancellationTokenSource stop = new();
    private TaskCompletionSource wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile ServerChannel? channel;
    private readonly HashSet<string> inFlight = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastUsage = DateTime.MinValue;
    private readonly Debounce sendSoon;

    /// <summary>Where it's at, worded for the user.</summary>
    public string Status { get; private set; } = "";
    public bool Connected => channel != null;
    /// <summary>The PCs the server knows (when it says).</summary>
    public int Pcs { get; private set; }

    public event Action? Changed;

    public ServerSync()
    {
        sendSoon = new Debounce(2000, () => { if (channel != null && ProcessDb.Db.DirtyCount > 0) Send(); });
        ServerLink.Changed += Wake;
        ProcessDb.Db.Changed += SendSoon;
        _ = Task.Run(RunAsync);
    }

    private void Wake() => Interlocked.Exchange(ref wake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private static string Identity => $"{ServerLink.Settings.Host}:{ServerLink.Settings.Port}";

    private void Set(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private async Task RunAsync()
    {
        var ct = stop.Token;
        int wait = 5;
        while (!ct.IsCancellationRequested)
        {
            var next = wake.Task;
            if (!ServerLink.Configured)
            {
                Set(T("Daisy's Server isn't set up: local only"));
                await Task.WhenAny(next, Task.Delay(Timeout.Infinite, ct));
                continue;
            }
            try
            {
                Set(T("Connecting…"));
                var c = await ServerLink.OpenAsync(Module, "ProcessAnalyzer", ct);
                channel = c;
                wait = 5;
                if (ProcessDb.Db.Server != Identity) ProcessDb.Db.SetVersion(Identity, 0);
                c.Post(new JsonObject { ["op"] = "join", ["version"] = ProcessDb.Db.Version, ["app"] = typeof(ServerSync).Assembly.GetName().Version?.ToString() });
                Set(F("Connected to {0}", c.Server));
                while (await c.ReadAsync(ct) is { } m) Handle(c, m);
                Set(F("Daisy's Server: {0}", (c.Ended.Length > 0 ? c.Ended : T("closed")).TrimEnd('.')));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Set(F("Daisy's Server: {0}", ex.Message.TrimEnd('.')));
                Log.Here.Info($"Process Analyzer: Daisy's Server: {ex.Message}");
            }
            channel?.Dispose();
            channel = null;
            lock (inFlight) { ProcessDb.Db.MarkDirty(inFlight); inFlight.Clear(); }
            Changed?.Invoke();
            await Task.WhenAny(wake.Task, Task.Delay(TimeSpan.FromSeconds(wait), ct));
            wait = Math.Min(wait * 3, 300);
        }
    }

    private void Handle(ServerChannel c, JsonObject m)
    {
        switch (ServerClient.Str(m, "op"))
        {
            case "changes":
            {
                long version = m["version"]?.GetValue<long>() ?? 0;
                bool full = m["full"]?.GetValue<bool>() ?? false;
                bool first = ProcessDb.Db.Version == 0;
                var list = m["entries"]?.Deserialize<List<KnownProcess>>(ProcessDb.Json) ?? new();
                lock (inFlight) foreach (var e in list) inFlight.Remove(KnownProcess.Key(e.Name));
                var (added, replaced) = ProcessDb.Db.Merge(list, "server", fromServer: true, serverFirst: full && first);
                ProcessDb.Db.SetVersion(Identity, version);
                if (added + replaced > 0) Log.Here.Info($"Process Analyzer: from {c.Server}: {added} new, {replaced} changed (version {version})");
                // a new client: what it knew that the server doesn't goes up
                if (full && first)
                {
                    var theirs = new HashSet<string>(list.Select(e => KnownProcess.Key(e.Name)), StringComparer.OrdinalIgnoreCase);
                    ProcessDb.Db.MarkDirty(ProcessDb.Db.All().Where(e => e.Source != "seed" && !theirs.Contains(e.Name)).Select(e => e.Name));
                }
                c.Post(new JsonObject { ["op"] = "seen", ["version"] = version });
                Set(F("Synced with {0} · version {1}", c.Server, version));
                Send();
                break;
            }
            case "clients":
                Pcs = (m["clients"] as JsonArray)?.Count ?? 0;
                Changed?.Invoke();
                break;
            case "error":
                Set(F("Daisy's Server: {0}", ServerClient.Str(m, "message")));
                break;
        }
    }

    private void SendSoon() => sendSoon.Poke();

    /// <summary>Sends what's changed here, and this PC's usage figures once an hour.</summary>
    private void Send()
    {
        var c = channel;
        if (c == null) return;
        var dirty = ProcessDb.Db.TakeDirty();
        if (dirty.Count > 0)
        {
            lock (inFlight) foreach (var e in dirty) inFlight.Add(e.Name);
            foreach (var chunk in dirty.Chunk(200))
                c.Post(new JsonObject { ["op"] = "report", ["entries"] = JsonSerializer.SerializeToNode(chunk, ProcessDb.Json) });
            Log.Here.Debug($"Process Analyzer: sent {dirty.Count} entries");
        }
        if (DateTime.Now - lastUsage > TimeSpan.FromHours(1))
        {
            lastUsage = DateTime.Now;
            var usage = new JsonArray();
            foreach (var e in ProcessDb.Db.All())
                if (ProcessDb.Db.SeenOf(e.Name) is { Samples: >= 10 } o)
                    usage.Add(new JsonObject
                    {
                        ["name"] = e.Name, ["cpu"] = Math.Round(o.AvgCpu, 2), ["peakCpu"] = Math.Round(o.PeakCpu, 1),
                        ["memoryMB"] = Math.Round(o.AvgMemoryMB, 1), ["peakMemoryMB"] = Math.Round(o.PeakMemoryMB, 1), ["samples"] = o.Samples,
                    });
            if (usage.Count > 0) c.Post(new JsonObject { ["op"] = "usage", ["items"] = usage });
        }
    }

    public void Dispose()
    {
        ServerLink.Changed -= Wake;
        ProcessDb.Db.Changed -= SendSoon;
        stop.Cancel();
        sendSoon.Dispose();
        channel?.Dispose();
    }
}
