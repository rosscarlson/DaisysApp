# Binder

*A module of [Daisy's App](../../../README.md) (since 3.36.0; a SimEffects tool in 3.36.0, a module of its own since 3.36.1),
from FotG Controls (a small app first made for Fate of the Galaxy). In Daisy's App it's a sub-tab of the
[Gaming](../Gaming/README.md) tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's
installed to `modules\Binder\` next to `DaisysApp.exe`.*

A game's key, mouse and controller bindings set up outside the game, before you play, for any game a JSON file describes
(a **title**). What you bind is written into the game's own files a moment later (no Save button), and the first time
Binder changes a file it keeps a copy beside it (`<file>.daisysapp.bak`).

Titles it knows: **Fate of the Galaxy** (FreeSpace Open, installed by Knossos.NET), **Elite Dangerous** and **Assetto
Corsa**, written into their files (3.36.0); and (3.36.6) **Star Citizen**, **Aces of Thunder**, **G-Rebels**, **Microsoft
Flight Simulator 2024**, **Star Wars: Squadrons**, **War Thunder**, **iRacing**, **Forza Horizon 4, 5 and 6**, **Assetto
Corsa Competizione**, **RaceRoom**, **rFactor 2** and **Project CARS 2 and 3**, whose bindings Binder keeps itself (a
list to set in the game: their own files are in Xbox's cloud, binary, or not yet known; see `sheet` below).

## The tab

- **Titles** (top left): the titles set up. **Add** sets up another: pick the title, its folder (found by itself where
  it can, or **Browse**), and what Binder found there (a ✓ or ✗ per file it needs; the game's defaults aren't needed);
  **Copy bindings from** another title set up, or nothing (the game's own). Under the list, the picked title's details:
  what it is, its folder (the folder button changes it), what was found, its own settings (Fate of the Galaxy: the
  **Preset** to write, an existing one picked from the list or a new name, and the game's **Use Mouse**), **Copy from**
  another title (Confirm?), **Reset All** to the game's defaults (Confirm?) and **Remove** (off the list; the game's
  files stay). **Running** beside a title while its game runs: a game reads its bindings when it starts.
- **Devices**: the wand (**Template Wizard**, left of Refresh) opens the wizard (below). Every controller as the title's game sees it, with a green dot while it has input, its buttons, axes
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

## The Template Wizard (3.36.6)

A template is your bindings for a kind of game, set up once and then copied into each new title. The wand on the
Devices block replaces the right side with the wizard: first **Driving** or **Flight** (a JSON file each, `Templates\`:
more kinds, a shooter's, can be added), and **Your Templates** (**Open**, **Remove**). Picking a kind opens its template
(the one filled in before, where it was left, or a new "My Flight Template"), a section at a time, the example device
above it: Flight is Main Axes (pitch, roll, yaw, throttle), Thrusters (the three axes and six directions), Common
Buttons (fire, fire secondary, target, countermeasures, boost, gear, free look), then the more advanced Throttle and
Flight, Targeting, Weapons, Power and Systems, Rotation Buttons, Views and Interface; Driving is Wheel and Pedals,
Shifting, Common Buttons, then Car Aids, Pit and Race, Views and Keyboard Driving. Each action has two slots, bound as a
title's are; where there's a usual key (Mouse Left for Fire, G for Next Target) it's filled in already and can be
changed or cleared. Over the bindings: the template's name (renamed by typing), **Step n of m**, **Back**, **Next**
(**Finish** on the last: back to the first step) and close; the chips jump to any section. It's saved as it changes, in
Binder.json.

A template is offered first in **Copy bindings from** (Add) and **Copy from** (a title's details) as "Template: <name>".
Copying from it reports, besides what couldn't be bound, **Not in your template** (this title's actions with a role the
template hasn't bound) and **In your template, not in this game**.

## Copying from another title

Each action has a **role** (what it does, in words every title shares: `pitch`, `fire.primary`, `gear.up`…). Copying
gives each action the bindings of the other title's action with the same role (else the same id, else the same name),
each in the first slot that takes it; the game's own bindings stay in slots left over (Fate of the Galaxy's T for Target
Next beside a joystick button). Actions with nothing like them keep theirs. What happened is shown above the bindings:
how many were copied, **Couldn't bind** (the binding and why: Assetto Corsa doesn't take hats…), **Check these axes**
(an axis copied inverted: games don't agree which way is up; or one moved between FSO's and DirectInput's numbering
without its device plugged in) and **Nothing like it here**. Controllers are the same devices in both titles.

## Titles: one JSON file each

Beside the module in `modules\Binder\Titles\` (the ones that come with it), and your own in
`%APPDATA%\DaisysApp\Binder Titles\` (one with a built-in title's `id` replaces it). Comments (`//`) are allowed.

| Field | |
|---|---|
| `id`, `name` | Its id (kept in Binder.json) and name |
| `kind` | `racing`, `flight` or `space`: the example device |
| `format` | The code that reads and writes its files: `fso`, `elite`, `ac`, or `sheet` (Binder keeps them; a new kind of file needs code: `Formats/`) |
| `folderHint` | What the folder to pick is |
| `find` | Where the folder usually is, tried in order: `%VARIABLES%`, `documents:` (Documents, wherever OneDrive has it), `steam:` (a folder in any Steam library) |
| `programs` | Its programs (without .exe): **Running** while one is |
| `options` | The format's settings (below) |
| `groups` | `{ "name", "match", "context" }`: an action's group by a pattern on its id (first match wins), and its context (bindings conflict only within one) |
| `actions` | `{ "id", "label", "group", "role", "axis", "centred", "keys", "slots", "hidden" }`: names, groups and roles for actions the game's files list, or every action (Assetto Corsa, `sheet`). `keys`: its defaults as written ("Up", "Shift+T", "Mouse Left", "Mouse Wheel Up", "Mouse Y"); `centred`: false for a throttle or pedal (its bar fills from the left); `hidden`: not shown (kept as it is) |

**Roles** the example device draws: `steer`, `gas`, `brake`, `clutch`, `handbrake`, `gear.up`, `gear.down`, `gear.1`–`gear.7`,
`gear.r`; `pitch`, `roll`, `yaw`, `throttle`, `strafe.x`, `strafe.y`, `fire.primary`, `fire.secondary`; and the buttons that
stand in for an axis: `pitch.up`/`pitch.down`, `roll.left`/`roll.right`, `yaw.left`/`yaw.right`, `steer.left`/`steer.right`,
`throttle.up`/`throttle.down`. Others used for copying: `strafe.left/right/up/down`, `thrust.forward/back`, `thrust.z`,
`throttle.zero/max/rel`, `boost`, `match.speed`, `weapon.next/prev`, `countermeasure`, `target.next/prev/ahead/attacker`,
`target.hostile.next/prev`, `target.subsystem.next/prev`, `target.targets.target`, `power.engines/weapons/shields/reset`,
`map`, `look.free/centre/up/back/left/right`, `camera.next`, `hud`, `lights`, `horn`, `pause`, `abs.up/down`, `tc.up/down`,
`bias.up/down`, and the templates' (`Templates/*.json`: `flaps.up/down`, `airbrake`, `wheelbrake`, `landing.gear`,
`pitlimiter`, `wipers`, `ignition`, `starter`, `map.up/down`…), and any you make up (copying matches them by name).

A template's JSON (`Templates\*.json`, or your own in `%APPDATA%\DaisysApp\Binder Templates\`): `id`, `name`, `order`
(among the first step's buttons), `kind` (the example device), `kinds` (the titles' kinds it's for: listed first when
copying), `sections` (`{ "name", "advanced", "actions" }`; an action's `id` is its role, with `label`, `axis`, `centred`,
`keys` as a title's).

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
- **`sheet`**: a game whose files Binder doesn't write. Every action is in the title's JSON; the bindings are kept in
  `%APPDATA%\DaisysApp\Binder Sheets\<id>.json` (only those that differ from the defaults), two slots each, anything
  (an axis for an axis). The folder is optional, only to see the game's there; option `file` names where the game keeps
  its own (a ✓ if it's there). **Copy List** puts them on the clipboard as text, a group at a time, to set in the game.
  Defaults are only those that could be checked (each file's comment says where from); the rest are to add.

## Files

`%APPDATA%\DaisysApp\Binder.json`: the titles set up (the title, its folder, its own settings), the one showing, and
each one's group, and your templates. The bindings themselves are in the games' files (a `sheet` title's in
`Binder Sheets\`). Log: `Binder-<date>.log`.

## Code

| File | |
|---|---|
| `BinderApplet.cs` | The module (an `IApplet`; `[Applet]` with `Group = "GamingTools"`, so it's a sub-tab of Gaming), `BinderSettings` (Binder.json) |
| `BinderView.cs` | The tab: Titles (and Add), Devices, the example device, the copy report, the bindings (groups, filters, rows, conflicts by context), capturing (keys from the window's messages as scancodes, the mouse, the input's buttons, hats and axes), the live state (held bindings bold, axis bars), writing a moment after a change |
| `Model.cs` | `Bind` (one input, the same for every game: a key by scancode with Shift, Ctrl, Alt; a mouse button or axis; a controller's button, hat or axis by `DeviceId`), `BinderAction`, `Keys` (scancodes with FSO's and Elite's names, and virtual keys for Assetto Corsa) |
| `Titles.cs` | `TitleDef` (a title's JSON), `Titles` (reading them), `Paths` (`documents:`, `steam:`, the once-only backup, atomic writes), `TitleSetup`, `Check`, `TitleData`, `IBindFormat` (what a format does) |
| `Describe.cs` | A binding as text |
| `Migrate.cs` | Copying another title's (or a template's) bindings by role, and the report; an axis between SDL's and DirectInput's numbering |
| `ExampleDevice.cs` | The example device, drawn |
| `AxisBar.cs` | The bar under an axis binding (from FotG Controls) |
| `Input/InputSource.cs` | `IInputSource`, `LiveDevice`, and `DirectInputSource` (Core's controller list, `DaisysApp.Controllers`, read on a thread of its own) |
| `Input/SdlSource.cs` | The game's SDL2.dll (from FotG Controls), for FSO |
| `Formats/FormatList.cs` | The formats a title can name |
| `Formats/Fso/` | FSO (from FotG Controls): `FsoFormat.cs` (FSO's bindings and ours, Joystick 0–3, the preset and Use Mouse), `FsoGameInstall.cs` (finding the mod, its actions from the engine and its tables, presets, the ini), `FsoActions.cs` (the engine's actions and defaults), `FsoBind.cs`, `FsoKeyNames.cs` |
| `Formats/EliteFormat.cs` | Elite Dangerous' .binds |
| `Formats/AcFormat.cs` | Assetto Corsa's controls.ini |
| `Formats/SheetFormat.cs` | `sheet`: the bindings kept by Binder, Copy List |
| `Template.cs` | `TemplateDef` (a template's JSON), `MyTemplate` (yours, in Binder.json), `Templates` (reading them, a template as a title's data), `TemplateFormat` |
| `Titles/*.json` | The titles that come with it (copied beside the module) |
| `Templates/*.json` | The wizard's kinds: `racing.json` (Driving), `flight.json` |

The project references `src/DaisysApp.Controllers` (the controllers, through DirectInput, every module shares). Translations in `lang/`.
