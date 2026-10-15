using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Applets.Gaming.WheelEmu;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Gaming tab: two tools, each on its own sub-tab. <b>Joy2Key</b>: controller buttons and sticks pressing keys and
/// moving the mouse, a profile per game (its own tab before 0.53). <b>WheelEmu</b>: a wheel, pedals, shifter and button
/// boxes as one vJoy controller for Forza, with Hush to hide the real devices from the game. Each keeps its own settings
/// file. The tab's id is GamingTools: Video's Gaming tool (the recorder and overlay) has had the id Gaming since before
/// it was a sub-tab, and its settings file is Gaming.json.
/// </summary>
[Applet("GamingTools", "Gaming", "", Order = 55,
    Description = "Joy2Key: controller buttons and sticks to keys, macros and mouse moves; WheelEmu: your wheel, pedals, shifter and button boxes as one controller for Forza")]
public sealed class GamingApplet : AppletGroup
{
    public const string Joy2Key = "Joy2Key", WheelEmu = "WheelEmu";

    public GamingApplet()
    {
        Add(Joy2Key, T("Joy2Key"), "", () => new Joy2KeyApplet());
        Add(WheelEmu, T("WheelEmu"), "", () => new WheelEmuApplet());
    }
}
