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
| Gaming → Button2Key ("Butt2Key"; folder ⚠ `Joy2Key/`) · ForzaWheel · Forza Analysis · iRacing | each its own module: `src/Modules/Joy2Key/` · `ForzaWheel/` · `ForzaAnalysis/` · `iRacing/` (the tab: `src/Modules/Gaming/README.md`) |
| Network → Zabbix · Logs | `src/Modules/Network/` → `Zabbix/` · `Logs/` |
| Tools → File Sync · Disk Usage · Renamer | `src/Modules/Tools/` → `FileSync/` · `DiskUsage/` · `Renamer/` (shared code: `Shared/`) |
| AI | `src/Modules/AI/` (one README for the whole module) |
| Template (writing a module) | `src/Modules/Template/` |
| Settings → General, the window, tabs, updates | `src/DaisysApp/` (main `README.md`: Settings → General, Project layout) |

A release also touches `CHANGELOG.md`, the version line at the top of `README.md`, `src/Directory.Build.props`, and the
module's `lang/en.json` (`python tools/strings.py --prune`).

## Session notes

- **Earlier (2026-10-19, 1.11.2–1.11.10, all released):** AI tab work (reopen closed tabs, remote control tweaks, collapsible tool cards, compact top, Hide details in usage, Claude's questions as an answerable card via `SessionView.AskQuestions` / `ToolCard.AskQuestions`, `SessionView.NeedYou` notifications); `Core/Logging/HangWatch.cs` (hang dumps to `logs\DaisysApp-hang-*.dmp`); `Core/Shared/LiveDataGrid.cs`; `Remote.cs` frame batching and traffic logging. The user's PC runs 1.11.10.
- **2.0.0 (71d8295; per-module layout, Settings → General rework, AI card titles one line):** the module map above and Code sections in every README; Modules card with sub-module switches (`Core/Shell/ToolSwitches.cs`, `DaisysApp/Shell/ModuleTools.cs` → `AppSettings.DisabledTools` "module/tool", `Shell/SettingsPage.Modules.cs`, `AppletGroup.Add` declares and skips switched-off tools, Recording `needs: Overlay`); Tab names removed; cards in rows; `ToolCard.SetInput` uses `OneLine(Summary())`. Tested with a renamed copy `DaisysDev.exe` (own settings, logs, lock).
- **2.0.1–2.0.2:** notes turn as one collapsed line (`ChatItems.NotesGroup`, `SessionView.StartNotes`/`EndNotes`); `AiSettings.DefaultNotesMessage` appends requests to `REQUESTS.md`; Allow Everything red when on; title bar plan/limit bubble only while a local Claude session works (`PlanUsage.Working`).
- **2.1.0–2.1.1 (Hardware → Processes Network column, % of all traffic):** `Performance/NetTrace.cs` (ETW Kernel-Network send/recv per PID, fixed session GUID, `Grant(sid)` run elevated by `NetGrantTask` from Allow…, works without sign-out); `ProcessRow.Net`, `NetPermissionPanel`; `TracePermission.cs` in `DaisysApp.Shared/Hardware`.
- **2.1.2 (AI):** Notepad RTF paste no longer a black code block (`FlowMarkdown.MonoProse`, scratch doc gets the box's Foreground); small reply timestamps (`AssistantText.When`); Settings → AI 2×3 grid, preview full width.
- **2.1.3 (Mini Mirror frozen on first frame, a friend's PC):** `MonitorCapture.CheckStall` re-creates duplication after 2 s without frames (backing off to 30 s); Debug counts frames per monitor/mirror.
- **2.1.4 (Overlay Medium alignment):** `OverlayWindow.AddRow` numbers against the outer edge, graphs against the other; `WholeGb` totals ("11.5 / 16 GB").
- **2.2.0 (asked: Gaming → Joy2Key renamed "Button2Key" everywhere incl. the settings page, called "Butt2Key" in our talk; a profile's process as a list, not one field; publish as the next minor):** visible name only (`[Applet("Joy2Key", "Button2Key"…)]`, T() texts, message titles, bubble text, logs, docs); the id, folder, namespace, DLL, `Joy2Key.json`, `Joy2Key\Profiles` and the `TitleBadges` key stay, so settings, tab order and switches carry over. Games: `Joy2KeyView.Render` builds a line per program (× removes), a box + Add/Enter, Add a running program… → `AddGames` (adds .exe, skips duplicates, saves); `ProgramMenu` takes a refresh `Action`. `Programs` was already a list. Files: `src/Modules/Joy2Key/` (Joy2KeyApplet, Joy2KeyView, Joy2KeyEngine, Profiles, JoyToKeyImport, GamingTab, README, lang/en.json), Gaming/ForzaWheel/ForzaAnalysis/iRacing/Template READMEs, README.md, CHANGELOG.md, Directory.Build.props. Why ids stay: renaming the folder/files would need a settings migration and leave `modules\Joy2Key\` behind on installs. Commit 8641968.
- **2.2.1 (asked: Overlay Medium, the left column justified right against the graphs, which already line up; publish a patch):** `OverlayWindow.AddRow` beside mode: graph unchanged at the outer edge, numbers at `total - graphW - gap - tw` (left-aligned overlay) or `graphW + gap` (right-aligned); below (Advanced) unchanged. Video/Gaming README Medium row, CHANGELOG.
- **Decisions:** UI text and docs say "module"; code identifiers (`IApplet`, `AppletGroup`…) stay, to avoid churn. Requests go in `REQUESTS.md` (not CLAUDE.md, which every session loads). Sub-module switches take effect at the next start, like modules. **Never stop DaisysApp.exe**: these sessions run inside its AI tab (killing it to test a dev build ended the session; memory `never-kill-daisysapp.md`), so UI checks are left to the user after updating.
- **State:** 2.2.1 released (Overlay numbers against the graphs); 2.2.0 8641968 (Button2Key); 2.1.4 d96b277, 2.1.3 b7cf3db. 2.0.1 is installed on the user's PC; 2.0.2–2.2.0 haven't been seen running (never stop DaisysApp.exe to test). Red limit bubble also hidden when idle: offered to keep it always on.
- **Next:** Butt2Key: confirm the reading (nickname for our talk, app says Button2Key everywhere); if the user wants "Butt2Key" in the UI except the tab name, change the T() texts. Mini Mirror: the friend updates to 2.1.3, Logging on Debug, tries a desktop mirror (clock/video) and one over the game, sends `Video-<date>.log` + `DaisysApp-<date>.log` and GPU (hybrid laptop?), HDR, monitors, borderless vs exclusive. Read it: repeated "setting it up again" = stuck duplication (restarts cover it; look for a real cause); frames in but "0 frames drawn" = window/drawing side; no "Capturing \\.\DISPLAYn" = capture never starts. The user checks Button2Key (2.2.0: the name, the games list), the Medium overlay (2.2.1: numbers against the graphs) and 2.1.2 (Notepad paste as plain text, reply timestamps, Settings → AI two columns) and the Network column. After publishing, wait for the Release run (`gh run watch <id>`) before saying it's out. Older follow-ups: on a hang, `dotnet-dump analyze` the .dmp (`clrstack` on thread 0); laptop traffic via the AI log's per-connection lines; the other PC's Smart App Control block.
