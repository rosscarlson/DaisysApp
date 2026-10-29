using DaisysApp.Applets.Support.Logs;
using DaisysApp.Applets.Support.Networking;
using DaisysApp.Applets.Support.QuickTools;
using DaisysApp.Applets.Support.StressTests;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support;

/// <summary>
/// Support tab: tools for working out what's wrong. <b>Network Tools</b> is its first sub-tab (connection details,
/// ping, speed test, traceroute, Wi-Fi scan, port scan and a log of drops), with its tools on sub-tabs of their own;
/// <b>Stress Tests</b> the second (CPU, memory, storage, GPU and video tests that score the PC and show what breaks);
/// <b>Logs</b> (Daisy's App's own logs, Windows' event logs and WSL's) and <b>Tools</b> (buttons for Windows' resets
/// and admin jobs), on the Tools tab until 3.24.9.
/// </summary>
[Applet("Support", "Support", "", Order = 47,
    Description = "Troubleshooting tools: your connection, ping, speed test, traceroute, Wi-Fi scan, port scan and drops; stress tests that score your PC; Daisy's App's, Windows' and WSL's logs; buttons for Windows' resets and admin jobs")]
public sealed class SupportApplet : AppletGroup
{
    public const string ModuleId = "Support", Networking = "Networking", StressTests = "StressTests", Logs = "Logs", QuickTools = "QuickTools";

    public SupportApplet()
    {
        Add(Networking, T("Network Tools"), "", () => new NetworkingApplet());
        Add(StressTests, T("Stress Tests"), "", () => new StressApplet());
        Add(Logs, T("Logs"), "", () => new LogsApplet());
        Add(QuickTools, T("Tools"), "", () => new QuickToolsApplet());
    }
}
