# SimEffects → Analysis

*A tool of [SimEffects](../README.md) (since 3.23.1). Its settings file is `Analysis.json`; its laps are in
`AnalysisLaps.jsonl` beside it.*

Your car, tyres and race from the game's telemetry, as blocks you can pop out as widgets, and every lap you drive kept
by track, car and class. Forza Horizon for now (it reads the SimEffects profile in use's game; Forza Motorsport sends
the same, but CarGroup; other games show nothing here yet).

## Using it

- **ON / OFF** (top left): records laps. It's kept per SimEffects profile, and starts on in Forza Horizon's. The blocks
  and widgets show the game's data either way. **Imperial / Metric** (top right): torque in lb·ft or N·m, boost in psi
  or bar, tyres in °F or °C, distances in miles or km (power is always hp).
- **The blocks** (laid out as below since 3.23.4), with Forza's names for each row. Some cells join rows, their
  labels joined the same way ("Class Index": "A 800"); a row with nothing to show is left out, with the separator
  before it ("Pos - Lap": "3 - 2", or "Pos": "3" outside a race):

  | Block | Layout |
  |---|---|
  | Vehicle | Two columns. Left: Make (CarOrdinal: the name you give it, or "Car n"), Class Index (CarClass, CarPerformanceIndex), Group (CarGroup). Right: Drive (DrivetrainType), HP TQ Motor ("469HP 443LB/FT V8": Power, Torque, NumCylinders), Boost |
  | Tires | A sports car from above, each tyre's temperature beside its wheel (TireTemp…: FL, FR, RL, RR) |
  | Laps | Last (LastLap) · Best (BestLap); Curr (CurrentLap) · PB (your best on this track in this car and class); Pos - Lap (RacePosition, LapNumber counted from 1) · Race (CurrentRaceTime); Track (the track it's on, see below) · Make - Class Index |

  Forza sends only how many cylinders, not their layout: Motor shows up to 5 as inline (I4) and 6 or more as a V (V8),
  so an inline six or a flat six reads V6.

  Forza doesn't send a car's name, only its number: **Name Car** under the Vehicle block names the car you're in.
- **Widgets:** a block's pop-out button, or right-click → **Make Widget**, makes it a widget (Core's, as every widget
  in the app: drag, stretch, opacity, On top, Done, Delete). While it's being edited (just made, or right-click) each
  cell shows its rows as checkboxes (Tires: one beside each temperature): untick the ones to leave out, then **Done**. The **Widgets** card lists the open ones.
- **New track:** the first time a race ends its first lap somewhere new, the tab asks for the track's name (and a
  notification says so, since the game is likely in front). **Later** leaves it as "Track n", named any time in Tracks.
- **Best laps:** the best lap of each track, car and class, with its PI, how many laps and when last driven; **Track**,
  **Car** and **Class** filter it and the list under it.
- **Laps:** every lap, newest first: when, track, car, class, PI, the lap's number in its race (1 is from a standing
  start), its time and the gap to the best of its track, car and class. Columns sort with a click.
- **Tracks:** each track's name (edit it in place), its laps, its length and when it was found; **Delete** removes it
  and its laps.

## How a track is known

Forza's Data Out says nothing about the track. Analysis knows one by two things it does send:

1. **Where its races start:** the car's place (PositionX/Y/Z) when the race clock (CurrentRaceTime) starts.
2. **How long a lap is:** DistanceTraveled over the first lap.

A race starting within 150 m of a known track's start, with a first lap within 6% (or 250 m) of its length, is on that
track; otherwise it's a new one. So two routes from the same place (common in Horizon) are two tracks, and a sprint is a
one-lap track. A track's length is measured again on its first flying lap. While a race's first lap runs, Track shows
the track only if one starts there.

A lap is recorded when LapNumber goes up, with LastLap as its time. A rewind (the race clock going back) is followed;
a restart (the clock back near 0) is a new race. Free roam (no race clock) records nothing.

## Code

| File | What it is |
|---|---|
| `AnalysisApplet.cs` | The tool: reads the profile's game (`SimData.Open()`) on its own thread while it records, the tab shows or a widget does; a 10/s tick feeds the tracker, the tab and the widgets; the widgets (Core's `Widget`, a `BlockView` as the body, `EditingChanged` → its checkboxes); registered with `SimProfiles` (Enabled per profile, `OnAtFirst`: Forza Horizon's) |
| `AnalysisView.cs` | The tab: on / off, status, units, the new track's name, the blocks (pop-out, `Widget.Offer`), Core's `Widget.ListCard()`, Best laps, Laps (filters, gaps), Tracks |
| `Blocks.cs` | The blocks, their rows and layout (`Block`, `BlockRow`, `BlockCell` of `CellPart`s, `Reading`), units, lap times, class, drive and engine names |
| `BlockView.cs` | A block's cells in columns (label and value, rows joined, empty ones left out), or the car drawing with a temperature at each wheel; in a widget being edited, a checkbox per row (the widget's `Hidden`) |
| `CarDrawing.cs` | The Tires block's car from above (paths in the theme's brushes), in a `Viewbox` so it scales with the widget |
| `LapTracker.cs` | `LapTracker` (races, laps, tracks by start and length), `LapRecord`, `LapStore` (`AnalysisLaps.jsonl`, a JSON line per lap; best by track, car and class) |
| `AnalysisSettings.cs` | `AnalysisSettings` (`Analysis.json`: Enabled, units, `BlockWidget`s, `Track`s, car names) |

The race data comes from `Telemetry/ForzaSource.cs` (`TelemetryFrame.HasRace` and the fields after it): the dash's
place, power, torque, tyres, boost, fuel, distance, laps and position, and Horizon's CarGroup (offset 232, the first of
the 12 bytes Horizon puts before the dash).
