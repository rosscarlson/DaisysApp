# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.30.3 Races kept (2026-10-31)

- **Asked:** a race finished 4th in FH5, nothing in Races. No `AnalysisRaces.jsonl` existed at all: no race was ever
  kept. Log: a race started, then the next one, no finish line. Cause (inferred, no packets captured): after the finish
  Forza sends live frames with the clock as it was and Distance near 0 elsewhere, so `Stopped()` saw driven ≤ 100.
- **Done:** `LapTracker.furthest` (the frame furthest into the lap) judges `Stopped()`; live frames back after a gap
  ≥ `GapMs`, clock not back, distance down > 100 m or moved > `SeriesRadius` → Stopped + finished; FH5's lap bump at
  the line: lapsEnded > 0 and < 15% of the next lap driven → `Result(lapsEnded, lapEndRaceTime)`; every "not a finish"
  logs why. Fixture `%TEMP%\da3303\fin.py <port> sprint|circuit` (DaisysDev on port 8123: 8000 is taken here).
- **Check next:** the user's next real FH5 log lines ("back after …", "not a finish (…)").

## 3.30.2 Analysis start radius, layout (2026-10-31)

- **Asked:** 750 m picked another event's start 740 yd away: 500 m and a setting; Laps table beside Best laps; drop
  the Vehicle and Laps blocks/widgets.
- **Done:** `AnalysisSettings.StartRadius` (500, 50–2000, clamped on Load) is `LapTracker`'s StartRadius and
  PlaceRadius (no longer consts); Settings → Analysis → Tracks → Start radius (m). Blocks.All = Tires, Performance,
  Timing, Position, Leaderboard (Vehicle/Laps definitions and Car/Engine/DriveName removed; their `Widgets` entries
  dropped on Load); Name Car moved into the Name Track card. Tested on screen in DaisysDev only.
- **Lesson:** OneDrive's provider stopped and left `.git/objects/pack` and 8 sources (Core `Shared/Ui.cs`…) cloud-only:
  git said "mmap failed". Worked with a bare clone (`git --git-dir=%TEMP%\da-git --work-tree=.`) and built in a temp
  checkout; DaisysDev was a stale 3.26.8 mix (SimEffects got no tab): refreshed with a full build.

## 3.28.2 Leaderboard (released 2026-10-31)

- **Asked:** a leaderboard during a race, 8–12 cars, each gap between neighbouring cars (not to me).
- **Done:** iRacing only (Forza has no field). `TelemetryFrame.Field` (`Standings`/`Racer`, Telemetry.cs);
  `IRacingSource.Field` 4/s: YAML `ReadField` (Drivers minus pace car / spectators, SessionType by SessionNum), race
  (SessionState ≥ 4 + "Race") ordered by LapCompleted + LapDistPct, gap = laps × CarClassEstLapTime + ΔCarIdxEstTime;
  else CarIdxPosition + best-lap gaps; lap count late at the line mended. Block `Leaderboard` (`BlockKind.Board`,
  `BlockView.BoardBody`, `Blocks.Around`/`GapText`), `AnalysisSettings.BoardRows` (Settings → Analysis → Cars, 10).
- **Tested:** DaisysDev, `%TEMP%\da3282\ir_field.py <s> race|practice|grid [pace step]` (fake irsdk memory, 13 cars,
  idx 12 in pits; prints the true order). Kill old fakes first (`python3.12.exe` by command line): two on one map
  gave nonsense. Not tested: real iRacing, multi-class, lapped cars, grid mode.

## 3.28.0 Analysis layout, last lap, units on Profiles, Shaker Test (released 2026-10-30)

- `AnalysisView` built on `Foldable(id, …)` (`AnalysisSettings.Folded`; Core's `Widget.ListCard()` re-headed); Name
  Track `matched` = `LapTracker.Here()`; Discovered Tracks a DataGrid (`TrackRow`, Z-order `Cell`, `Fill()` keeps
  sorts); `ProfileCard.UnitBoxes`. Circuit last lap: LastLap changing or the clock still 1 s at ≥85% of a lap, and
  `Stopped()` without the data gap. Shaker: a tested effect plays through mute (cause proven by loopback).
- Fixtures: `%TEMP%\da328\race_fin.py <port> <x> <z> <lap m> <laps> <s> zero|still|lap`, `roam.py`; Shaker
  `%TEMP%\da328\loop` (`MUTED=1`). Not clicked: sort headers, Game's funnel.
- **Next:** the user's FH6 / iRacing / rig report (`docs/UNTESTED.md` 3.28.0–3.28.2). Offered: Core-wide widget keys.

## Earlier Analysis releases (all released; details in `CHANGELOG.md`)

- 3.28.1: `RaceRecord`, `LapStore.Races` (`AnalysisRaces.jsonl`), `LapTracker.Result()` (finished races only), Races
  card. Fixture `%TEMP%\da329
ace_pos.py`.
- 3.27.1: StartRadius/PlaceRadius 750, `SeriesRadius` 150; a tagged race matching a known track renames it; sprint
  `Progress`; hotkeys `Analysis.Widget.<id>` (`BlockWidget.Key`). Fixtures `%TEMP%\da327\sprint.py`.
- 3.24.7: Forza sends nothing after a finish, so `Stopped()` (data gone ≥1.5 s, `GapMs`, then free roam or a series'
  next race > `SeriesRadius`) ends a sprint (≥85% of a known length, else a quit) or a circuit's last lap; a tagged
  place = the next race's track (15 min, `PendingMinutes`); Export. No published FH race coordinates exist.
  Fixtures: `%TEMP%\da327\` (`sprint.py <port> <x> <z> <roam> <sx> <sz> <secs> <gap> [share]`, `series.py`,
  `restart.py`, `race3.py`).
- 3.24.3: Core `Widget` `Showing` (not `Shown`) = `settings.On`; Play colours from `SimData.GameRunning`.
- 3.24.2: `OverlayFrame` card (`Background`, `Title`); PB = car + track, PBC = class + track; FH6 class 6 = R, 7 = X
  (unconfirmed); tyre bands °F 140/210/240. Seeds: `%TEMP%\da3241\seed.py`.
- 3.24.0: car lists (`Telemetry/Cars.json`, built by `%TEMP%\da324db\build.py`), `SimProfile.Version` →
  `CarNames.Edition`; pause hold; Bahnschrift SemiCondensed. `%TEMP%\da324\race2.py <port> <ordinal> <laps>`.

## Analysis design (3.23.1)

- Track = start point within the start radius (setting since 3.30.2, 500; 750 from 3.27.1, 150 before) + first-lap length within 6% or 250 m (length re-measured on the first flying
  lap); lap recorded when LapNumber rises (time = LastLap); laps in `%APPDATA%\DaisysApp\AnalysisLaps.jsonl` (outside
  Storage); a new track = a notification (since 3.27.1 the top box only tags the place the car is); car names typed by the user (FH sends only CarOrdinal); PB per
  track + car + class; widgets global. FH offsets: 224 drivetrain, 228 cylinders, CarGroup U32 at 232; dash d+0 pos,
  +16 power W, +20 torque N·m, +24 tyres °F, +40 boost, +48 distance, +52 best/last/current/race, +68 U16 lap, +70 pos.
- Test: `%TEMP%\da323\race_send.py <port> <x> <z> <lap m> <laps> [s]`. Untested on real FH6: units, CarGroup,
  class numbering, sprints, rewinds, track Delete, Metric.

## How the module fits together

- `AppletGroup` tab (Order 60): Profiles, Shaker (folder ⚠ `BassShaker/`), Arduino, Notify (needs Arduino, shown
  while a board has its lights), Fanatec, AmbiLight, Input Overlay, Analysis. `HeaderRight` = Profile drop-down +
  Play/Stop + its shortcut.
- **Profiles** (3.18): `SimProfiles` (static, `SimProfiles.json`); each tool `Register`s a `ToolScope` (settings object
  + property names kept per profile) **before building its view**, and its settings' `Save` calls `SimProfiles.Saved`.
  Each settings class has `On => Enabled && SimProfiles.Playing`: runtime uses `On`, UI uses `Enabled`;
  `Ui.ShowOnOff` greys ON while stopped. Hardware (devices, boards, lights, axes, widget places, redlines) is global.
- **Telemetry:** tools read `SimData.Open()` (follows the profile's game) over `TelemetryHub` (one reader per
  game + port, so only one socket per UDP port). FH default port 8000.
- **Widgets:** always Core's `Shared/Widgets/Widget` (`IWidgetPlace` in the tool's settings, `Widget.Offer` /
  `ListCard` / `DeleteButton`); a closed widget lets its body go.
- Shaker: effects per wheel, `ShakerOutput` filters to the transducer's range, `DeviceVolumes` drives Windows' volume
  live, Level Wizard keys only registered while it's open. Arduino: speaks SimHub's sketch (ARQ) or DaisysSim
  (`Arduino/DaisysSim/`), own Win32 serial. Fanatec: HID LEDs `01 F8 09 08 hi lo` (RS rim). AmbiLight: Govee LAN
  `razer` stream, DXGI duplication with GDI fallback, LEDs = Segments unless set. Input Overlay: a `Widget` with
  `card: false`, session logs for Forza Analysis.

## Test fixtures and tools

- DaisysDev: settings in `%APPDATA%\DaisysDev`; copy `src\DaisysApp\bin\Debug\net8.0-windows\modules\SimEffects\
  DaisysApp.SimEffects.*` and `lang\en.json` into `%TEMP%\DaisysDev\modules\SimEffects\`; `taskkill /F` DaisysDev to
  restart with a fixture (it closes to the tray; starting it again shows the window). Back up and restore any
  settings file changed. A Core change: copy `DaisysApp.Core.dll` into `%TEMP%\DaisysDev\` too. The other session also tests in DaisysDev and may stop it mid-test.
- `%TEMP%\da317\`: `fh_send.py` (fake FH packets), `uia.ps1` (list/invoke/toggle/select by UIA name),
  `winshot.ps1` (main window PNG), `shot.ps1` (screen rect); `%TEMP%\da323\uia.ps1` adds `set <name> <value>`. `%TEMP%\da324\`: `rects.ps1` (DaisysDev's windows), `combo.ps1 <name> <index>` (pick a ComboBox item), `selnth.ps1 <name> <n>` (the nth selectable by name, e.g. Settings' SimEffects), `move.ps1 <title> x y` (a widget, by UIA), `setget.ps1 <name> <value>` (Edit boxes only; uia.ps1's set can hit a label).
  `PING.EXE` as a profile program stands in for a game.
- Harnesses: `%TEMP%\shakertest`, `%TEMP%\simtest` (fake SimHub/DaisysSim boards), `%TEMP%\ambitest`,
  `%TEMP%\ambisample`.

## Lessons

- A UIA Toggle raises no `Click`: use `Checked`/`Unchecked` (or test on/off through settings).
- Edit files with Python, not `sed -i` over globs (it rewrote `AccSource.cs`'s BEL char).
- Don't name a Window method `Show()` (it hides `Window.Show`).
- UIA "Invoke Delete" can hit the wrong Delete (it once removed DaisysDev's FH profile): use unique names.
- Shared git index with the other session: stage only your own files (check `git diff --cached` first), and keep your
  lines out of shared docs until you commit.
- Session logs from DaisysDev still go to the real `%LOCALAPPDATA%\DaisysApp\ForzaSessions`.

## Waiting on the user (see `docs/UNTESTED.md`)

Real hardware and games throughout: shakers on the rig, an Arduino with SimHub's sketch, the Fanatec RS rim's LEDs,
the Govee ropes (largest LED count they take), FH6 for Analysis.
