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
    private readonly DeviceVolumes deviceVolumes;
    private readonly VolumeHotkeys hotkeys;
    private readonly BassShakerView view;
    private readonly FrameworkElement settingsView;
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? lastGame;
    private bool checking, closing;

    public BassShakerApplet()
    {
        engine = new ShakerEngine(settings);
        deviceVolumes = new DeviceVolumes(settings);
        // a device's volume changed in Windows: its slider shows it
        deviceVolumes.Changed += () => { VolumeControl.Refresh(); settings.Save(); };
        hotkeys = new VolumeHotkeys(settings, Volumes, () => view!.SetEnabled(!settings.Enabled));
        LevelWizard.AddKeys(settings);
        view = new BassShakerView(settings, engine, hotkeys);
        gameTimer.Tick += async (_, _) => await CheckGames();
        settingsView = SettingsPage();
    }

    /// <summary>Settings → SimEffects → Shaker: how strong the Test buttons play.</summary>
    private FrameworkElement SettingsPage()
    {
        var card = new System.Windows.Controls.StackPanel { Width = 420 };
        card.Children.Add(Ui.Header(T("Shaker")));
        card.Children.Add(Ui.SliderRow(T("Test level"), 10, 100, 5, settings.TestLevel * 100, v => $"{v:0}%",
            v => { settings.TestLevel = v / 100; settings.Save(); }, T("How strong the Test buttons play")));
        var box = Ui.Card(card);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Top;
        return box;
    }

    /// <summary>Everything whose volume has shortcuts: the master, the devices, the profile in use's effects.</summary>
    private IEnumerable<(string Id, string Title, IVolume Volume)> Volumes()
    {
        yield return ("Master", T("Master"), settings.Master);
        foreach (var d in settings.Devices) yield return ($"Device:{d.DeviceId}:{d.Name}", d.Name, d);
        if (view?.Active is { } p)
            for (int i = 0; i < p.Effects.Count; i++)
                yield return ($"Effect:{i}:{p.Effects[i].Kind}", EffectKinds.Of(p.Effects[i].Kind).Title, p.Effects[i]);
    }

    private async Task CheckGames()
    {
        if (checking || closing || !settings.GameSwitching) return;
        checking = true;
        try
        {
            var wanted = settings.Profiles.SelectMany(p => p.Programs.Select(prog => (p, prog: Games.Bare(prog)))).ToList();
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


    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        deviceVolumes.Start();
        engine.Apply(view.Active);
        hotkeys.Register();
        view.Render();
        if (settings.MetersWidget.Show) view.ShowWidget();
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
        view.HideWidget();
        hotkeys.Dispose();
        LevelWizard.RemoveKeys();
        engine.Dispose();
        deviceVolumes.Dispose();
        settings.Save();
    }
}
