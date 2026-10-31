using System.Windows;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// SimEffects → Shaker: a game's telemetry as vibration on transducers (ButtKicker, Dayton Audio…) on audio
/// devices, effect by effect. Keeps BassShaker.json: on / off is kept per SimEffects profile, and each profile has its
/// own effects (<see cref="ShakerSettings.ForProfile"/>); the game is the profile's (<see cref="SimData"/>).
/// </summary>
public sealed class BassShakerApplet : IApplet
{
    private readonly ShakerSettings settings = ShakerSettings.Load();
    private readonly ShakerEngine engine;
    private readonly DeviceVolumes deviceVolumes;
    private readonly VolumeHotkeys hotkeys;
    private readonly BassShakerView view;
    private readonly FrameworkElement settingsView;

    public BassShakerApplet()
    {
        engine = new ShakerEngine(settings);
        deviceVolumes = new DeviceVolumes(settings);
        // a device's volume changed in Windows: its slider shows it
        deviceVolumes.Changed += () => { VolumeControl.Refresh(); settings.Save(); };
        hotkeys = new VolumeHotkeys(settings, Volumes, () => view!.SetEnabled(!settings.Enabled));
        LevelWizard.AddKeys(settings);
        SimProfiles.Register(new ToolScope
        {
            Id = "BassShaker", Title = T("Shaker"), Settings = settings, Props = new[] { nameof(ShakerSettings.Enabled) },
            IsOn = () => settings.Enabled, SetOn = on => view.SetEnabled(on), Loaded = () => view.ProfileLoaded(), OnWithGames = settings.SwitchOnWithGame,
        });
        view = new BassShakerView(settings, engine, hotkeys);
        SimProfiles.Renamed += Renamed;
        settingsView = SettingsPage();
    }

    /// <summary>Settings → SimEffects → Shaker: how strong the Test buttons play.</summary>
    private FrameworkElement SettingsPage()
    {
        var card = new System.Windows.Controls.StackPanel { Width = 420 };
        card.Children.Add(Ui.Header(T("Shaker")));
        card.Children.Add(SimUi.SliderRow(T("Test level"), 10, 100, 5, settings.TestLevel * 100, v => $"{v:0}%",
            v => { settings.TestLevel = v / 100; settings.Save(); }, T("How strong the Test buttons play")));
        var box = SimUi.Card(card);
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

    /// <summary>A SimEffects profile renamed: its effects go with it.</summary>
    private void Renamed(string old, string name)
    {
        if (settings.Profiles.FirstOrDefault(p => p.Name == old) is { } p && settings.Profiles.All(o => o.Name != name)) p.Name = name;
        settings.Save();
        view.Render();
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
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Shaker on"), () => view.SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        SimProfiles.Renamed -= Renamed;
        view.HideWidget();
        hotkeys.Dispose();
        TitleBadges.Set("BassShaker", null);
        LevelWizard.RemoveKeys();
        engine.Dispose();
        deviceVolumes.Dispose();
        settings.Save();
    }
}
