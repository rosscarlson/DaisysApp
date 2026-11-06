# Session notes: Forza Analysis

Read only when working in this folder. Keep it short.

- **4.4.0:** marker tooltips (drawn in `SessionChart.MarkerTip`, hover the arrow row); Compare (`CompareChart`, by the
  lap's distance, time without it; Export/Import into `ForzaSessions\Imported\`, keys `Imported\<file>` in
  settings); Smoothness (`Smoothness.cs`); Brake/Throttle Analysis with `MomentWindow` on a row click; contact
  (`Incidents.cs`) and timed, sorted `Tip`s; descriptive text removed. Needs session format 2 (Core 0.57
  `SessionTelemetry`, written by SimEffects' `OverlayReader.Telemetry`). `PedalAnalysis.Version` 2.
- **Tested** in DaisysDev with synthetic format-2 sessions (`%TEMP%\fatest\gen.py <folder>`: "Ross" 4 laps + an
  impact, "Friend" slower, a crash and an impact; `ui.ps1` selects tabs / combos / scroll by UIA and saves a
  PrintWindow PNG; `deploy.ps1` copies the build into DaisysDev). Pick a session by setting `Selected` in
  `%APPDATA%\DaisysDev\ForzaAnalysis.json` (rows are virtualised). Not tried: hovering a marker, a row click opening
  the window (both need the mouse), a real Forza session.
- The time estimates for pedal habits are rules of thumb (README); contact is measured against clean laps when there
  are two or more.
