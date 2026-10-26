# Tools

*A module of [Daisy's App](../../../README.md): the Tools tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Tools\` next to `DaisysApp.exe`.*

Six tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **File Sync** | Two folders kept the same, like FreeFileSync: **Mirror** (the destination exactly like the source) or **Update** (new and changed files only), Compare to see what would happen, and each sync in its own window with a speed graph, or in the background on the tab, so the app stays free | [FileSync/README.md](FileSync/README.md) |
| **Disk Usage** | What takes the space on a drive or in a folder, like WizTree and WinDirStat: the folders largest first, the file types, and a treemap; a whole NTFS drive in seconds | [DiskUsage/README.md](DiskUsage/README.md) |
| **Renamer** | Many files and folders renamed at once, like Bulk Rename Utility: its eleven numbered panels, in three columns you can rearrange (RegEx, Name, Replace, Case, Remove, Move/Copy, Add, Auto Date, Folder Name, Numbering, Extension), every new name shown as you go, and Undo | [Renamer/README.md](Renamer/README.md) |
| **Notepad** | Notes in tabs shared between your PCs (since 3.0.0; tabs since 3.0.6): one Daisy's App hosts them and the others connect, over the AI module's remote control (its port and passphrase); what's typed on any shows on all at once; Bold, Italic, Underline; `---` drawn as a line; pops out into its own window; can be Windows' app for text files (Settings → Tools → Notepad) | [Notepad/README.md](Notepad/README.md) |
| **Terminal** | CMD, PowerShell and WSL sessions in tabs (since 3.1.7), each the real shell in Windows Terminal's own control, with Windows' console colours and font; + opens the default kind (Settings → Tools → Terminal), right-click a tab to rename it | [Terminal/README.md](Terminal/README.md) |
| **Logs** | Daisy's App's own logs, Windows' event logs and WSL's journal, each in a tab, with filters, copy, **Clear** (the app's logs; Confirm? first) and export of what's shown (on the Network tab before 3.12.9) | [Logs/README.md](Logs/README.md) |

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`: `FileSync.json`, `DiskUsage.json`, `Renamer.json`, `Notepad.json`, `Terminal.json` and `Logs.json`; `Tools.json`
remembers which tool was showing. Their log is `Tools-<date>.log`.

**Code:** `ToolsApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core); each tool's code is in its folder,
`FileSync/`, `DiskUsage/`, `Renamer/`, `Notepad/`, `Terminal/` (which brings NuGet's `CI.Microsoft.Terminal.Wpf`) and
`Logs/` (reading the logs Core writes), and what they share is in `Shared/`: `FastWalk` (a folder tree read with several threads,
FindFirstFileEx with large fetches), `Native` (Windows' calls: listing, copying with progress, the Recycle Bin, a
drive's raw reads), `Sizes` and `Ui`. The translations for all six are in `lang/`.
