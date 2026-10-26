using System.Windows;
using DaisysApp.Applets.HomeAssistant.Views;
using DaisysApp.Shell;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>
/// The HA tab: every device in a Home Assistant, grouped by area, floor or type as Zabbix's hosts are, with a quick
/// control on each box and a window per device with everything it can do; shortcuts for anything that switches or has
/// a level; devices hidden from the tab and shown again in Settings → HA. Connected over Home Assistant's WebSocket API
/// with a long-lived access token while the module runs. Until it's set up it asks in the setup wizard (with Skip and
/// Switch off), or in its own window when the module was switched on later.
/// </summary>
[Applet(Id, "HA", "", Order = 47,
    Description = "Your Home Assistant devices by area or type, with quick toggles, a window per device for full control, and shortcuts")]
public sealed class HaApplet : IApplet
{
    public const string Id = "HomeAssistant";

    private readonly HaSettings settings = HaSettings.Load();
    private readonly HaService service;
    private readonly HaHotkeys hotkeys;
    private readonly HaView view;
    private readonly HaSettingsView settingsView;
    private bool setupShown; // the wizard showed its step this run

    public HaApplet()
    {
        service = new HaService(settings);
        hotkeys = new HaHotkeys(service);
        view = new HaView(service, hotkeys);
        settingsView = new HaSettingsView(service);
    }

    public FrameworkElement View => view;

    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        hotkeys.Register();
        service.Start();
    }

    /// <summary>The wizard's step, while it isn't set up.</summary>
    public FrameworkElement? SetupPage
    {
        get
        {
            setupShown = true;
            return new HaSetupPanel(service);
        }
    }

    /// <summary>Never set up (nor skipped): the wizard opens for it.</summary>
    public bool SetupRequired => !settings.Configured && !settings.SetupSkipped;

    /// <summary>Switched on after the wizard (so it had no step there), and never set up or skipped.</summary>
    public bool NeedsOnboarding => !settings.Configured && !settings.SetupSkipped && !setupShown;

    public void RunOnboarding(Window owner)
    {
        setupShown = true;
        HaSetupPanel.ShowWindow(service, owner);
    }

    public IReadOnlyList<AppletMenuItem>? TrayMenu => null;

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        ThingWindow.CloseAll();
        hotkeys.Dispose();
        service.Dispose();
    }
}
