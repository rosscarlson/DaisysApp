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

- **Asked and done (2026-10-19; each published as the next patch version):**
  - 1.11.2–1.11.7 (AI module): reopen closed tabs, remote tabs only disconnect, Pause/Stop/Send stacked, tool cards start collapsed (`ChatItems.cs` ToolCard), browse/make folders on the other PC (`Remote.cs`, `RemoteFolderWindow.cs`), compact top of the AI tab.
  - 1.11.8: `Core/Logging/HangWatch.cs` (dump to `logs\DaisysApp-hang-*.dmp` after 10 s not responding), `Core/Shared/LiveDataGrid.cs` for Performance's `ProcessGrid`, `Remote.cs` batches frames and logs traffic. 1.11.9: Hide details in the AI usage card (`UsagePanel.cs`).
  - 1.11.10 (72414d4): Claude's AskUserQuestion shown as a card to answer (`SessionView.AskQuestions` → `ToolCard.AskQuestions`; allow with `updatedInput` + `answers`, Skip = deny; never auto-answered), `SessionView.NeedYou` Windows notification when a session needs you; remote passes `answers`. The user's PC has updated to 1.11.10.
- **2.0.0 (asked: per-module layout; Settings → General rework; card titles; publish as 2.0):**
  - Layout: the "Working on one module" map above; Code sections added to `ForzaAnalysis`, `Tools/FileSync`, `Tools/Renamer` READMEs; "Settings → General → Applets" → "Modules" in every README; Template README explains sub-module switches.
  - Settings → General: Modules card with sub-module switches; Tab names removed; Startup and System Tray + Updates side by side; Appearance, Notifications, Language one row; "applet" → "module" in UI text. Files: new `Core/Shell/ToolSwitches.cs`, `DaisysApp/Shell/ModuleTools.cs` (`AppSettings.ModuleTools`, `DisabledTools` "module/tool"), `Shell/SettingsPage.Modules.cs`; edited `AppletGroup.Add` (declare, skip if off; a separate overload with `needs` keeps the old signature for already-built modules), `AppletCatalog.Entry.Create(isOn)`, `App.xaml.cs`, `AppSettings.cs`, `SettingsPage.xaml(.cs)`, `AudioToolsApplet.cs`, `VideoApplet.cs` (Recording `needs: Overlay`).
  - AI cards: `ToolCard.SetInput` shows `OneLine(Summary())`, so a multi-line command no longer fills the title.
  - Tested with a renamed copy (`DaisysDev.exe`: own settings folder, logs and single-instance lock, so it runs beside the user's app): the page rendered as asked, and switched-off tools weren't loaded. It also showed a stale `modules/Gaming` DLL in `bin` (deleted; not in the install).
- **Decisions:** UI text and docs say "module"; code identifiers (`IApplet`, `AppletGroup`…) stay, to avoid churn. Sub-module switches take effect at the next start, like modules. **Never stop DaisysApp.exe**: these sessions run inside its AI tab (killing it to test a dev build ended the session; memory `never-kill-daisysapp.md`), so UI checks are left to the user after updating.
- **State:** 2.0.0 committed, tagged and pushed (Release run started). The user's PC runs 1.11.10 until it updates.
- **Next:** check the 2.0.0 release finished; fix what the user reports from the new Settings page. Older follow-ups: on a hang, `dotnet-dump analyze` the .dmp (`clrstack` on thread 0); laptop traffic via the AI log's per-connection lines; the other PC's Smart App Control block (turn it off, or add code signing).
