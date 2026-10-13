# Audio: Volume

*A tool in [Daisy's App](../../../../README.md)'s [Audio](../README.md) tab: its **Volume** sub-tab (called Levels before 0.26). Switch Audio on or off in Settings → General → Applets. Its code is in this folder, part of the Audio module (installed to `modules\AudioTools\` next to `DaisysApp.exe`).*

Three columns side by side:

- **Playback devices** and **Recording devices**: every connected device with its Windows volume (0–100, the same
  number as Sound settings) and a mute button. The default devices are listed first, marked **Default ·**.
- **Apps**: every program that's playing sound (or has since it started), with its own volume and mute: the volume
  mixer's sliders, which work on top of the device's volume. An app playing to two devices is one row, and setting it
  sets both. Windows' system sounds have a row too.

Everything follows live: if something turns a device or an app down (a Bluetooth headset resetting its volume, another
app, the device's own buttons) the slider moves, so you can see it and put it back. Drag a slider or scroll over it
(Shift = 1 at a time); click the speaker to mute, or the number to set it straight to 100. Devices appear and disappear
as they're connected; apps are looked for again every second while the tab is open; the refresh button looks again now.

## Shortcuts

**Click any device's or app's name** to give it three shortcuts: **volume up**, **volume down** and **mute on / off**.
Each can be a key combination (with Ctrl, Alt or Shift) or a button on a controller, wheel or button box, as in Mini
Mirror. They work anywhere — with the app in the tray, and in games. A small keyboard sign beside a name shows it has
shortcuts (hover for which).

**Each shortcut press changes the volume by** the number set in Settings → Audio: 10 by default, anything from 1 to 100.

An app's shortcuts are kept by its program name (e.g. `spotify`), so they work whenever it's running; a device's by
the device, so they come back when it's reconnected. A shortcut another program already has is flagged in the
shortcut window.

Saved in `%APPDATA%\DaisysApp\AudioLevels.json` (the step and the shortcuts; the volumes themselves live in Windows).

## Code

This is the **Volume** sub-tab (the folder keeps its old name, `Levels/`).

| File | What it is |
|---|---|
| `AudioLevelsView.xaml(.cs)` | The sub-tab: a row per playback and recording device, then a row per app |
| `DeviceVolumeRow.cs` | One row: a device's Windows volume or an app's own volume, kept in step both ways (slider ↔ Windows) |
| `AppVolumes.cs` | Each program's sound as Windows' volume mixer has it (its sessions on every playback device) |
| `VolumeHotkeyService.cs` | The volume up / down / mute shortcuts (keys, controller or wheel buttons), working anywhere |
| `HotkeyWindow.cs` | Sets a device's or app's three shortcuts |
| `LevelsSettingsCard.cs` | Its part of Settings → Audio (the step one shortcut press moves the volume) |
| `AudioLevelsSettings.cs` | `AudioLevels.json`: the step and the shortcuts |

Devices come from `DaisysApp.Shared.Audio` (Core); shortcuts from `DaisysApp.Shared.Hotkeys`.
