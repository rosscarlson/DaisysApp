# Gaming: Forza Analysis

*A module of [Daisy's App](../../../README.md): the **Forza Analysis** sub-tab of the [Gaming](../Gaming/README.md) tab (with Joy2Key and ForzaWheel; on a tab of its own when it's the only one installed). Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\ForzaAnalysis\`. New in 1.2.0, a first draft.*

Driving sessions, analysed for how smoothly you use the pedals, as a driving coach (or a tool like Racing Labs) would
look at them, mostly the **brake and throttle, going on and coming off**.

## Where the sessions come from

[ForzaWheel](../ForzaWheel/README.md) logs a session while its **input overlay** shows (**Record a session log while it
shows** in its Input overlay block, on to start with). The session starts when a pedal or the wheel first moves, and a
row is written about every 10 ms (steering, throttle, brake, clutch, handbrake). It ends when the overlay closes or
after two minutes with nothing moving, and the next movement starts a new one. A session with less than 20 seconds of
driving isn't kept. While it records, the emulator reads the devices every 10 ms, even when it isn't feeding vJoy.

The sessions are CSV files, one per session, named after when it started, in `%LOCALAPPDATA%\DaisysApp\ForzaSessions\`.
That folder is the same for Daisy's App and any one-tool app, so they can read each other's sessions. The format is in
`../ForzaWheel/SessionLog.cs`, which both modules compile in.

## The tab

**Left: the sessions**, newest first. Each row shows its driving time (time in menus doesn't count) and its overall
score. **Delete** removes one, and **Open folder** opens the folder.

**Right: the session you picked.**

- **The name** at the top: give it one (the car, the track). It's kept in `ForzaAnalysis.json`.
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
  throttle; throttle to brake; throttle application time; throttle stabs; and steering corrections a minute.
- **Trace**: throttle, brake and steering (clutch too, if you tick it) over time.
  - The brake zones are shaded and numbered.
  - Marks at the top show what's worth a look: point at one to see what it is.
  - The mouse wheel zooms, dragging moves along, and a double-click shows it all. Clicking a zone picks it.
  - The strip underneath is the whole session; click or drag it to jump.
- **Brake zones compared**: every zone drawn over the others, with the average dashed and the zone you picked bold.
  Line them up **at the brake point** to compare the applications, or **at the release** to compare the releases.
- **Throttle applications compared**: the same for each time the throttle goes on.
- **Brake zones**: a table with a row for each zone.
  - Peak.
  - Apply: 10% to 90% of the peak.
  - Hold: at 90% or more of the level held.
  - Release: from the hold to letting go.
  - Smooth: 100% means the release was one movement.
  - Trail: how much of the release was with the wheel turned.
  - Coast: from letting go of the brake to the throttle.
  - The apply and release scores.
  - Notes, such as *let go from 75%*, *pressed again while releasing*, *spiked to 98%, held 83%*, *slow to build*,
    *throttle on too* and *at 100% for…*

  Picking a row zooms the trace to that zone.
- **Throttle**: a table with every application (on) and release (off) of a quarter of the pedal's travel or more.
  - From and to.
  - Time: 10% to 90% of the way.
  - Smooth.
  - Score.
  - Notes, such as *stab*, *lifted on the way*, *straight to full* and *part lift*.
- **Tips**: what the numbers suggest, in plain words. For example: ease the brake off over half a second as you turn
  in; don't press it again while releasing; build pressure without spiking it; commit to one progressive throttle
  application; don't coast; keep the pedals apart.

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

**Also:**

- **Steering corrections**: turns back of less than 13° on Forza's 450° wheel, within a second.
- **Consistency**: from how much the firm zones' peak, application time and release time vary, if there are 4 zones or
  more.
- **The time split**: counts only time within 3 s of something moving.

`PedalAnalysis.Version` is bumped when any of this changes, so the session list works its scores out again. They're
cached in `ForzaAnalysis.json` (`Summaries`).

## Not yet

Without the game's telemetry, it can't tell laps, corners, speed or wheelspin. Forza's **Data Out** (UDP telemetry)
would add those: laps and sectors to compare, braking points by distance, and whether a stab spun the wheels. That's
the natural next step.
