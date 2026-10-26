using DaisysApp.Applets.Network.Zabbix;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Network;

/// <summary>
/// Network tab: <b>Zabbix</b>, every host on a Zabbix server, with its graphs, history, problems and widgets (Logs was
/// its second sub-tab from 0.39 until 3.12.9, when it went to the Tools tab). Zabbix can be switched off on its own (in
/// its setup, or Settings → Network → Zabbix), which hides its sub-tab.
/// </summary>
[Applet("Network", "Network", "\uE968", Order = 45,
    Description = "Your Zabbix server's hosts with their graphs, history, problems and widgets")]
public sealed class NetworkApplet : AppletGroup
{
    public const string Zabbix = "Zabbix";

    public NetworkApplet()
    {
        ZabbixApplet? zabbix = null;
        Add(Zabbix, T("Zabbix"), "\uE968", () => zabbix = new ZabbixApplet());
        if (zabbix == null) return;
        ShowTool(Zabbix, !zabbix.Off);
        zabbix.OffChanged += () => ShowTool(Zabbix, !zabbix.Off);
    }
}
