# Daisy's App

## Working on one module

When the user names a module (and a sub-module), load only its row below: its README (what it does, and a **Code**
section listing its files) and its folder. Each module is its own project under `src/Modules/<Folder>/` and depends only
on `src/DaisysApp.Core` (tabs, settings, logs, translations; read only what the README's Code section names) and, for
Audio, Hardware, Video, SimEffects and Support, `src/DaisysApp.Shared`; for ForzaWheel, Button2Key, SimEffects and Devices, `src/DaisysApp.Controllers` (the one controller list). The shell (`src/DaisysApp`) and other modules aren't needed unless the
change is about the window, Settings → General or the module loader. Folder names that don't match the tab are marked ⚠.

| Tab → sub-tab | Folder (README.md inside) |
|---|---|
| Hardware → Performance · USB Monitor · Devices · Sensors | `src/Modules/Hardware/` → `Performance/` · `UsbMonitor/` · `UsbMonitor/Devices/` (in USB Monitor's README) · `Sensors/` |
| Audio → Volume · Levels · Delay | `src/Modules/AudioTools/` → ⚠ `Levels/` (Volume) · ⚠ `Tests/` (Levels) · `Delay/` |
| Video → Resizer · Mini Mirror · Overlay · Recording | Resizer is its own module `src/Modules/Resizer/`; `src/Modules/Video/` → `MiniMirror/` · ⚠ `Gaming/` (Overlay and Recording) |
| Gaming → Button2Key ("Butt2Key"; folder ⚠ `Joy2Key/`) · Button2Key Remote ("Butt2Key Remote") · ForzaWheel · iRacing · Devices (with Calibration since 4.6.7) · RGB | each its own module: `src/Modules/Joy2Key/` · `Butt2KeyRemote/` · `ForzaWheel/` · `iRacing/` · ⚠ `GameDevices/` (calibration: `Calibration/`) · `Rgb/` (the tab: `src/Modules/Gaming/README.md`) |
| Network → Zabbix | `src/Modules/Network/` → `Zabbix/` |
| Tools → File Sync · Disk Usage · DiskMark · Renamer · Notepad · Terminal · Screen | `src/Modules/Tools/` → `FileSync/` · `DiskUsage/` · `DiskMark/` · `Renamer/` · `Notepad/` · `Terminal/` · `Screen/` (shared code: `Shared/`; Notepad talks over the AI module's connection: Core's `Shell/RemoteChannels.cs`) |
| SimEffects → Profiles · Shaker · Arduino · Notify · Fanatec · AmbiLight · Overlays · Analysis | `src/Modules/SimEffects/` → `Profiles/` (the module's profiles and telemetry, `SimData`) · ⚠ `BassShaker/` · `Arduino/` (sketch in `Arduino/DaisysSim/`) · `Notify/` · `Fanatec/` · `AmbiLight/` · ⚠ `InputOverlay/` (Overlays: Inputs, Dash, Analysis' block widgets) · `Analysis/` (its Race Analysis, Forza Analysis until 4.5.0: `Sessions/`) (shared: `Telemetry/`, `Lights/`) |
| Support → Network Tools → Connection · Ping · Speed Test · Traceroute · Wi-Fi · Port Scan · Network Scan · Host Scan · Events · Stress Tests · Logs · Tools | `src/Modules/Support/` → `Networking/` (one README for its tools; a folder each: `Connection/` `Ping/` `SpeedTest/` `Traceroute/` `WiFi/` `PortScan/` ⚠ `DeviceScan/` (Network Scan) `HostScan/` `Events/`; probes shared by Port Scan and Host Scan: `Probes/`; the speed test is Core's `Shared/Network/`) · `StressTests/` (its own README) · `Logs/` · ⚠ `QuickTools/` (Tools) |
| AI | `src/Modules/AI/` (one README for the whole module) |
| HA (Home Assistant) | `src/Modules/HomeAssistant/` (one README) |
| Remote API (other devices: remote control ops, module channels, Button2Key Remote) | `API.md` (the only description of the protocol; clients: AI `Remote.cs`, Core `RemoteChannels`, `clients/android`, Daisy's App Mobile `D:\OneDrive\Code\Android\DaisysApp`) |
| Template (writing a module) | `src/Modules/Template/` |
| Settings → General, the window, tabs, updates | `src/DaisysApp/` (main `README.md`: Settings → General, Project layout) |

A release also touches `CHANGELOG.md`, the version line at the top of `README.md`, `src/Directory.Build.props`, and the
module's `lang/en.json` (`python tools/strings.py --prune`).

## Where the notes are (since 3.14.0)

Claude Code loads this file into every session on every turn, so it holds only what every session needs. A module's
session notes (past releases, decisions, what was tested, lessons, fixtures) are in **`CLAUDE.md` in its folder**
(`src/Modules/<Folder>/CLAUDE.md`; the shell, Core and app-wide work such as hotkeys, widgets, title bubbles and the
rig's crash hunts: `src/DaisysApp/CLAUDE.md`). Claude Code reads one only when it works in that folder. Write new
notes there, and keep each one short. What's waiting for the user to try is `docs/UNTESTED.md`. The words of every
request are in `REQUESTS.md` (since 3.14.1 the app appends each one itself: don't write or read it). `CHANGELOG.md` (≈140 KB) and
`README.md` are big: read only their top (a few dozen lines) to add a release or bump the version line.

## Session notes

- **UI text:** new modules and features get labels and short tooltips only, no explanations in the app.

- **Decisions:** UI text and docs say "module"; code identifiers (`IApplet`, `AppletGroup`…) stay. Requests go in `REQUESTS.md`. **Never stop DaisysApp.exe** (sessions run in its AI tab; memory `never-kill-daisysapp.md`): test with the DaisysDev copy. The Android client's APK goes on the DA release with `gh release upload` after the Release run, named `Button2Key-Remote-<versionName>.apk` so installed apps find it. A B2KR game switches it on only if it was off, and off only if the game switched it on (as ForzaWheel), so a hand-set state is never overridden. After publishing, wait for the Release run (`gh run watch <id>`) before saying it's out. **A release publishes only
  Daisy's App** (`DaisysApp-Setup-*.exe`; the user, 2026-10-29): no wrapper, no `Module-*.zip`/`Modules-*.json`, unless
  the user asks for them; then `gh workflow run release.yml --ref vX.Y.Z -f full=true` adds them (`build.ps1 -Full`).
- **Test fixtures and tools** (details in the modules' notes): DaisysDev (`%TEMP%\DaisysDev\DaisysDev.exe`,
  `%APPDATA%\DaisysDev`) is the copy to test with; DaisysDev2 is a second PC on localhost. Android toolchain in
  `%USERPROFILE%\Android` (see `src/Modules/Butt2KeyRemote/CLAUDE.md`). Never send keys or position-based clicks to
  DaisysDev windows the user might be typing near; select by UIA. The rig is GAMER11 (192.168.1.10, AI remote port 38863).
- **Shared code:** a module uses Core's pieces, never its own copy, and doesn't link a file into another project
  (move it to Core, or `DaisysApp.Controllers` for controller/Button2Key code): `Shared/Ui` (`Ui.Card`…, `Ui.Every`,
  `Ui.Brush/Pen/Typeface`, `Ui.Open/Copy`, `Ui.ProgramList`), `Sizes`, `HotkeyWindow.Button` for every shortcut
  button, `RunningPrograms` (+ `Watch`, `GameSwitch`), `DeviceChanges`, `Debounce`, `JsonStore.SaveSoon`, `DailyLog`,
  `Win32/Native`, `Network/Http`/`Pings`, `Audio/AppAudio`/`CoreAudio`, `Hotkeys/Joystick`, `Server/` (the list: the
  Template's README). A module's own pieces go in `SimUi`, `NetUi`, `HaUi` or `ZabbixUi`, which must call `Ui.`.
- **Binder** left DA in 3.37.4: now **Game Tether** (`C:\Users\ross\OneDrive\Code\Windows\GameTether`, own CLAUDE.md).
- **State:** 4.6.7 and earlier released (4.6.8: widgets start click-through, strip in rows: `src/DaisysApp/CLAUDE.md`; 4.6.7: Calibration folded into Devices, module removed: Devices' notes; 4.6.6: Notepad images: Tools'; 4.6.3–4.6.5: Analysis: SimEffects'; 4.1.0: Process Analyzer, server side briefed, not written:
  Support's notes; 4.0.0, the audit (all but 1, 3, 4): Core 0.55; what each item became is the status table in `docs/AUDIT-2026-11-03.md`, details `src/DaisysApp/CLAUDE.md`).
  Another session works in parallel (uncommitted: `Support/Gpu` and its lines, SimEffects' `en.json`, some
  CLAUDE.md; write shared files only from fully built text): check the last tag before picking a version; stage
  files by name, never a folder; a shared `en.json`: commit HEAD's blob plus your keys (`src/Modules/Support/CLAUDE.md`).
  APKs: Button2Key Remote 1.1.4 on 3.0.3; Daisy's App Mobile 0.2 on 3.5.4. Untracked `*-GAMER11.*` copies: not touched;
  they break local builds (workaround: `src/DaisysApp/CLAUDE.md` Lessons).
- **Next:** the user tries what's in `docs/UNTESTED.md` on the rig, newest first (first 4.6.7's Devices → Calibration on the pedals and PXN-HB).
  The audit's **Still to do** (`docs/AUDIT-2026-11-03.md`, top; tick off there): 1, 3, 4 each its own release (1 on
  the rig); 23–24 once `Support/Gpu` lands; 13's bonus only if asked. Offered, waiting: widget shortcuts (SimEffects),
  3.26.2's idle costs (`src/DaisysApp/CLAUDE.md`), AI's offers, 3.18.5's Mini Mirror lock (Video).
