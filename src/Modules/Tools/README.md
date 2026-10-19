# Tools

*A module of [Daisy's App](../../../README.md): the Tools tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Tools\` next to `DaisysApp.exe`.*

Three tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **File Sync** | Two folders kept the same, like FreeFileSync: **Mirror** (the destination exactly like the source) or **Update** (new and changed files only), Compare to see what would happen, and each sync in its own window with a speed graph, or in the background on the tab, so the app stays free | [FileSync/README.md](FileSync/README.md) |
| **Disk Usage** | What takes the space on a drive or in a folder, like WizTree and WinDirStat: the folders largest first, the file types, and a treemap; a whole NTFS drive in seconds | [DiskUsage/README.md](DiskUsage/README.md) |
| **Renamer** | Many files and folders renamed at once, like Bulk Rename Utility: its eleven numbered panels, in three columns you can rearrange (RegEx, Name, Replace, Case, Remove, Move/Copy, Add, Auto Date, Folder Name, Numbering, Extension), every new name shown as you go, and Undo | [Renamer/README.md](Renamer/README.md) |

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`: `FileSync.json`, `DiskUsage.json` and `Renamer.json`; `Tools.json`
remembers which tool was showing. Their log is `Tools-<date>.log`.

**Code:** `ToolsApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core); each tool's code is in its folder,
`FileSync/`, `DiskUsage/` and `Renamer/`, and what they share is in `Shared/`: `FastWalk` (a folder tree read with several threads,
FindFirstFileEx with large fetches), `Native` (Windows' calls: listing, copying with progress, the Recycle Bin, a
drive's raw reads), `Sizes` and `Ui`. The translations for all three are in `lang/`.
