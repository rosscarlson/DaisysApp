# Video

*A module of [Daisy's App](../../../README.md): the Video tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Video\` next to `DaisysApp.exe`.*

Five tools, each on its own sub-tab at the top of the tab (the one you used last opens next time). The Resizer is a
module of its own ([src/Modules/Resizer](../Resizer/README.md), `modules\Resizer\`, since 1.1.68, so it can also be an
app of its own); it names this tab (`[Applet(Group = "Video", GroupOrder = 0)]`) and joins it as the first sub-tab.
Without it, the tab has the other four.

| Tool | What it does | Guide |
|---|---|---|
| **Resizer** | Saved window sizes and positions per program (e.g. a game stretched over three monitors), applied by click, hotkey, tray, script or automatically when the program starts | [../Resizer/README.md](../Resizer/README.md) (its own module) |
| **Mini Mirror** | Any part of the screen live in its own always-on-top window, e.g. a track map, delta bar or HUD corner on another monitor | [MiniMirror/README.md](MiniMirror/README.md) |
| **Overlay** | What's running now (frame rate, 1% low, frame time) and the FPS overlay | [Gaming/README.md](Gaming/README.md) |
| **Recording** | Performance (the PC's GPU, CPU and memory load), recording with the graphics card's encoder, benchmarks, and each app's history | [Gaming/README.md](Gaming/README.md) |
| **Fire TV** | An Amazon Fire TV (Fire OS) debloated over the network with ADB: ads, tracking and Amazon's apps removed, the apps you keep left alone, everything restorable (since 3.8.0) | [FireTv/README.md](FireTv/README.md) |

Settings → Video has a sub-tab for each of them, and the tray menu an item for each that has one. Overlay and
Recording are one tool's two pages (they were the Gaming sub-tab before 0.25): their code is in `Gaming/`, and they
share `Gaming.json`.

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a tab of its own (before 0.22):
`Resizer.json`, `MiniMirror.json`, `Gaming.json` (Overlay and Recording) and `FireTv.json`; `Video.json` remembers which tool was
showing. Their log is `Video-<date>.log` (the Resizer's, `Resizer-<date>.log`). Updating from an earlier version
moves the tabs' place, on/off state and the last tab open to Video.

**Code:** `VideoApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the tools;
the app adds the Resizer to it); each tool's code is in its folder, `MiniMirror/`, `Gaming/` and `FireTv/`, with its own namespace
(`DaisysApp.Applets.Video.Gaming` …). The translations are in `lang/`. `Video.csproj` references DaisysApp.Shared
(the hardware readings Recording uses), copied into `modules\Video\`.
