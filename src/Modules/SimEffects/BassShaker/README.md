# SimEffects → Shaker

*A tool of [SimEffects](../README.md) (since 3.2.0; "Bass Shaker" until 3.3.0: its folder, id and settings file `BassShaker.json` keep that name).*

The game's telemetry, turned into low-frequency sound and played to tactile transducers ("bass shakers") on one or
more audio devices, so the rig feels the car: as SimHub's ShakeIt Bass Shakers.

## Using it

- **ON / OFF** (top left) starts or stops it; the controller icon beside it sets a shortcut for that (keys with
  Ctrl, Alt or Shift, or a controller / button box button; it listens as soon as it opens). While it's on, what it's doing shows next: *Waiting for Forza Horizon on port 8000*,
  *Forza Horizon: driving*, *not driving* (a menu, pause or replay: everything stops), or a problem (the port taken by
  another program). It stays on or off across restarts. The tray menu has **Bass Shaker on** too.
- **Title bar bubble**: while it's on, a green bubble in the title bar always names the profile in use,
  e.g. **Shaker: Forza Horizon** (since 3.16.3; click it to come here; since 3.17.1 there's no checkbox to leave it out).
- **Master** (top right): the volume of everything.
- **Devices** (left): **Add Device** asks for the playback device, the **transducer** on it, its **position** and its
  **channels**.
  The transducer sets the range of frequencies the device is ever sent (*Low* / *High*): effects are kept inside it, so
  a ButtKicker gets its deep 10–80 Hz and a small puck isn't sent rumble it can't make. Two shakers on one stereo amp
  are two devices, **Left** and **Right**; **LFE** plays on a surround card's subwoofer channel.
  **Position** (since 3.3.2) is which wheels a device stands for: **All Wheels**, **Front**, **Rear**, **Left Side**,
  **Right Side**, or one corner. Every effect is worked out wheel by wheel, so a device only feels its wheels: a kerb
  under the front left wheel shakes a Front device and not a Rear one. **Split (left / right wheels)** channels play
  the position's left wheels on the left channel and its right wheels on the right: one amplifier with **Front** and
  Split drives front left and front right; **All Wheels** and Split is side to side. So, for example:
  front / rear from one stereo amp = two devices, Left channel at Front and Right channel at Rear; side to side = one
  device, All Wheels, Split; all four corners = two stereo amps, one at Front and one at Rear, both Split. Opened
  (the arrow), a device can change its transducer, position, channels, Low and High, or be removed (**Remove**, bottom
  right). Each has **Test** (a second at the frequency it's strongest at; left of its volume, since 3.18.3), its volume and a meter of what it's playing. A device's volume and mute are Windows' own for that device (since 3.3.1), as on the Audio tab: moving the slider, muting or a shortcut changes the device in Windows straight away, and a change made in Windows or on the Audio tab shows here; two entries of one device (Left and Right) share it. A device that isn't plugged in says *Not
  connected* and starts playing when it is (checked every 3 seconds).
- **Profiles** (since 3.18.0): the game and the profile are SimEffects' ([Profiles](../Profiles/README.md)): the Shaker
  reads the profile in use's game, and keeps its on / off and its **effects** per profile (each profile's made from its
  game's defaults the first time). **Reset Effects** (on the Effects card) puts the profile's effects back to its
  game's own. The devices and Master are the same in every profile.
- **Meters** (top right): a column of segments per effect, in the effects' order, lit as it shakes, its highest point
  held for a moment. A muted effect's column stays dark. **Pop out** (the icon at its top right; since 3.5.1), or
  right-click the meters → **Make Widget** (since 3.7.0), shows them as a widget, as Hardware → Performance's are (Core's widget): drag it anywhere and stretch it, set its opacity
  and On top, then **Done**; right-click it to move it again, its **Delete** closes it. It stays open at the next start
  (`MetersWidget`), and moves while the tab isn't showing. While it's open, a **Widgets** card at the bottom left
  lists it with **Edit** and **Delete** (since 3.8.6, Core's `Widget.ListCard`; a deleted one can be made again: 3.7.0
  failed with "already the logical child of another element").
- **Effects**: each one's meter, mute, volume and shortcuts show folded; opened (the arrow) it has its settings,
  **Plays On** (each device by its name without Windows' description in brackets: on or off for this effect, and a
  level for how much of it that device gets), then **Test** (it plays for a moment, even when off) and **Remove**
  at the bottom right. Drag one by its grip (left) onto another to move it: above it in its top half, below in its bottom
  half. **Add Effect** adds any kind (the same kind twice is fine: two engine tones on different devices, say).
- **Level Wizard** (beside Add Effect): a window, kept above the game (**On Top**), that takes you through the
  profile's effects one at a time, so each can be set while you drive. Its first step is its shortcuts (keys or
  controller / button box buttons, kept for next time): **Previous Step**, **Next Step**, **Level Up** and **Level
  Down** are needed; **Frequency**, **Sensitivity**, **Threshold** and **Length** Up / Down and **Solo / All** are
  optional. Each effect's step shows its meter, its level and settings as sliders (they can always be moved by hand),
  and each setting's shortcuts, or *No shortcut* in red. A step moves Level 5%, Frequency 2 Hz (the Engine's whole
  range moves with it), and the others by their slider's step. **Solo** (the default) plays only the step's effect;
  **All** plays every effect muted or not as it was when the wizard opened, with the changes made since, to feel it
  in context. Closing the wizard keeps the changes and puts every effect's mute back as it was. While it's open the tab
  follows it as it happens (for a second screen): the step's effect is outlined and bold in the list, its meter and
  its column in the meters brighter, and every change shows on its sliders.
- **Shortcuts** (here a key by itself, an F-key or a number pad key say, is a shortcut too, as well as key
  combinations and controller buttons; since 3.3.4): the controller icon beside every volume (master, devices, effects) opens a small window for
  **Volume Up**, **Volume Down** (5% each) and **Mute**: key combinations (with Ctrl, Alt or Shift, working in the
  game too) or controller / button box buttons. An icon that's blue has some set. An effect's shortcuts work while
  its profile is in use.

**Settings → SimEffects → Shaker** (since 3.7.0): **Test level**, how strong every **Test** button plays (10–100%,
100% by default; before, a device's Test played at 60% and an effect's at 80%). An effect's Test is still at that
effect's own volume and its devices' levels, times the master.

**If a shaker doesn't move:** open the device and press **Test** (a second at the frequency it's strongest at). If
that shakes it, the device and amplifier are right and it's the effects' levels; if not, check the amplifier, its
input, and the device's volume (its slider here is Windows' volume for it). The log (Tools → Logs, SimEffects) has a line per device after it's
played 5 seconds: the format Windows asks for, the channels it plays on, and any gap.

## Games

| Game | Telemetry | In the game |
|---|---|---|
| **Forza Horizon** 4 / 5 (and 6, if it keeps the format) | Data Out, UDP, 324-byte packets | Settings → HUD and Gameplay → **Data Out** On, **IP** 127.0.0.1, **Port** the profile's (8000) |
| **Forza Motorsport** (2023) and 7 | Data Out, UDP, 331 / 311 bytes | Settings → Gameplay & HUD → **Data Out**, IP 127.0.0.1, port the profile's (5301) |
| **iRacing** | its shared memory (irsdk), 60 a second: no port | nothing |
| **Assetto Corsa Competizione** | its shared memory (physics and graphics pages): no port | nothing |

Only one program can listen on a UDP port: if SimHub (or anything else) has the port, give the profile another port
and set the same in the game.

## The effects

| Effect | From | Sound |
|---|---|---|
| **Crashes** | the car's horizontal acceleration passing *Threshold* (g) | a hit of *Length*: three quarters strength at the threshold, all of it at twice the threshold |
| **Impacts** | a wheel's suspension moving faster than *Threshold* (m/s): bumps, potholes, landings | a hit: over half strength at the threshold, all of it at twice |
| **Curbs** | Forza: wheels on a rumble strip; iRacing: the surface under the car; ACC: its kerb vibration | the tone switched on and off at a rate that follows the speed |
| **Road Feel** | the suspension's movement above *Threshold*, more with speed | rumble (a tone wandering in pitch and strength) |
| **Off-Road** | Forza: the surface rumble; iRacing: grass, dirt, sand, gravel; ACC: wheels off the track | low rumble |
| **Gear Shift** | the gear changing while moving | a short knock |
| **Wheel Slip** | Forza: the tyres' combined slip; ACC: its slip vibration (not iRacing: it doesn't give it) | a tone, from *Threshold* past grip |
| **ABS** | iRacing and ACC: ABS working; Forza (no ABS flag): a front wheel locking under braking | a tone pulsing 14 times a second |
| **Rev Limiter** | RPM above *Threshold* of the redline | a tone pulsing 10 times a second |
| **Engine** | RPM (pitch from *Idle Hz* to *Redline Hz*) and throttle; *Idle Level* is how much it shakes idling | a tone |

**Wheel by wheel:** Road Feel and Impacts follow each wheel's suspension; Curbs, Off-Road and Wheel Slip follow each
wheel in Forza (iRacing and ACC give them for the whole car, so every wheel gets the same); Impacts go mostly to the
wheels that hit (30% to the others); Crashes go to the side the hit came from (slowing hard is a hit in front, being
pushed right a hit on the left; 40% to the other wheels); the Engine, Gear Shift, ABS and the Rev Limiter are the
same on every wheel. A device with several wheels takes the most any of them asks (Road Feel and Off-Road: their
average).

**A hit** (Crashes and Impacts, since 3.10.2) is the strongest thing the shakers play: full strength at once and held
for the first third of its *Length*, then dying away; its pitch starts at the shaker's strongest frequency (at least
twice the effect's) and drops to the effect's *Frequency* (the body of it), and its wave is rounded off towards a
square for more push each cycle. While it plays, the other effects on that device are turned down (to 30% at its
height), so it isn't lost under the engine and road. Gear Shift stays a plain knock. An effect's **Test** plays one
knock or hit (before 3.10.2, one every 0.45 s).

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

The games' telemetry (`Game`, `TelemetryFrame`, each game's reader) is the module's, in `../Telemetry/` (since 3.5.3; see [the module's guide](../README.md)).

| File | |
|---|---|
| `BassShakerApplet.cs` | The tool: settings, engine, shortcuts, the view; its part in the SimEffects profiles (`SimProfiles.Register`: on / off; `Renamed`: its effects follow a profile's name); the tray item |
| `BassShakerApplet.cs` (settings page) | Settings → SimEffects → Shaker: the Test level (`ShakerSettings.TestLevel`, handed to `EffectSignal.TestLevel` / `ShakerOutput.TestLevel` at each Test) |
| `BassShakerView.cs` | The tab: the top line (on/off, the title bar bubble (`ShowBadge`), status, profile, New, Master), devices, the meters, the Effects card (Reset Effects, Level Wizard, Add Effect), the effect rows (fold/open, settings sliders, Plays On, Test, Remove, drag by the grip to reorder), Confirm? buttons |
| `ShakerSettings.cs` | `BassShaker.json`: master, devices (`ShakerDevice`: Windows' device id, transducer, Low/High, channels, position, volume), `Position`, `Wheels` (each position's wheels), each SimEffects profile's effects by its name (`ShakerProfile`, `ForProfile()`), effects (`ShakerEffect`: kind, volume, settings, the devices it's off on, its level on each), the on / off shortcut; `IVolume` and `VolumeKeys` |
| `Effects.cs` | `EffectKind`, `EffectParam` (with the `Knob` the Level Wizard's shortcuts move), `EffectKinds` (each kind's title, sound, settings and default values per game), `EffectSignal` (an effect as it plays, read by the outputs: `Wheel` per wheel and `KnockWheel` per knock, `For`/`KnockFor` a device's wheels; `Gains` per device from `Route`), `EffectRunner` (the effects worked out from a frame; knocks with a cooldown; the meters' levels; Test) |
| `DefaultProfiles.cs` | Each game's profile: its effects in order, port and programs |
| `ShakerEngine.cs` | The thread: the module's telemetry (`SimData.Open()`: the profile in use's game, read once for every tool), the effects, an output per device (reopened when one is plugged in or changed); runs while on, or for a Test |
| `ShakerOutput.cs` | A device's sound, in one feed or two (Split: its position's left wheels on channel 0, right wheels on 1), each: each effect's tone, rumble, pulses and knocks within the device's Low/High (hits: `Voice.Hit`, held, pitch dropping from the transducer's `PeakHz`, tanh-rounded, rendered first to fill `Feed.Duck`, which turns the other voices down), mixed, times the master (the device's own volume is Windows', `DeviceVolumes`), then filtered to the device's range (two low-passes at 1.25 × High, a high-pass at half Low) and limited (an envelope limiter at 0.9, 0.2 s release; no clipping, which would add harmonics), on its channels; shared-mode WASAPI at 50 ms; after 5 s it logs the format, frames per read and the longest wait between reads |
| `Transducers.cs` | The transducer types and their ranges |
| `LevelWizard.cs` | The Level Wizard (`Showing` and `Changed` for the tab, which re-draws the effects at most every 100 ms): the shortcuts step (`WizardKeys` as Core `Hotkeys` added by `AddKeys` at the start, listed in Settings → Shortcuts, registered only while it's open, "Needed"), a step per effect (meter, Level and its `EffectParam`s as sliders, each `Knob`'s keys or "No shortcut"), Solo / All (`ApplySolo`: mutes saved when it opens, put back when it closes), `Nudge` |
| `DeviceVolumes.cs` | Each device's volume and mute = Windows' endpoint volume (`AudioEndpointVolume`): read when the device is found, the app's changes sent every 100 ms, Windows' changes taken from its notification; entries of one device share it |
| `Volume.cs` | `VolumeHotkeys` (every volume's shortcuts and the on / off one as Core `Hotkeys`, put in place again when devices, effects or the profile change; `For(volume)` for Core's `HotkeyWindow`), `VolumeControl` (mute, slider, %, shortcut button) |
| `Meters.cs` | `SpectrumMeter` (the column per effect with held peaks; the Level Wizard's effect bright, its label bold; a second one is the meters' widget, in Core's `Widget`) and `MiniMeter` (the line in a row; `Bright`) |
| `AddDeviceWindow.cs` | Add Device: device, transducer, channels |
