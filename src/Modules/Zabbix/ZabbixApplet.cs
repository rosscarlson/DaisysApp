using System.Windows;
using DaisysApp.Applets.Zabbix.Views;
using DaisysApp.Applets.Zabbix.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Zabbix;

/// <summary>
/// The Zabbix tab: every host on a Zabbix server as a box (status, CPU, memory, problems), each host's page with
/// Performance-style tiles, graphs and history windows, graphs of several values together, and widgets. Read-only.
/// Until a server is set up it asks for one in the setup wizard (with Skip and Switch off), or in its own window when
/// it was switched on later.
/// </summary>
[Applet("Zabbix", "Zabbix", "", Order = 45,
    Description = "Your Zabbix server's hosts at a glance, each with its graphs, history and problems, and widgets for any of them")]
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
    }

    public FrameworkElement View => view;

    public FrameworkElement? SettingsView => settingsView;

    public void Start() => widgets.Start();

    /// <summary>The wizard's step, after installing or updating, while no server has answered.</summary>
    public FrameworkElement? SetupPage
    {
        get
        {
            if (settings.Configured) return null;
            setupShown = true;
            return new ZabbixSetupPanel(service);
        }
    }

    /// <summary>Switched on after the wizard (so it had no step there), and never set up or skipped.</summary>
    public bool NeedsOnboarding => !settings.Configured && !settings.SetupSkipped && !setupShown;

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
