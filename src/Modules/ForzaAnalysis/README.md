# Gaming: Forza Analysis

*A module of [Daisy's App](../../../README.md): the **Forza Analysis** sub-tab of the [Gaming](../Gaming/README.md) tab (with Button2Key and ForzaWheel; on a tab of its own when it's the only one installed). Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\ForzaAnalysis\`. New in 1.2.0, a first draft.*

Driving sessions, analysed for how smoothly you use the pedals, as a driving coach (or a tool like Racing Labs) would
look at them, mostly the **brake and throttle, going on and coming off**. Since 4.4.0, with the game's telemetry, it
also compares laps with your own or someone else's, finds contact with walls and cars, and says about what each habit
costs a lap.

## Where the sessions come from

[SimEffects → Input Overlay](../SimEffects/InputOverlay/README.md) logs a session while it shows (**Record a session
log while it shows**, on to start with; ForzaWheel's input overlay did before 3.17.0, though a bug meant none was ever
written). The session starts when a pedal or the wheel first moves, and a
row is written about every 10 ms (steering, throttle, brake, clutch, handbrake). It ends when the overlay closes or
after two minutes with nothing moving, and the next movement starts a new one. A session with less than 20 seconds of
driving isn't kept. The pedals and wheel are the game's telemetry, or a controller's axes (its Read From).

Since 4.4.0 (format 2) each row also has the game's telemetry while it's sending: speed, lateral and longitudinal g,
the lap and the time into it, distance, where the car is, and the fastest suspension movement; and a `# car,` line
names the car. Laps, distance and place come only where the game gives them (Forza). Older sessions have none of it,
so the parts that need it stay empty for them.

The sessions are CSV files, one per session, named after when it started, in `%LOCALAPPDATA%\DaisysApp\ForzaSessions\`
(imported ones in its `Imported\`). That folder is the same for Daisy's App and the wrapper, so they can read each
other's sessions. The format is in Core's `Shared/SessionLog.cs`, which both modules use.

## The tab

The page has no explanations on it (since 4.4.0): only labels, and short tooltips.

**Left: the sessions**, newest first. Each row shows its driving time (time in menus doesn't count), best lap and
overall score; an imported one starts with its driver's name. **Delete** removes one, **Import…** adds someone else's
(see Compare) and **Open folder** opens the folder.

**Right: the session you picked.**

- **The name** at the top: give it one (the car, the track). It's kept in `ForzaAnalysis.json`. Under it: the driver
  (if imported), the date, the car, the length, the laps and the best one.
- **Scores**, 0 to 100. Green is 85 and over, amber 65 and over.
  - **Brake application**
  - **Brake release**
  - **Throttle application**
  - **Throttle release**
  - **Steering**
  - **Consistency**
  - **Overall**: the others together, with the brake release counting most.
- **The time split**: full throttle, part throttle, braking, both together, and coasting.
- **Key numbers**: the average brake peak, application time, release time and trail braking; coasting from brake to
  throttle; throttle to brake; throttle application time; throttle stabs; steering corrections a minute; and, with
  telemetry, impacts and crashes.
- **Trace**: throttle, brake, steering and speed (clutch too, if you tick it) over time, each lap's start marked.
  - The brake zones are shaded and numbered.
  - Marks at the top show what's worth a look (impacts violet, crashes pink): point at one and a tooltip under it
    says what it is.
  - The mouse wheel zooms, dragging moves along, and a double-click shows it all. Clicking a zone picks it.
  - The strip underneath is the whole session; click or drag it to jump.
- **Compare** (since 4.4.0): **This** (the whole session or one of its laps, the best starred) against **With** (any
  session, yours or imported, and its lap; the same session's other laps too).
  - A lane each, along the lap's distance (by time without it): **Delta** (the other's time less this one's at each
    point: above the line, this one is ahead), speed, throttle, brake and steering; each is a chip to show or hide.
  - This run is blue, the other orange. A box beside each hides it; clicking its name (or its line) highlights it
    white.
  - The wheel zooms, dragging moves along, a double-click shows it all; the readout gives both runs' values.
  - Under the picks: the lap times and the gap, and *Not the same track?* when their starts are over 500 m apart or
    the laps' lengths differ by over 6%.
  - **Corners**: each of this run's brake zones with the other's nearest (within 100 m): where the other braked (+
    later, in metres), peak, entry and slowest speed, and the time gained or lost through it. Picking a row zooms
    there.
  - **Export…** saves This (a lap or the session) as a session file with your name (**Driver**; Windows' user name if
    it's empty) and the session's. **Import…** copies files like it into `ForzaSessions\Imported\`, where they're
    listed and analysed like your own, and puts the first in **With**.
  - Speeds are in mph where the region isn't metric, km/h elsewhere.
- **Brake zones compared**: every zone drawn over the others, with the average dashed and the zone you picked bold.
  Line them up **at the brake point** to compare the applications, or **at the release** to compare the releases.
- **Throttle applications compared**: the same for each time the throttle goes on.
- **Brake Analysis** and **Throttle Analysis**, side by side (since 4.4.0; *Brake zones* and *Throttle* before), a row
  each:
  - the brake's: #, Peak, Hold, Release, Smooth, Trail, AppScore and ResScore;
  - the throttle's: Way, From, To, Time (10% to 90% of the way), Smooth and Score.

  Picking a row zooms the trace there; a click also opens it in a window: its moment of the trace (a second either
  side; the zone's application, hold and release shaded; speed too), its numbers (Application and Release are the
  scores) and its notes, such as *let go from 75%*, *pressed again while releasing*, *spiked to 98%, held 83%*, *slow
  to build*, *throttle on too*, *at 100% for…*, *stab*, *lifted on the way*, *straight to full* and *part lift*.
- **Smoothness** (since 4.4.0), left of the Tips, half and half:
  - every brake and throttle application (filled) and release (hollow) at its score over the session, with each
    one's running average; clicking a dot picks it;
  - the friction circle: the car's g, sideways across, braking down, accelerating up; red braking, green on the
    throttle, grey coasting; the peaks under it.

  Chips show or hide Brake, Throttle, On and Off.
- **Tips**: what the numbers suggest, in plain words, each with about how much time it would save (a lap when the
  game sent laps, else the session), the most first. For example: ease the brake off over half a second as you turn
  in; don't press it again while releasing; build pressure without spiking it; commit to one progressive throttle
  application; don't coast; keep the pedals apart; and contact with walls and cars, in red when it's too much (two
  crashes or more, contact once a lap or more (every two minutes without laps), or over a second a lap).

## How it's worked out (`PedalAnalysis.cs`)

The log is resampled to an even 100 Hz (`SessionData.cs`) and lightly smoothed (30 ms).

**Brake zones.** A zone is the brake above 5%, from the foot leaving the floor until the pedal is let go. If the brake
is let go for under 0.15 s and pressed again, it's the same zone. Zones that peak under 8% are ignored.

- **Application**: the time from 10% to 90% of the peak. Over 0.3 s for a firm stop (50% or more) counts as slow.
- **Spike**: the peak in the first half-second is more than 8% over the level then held (the 80th percentile of the
  rest of the zone).
- **Release**: starts where the pressure last falls below 90% of the level held.
  - Its smoothness is the net travel divided by the total travel (jitter under 1% ignored), so 100% means one movement.
  - Pressing again by 3% or more counts as a re-application.
  - A brake still at 30% or more 0.1 s before it's let go counts as *let go in one go*.
- **Trail braking**: the share of the release with the wheel turned 5% or more of its travel.
- **The scores**:
  - The application score is smoothness², less for a slow build-up or a spike.
  - The release score is smoothness², less for letting go in one go or a release under 0.15 s.
  - Zones under 15% don't count, and the session score weighs each zone by its peak.

**Throttle.** The pedal's turning points are found, ignoring wobbles of under 8%. Each rise or fall of 25% or more is
an application or a release.

- Its time is 10% to 90% of the way.
- Its smoothness is measured as for the brake, and *lifted on the way* means a drop of 3% or more during the rise.
- A **stab** is on by 30% or more in 0.15 s, then off again by 20% or more within 0.3 s.

**Laps** (`SessionData.cs`): a run of the same lap number; complete when the next one follows and it started at the
line. Its time is the game's time into it at its end, plus what was left across the line.

**Contact** (`Incidents.cs`), as the shaker's Crashes and Impacts feel it: a crash is 3.5 g or more sideways and
lengthways together; an impact is a jolt, that g changing by 1.3 g or more within 50 ms (a car or a wall: braking and
kerbs build slower), or a suspension hit of 1.5 m/s with a jolt of 0.8 g. Only above about 18 km/h.

**What it costs.** Contact: with two clean laps or more, the laps with contact against the clean laps' median;
otherwise the speed it took off (more than the trend before it, within 0.6 s, 1.5 s for a crash) and the time to get it
back at about 0.4 g (Δv² / 2av), plus any time stopped after a crash. The pedal tips' times are rules of thumb: a
re-application 0.1 s, a stab 0.1 s, a snapped release, a spike or a lift on the way 0.05 s, half of a slow build's time
over 0.3 s, 0.3 s a second of coasting (over 0.3 s a zone) or of both pedals, and 0.15 s a second at 100%. A lap's
share is the session's over the laps its driving is worth (its driving time over the median lap).

**Also:**

- **Steering corrections**: turns back of less than 13° on Forza's 450° wheel, within a second.
- **Consistency**: from how much the firm zones' peak, application time and release time vary, if there are 4 zones or
  more.
- **The time split**: counts only time within 3 s of something moving.

`PedalAnalysis.Version` is bumped when any of this changes, so the session list works its scores out again. They're
cached in `ForzaAnalysis.json` (`Summaries`).

## Not yet

Sectors; wheelspin (whether a stab spun the wheels); and laps in iRacing and ACC (their sessions have speed and g,
not laps).

## Code

`ForzaAnalysisApplet.cs` (the module: its sub-tab on the Gaming tab), `ForzaAnalysisView.cs` (the page: the sessions
list, the blocks, Compare with its export and import), `SessionData.cs` (a session log read back, resampled to 100
samples a second, and its laps), `PedalAnalysis.cs` (the brake zones and throttle applications, their scores, markers
and timed tips), `Incidents.cs` (contact found and costed; speeds in mph or km/h), `SessionChart.cs` (a session's
traces with the markers and their tooltips), `CompareChart.cs` (two runs over each other by distance or time, and the
delta), `Smoothness.cs` (the scores over the session, and the friction circle), `MomentWindow.cs` (a brake zone or
throttle move in a window), `OverlayChart.cs` (every brake zone or throttle application drawn over each other),
`ForzaAnalysisSettings.cs` (`ForzaAnalysis.json`). The sessions come from SimEffects → Input Overlay
(`../SimEffects/InputOverlay/OverlayReader.cs`, written with Core's `Shared/SessionLog.cs`).
