# SimEffects → Analysis

*A tool of [SimEffects](../README.md) (since 3.23.1). Its settings file is `Analysis.json`; its laps are in
`AnalysisLaps.jsonl` beside it.*

Your car, tyres and race from the game's telemetry, as blocks you can pop out as widgets, and every lap you drive kept
by game, track, car and class. Forza Horizon for now (it reads the SimEffects profile in use's game; Forza Motorsport sends
the same, but CarGroup; other games show nothing here yet).

## Using it

- **ON / OFF** (top left): records laps, and shows the widgets (since 3.24.3: off, they hide; on, they're back, while
  the profile plays). It's kept per SimEffects profile, and starts on in Forza Horizon's. The blocks on the tab show the
  game's data either way. The **shortcut** button beside it (since 3.24.3) sets a key or a controller button for it
  (also in Settings → Shortcuts). **Imperial / Metric** (top right): torque in lb·ft or N·m, boost in psi
  or bar, distances in miles or km (power is always hp); **°F / °C** beside it: the tyres (since 3.24.0; before, they
  followed the units). Both are also in Settings → SimEffects → Analysis, with **Tyre colours** (since 3.24.2): where
  green, orange and red start, in °F or °C (60, 99 and 116 °C at first; below green, no colour).
- **Paused** (since 3.24.0): in the pause menu, a menu or a replay Forza sends zeros; the blocks and widgets keep what
  they showed last instead, and the graphs stop, until you drive again.
- **Cars by name** (since 3.24.0): Forza sends only a car's number; Analysis looks it up in the list of the game's cars
  (Horizon 4, 5 and 6, Motorsport 7 and 2023: the profile's **Version**, or the one running), so Make reads "2017 Nissan
  GT-R". A car newer than the list shows "Car n" until you name it (Name Car).
- **The blocks** (laid out as below since 3.23.4), with Forza's names for each row. Some cells join rows, their
  labels joined the same way ("Class Index": "A 800"); a row with nothing to show is left out, with the separator
  before it ("Pos - Lap": "3 - 2", or "Pos": "3" outside a race):

  | Block | Layout |
  |---|---|
  | Vehicle | Two columns. Left: Make (CarOrdinal: the name you give it, or "Car n"), Class Index (CarClass, CarPerformanceIndex), Group (CarGroup). Right: Drive (DrivetrainType), HP TQ Motor ("469HP 443LB/FT V8": Power, Torque, NumCylinders), Boost |
  | Tires | A sports car from above, each tyre's temperature beside its wheel (TireTemp…: FL, FR, RL, RR); the tyre and its temperature green, orange or red by how hot it is (since 3.24.2) |
  | Laps | Last (LastLap) · Best (BestLap); Curr (CurrentLap) · PB (your best on this track in this car, since 3.24.2 in any class); Pos - Lap (RacePosition, LapNumber counted from 1) · Race (CurrentRaceTime); Track (the track it's on, see below) · Make - Class Index |
  | Performance (3.24.0) | Power, Torque and Boost, each a graph of the last minute with its value now and its peak since the car changed (a dashed line); boost below 0 (vacuum) under a line at 0 |
  | Timing (3.24.0) | Current, Last, Best (this session's: since the app started, or Forza's best of the race), PB (your best on this track in this car) and PBC (since 3.24.2: in any car of its class, from every lap kept), large, for reading while driving; "—" until there's one; Last in purple when it's your PB, green when it's the session's best |
  | Position (3.24.0) | "3 / 8": your place, of the most cars seen in this race or on this track (Forza doesn't send how many); beside it (since 3.24.2; the Lap block before) "2 / 3", the lap, of the most laps a race on this track has had (just "2" until a race there is recorded), in a race with laps; a sprint shows Position alone, in the middle |

  Tires (since 3.24.0): the car smaller and faint, the temperatures larger. Timing and Position use Bahnschrift
  (Windows' own DIN, close to the type Forza's screens use) and grow with their widget.

  Forza sends only how many cylinders, not their layout: Motor shows up to 5 as inline (I4) and 6 or more as a V (V8),
  so an inline six or a flat six reads V6.

  Forza doesn't send a car's name, only its number: **Name Car** under the Vehicle block names the car you're in.
- **Widgets:** a block's pop-out button, or right-click → **Make Widget**, makes it a widget (Core's, as every widget
  in the app: drag, stretch, opacity, On top, Done, Delete), drawn in its own card (since 3.24.2, little padding):
  its edit strip (since 3.24.3 in rows): **Background** (0%: none, only what it shows, with a shadow, over the game),
  then **On top** and **Title** (Timing and Position start without one, their numbers take the room), then **Done** and
  Delete. They hide while Analysis is off or the profile is stopped (Stop at the top right) and show again when it's on
  and plays. While it's being edited (just made, or right-click) each
  cell shows its rows as checkboxes (Tires: one beside each temperature): untick the ones to leave out, then **Done**. The **Widgets** card lists the open ones.
- **New track:** the first time a race ends its first lap (or a sprint finishes, since 3.24.5) somewhere new, the tab
  asks for the track's name (and a notification says so, since the game is likely in front). **Later** leaves it as
  "Track n", named any time in Tracks.
- **Name** (a flag, top right beside °F / °C, since 3.24.5): names the place the car is as a track. Press it where an
  event starts (its marker) and type the name: the next race started within 15 minutes is on it wherever its grid is
  (since 3.24.7; the event's grid can be far from its marker), and later a race starting within 300 m of it (the
  nearest such place), shown in Track from its first second; its start and lap length are learnt from that race. Track
  names lists it with "no race yet" until then.
- **Export** (the flag, beside Name, since 3.24.7): saves every track and named place to a JSON file (game, name, start
  X / Y / Z, length in metres, laps, cars, when found), to share or keep. No list of the games' race coordinates is
  published, so it's built by driving.
- **Best laps:** the best lap of each game, track, car and class, with its PI, how many laps and when last driven; **Track**,
  **Car** and **Class** filter it and the list under it.
- **Laps:** every lap, newest first: when, game, track, car, class, PI, the lap's number in its race (1 is from a standing
  start), its time and the gap to the best of its track, car and class. Columns sort with a click.
- **Tracks** (since 3.24.2): a tile per track, four across: its name and how many laps it has, its best lap overall, then
  each class's best with its car, the fastest class first ("—": none yet).
- **Track names** ("Tracks" before 3.24.2): each track's name (edit it in place), its laps, its length and when it was
  found; **Delete** removes it and its laps.
- **Classes:** Horizon 6 added R above S2; its class 6 reads R (7 X), the other games' 6 X (since 3.24.2, until a real
  FH6 says otherwise).

## How a track is known

Forza's Data Out says nothing about the track. Analysis knows one by two things it does send:

1. **Where its races start:** the car's place (PositionX/Y/Z) when the race clock (CurrentRaceTime) starts.
2. **How long a lap is:** DistanceTraveled over the first lap.

Each game has its own tracks (laps and tracks from before 3.24.0 are Horizon 6's). A race starting within 150 m of a known track's start, with a first lap within 6% (or 250 m) of its length, is on that
track; failing that, the place just named with **Name** (within 15 minutes, wherever the race starts), or one within 300 m (the nearest); otherwise it's a new one. So two routes from the same place (common in Horizon) are two tracks, and a sprint is a
one-lap track. A track's length is measured again on its first flying lap. While a race's first lap runs, Track shows
the track only if one starts there.

A lap is recorded when LapNumber goes up, with LastLap as its time. A sprint (since 3.24.5) ends no lap: its finish is
the race clock (more than 5 s in, over 100 m driven) standing still for a second while the car still sends, or LastLap
set to the race's time, or (since 3.24.7) the data stopping: Forza sends nothing after the finish line (its Data Out
guide says so), so a race whose data stops for 1.5 s and comes back in free roam (or in another race starting
elsewhere: a series) finished at its last race time, if over 100 m were driven and, once the track's length is known,
at least 85% of it (less is a race quit from the pause menu). It's recorded as lap 1 with the race's time and the
distance driven as its length. A circuit's last lap ends the same way (since 3.24.7), recorded with its current lap time
when most of a lap was driven. A rewind (the race clock going back) is followed; a restart (the clock back near 0, or
back at all after the data stopped) is a new race. Free roam (no race clock) records nothing.

## Code

| File | What it is |
|---|---|
| `AnalysisApplet.cs` | The tool, its on / off shortcut (`Analysis.OnOff`): reads the profile's game (`SimData.Open()`) on its own thread while it records, the tab shows or a widget does; a 10/s tick feeds the tracker, the tab and the widgets (the last frame driving held while the game's paused, and the `PerfHistory`); its Settings page; the widgets (Core's `Widget` with an `OverlayFrame` round a `BlockView`, `EditingChanged` → its checkboxes; `Showing` only while it's on and the profile plays); registered with `SimProfiles` (Enabled per profile, `OnAtFirst`: Forza Horizon's) |
| `AnalysisView.cs` | The tab: on / off and its shortcut button, status, units, the new track's name, the blocks (pop-out, `Widget.Offer`), Core's `Widget.ListCard()`, the Tracks tiles (`RenderTiles`), Best laps, Laps (filters, gaps), Track names |
| `Blocks.cs` | The blocks, their rows and layout (`Block` and its `BlockKind`, `BlockRow`, `BlockCell` of `CellPart`s, `Reading`), units, lap times, class, drive and engine names; `PerfHistory` (power, torque and boost, the last 600 samples, peaks) |
| `BlockView.cs` | A block's cells in columns (label and value, rows joined, empty ones left out), the car drawing with a temperature at each wheel, Performance's graphs (`Graph`, drawn in `OnRender`), Timing's times, tyre colours, or Position's and Lap's "3 / 8"s (the driving ones in a `Viewbox`, `RaceFont`); in a widget being edited, a checkbox per row (the widget's `Hidden`) |
| `AnalysisSettingsView.cs` | Settings → SimEffects → Analysis, and the Units and °F / °C boxes it shares with the tab |
| `CarDrawing.cs` | The Tires block's car from above (paths in the theme's brushes; the tyres under it, `Paint`ed by temperature), in a `Viewbox` so it scales with the widget |
| `LapTracker.cs` | `LapTracker` (races, laps, a sprint's finish, tracks by game, start and length, places named with `NameHere`; each track's most laps and cars), `LapRecord`, `LapStore` (`AnalysisLaps.jsonl`, a JSON line per lap; best by track, car and class) |
| `AnalysisSettings.cs` | `AnalysisSettings` (`Analysis.json`: Enabled, `OnOffKey`, units, Temperature, `BlockWidget`s, `Track`s, car names) |

The race data comes from `Telemetry/ForzaSource.cs` (`TelemetryFrame.HasRace` and the fields after it): the dash's
place, power, torque, tyres, boost, fuel, distance, laps and position, and Horizon's CarGroup (offset 232, the first of
the 12 bytes Horizon puts before the dash).
