# SimEffects → Profiles

*A tool of [SimEffects](../README.md) (since 3.18.0; before, the Shaker, Input Overlay, Arduino and AmbiLight each
had profiles of their own). Its settings file is `SimProfiles.json`.*

One set of profiles for everything in SimEffects: a profile per game (and one for no game, **Desktop**). The profile
in use decides two things:

- **The game every tool reads.** Its telemetry is read once (one reader, on the profile's port for Forza) and every
  tool takes it from there: the Shaker, Notify and Fanatec's shift lights, the Input Overlay. Switching to another
  game's profile switches them all to that game.
- **Every tool's settings.** Each sub-module keeps its settings per profile: whether it's on (all of them), and its
  own (below). A change made on a tool's tab is kept in the profile in use.

## Using it

- **Profile:** (at the right of the sub-tabs, on every SimEffects tab): the profile in use. Picking another switches
  every sub-module to it.
- **Play / Stop** (since 3.18.1; beside the drop-down, red while the profile plays, green while it's stopped) and its
  shortcut button (a key or a controller button, in Settings → Shortcuts too): stopped, every sub-module goes dark at
  once, its own ON kept (shown greyed on its tab) for when it plays again. A profile picked by hand or by a game plays.
- **Profile** card (left; since 3.23.4 as below): **Profile** ("Name" before 3.24.0), a drop-down of the profiles like
  the one at the top right (picking one uses it), **Game** (or **None**: no telemetry), **Version** (Forza's, since
  3.24.0: Forza Horizon 6, 5 or 4, Forza Motorsport (2023) or 7, or **Auto**, the one running (the newest until one
  has); it names the cars, and Analysis keeps each game's tracks and laps apart), **UDP Port** (Forza: set the game's Data Out to IP
  127.0.0.1 and this port), **Auto Start**, **Programs**, and at the bottom right:
  - **Edit**: Profile becomes a box with the name in it, and Edit becomes **Save** (or Enter; Esc cancels), which renames
    the profile and puts the drop-down back. A name another profile has isn't taken (the box stays).
  - **New**: a game's profile with its programs, a profile with no game, or a copy of the one in use (its tools'
    settings as they are now); it's used at once, with its name in the box to change, as after Edit.
  - **Delete** (Confirm? first; not with only one profile left).
- **Auto Start** (Yes / No, per profile; Game Switching for all profiles before 3.23.4): with Yes, one of its
  **Programs** starting uses and plays the profile (once: picking another by hand meanwhile stays), and that program
  ending stops it, if it's still the one in use. A profile's Auto Start comes from the old Game Switching the first
  time (Yes where it was on and the profile has programs); a new profile's is Yes when it has programs. (No Game, the
  profile used when no program ran, went with it: the profile stays, stopped.)
- **Auto Start Modules** (right; "Sub-modules" before 3.18.1): the profile in use's sub-modules, a tile each with its
  checkbox (since 3.24.0; before, a column of checkboxes per profile): the ones ticked start when the profile does
  (picked by hand or by a game), the others stop. Clicking a tile switches the tool at once, as its own ON / OFF does.

What each tool keeps per profile:

| Tool | Kept per profile |
|---|---|
| Shaker | On / off, and its effects (each profile has its own, made from its game's defaults the first time; **Reset Effects** on the Effects card puts them back). The devices and the master volume are the same for all |
| Input Overlay | On / off, Read From, history, background, the traces, the speeds (shown, Window Size, units), Record. Its place and the axes are the same for all |
| Arduino, Notify, Fanatec | On / off (the boards, the wheel and the colours are the same for all) |
| Shift points (Notify and Fanatec) | First Light, Shift, Flash, Flash Rate, iRacing's own shift lights (each car's redline is the same for all) |
| Analysis | On / off (recording laps; on in Forza Horizon's profiles the first time). Its widgets, tracks, car names and laps are the same for all |
| AmbiLight | On / off, Frame Rate, Smoothing, Saturation, Black Level (the lights and their zones are the same for all) |

**From 3.17** (the first time): a profile for each of the Shaker's (name, game, port, programs; its effects stay with
it) and Desktop, which is in use. Every profile starts with each tool's settings as they were; a tool
that had 3.17's **Switch On With Game** ticked is on in every game's profile. Game Switching (now each profile's Auto Start) is the Shaker's.

## Code

| File | What it is |
|---|---|
| `SimProfiles.cs` | `SimProfile` (name, game or none, `Version` (Forza's; null: the one running), port, programs, `AutoStart`, `Tools`: each tool's settings by its id), `SimProfilesSettings` (`SimProfiles.json`: the profiles, the one in use, Playing and its shortcut; GameSwitching only to set each profile's AutoStart the first time), `ToolScope` (a tool's part: its settings object, the properties kept per profile, on / off, `Loaded`), and `SimProfiles`: `Register` (the profile in use's settings put into the tool as it's made), `Saved` (a tool's settings' Save keeps them in the profile in use; any thread), `Use` (every tool's settings kept, the new profile's put in, each tool's `Loaded`; it plays), `Playing` / `Play` (each tool runs while its settings' `On` = Enabled and playing; `Loaded` again on a change), `Add`, `Remove`, `Rename` (`Renamed`: the Shaker's effects go with it), `Edited`, `Changed`; first made from the Shaker's profiles |
| `SimData.cs` | The module's telemetry: `Game` (the profile in use's, and its port), the Forza being read (`CarNames.Edition`: the profile's Version, or `Running`'s), `Open()` (a source for one tool that follows the profile: another game is read from the next read on), `Status` (each tool's status line about the game) |
| `GameSwitcher.cs` | Auto Start: the programs running every 3 s (`RunningPrograms`; also which Forza runs, for a profile on Auto); a program of a profile with `AutoStart` starting uses (and plays) it, its ending stops it if it's still in use and playing |
| `ProfileCard.cs` | The Profile card (the Shaker's until 3.18.0): Name a drop-down, or a box while `naming` (Edit, or New) until Save; Auto Start; Edit / New / Delete (Core's `ConfirmButton`) at the bottom right |
| `ProfilesApplet.cs` | The tab (the card, and Auto Start Modules: a tile per sub-module, `ToolsCard`, `TileTemplate`) and `Picker()`, the Profile drop-down, Play / Stop and its shortcut (`SimEffects.Play`) the tab puts at the right of its sub-tabs (Core's `AppletGroup.HeaderRight`, since Core 0.42) |
| `GameProfile.cs` | `GameProfile`: the Shaker's effects profile's name, game, port and programs, and `GameProfiles.For` (a game's) |
