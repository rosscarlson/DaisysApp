using DaisysApp.Applets.Support.Networking;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support;

/// <summary>
/// Support tab: tools for working out what's wrong. <b>Networking Tools</b> is its first sub-tab (connection details,
/// ping, speed test, traceroute, Wi-Fi scan, port scan and a log of drops), with its tools on sub-tabs of their own.
/// </summary>
[Applet("Support", "Support", "", Order = 47,
    Description = "Troubleshooting tools: your connection, ping, speed test, traceroute, Wi-Fi scan, port scan and drops")]
public sealed class SupportApplet : AppletGroup
{
    public const string ModuleId = "Support", Networking = "Networking";

    public SupportApplet()
    {
        Add(Networking, T("Networking Tools"), "", () => new NetworkingApplet());
    }
}
