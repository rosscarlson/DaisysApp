using DaisysApp.Applets.Tools.DiskMark;
using DaisysApp.Applets.Tools.DiskUsage;
using DaisysApp.Applets.Tools.FileSync;
using DaisysApp.Applets.Tools.Logs;
using DaisysApp.Applets.Tools.Notepad;
using DaisysApp.Applets.Tools.QuickTools;
using DaisysApp.Applets.Tools.Renamer;
using DaisysApp.Applets.Tools.Terminal;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools;

/// <summary>
/// Tools tab: eight tools, each on its own sub-tab. <b>File Sync</b>: two folders kept the same (Mirror or Update), like
/// FreeFileSync. <b>Disk Usage</b>: what takes the space on a drive or in a folder, like WizTree and WinDirStat.
/// <b>DiskMark</b>: a drive's speeds measured as CrystalDiskMark does. <b>Renamer</b>: many files renamed at once, like Bulk Rename Utility. <b>Notepad</b>: notes shared by several PCs.
/// <b>Terminal</b>: CMD, PowerShell and WSL sessions in tabs. <b>Logs</b>: Daisy's App's own logs, Windows' event logs and
/// WSL's (on the Network tab before 3.12.9).
/// <b>Tools</b>: buttons for Windows' resets and admin jobs (since 3.20.2).
/// Each keeps its own settings file.
/// </summary>
[Applet("Tools", "Tools", "", Order = 50,
    Description = "File Sync keeps two folders the same (mirror or update), Disk Usage shows what takes the space on a drive in seconds, DiskMark measures a drive's read and write speeds as CrystalDiskMark does, Renamer renames many files at once, Notepad shares notes between PCs, Terminal runs CMD, PowerShell and WSL in tabs, Logs shows Daisy's App's own logs, Windows' event logs and WSL's, and Tools has buttons for Windows' resets and admin jobs")]
public sealed class ToolsApplet : AppletGroup
{
    public const string FileSync = "FileSync", DiskUsage = "DiskUsage", DiskMark = "DiskMark", Renamer = "Renamer", Notepad = "Notepad", Terminal = "Terminal", Logs = "Logs", QuickTools = "QuickTools";

    public ToolsApplet()
    {
        Add(FileSync, T("File Sync"), "", () => new FileSyncApplet());
        Add(DiskUsage, T("Disk Usage"), "", () => new DiskUsageApplet());
        Add(DiskMark, T("DiskMark"), "", () => new DiskMarkApplet());
        Add(Renamer, T("Renamer"), "", () => new RenamerApplet());
        Add(Notepad, T("Notepad"), "", () => new NotepadApplet());
        Add(Terminal, T("Terminal"), "", () => new TerminalApplet());
        Add(Logs, T("Logs"), "", () => new LogsApplet());
        Add(QuickTools, T("Tools"), "", () => new QuickToolsApplet());
    }
}
