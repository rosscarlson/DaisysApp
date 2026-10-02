# Daisy's App

A tabbed Windows app that hosts small tools. Each tool is a tab; **Settings** is always the last tab, with a sub-tab
per tool and **General** (startup, system tray, theme, updates) at the end.

![Daisy's App](docs/screenshot.png)

| Tab | What it does |
|---|---|
| Audio Leveler | Calibrated test signals, per-speaker level knobs, microphone leveling, Voicemeeter bus levels |
| USB Monitor | Real-time log of device connect / disconnect / status changes, with a per-launch log file |

- Installs to `C:\Program Files\Daisys App` (installer: `DaisysApp-Setup-x.y.z.exe`, needs admin).
- Settings: `%APPDATA%\DaisysApp\` (`settings.json` for the app, one JSON file per tool).
- Logs: `%LOCALAPPDATA%\DaisysApp\logs\`.
- Command line: `--tray` starts hidden in the tray, `--exit` closes the running copy.
- Updates: checked against GitHub Releases on launch (banner when one exists) and from the **Check for updates**
  button, tray menu, or Settings → General. The installer is verified by SHA-256 and run silently.

## Audio Leveler

Plays a calibrated test signal on any speakers of the selected output device and lets you set each speaker's level,
by hand or automatically with a microphone. The speaker map follows the speaker configuration Windows reports for
the device (Mono, Stereo, Quad, 5.1, 7.1, 7.1.4 …; a numbered grid if it isn't recognized).

**Signals:** pink noise (full range), pink noise 500 Hz–2 kHz (calibration band), white noise, sine 10 Hz–20 kHz.
All are RMS-normalized, so the level slider (−60 to 0 dBFS RMS) means the same for each. The LFE channel can be
low-passed at 24 dB/octave (30–200 Hz, default 80 Hz).

**Level knobs:** select a speaker and a knob appears on its tile. On Windows devices it sets the per-channel volume
(−40 to 0 dB); on Voicemeeter devices it applies a gain inside the chosen bus (−40 to +12 dB).

### Leveling

Before you start: set the device's speaker configuration in Windows Sound settings, turn off Spatial sound and
enhancements, and put the mic or SPL meter at the listening position, at ear height, pointing at the ceiling.

- **With an SPL meter:** choose *Pink noise — 500 Hz–2 kHz* at −20 dBFS RMS, solo a speaker, press Play, and turn its
  knob until the meter reads your target (typically 75 dB SPL, C-weighted, slow). Repeat; Auto-cycle steps for you.
- **With a mic, by hand:** press **Listen**, solo a reference speaker, press Play, then **Set reference**. Each other
  speaker's tile shows its mic level and Δ from the reference; turn its knob until Δ reads 0.0 (check mark within
  ±0.5 dB).
- **Auto-level:** select the speakers and press **Auto-level**. It measures the background noise, plays each speaker
  in turn, and adjusts over up to three passes until all are within ±0.5 dB. With a reference set, speakers are
  matched to it; without one, several speakers are matched to the quietest and a single speaker becomes the
  reference. Esc cancels. It stops with a message if the mic clips, a speaker isn't 10 dB above the noise floor, or
  a level change made no measurable difference.

Mic readings are relative (dB at the mic), not calibrated SPL. The reference lasts for the session only.

| Action | How |
|---|---|
| Play / stop | Space or the Play button |
| Select / deselect a speaker | Click its tile |
| Solo a speaker | Right-click its tile, or keys 1–9 |
| Adjust a level | Drag the knob up/down (Shift = fine) or scroll (0.5 dB; Shift = 0.1 dB); double-click = 0 dB |
| Reset all levels | Reset levels, then click again to confirm |

### Voicemeeter

Voicemeeter ignores Windows channel volume, so for Voicemeeter devices the levels are applied through Voicemeeter's
**bus output insert**. Pick the bus your speakers are on under *Output device*. Gains are saved per bus and reapplied
when Voicemeeter restarts.

- Only one program can use the insert at a time. If the 8x8 Matrix holds it, close it (Voicemeeter → *Other Tools* →
  *Shut Down Matrix 8x8*); the app connects automatically once it's free. To release the insert yourself, turn off
  *Set speaker levels inside Voicemeeter* in Settings → Audio Leveler.
- The levels apply only while the app is running: keep it in the tray and start it at sign-in (Settings → General).

### Troubleshooting

| Problem | Fix |
|---|---|
| Knobs don't change what you hear (Windows device) | Some drivers and virtual devices ignore per-channel volume. Use the physical output device. |
| Knobs don't change what you hear (Voicemeeter) | Check the bus selection and the status under Output device. |
| "Windows blocked microphone access" | Settings → Privacy & security → Microphone → *Let desktop apps access your microphone*. |
| Mic shows CLIPPING | Lower the mic gain in Windows, or the signal level. |
| "Couldn't hear … above the background noise" | Raise the signal level or mic gain, move the mic closer, or quiet the room. |
| Wrong speaker layout | Set it in Windows Sound settings → the device → *Configure*, then press refresh. |

## USB Monitor

Logs device connect/disconnect activity in real time, including devices that fail to enumerate ("Unknown USB Device
(Device Descriptor Request Failed)"). It runs for as long as the app does, whichever tab is open and while the window
is hidden in the tray.

- New events appear at the top; click a column header to sort. Double-click a row for every property that could be
  read, with copy buttons.
- A removed device shows the identity it had on arrival (details are cached when first seen). Missing fields are
  left blank; an event is never dropped.
- **Log file:** `%LOCALAPPDATA%\DaisysApp\logs\usbmon_<yyyy-MM-dd_HHmmss>.log`, tab-delimited, one per launch,
  flushed on every event. Turn it off in Settings → USB Monitor. Unhandled errors go to `errors.log` in the same
  folder.
- The filter bar is reserved; no filters exist yet (`Tools/UsbMonitor/Filtering/IEventFilter.cs` is the seam).

**How detection works** — three layers, so a flaky device is caught however badly it misbehaves:

1. A hidden top-level window registers for device-interface arrival/removal (`RegisterDeviceNotification`, all
   interface classes — so an occasional non-USB device, such as a Bluetooth pairing, can also appear).
2. On `DBT_DEVNODES_CHANGED` (debounced ~400 ms) the USB device tree is snapshotted through SetupAPI/CfgMgr32 and
   diffed against the previous snapshot. This path is USB-only and catches devices that never register an interface.
3. The same diff runs every 3 seconds regardless, so a missed notification can't hide a plug/unplug.

Events are de-duplicated (same device and transition within ~1.5 s), though one physical plug can still produce
several rows: a hub, its composite parent and each child interface.

## Adding a tool

1. Create `src/DaisysApp/Tools/<Name>/` with a class implementing `Shell/ITool.cs` (a view, an optional settings view).
2. Add one line to `Shell/ToolRegistry.cs`.

The shell supplies the tab, the Settings sub-tab, tray, startup and updates. Keep the tool's settings in its own file
with `Settings/JsonStore`. UI follows the design system in `design.md` (styles in `Themes/Controls.xaml`).

## Build and release

Requires the .NET 8 SDK and Inno Setup 6.

```powershell
dotnet build src\DaisysApp\DaisysApp.csproj
.\build.ps1                 # -> artifacts\DaisysApp-Setup-<version>.exe
```

To release: bump `<Version>` in `DaisysApp.csproj`, commit, then `git tag v0.2.0; git push origin v0.2.0`. The Release
workflow builds the installer and publishes the GitHub Release that installed copies update from. The repository must
be public for the updater to see releases.

## Third-party

[NAudio](https://github.com/naudio/NAudio) (MIT), .NET 8 runtime (bundled), Inno Setup (installer).
