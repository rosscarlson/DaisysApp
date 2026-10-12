# Video

*A module of [Daisy's App](../../../README.md): the Video tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\Video\` next to `DaisysApp.exe`.*

Three tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Resizer** | Saved window sizes and positions per program (e.g. a game stretched over three monitors), applied by click, hotkey, tray, script or automatically when the program starts | [Resizer/README.md](Resizer/README.md) |
| **Mini Mirror** | Any part of the screen live in its own always-on-top window, e.g. a track map, delta bar or HUD corner on another monitor | [MiniMirror/README.md](MiniMirror/README.md) |
| **Gaming** | What's running now, the PC's GPU, CPU and memory load, the FPS overlay, recording with the graphics card's encoder, benchmarks, and each app's performance history | [Gaming/README.md](Gaming/README.md) |

Settings → Video has a sub-tab for each of them, and the tray menu's **Video** item a submenu for each that has one.

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a tab of its own (before 0.22):
`Resizer.json`, `MiniMirror.json` and `Gaming.json`; `Video.json` remembers which tool was showing. Their log is
`Video-<date>.log`. Updating from an earlier version moves the tabs' place, on/off state and the last tab open to Video.

**Code:** `VideoApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the tools);
each tool's code is in its folder, `Resizer/`, `MiniMirror/` and `Gaming/`, with its own namespace
(`DaisysApp.Applets.Video.Gaming` …). The translations for all three are in `lang/`.
