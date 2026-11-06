# SimEffects → Analysis

*A tool of [SimEffects](../README.md) (since 3.23.1). Its settings file is `Analysis.json`; its laps are in
`AnalysisLaps.jsonl` beside it, its races (since 3.28.1) in `AnalysisRaces.jsonl`; the Race Analysis' choices (since
4.5.0, Forza Analysis' before) in `ForzaAnalysis.json`.*

Your car, tyres and race from the game's telemetry, as blocks (each an overlay over the game: SimEffects →
[Overlays](../InputOverlay/README.md), since 3.33.0), and every lap you drive kept
by game, track, car and class. Forza Horizon for now (it reads the SimEffects profile in use's game; Forza Motorsport sends
the same, but CarGroup; other games show nothing here yet), and since 3.28.2 iRacing's field in the **Leaderboard**
overlay. Since 4.5.0 each race is logged and analysed here (**Session Analysis**, below; Race Analysis until 4.6.3, Gaming →
Forza Analysis before).

The tab, top to bottom: Current Location (with Name Track in it since 4.5.1, a quarter of the row) beside **Widgets**
(the blocks, in one card that starts folded and remembers; open, all of them show); **Racing Statistics** (since
4.6.3); Session Analysis' cards; Best Times; Best Laps, All Laps and Races, a third of the row each (since 4.6.3); the
Map (above Discovered Tracks since 4.6.3); Discovered Tracks.

## Using it

- **ON / OFF** (top left): records laps (until 3.33.0 it showed the widgets too; they're Overlays' now). It's kept per SimEffects profile, and starts on in Forza Horizon's. The blocks on the tab show the
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
  Since 4.5.0 the blocks aren't folded one by one: the Widgets card folds them all (folded at first).
- **Paused** (since 3.24.0): in the pause menu, a menu or a replay Forza sends zeros; the blocks and widgets keep what
  they showed last instead, and the graphs stop, until you drive again.
- **Cars by name** (since 3.24.0): Forza sends only a car's number; Analysis looks it up in the list of the game's cars
  (Horizon 4, 5 and 6, Motorsport 7 and 2023: the profile's **Version**, or the one running), so the tables read "2017 Nissan
  GT-R". A car newer than the list shows "Car n" until you name it (Name Car).
- **The blocks** (since 3.31.1 on one row; since 4.5.0 Tires, Performance, Timing and Position, in the Widgets card;
  the Leaderboard is an overlay only), with Forza's names for each row:

  | Block | Layout |
  |---|---|
  | Tires | A sports car from above, each tyre's temperature beside its wheel (TireTemp…: FL, FR, RL, RR); the tyre and its temperature green, orange or red by how hot it is (since 3.24.2) |
  | Performance (3.24.0) | Power, Torque and Boost, each a graph of the last minute with its value now and its peak since the car changed (a dashed line); boost below 0 (vacuum) under a line at 0 |
  | Timing (3.24.0) | Current, Last, Best (this session's: since the app started, or Forza's best of the race), PB (your best on this track in this car) and PBC (since 3.24.2: in any car of its class, from every lap kept), large, for reading while driving; "—" until there's one; Last in purple when it's your PB, green when it's the session's best |
  | Leaderboard (3.28.2) | iRacing only (Forza sends only your own place: "No field from this game"). The cars around you, yours in the middle on the accent (at the front or back, the first or last ones), a line each: Pos, # (car number), Driver, **Gap**: each car's gap to the car directly ahead of it, so the gaps are between the cars, not to you (the leader has none). In a race once it's started the order is live, from how far round each car is (laps done + its place on the lap), and a gap is iRacing's estimate of the time between the two (CarIdxEstTime, plus a lap of the class's estimated lap time per lap between them), "+1.4", or "+1 L" a lap or more; before the start and in practice or qualifying, iRacing's places with the gaps between best laps ("+0.352"). A car in the pits is faded; the pace car and spectators are left out. How many cars: Settings → SimEffects → Analysis → Leaderboard → **Cars** (10 at first, 3 to 20). Its columns can be hidden like any widget's rows |
  | Track Map (4.6.3) | The track as your best lap on it went (grey, read from that race's log: Forza's X and Z, the game's own metres, so it's the track's real shape), the line you're driving now (the last few minutes, in the accent colour) and the car (a white dot), north up; on a track without a lap kept yet, just your line as you drive it. Rows: **Best lap** and **Your line**. Its overlay, as every block's, is made on Overlays |
  | Position (3.24.0) | "3 / 8": your place, of the most cars seen in this race or on this track (Forza doesn't send how many); beside it (since 3.24.2; the Lap block before) "2 / 3", the lap, of the most laps a race on this track has had (just "2" until a race there is recorded), in a race with laps; in a sprint, **Done** ("45%", since 3.27.1) once its length is known, else Position alone. Since 4.5.0, with no place (not in a race) the block (and its overlay) is blank |

  Tires (since 3.24.0): the car smaller and faint, the temperatures larger. Timing and Position use Bahnschrift
  (Windows' own DIN, close to the type Forza's screens use) and grow with their widget.

  Forza doesn't send a car's name, only its number: **Name Car** in the Name Track card (under the Vehicle block before
  3.30.2) names the car you're in.

  The Vehicle and Laps blocks, and their widgets, went in 3.30.2 (Timing, Position and Current Location show what they
  did); a widget of either that was open isn't shown again.

  **Done** (since 3.27.1): Forza sends nothing about how much of a race is done, nor about the other cars (so no
  standings). It's the distance driven since the start over the sprint's length, learnt from its first finish (until
  then nothing shows).
- **Widgets** (overlays since 3.33.0): a block's widget is made, ticked, edited and deleted on SimEffects →
  [Overlays](../InputOverlay/README.md), with its rows, title, background and shortcut there (the pop-out and shortcut
  buttons on the blocks went). They read from here (Analysis switched off in Settings → General → Modules: no block
  overlays). A widget open before 3.33.0 is in Overlays' list, and shows while Overlays is on (switched on in the
  profiles Analysis was on in). Still Core's widget in its own card (`OverlayFrame`): drag, stretch, Background, On top,
  Title, Click through; while it's edited each cell shows its rows as checkboxes (Tires: one beside each temperature).
- **Current Location** (top right, since 3.28.0): the track the car is at, in large type (in a race, its track; else
  the one starting nearest within the start radius), or **Unknown**, and under it where the car is ("x, z") and how far from the
  track's start (nothing, since 4.6.3, while the game sends no place).
- **Name Track** (top left; "New track" in 3.27.1; since 4.5.1 in Current Location's card, under the place; since
  4.6.3 the box, **Save** and **Export** on one row, and with no place from the game the box says *Unknown*, greyed out
  with Save): at a known track the box has its name: type over it and **Save**
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
- **Map** (since 3.31.0; since 4.6.3 just above Discovered Tracks): the game's tracks as dots where their races start (the Game box picks
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
- **Best Laps**, **All Laps** ("Laps" before 4.6.3) and **Races** side by side, a third of the row each (since 4.6.3;
  Best Laps and Laps side by side since 3.30.2, Races under them); the tables fill their cards, so they end level.
- **Track**, **Car** and **Class** (since 4.6.3 Best Laps, All Laps and Best Times each have their own, *All tracks*
  and so on at first; one set for Best Laps and Laps before): the tracks and cars A to Z, the classes fastest first.
- **Best Laps:** the best lap of each game, track, car and class, with its PI, its **Date** (since 3.31.1: the day that lap was set, "Oct 31, 2026"), how many laps and when last driven.
- **Races** (since 3.28.1): every race finished (a circuit's last lap, or a sprint's finish; a race quit isn't kept),
  newest first: when, game, track, car, class, laps, its time, **Start** and **Finish** (the place the car was in on
  the grid and at the line, of how many: the most cars the race or track has shown) and **Gained** (places gained, or
  lost: −). Its numbers are Racing Statistics' (over it before 4.6.3).
- **Racing Statistics** (since 4.6.3; a line over Races before): over every race finished, a tile each: Races (and on
  how many tracks), Wins, Podiums and Top 5 (each with its share of the races and a bar: gold, silver, bronze), Average
  finish (and the best), Average start (and the best), Places gained a race (green up, red down; how many races went
  up and down) and Finished ahead (the share of races finished ahead of where you started). Beside them **Finishes**:
  a bar per place, 1 to 10 and 11+, the podium's gold, silver and bronze; under them **Last races**: the last 15 races'
  places, newest right (a tooltip says when, and where you started).
  Forza sends only the player's own position (no other cars, no gaps), so the Leaderboard shows only iRacing's field.
- **Laps:** every lap, newest first: when, game, track, car, class, PI, the lap's number in its race (1 is from a standing
  start), its time and the gap to the best of its track, car and class. Columns sort with a click.
- **Best Times** ("Tracks" before 3.28.0, since 3.24.2): a tile per track, four across: its name and how many laps it
  has, its best lap overall, then the best of each class raced there with its car, the fastest class first (since
  3.28.0 only the classes raced; none: Overall alone). The tiles of a row are as tall as its tallest. The **search box** (top right, since 3.31.1) filters as you type: tracks whose name has the text, and on the others only the laps of cars whose name has it (a tile's times are then that car's).
  Beside it (since 4.6.3) **Track**, **Car** and **Class**: one track's tile, or only that car's or class's laps (and
  only the tracks it has laps on).
- **Discovered Tracks** ("Track names" before 3.28.0): a table, sorted by any column with a click on its header: Name
  (type over it to rename the track), Game (its funnel filters to one game), Laps, Lap (its length), Location (the
  start's x, z; sorted, places near each other come together), Nearest (the track starting nearest it, and how far:
  sorted, the ones near each other come first) and Found. **Delete** (top right) removes the track picked and its laps and races (since 4.5.1 several, picked with Ctrl or Shift).
- **Classes:** Horizon 6 added R above S2; its class 6 reads R (7 X), the other games' 6 X (since 3.24.2, until a real
  FH6 says otherwise).

## Session Analysis (since 4.5.0, Race Analysis until 4.6.3; Gaming → Forza Analysis before, a module of its own)

How smoothly you drive each race, as a driving coach (or a tool like Racing Labs) would look at it: mostly the
**brake and throttle, going on and coming off**, laps compared with your own or someone else's, contact with walls
and cars, and about what each habit costs a lap. Its cards come under the Widgets, each folding; the page has only
labels and short tooltips.

### Where the races come from

While Analysis is on, each race is logged (`RaceLog.cs`) from its game's telemetry while the race clock runs: a row
for every frame (Forza's 60 a second): steering, throttle, brake, clutch and handbrake, speed, lateral and
longitudinal g, the lap and the time into it, distance, where the car is and the fastest suspension movement, and a
`# car,` line naming the car. A restart, or the next race of a series, starts another log; free roam, or the data
stopping for 5 s, ends it.

A log is kept only when the race has a lap or its result kept (the tracker's laps and races then carry its file
name, `Session`, in `AnalysisLaps.jsonl` and `AnalysisRaces.jsonl`): driving around and a race quit before a lap
are deleted, 15 s after the next frames come without the race being kept (or when the app closes). The logs are CSV
files in `%LOCALAPPDATA%\DaisysApp\ForzaSessions\` (imported ones in its `Imported\`), the format in Core's
`Shared/SessionLog.cs`. Until 4.5.0 SimEffects → Overlays' Inputs overlay logged sessions while it showed, races or
not; those older files aren't listed (they name no race), and stay in the folder.

### Picking a race

- **Session** (Session Analysis' title row): every race with a log, newest first: track, car, when, best lap.
  The folder icon (**Open folder** before 4.6.3) opens the folder.
- **A click** on a lap (Laps), a best lap (Best Laps), a race (Races) or a time on a Best Times tile opens its race
  here with that lap in Compare's **This** (a race: its best lap), and scrolls up to it. What's in **With** stays, so a
  click changes your run against the one you're comparing with.
- **Right-click** one of those rows: **Analyse**, or **Compare With** (it goes in With).

### The cards

- **Session Analysis**: the date, car, length, laps and best, the brake zones and throttle applications; then the
  **scores**, 0 to 100 (green 85 and over, amber 65 and over): Overall (the others together, the brake release
  counting most), Brake application, Brake release, Throttle application, Throttle release, Steering, Consistency;
  the **time split** (full throttle, part throttle, braking, both together, coasting); and **key numbers**: the
  average brake peak, application time, release time and trail braking; coasting from brake to throttle; throttle
  to brake; throttle application time; throttle stabs; steering corrections a minute; impacts and crashes.
- **Trace**: throttle, brake, steering and speed (clutch too, if ticked) over time, each lap's start marked. The
  brake zones are shaded and numbered; marks at the top show what's worth a look (impacts violet, crashes pink),
  each with a tooltip. The wheel zooms, dragging moves along, a double-click shows it all; clicking a zone picks it.
  The strip underneath is the whole race; click or drag it to jump.
- **Compare**: **This** (the whole race or one of its laps, the best starred) against **With** (any race, yours or
  imported, the same track's first, and its lap; the same race's other laps too).
  - A lane each, along the lap's distance: **Delta** (the other's time less this one's at each point: above the
    line, this one is ahead; amber; a chip shows or hides it), speed, throttle, brake and steering.
  - Since 4.6.3 each channel has its colour (speed blue, throttle green, brake red, steering white), the other run's
    lines dotted (before, this run blue and the other orange). Under the picks a row per run (This, With), with a
    chip per channel: its box shows or hides that line (8 in all; a lane goes when both its lines are hidden), its
    name (or a click on the line) highlights it, bold, the others faded; again, back to all. Kept. The wheel zooms,
    dragging moves along, a double-click shows it all; the readout gives both runs' values.
  - Under the picks: the lap times and the gap, and *Not the same track?* when their starts are over 500 m apart or
    the laps' lengths differ by over 6%.
  - **Corners**: each of this run's brake zones with the other's nearest (within 100 m): where the other braked (+
    later, in metres), peak, entry and slowest speed, and the time gained or lost through it. Picking a row zooms
    there (and the Track Map to it).
  - **Export…** saves This (a lap or the race) as a file with your name (**Driver**; Windows' user name if empty)
    and the race's. **Import…** copies files like it into `ForzaSessions\Imported\`, where they're in With, and puts
    the first in With.
  - Speeds are in mph where the region isn't metric, km/h elsewhere.
- **Track Map** (since 4.6.3, a third of Compare's row): the lines This and With took, from where the game says the
  car was (X and Z, 60 times a second, in the game's own metres): this run blue and solid, the other orange and
  dotted, north up, the start a white square. Both are in the same coordinates, so two sessions on a track lie over
  each other as driven, a lane apart or a metre. The mouse on Compare's chart puts a dot on each line at that point; a
  corner picked zooms there; the wheel zooms, dragging moves, a double-click shows it all.
- **Brake Point**, **Brake Release** and **Throttle**, a third of the row each (since 4.6.3; "Braking" before, with a
  drop-down for the two line-ups; "Brake zones compared" before 4.5.1): every zone drawn over the others, the average
  dashed and the zone picked bold, lined up at the brake point, and at the release; Throttle the same for each time the
  throttle goes on.
- **Brake Analysis** and **Throttle Analysis**, a row each: the brake's #, Peak, Hold, Release, Smooth, Trail,
  AppScore and ResScore; the throttle's Way, From, To, Time (10% to 90% of the way), Smooth and Score. Picking a row
  zooms the trace there; a double-click (a click before 4.6.3) opens it in a window, with **Close** at its bottom right: its moment of the trace (a second either side; the
  zone's application, hold and release shaded; speed too), its numbers (Application and Release are the scores) and
  its notes (*let go from 75%*, *pressed again while releasing*, *spiked to 98%, held 83%*, *slow to build*,
  *throttle on too*, *at 100% for…*, *stab*, *lifted on the way*, *straight to full*, *part lift*).
- **Smoothness**, beside the Tips (since 4.5.1 two thirds of the width, the friction circle left of the chart; since
  4.6.3 under Compare): every brake and throttle application (filled) and release (hollow) at its score
  over the race, with each one's running average (clicking a dot picks it); the friction circle (the car's g:
  sideways across, braking down, accelerating up; red braking, green on the throttle, grey coasting; the peaks under
  it). Chips show or hide Brake, Throttle, On and Off.
- **Tips**: what the numbers suggest, each with about how much time it would save a lap, the most first; contact
  with walls and cars in red when it's too much (two crashes or more, contact once a lap or more, or over a second a
  lap). Since 4.6.3 in a box per colour, no titles: red first, then white, then green; and without the tip about small
  steering corrections (the number stays in the key numbers).

### How it's worked out (`Sessions/PedalAnalysis.cs`)

The log is resampled to an even 100 Hz (`Sessions/SessionData.cs`) and lightly smoothed (30 ms).

**Brake zones.** A zone is the brake above 5%, from the foot leaving the floor until the pedal is let go. If the brake
is let go for under 0.15 s and pressed again, it's the same zone. Zones that peak under 8% are ignored.

- **Application**: the time from 10% to 90% of the peak. Over 0.3 s for a firm stop (50% or more) counts as slow.
- **Spike**: the peak in the first half-second is more than 8% over the level then held (the 80th percentile of the
  rest of the zone).
- **Release**: starts where the pressure last falls below 90% of the level held. Its smoothness is the net travel
  divided by the total travel (jitter under 1% ignored), so 100% means one movement; pressing again by 3% or more is
  a re-application; a brake still at 30% or more 0.1 s before it's let go counts as *let go in one go*.
- **Trail braking**: the share of the release with the wheel turned 5% or more of its travel.
- **The scores**: the application score is smoothness², less for a slow build-up or a spike; the release score is
  smoothness², less for letting go in one go or a release under 0.15 s. Zones under 15% don't count, and the race's
  score weighs each zone by its peak.

**Throttle.** The pedal's turning points are found, ignoring wobbles of under 8%; each rise or fall of 25% or more is
an application or a release. Its time is 10% to 90% of the way; its smoothness is measured as for the brake, and
*lifted on the way* is a drop of 3% or more during the rise. A **stab** is on by 30% or more in 0.15 s, then off again
by 20% or more within 0.3 s.

**Laps** (`Sessions/SessionData.cs`): a run of the same lap number; complete when the next one follows and it started
at the line. Its time is the game's time into it at its end, plus what was left across the line.

**Contact** (`Sessions/Incidents.cs`), as the Shaker's Crashes and Impacts feel it: a crash is 3.5 g or more sideways
and lengthways together; an impact is a jolt, that g changing by 1.3 g or more within 50 ms, or a suspension hit of
1.5 m/s with a jolt of 0.8 g. Only above about 18 km/h.

**What it costs.** Contact: with two clean laps or more, the laps with contact against the clean laps' median;
otherwise the speed it took off and the time to get it back at about 0.4 g (Δv² / 2av), plus any time stopped after
a crash. The pedal tips' times are rules of thumb: a re-application 0.1 s, a stab 0.1 s, a snapped release, a spike
or a lift on the way 0.05 s, half of a slow build's time over 0.3 s, 0.3 s a second of coasting (over 0.3 s a zone)
or of both pedals, and 0.15 s a second at 100%. A lap's share is the race's over the laps its driving is worth.

**Also:** steering corrections are turns back of less than 13° on Forza's 450° wheel, within a second; consistency
comes from how much the firm zones' peak, application time and release time vary (4 zones or more); the time split
counts only time within 3 s of something moving.

Not yet: sectors; wheelspin; laps in iRacing and ACC (their races aren't followed).

## The tracks it comes with

Since 3.34.0 Analysis comes with every FH5 and FH6 track (`Tracks.json`, Export's file, built in): added to your
tracks once, at the start after an update brings them (each time the file changes); one you already have (the same
game, and the same name or start) isn't added again, and one you delete stays gone until the file changes.

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
| `AnalysisApplet.cs` | The tool and its on / off shortcut (`Analysis.OnOff`): reads the profile's game (`SimData.Open()`) on its own thread while it records, the tab shows or Overlays wants it (`Wanted`); a 10/s tick feeds the tracker, the tab and Overlays' block widgets (`Ticked`; the last frame driving held while the game's paused, and the `PerfHistory`); `Current` for Overlays (the widgets and their `Analysis.Widget.<block>` shortcuts are `InputOverlay/InputOverlayApplet.cs`'s since 3.33.0); its Settings page; registered with `SimProfiles` (Enabled per profile, `OnAtFirst`: Forza Horizon's); feeds the race log (`RaceLog`) every frame, which the tracker's `Claim` keeps |
| `AnalysisView.cs` | The tab (Races since 3.28.1: `RenderRaces`, the stats line): on / off and its shortcut button, status, Name Track (`TagHere`: rename the track the car's at, or `NameHere`), Current Location (`Here`), the blocks, Best Times tiles (`RenderTiles`, a Grid: rows as tall as their tallest; `tileSearch`), Best Laps, Laps (filters, gaps; `Foldable`'s `fill`), Discovered Tracks (`RenderTracks`, `TrackRow`, Game's filter, Location's Z-order `Cell`); each card `Foldable` (`AnalysisSettings.Folded`); since 4.5.0 the Widgets card (`Foldable(…, foldedAtFirst)`), the race analysis' cards, and `ClickToOpen` (a row's lap or race opened there, right-click Compare With); since 4.6.3 `Filters` (Track, Car and Class, one set per card) and the three tables on one row |
| `Blocks.cs` | The blocks, their rows and layout (`Block` and its `BlockKind`, `BlockRow`, `BlockCell` of `CellPart`s, `Reading`), the Leaderboard's cars around yours (`Around`) and gaps (`GapText`), units, lap times, class names; `PerfHistory` (power, torque and boost, the last 600 samples, peaks) |
| `BlockView.cs` | A block's cells in columns (label and value, rows joined, empty ones left out), the car drawing with a temperature at each wheel, Performance's graphs (`Graph`, drawn in `OnRender`), Timing's times, tyre colours, Position's and Lap's "3 / 8"s, or the Leaderboard (`BoardBody`: a column a row, its lines filled every update) (the driving ones in a `Viewbox`, `RaceFont`); in a widget being edited, a checkbox per row (the widget's `Hidden`) |
| `AnalysisSettingsView.cs` | Settings → SimEffects → Analysis, and the Units and °F / °C boxes it shares with the Profile card (`ProfileCard.UnitBoxes`, since 3.28.0; the tab before) |
| `MapView.cs` | The Map card (since 3.31.0): the map image (`LoadPicture`, off the UI thread), the line-up (`Lineup`: a least-squares line per axis from `GameMap.Anchors`), the dots and labels (`Rebuild`, placed by `Place` as the view zooms and pans), the car's dot (`Tick`), search and filters, the track picked (`ShowPicked`: its tables), `Kind` / `Form` (a track's type, guessed from its name unless `Track.Type` is set) |
| `CarDrawing.cs` | The Tires block's car from above (paths in the theme's brushes; the tyres under it, `Paint`ed by temperature), in a `Viewbox` so it scales with the widget |
| `LapTracker.cs` | `LapTracker` (races, laps, a sprint's finish and `Progress`, tracks by game, start and length, places tagged with `NameHere`, the `Nearest` to the car; each track's most laps and cars), `LapRecord`, `RaceRecord` (a race finished: start and finish places, `Result`), `LapStore` (`AnalysisLaps.jsonl`, a JSON line per lap; `AnalysisRaces.jsonl`, one per race; best by track, car and class); since 4.5.0 each `LapRecord` and `RaceRecord` names its race's log (`Session`, from `Claim`) |
| `RaceStatsView.cs` | (since 4.6.3) Racing Statistics: the tiles (`Tile`, a share's bar), Finishes' bars and Last races' places |
| `TrackMapView.cs` | (since 4.6.3) The Track Map block: `TrackLines` (the best lap's line on a track, read from its race's log off the UI thread, kept per track and best lap) and `TrackMapView` (that line, your line of the last few minutes and the car, fitted north up) |
| `RaceLog.cs` | (since 4.5.0) A session log a race: Core's `SessionRecorder` (Manual) from the reading thread while the race clock runs; `Claim` keeps one for a lap or result, the rest deleted (`SettleLocked`, 15 s after the next frames) |
| `Sessions/` | (since 4.5.0; Forza Analysis' module before) The Race Analysis: `SessionsView.cs` (its cards, the Session box, `Open` / `CompareWith` from the tables, Compare with its export and import), `SessionData.cs` (a log read back at 100 samples a second, and its laps), `PedalAnalysis.cs` (brake zones, throttle applications, scores, markers, timed tips), `Incidents.cs` (contact found and costed; speeds in mph or km/h), `SessionChart.cs` (the trace, markers and their tooltips), `CompareChart.cs` (two runs by distance, and the delta; since 4.6.3 a colour per channel, the other run dotted, each line hidden or highlighted: `Hidden`, `Highlight`, `LinePen`), `LineMap.cs` (since 4.6.3, the Track Map card: both runs' lines from X and Z, `HoverAt` from Compare's chart), `Smoothness.cs` (scores over the race, the friction circle), `MomentWindow.cs` (a zone or throttle move in a window), `OverlayChart.cs` (zones or applications drawn over each other), `SessionSettings.cs` (`ForzaAnalysis.json`) |
| `KnownTracks.cs`, `Tracks.json` | The tracks the app comes with, embedded, merged into `AnalysisSettings.Tracks` once per version of the file (`AnalysisSettings.KnownTracks`, a hash of it) |
| `AnalysisSettings.cs` | `AnalysisSettings` (`Analysis.json`: Enabled, `OnOffKey`, units, Temperature, `BoardRows`, `StartRadius` (since 3.30.2), `BlockWidget`s, `Track`s (since 3.31.0 with a `Type`), `Maps` (since 3.31.0: a `GameMap` per game, its image and `MapAnchor`s), car names) |

The race data comes from `Telemetry/ForzaSource.cs` (`TelemetryFrame.HasRace` and the fields after it): the dash's
place, power, torque, tyres, boost, fuel, distance, laps and position, and Horizon's CarGroup (offset 232, the first of
the 12 bytes Horizon puts before the dash). The Leaderboard's field is `TelemetryFrame.Field` (`Standings` of `Racer`s,
`Telemetry/Telemetry.cs`), made by `Telemetry/IRacingSource.cs` (`Field`, 4 times a second: the drivers and sessions from
the session YAML, `ReadField`; CarIdxPosition, CarIdxLapCompleted, CarIdxLapDistPct, CarIdxEstTime, CarIdxBestLapTime,
CarIdxOnPitRoad; a lap count a moment late at the line is mended).
