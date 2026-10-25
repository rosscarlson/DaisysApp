using DaisysApp.Shell;

// The Gaming tab, shared by Joy2Key, Button2Key Remote, ForzaWheel, Forza Analysis, iRacing and Calibration (each a module of its own): when two or more
// are installed they're its sub-tabs. In each of them (this file is linked into the others' projects), so any describes it.
// The tab's id is GamingTools: Video's Overlay tool has had the id Gaming since before it was a sub-tab.
[assembly: TabGroup("GamingTools", "Gaming", "", Order = 55,
    Description = "Button2Key: controller buttons and sticks to keys, macros and mouse moves; Button2Key Remote: screens of buttons on your phone, tablet or PC that do the same; ForzaWheel: your wheel, pedals, shifter and button boxes as one controller for Forza; iRacing: your races, in numbers and graphs; Calibration: your pedals', wheels' and sticks' axes calibrated in Windows")]
