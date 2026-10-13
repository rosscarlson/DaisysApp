using DaisysApp.Applets.Tools.DiskUsage;
using DaisysApp.Applets.Tools.FileSync;
using DaisysApp.Applets.Tools.Renamer;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools;

/// <summary>
/// Tools tab: three tools, each on its own sub-tab. <b>File Sync</b>: two folders kept the same (Mirror or Update), like
/// FreeFileSync. <b>Disk Usage</b>: what takes the space on a drive or in a folder, like WizTree and WinDirStat.
/// <b>Renamer</b>: many files renamed at once, like Bulk Rename Utility. Each keeps its own settings file.
/// </summary>
[Applet("Tools", "Tools", "", Order = 50,
    Description = "File Sync keeps two folders the same (mirror or update), Disk Usage shows what takes the space on a drive in seconds, and Renamer renames many files at once")]
public sealed class ToolsApplet : AppletGroup
{
    public const string FileSync = "FileSync", DiskUsage = "DiskUsage", Renamer = "Renamer";

    public ToolsApplet()
    {
        Add(FileSync, T("File Sync"), "", () => new FileSyncApplet());
        Add(DiskUsage, T("Disk Usage"), "", () => new DiskUsageApplet());
        Add(Renamer, T("Renamer"), "", () => new RenamerApplet());
    }
}
