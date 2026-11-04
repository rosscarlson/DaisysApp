using DaisysApp.Logging;

namespace DaisysApp.Applets.Video.Resizer;

/// <summary>
/// Looks at the running processes (Core's watch, about every <see cref="ResizerData.PollRateMs"/>, told only when a
/// program starts or ends or the profiles change). Always reports which profiles' programs
/// are running (for the "running" markers); when the watcher is on, applies each Automatic profile once per new
/// process instance, after the profile's delay — as Resize Rabbit's process watcher does.
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly Func<ResizerData> data;
    private IDisposable? watch;
    private int watchMs;
    private readonly HashSet<int> applied = new();
    private HashSet<string> lastRunning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (background thread) with the profile process names (normalized) that are running, when that set changes.</summary>
    public event Action<HashSet<string>>? RunningChanged;

    /// <summary>Raised (background thread) when an Automatic profile was applied, with its result.</summary>
    public event Action<ResizeProfile, ApplyResult>? AutoApplied;

    public ProcessWatcher(Func<ResizerData> data) => this.data = data;

    /// <summary>Starts watching, or again at a new poll rate.</summary>
    public void Start()
    {
        int ms = Math.Clamp(data().PollRateMs, 100, 60000);
        if (watch != null && ms == watchMs) return;
        watch?.Dispose();
        watchMs = ms;
        watch = Shared.RunningPrograms.Watch(snapshot =>
        {
            try { Poll(data(), snapshot); }
            catch (Exception ex) { ErrorLog.Write("Resizer watcher", ex); }
        }, ms, () =>
        {
            var d = data();
            return d.ProcessWatcherEnabled + "|" + string.Join("|", d.Profiles.Select(p => p.ProcessName + (p.Auto ? "*" : "")));
        });
    }

    // (the app's one process list, shared with the other modules watching for programs)
    private void Poll(ResizerData settings, Shared.RunningPrograms.Snapshot snapshot)
    {
        var processes = snapshot.Processes;
        var names = snapshot.Names;

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

    public void Dispose() => watch?.Dispose();
}
