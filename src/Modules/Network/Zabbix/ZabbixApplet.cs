using System.Windows;
using DaisysApp.Applets.Network.Zabbix.Views;
using DaisysApp.Applets.Network.Zabbix.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Network.Zabbix;

/// <summary>
/// Zabbix, a sub-tab of the Network tab: every host on a Zabbix server as a box (status, CPU, memory, problems), each
/// host's page with Performance-style tiles, graphs and history windows, graphs of several values together, and
/// widgets. Read-only. Until a server is set up it asks for one in the setup wizard (with Skip and Switch off), or in
/// its own window when the Network tab was switched on later. Switched off (<see cref="Off"/>), its sub-tab is hidden
/// and it does nothing.
/// </summary>
public sealed class ZabbixApplet : IApplet
{
    private readonly ZabbixSettings settings = ZabbixSettings.Load();
    private readonly ZabbixService service;
    private readonly ZabbixWidgets widgets;
    private readonly ZabbixView view;
    private readonly ZabbixSettingsView settingsView;
    private bool setupShown; // the wizard showed its step this run

    public ZabbixApplet()
    {
        service = new ZabbixService(settings);
        widgets = new ZabbixWidgets(service);
        view = new ZabbixView(service, widgets);
        settingsView = new ZabbixSettingsView(service);
        service.OffChanged += () =>
        {
            if (Off) widgets.Shutdown();
            else widgets.Start();
            OffChanged?.Invoke();
        };
    }

    /// <summary>Switched off: the Network tab hides the sub-tab.</summary>
    public bool Off => settings.Off;

    public event Action? OffChanged;

    public FrameworkElement View => view;

    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        if (!Off) widgets.Start();
    }

    /// <summary>The wizard's step, after installing or updating, while no server has answered.</summary>
    public FrameworkElement? SetupPage
    {
        get
        {
            if (Off) return null;
            setupShown = true;
            return new ZabbixSetupPanel(service);
        }
    }

    /// <summary>Switched on but never set up (or skipped, or switched off): the wizard opens for it.</summary>
    public bool SetupRequired => !Off && !settings.Configured && !settings.SetupSkipped;

    /// <summary>Switched on (the Network tab) after the wizard (so it had no step there), and never set up or skipped.</summary>
    public bool NeedsOnboarding => !Off && !settings.Configured && !settings.SetupSkipped && !setupShown;

    public void RunOnboarding(Window owner)
    {
        setupShown = true;
        ZabbixSetupPanel.ShowWindow(service, owner);
    }

    public IReadOnlyList<AppletMenuItem>? TrayMenu => null;

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        widgets.Shutdown();
        service.Dispose();
    }
}
