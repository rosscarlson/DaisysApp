# Daisy's App

**Version 0.2.2** · [Download the latest release](https://github.com/rosscarlson/DaisysApp/releases/latest)

A tabbed Windows app that hosts small audio and hardware tools, called **applets**. Each applet is a tab and can be
switched on or off in Settings → General; **Settings** is always the last tab.

![Daisy's App](docs/screenshot.png)

| Tab | What it does |
|---|---|
| Audio Leveler | Test signals, per-speaker level knobs, microphone leveling and an auto-level wizard; levels stored in Voicemeeter's bus EQ (or Windows channel volume) |
| Audio Delay | Brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker or VBAN stream) into sync with Voicemeeter's output delay |
| USB Monitor | Real-time log of device connect / disconnect / status changes, with a per-launch log file |
| Settings | **General** (startup and tray, updates, applets on/off, theme, files), then a page for each applet that has settings |

---

## Contents

- [Install](#install)
- [Voicemeeter](#voicemeeter)
- [Audio Leveler](#audio-leveler)
- [Audio Delay](#audio-delay)
- [USB Monitor](#usb-monitor)
- [Settings](#settings)
- [Files and command line](#files-and-command-line)
- [Updates](#updates)
- [Version history](#version-history)
- [Project layout and adding an applet](#project-layout-and-adding-an-applet)
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

The Audio Leveler and Audio Delay rely on **[Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/)**
(standard Voicemeeter lacks the per-channel bus EQ these tools use). A banner at the top of both tabs appears whenever
Voicemeeter isn't installed (**Get Voicemeeter**), isn't running (**Start Voicemeeter**), or is the standard edition;
it disappears by itself, and the tab refreshes, once Voicemeeter is up.

Everything the tools set — speaker levels and output delays — is stored **in Voicemeeter's own settings**, so it stays
applied whether or not Daisy's App is running. Save / Load buttons on both tabs keep a copy in a file too.

> **Why Voicemeeter?** Windows does have per-channel volume (Sound settings → device → Levels → Balance), and the
> Audio Leveler uses it for ordinary output devices. But Voicemeeter's virtual devices ignore it, and Voicemeeter
> usually drives the sound card in a way that bypasses it, so once audio goes through Voicemeeter, its per-channel EQ
> is the only place a level actually takes effect. Windows has no per-device delay at all.

---

## Audio Leveler

Plays a calibrated test signal on any speakers of the selected output device and lets you set each speaker's level,
by hand or automatically with a microphone.

**Speaker map.** Follows the speaker configuration Windows reports for the device (Stereo, Quad, 5.1, 7.1, 7.1.4 …;
a numbered grid if it isn't recognized). Each tile shows the speaker's full name, one word per line ("Front / Left",
"Low / Frequency / Effect"), and, for Voicemeeter devices, the bus channel it's on (**Out 1**, **Out 2** …, matching
Voicemeeter's own channel numbers). To level all six speakers of a 5.1 system, the Windows device you play through
(e.g. *Voicemeeter Input*) must be set to 5.1 in Sound settings → *Configure*.

**Signals.** Pink noise (full range), pink noise 500 Hz–2 kHz (calibration band), white noise, sine 10 Hz–20 kHz. All
play at −20 dBFS RMS (the calibration standard) and are RMS-normalized. The subwoofer (LFE) channel can be low-passed
at 24 dB/octave (30–200 Hz, default 80 Hz).

**Level knobs.** Select a speaker and a knob appears on its tile:
- **Voicemeeter devices:** the level is set in the EQ of the bus you pick under *Output device* (±12 dB). Each bus has
  an 8-channel parametric EQ; the app uses **cells 5 and 6** of a speaker's channel as a low shelf and a high shelf at
  1 kHz with the same gain, which together are a flat volume change. See them in Voicemeeter: right-click the bus's
  **EQ** button. Setting a level turns the bus EQ on; if that bus's EQ was off but has other bands set up, the app
  warns you. This is separate from VB-Audio's 8x8 Matrix (which only works while the Matrix runs); don't level the
  same speakers with both.
- **Other devices:** the level is the Windows per-channel volume (−40 to 0 dB).

**Microphone.** The Windows default mic is marked **Windows default ·** and is chosen on first run; any other mic you
pick is remembered. **Listen** shows a live meter; **Mic level** is the mic's Windows input volume (lower it if the
meter shows CLIPPING).

**Save levels… / Load levels…** write every speaker's level to a `*.levels.json` file (by default in
`Documents\Daisy's App`) and set them again from it — handy after resetting Voicemeeter. Levels are matched by channel;
the app tells you if the file came from another device or if a speaker's name has changed.

### Leveling

Before you start: set the device's speaker configuration in Windows, turn off Spatial sound and enhancements, and put
the mic or SPL meter at the listening position, at ear height, pointing at the ceiling.

- **With an SPL meter:** choose *Pink noise — 500 Hz–2 kHz*, solo a speaker, press Play, and turn its knob until the
  meter reads your target (typically 75 dB SPL, C-weighted, slow). Repeat; Auto-cycle steps through them for you.
- **With a mic, by hand:** press **Listen**, solo a reference speaker, press Play, then **Set reference**. Each other
  speaker's tile shows its mic level and Δ from the reference; turn its knob until Δ reads 0.0 (check mark within
  ±0.5 dB).
- **Auto-level wizard:** press **Auto-level…**, tick the speakers, and press **Start**.
  - All ticked speakers start from the same level. The wizard measures the room's background noise, then plays
    band-limited pink noise on each speaker for 2 seconds, showing the live mic reading in that speaker's row.
  - **Baseline** (pass 1): the softest speaker becomes the baseline and the others are turned down to match. No
    speaker is cut by more than 10 dB: if one would need more, all speakers are raised by the difference instead,
    keeping them balanced within ±12 dB. **Leveling** (pass 2) checks every speaker and corrects; **Verifying**
    (pass 3) runs only if one is still more than 0.5 dB out. **Level** shows each speaker's setting.
  - A subwoofer that's too quiet to measure, more than 10 dB quieter than the other speakers, or still unbalanced
    after pass 3 is left out and set back to 0 dB; the result says it may need more power.
  - If the mic clips, its level is lowered by 6 dB and the run starts over (up to six times). Esc or Cancel puts the
    original levels back. It stops with a message if a speaker isn't 10 dB above the noise, or if a level change made
    no measurable difference (usually the wrong Voicemeeter bus).

Mic readings are relative (dB at the mic), not calibrated SPL. The manual reference lasts for the session only.

| Action | How |
|---|---|
| Play / stop | Space or the Play button |
| Select / deselect a speaker | Click its tile |
| Solo a speaker | Right-click its tile, or keys 1–9 |
| Adjust a level | Drag the knob up/down (Shift = fine) or scroll (0.5 dB; Shift = 0.1 dB); double-click = 0 dB |
| Reset all levels | Reset levels, then click again to confirm |

| Problem | Fix |
|---|---|
| Only two speakers on a 5.1 system | Set the playback device (e.g. *Voicemeeter Input*) to 5.1 in Windows Sound settings → *Configure*, then press refresh. |
| Knobs don't change what you hear (Voicemeeter) | Check the bus under *Output device* is the one your speakers are on. |
| Knobs don't change what you hear (other device) | Some drivers ignore per-channel volume; use the physical output device. |
| "Windows blocked microphone access" | Settings → Privacy & security → Microphone → *Let desktop apps access your microphone*. |
| Mic shows CLIPPING | Lower **Mic level**, or turn the speakers down. |
| "Couldn't hear … above the background noise" | Raise the mic level or speaker volume, move the mic closer, or quiet the room. |

---

## Audio Delay

For two outputs that play the same audio but reach you at different times — typically a sound card and a Bluetooth
speaker, or a VBAN stream to another PC, which lag. Choose the two Voicemeeter outputs, the device to play through
(usually *Voicemeeter Input*, whose strip must be routed to both outputs) and a microphone at your listening position,
then press **Start**.

- **Device 1 is the base** and keeps no delay; **Device 2 gets the delay**. If the baseline shows Device 2 is actually
  the later one, the app says so, swaps the two selections and carries on.
- Outputs can be hardware (A1 …) or virtual (B1 …, e.g. sent on over VBAN). Voicemeeter can only delay hardware
  outputs, so a virtual output can be the base but never the delayed one. For a VBAN stream that lags, make it
  Device 1 and the sound card (A1) Device 2.
- It plays a short beep (a 30 ms sweep) on each output in turn, three times each, and times when each reaches the mic,
  so it knows which one is late without any guessing. For each beep the other hardware outputs (and the other selected
  output) are muted in Voicemeeter.
- The delay is Voicemeeter's **output delay** (Menu → System Settings, 0–500 ms). Only one output is ever delayed, to
  keep audio as close to the video as possible.
- **Baseline** measures how far apart they are, **Adjusting** checks after setting the delay, and **Verifying** runs
  only if they're still more than 1 ms apart.
- Mutes are always put back afterwards; Cancel (Esc) or an error also puts the delays back. If the mic clips, its level
  is lowered and the run starts over. **Reset delays** sets the hardware outputs back to 0 ms.
- **Save delays… / Load delays…** write every hardware output's delay to a `*.delays.json` file (by default in
  `Documents\Daisy's App`) and set them again from it, matched by output name (A1, A2 …).
- **Microphones that come through Voicemeeter** (e.g. *Voicemeeter Out B2*) work: that bus is never muted, and if the
  playback strip also feeds it, that route is switched off during the test (and restored) so the beep can't reach the
  "mic" electronically. The mic's bus can't be one of the two outputs being synced. For headphones, hold an earcup
  against the mic.

---

## USB Monitor

Logs device connect/disconnect activity in real time, including devices that fail to enumerate ("Unknown USB Device
(Device Descriptor Request Failed)"). It runs for as long as the app does, whichever tab is open and while the window
is hidden in the tray.

- New events appear at the top; click a column header to sort, drag a header edge to resize. **Event** says
  Connected, Disconnected or Status changed; **Status** (third column) is the device's own state (OK, or a problem code
  such as Code 43). Double-click a row for every property that could be read, on one page, with copy buttons.
- A disconnected device shows the identity it had when it connected (details are cached when first seen). Missing
  fields are left blank; an event is never dropped.
- **Log file:** `%LOCALAPPDATA%\DaisysApp\logs\usbmon_<yyyy-MM-dd_HHmmss>.log`, tab-delimited, one per launch, flushed
  on every event. Turn it off in Settings → USB Monitor.

**How detection works** — three layers, so a flaky device is caught however badly it misbehaves:

1. A hidden top-level window registers for device-interface arrival/removal (`RegisterDeviceNotification`, all
   interface classes — so an occasional non-USB device, such as a Bluetooth pairing, can also appear).
2. On `DBT_DEVNODES_CHANGED` (debounced ~400 ms) the USB device tree is snapshotted through SetupAPI/CfgMgr32 and
   diffed against the previous snapshot. This path is USB-only and catches devices that never register an interface.
3. The same diff runs every 3 seconds regardless, so a missed notification can't hide a plug/unplug.

Events are de-duplicated (same device and transition within ~1.5 s), though one physical plug can still produce
several rows: a hub, its composite parent and each child interface.

---

## Settings

**General** (first):
- **Startup and system tray** — keep running in the tray when the window is closed (on by default; right-click the
  tray icon to exit), start when you sign in to Windows, and start hidden in the tray.
- **Updates** — the installed version, check for updates when the app starts, **Check for updates** and
  **Release notes**.
- **Applets** — every applet the app contains, each with an on/off checkbox and a one-line description. A switched-off
  applet isn't loaded at all (no tab, no settings page, nothing running in the background); its settings are kept for
  when it's switched back on. Changes apply after a restart: **Restart now** appears when there's one to apply.
- **Appearance** — Dark (default), Light, or System theme.
- **Files** — open the settings and logs folders.

Then a page for each enabled applet that has settings: **Audio Leveler** (turn Voicemeeter EQ levels on/off) and
**USB Monitor** (log file on/off, open the log folder).

---

## Files and command line

| Item | Location |
|---|---|
| Program | `C:\Program Files\Daisys App\DaisysApp.exe` |
| Settings | `%APPDATA%\DaisysApp\` — `settings.json` (app), `AudioLevel.json`, `AudioDelay.json`, `UsbMonitor.json` |
| Saved levels and delays | `Documents\Daisy's App\` by default (`*.levels.json`, `*.delays.json`) |
| Logs | `%LOCALAPPDATA%\DaisysApp\logs\` (USB events, `errors.log`) |
| Start at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysApp` |
| Update downloads | `%TEMP%\DaisysApp-Update\` |

On first run the Audio Leveler and USB Monitor import settings from the standalone MCAL and USB Mon apps they came from.

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

## Project layout and adding an applet

```
src/DaisysApp/
  App.xaml(.cs), MainWindow.xaml(.cs)   shell: startup, single instance, tabs, update banner, tray, restart
  Shell/      IApplet + [Applet] + AppletCatalog (applet discovery), SettingsPage, TabStrip, TrayIcon
  Settings/   AppSettings (settings.json), JsonStore (one JSON file per applet), StartupManager
  Theming/, Themes/   Dark/Light palettes and control styles (design.md)
  Updates/    GitHub Releases check, verified download, installer launch
  Logging/    errors.log
  Shared/     code used by more than one applet
    Audio/          DeviceService (playback/capture devices), SpeakerLayout, MicGain (Windows mic volume)
    Voicemeeter/    VoicemeeterRemote (Remote API), VoicemeeterBanner (installed/running check)
  Applets/    one folder per applet, nothing shared between them
    AudioLevel/     AudioLevelApplet + view, auto-level wizard, signals, mic meter, Voicemeeter EQ levels
    AudioDelay/     AudioDelayApplet + view, beep player, mic recorder, arrival-time analysis
    UsbMonitor/     UsbMonitorApplet + view, device capture, event log, details window
```

To add an applet:

1. Create a folder `src/DaisysApp/Applets/<Name>/` and put all of its code there (namespace `DaisysApp.Applets.<Name>`).
2. Add a class that implements `Shell/IApplet` (its tab content, an optional settings page, start/save/dispose) and tag
   it with its metadata:
   ```csharp
   [Applet("MyThing", "My Thing", "", Order = 40, Description = "One line for Settings → Applets")]
   public sealed class MyThingApplet : IApplet { … }
   ```
   `Order` is the tab position; the Id is the stable key for its settings file (`%APPDATA%\DaisysApp\MyThing.json`
   via `Settings/JsonStore`) and its on/off setting.

That's all: the app finds it automatically, gives it a tab and a Settings page, and lists it under Settings → General
→ Applets. If two applets need the same code, it goes in `Shared/`, never in another applet's folder. The UI follows
the design system in `design.md` (styles in `Themes/Controls.xaml`).

## Build and release

Requires the .NET 8 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build src\DaisysApp\DaisysApp.csproj
.\build.ps1                 # -> artifacts\DaisysApp-Setup-<version>.exe
```

To release: bump `<Version>` in `src/DaisysApp/DaisysApp.csproj` (and the version at the top of this file), commit,
then tag and push, e.g. `git tag v0.2.2; git push origin v0.2.2`. The Release workflow builds the installer on GitHub
and publishes the release that installed copies update from. CI builds every push to `main`.

## Third-party

[NAudio](https://github.com/naudio/NAudio) (MIT), .NET 8 runtime (bundled), Inno Setup (installer). Voicemeeter and
its Remote API are VB-Audio's (installed separately, not shipped).
