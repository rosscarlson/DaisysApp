using System.Windows;
using System.Windows.Threading;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// SimEffects → Shaker: a game's telemetry as vibration on transducers (ButtKicker, Dayton Audio…) on audio
/// devices, effect by effect, a profile per game. Keeps BassShaker.json. With Game Switching on, a profile's program
/// starting picks that profile (once, when it starts: picking another by hand meanwhile stays).
/// </summary>
public sealed class BassShakerApplet : IApplet
{
    private readonly ShakerSettings settings = ShakerSettings.Load();
    private readonly ShakerEngine engine;
    private readonly VolumeHotkeys hotkeys;
    private readonly BassShakerView view;
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? lastGame;
    private bool checking, closing;

    public BassShakerApplet()
    {
        engine = new ShakerEngine(settings);
        hotkeys = new VolumeHotkeys(Volumes, () => settings.OnOffKey, () =>
        {
            Logging.Log.Here.Info("Shaker shortcut: on / off");
            view!.SetEnabled(!settings.Enabled);
        });
        view = new BassShakerView(settings, engine, hotkeys);
        gameTimer.Tick += async (_, _) => await CheckGames();
    }

    /// <summary>Everything whose volume has shortcuts: the master, the devices, the profile in use's effects.</summary>
    private IEnumerable<IVolume> Volumes()
    {
        yield return settings.Master;
        foreach (var d in settings.Devices) yield return d;
        if (view?.Active is { } p) foreach (var e in p.Effects) yield return e;
    }

    private async Task CheckGames()
    {
        if (checking || closing || !settings.GameSwitching) return;
        checking = true;
        try
        {
            var wanted = settings.Profiles.SelectMany(p => p.Programs.Select(prog => (p, prog: Bare(prog)))).ToList();
            if (wanted.Count == 0) return;
            var running = await Task.Run(() =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    try { names.Add(proc.ProcessName); } catch { }
                    proc.Dispose();
                }
                return names;
            });
            if (closing) return;
            var hit = wanted.FirstOrDefault(w => running.Contains(w.prog));
            string? game = hit.p != null ? hit.prog : null;
            if (game == lastGame) return;
            lastGame = game;
            if (hit.p != null)
            {
                Logging.Log.Here.Info($"Shaker: {game}.exe is running");
                view.Use(hit.p);
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Shaker games", ex); }
        finally { checking = false; }
    }

    private static string Bare(string s)
    {
        s = System.IO.Path.GetFileName(s.Trim());
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        engine.Apply(view.Active);
        hotkeys.Register();
        view.Render();
        gameTimer.Start();
        _ = CheckGames();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Shaker on"), () => view.SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        closing = true;
        gameTimer.Stop();
        hotkeys.Dispose();
        engine.Dispose();
        settings.Save();
    }
}
