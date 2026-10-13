# Daisy's App

**Version 0.45.0** · [Download the latest release](https://github.com/rosscarlson/DaisysApp/releases/latest)

A tabbed Windows app that hosts small audio, video and hardware tools, called **applets**. Each applet is a tab and
can be switched on or off in Settings → General; right-click a tab to rename it, or drag it to move it (as in a
browser: it follows the pointer and the others slide out of its way). **Settings** is always the last tab. Each applet
is a separate **module** the app loads from its `modules` folder, with its own guide (linked below), so new ones are
easy to add — see [Project layout and modules](#project-layout-and-modules). Some hold several tools, each on its own
sub-tab (Hardware, Audio, Video, Network and Tools).

![Daisy's App](docs/screenshot.png)

| Tab | What it does |
|---|---|
| Hardware | Three tools on sub-tabs: **Performance** (live graphs of CPU, GPU, memory, video memory, disk, network and temperatures, per-core load, processes and system details, with a log and history windows), **USB Monitor** (real-time log of device connect / disconnect / status changes) and **Sensors** (every temperature, fan, voltage, power, clock and load sensor from LibreHardwareMonitor, with history graphs and a log) |
| Audio | Three tools on sub-tabs: **Volume** (volume and mute for every playback and recording device and every app, live, with volume up / down / mute shortcuts), **Levels** (test signals, per-speaker levels, microphone leveling, auto-level and EQ wizards, stored in Voicemeeter's bus EQ or Windows channel volume / Equalizer APO) and **Delay** (brings two Voicemeeter outputs into sync with output delay) |
| Video | Four tools on sub-tabs: **Resizer** (saved window sizes and positions per program, e.g. a game stretched over three monitors, applied by click, hotkey, tray, script or automatically), **Mini Mirror** (any part of the screen live in its own always-on-top window, e.g. a track map on another monitor), **Overlay** (what's running now and the FPS overlay) and **Recording** (the PC's GPU, CPU and memory load, screen recording with the graphics card's encoder, benchmarks, and each app's performance history) |
| Network | Two tools on sub-tabs: **Zabbix** (your Zabbix server's hosts as a grid of boxes: reachable or not, CPU, memory, problems; each host's page with Performance-style tiles, history windows, graphs of several values together, problems and every item; widgets for any of them) and **Logs** (Daisy's App's own logs, one a day for the app and each applet, Windows' event logs and WSL's journal, each in a tab, with filters, copy and export; marks where the app stopped without shutting down properly) |
| Tools | Three tools on sub-tabs: **File Sync** (two folders kept the same, like FreeFileSync: Mirror or Update, Compare to see what would happen, each sync in its own window with its progress) and **Disk Usage** (what takes the space on a drive or in a folder, like WizTree: the folders largest first, file types and a treemap; a whole NTFS drive in seconds) and **Renamer** (many files renamed at once, like Bulk Rename Utility: its numbered panels, a live preview of every new name, and Undo) |
| Joy2Key | Controller buttons, sticks, triggers and the POV hat pressing keys (held, tapped, repeated, toggled or long-pressed) or playing macros (keys in order with pauses), clicking, scrolling and moving the mouse, with a profile per game that switches with the game in front; imports JoyToKey's profiles |
| Settings | **General** (startup and tray, updates, applets on/off, theme, files and logging), then a page for each applet that has settings |

---

## Contents

- [Install](#install)
- [Voicemeeter](#voicemeeter)
- [Hardware](#hardware): Performance, USB Monitor, Sensors
- [Audio](#audio): Volume, Levels, Delay
- [Video](#video): Resizer, Mini Mirror, Overlay, Recording
- [Network](#network): Zabbix, Logs
- [Tools](#tools): File Sync, Disk Usage, Renamer
- [Joy2Key](#joy2key)
- [Settings](#settings)
- [Translations](#translations)
- [Files and command line](#files-and-command-line)
- [Updates](#updates)
- [Version history](#version-history)
- [Project layout and modules](#project-layout-and-modules)
- [Build and release](#build-and-release)
- [Third-party](#third-party)

---

## Install

1. Download `DaisysApp-Setup-x.y.z.exe` from the [latest release](https://github.com/rosscarlson/DaisysApp/releases/latest).
2. Run it. Windows asks for admin approval, because the app installs to `C:\Program Files\Daisys App`.
   The installer isn't code-signed, so SmartScreen may warn: choose **More info → Run anyway**.
3. Start **Daisy's App** from the Start menu.

Everything the app needs is included (.NET doesn't have to be installed). Installing a newer version upgrades in
place. Uninstall from Settings → Apps → *Daisy's App*; that closes a running copy and removes the sign-in entry.

---

## Voicemeeter

The Audio tab's **Levels** and **Delay** rely on **[Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/)**
(standard Voicemeeter lacks the per-channel bus EQ these tools use). A banner at the top of both appears whenever
Voicemeeter isn't installed (**Get Voicemeeter**), isn't running (**Start Voicemeeter**), or is the standard edition;
it disappears by itself, and the tab refreshes, once Voicemeeter is up.

Everything the tools set — speaker levels and output delays — is stored **in Voicemeeter's own settings**, so it stays
applied whether or not Daisy's App is running. Save / Load buttons on both keep a copy in a file too.

> **Why Voicemeeter?** Windows does have per-channel volume (Sound settings → device → Levels → Balance), and the
> Levels tool uses it for ordinary output devices. But Voicemeeter's virtual devices ignore it, and Voicemeeter
> usually drives the sound card in a way that bypasses it, so once audio goes through Voicemeeter, its per-channel EQ
> is the only place a level actually takes effect. Windows has no per-device delay at all.

---

## Hardware

Three tools, each on its own sub-tab:

- **Performance** — live graphs of CPU, GPU, memory, video memory, disk, network and temperatures, per-core load,
  processes and system details, with a log and history windows; network tests and a speed test (off until you switch
  it on; then once a day by default, 5 seconds each way).
  [src/Modules/Hardware/Performance/README.md](src/Modules/Hardware/Performance/README.md)
- **USB Monitor** — real-time log of device connect / disconnect / status changes, with a per-launch log file.
  [src/Modules/Hardware/UsbMonitor/README.md](src/Modules/Hardware/UsbMonitor/README.md)
- **Sensors** — every temperature, fan, voltage, power, clock and load sensor from LibreHardwareMonitor, grouped by
  hardware, with history graphs and a log of temperatures, fans and power.
  [src/Modules/Hardware/Sensors/README.md](src/Modules/Hardware/Sensors/README.md)

**How they work and how to use them: [src/Modules/Hardware/README.md](src/Modules/Hardware/README.md)**

---

## Audio

Three tools, each on its own sub-tab:

- **Volume** — volume and mute for every playback and recording device and every app, side by side and live, with
  volume up / down / mute shortcuts (keys or controller buttons) for any of them.
- **Levels** — test signals, per-speaker level knobs, the microphone's level and a live analyser in the middle of the
  speaker map while the signal plays (with the mic's calibration file), microphone leveling and an auto-level wizard, plus an EQ wizard
  that evens out each speaker's response in the room; levels and EQ stored in Voicemeeter's bus EQ (or Windows channel
  volume / Equalizer APO).
- **Delay** — brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker or VBAN stream) into sync with
  Voicemeeter's output delay.

**How they work and how to use them: [src/Modules/AudioTools/README.md](src/Modules/AudioTools/README.md)**

---

## Video

Four tools, each on its own sub-tab:

- **Resizer** — saved window sizes and positions per program (e.g. a game stretched over three monitors), applied by
  click, hotkey, tray, script or automatically (from Resize Rabbit).
  [src/Modules/Video/Resizer/README.md](src/Modules/Video/Resizer/README.md)
- **Mini Mirror** — any part of the screen live in its own always-on-top window: a track map, delta bar or HUD corner
  moved to another monitor (from the MiniMirror SimHub plugin).
  [src/Modules/Video/MiniMirror/README.md](src/Modules/Video/MiniMirror/README.md)
- **Overlay** — two squares the width of the window: what's running now (frame rate, 1% low, frame time) and the FPS
  overlay (off until you switch it on).
- **Recording** — **Performance** (the PC's GPU, video memory, CPU and memory, with the last two minutes' graphs)
  beside **Record** (a monitor, region or game with the graphics card's encoder: NVENC on NVIDIA cards; H.264, HEVC or
  AV1, in editable quality profiles), with **cameras** laid over the picture (any number of webcams, up to four,
  placed, sized, cropped, rounded and mirrored), and a **virtual webcam** (*Daisy's App Camera*) showing the same picture to
  Discord or any camera app, with the game's sound sent to a virtual cable for Discord's microphone; and
  **Benchmark**; under them, each app's **History**: every session's average, then its sessions listed beside
  the picked one's graph, with the values under the cursor.
  Both: [src/Modules/Video/Gaming/README.md](src/Modules/Video/Gaming/README.md)

**How they work and how to use them: [src/Modules/Video/README.md](src/Modules/Video/README.md)**

---

## Network

Two tools, each on its own sub-tab: **Zabbix** and **Logs** (before 0.39 Zabbix was a tab of its own and Logs the
last page of Settings). The module: [src/Modules/Network/README.md](src/Modules/Network/README.md).

### Zabbix

Your [Zabbix](https://www.zabbix.com) server's hosts at a glance, read-only: a box per host (whether Zabbix reaches it,
CPU and memory with the last hour's CPU, its problems in their severity's colour, its uptime), filtered by name, host
group or problems only. Click a host for its page: tiles like Performance's (CPU, memory, disks, network,
temperatures, ping), each opening the same history window (live, or up to 30 days from Zabbix's history and trends),
**New graph** to draw several values together (e.g. two network cards' traffic in and out), its problems, details and
every item. Any host's box, tile, graph or item can be a widget. The setup wizard asks for the server (address, port,
path, an API token or a user name and password) while it isn't set up, with **Skip** and **Switch off Zabbix** (which hides its sub-tab).

**How it works and how to use it: [src/Modules/Network/Zabbix/README.md](src/Modules/Network/Zabbix/README.md)**

### Logs

Daisy's App writes a log for itself and one for each applet, one file a day, kept for 7 days: **Normal** logging records
what they do and any errors, **Debug** (Settings → General → Logging) much more. **Logs** shows them, merged or
one applet at a time, next to Windows' own event logs (Application, System, Setup, what Windows recorded when the app
crashed, or any other) and a WSL distribution's journal, each in its own tab (**Add**), with a time range, level filter
and search. The level is coloured (errors red, warnings yellow, everything else green), and where the app started
again without having logged a clean exit, a red row marks the spot (**Next improper shutdown** jumps to each).
**Copy** puts entries on the clipboard; **Export…** saves them, or a zip of every log file to send in.

**How it works and how to use it: [src/Modules/Network/Logs/README.md](src/Modules/Network/Logs/README.md)**

---

## Tools

Three tools, each on its own sub-tab. The module: [src/Modules/Tools/README.md](src/Modules/Tools/README.md).

### File Sync

Two folders kept the same, like [FreeFileSync](https://freefilesync.org/): the source on the left, the destination
on the right (type, paste, **Browse…** or pick a recent one; a button swaps them). **Mirror** makes the destination
exactly like the source, deleting what's only there (to the Recycle Bin, by default); **Update** copies new and
changed files and deletes nothing. Tick what files must share to count as the same (name, size, date and time,
contents: the name alone when times are wrong), and leave names or paths alone.
**Compare** lists what a sync would do; **Sync** then opens the sync's own window, which lists what it's about to do
with a tick each (copy, make folders, update, delete), so you can leave anything out at the last moment, and warns
about anything risky before **Start**; then it shows its progress with **Pause** and **Stop**, while the app stays
free. Syncs can be saved by name and run from the tray icon's menu.

**How it works and how to use it: [src/Modules/Tools/FileSync/README.md](src/Modules/Tools/FileSync/README.md)**

### Disk Usage

What takes the space on a drive or in a folder, like [WizTree](https://diskanalyzer.com/) and WinDirStat: the folder
tree largest first (or sorted by any column) with each entry's share, size, size on disk, files, folders and last
change; the file types; and a cushion-shaded treemap of every file, coloured by type. With **Fast scan**, an NTFS
drive's file table is read straight from the disk (Windows asks for administrator permission), so a whole drive takes
seconds; other drives are read folder by folder, several at once. Right-click anything to open it, show it in
Explorer, copy its path, scan just it, or move it to the Recycle Bin.

**How it works and how to use it: [src/Modules/Tools/DiskUsage/README.md](src/Modules/Tools/DiskUsage/README.md)**

### Renamer

Many files and folders renamed at once, like [Bulk Rename Utility](https://www.bulkrenameutility.co.uk/): pick a
folder (and which files: a mask, subfolders, folders, hidden), tick the entries, and change the panels: **RegEx**,
**Name**, **Replace**, **Case**, **Remove**, **Move / Copy**, **Add**, **Auto Date** (modified, created, photo taken),
**Append Folder Name**, **Numbering** (decimal, hex, letters, Roman) and **Extension**, numbered and applied in Bulk Rename
Utility's order. The New name column shows every result as you type, in red where a name clashes or Windows won't take
it. **Rename**, then **Undo** if needed.

**How it works and how to use it: [src/Modules/Tools/Renamer/README.md](src/Modules/Tools/Renamer/README.md)**

---

## Joy2Key

Controller buttons, sticks, triggers and the POV hat pressing keys (held, tapped, repeated, toggled, or other keys on
a long press), macros (any number of keys in order, with pauses), clicking, scrolling and moving the mouse, running programs and switching profiles, like JoyToKey. Each
game gets a profile that's used while it's the window in front; press something on a controller to find its tile,
double-click the tile to say what it does. **Import from JoyToKey…** brings JoyToKey's profiles in.

**How it works and how to use it: [src/Modules/Joy2Key/README.md](src/Modules/Joy2Key/README.md)**

---

## Settings

**Setup wizard:** the first time the window opens after installing, and again after each update, a short setup asks
which tools to switch on, each with a one-sentence description, and lets you drag them into the order you want their
tabs in. After an update it says which version you're on, with **What's new**. Then come steps from the tools
themselves, for anything still to set up: **Gaming**'s, while Windows doesn't yet let the app count frames (**Allow…**
adds you to the *Performance Log Users* group, then you sign out and in or restart), with a switch for the FPS
overlay, which starts off. Changes restart the app. Run it again any time with **Run the setup wizard…** under
Settings → General → Applets. **Back** goes to the step before, on every step (the last one too), and a double-click on
**Next** doesn't skip a step.

**General** (first):
- **Startup and system tray** — keep running in the tray when the window is closed (on by default; right-click the
  tray icon to exit), start when you sign in to Windows, and start hidden in the tray.
- **Updates** — the installed version, check for updates when the app starts, **Check for updates** and
  **Release notes**.
- **Tab names** — rename any tab (or right-click a tab and type its new name: Enter saves, Esc cancels); Reset puts
  its own name back. The new name shows on the tab, its Settings page and the tray menu.
- **Applets** — every applet the app contains, each with an on/off checkbox and a one-line description. A switched-off
  applet isn't loaded at all (no tab, no settings page, nothing running in the background); its settings are kept for
  when it's switched back on. Changes apply after a restart: **Restart now** appears when there's one to apply.
  **Run the setup wizard…** opens the setup wizard.
- **Appearance** — Dark (default), Light, or System theme.
- **Language** — English, Español, Français or Português, plus any language someone has added (see
  [Translations](#translations)). Takes effect after **Restart now**.
- **Files** — open the settings and logs folders, and **Logging**: Normal or Debug (much more detail, for tracking
  down a problem); **View** opens Network → Logs.

Every setting is saved as soon as you change it (a **✓ Saved** note appears beside it); there's no Save button.

Then a page for each enabled applet that has settings, with a sub-tab per tool where it has several: **Hardware**
(**Performance**: the log; **USB Monitor**: log file on/off, open the log folder; **Sensors**: LibreHardwareMonitor's
address and setup guide, the sensor log), **Audio** (Levels' shortcut step, Voicemeeter EQ levels on/off, how far the Level Wizard may raise a speaker) and **Video**
(**Resizer**: process watcher speed, import from Resize Rabbit / Raccoon; **Mini Mirror**: new-mirror shortcut, HDR,
hide from screen capture, import from the SimHub plugin; **Overlay**: the overlay's look, the shortcuts, game names;
**Recording**: recording folder, audio and quality profiles, the performance history) and **Network** (**Zabbix**: on or off, the server and sign-in, how often to read, hidden hosts). Every page uses the
whole width of the window.

---

## Translations

Every piece of text in the app is in translation files, one per language, as plain JSON next to the program:
- `lang\` beside `DaisysApp.exe` holds the main window's and the Settings page's text.
- `modules\<applet>\lang\` holds each applet's (e.g. `modules\Hardware\lang\`).

Each file maps the English text to its translation, with `_language` naming the language in Settings:

```json
{
  "_language": "Español",
  "Run now": "Ejecutar ahora",
  "Next at {0:t}": "Próxima a las {0:t}"
}
```

**To add a language,** copy `en.json` in each `lang` folder to the language's code (`de.json`, `it.json`…), set
`_language`, and translate the text on the right. Keep the English on the left exactly as it is, and keep the `{0}`,
`{1}`… placeholders (the app fills in the numbers and names; they can move around in the sentence). Anything left out,
or a file that's missing, stays in English. The new language appears in Settings → General → Language.

For developers: code uses `T("text")`, `F("text {0}", value)` and `P(count, "{0} profile", "{0} profiles")`; XAML uses
`{l:Tr 'text'}`. `python tools/strings.py` rewrites every `en.json` from the source and reports what each other
language is missing (`--missing es` lists it, `--prune` drops texts no longer used).

---

## Files and command line

| Item | Location |
|---|---|
| Program | `C:\Program Files\Daisys App\DaisysApp.exe`, with the applets in `modules\<Name>\` beside it |
| Settings | `%APPDATA%\DaisysApp\` — `settings.json` (app), `Performance.json`, `UsbMonitor.json` and `Sensors.json` (Hardware), `AudioLevel.json`, `AudioLevels.json` and `AudioDelay.json` (Audio: Levels, Volume and Delay), `Resizer.json` (profiles and groups), `MiniMirror.json` (mirrors) and `Gaming.json` (Video), `Hardware.json`, `AudioTools.json` and `Video.json` (which sub-tab was showing), `Joy2Key.json` and `Joy2Key\Profiles\*.json` (one file per profile) |
| Saved levels and delays | `Documents\Daisy's App\` by default (`*.levels.json`, `*.delays.json`) |
| Recordings | `Videos\Daisy's App\` by default |
| Logs | `%LOCALAPPDATA%\DaisysApp\logs\`: the app's and each applet's log, one a day (`DaisysApp-10-10-26.log`, `Video-10-10-26.log`…, kept 7 days), USB events, `performance\` for the Performance and Sensors logs, `gaming\` for the performance history; the Audio wizards' runs are in `%APPDATA%\DaisysApp\Logs\` |
| Start at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysApp` |
| Update downloads | `%TEMP%\DaisysApp-Update\` |

On first run the Audio and USB Monitor tools import settings from the standalone MCAL and USB Mon apps they came from.

| Argument | Effect |
|---|---|
| *(none)* | Start, or bring the running copy to the front (only one copy runs) |
| `--tray` | Start hidden in the tray (used by start at sign-in) |
| `--exit` | Ask the running copy to exit cleanly (used by the uninstaller) |
| `--restart` | Wait for the running copy to close, then start (used by **Restart now**) |

---

## Updates

On launch (if enabled) and from **Check for updates** (Settings → General, or the tray menu) the app reads the latest
[GitHub release](https://github.com/rosscarlson/DaisysApp/releases). When it's newer, a banner offers **What's new**
and **Install update**; nothing installs until you click. The installer is downloaded, checked against the SHA-256
GitHub publishes for it, run silently (Windows asks for admin approval), and the app restarts on the new version.

**Quick update: double-click the Settings tab.** It checks, and if there's a newer version it downloads it (the banner
shows how far), closes the app, installs it with no windows and no questions, and starts the app again on the new
version, without the setup wizard (unless the wizard itself has something new; a tool's own first-run setup still
appears if something it needs is missing). If you're up to date it says so. No admin question: the installer registers
two tasks in Task Scheduler (folder *DaisysApp*) for the user installing it: **Update** runs the app's own updater with
that user's highest rights (it only ever installs the newest release it finds on GitHub itself, checked against its
SHA-256, so starting it can't make it run anything else) and **Start** starts the app again with ordinary rights.
Installs from before 0.44 don't have the tasks yet, so the first quick update asks for admin approval once; after
that it doesn't. Uninstalling removes the tasks.

---

## Version history

**0.45.0**
- **Audio → Levels:** the analyser in the middle of the speaker map is always shown: greyed out with *No Microphone*
  while the Microphone card is unticked, empty (saying what it shows) until the test signal plays, live while it does.

**0.44.0**
- **Quick update:** double-click the **Settings** tab to update now: the newest version is downloaded, installed with
  no windows and no admin question (through a Task Scheduler task the installer sets up), and the app starts again on
  it by itself, without the setup wizard (unless it has something new). The first time, from 0.43 or earlier, Windows
  asks for admin approval once.

**0.43.0**
- **Audio → Levels:** a **Microphone** card under *Output device*: tick it to see what the mic hears while the test
  signal plays: its level, and a live spectrum analyser in the middle of the speaker map (in the listener's place),
  corrected for the mic with its **Calibration file** if ticked (the same file the EQ Wizard uses). Unticked, it shows
  nothing and the mic stays closed; it's off while a wizard is open. The map's middle 3 × 3 cells are kept free of
  speakers for it (the 16 around them are the speakers'; any saved in the middle move to the nearest free cell).

**0.42.0**
- **Tools → File Sync:** **Compare** and **Sync** (was Synchronize…) are at the top right, beside the saved syncs.
  Compare can be clicked once both folders are filled in, and Sync once a comparison has found something to do (the
  sync uses it). The sync's window lists what it's about to do with a tick each (copy new files, make folders, update,
  delete files, delete folders), to leave any of it out at the last moment. The saved syncs list starts on *Saved
  syncs*, with **New…** to name a sync; **Save as…** is available once it's named.

**0.41.0**
- **Tools → Renamer** (new), like Bulk Rename Utility: a folder's files (and folders, subfolders, hidden ones, by a
  mask) with a tick each, and Bulk Rename Utility's panels in its order: RegEx, Name, Replace, Case (with exceptions),
  Remove (first/last, from–to, characters, words, crop, digits, symbols, accents, trim…), Move / Copy, Add (prefix,
  insert, suffix, word space), Auto Date (modified, created, photo taken, today), Append Folder Name, Numbering
  (start, step, digits, decimal, hex, letters, Roman, per folder) and Extension. Every new name shows as you type, in
  red when it clashes or Windows won't take it. Renames swap and change case safely; **Undo** puts the last rename back.
- **Tools → File Sync:** **Compare by** is now ticks: name (always), size, date and time, contents, in any mix, e.g.
  name and size when times are wrong. **Compare** is green.

**0.40.0**
- **Tools** (new tab) with two sub-tabs:
  - **File Sync**, like FreeFileSync: a source and a destination side by side (browse, paste or pick a recent one;
    swap them), **Mirror** (the destination exactly like the source, extra files to the Recycle Bin) or **Update**
    (new and changed files, nothing deleted), compare by time and size or by contents, a Leave alone list.
    **Compare** lists what would happen; **Synchronize…** opens the sync's own window: what's about to happen (with
    warnings, e.g. an empty source), then progress (speed, time left, **Pause**, **Stop**) while the app stays free.
    Files are copied under a temporary name and swapped in, so a stopped sync leaves no half files. Saved syncs, also
    run from the tray.
  - **Disk Usage**, like WizTree: the folder tree largest first (any column sorts), file types, and a cushion-shaded
    treemap. **Fast scan** reads an NTFS drive's file table directly (as administrator: Windows asks), so a whole
    drive takes seconds (2.3 million entries in under 4 s here); other drives are read folder by folder with several
    threads. Open, show in Explorer, copy the path, scan a folder, or delete to the Recycle Bin.
- **Setup wizard:** **Back** is there on the last step too, and a double-click on **Next** no longer skips a step.
- Typing in drop-down boxes that take text (e.g. Resizer's program box) shows what's typed; it didn't before.
- Core: `ModuleTasks` (a module's job run in a second copy of the app, as administrator if needed).

**0.39.0**
- **Network** (new tab) with two sub-tabs: **Zabbix** (was a tab of its own) and **Logs** (was the last page of
  Settings); everything in them works as before, and their settings are kept. Settings → Network has Zabbix's page.
  Updating moves Zabbix's place among the tabs to Network; if Zabbix was switched off, Network is on (for Logs)
  with Zabbix switched off.
- **Zabbix** is switched off and on by itself now (it's part of the Network module): **Switch off Zabbix** in its
  setup step, or **Use Zabbix** in Settings → Network → Zabbix. Off, its sub-tab and widgets go and nothing is read.
- Settings → General → Logging **View** opens Network → Logs.

**0.38.0**
- **Zabbix** (new tab): your Zabbix server's hosts as a grid of boxes (reachable or not, CPU, memory, problems,
  uptime; a host that's only pinged shows its response time). Click one for its page: Performance-style tiles (CPU,
  memory, disks, network, temperatures, ping) opening the same history windows (live, or up to 30 days, with peaks,
  a summary and Export), **New graph** to put several values in one unit on one graph (values in other units are
  greyed out), problems, details and every item. Hosts, tiles, graphs and items can be widgets. The setup wizard
  asks for the server while it isn't set up (Test connection, Skip for now, or Switch off Zabbix). Read-only;
  Zabbix 5.0 and later.

**0.37.0**
- **Hardware → USB Monitor:** problem devices show **bold red**, with what the code means and the device they most
  likely are (matched against the devices known to have worked: the same port, hub, vendor and product, serial or
  container), in a new **Likely device** column, the details page and the logs. Right-click a column header to
  filter it (a tick box per value) or sort it; **Reset filters** clears them.
- **Hardware → Devices** (new): every USB device known, from the USB Monitor and Windows' own records, with last
  connected, disconnected and working (status bold red when disconnected or failing); double-click for details.

**0.36.0**
- **Video → Recording, cameras:** moving and resizing snap to the picture's edges and the other cameras' edges, and a
  resize snaps to another camera's height or width, so side-by-side cameras match exactly.
- **Mute virtual webcam:** a button, a tray item and a shortcut (a key combination or a controller / button box
  button; Ctrl+Alt+F8 to start). The webcam keeps running and shows a VIDEO MUTED card instead of the picture.

**0.35.0**
- **Video → Recording, cameras:** resizing keeps a camera's shape (right-click it to untick **Lock aspect**);
  right-click also has **Rotate 90° / 180° / 270°**, **Flip horizontal** and **Flip vertical**. Rounded corners start
  at 20%, and a new camera's box takes its picture's shape.
- **Shape:** record (and show on the virtual webcam) as captured or the middle of it in 32:9, 21:9, 16:9, 4:3 or
  9:16 portrait, e.g. 5120 × 1440 at 16:9 records 2560 × 1440; the camera layout shows that shape.
- **Overlay while recording:** Simple shows a solid deep-red light; Medium the time and file size; Advanced also
  the bit rate, frames written and dropped, and the recording's cost (video encoder, GPU for the conversion, CPU).

**0.34.0**
- **EQ Wizard, Hear the difference:** after a run, play your own music (chosen once and kept; **Reset** to pick
  another) on all the speakers while the new EQ switches on and off every few seconds (5 to start, on a slider),
  with the speaker levels left alone. Then **Keep EQ**, **Start again** with other options, or **Discard EQ**.

**0.33.0**
- **Audio → Levels:** the speaker that's playing counts down the seconds until the next one, on its tile (Auto-cycle)
  and on its row in the Level and EQ Wizards.
- **Live spectrum:** the Level Wizard has a graph of what the mic hears, and the EQ Wizard draws it under the
  speaker's curves while it measures (pink noise reads flat; it's only to watch).

**0.32.0**
- **Audio → Levels:** the speaker that's making sound turns green, fading smoothly from one speaker to the next as
  Auto-cycle steps through them (and as the wizards measure).
- **Mic AGC:** a mic whose automatic gain control is on and won't switch off is flagged as soon as it's picked, and
  the wizards won't run with it; the start-loud-then-drop check is stricter (6 dB).

**0.31.0**
- **Audio → Levels:** the wizards switch off the mic's automatic gain control (AGC) while measuring, where the
  driver allows, and put it back after. If a speaker still starts loud and then drops and stays down (AGC's
  signature, common in USB headset adapters), the run stops and says so instead of "Couldn't hear" or failing
  on a squeezed, wrong level.

**0.30.0**
- **Video → Recording, cameras:** each camera box has handles on its corners and sides to resize it from there, and
  **Alt** + drag on a handle crops the camera's picture at that edge (the picture stays put; drag back out, or
  **Reset the crop**, to undo). Recordings and the virtual webcam show the crop.

**0.29.0**
- **Video → Recording, cameras:** **Add Camera** on the Record card lays one or more webcams (up to four) over the
  picture: placed and sized on a canvas of the capture monitor at its own resolution, with rounded corners and
  mirroring. Recordings include them.
- **Virtual webcam:** **Start virtual webcam** shows the same picture, cameras and all, as *Daisy's App Camera*, for
  Discord, Teams, OBS or a browser (30 fps, fitted inside 1920×1080). Its sound: the game's playback device can be
  sent to a virtual cable (Voicemeeter's, VB-CABLE), whose other end Discord takes as its microphone. The installer
  registers the camera with Windows (softcam, MIT).
- **History:** every session's average graph has its own row; under it, the sessions listed beside the picked
  session's graph, with its average on one line and its other figures on the next.

**0.28.0**
- **Video → Recording:** **Performance** (was GPU, CPU and memory) stays on the left, as tall as **Record** and
  **Benchmark**, now stacked on the right. **History** (was Performance history) lists an app's sessions on the left,
  short (*10/9 7:19 PM - 13 min - 95 FPS*), beside the graphs; clicking one shows its graph with its other figures (1%
  and 0.1% lows, GPU load, top temperature) centred above it. Hovering over either graph shows the values there.

**0.27.0**
- **Level Wizard:** new **Match by** choice (in the wizard and Settings → Audio), **Full range** by default: full-range
  pink noise read across the whole spectrum, as the ear and an SPL meter hear it, so a speaker with weak bass (a small
  centre) is turned up to match. Before, it always matched on 500 Hz–2 kHz only (still there as **Voice band**, the
  cinema standard), which left such a centre sounding several dB quiet. After a successful run, **Close** reads
  **Close & Save**.

**0.26.0**
- **Audio:** the sub-tabs are now **Volume** (was Levels), **Levels** (was Tests) and **Delay**.
- **Level Wizard:** no more false "the microphone's level jumped" on the subwoofer (and rears) with longer measuring
  times. A steady sub's noise, mostly a few octaves at the very bottom that the room's resonances narrow further,
  naturally swings by 5–9 dB from one half second to the next (an SPL meter's one-second averaging hides it), more
  the longer it's measured. Each speaker is now judged in the band that's read (sounds outside it don't count); the
  sub on one-second slices, leaving out its loudest and softest. The wizard also catches noise suppression, which
  learns a steady sound and fades it by 10 dB or more after a second or two, and says so. The run log shows both.

**0.25.0**
- **Tray menu:** the tools of a tab with sub-tabs each get their own item at the top level (Resizer, Mini Mirror,
  Overlay, Recording), rather than a submenu inside Video's.
- **Video:** Gaming is now two sub-tabs: **Overlay** (Now and FPS overlay) and **Recording** (GPU, CPU and memory,
  Record, Benchmark, and Performance history); Settings → Video splits the same way. The squares use the whole width
  of the window, two to a row.

**0.24.0**
- **Audio → Tests, Level Wizard:** the louder speakers still come down to the softest, but when one would have to be cut
  by more than 10 dB, the softest is raised instead (and every speaker with it, so they stay matched) by up to a limit
  you set in Settings → Audio → **Level Wizard** (6 dB by default), before the others are cut further. Before, it raised
  them as far as it took. Speakers still too far apart are listed as out of range, with what to do.

**0.23.0**
- **Logs** moved from a tab of its own to the last page of Settings (after Video).
- **Settings:** every page uses the whole width of the window.
- **Performance:** the tiles are **RAM**, **VRAM**, **CPU Cores** and **Temps**. **Network Tests**' graph goes from 0 to
  50 ms; each host has a **Max** (50 ms) and a **Red for** time (15 s): an answer slower than its Max turns its line
  bold and red until it has been back under it that long. All of it, and the graph's top, is set in the card's gear.
  **Speed Test**'s scale reads 2.5 Gb, 1.8 Gb, 1.2 Gb, .6 Gb; the server and the data used moved from the card to an
  **Information** group at the bottom of its gear.

**0.22.0**
- **Video** (new tab) holds **Resizer**, **Mini Mirror** and **Gaming** as sub-tabs, and **Hardware** (new tab)
  **Performance**, **USB Monitor** and **Sensors**, each tool as it was; their settings pages are sub-tabs of Settings →
  Video and Settings → Hardware. Each tool keeps its settings, and the tabs' order, on/off state and last tab open move
  over by themselves. The installer removes the six old module folders.
- **Tabs:** drag one to move it, as in a browser: it follows the pointer and the others slide out of its way (no more
  press and hold).
- **Setup wizard:** runs after each update too (saying which version you're on, with **What's new**), and tools can
  add a step. **Gaming**'s, until Windows lets the app count frames: **Allow…**, then sign out and in (or restart),
  and whether to show the FPS overlay.
- **Gaming:** the FPS overlay is off on a new install. The tab is now squares, two to a row: Now, a new **GPU, CPU and
  memory** square (GPU load and video memory moved here from Now, with CPU and memory, and graphs of the last two
  minutes), FPS overlay, Record and Benchmark; **Performance history** (was Game history, now by **App**) stays under
  them, the whole width.
- **Logs:** only the Level column is coloured: errors and crashes red, warnings yellow, everything else green. Where
  the app started again without having logged a clean exit, a red row marks the spot, with when that run started and
  its last entry just below; **N improper shutdowns** selects each.
- **Audio Tools** is now just **Audio**, and **Joy 2 Key** is **Joy2Key**.
- **Speed Test** is off on a new install; when switched on it runs once a day by default, for 5 seconds each way.

**0.21.0**
- **Logging:** the app and every applet now keep a log, one file a day each (`DaisysApp-10-10-26.log`,
  `AudioTools-10-10-26.log`…) in `%LOCALAPPDATA%\DaisysApp\logs\`, deleted after 7 days. **Normal** records what they
  do (startup with each applet's time, devices, profiles, mirrors, recordings, wizards, settings changed, update
  checks) and every error; **Debug** (Settings → General → Logging) adds much more, including every exception thrown
  and every settings file saved. They replace `errors.log`. After an unexpected close, the next start notes it and
  copies in what Windows recorded about the crash (for a .NET crash, the error and where it happened).
- **Logs** (new tab): Daisy's App's logs (all applets merged, or one), Windows' Application, System and Setup logs, what
  Windows recorded about the app's crashes, any other Windows event log, and each WSL distribution's journal, each in
  its own tab (**Add**), remembered. Time range, level filter and search; the selected entry in full below. **Copy**
  (to the clipboard) and **Export…** (text, CSV, or a zip of every log file to send in).

**0.20.1**
- **Audio Tools:** a single jump in the mic's level (a noise in the room, a glitch) no longer stops the Level or EQ
  Wizard: that speaker is measured again, and the run stops only if it jumps all 3 times. The log has every half-second
  level of a rejected recording.
- **EQ Wizard:** a speaker that gets no filters says why: the range it was corrected over, and, for a small speaker,
  below which frequency it falls short of the target's bass (which the EQ doesn't boost). Fixed the EQ adding narrow
  cuts at the bottom of a speaker's range to undo the spill of a bass boost just above it.

**0.20.0**
- **Audio Tools:** the Level and EQ Wizards check that the mic's signal is real before trusting it, and stop with an
  explanation (putting the levels or EQ back) when it isn't: a quiet room recorded as digital silence, a steady test
  noise swinging by more than 10 dB, speakers still more than 3 dB apart after the last pass, or an EQ check that
  doesn't match the EQ. The diagnostics from 0.19.4 showed all of these: the mic's sound processing (noise
  suppression and echo cancellation) was removing the test noise, which 0.19.5's raw mode turns off.
- Both wizards show a warning when the mic can't be read raw.

**0.19.5**
- **Audio Tools:** the mic is opened in Windows' raw mode, without the sound card's noise suppression, automatic gain
  and echo cancellation, which made the Level and EQ Wizards' readings wrong on analog mic inputs (echo cancellation
  removes part of the test signal). If a mic can't give its raw signal, the status line says how to turn the
  processing off. On a stereo input the mic is read from the louder channel.
- **Level Wizard:** each speaker is measured for as long as Auto-cycle gives it (its seconds on the main screen)
  instead of always 2 seconds; every run is logged, and it has **Export diagnostics…** too.

**0.19.4**
- **Audio Tools, EQ Wizard:** every run is logged (settings, mic, the EQ as written, each recording and a table per
  speaker of what was heard, the response and the filters), and **Export diagnostics…** zips the last runs to send in.

**0.19.3**
- **Audio Tools, EQ Wizard:** no longer leaves speakers sounding thin. The target is now a proper in-room curve by
  default (the bass rising to +6 dB below about 150 Hz, the treble falling gently above 1 kHz) instead of flat, and
  only the narrow peaks in the bass are cut fully: the broad shape is cut by at most 3 dB, as a single mic position
  overstates the bass the room adds. Run the wizard again to replace an EQ made with 0.19.2 or earlier.

**0.19.2**
- **Performance:** the Network Tests and Speed Test graphs have a value scale and times, and show each line's value
  under the mouse; the "Last 5 minutes · top of the graph…" line under the pings is gone.
- **Joy 2 Key:** the input tiles are two lines (the input and what it does) and a little narrower.

**0.19.1**
- **Gaming:** the Advanced overlay (and the tab) shows the game's loaded upscaling and frame generation libraries and
  their versions (DLSS, DLSS Frame Gen, Ray Reconstruction, FSR, XeSS, Reflex). Settings → Gaming can switch on NVIDIA's
  own DLSS indicator, which shows the DLSS mode and render resolution in games.

**0.19**
- **Gaming → Benchmark:** measure a run of the game in front, for a set time or until it's stopped, started and stopped
  with a shortcut (Ctrl+Alt+F12) or from the tab. Each run is saved with an editable name; **Benchmarks…** shows a
  report for each, and compares runs of the same game and length side by side, with the differences and their frame
  rates on one graph.

**0.18**
- **Setup wizard:** runs once for each user, the first time the window opens after installing or updating: a welcome,
  then every tool with an on / off switch and a one-sentence description, dragged up and down to set the order of the
  tabs, then a summary (restarting if anything changed). Settings → General → Applets has **Run the setup wizard…**.
- Modules can now have their own first-run setup, run after the app's (none use it yet; see the Template's README).

**0.17.2**
- **Joy 2 Key:** the profile selected in the list is now the one in use (as in JoyToKey). Before, selecting a profile
  only showed it and the **Use this profile** button had to be clicked too, so a profile that was set up but not
  "chosen" sent nothing. On updating, the profile that was showing becomes the one in use.
- **Joy 2 Key:** the tab shows the last key it sent and when, and says when it's paused (while the tab is in front).
- **Joy 2 Key:** an error no longer stops it until the app restarts: it's logged and it carries on.

**0.17.1**
- **Tabs** can be put in any order: press and hold one, then drag it. (Settings stays last.)
- **Audio Tools → Levels:** how much a shortcut press changes the volume moved to Settings → Audio Tools.
- **Joy 2 Key:**
  - The left column stays put while the controllers on the right scroll: at its top the profile you're looking at
    (its games and options), then the profiles (with **Import**, which opens the JoyToKey import, next to **Delete**),
    then on / off and auto-switching.
  - Each controller's box can be folded up (the arrow next to the bin), and removing one always asks first.
  - The input tiles are half as wide.
  - **+ Add another key after it (makes a macro)** under the keys of an input, the quick way into a macro.
  - The JoyToKey import has a new step: which controller each of JoyToKey's numbered joysticks is, to check and
    change before anything's brought in.

**0.17**
- **Joy 2 Key:** macros. An input can play any number of keys one after another (**+ Add a key**, reorder with the
  arrows), each a single key or a combination, with the same pause between all of them (e.g. 0.5 seconds) or a pause
  set after each, once per press or over and over while it's held.

**0.16**
- New **Joy 2 Key** tab, a first draft of a JoyToKey replacement: profiles per game (switching with the game in
  front), a tile per button / axis direction / POV direction of each controller that lights up when it's used, and
  for each input keys (up to four together; held, tapped for a set time, repeated, toggled, or other keys on a long
  press), mouse movement, clicks and the wheel, running a program or switching profile. Right-click a keys box for
  every key by group (number pad, F13–F24, left / right modifiers, media, browser, mouse…). Xbox-style pads are read
  through XInput, numbered as JoyToKey numbers them. **Import from JoyToKey…** brings its `.cfg` profiles in, and
  lists what couldn't come across.

**0.15**
- **Audio Tools** now holds three tools on sub-tabs: **Levels** (was the Audio Levels tab), **Tests** (what was Audio
  Tools) and **Delay** (was the Audio Delay tab). Their settings and shortcuts are kept. Levels no longer has the
  explanation under its columns.

**0.14**
- **Performance widgets:** a history window's **Widget** button pops its graph out into a small window to keep anywhere
  on the screen: drag and stretch it, set its opacity, stay on top and time span; right-click to change it. A new
  **Widgets** card under Processes lists them, to change or delete.
- **Performance:** press and hold a tile to drag it to a new place; click a graph's scale to set its top (clocks now
  fit closer to the readings too: a 6.1 GHz CPU tops out at 7, not 10); on a PC with several graphics cards, pick which
  the GPU window graphs, each in its own colour; the history window's Summary is folded away until you click it.

**0.13**
- **Audio Leveler** is now called **Audio Tools** (its settings are kept).
- **Audio Levels:** playback devices, recording devices and a new **Apps** column (each program's own volume, as in
  the volume mixer) side by side. Click any name to give it volume up, volume down and mute shortcuts — keys or
  controller / wheel buttons — with a step you set (10 by default).
- **Mini Mirror groups:** name a set of mirrors (e.g. one game's), give the group a show / hide shortcut, and drag
  mirrors into it. New mirrors go at the top.
- **Performance:** the log is kept a year by default now (it's about 50 MB a year), or 2 years or forever; the tiles
  and live graphs pick up the last 10 minutes from it after a restart. The Speed Test graph has a fixed top (2,500
  Mbit/s by default, in its settings), Data used explains itself when you hover over it, and the Network Tests and Speed
  Test graphs are a third shorter.
- **Gaming:** **Browse Folder** (was Open the folder) and a **Settings** button beside it that opens Settings → Gaming at
  the recording options. Applets can now open their own Settings page.

**0.12**
- The tabs (and the Settings page's tabs) stay on one row however many there are, and scroll sideways when they don't
  fit: drag the bar under them or use the mouse wheel over them. The open tab is always scrolled into view.
- Settings save as soon as you change them, and now say so: a green **✓ Saved** appears beside the checkbox, option,
  list, slider or text box you changed, then fades.

**0.11**
- **Sensors**, a new tab: LibreHardwareMonitor's sensors (until now at the bottom of Performance) with their history
  windows, the setup guide and the sensor log, now with a legend in the history graphs. Settings → Sensors has the web
  server's address and the log. The connection to LibreHardwareMonitor is shared, so Performance's Temperatures tile
  still shows the CPU. Existing settings and sensor history carry over.
- **Rename any tab:** right-click it and type (Enter saves, Esc cancels), or Settings → General → Tab names.
- Performance: **Speed Test** shows download, upload and latency side by side, with the last test's time, server, data
  used and the next test aligned underneath; the caption under the graph is gone. **Storage** moved below Speed Test.

**0.10**
- **Gaming**, a new module:
  - **FPS overlay** over games: Simple (the frame rate), Medium (frame rate, frame time and video memory with small
    graphs) or Advanced (adds the 1% / 0.1% lows, GPU, CPU and memory, bigger graphs). Drag to move, drag a corner to
    scale, lock it so clicks go through; left or right justified; background colour and opacity; text colour.
  - **Recording** of a monitor, a region or a game to MP4 with the graphics card's encoder (NVENC, like the NVIDIA app),
    H.264, HEVC or AV1, in editable High / Medium / Low profiles (up to 500 Mbit/s); the PC's sound and optionally the
    microphone; SDR or HDR10 on HDR monitors.
  - **History:** each game's frame rates and hardware logged a second at a time, with per-game session graphs.
    Games are recognised by themselves and can be given friendly names.
  - Shortcuts for recording, showing the overlay and changing its mode.
  - Frame rates come from Windows' event tracing, like PresentMon: nothing is injected into games. Needs one-time
    membership of Windows' Performance Log Users group (the tab offers to add you).
- Building one module on its own (not the whole solution) now puts it in the app's Debug `modules` folder.

**0.9**
- **Modules:** each applet is now its own module, installed in its own folder under `modules\` next to the app with its
  translations and its README. The app loads every module it finds there (switch them on or off in Settings → General
  → Applets as before), so modules can be added without changing the rest. Shared code is in DaisysApp.Core.
- **Template** module: a hello-world example, installed but off, whose README is the guide to writing a module.
- Each applet's documentation moved from this README into its module's README (linked from its section here).

**0.8**
- **Languages:** the whole app is translatable, with Spanish, French and Portuguese included. Pick one in Settings →
  General → Language. Translations are plain JSON files in each module's `lang` folder, so anyone can add a language.
- Update banner: "Running version" instead of "You have".
- Performance: **Speed Test** uses the nearest speedtest.net server (Cloudflare's had started refusing with "too many
  requests"), falling back to Cloudflare. Click a host in **Network Tests** to highlight its line in the graph.
- The tab strips wrap onto a second row when the tab names don't fit.

**0.7.3**
- Performance: fixed games stuttering while the Performance tab was open (even minimized, and with the network cards
  off). Since 0.7.1 the process list grew to match the taller side column, showing about three times as many rows,
  all redrawn every second, which doubled the app's CPU use. It's back to its 0.7 height.

**0.7.2**
- Performance: **Network Tests** and **Speed Test** each have an on/off checkbox; off, the card dims and sends nothing
  at all.
- **Speed Test** has its own gear: schedule (every 5 minutes to once a day), test length from 1 second (timed from when
  data starts arriving) and orange / red warning levels for download, upload and latency. A test fails, red, after 10
  seconds without data or an answer, and failures are kept in the history.

**0.7.1**
- Audio Levels: click a volume's number to set it to 100.
- Performance: **Network tests** (ping each host once a second, graphed; hosts set with the gear) and **Speed test**
  (download and upload on a schedule, every 10 minutes by default, graphed over 24 hours) between Storage and System.

**0.7**
- Audio Leveler: **EQ Wizard**. Measures each speaker's response with a mic (with its calibration file, e.g. the
  iMM-6's) and sets a per-speaker EQ, in Voicemeeter's bus EQ or through Equalizer APO. Levels files now keep the EQ
  too.

**0.6.4**
- Mini Mirror: fixed a crash a few seconds after creating a mirror on an HDR display (the HDR capture call was made
  with bad arguments). Mirrors that were saved also crashed the app at every launch.
- Mini Mirror can no longer stop the app from opening: if screen capture crashed the last run, the mirrors aren't
  started (and HDR conversion is switched off) until you click **Start mirrors** on the Mini Mirror page.
- Error log: Settings → General → Files → **Error log** opens it. It now also records errors on background threads,
  and a note when the app closed unexpectedly last time.

**0.6.3**
- Performance is the first tab and USB Monitor the second.

**0.6.2**
- Performance: warning levels — tiles turn orange / red, over-limit processes are highlighted, levels set from each
  history window's **Warnings** button and the process list's gear (with the list's refresh rate, down to 0.5 s);
  levels drawn on the graphs; CPU cores tile moved to the start of the second row; legends back on the graph's title
  line; process groups indented; fixed the process list not keeping its sort order (it looked frozen).

**0.6.1**
- Performance: processes split into Apps and Background processes; RAM / VRAM columns; GPU engine column; centred
  headers; Storage above System; the Disk tile shows the busiest disk (an average over all disks hid one disk being
  flat out); history windows: clickable legend highlights a line, CPU and GPU power on one graph, the summary always
  in view, a Close button.
- Performance: every LibreHardwareMonitor sensor in a **Hardware sensors** section (grouped by hardware, filtered by
  kind, value / min / max, history per sensor or per piece of hardware); temperatures, fans and power logged; a **Get
  more sensor data…** banner and setup guide when it isn't running; its address in Settings → Performance.

**0.6.0**
- New **Performance** applet: live tiles for CPU, GPU, memory, video memory, disk, network, temperatures and CPU
  cores; process list; system and storage details; a background log with history windows (date ranges, peaks,
  summary, busiest processes, CSV export); CPU temperature from LibreHardwareMonitor, GPU sensors from NVIDIA's driver.
- Mini Mirror: fixed an error that could appear while picking a region.

**0.5.0**
- New **Audio Levels** applet: a live volume slider and mute for every playback and recording device.

**0.4.0**
- Audio Leveler: the speaker map is a grid (5 × 5 by default, set in Settings → Audio Leveler) and speakers can be
  dragged anywhere on it to match the room, saved per device; the microphone is chosen in the wizard (the Microphone
  card is gone from the page); **Auto-level…** is now **Level Wizard**; Load / Save / Reset levels are icon buttons.
- Mini Mirror: **Ctrl+Shift+F8** starts a new mirror without leaving the game (the selection never takes the focus,
  so games don't pause); HDR monitors are converted to SDR so mirrors aren't washed out.
- The window title shows the version (Daisy's App v0.4).
- Global shortcuts no longer use a hidden window (which could take the focus from a game).

**0.3.0**
- New **Resizer** applet, ported from Resize Rabbit: window size/position profiles and groups, global shortcuts,
  tray menu, script / Stream Deck pipe (same name as Resize Rabbit's), process watcher, import from Resize Rabbit and
  Resize Raccoon.
- New **Mini Mirror** applet, ported from the MiniMirror SimHub plugin (no SimHub needed): live always-on-top mirrors of
  any screen region (rectangle or circle, across monitors), window size and zoom, opacity, frame rate, lock,
  click-through, duplicate, Alt-drag snapping, show / hide shortcuts that can be a keyboard combination or a wheel /
  controller button, tray menu, optional hiding from screen capture, import from the SimHub plugin.
- Applets can add their own submenu to the tray icon's menu.
- Global shortcut handling (and the shortcut box, now with controller buttons) moved to `Shared/Hotkeys/`.

**0.2.2**
- Applets: each applet lives in its own folder under `src/DaisysApp/Applets/`, is found automatically, and can be
  switched on or off in Settings → General → **Applets** (with **Restart now**). Code shared between applets moved to
  `src/DaisysApp/Shared/`.

**0.2.1**
- Settings: **General** is the first page, with **Updates** second; tool pages follow in tab order.
- Audio Leveler: **Save levels… / Load levels…**.
- Audio Delay: **Save delays… / Load delays…**.

**0.2.0** — first release of the combined app
- Shell: tabs (Audio Leveler, Audio Delay, USB Monitor, Settings), system tray, start at sign-in, dark/light/system
  theme, GitHub auto-update.
- Audio Leveler (from MCAL): levels stored in Voicemeeter's bus EQ (persist without the app), auto-level wizard
  (Baseline / Leveling / Verifying, 10 dB cut cap, subwoofer handling, live readings, mic clipping recovery), mic level
  control, full speaker names with Voicemeeter **Out** channels, Voicemeeter check banner.
- Audio Delay (new): syncs two Voicemeeter outputs with output delay, including virtual/VBAN outputs.
- USB Monitor (from USB Mon): newest first, Status column third, Connected/Disconnected, one-page device details,
  resizable columns.

---

## Project layout and modules

Each applet is a **module**: its own project in `src/Modules/<Name>/`, built into its own folder,
`modules\<Name>\`, next to `DaisysApp.exe`. The app loads every module it finds there (unless it's switched off in
Settings → General → Applets), so a module can be added, updated or removed without touching the rest.

```
src/
  Directory.Build.props   settings every project shares (version, .NET 8, WPF)
  DaisysApp/              the app: startup, single instance, window and tabs, update banner, tray, Settings → General,
                          and the module loader (Shell/AppletCatalog); lang/ is its translations
  DaisysApp.Core/         what the app and the modules share: IApplet + [Applet], AppletGroup (one tab of sub-tabs), translations (Loc, {l:Tr}),
                          settings storage (JsonStore), the logs (Logging: Log, ErrorLog, Windows' event logs),
                          theming, and code more than one module uses:
                          Shared/Audio (devices, speaker layouts, mic volume), Shared/Voicemeeter, Shared/Hotkeys,
                          Shared/Hardware (NVIDIA's GPU library, Windows' counters, HDR monitors' SDR brightness,
                          the LibreHardwareMonitor connection), Shared/Charts (the time graph), Shared/Csv
  Modules/
    Directory.Build.props   makes each folder here a module (output to modules\<Name>\, references Core)
    Template/               a hello-world module, and the guide to writing one
    Hardware/ (Performance/ UsbMonitor/ Sensors/)   AudioTools/ (Levels/ = Volume, Tests/ = Levels, Delay/)
    Video/ (Resizer/ MiniMirror/ Gaming/)   Network/ (Zabbix/ Logs/)   Tools/ (FileSync/ DiskUsage/ Renamer/)   Joy2Key/
                            each with its README, lang/ translations and code; a module with several tools
                            (one tab, a sub-tab each) has a folder per tool and an AppletGroup (in Core)
installed:
  DaisysApp.exe, lang\, modules\<Name>\ (DaisysApp.<Name>.dll, lang\, README.md, its own dependencies)
```

**To write a module,** copy `src/Modules/Template`, rename it, and follow
[src/Modules/Template/README.md](src/Modules/Template/README.md). The Template ships with the app but is off: tick it
in Settings → General → Applets to see it. The UI follows the design system in `design.md` (styles in
`src/DaisysApp/Themes/Controls.xaml`, available to every module).

## Build and release

Requires the .NET 8 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build DaisysApp.sln   # the app and every module (modules land in src\DaisysApp\bin\...\modules)
.\build.ps1                  # -> artifacts\DaisysApp-Setup-<version>.exe
```

To release: bump `<Version>` in `src/Directory.Build.props` (and the version at the top of this file), commit,
then tag and push, e.g. `git tag v0.6.3; git push origin v0.6.3`. The Release workflow builds the installer on GitHub
and publishes the release that installed copies update from. CI builds every push to `main`.

## Third-party

[NAudio](https://github.com/naudio/NAudio) (MIT), [softcam](https://github.com/tshino/softcam) (MIT, the virtual webcam; built with `tools/build-softcam.ps1` into `third_party/softcam`), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (read over its web server if you run it; not included), [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT, Mini Mirror's screen capture and Gaming's recording), .NET 8 runtime (bundled), Inno Setup (installer). The Resizer is ported
from [Resize Rabbit](https://github.com/rosscarlson/resize-rabbit) and [Resize Raccoon](https://github.com/mistenkt/resize-raccoon)
by mistenkt (MIT). Voicemeeter and
its Remote API are VB-Audio's (installed separately, not shipped).
