# SimEffects → Bass Shaker

*A tool of [SimEffects](../README.md) (since 3.2.0).*

The game's telemetry, turned into low-frequency sound and played to tactile transducers ("bass shakers") on one or
more audio devices, so the rig feels the car: as SimHub's ShakeIt Bass Shakers.

## Using it

- **ON / OFF** (top left) starts or stops it; the controller icon beside it sets a shortcut for that (keys with
  Ctrl, Alt or Shift, or a controller / button box button; it listens as soon as it opens). While it's on, what it's doing shows next: *Waiting for Forza Horizon on port 5300*,
  *Forza Horizon: driving*, *not driving* (a menu, pause or replay: everything stops), or a problem (the port taken by
  another program). It stays on or off across restarts. The tray menu has **Bass Shaker on** too.
- **Master** (top right): the volume of everything.
- **Devices** (left): **Add Device** asks for the playback device, the **transducer** on it and its **channels**.
  The transducer sets the range of frequencies the device is ever sent (*Low* / *High*): effects are kept inside it, so
  a ButtKicker gets its deep 10–80 Hz and a small puck isn't sent rumble it can't make. Two shakers on one stereo amp
  are two devices, **Left** and **Right**; **LFE** plays on a surround card's subwoofer channel. Opened (the arrow), a
  device can change its transducer, channels, Low and High, **Test** it (a second at the frequency it's strongest at)
  or be removed. Each has its volume and a meter of what it's playing. A device that isn't plugged in says *Not
  connected* and starts playing when it is (checked every 3 seconds).
- **Profile** card (left): at the top, the profile in use and **New** (a game's defaults, or a copy of this one); then its name (type to rename), **Game**, **UDP Port** (Forza only), **Programs** that pick it
  with **Game Switching** on (a program starting switches to its profile once; picking another by hand meanwhile
  stays), **Reset Effects** back to the game's defaults, and **Delete**.
- **Meters** (top right): a column of segments per effect, in the effects' order, lit as it shakes, its highest point
  held for a moment. A muted effect's column stays dark.
- **Effects**: each one's meter, mute, volume and shortcuts show folded; opened (the arrow) it has its settings,
  **Plays On** (each device by its name without Windows' description in brackets: on or off for this effect, and a
  level for how much of it that device gets), then **Test** (it plays for a moment, even when off) and **Remove**
  at the bottom right. Drag one by its grip (left) onto another to move it: above it in its top half, below in its bottom
  half. **Add Effect** adds any kind (the same kind twice is fine: two engine tones on different devices, say).
- **Shortcuts**: the controller icon beside every volume (master, devices, effects) opens a small window for
  **Volume Up**, **Volume Down** (5% each) and **Mute**: key combinations (with Ctrl, Alt or Shift, working in the
  game too) or controller / button box buttons. An icon that's blue has some set. An effect's shortcuts work while
  its profile is in use.

**If a shaker doesn't move:** open the device and press **Test** (a second at the frequency it's strongest at). If
that shakes it, the device and amplifier are right and it's the effects' levels; if not, check the amplifier, its
input, and Windows' volume for that device. The log (Network → Logs, SimEffects) has a line per device after it's
played 5 seconds: the format Windows asks for, the channels it plays on, and any gap.

## Games

| Game | Telemetry | In the game |
|---|---|---|
| **Forza Horizon** 4 / 5 (and 6, if it keeps the format) | Data Out, UDP, 324-byte packets | Settings → HUD and Gameplay → **Data Out** On, **IP** 127.0.0.1, **Port** the profile's (5300) |
| **Forza Motorsport** (2023) and 7 | Data Out, UDP, 331 / 311 bytes | Settings → Gameplay & HUD → **Data Out**, IP 127.0.0.1, port the profile's (5301) |
| **iRacing** | its shared memory (irsdk), 60 a second: no port | nothing |
| **Assetto Corsa Competizione** | its shared memory (physics and graphics pages): no port | nothing |

Only one program can listen on a UDP port: if SimHub (or anything else) has the port, give the profile another port
and set the same in the game.

## The effects

| Effect | From | Sound |
|---|---|---|
| **Crashes** | the car's horizontal acceleration passing *Threshold* (g) | a knock of *Length*, stronger the harder the hit |
| **Impacts** | a wheel's suspension moving faster than *Threshold* (m/s): bumps, potholes, landings | a knock |
| **Curbs** | Forza: wheels on a rumble strip; iRacing: the surface under the car; ACC: its kerb vibration | the tone switched on and off at a rate that follows the speed |
| **Road Feel** | the suspension's movement above *Threshold*, more with speed | rumble (a tone wandering in pitch and strength) |
| **Off-Road** | Forza: the surface rumble; iRacing: grass, dirt, sand, gravel; ACC: wheels off the track | low rumble |
| **Gear Shift** | the gear changing while moving | a short knock |
| **Wheel Slip** | Forza: the tyres' combined slip; ACC: its slip vibration (not iRacing: it doesn't give it) | a tone, from *Threshold* past grip |
| **ABS** | iRacing and ACC: ABS working; Forza (no ABS flag): a front wheel locking under braking | a tone pulsing 14 times a second |
| **Rev Limiter** | RPM above *Threshold* of the redline | a tone pulsing 10 times a second |
| **Engine** | RPM (pitch from *Idle Hz* to *Redline Hz*) and throttle; *Idle Level* is how much it shakes idling | a tone |

*Sensitivity* scales what an effect reads; its volume scales what it plays. Each game's profile starts with the
effects in the order above (Motorsport without Off-Road, iRacing without Wheel Slip) and thresholds suited to the
game's numbers (iRacing's shock speeds are smaller than Forza's suspension speeds, say). The thresholds and ACC's
scaling of its own vibration values are starting points, not measured in the games yet.

## Transducers

The ranges in the Add Device list, from the makers' figures narrowed to where each is strong; Low and High can be
changed on the device afterwards.

| Transducer | Range | Strongest |
|---|---|---|
| ButtKicker Gamer 2 / Gamer Pro | 10–80 Hz | 35 Hz |
| ButtKicker Advance / Simrig, LFE | 8–100 Hz | 30 Hz |
| ButtKicker Mini LFE | 12–100 Hz | 40 Hz |
| Dayton Audio BST-1 / BST-1 Pro, BST-300EX | 20–80 Hz | 40 Hz |
| Dayton Audio BST-2 | 20–100 Hz | 40 Hz |
| Dayton Audio TT25 Puck | 30–120 Hz | 50 Hz |
| Dayton Audio Mini Puck (TT10 / TT20) | 40–150 Hz | 60 Hz |
| AuraSound AST-2B-4 Pro Bass Shaker | 15–80 Hz | 40 Hz |
| AuraSound Bass Shaker | 20–80 Hz | 40 Hz |
| Clark Synthesis TST239 / TST429 | 10–120 Hz | 35 Hz |
| Subwoofer | 20–120 Hz | 45 Hz |
| Other bass shaker | 20–100 Hz | 40 Hz |

## Code

| File | |
|---|---|
| `BassShakerApplet.cs` | The tool: settings, engine, shortcuts, the view; Game Switching (process list every 3 s, a program starting picks its profile once); the tray item |
| `BassShakerView.cs` | The tab: the top line (on/off, status, profile, New, Master), devices, the profile card, the meters, the effect rows (fold/open, settings sliders, Plays On, Test, Remove, drag by the grip to reorder), Confirm? buttons |
| `ShakerSettings.cs` | `BassShaker.json`: master, devices (`ShakerDevice`: Windows' device id, transducer, Low/High, channels, volume), profiles (`ShakerProfile`: game, port, programs, effects), effects (`ShakerEffect`: kind, volume, settings, the devices it's off on, its level on each), the on / off shortcut; `IVolume` and `VolumeKeys` |
| `Telemetry.cs` | `Game`, `Games` (titles, which use a port, their source), `TelemetryFrame` (the same for every game), `ITelemetrySource` |
| `ForzaSource.cs` | Forza's Data Out over UDP: the sled (232 bytes) and the dash at 232, or 244 in Horizon; the newest packet only |
| `IRacingSource.cs` | iRacing's shared memory: header, variable list by name, the newest of its buffers; redline and idle from the session YAML |
| `AccSource.cs` | ACC's shared memory: the physics page (field place × 4) and the graphics page's status |
| `Effects.cs` | `EffectKind`, `EffectKinds` (each kind's title, sound, settings and default values per game), `EffectSignal` (an effect as it plays, read by the outputs; `Gains` per device from `Route`), `EffectRunner` (the effects worked out from a frame; knocks with a cooldown; the meters' levels; Test) |
| `DefaultProfiles.cs` | Each game's profile: its effects in order, port and programs |
| `ShakerEngine.cs` | The thread: the profile's source, the effects, an output per device (reopened when one is plugged in or changed); runs while on, or for a Test |
| `ShakerOutput.cs` | A device's sound: each effect's tone, rumble, pulses and knocks within the device's Low/High, mixed, times its volume and the master, then filtered to the device's range (two low-passes at 1.25 × High, a high-pass at half Low) and limited (an envelope limiter at 0.9, 0.2 s release; no clipping, which would add harmonics), on its channels; shared-mode WASAPI at 50 ms; after 5 s it logs the format, frames per read and the longest wait between reads |
| `Transducers.cs` | The transducer types and their ranges |
| `Volume.cs` | `VolumeHotkeys` (every volume's shortcuts and the on / off one, keys and controller buttons), `OnOffKeyWindow`, `VolumeKeysWindow` (the shortcut window), `VolumeControl` (mute, slider, %, shortcut button) |
| `Meters.cs` | `SpectrumMeter` (the column per effect with held peaks) and `MiniMeter` (the line in a row) |
| `AddDeviceWindow.cs` | Add Device: device, transducer, channels |
