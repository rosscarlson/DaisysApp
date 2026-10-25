using System.Windows;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// Keeps turned-off apps off. Fire OS 8 turns some of its apps back on at every start (its own package manager, in
/// the system process, so nothing on the TV can stop it without root: seen with Alexa, Silk Browser, Push
/// notifications and Fubo on Fire OS 8.1.8.5). While the app runs, every 2 minutes each TV Apply has changed is
/// checked at its last address (only one that already allows this PC: no prompt), and whatever this tool turned off
/// but is on again is turned off again (and suspended). <see cref="Said"/> tells the tab what it did.
/// </summary>
internal sealed class KeepOff : IDisposable
{
    private static readonly Logging.Logger log = Logging.Log.Here;
    private static readonly TimeSpan every = TimeSpan.FromMinutes(2);

    private readonly FireTvSettings settings;
    private readonly Func<bool> busy;
    private readonly CancellationTokenSource stop = new();

    /// <summary>On a thread pool thread: the TV's serial, a line and what the TV answered.</summary>
    public event Action<string, string, string>? Said;

    public KeepOff(FireTvSettings settings, Func<bool> busy)
    {
        this.settings = settings;
        this.busy = busy;
    }

    public void Start() => _ = Task.Run(Loop);

    private async Task Loop()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stop.Token);
            while (!stop.IsCancellationRequested)
            {
                try { await CheckAll(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.Warn("Keep Off", ex); }
                await Task.Delay(every, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private record Tv(string Serial, string Name, string Address, List<string> Off);

    private async Task CheckAll()
    {
        // (a copy, taken on the UI thread, which changes the settings)
        List<Tv>? tvs = null;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!settings.KeepOff || busy()) return;
            // (TVs applied before 3.8.4 have no address of their own: the one typed last, checked by serial)
            tvs = settings.Devices
                .Select(d => new Tv(d.Key, d.Value.Name, d.Value.Address.Length > 0 ? d.Value.Address : settings.Address, d.Value.Packages.Keys.ToList()))
                .Where(t => t.Address.Length > 0)
                .Where(t => t.Off.Count > 0).ToList();
        });
        foreach (var tv in tvs ?? new())
            await Check(tv);
    }

    /// <summary>Checks one TV now (after the tab's own restart, too).</summary>
    public async Task<List<string>> Check(string serial, string address, IEnumerable<string> off, CancellationToken ct = default) =>
        await Check(new Tv(serial, "", address, off.ToList()), ct);

    private async Task<List<string>> Check(Tv tv, CancellationToken ct = default)
    {
        var turned = new List<string>();
        FireTvDevice d;
        using var both = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, ct);
        try { d = await FireTvDevice.ConnectAsync(tv.Address, () => { }, both.Token, quiet: true); }
        catch (AdbException ex)
        {
            log.Debug($"Keep Off: {tv.Address}: {ex.Message}");
            return turned;
        }
        using (d)
        {
            if (d.Serial != tv.Serial) return turned;
            var disabled = await d.DisabledAsync(both.Token);
            foreach (var p in tv.Off.Where(p => !disabled.Contains(p)))
            {
                var name = Catalog.Find(p)?.Name ?? p;
                var (how, answer) = await d.RemoveAsync(new TvApp { Package = p, Name = name }, both.Token);
                if (how == null)
                {
                    log.Warn($"Keep Off: couldn't turn {p} off again on {d.Name}: {answer}");
                    continue;
                }
                turned.Add(name);
                log.Info($"Keep Off: {p} was on again on {d.Name}: turned off");
            }
            if (turned.Count > 0)
                Said?.Invoke(tv.Serial, F("Turned off again on {0}: {1}", d.Name, string.Join(", ", turned)), T("Fire OS turned them back on"));
        }
        return turned;
    }

    public void Dispose() => stop.Cancel();
}
