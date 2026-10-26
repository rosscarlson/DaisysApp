using DaisysApp.Applets.Tools.DiskUsage;
using DaisysApp.Applets.Tools.FileSync;
using DaisysApp.Applets.Tools.Logs;
using DaisysApp.Applets.Tools.Notepad;
using DaisysApp.Applets.Tools.Renamer;
using DaisysApp.Applets.Tools.Terminal;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools;

/// <summary>
/// Tools tab: six tools, each on its own sub-tab. <b>File Sync</b>: two folders kept the same (Mirror or Update), like
/// FreeFileSync. <b>Disk Usage</b>: what takes the space on a drive or in a folder, like WizTree and WinDirStat.
/// <b>Renamer</b>: many files renamed at once, like Bulk Rename Utility. <b>Notepad</b>: notes shared by several PCs.
/// <b>Terminal</b>: CMD, PowerShell and WSL sessions in tabs. <b>Logs</b>: Daisy's App's own logs, Windows' event logs and
/// WSL's (on the Network tab before 3.12.9).
/// Each keeps its own settings file.
/// </summary>
[Applet("Tools", "Tools", "", Order = 50,
    Description = "File Sync keeps two folders the same (mirror or update), Disk Usage shows what takes the space on a drive in seconds, Renamer renames many files at once, Notepad shares notes between PCs, Terminal runs CMD, PowerShell and WSL in tabs, and Logs shows Daisy's App's own logs, Windows' event logs and WSL's")]
public sealed class ToolsApplet : AppletGroup
{
    public const string FileSync = "FileSync", DiskUsage = "DiskUsage", Renamer = "Renamer", Notepad = "Notepad", Terminal = "Terminal", Logs = "Logs";

    public ToolsApplet()
    {
        Add(FileSync, T("File Sync"), "", () => new FileSyncApplet());
        Add(DiskUsage, T("Disk Usage"), "", () => new DiskUsageApplet());
        Add(Renamer, T("Renamer"), "", () => new RenamerApplet());
        Add(Notepad, T("Notepad"), "", () => new NotepadApplet());
        Add(Terminal, T("Terminal"), "", () => new TerminalApplet());
        Add(Logs, T("Logs"), "", () => new LogsApplet());
    }
}
