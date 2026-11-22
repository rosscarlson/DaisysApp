# SimEffects → Profiles

*A tool of [SimEffects](../README.md) (since 3.18.0; before, the Shaker, Input Overlay, Arduino and AmbiLight each
had profiles of their own). Its settings file is `SimProfiles.json`.*

One set of profiles for everything in SimEffects: a profile per game (and one for no game, **Desktop**). The profile
in use decides two things:

- **The game every tool reads.** Its telemetry is read once (one reader, on the profile's port for Forza) and every
  tool takes it from there: the Shaker, Notify and the Wheel's shift lights, the Input Overlay. Switching to another
  game's profile switches them all to that game.
- **Every tool's settings.** Each sub-module keeps its settings per profile: whether it's on (all of them), and its
  own (below). A change made on a tool's tab is kept in the profile in use.

## Using it

- **Profile:** (at the right of the sub-tabs, on every SimEffects tab): the profile in use. Picking another switches
  every sub-module to it.
- **Play / Stop** (since 3.18.1; beside the drop-down; since 3.24.3 its colour says whether the profile's game is
  running: stopped, white, or green with the game running; playing, blue with the game running, or red without it) and its
  shortcut button (a key or a controller button, in Settings → Shortcuts too): stopped, every sub-module goes dark at
  once, its own ON kept (shown greyed on its tab) for when it plays again. A profile picked by hand or by a game plays.
- **Record** (since 4.20.0; right of Play, its shortcut's button right of Play's): see
  [Recordings](../Recordings/README.md).
- **Title bar** (since 4.17.18): one green bubble while any of the Shaker, Overlays, Wheel, AmbiLight or Arduino runs:
  the profile's **Short Name**, then each running, a line between them, e.g. **FH: Shaker | Overlay | Wheel**
  (clicking it opens Profiles). Before, the Shaker, Overlays, AmbiLight and Arduino had a bubble each; the Wheel none.
- **The blocks** (since 4.17.17; a Profile card and Auto Start Modules before): a block per game SimEffects reads
  (Forza Horizon, Forza Motorsport, iRacing, Assetto Corsa Competizione, Assetto Corsa, AC EVO) and **Desktop** (no
  game: no telemetry), 3 across, or 4 where that fills the rows better (fewer while the tab is narrow). **Units**
  (Analysis': **System**, Imperial / Metric, and **Temperature**, °F / °C; the same in every profile) are above them.
  - Every game has its profile (since 4.17.20; made at load with the game's port and processes, Auto Start Yes,
    where there's none; 4.17.17-19 had a checkbox to switch a game's on or off).
  - **Activate** (top right) uses and plays it; the one in use says **Active** (in the accent, with the accent's
    edge round the block; clicked while stopped, it plays). Its game starting, with Auto Start, makes it active too.
  - A line of what it's set to: the Forza version, the port, Auto Start, how many processes.
  - **Options** (folds; folded at first): **Short Name** (since 4.17.18; up to 8 letters, the title bar bubble's
    and first in the block's line: FH, FM, IR, ACC, AC, ACE from the game at first, Desktop's its name; emptied, the
    game's again), **Version** (Forza's: Forza Horizon 6, 5 or 4, Forza Motorsport (2023) or 7,
    or **Auto**, the one running (the newest until one has); it names the cars, and Analysis keeps each game's tracks
    and laps apart), **UDP Port** (Forza: set the game's Data Out to IP 127.0.0.1 and this port), **Auto Start**,
    **Processes** (Core's program list: more than 3 scroll, 3 tall; clicking in the box drops down the programs
    with a window, typing narrows them, a click or ↓ / ↑ and Enter adds one, or type a name and Add; since
    4.17.20, Add a running program… and **Defaults** before).
  - **Modules** (folds; the ones on named on its line): a chip per sub-module: the ones on start when the profile
    does (picked by hand or by a game), the others stop. A chip of the profile in use switches the tool at once, as
    its own ON / OFF does.
- **Auto Start** (Yes / No, per profile; Game Switching for all profiles before 3.23.4): with Yes, one of its
  **Processes** starting uses and plays the profile (once: picking another by hand meanwhile stays), and that program
  ending stops it, if it's still the one in use. A profile's Auto Start comes from the old Game Switching the first
  time (Yes where it was on and the profile has programs); a new profile's is Yes when it has programs.
- **Each game's defaults** (`Games.Port`, `Games.Programs`): Forza Horizon port 8001 (ForzaHorizon4/5/6.exe, fm4.exe,
  azimuth.exe), Forza Motorsport 5301 (its Steam and Microsoft Store programs, ForzaMotorsport7.exe), iRacing
  iRacingSim64DX11.exe, ACC AC2-Win64-Shipping.exe, Assetto Corsa acs.exe, AC EVO AssettoCorsaEVO.exe.
- Until 4.17.16 the profiles were named, edited, made (**New**: a game's, none, or a copy) and deleted on the Profile
  card; a profile per game now (a second one of a game, from before, is still in the Profile drop-down).

What each tool keeps per profile:

| Tool | Kept per profile |
|---|---|
| Shaker | On / off, and its effects (each profile has its own, made from its game's defaults the first time; **Reset Effects** on the Effects card puts them back). The devices and the master volume are the same for all |
| Input Overlay | On / off, Read From, history, background, the traces, the speeds (shown, Window Size, units), Record. Its place and the axes are the same for all |
| Arduino, Notify, Wheel | On / off (the boards, the wheel and the colours are the same for all) |
| Shift points (Notify and the Wheel) | First Light, Shift, Flash, Flash Rate, iRacing's own shift lights (each car's redline is the same for all) |
| Analysis | On / off (recording laps; on in every game's profiles the first time since 4.10.0, Forza Horizon's before). Its widgets, tracks, car names and laps are the same for all |
| AmbiLight | On / off, Frame Rate, Smoothing, Saturation, Black Level (the lights and their zones are the same for all) |

**A new install** (since 3.32.0): four profiles, as they're tuned on the rig: **Forza Horizon** (UDP port 8001),
**Forza Motorsport** (5301), **iRacing** and **Assetto Corsa Competizione**, each with Auto Start and its programs,
every tool's settings (the Shaker, Notify, the Input Overlay and Analysis on; Arduino, the Wheel and AmbiLight off), and
the Shaker's effects as they're tuned there (the devices are each PC's own). Forza Horizon is picked, stopped until a
game starts. Those effects are also each game's defaults: a new profile's, and **Reset Effects**.

**From 3.17** (the first time): a profile for each of the Shaker's (name, game, port, programs; its effects stay with
it) and Desktop, which is in use. Every profile starts with each tool's settings as they were; a tool
that had 3.17's **Switch On With Game** ticked is on in every game's profile. Game Switching (now each profile's Auto Start) is the Shaker's.

## Code

| File | What it is |
|---|---|
| `SimProfiles.cs` | `SimProfile` (name, `ShortName` (since 4.17.18; `DefaultShort` at Load / Add, `ShortOf`), game or none, `Version` (Forza's; null: the one running), port, programs, `AutoStart`, `Tools`: each tool's settings by its id), `SimProfilesSettings` (`SimProfiles.json`: the profiles, the one in use, Playing and its shortcut; GameSwitching only to set each profile's AutoStart the first time), `ToolScope` (a tool's part: its settings object, the properties kept per profile, on / off, `Loaded`), and `SimProfiles`: `Register` (the profile in use's settings put into the tool as it's made), `Saved` (a tool's settings' Save keeps them in the profile in use; any thread), `Use` (every tool's settings kept, the new profile's put in, each tool's `Loaded`; it plays, or `play: false`), `Playing` / `Play` (each tool runs while its settings' `On` = Enabled and playing; `Loaded` again on a change), `Add`, `Remove`, `Rename` (`Renamed`: the Shaker's effects go with it), `Edited`, `Changed`; a new install's from `SimDefaults`, else first made from the Shaker's profiles; `Load` makes
any game's (and Desktop's) that's missing (`New`, since 4.17.20) |
| `SimDefaults.cs`, `Defaults.json` | What a new install starts with (embedded): the profiles, the one in use, and the Shaker's effects by game (`EffectsFor`, which `BassShaker/DefaultProfiles` uses first) |
| `SimData.cs` | The module's telemetry: `Game` (the profile in use's, and its port), the Forza being read (`CarNames.Edition`: the profile's Version, or `Running`'s), `Open()` (a source for one tool that follows the profile: another game is read from the next read on), `Status` (each tool's status line about the game: nothing while it isn't sending, since 3.24.3), `GameRunning` (the Play button's colour) |
| `GameSwitcher.cs` | Auto Start: the programs running every 3 s (`RunningPrograms`; also which Forza runs, for a profile on Auto); a program of a profile with `AutoStart` starting uses (and plays) it, its ending stops it if it's still in use and playing; whether the profile in use's game runs (its programs, or its game's: `SimData.GameRunning`) |
| `SimBadges.cs` | (since 4.17.18) The title bar's one SimEffects bubble: each tool's part (`Set` by its id, from any thread), in a set order after the profile's short name; again on `SimProfiles.Changed` |
| `ProfileCard.cs` | A game's block (since 4.17.17; the Profile card before, the Shaker's until 3.18.0): Activate / Active (since 4.17.20; a checkbox and Use before), the summary line, and the two parts that fold (`Fold`, kept open in `open` while the tab redraws): `Options` and `Modules` (Core's `Ui.Chip`s) |
| `ProfilesApplet.cs` | The tab (`ProfilesView`: Units, then the blocks in a grid, 3 or 4 across: `Columns`, `Place`) and `Picker()`, the Profile drop-down, Play / Stop and its shortcut (`SimEffects.Play`) the tab puts at the right of its sub-tabs (Core's `AppletGroup.HeaderRight`, since Core 0.42) |
| `GameProfile.cs` | `GameProfile`: the Shaker's effects profile's name, game, port and programs, and `GameProfiles.For` (a game's) |
