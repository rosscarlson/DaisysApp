# Daisy's App

## Working on one module

When the user names a module (and a sub-module), load only its row below: its README (what it does, and a **Code**
section listing its files) and its folder. Each module is its own project under `src/Modules/<Folder>/` and depends only
on `src/DaisysApp.Core` (tabs, settings, logs, translations; read only what the README's Code section names) and, for
Audio, Hardware, Video and SimEffects, `src/DaisysApp.Shared`. The shell (`src/DaisysApp`) and other modules aren't needed unless the
change is about the window, Settings → General or the module loader. Folder names that don't match the tab are marked ⚠.

| Tab → sub-tab | Folder (README.md inside) |
|---|---|
| Hardware → Performance · USB Monitor · Devices · Sensors | `src/Modules/Hardware/` → `Performance/` · `UsbMonitor/` · `UsbMonitor/Devices/` (in USB Monitor's README) · `Sensors/` |
| Audio → Volume · Levels · Delay | `src/Modules/AudioTools/` → ⚠ `Levels/` (Volume) · ⚠ `Tests/` (Levels) · `Delay/` |
| Video → Resizer · Mini Mirror · Overlay · Recording | Resizer is its own module `src/Modules/Resizer/`; `src/Modules/Video/` → `MiniMirror/` · ⚠ `Gaming/` (Overlay and Recording) |
| Gaming → Button2Key ("Butt2Key"; folder ⚠ `Joy2Key/`) · Button2Key Remote ("Butt2Key Remote") · ForzaWheel · Forza Analysis · iRacing · Calibration · RGB | each its own module: `src/Modules/Joy2Key/` · `Butt2KeyRemote/` · `ForzaWheel/` · `ForzaAnalysis/` · `iRacing/` · `Calibration/` · `Rgb/` (the tab: `src/Modules/Gaming/README.md`) |
| Network → Zabbix | `src/Modules/Network/` → `Zabbix/` |
| Tools → File Sync · Disk Usage · Renamer · Notepad · Terminal · Logs | `src/Modules/Tools/` → `FileSync/` · `DiskUsage/` · `Renamer/` · `Notepad/` · `Terminal/` · `Logs/` (shared code: `Shared/`; Notepad talks over the AI module's connection: Core's `Shell/RemoteChannels.cs`) |
| SimEffects → Profiles · Shaker · Arduino · Notify · Fanatec · AmbiLight · Input Overlay | `src/Modules/SimEffects/` → `Profiles/` (the module's profiles and telemetry, `SimData`) · ⚠ `BassShaker/` · `Arduino/` (sketch in `Arduino/DaisysSim/`) · `Notify/` · `Fanatec/` · `AmbiLight/` · `InputOverlay/` (shared: `Telemetry/`, `Lights/`) |
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

- **UI text:** new modules and features get labels and short tooltips only, no explanations in the app (the user asked for it; the README explains).

- **Decisions:** UI text and docs say "module"; code identifiers (`IApplet`, `AppletGroup`…) stay. Requests go in `REQUESTS.md`. Sub-module switches apply at the next start. **Never stop DaisysApp.exe** (sessions run in its AI tab; memory `never-kill-daisysapp.md`): test with the DaisysDev copy. The Android client uses plain views without AndroidX so it stays tiny and simple to build; its APK goes on the DA release with `gh release upload` after the Release run (the updater only picks `*-Setup-*.exe` / `Module-*` by name), named `Button2Key-Remote-<versionName>.apk` so installed apps find it. A B2KR game switches it on only if it was off, and off only if the game switched it on (as ForzaWheel), so a hand-set state is never overridden. After publishing, wait for the Release run (`gh run watch <id>`) before saying it's out.
- **Test fixtures and tools** (details in the modules' notes): DaisysDev (`%TEMP%\DaisysDev\DaisysDev.exe`,
  `%APPDATA%\DaisysDev`) is the copy to test with; DaisysDev2 is a second PC on localhost. Android toolchain in
  `%USERPROFILE%\Android` (see `src/Modules/Butt2KeyRemote/CLAUDE.md`). Never send keys or position-based clicks to
  DaisysDev windows the user might be typing near; select by UIA. The rig is GAMER11 (192.168.1.10, AI remote port 38863).
- **State:** 3.18.4 and earlier all released (tag, Release run passed, 19 assets): 3.18.4 Mini Mirror Switch On With (programs per mirror / group switch it on and off; notes in Video); 3.18.3 Shaker Test beside the volume, Input Overlay a right-click widget with speeds between bars and wheel, ForzaWheel's start-with-app and bubble switches gone (notes in SimEffects, ForzaWheel); 3.18.2 the AI's title bubbles always last (`TitleBadges.All`); 3.18.1 SimEffects Play / Stop for the whole profile (+ shortcut; tools' ON grey while stopped), "Auto Start Modules" grid, Input Overlay speeds one size (notes in SimEffects); 3.18.0 SimEffects module-level profiles (Profiles sub-tab, Profile drop-down at the right of the sub-tabs; every tool's settings per profile) and one shared telemetry feed `Profiles/SimData` (notes in SimEffects; Core 0.42); 3.17.2 widgets' trash can asks with a red ✕, Performance tiles right-click Make Widget, Core 0.41 `Widget.Menu`/`DeleteButton` for every widget (notes in `src/DaisysApp/CLAUDE.md`), Titlebar Status gone from Input Overlay/Arduino/AmbiLight, AI "Back @ 2:10PM"; 3.17.1 Shaker's Titlebar Status checkbox removed, its bubble always shows (notes in SimEffects); 3.17.0 SimEffects → Input Overlay (ForzaWheel's overlay moved, game or device, speeds), the Shaker's Profile card shared with Switch On With Game (Overlay, Arduino, AmbiLight), Arduino ON/OFF (notes in SimEffects); 3.16.5 AI Compact after Release + Brief Replies (Settings → AI defaults, per session on a new tab, remote `new` fields; notes in AI); 3.16.4 AI compacting shows (status, chat line, ring after; notes in AI); 3.16.3 bubbles name the profile: "B2K: …", new "Shaker: …" (notes in Joy2Key, SimEffects); 3.16.2 AI tab renames reach every PC over the API (daisy_name, control rename); 3.16.1 AI title bubble per working session (click → its tab; Core 0.40), no install text in the Claude Account box, "Back in" right-aligned (notes in `src/Modules/AI/CLAUDE.md`); 3.16.0 Tools → Logs header sort/filter menus, Event column only on Windows logs (notes in `src/Modules/Tools/CLAUDE.md`). Another session works in
  parallel (it took 3.11.1, 3.12.7): check the last tag before picking a version. APKs: Button2Key Remote 1.1.4 on
  3.0.3; Daisy's App Mobile 0.2 on 3.5.4. Untracked OneDrive conflict copies `*-GAMER11.*` in the repo (not touched).
- **Next:** Daisy's App Mobile could send 3.16.5's `compact_after_release`/`brief_replies` on `new` (it gets the PC's
  defaults now). (offered, waiting on the user) a per-session Keep session notes switch; Daisy's App Mobile handling `daisy_name` (3.16.2) so a rename shows at once there. the user tries the latest releases on the rig (3.18.0/3.18.1 profiles with a real game: everything switches, the Shaker per profile, Play / Stop; 3.17.0 Input Overlay in a real game: iRacing/ACC steering and ACC clutch signs, Set on real pedals, Switch On With Game; AmbiLight slices and LED counts, clickable bubbles, 3.12.9
  crash hardening: if it still crashes, a full crash dump there); the full list is `docs/UNTESTED.md`. **Not started**
  (interrupted earlier): AI tab, a block above "Claude Account" listing active sessions on every remote PC, click to
  open one (needs a server op listing running sessions, and the client asking each of `RemoteSettings.Pcs`).
