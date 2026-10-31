# SimEffects → Analysis

*A tool of [SimEffects](../README.md) (since 3.23.1). Its settings file is `Analysis.json`; its laps are in
`AnalysisLaps.jsonl` beside it, its races (since 3.28.1) in `AnalysisRaces.jsonl`.*

Your car, tyres and race from the game's telemetry, as blocks you can pop out as widgets, and every lap you drive kept
by game, track, car and class. Forza Horizon for now (it reads the SimEffects profile in use's game; Forza Motorsport sends
the same, but CarGroup; other games show nothing here yet), and since 3.28.2 iRacing's field in the **Leaderboard**.

## Using it

- **ON / OFF** (top left): records laps, and shows the widgets (since 3.24.3: off, they hide; on, they're back, while
  the profile plays). It's kept per SimEffects profile, and starts on in Forza Horizon's. The blocks on the tab show the
  game's data either way. The **shortcut** button beside it (since 3.24.3) sets a key or a controller button for it
  (also in Settings → Shortcuts).
- **Units** (on the Profiles tab's **Profile** card since 3.28.0, the tab's top right before; the same in every
  profile): **System**, Imperial / Metric: torque in lb·ft or N·m, boost in psi or bar, distances in miles or km (power
  is always hp); **Temperature**, °F / °C: the tyres (since 3.24.0; before, they followed the units). Both are also in
  Settings → SimEffects → Analysis, with **Tyre colours** (since 3.24.2): where green, orange and red start, in °F or °C
  (60, 99 and 116 °C at first; below green, no colour), and (since 3.30.2) **Start radius (m)** under Tracks: how far
  from a track's start (or a tagged place) a race can start and be on it, 500 m at first (750 before 3.30.2, which
  found another event's start 700 m away).
- **Folding** (since 3.28.0): every card has a chevron at its top right that folds it to its title, or opens it; kept.
- **Paused** (since 3.24.0): in the pause menu, a menu or a replay Forza sends zeros; the blocks and widgets keep what
  they showed last instead, and the graphs stop, until you drive again.
- **Cars by name** (since 3.24.0): Forza sends only a car's number; Analysis looks it up in the list of the game's cars
  (Horizon 4, 5 and 6, Motorsport 7 and 2023: the profile's **Version**, or the one running), so the tables read "2017 Nissan
  GT-R". A car newer than the list shows "Car n" until you name it (Name Car).
- **The blocks** (since 3.31.1 on one row: Tires, Performance, Timing, Position and Leaderboard), with Forza's names
  for each row:

  | Block | Layout |
  |---|---|
  | Tires | A sports car from above, each tyre's temperature beside its wheel (TireTemp…: FL, FR, RL, RR); the tyre and its temperature green, orange or red by how hot it is (since 3.24.2) |
  | Performance (3.24.0) | Power, Torque and Boost, each a graph of the last minute with its value now and its peak since the car changed (a dashed line); boost below 0 (vacuum) under a line at 0 |
  | Timing (3.24.0) | Current, Last, Best (this session's: since the app started, or Forza's best of the race), PB (your best on this track in this car) and PBC (since 3.24.2: in any car of its class, from every lap kept), large, for reading while driving; "—" until there's one; Last in purple when it's your PB, green when it's the session's best |
  | Leaderboard (3.28.2) | iRacing only (Forza sends only your own place: "No field from this game"). The cars around you, yours in the middle on the accent (at the front or back, the first or last ones), a line each: Pos, # (car number), Driver, **Gap**: each car's gap to the car directly ahead of it, so the gaps are between the cars, not to you (the leader has none). In a race once it's started the order is live, from how far round each car is (laps done + its place on the lap), and a gap is iRacing's estimate of the time between the two (CarIdxEstTime, plus a lap of the class's estimated lap time per lap between them), "+1.4", or "+1 L" a lap or more; before the start and in practice or qualifying, iRacing's places with the gaps between best laps ("+0.352"). A car in the pits is faded; the pace car and spectators are left out. How many cars: Settings → SimEffects → Analysis → Leaderboard → **Cars** (10 at first, 3 to 20). Its columns can be hidden like any widget's rows |
  | Position (3.24.0) | "3 / 8": your place, of the most cars seen in this race or on this track (Forza doesn't send how many); beside it (since 3.24.2; the Lap block before) "2 / 3", the lap, of the most laps a race on this track has had (just "2" until a race there is recorded), in a race with laps; in a sprint, **Done** ("45%", since 3.27.1) once its length is known, else Position alone |

  Tires (since 3.24.0): the car smaller and faint, the temperatures larger. Timing and Position use Bahnschrift
  (Windows' own DIN, close to the type Forza's screens use) and grow with their widget.

  Forza doesn't send a car's name, only its number: **Name Car** in the Name Track card (under the Vehicle block before
  3.30.2) names the car you're in.

  The Vehicle and Laps blocks, and their widgets, went in 3.30.2 (Timing, Position and Current Location show what they
  did); a widget of either that was open isn't shown again.

  **Done** (since 3.27.1): Forza sends nothing about how much of a race is done, nor about the other cars (so no
  standings). It's the distance driven since the start over the sprint's length, learnt from its first finish (until
  then nothing shows).
- **Widgets:** a block's pop-out button, or right-click → **Make Widget**, makes it a widget (Core's, as every widget
  in the app: drag, stretch, opacity, On top, Done, Delete), drawn in its own card (since 3.24.2, little padding):
  its edit strip (since 3.24.3 in rows): **Background** (0%: none, only what it shows, with a shadow, over the game),
  then **On top** and **Title** (Timing and Position start without one, their numbers take the room), then **Done** and
  Delete. The **joystick** beside a block's pop-out button (since 3.27.1) sets a shortcut (a key combination or a
  controller button, also in Settings → Shortcuts: Analysis · *block* · Widget on / off) that shows its widget where it
  was, or closes it. They hide while Analysis is off or the profile is stopped (Stop at the top right) and show again when it's on
  and plays. While it's being edited (just made, or right-click) each
  cell shows its rows as checkboxes (Tires: one beside each temperature): untick the ones to leave out, then **Done**. Right-click a widget to manage it (the Widgets card went in 3.31.1).
- **Current Location** (top right, since 3.28.0): the track the car is at, in large type (in a race, its track; else
  the one starting nearest within the start radius), or **Unknown**, and under it where the car is ("x, z") and how far from the
  track's start.
- **Name Track** (top left; "New track" in 3.27.1): at a known track the box has its name: type over it and **Save**
  (or Enter) to rename it. Elsewhere, type a name and **Save** to tag the place the car is as a track ("Saved …", and
  it's then where you are). **Add New** (shown at a known track) tags the place as another track all the same (two
  events starting near each other): where the start radii of two overlap, a race is on the nearest (since 3.31.1).
  Every button in the card (Save, Add New, Export, Name Car) leaves the name box focused and selected for the next
  name (since 3.31.1). Tag where an event starts (its marker): the next race started within 15 minutes is
  on it wherever its grid is (since 3.24.7), and later a race starting within the start radius of it (the nearest such place),
  shown in Track from its first second; its start and lap length are learnt from that race. Discovered Tracks lists it
  with "no race yet" until then. If that race is a track already known, the track takes the name (if it had none) and
  the place goes. (Before 3.27.1 a **Name** button did this, and the box asked for a name for each new track.)
- **A race on a new place** (no tag) adds "Track n" when its first lap ends or the sprint finishes, with a
  notification; name it in Name Track while there, or in Discovered Tracks.
- **Export** (in Name Track since 3.28.0; the top right before, since 3.24.7): saves every track and named place to a
  JSON file (game, name, start X / Y / Z, length in metres, laps, cars, when found), to share or keep. No list of the
  games' race coordinates is published, so it's built by driving.
- **Map** (since 3.31.0, under the blocks): the game's tracks as dots where their races start (the Game box picks
  which game's), coloured by type: road (blue), dirt (orange), cross country (green), street (purple), specials ("The
  Goliath"…, red), other (grey; unnamed). The type is guessed from the name (… Cross Country; … Scramble or … Trail;
  … Circuit or … Sprint; "The …"; any other name a street race), or set by hand in **Type** under the track picked.
  Form: a circuit if a race on it had laps or its name says Circuit or Scramble, else a sprint.
  - **Map image…** picks an image of the game's map (PNG, JPG, BMP or TIFF; copied to `AnalysisMaps\<game>.<ext>`
    beside the settings; over 8192 pixels across it's read at 8192). Then line it up: pick a track, **Place on map**,
    click where its event is on the image (Esc cancels); placing it again moves it. Two tracks far apart (200 m at
    least each way) line it up ("Lined up from 2 tracks"), each axis fitted on its own (the games' maps are north up);
    more make it truer. **Clear line-up** forgets them. Without an image (or until it's lined up) the dots are spread
    over a plain ground, north up.
  - The wheel zooms round the pointer, a drag pans, **Fit** shows the whole map. Beside it: **Search** (the list and
    the dots; Enter jumps to the first), **Type**, **Form**, **Raced only** (laps or races kept), **Names** (each dot's
    name), how many show, and the list. Clicking a dot or a name picks the track: it's larger with its name, and its
    type, form, length, place, laps, races and best lap show beside the map, with **Best laps here** (by car and class),
    **Races here** and **Laps here** under it. A search or the list centres the map on it.
  - Your car is a white dot (the accent round it) while the game sends where it is.
- **Best Laps** and **Laps** side by side (since 3.30.2; one under the other before); since 3.31.1 both tables fill their cards, so they end level.
- **Best Laps:** the best lap of each game, track, car and class, with its PI, its **Date** (since 3.31.1: the day that lap was set, "Oct 31, 2026"), how many laps and when last driven; **Track**,
  **Car** and **Class** (on one row, the boxes growing with the window, since 3.31.1) filter it and the Laps table.
- **Races** (since 3.28.1): every race finished (a circuit's last lap, or a sprint's finish; a race quit isn't kept),
  newest first: when, game, track, car, class, laps, its time, **Start** and **Finish** (the place the car was in on
  the grid and at the line, of how many: the most cars the race or track has shown) and **Gained** (places gained, or
  lost: −). Above it: wins, podiums, the average and best finish, the average start and the places gained a race.
  Forza sends only the player's own position (no other cars, no gaps), so the Leaderboard shows only iRacing's field.
- **Laps:** every lap, newest first: when, game, track, car, class, PI, the lap's number in its race (1 is from a standing
  start), its time and the gap to the best of its track, car and class. Columns sort with a click.
- **Best Times** ("Tracks" before 3.28.0, since 3.24.2): a tile per track, four across: its name and how many laps it
  has, its best lap overall, then the best of each class raced there with its car, the fastest class first (since
  3.28.0 only the classes raced; none: Overall alone). The tiles of a row are as tall as its tallest. The **search box** (top right, since 3.31.1) filters as you type: tracks whose name has the text, and on the others only the laps of cars whose name has it (a tile's times are then that car's).
- **Discovered Tracks** ("Track names" before 3.28.0): a table, sorted by any column with a click on its header: Name
  (type over it to rename the track), Game (its funnel filters to one game), Laps, Lap (its length), Location (the
  start's x, z; sorted, places near each other come together), Nearest (the track starting nearest it, and how far:
  sorted, the ones near each other come first) and Found. **Delete** (top right) removes the track picked and its laps and races.
- **Classes:** Horizon 6 added R above S2; its class 6 reads R (7 X), the other games' 6 X (since 3.24.2, until a real
  FH6 says otherwise).

## How a track is known

Forza's Data Out says nothing about the track. Analysis knows one by two things it does send:

1. **Where its races start:** the car's place (PositionX/Y/Z) when the race clock (CurrentRaceTime) starts.
2. **How long a lap is:** DistanceTraveled over the first lap.

Each game has its own tracks (laps and tracks from before 3.24.0 are Horizon 6's). A race starting within the start radius (Settings, 500 m at first; since 3.30.2, 750 m from 3.27.1, 150 before) of a known track's start, with a first lap within 6% (or 250 m)
of its length, is on that track (the nearest start that fits; since 3.31.1 not the closest in length), unless a place tagged for this race starts nearer (since 3.31.1); failing that, the place just tagged (within 15 minutes,
wherever the race starts), or one within the start radius (since 3.30.2; 750 m from 3.27.1, 300 before; the nearest); otherwise it's a new one. So two routes from the same place (common in Horizon) are two tracks, and a sprint is a
one-lap track. A track's length is measured again on its first flying lap. While a race's first lap runs, Track shows
the track only if one starts there.

A lap is recorded when LapNumber goes up, with LastLap as its time. A sprint (since 3.24.5) ends no lap: its finish is
the race clock (more than 5 s in, over 100 m driven) standing still for a second while the car still sends, or LastLap
set to the race's time, or (since 3.24.7) the data stopping: Forza sends nothing after the finish line (its Data Out
guide says so), so a race whose data stops for 1.5 s and comes back in free roam (or in another race starting
elsewhere: a series) finished at its last race time, if over 100 m were driven and, once the track's length is known,
at least 85% of it (less is a race quit from the pause menu). It's recorded as lap 1 with the race's time and the
distance driven as its length. A circuit's last lap ends the same way (since 3.24.7), recorded with its current lap time
when most of a lap was driven; since 3.28.0 also when the race clock goes straight back to 0 at the line, stops, or
LastLap takes the lap's time without the lap number moving on (a race of 3 laps recorded only 2). Since 3.30.3 a
finish is judged on the frame furthest into the lap, not the last one: after the finish Forza sends a few frames (the
next race loading) with the race clock as it was, the distance back near 0 and the car elsewhere, which made every race
look undriven, so none was kept. Such frames after a gap end the race there. A circuit whose lap number goes up at the
line (Horizon 5) finished at that line when the data stops with hardly any of the next lap driven. The log says why a
race wasn't kept ("not a finish (…)"). A rewind (the race clock going back) is followed; a restart (the clock back near 0, or
back at all after the data stopped) is a new race. Free roam (no race clock) records nothing.

## Code

| File | What it is |
|---|---|
| `AnalysisApplet.cs` | The tool, its on / off shortcut (`Analysis.OnOff`) and each widget's (`Analysis.Widget.<block>`, since 3.27.1, kept in its `BlockWidget.Key`): reads the profile's game (`SimData.Open()`) on its own thread while it records, the tab shows or a widget does; a 10/s tick feeds the tracker, the tab and the widgets (the last frame driving held while the game's paused, and the `PerfHistory`); its Settings page; the widgets (Core's `Widget` with an `OverlayFrame` round a `BlockView`, `EditingChanged` → its checkboxes; `Showing` only while it's on and the profile plays); registered with `SimProfiles` (Enabled per profile, `OnAtFirst`: Forza Horizon's) |
| `AnalysisView.cs` | The tab (Races since 3.28.1: `RenderRaces`, the stats line): on / off and its shortcut button, status, Name Track (`TagHere`: rename the track the car's at, or `NameHere`), Current Location (`Here`), the blocks (shortcut, pop-out, `Widget.Offer`), Best Times tiles (`RenderTiles`, a Grid: rows as tall as their tallest; `tileSearch`), Best Laps, Laps (filters, gaps; `Foldable`'s `fill`), Discovered Tracks (`RenderTracks`, `TrackRow`, Game's filter, Location's Z-order `Cell`); each card `Foldable` (`AnalysisSettings.Folded`) |
| `Blocks.cs` | The blocks, their rows and layout (`Block` and its `BlockKind`, `BlockRow`, `BlockCell` of `CellPart`s, `Reading`), the Leaderboard's cars around yours (`Around`) and gaps (`GapText`), units, lap times, class names; `PerfHistory` (power, torque and boost, the last 600 samples, peaks) |
| `BlockView.cs` | A block's cells in columns (label and value, rows joined, empty ones left out), the car drawing with a temperature at each wheel, Performance's graphs (`Graph`, drawn in `OnRender`), Timing's times, tyre colours, Position's and Lap's "3 / 8"s, or the Leaderboard (`BoardBody`: a column a row, its lines filled every update) (the driving ones in a `Viewbox`, `RaceFont`); in a widget being edited, a checkbox per row (the widget's `Hidden`) |
| `AnalysisSettingsView.cs` | Settings → SimEffects → Analysis, and the Units and °F / °C boxes it shares with the Profile card (`ProfileCard.UnitBoxes`, since 3.28.0; the tab before) |
| `MapView.cs` | The Map card (since 3.31.0): the map image (`LoadPicture`, off the UI thread), the line-up (`Lineup`: a least-squares line per axis from `GameMap.Anchors`), the dots and labels (`Rebuild`, placed by `Place` as the view zooms and pans), the car's dot (`Tick`), search and filters, the track picked (`ShowPicked`: its tables), `Kind` / `Form` (a track's type, guessed from its name unless `Track.Type` is set) |
| `CarDrawing.cs` | The Tires block's car from above (paths in the theme's brushes; the tyres under it, `Paint`ed by temperature), in a `Viewbox` so it scales with the widget |
| `LapTracker.cs` | `LapTracker` (races, laps, a sprint's finish and `Progress`, tracks by game, start and length, places tagged with `NameHere`, the `Nearest` to the car; each track's most laps and cars), `LapRecord`, `RaceRecord` (a race finished: start and finish places, `Result`), `LapStore` (`AnalysisLaps.jsonl`, a JSON line per lap; `AnalysisRaces.jsonl`, one per race; best by track, car and class) |
| `AnalysisSettings.cs` | `AnalysisSettings` (`Analysis.json`: Enabled, `OnOffKey`, units, Temperature, `BoardRows`, `StartRadius` (since 3.30.2), `BlockWidget`s, `Track`s (since 3.31.0 with a `Type`), `Maps` (since 3.31.0: a `GameMap` per game, its image and `MapAnchor`s), car names) |

The race data comes from `Telemetry/ForzaSource.cs` (`TelemetryFrame.HasRace` and the fields after it): the dash's
place, power, torque, tyres, boost, fuel, distance, laps and position, and Horizon's CarGroup (offset 232, the first of
the 12 bytes Horizon puts before the dash). The Leaderboard's field is `TelemetryFrame.Field` (`Standings` of `Racer`s,
`Telemetry/Telemetry.cs`), made by `Telemetry/IRacingSource.cs` (`Field`, 4 times a second: the drivers and sessions from
the session YAML, `ReadField`; CarIdxPosition, CarIdxLapCompleted, CarIdxLapDistPct, CarIdxEstTime, CarIdxBestLapTime,
CarIdxOnPitRoad; a lap count a moment late at the line is mended).
