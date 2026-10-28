using DaisysApp.Applets.Support.Networking;
using DaisysApp.Applets.Support.StressTests;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support;

/// <summary>
/// Support tab: tools for working out what's wrong. <b>Networking Tools</b> is its first sub-tab (connection details,
/// ping, speed test, traceroute, Wi-Fi scan, port scan and a log of drops), with its tools on sub-tabs of their own;
/// <b>Stress Tests</b> the second (CPU, memory, storage, GPU and video tests that score the PC and show what breaks).
/// </summary>
[Applet("Support", "Support", "", Order = 47,
    Description = "Troubleshooting tools: your connection, ping, speed test, traceroute, Wi-Fi scan, port scan and drops; stress tests that score your PC")]
public sealed class SupportApplet : AppletGroup
{
    public const string ModuleId = "Support", Networking = "Networking", StressTests = "StressTests";

    public SupportApplet()
    {
        Add(Networking, T("Networking Tools"), "", () => new NetworkingApplet());
        Add(StressTests, T("Stress Tests"), "", () => new StressApplet());
    }
}
