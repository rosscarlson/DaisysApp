using System.Diagnostics;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Resizer;

/// <summary>
/// Polls the running processes (every <see cref="ResizerData.PollRateMs"/>). Always reports which profiles' programs
/// are running (for the "running" markers); when the watcher is on, applies each Automatic profile once per new
/// process instance, after the profile's delay — as Resize Rabbit's process watcher does.
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly Func<ResizerData> data;
    private readonly CancellationTokenSource cts = new();
    private readonly HashSet<int> applied = new();
    private HashSet<string> lastRunning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (background thread) with the profile process names (normalized) that are running, when that set changes.</summary>
    public event Action<HashSet<string>>? RunningChanged;

    /// <summary>Raised (background thread) when an Automatic profile was applied, with its result.</summary>
    public event Action<ResizeProfile, ApplyResult>? AutoApplied;

    public ProcessWatcher(Func<ResizerData> data) => this.data = data;

    public void Start() => _ = Task.Run(() => LoopAsync(cts.Token));

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var settings = data();
            try { Poll(settings); }
            catch (Exception ex) { ErrorLog.Write("Resizer watcher", ex); }
            try { await Task.Delay(Math.Clamp(settings.PollRateMs, 100, 60000), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Poll(ResizerData settings)
    {
        var processes = new List<(string Name, int Pid)>();
        foreach (var p in Process.GetProcesses())
            using (p) processes.Add((p.ProcessName, p.Id));
        var names = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var profiles = settings.Profiles.ToList();
        var running = profiles.Select(p => ProcessFinder.Normalize(p.ProcessName))
                              .Where(n => n.Length > 0 && names.Contains(n))
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!running.SetEquals(lastRunning))
        {
            Logging.Log.Here.Info($"Programs with a profile running: {(running.Count == 0 ? "none" : string.Join(", ", running))}");
            lastRunning = running;
            RunningChanged?.Invoke(running);
        }

        if (!settings.ProcessWatcherEnabled) return;
        foreach (var profile in profiles.Where(p => p.Auto))
        {
            string name = ProcessFinder.Normalize(profile.ProcessName);
            var match = processes.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match.Name == null || !applied.Add(match.Pid)) continue;
            var copy = profile.Clone();
            Logging.Log.Here.Info($"{copy.Name}: {match.Name} started (process {match.Pid}), applying in {copy.Delay} ms");
            _ = Task.Run(async () =>
            {
                await Task.Delay(Math.Max(0, copy.Delay));
                var result = await WindowMover.ApplyAsync(copy, retry: true, monitor: true);
                Logging.Log.Here.Info($"{copy.Name} (automatic): {result}");
                AutoApplied?.Invoke(copy, result);
            });
        }

        // forget instances that have exited, so a relaunch is applied again
        var alive = processes.Select(p => p.Pid).ToHashSet();
        applied.RemoveWhere(pid => !alive.Contains(pid));
    }

    public void Dispose() => cts.Cancel();
}
