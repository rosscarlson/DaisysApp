# SimEffects

*A module of [Daisy's App](../../../README.md): the SimEffects tab (since 3.2.0). Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\SimEffects\` next to `DaisysApp.exe`.*

What SimHub does for a sim rig, rebuilt tool by tool as each is needed. Each tool is a sub-tab (and a sub-module that
can be switched off on its own):

| Tool | What it does | Guide |
|---|---|---|
| **Bass Shaker** | A racing game's telemetry played as vibration to bass shakers (ButtKicker, Dayton Audio, AuraSound…) on your audio devices: effects for crashes, impacts, curbs, road feel, off-road, gear shifts, wheel slip, ABS, the rev limiter and the engine, each with its own volume and settings, a profile per game (Forza Horizon, Forza Motorsport, iRacing, ACC), meters, and shortcuts for every volume | [BassShaker/README.md](BassShaker/README.md) |

Settings: `%APPDATA%\DaisysApp\BassShaker.json`; `SimEffects.json` remembers which tool was showing. Log:
`SimEffects-<date>.log`. No Settings page: everything is on the tab.

**Code:** `SimEffectsApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core); each tool's code is in its folder.
The project references DaisysApp.Shared (audio devices, with NAudio). Translations in `lang/`.
