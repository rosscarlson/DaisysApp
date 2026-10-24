# SimEffects

*A module of [Daisy's App](../../../README.md): the SimEffects tab (since 3.2.0). Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\SimEffects\` next to `DaisysApp.exe`.*

What SimHub does for a sim rig, rebuilt tool by tool as each is needed. Each tool is a sub-tab (and a sub-module that
can be switched off on its own):

| Tool | What it does | Guide |
|---|---|---|
| **Shaker** | A racing game's telemetry played as vibration to bass shakers (ButtKicker, Dayton Audio, AuraSound…) on your audio devices: effects for crashes, impacts, curbs, road feel, off-road, gear shifts, wheel slip, ABS, the rev limiter and the engine, each with its own volume and settings, a Level Wizard to set them by shortcuts while driving, a profile per game (Forza Horizon, Forza Motorsport, iRacing, ACC), meters, and shortcuts for every volume | [BassShaker/README.md](BassShaker/README.md) (folder ⚠ `BassShaker/`) |
| **Arduino** (since 3.5.3) | Your Arduinos, each on its COM port with SimHub's sketch (as SimHub's Arduino setup left it, since 3.5.6) or the DaisysSim sketch, and what's wired to each (its **features**: gear and shift lights now; more kinds later, as plugins). The sub-tabs that drive a kind show while some Arduino has one | [Arduino/README.md](Arduino/README.md) |
| **Notify** (since 3.5.3; needs Arduino) | The Arduinos' lights: an 8×8 RGB matrix showing the gear (flashing over-rev) and 8 or 16 RGB LEDs as shift lights, with each car's redline found as SimHub does. Shown only while an Arduino has one of them | [Notify/README.md](Notify/README.md) |
| **Fanatec** (since 3.5.3) | The same shift lights on a Fanatec wheel's rev LEDs (the ClubSport Steering Wheel RS's nine) | [Fanatec/README.md](Fanatec/README.md) |

Settings: `%APPDATA%\DaisysApp\BassShaker.json` (Shaker), `Arduino.json`, `Notify.json`, `Fanatec.json`, and
`SimLights.json` (the shift points and each car's redline, shared by Notify and Fanatec); `SimEffects.json` remembers
which tool was showing. Log: `SimEffects-<date>.log`. No Settings page: everything is on the tab.

**Shared by the tools:**

- **Telemetry** (`Telemetry/`): each game's reader, and `TelemetryHub`, through which every tool reads it. Only one
  program can listen on a UDP port, so tools following the same game share one reader: the Shaker, Notify and Fanatec
  can all follow Forza Horizon on port 8000 at once.
- **Shift lights** (`Lights/`): where to shift and what the lights show, worked out once for Notify and Fanatec
  (see [Notify/README.md](Notify/README.md), "Shift points and redlines").

**Code:**

| File | |
|---|---|
| `SimEffectsApplet.cs` | The tab: an `AppletGroup` from DaisysApp.Core with the four sub-tabs; makes the `ArduinoHub` the Arduino and Notify tools share, and shows Notify only while an Arduino has its lights (`ShowTool`) |
| `Ui.cs` | The pieces the Arduino, Notify and Fanatec tabs are built of (cards, headers, slider rows, fields, the ON / OFF chip, Confirm? buttons, two scrolling columns, a board's status light) |
| `Telemetry/Telemetry.cs` | `Game`, `Games` (titles, which use a port, the programs each runs as, their reader), `TelemetryFrame` (the same for every game: speed, RPM, gear as the game counts and `GearNo` (-1 R, 0 N), the car's id and name, iRacing's own shift-light RPMs, accelerations, wheels), `ITelemetrySource` |
| `Telemetry/TelemetryHub.cs` | One reader per game and port on its own thread while some lease is open; `Open` hands out a lease that reads like a source (the newest frame each time) |
| `Telemetry/ForzaSource.cs` | Forza's Data Out over UDP: the sled (232 bytes, with the car's number, class and PI at 212) and the dash at 232, or 244 in Horizon; the newest packet only |
| `Telemetry/IRacingSource.cs` | iRacing's shared memory: header, variable list by name, the newest of its buffers; redline, idle, the shift lights (`DriverCarSL*RPM`) and the player's car (`CarPath`, `CarScreenName`) from the session YAML |
| `Telemetry/AccSource.cs` | ACC's shared memory: the physics page (field place × 4), the graphics page's status, the static page's car model |
| `Lights/` | The shift lights' logic and cards: see [Notify/README.md](Notify/README.md) |

The project references DaisysApp.Shared (audio devices, with NAudio). Translations in `lang/`.
