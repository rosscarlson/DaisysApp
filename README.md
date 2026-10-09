# Daisy's App

**Version 0.17.2** · [Download the latest release](https://github.com/rosscarlson/DaisysApp/releases/latest)

A tabbed Windows app that hosts small audio and hardware tools, called **applets**. Each applet is a tab and can be
switched on or off in Settings → General; right-click a tab to rename it, or press and hold it and drag it to move it.
**Settings** is always the last tab. Each applet is a separate **module**
the app loads from its `modules` folder, with its own guide (linked below), so new ones are easy to add — see
[Project layout and modules](#project-layout-and-modules).

![Daisy's App](docs/screenshot.png)

| Tab | What it does |
|---|---|
| Performance | Live graphs of CPU, GPU, memory, video memory, disk, network and temperatures, per-core load, processes and system details, with a log and history windows |
| USB Monitor | Real-time log of device connect / disconnect / status changes, with a per-launch log file |
| Sensors | Every temperature, fan, voltage, power, clock and load sensor from LibreHardwareMonitor, grouped by hardware, with history graphs and a log |
| Audio Tools | Three tools on sub-tabs: **Levels** (volume and mute for every playback and recording device and every app, live, with volume up / down / mute shortcuts), **Tests** (test signals, per-speaker levels, microphone leveling, auto-level and EQ wizards, stored in Voicemeeter's bus EQ or Windows channel volume / Equalizer APO) and **Delay** (brings two Voicemeeter outputs into sync with output delay) |
| Resizer | Saved window sizes and positions per program (e.g. a game stretched over three monitors), applied by click, hotkey, tray, script or automatically (from Resize Rabbit) |
| Mini Mirror | Shows any part of the screen live in its own always-on-top window — a track map, delta bar or HUD corner moved to another monitor (from the MiniMirror SimHub plugin) |
| Joy 2 Key | Controller buttons, sticks, triggers and the POV hat pressing keys (held, tapped, repeated, toggled or long-pressed) or playing macros (keys in order with pauses), clicking, scrolling and moving the mouse, with a profile per game that switches with the game in front; imports JoyToKey's profiles |
| Gaming | An FPS overlay over games (frame rate, frame time, video memory and more, in three sizes), screen recording with the graphics card's encoder (NVENC), and each game's performance history |
| Settings | **General** (startup and tray, updates, applets on/off, theme, files), then a page for each applet that has settings |

---

## Contents

- [Install](#install)
- [Voicemeeter](#voicemeeter)
- [Performance](#performance)
- [USB Monitor](#usb-monitor)
- [Sensors](#sensors)
- [Audio Tools](#audio-tools)
- [Resizer](#resizer)
- [Mini Mirror](#mini-mirror)
- [Joy 2 Key](#joy-2-key)
- [Gaming](#gaming)
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

Audio Tools' **Tests** and **Delay** rely on **[Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/)**
(standard Voicemeeter lacks the per-channel bus EQ these tools use). A banner at the top of both appears whenever
Voicemeeter isn't installed (**Get Voicemeeter**), isn't running (**Start Voicemeeter**), or is the standard edition;
it disappears by itself, and the tab refreshes, once Voicemeeter is up.

Everything the tools set — speaker levels and output delays — is stored **in Voicemeeter's own settings**, so it stays
applied whether or not Daisy's App is running. Save / Load buttons on both keep a copy in a file too.

> **Why Voicemeeter?** Windows does have per-channel volume (Sound settings → device → Levels → Balance), and the
> Tests tool uses it for ordinary output devices. But Voicemeeter's virtual devices ignore it, and Voicemeeter
> usually drives the sound card in a way that bypasses it, so once audio goes through Voicemeeter, its per-channel EQ
> is the only place a level actually takes effect. Windows has no per-device delay at all.

---

## Audio Tools

Three tools, each on its own sub-tab:

- **Levels** — volume and mute for every playback and recording device and every app, side by side and live, with
  volume up / down / mute shortcuts (keys or controller buttons) for any of them.
- **Tests** — test signals, per-speaker level knobs, microphone leveling and an auto-level wizard, plus an EQ wizard
  that evens out each speaker's response in the room; levels and EQ stored in Voicemeeter's bus EQ (or Windows channel
  volume / Equalizer APO).
- **Delay** — brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker or VBAN stream) into sync with
  Voicemeeter's output delay.

**How they work and how to use them: [src/Modules/AudioTools/README.md](src/Modules/AudioTools/README.md)**

---

## USB Monitor

Real-time log of device connect / disconnect / status changes, with a per-launch log file.

**How it works and how to use it: [src/Modules/UsbMonitor/README.md](src/Modules/UsbMonitor/README.md)**

---

## Sensors

Every temperature, fan, voltage, power, clock and load sensor from LibreHardwareMonitor, grouped by hardware, with history graphs and a log of temperatures, fans and power.

**How it works and how to use it: [src/Modules/Sensors/README.md](src/Modules/Sensors/README.md)**

---

## Resizer

Saved window sizes and positions per program (e.g. a game stretched over three monitors), applied by click, hotkey, tray, script or automatically (from Resize Rabbit).

**How it works and how to use it: [src/Modules/Resizer/README.md](src/Modules/Resizer/README.md)**

---

## Mini Mirror

Shows any part of the screen live in its own always-on-top window — a track map, delta bar or HUD corner moved to another monitor (from the MiniMirror SimHub plugin).

**How it works and how to use it: [src/Modules/MiniMirror/README.md](src/Modules/MiniMirror/README.md)**

---

## Joy 2 Key

Controller buttons, sticks, triggers and the POV hat pressing keys (held, tapped, repeated, toggled, or other keys on
a long press), macros (any number of keys in order, with pauses), clicking, scrolling and moving the mouse, running programs and switching profiles, like JoyToKey. Each
game gets a profile that's used while it's the window in front; press something on a controller to find its tile,
double-click the tile to say what it does. **Import from JoyToKey…** brings JoyToKey's profiles in.

**How it works and how to use it: [src/Modules/Joy2Key/README.md](src/Modules/Joy2Key/README.md)**

---

## Gaming

An FPS overlay over games (frame rate, frame time, video memory and more, in three sizes, movable, scalable and lockable), screen recording of a monitor, region or game with the graphics card's encoder (NVENC on NVIDIA cards; H.264, HEVC or AV1, in editable quality profiles), each game's performance logged for history graphs, and shortcuts for all of it.

**How it works and how to use it: [src/Modules/Gaming/README.md](src/Modules/Gaming/README.md)**

---

## Performance

Live graphs of CPU, GPU, memory, video memory, disk, network and temperatures, per-core load, processes and system details, with a log and history windows.

**How it works and how to use it: [src/Modules/Performance/README.md](src/Modules/Performance/README.md)**

---

## Settings

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
- **Appearance** — Dark (default), Light, or System theme.
- **Language** — English, Español, Français or Português, plus any language someone has added (see
  [Translations](#translations)). Takes effect after **Restart now**.
- **Files** — open the settings and logs folders.

Every setting is saved as soon as you change it (a **✓ Saved** note appears beside it); there's no Save button.

Then a page for each enabled applet that has settings: **Audio Tools** (turn Voicemeeter EQ levels on/off),
**USB Monitor** (log file on/off, open the log folder), **Resizer** (process watcher speed, import from Resize
Rabbit / Raccoon), **Mini Mirror** (new-mirror shortcut, HDR, hide from screen capture, import from the SimHub
plugin), **Performance** (the log), **Sensors** (LibreHardwareMonitor's address and setup guide, the sensor log) and **Gaming** (the overlay's look,
recording folder, audio and quality profiles, shortcuts, game names, history).

---

## Translations

Every piece of text in the app is in translation files, one per language, as plain JSON next to the program:
- `lang\` beside `DaisysApp.exe` holds the main window's and the Settings page's text.
- `modules\<applet>\lang\` holds each applet's (e.g. `modules\Performance\lang\`).

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
| Settings | `%APPDATA%\DaisysApp\` — `settings.json` (app), `AudioLevel.json`, `AudioLevels.json` and `AudioDelay.json` (Audio Tools: Tests, Levels and Delay), `UsbMonitor.json`, `Resizer.json` (profiles and groups), `MiniMirror.json` (mirrors), `Sensors.json`, `Gaming.json`, `Joy2Key.json` and `Joy2Key\Profiles\*.json` (one file per profile) |
| Saved levels and delays | `Documents\Daisy's App\` by default (`*.levels.json`, `*.delays.json`) |
| Recordings | `Videos\Daisy's App\` by default |
| Logs | `%LOCALAPPDATA%\DaisysApp\logs\` (USB events, `errors.log`, `performance\` for the Performance and Sensors logs, `gaming\` for the game history) |
| Start at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysApp` |
| Update downloads | `%TEMP%\DaisysApp-Update\` |

On first run the Audio Tools and USB Monitor import settings from the standalone MCAL and USB Mon apps they came from.

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

---

## Version history

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
  DaisysApp.Core/         what the app and the modules share: IApplet + [Applet], translations (Loc, {l:Tr}),
                          settings storage (JsonStore), errors.log, theming, and code more than one module uses:
                          Shared/Audio (devices, speaker layouts, mic volume), Shared/Voicemeeter, Shared/Hotkeys,
                          Shared/Hardware (NVIDIA's GPU library, Windows' counters, HDR monitors' SDR brightness,
                          the LibreHardwareMonitor connection), Shared/Charts (the time graph), Shared/Csv
  Modules/
    Directory.Build.props   makes each folder here a module (output to modules\<Name>\, references Core)
    Template/               a hello-world module, and the guide to writing one
    AudioTools/ (Levels/ Tests/ Delay/)  UsbMonitor/  Resizer/  MiniMirror/  Performance/  Sensors/  Gaming/  Joy2Key/
                            each with its README, lang/ translations and code
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

[NAudio](https://github.com/naudio/NAudio) (MIT), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (read over its web server if you run it; not included), [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT, Mini Mirror's screen capture), .NET 8 runtime (bundled), Inno Setup (installer). The Resizer is ported
from [Resize Rabbit](https://github.com/rosscarlson/resize-rabbit) and [Resize Raccoon](https://github.com/mistenkt/resize-raccoon)
by mistenkt (MIT). Voicemeeter and
its Remote API are VB-Audio's (installed separately, not shipped).
