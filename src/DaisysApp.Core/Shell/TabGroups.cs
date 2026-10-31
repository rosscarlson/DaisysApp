using System.Reflection;
using DaisysApp.Shell;

// Gaming, shared by Button2Key, Button2Key Remote, ForzaWheel, Forza Analysis, iRacing, Calibration and RGB (each a module
// of its own): with two or more installed they're its sub-tabs. Its id is GamingTools: Video's Overlay tool has had the id
// Gaming since before it was a sub-tab.
[assembly: TabGroup("GamingTools", "Gaming", "", Order = 55,
    Description = "Button2Key: controller buttons and sticks to keys, macros and mouse moves; Button2Key Remote: screens of buttons on your phone, tablet or PC that do the same; ForzaWheel: your wheel, pedals, shifter and button boxes as one controller for Forza; iRacing: your races, in numbers and graphs; Calibration: your pedals', wheels' and sticks' axes calibrated in Windows")]

namespace DaisysApp.Shell;

/// <summary>
/// Tabs that several modules share and none of them owns, described here. A module joins one with
/// <c>[Applet(Group = "GamingTools")]</c>.
/// </summary>
public static class TabGroups
{
    public static IEnumerable<TabGroupAttribute> All => typeof(TabGroups).Assembly.GetCustomAttributes<TabGroupAttribute>();
}
