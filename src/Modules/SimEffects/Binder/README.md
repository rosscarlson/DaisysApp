# SimEffects → Binder

*A tool of [SimEffects](../README.md) (since 3.36.0), from FotG Controls (a small app first made for Fate of the Galaxy). Switch it
on or off in Settings → General → Modules (SimEffects' sub-modules).*

A game's key, mouse and controller bindings set up outside the game, before you play, for any game a JSON file describes
(a **title**). What you bind is written into the game's own files a moment later (no Save button), and the first time
Binder changes a file it keeps a copy beside it (`<file>.daisysapp.bak`).

Titles it knows (3.36.0): **Fate of the Galaxy** (FreeSpace Open, installed by Knossos.NET), **Elite Dangerous** and
**Assetto Corsa**.

## The tab

- **Titles** (top left): the titles set up. **Add** sets up another: pick the title, its folder (found by itself where
  it can, or **Browse**), and what Binder found there (a ✓ or ✗ per file it needs; the game's defaults aren't needed);
  **Copy bindings from** another title set up, or nothing (the game's own). Under the list, the picked title's details:
  what it is, its folder (the folder button changes it), what was found, its own settings (Fate of the Galaxy: the
  **Preset** to write, an existing one picked from the list or a new name, and the game's **Use Mouse**), **Copy from**
  another title (Confirm?), **Reset All** to the game's defaults (Confirm?) and **Remove** (off the list; the game's
  files stay). **Running** beside a title while its game runs: a game reads its bindings when it starts.
- **Devices**: every controller as the title's game sees it, with a green dot while it has input, its buttons, axes
  and hats, and what the game calls it (Fate of the Galaxy: which of its **Joystick 0–3** it is, a drop-down; Elite: its
  name in the game; Assetto Corsa: its **JOY** number, or Not listed yet).
- **The example device** (top right): a pretend device for the title's kind, showing what's bound and moving with it,
  so bindings can be tried without the game. Racing: the wheel (turns with Steering), the clutch, brake, gas and
  handbrake, the H-shifter's gears and the paddles. Flight and space: the stick (Pitch and Roll), Yaw, the throttle,
  Strafe, **Fire** and **Fire 2**. Then a cap for each other button bound, lit while it's pressed. An axis that isn't
  bound is shown from the buttons or keys that do the same (Pitch Up and Pitch Down).
- **Bindings**: the title's actions a group at a time (its chips; **All**), with **Search** (names and bindings),
  **Changed** (not the game's default) and **Conflicts** (the same input on two actions; Elite's ship, SRV, on foot,
  landing and cameras each count on their own). Each row: the action (the game's default under it, to bind by; the
  game's own name in the tooltip where Binder uses another, as Roll for FSO's Bank), its slots (Primary and Secondary;
  Elite's axes one; Assetto Corsa's Controller and Key), **Invert** for an axis, and back to the default. Click a slot,
  then press a key (Shift, Ctrl and Alt combinations where the game takes them), a mouse button or the wheel, a
  controller's button or hat, or move an axis half its range (or the mouse firmly); **Esc** or **Cancel** stops,
  **Clear** empties it, and a right-click on a slot clears it. A binding the game can't take says why and waits for
  another. A binding goes bold while it's pressed, and an axis has a bar under it that follows it. A red border: its
  input is on another action too (the tooltip says which).

## Copying from another title

Each action has a **role** (what it does, in words every title shares: `pitch`, `fire.primary`, `gear.up`…). Copying
gives each action the bindings of the other title's action with the same role (else the same id, else the same name),
each in the first slot that takes it; the game's own bindings stay in slots left over (Fate of the Galaxy's T for Target
Next beside a joystick button). Actions with nothing like them keep theirs. What happened is shown above the bindings:
how many were copied, **Couldn't bind** (the binding and why: Assetto Corsa doesn't take hats…), **Check these axes**
(an axis copied inverted: games don't agree which way is up; or one moved between FSO's and DirectInput's numbering
without its device plugged in) and **Nothing like it here**. Controllers are the same devices in both titles.

## Titles: one JSON file each

Beside the module in `modules\SimEffects\Binder\Titles\` (the ones that come with it), and your own in
`%APPDATA%\DaisysApp\Binder Titles\` (one with a built-in title's `id` replaces it). Comments (`//`) are allowed.

| Field | |
|---|---|
| `id`, `name` | Its id (kept in Binder.json) and name |
| `kind` | `racing`, `flight` or `space`: the example device |
| `format` | The code that reads and writes its files: `fso`, `elite` or `ac` (a new kind of file needs code: `Formats/`) |
| `folderHint` | What the folder to pick is |
| `find` | Where the folder usually is, tried in order: `%VARIABLES%`, `documents:` (Documents, wherever OneDrive has it), `steam:` (a folder in any Steam library) |
| `programs` | Its programs (without .exe): **Running** while one is |
| `options` | The format's settings (below) |
| `groups` | `{ "name", "match", "context" }`: an action's group by a pattern on its id (first match wins), and its context (bindings conflict only within one) |
| `actions` | `{ "id", "label", "group", "role", "axis", "centred", "keys", "slots", "hidden" }`: names, groups and roles for actions the game's files list, or every action (Assetto Corsa). `keys`: its defaults as written ("Up", "Shift+T"); `centred`: false for a throttle or pedal (its bar fills from the left); `hidden`: not shown (kept as it is) |

**Roles** the example device draws: `steer`, `gas`, `brake`, `clutch`, `handbrake`, `gear.up`, `gear.down`, `gear.1`–`gear.7`,
`gear.r`; `pitch`, `roll`, `yaw`, `throttle`, `strafe.x`, `strafe.y`, `fire.primary`, `fire.secondary`; and the buttons that
stand in for an axis: `pitch.up`/`pitch.down`, `roll.left`/`roll.right`, `yaw.left`/`yaw.right`, `steer.left`/`steer.right`,
`throttle.up`/`throttle.down`. Others used for copying: `strafe.left/right/up/down`, `thrust.forward/back`, `thrust.z`,
`throttle.zero/max/rel`, `boost`, `match.speed`, `weapon.next/prev`, `countermeasure`, `target.next/prev/ahead/attacker`,
`target.hostile.next/prev`, `target.subsystem.next/prev`, `target.targets.target`, `power.engines/weapons/shields/reset`,
`map`, `look.free/centre/up/back/left/right`, `camera.next`, `hud`, `lights`, `horn`, `pause`, `abs.up/down`, `tc.up/down`,
`bias.up/down`, and any you make up (copying matches them by name).

### The formats

- **`fso`**: FreeSpace Open mods installed by Knossos.NET. The folder is the one with Knossos.NET.exe (or kn_portable,
  or a Knossos library). Option `mod`: the mod's id (`FotG_R`). The actions are the engine's (FSO 26's own list), with
  the mod's `controlconfigdefaults.tbl` and string tables applied; the bindings a preset,
  `data\players\presets\<name>.json` (every action, as the game writes them); which device is Joystick 0–3, and Use
  Mouse, the mod's settings ini (`data\<mod>_settings.ini`, its other lines kept). Controllers are read through the
  game's own SDL2.dll, so they're numbered as the game numbers them (its axes: Axis 1, 2… among the ones a device has).
  The game takes Alt and Shift combinations (no Ctrl), a controller's first 8 axes, 128 buttons and first hat, and four
  controllers. In the game: Options → Control Config → **Next Preset** until it shows the preset, then **Accept**.
  Only one game's SDL2.dll can be read at a time: another one needs a restart.
- **`elite`**: Elite Dangerous. The folder is `Options\Bindings` in `%LOCALAPPDATA%\Frontier Developments\Elite
  Dangerous`; the preset is the one its StartPreset file names (`Custom`), its newest `Custom.4.2.binds`, changed in
  place (only the actions changed; everything else as it was). A preset that's one of the game's own (no file there)
  has to be changed in the game once first, so it makes Custom. Options: `schemes` (its ControlSchemes folder: the
  defaults and DeviceMappings.xml, which names controllers such as ThrustMasterWarthogJoystick; others are their vendor
  and product in hex, `231D0201`) and `defaults` (`KeyboardMouseOnly.binds`). Read through DirectInput, as the game
  does. It takes a controller's first 32 buttons and four hats; it can't tell two of the same controller apart; a
  gamepad (an Xbox controller) is bound in the game. Bindings it has that Binder doesn't show (a gamepad's, a
  controller button held as a modifier) are kept as they are.
- **`ac`**: Assetto Corsa's `cfg\controls.ini` (in Documents). Every action is in the title's JSON: its id the section
  (`GEARUP`), or the section and key (`SHIFTER.GEAR_1`, `KEYBOARD.GAS`); its `slots` `axis`, `button` and `key`. A
  controller is its number in [CONTROLLERS] (a new one is listed there, with its product and instance GUIDs); a key is
  a Windows virtual key. One controller per section (Handbrake's axis and button), and every gear on one shifter; no
  hats, no Shift, Ctrl or Alt with keys; an axis' direction and range are its MIN and MAX (set in the game), so no
  Invert. Only the actions changed are written.

## Files

`%APPDATA%\DaisysApp\Binder.json`: the titles set up (the title, its folder, its own settings), the one showing, and
each one's group. The bindings themselves are only in the games' files. Log lines in `SimEffects-<date>.log` ("Binder: …").

## Code

| File | |
|---|---|
| `BinderApplet.cs` | The tool (an `IApplet`), `BinderSettings` (Binder.json) |
| `BinderView.cs` | The tab: Titles (and Add), Devices, the example device, the copy report, the bindings (groups, filters, rows, conflicts by context), capturing (keys from the window's messages as scancodes, the mouse, the input's buttons, hats and axes), the live state (held bindings bold, axis bars), writing a moment after a change |
| `Model.cs` | `Bind` (one input, the same for every game: a key by scancode with Shift, Ctrl, Alt; a mouse button or axis; a controller's button, hat or axis by `DeviceId`), `BinderAction`, `Keys` (scancodes with FSO's and Elite's names, and virtual keys for Assetto Corsa) |
| `Titles.cs` | `TitleDef` (a title's JSON), `Titles` (reading them), `Paths` (`documents:`, `steam:`, the once-only backup, atomic writes), `TitleSetup`, `Check`, `TitleData`, `IBindFormat` (what a format does) |
| `Describe.cs` | A binding as text |
| `Migrate.cs` | Copying another title's bindings by role, and the report; an axis between SDL's and DirectInput's numbering |
| `ExampleDevice.cs` | The example device, drawn |
| `AxisBar.cs` | The bar under an axis binding (from FotG Controls) |
| `Input/InputSource.cs` | `IInputSource`, `LiveDevice`, and `DirectInputSource` (Core's controller list, `DaisysApp.Controllers`, read on a thread of its own) |
| `Input/SdlSource.cs` | The game's SDL2.dll (from FotG Controls), for FSO |
| `Formats/FormatList.cs` | The formats a title can name |
| `Formats/Fso/` | FSO (from FotG Controls): `FsoFormat.cs` (FSO's bindings and ours, Joystick 0–3, the preset and Use Mouse), `FsoGameInstall.cs` (finding the mod, its actions from the engine and its tables, presets, the ini), `FsoActions.cs` (the engine's actions and defaults), `FsoBind.cs`, `FsoKeyNames.cs` |
| `Formats/EliteFormat.cs` | Elite Dangerous' .binds |
| `Formats/AcFormat.cs` | Assetto Corsa's controls.ini |
| `Titles/*.json` | The titles that come with it |
