using DaisysApp.Shell;

// The Gaming tab, shared by Joy2Key and ForzaWheel (each a module of its own since 1.1.68): when both are installed
// they're its two sub-tabs. In both modules (this file is linked into ForzaWheel's project), so either describes it.
// The tab's id is GamingTools: Video's Overlay tool has had the id Gaming since before it was a sub-tab.
[assembly: TabGroup("GamingTools", "Gaming", "", Order = 55,
    Description = "Joy2Key: controller buttons and sticks to keys, macros and mouse moves; ForzaWheel: your wheel, pedals, shifter and button boxes as one controller for Forza")]
