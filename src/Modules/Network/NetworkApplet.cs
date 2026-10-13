using DaisysApp.Applets.Network.Logs;
using DaisysApp.Applets.Network.Zabbix;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Network;

/// <summary>
/// Network tab: two tools, each on its own sub-tab. <b>Zabbix</b>: every host on a Zabbix server, with its graphs,
/// history, problems and widgets. <b>Logs</b>: Daisy's App's own logs, Windows' event logs and WSL's. Each keeps its
/// own settings file, as when they were separate modules (before 0.39: Logs was a sub-tab of Settings). Zabbix can be
/// switched off on its own (in its setup, or Settings → Network → Zabbix), which hides its sub-tab.
/// </summary>
[Applet("Network", "Network", "\uE968", Order = 45,
    Description = "Your Zabbix server's hosts with their graphs, history, problems and widgets, and Daisy's App's own logs, Windows' event logs and WSL's")]
public sealed class NetworkApplet : AppletGroup
{
    public const string Zabbix = "Zabbix", Logs = "Logs";

    public NetworkApplet()
    {
        ZabbixApplet? zabbix = null;
        Add(Zabbix, T("Zabbix"), "\uE968", () => zabbix = new ZabbixApplet());
        Add(Logs, T("Logs"), "\uE9F9", () => new LogsApplet());
        if (zabbix == null) return;
        ShowTool(Zabbix, !zabbix.Off);
        zabbix.OffChanged += () => ShowTool(Zabbix, !zabbix.Off);
    }
}
