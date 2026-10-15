using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Applets.Gaming.ForzaWheel;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Gaming tab: two tools, each on its own sub-tab. <b>Joy2Key</b>: controller buttons and sticks pressing keys and
/// moving the mouse, a profile per game (its own tab before 0.53). <b>ForzaWheel</b> (WheelEmu before 0.58): a wheel, pedals, shifter and button
/// boxes as one vJoy controller for Forza, with Hush to hide the real devices from the game. Each keeps its own settings
/// file. The tab's id is GamingTools: Video's Gaming tool (the recorder and overlay) has had the id Gaming since before
/// it was a sub-tab, and its settings file is Gaming.json.
/// </summary>
[Applet("GamingTools", "Gaming", "", Order = 55,
    Description = "Joy2Key: controller buttons and sticks to keys, macros and mouse moves; ForzaWheel: your wheel, pedals, shifter and button boxes as one controller for Forza")]
public sealed class GamingApplet : AppletGroup
{
    public const string Joy2Key = "Joy2Key", ForzaWheel = "ForzaWheel";

    /// <summary>The tool was WheelEmu until 0.58: the sub-tab remembered as showing follows its new id.</summary>
    static GamingApplet()
    {
        try
        {
            var path = DaisysApp.Settings.JsonStore.PathFor("GamingTools");
            if (System.IO.File.Exists(path) && System.IO.File.ReadAllText(path) is { } text && text.Contains("\"WheelEmu\""))
                System.IO.File.WriteAllText(path, text.Replace("\"WheelEmu\"", "\"ForzaWheel\""));
        }
        catch { }
    }

    public GamingApplet()
    {
        Add(Joy2Key, T("Joy2Key"), "", () => new Joy2KeyApplet());
        Add(ForzaWheel, T("ForzaWheel"), "", () => new ForzaWheelApplet());
    }
}
