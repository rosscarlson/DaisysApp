# SimEffects

*A module of [Daisy's App](../../../README.md): the SimEffects tab (since 3.2.0). Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\SimEffects\` next to `DaisysApp.exe`.*

What SimHub does for a sim rig, rebuilt tool by tool as each is needed. Each tool is a sub-tab (and a sub-module that
can be switched off on its own):

| Tool | What it does | Guide |
|---|---|---|
| **Profiles** (since 3.18.0) | One set of profiles for the whole module, a profile per game and one for no game: the profile in use (the **Profile:** drop-down at the right of the sub-tabs, or a game starting) is the game every tool reads, once, and every tool's settings (whether each is on, and its own) | [Profiles/README.md](Profiles/README.md) |
| **Shaker** | A racing game's telemetry played as vibration to bass shakers (ButtKicker, Dayton Audio, AuraSound…) on your audio devices: effects for crashes, impacts, curbs, road feel, off-road, gear shifts, wheel slip, ABS, the rev limiter and the engine, each with its own volume and settings, a Level Wizard to set them by shortcuts while driving, a profile per game (Forza Horizon, Forza Motorsport, iRacing, ACC), meters, and shortcuts for every volume | [BassShaker/README.md](BassShaker/README.md) (folder ⚠ `BassShaker/`) |
| **Arduino** (since 3.5.3) | Your Arduinos, each on its COM port with SimHub's sketch (as SimHub's Arduino setup left it, since 3.5.6) or the DaisysSim sketch, and what's wired to each (its **features**: gear and shift lights now; more kinds later, as plugins). The sub-tabs that drive a kind show while some Arduino has one | [Arduino/README.md](Arduino/README.md) |
| **Notify** (since 3.5.3; needs Arduino) | The Arduinos' lights: an 8×8 RGB matrix showing the gear (flashing over-rev) and 8 or 16 RGB LEDs as shift lights, with each car's redline found as SimHub does. Shown only while an Arduino has one of them | [Notify/README.md](Notify/README.md) |
| **Fanatec** (since 3.5.3) | The same shift lights on a Fanatec wheel's rev LEDs (the ClubSport Steering Wheel RS's nine) | [Fanatec/README.md](Fanatec/README.md) |
| **Input Overlay** (since 3.17.0; ForzaWheel's before) | The pedals' and wheel's last few seconds as traces over the game, with the speed (now, and the highest and lowest of the last few seconds), read from the game's telemetry or a controller's axes; logs sessions for Forza Analysis | [InputOverlay/README.md](InputOverlay/README.md) |
| **AmbiLight** (since 3.9.0) | What's on your displays (the ones you tick) on Govee lights behind them, as Philips' Ambilight: each zone of the light placed and sized over the screens, down to a segment each; lights found by name and tested before adding | [AmbiLight/README.md](AmbiLight/README.md) |

Settings: `%APPDATA%\DaisysApp\BassShaker.json` (Shaker), `Arduino.json`, `Notify.json`, `Fanatec.json`, and
`SimLights.json` (the shift points and each car's redline, shared by Notify and Fanatec), `AmbiLight.json`, `InputOverlay.json`, `SimProfiles.json` (the profiles, with each tool's settings in each); `SimEffects.json` remembers
which tool was showing. Log: `SimEffects-<date>.log`. Settings → SimEffects has a page for the Shaker (its Test level, since 3.7.0); everything else is on the tab.

**Shared by the tools:**

- **Telemetry** (`Telemetry/`): each game's reader, and `TelemetryHub`, one reader per game and port. Since 3.18.0
  every tool reads the profile in use's game through `Profiles/SimData.cs` (`SimData.Open()`): read once, the same
  frames for the Shaker, the shift lights and the Input Overlay, and a new game for all of them when the profile
  changes. A new tool reads the game by opening it there.
- **Profiles** (`Profiles/`, since 3.18.0): see [Profiles/README.md](Profiles/README.md). A tool takes part with
  `SimProfiles.Register` (its settings object, the properties kept per profile, its on / off, what to do when another
  profile is put in) and calls `SimProfiles.Saved(this)` from its settings' Save.
- **Shift lights** (`Lights/`): where to shift and what the lights show, worked out once for Notify and Fanatec
  (see [Notify/README.md](Notify/README.md), "Shift points and redlines").

**Code:**

| File | |
|---|---|
| `SimEffectsApplet.cs` | The tab: an `AppletGroup` from DaisysApp.Core with its sub-tabs (Profiles first) and the Profile drop-down at their right (`HeaderRight`); loads the profiles before any tool is made; makes the `ArduinoHub` the Arduino and Notify tools share, and shows Notify only while an Arduino has its lights (`ShowTool`) |
| `Profiles/` | The profiles, the module's telemetry (`SimData`) and Game Switching: see [Profiles/README.md](Profiles/README.md) |
| `InputOverlay/` | Input Overlay: see [InputOverlay/README.md](InputOverlay/README.md) |
| `Ui.cs` | The pieces the Arduino, Notify and Fanatec tabs are built of (cards, headers, slider rows, fields, the ON / OFF chip, Confirm? buttons, two scrolling columns, a board's status light) |
| `Telemetry/Telemetry.cs` | `Game`, `Games` (titles, which use a port, the programs each runs as, their reader), `TelemetryFrame` (the same for every game: speed, RPM, throttle, brake, clutch, handbrake and steering (since 3.17.0), gear as the game counts and `GearNo` (-1 R, 0 N), the car's id and name, iRacing's own shift-light RPMs, accelerations, wheels), `ITelemetrySource` |
| `Telemetry/TelemetryHub.cs` | One reader per game and port on its own thread while some lease is open; `Open` hands out a lease that reads like a source (the newest frame each time) |
| `Telemetry/ForzaSource.cs` | Forza's Data Out over UDP: the sled (232 bytes, with the car's number, class and PI at 212) and the dash at 232, or 244 in Horizon; the newest packet only |
| `Telemetry/IRacingSource.cs` | iRacing's shared memory: header, variable list by name, the newest of its buffers; redline, idle, the shift lights (`DriverCarSL*RPM`) and the player's car (`CarPath`, `CarScreenName`) from the session YAML |
| `Telemetry/AccSource.cs` | ACC's shared memory: the physics page (field place × 4), the graphics page's status, the static page's car model |
| `Lights/` | The shift lights' logic and cards: see [Notify/README.md](Notify/README.md) |
| `AmbiLight/` | AmbiLight: see [AmbiLight/README.md](AmbiLight/README.md) |

The project references DaisysApp.Shared (audio devices, with NAudio), Vortice's Direct3D 11 and DXGI (AmbiLight's screen capture) and DirectInput (Input Overlay's controllers, with ForzaWheel's `GameControllers.cs`, `InputRef.cs` and `Capture.cs` linked in). Translations in `lang/`.
