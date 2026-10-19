# Daisy's App

## Working on one module

When the user names a module (and a sub-module), load only its row below: its README (what it does, and a **Code**
section listing its files) and its folder. Each module is its own project under `src/Modules/<Folder>/` and depends only
on `src/DaisysApp.Core` (tabs, settings, logs, translations; read only what the README's Code section names) and, for
Audio, Hardware and Video, `src/DaisysApp.Shared`. The shell (`src/DaisysApp`) and other modules aren't needed unless the
change is about the window, Settings → General or the module loader. Folder names that don't match the tab are marked ⚠.

| Tab → sub-tab | Folder (README.md inside) |
|---|---|
| Hardware → Performance · USB Monitor · Devices · Sensors | `src/Modules/Hardware/` → `Performance/` · `UsbMonitor/` · `UsbMonitor/Devices/` (in USB Monitor's README) · `Sensors/` |
| Audio → Volume · Levels · Delay | `src/Modules/AudioTools/` → ⚠ `Levels/` (Volume) · ⚠ `Tests/` (Levels) · `Delay/` |
| Video → Resizer · Mini Mirror · Overlay · Recording | Resizer is its own module `src/Modules/Resizer/`; `src/Modules/Video/` → `MiniMirror/` · ⚠ `Gaming/` (Overlay and Recording) |
| Gaming → Joy2Key · ForzaWheel · Forza Analysis · iRacing | each its own module: `src/Modules/Joy2Key/` · `ForzaWheel/` · `ForzaAnalysis/` · `iRacing/` (the tab: `src/Modules/Gaming/README.md`) |
| Network → Zabbix · Logs | `src/Modules/Network/` → `Zabbix/` · `Logs/` |
| Tools → File Sync · Disk Usage · Renamer | `src/Modules/Tools/` → `FileSync/` · `DiskUsage/` · `Renamer/` (shared code: `Shared/`) |
| AI | `src/Modules/AI/` (one README for the whole module) |
| Template (writing a module) | `src/Modules/Template/` |
| Settings → General, the window, tabs, updates | `src/DaisysApp/` (main `README.md`: Settings → General, Project layout) |

A release also touches `CHANGELOG.md`, the version line at the top of `README.md`, `src/Directory.Build.props`, and the
module's `lang/en.json` (`python tools/strings.py --prune`).

## Session notes

- **Earlier (2026-10-19, 1.11.2–1.11.10, all released):** AI tab work (reopen closed tabs, remote control tweaks, collapsible tool cards, compact top, Hide details in usage, Claude's questions as an answerable card via `SessionView.AskQuestions` / `ToolCard.AskQuestions`, `SessionView.NeedYou` notifications); `Core/Logging/HangWatch.cs` (hang dumps to `logs\DaisysApp-hang-*.dmp`); `Core/Shared/LiveDataGrid.cs`; `Remote.cs` frame batching and traffic logging. The user's PC runs 1.11.10.
- **2.0.0 (71d8295; asked: per-module layout, Settings → General rework, fix the AI cards that looked expanded, publish as 2.0):**
  - Layout: the "Working on one module" map above; Code sections added to `ForzaAnalysis`, `Tools/FileSync`, `Tools/Renamer` READMEs; "Settings → General → Applets" → "Modules" in every README; Template README explains sub-module switches.
  - Settings → General: Modules card with sub-module switches; Tab names removed; Startup and System Tray + Updates side by side; Appearance, Notifications, Language one row; "applet" → "module" in UI text. Files: new `Core/Shell/ToolSwitches.cs`, `DaisysApp/Shell/ModuleTools.cs` (`AppSettings.ModuleTools`, `DisabledTools` "module/tool"), `Shell/SettingsPage.Modules.cs`; edited `AppletGroup.Add` (declare, skip if off; a separate overload with `needs` keeps the old signature for already-built modules), `AppletCatalog.Entry.Create(isOn)`, `App.xaml.cs`, `AppSettings.cs`, `SettingsPage.xaml(.cs)`, `AudioToolsApplet.cs`, `VideoApplet.cs` (Recording `needs: Overlay`).
  - AI cards: `ToolCard.SetInput` shows `OneLine(Summary())`, so a multi-line command no longer fills the title.
  - Tested with a renamed copy (`DaisysDev.exe`: own settings folder, logs and single-instance lock, so it runs beside the user's app): the page rendered as asked, and switched-off tools weren't loaded. It also showed a stale `modules/Gaming` DLL in `bin` (deleted; not in the install).
- **2.0.1 (asked: show the notes turn as one collapsed line, above the reply; keep requests word for word to search; Allow Everything red instead of the red line; publish 2.0.1):**
  - `ChatItems.cs` new `NotesGroup` (one clickable line; `Done()` lists the files changed). `SessionView`: `notesGroup` field, `Add` routes into it, `StartNotes` (inserted above the last reply if nothing came after it) / `EndNotes` (at `Result`, the next user message, end of history), remote `daisy_note` with the notes text starts one, a permission/question in it opens it.
  - `AiSettings.DefaultNotesMessage` now also has the AI append the request word for word to `REQUESTS.md` (a log, never condensed); `EarlierNotesMessages` recognises the 2.0.0 default in old transcripts and replaces it on `Load` if still in use.
  - Dashboard: `allowAllText` "Allow Everything", red + semibold when on (`ShowSkip`); `skipNote` removed.
 and docs say "module"; code identifiers (`IApplet`, `AppletGroup`…) stay, to avoid churn. Sub-module switches take effect at the next start, like modules. **Never stop DaisysApp.exe**: these sessions run inside its AI tab (killing it to test a dev build ended the session; memory `never-kill-daisysapp.md`), so UI checks are left to the user after updating.
- **State:** 2.0.1 committed, tagged and pushed (see git log). Built clean; the new UI isn't seen yet (never stop DaisysApp.exe to test). The user's settings get the new notes message on the next start, since theirs is the 2.0.0 default.
- **Next:** after publishing, wait for the Release run (`gh run watch <id>`) before saying it's out. Fix what the user reports from 2.0.0's Settings page and 2.0.1's notes line. Older follow-ups: on a hang, `dotnet-dump analyze` the .dmp (`clrstack` on thread 0); laptop traffic via the AI log's per-connection lines; the other PC's Smart App Control block (turn it off, or add code signing).
