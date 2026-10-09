# Audio Levels

*A module of [Daisy's App](../../../README.md): the Audio Levels tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\AudioLevels\` next to `DaisysApp.exe`.*

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

**Each shortcut press changes the volume by** the number at the top of the tab: 10 by default, anything from 1 to 100.

An app's shortcuts are kept by its program name (e.g. `spotify`), so they work whenever it's running; a device's by
the device, so they come back when it's reconnected. A shortcut another program already has is flagged in the
shortcut window.

Saved in `%APPDATA%\DaisysApp\AudioLevels.json` (the step and the shortcuts; the volumes themselves live in Windows).
