using DaisysApp.Applets.Tools.DiskUsage;
using DaisysApp.Applets.Tools.FileSync;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools;

/// <summary>
/// Tools tab: two tools, each on its own sub-tab. <b>File Sync</b>: two folders kept the same (Mirror or Update), like
/// FreeFileSync. <b>Disk Usage</b>: what takes the space on a drive or in a folder, like WizTree and WinDirStat. Each
/// keeps its own settings file.
/// </summary>
[Applet("Tools", "Tools", "", Order = 50,
    Description = "File Sync keeps two folders the same (mirror or update), and Disk Usage shows what takes the space on a drive, in seconds")]
public sealed class ToolsApplet : AppletGroup
{
    public const string FileSync = "FileSync", DiskUsage = "DiskUsage";

    public ToolsApplet()
    {
        Add(FileSync, T("File Sync"), "", () => new FileSyncApplet());
        Add(DiskUsage, T("Disk Usage"), "", () => new DiskUsageApplet());
    }
}
