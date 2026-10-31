# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.28.0 Analysis layout, last lap, units on Profiles, Shaker Test (2026-10-30)

- **Asked:** Name Track (Name + Export in it; the name auto-filled at a known place, Save renames) and Current Location
  (name or Unknown + coordinates); Track names → Discovered Tracks, sortable headers, Game filter, a location column to
  find near ones; Tracks → Best Times, only classes raced, rows equal height; Widgets card last; a 3-lap race kept 2
  laps; Imperial / °F to the Profile card ("Units", above Programs → "Processes", more space); a fold button on every
  block, remembered; Shaker: an effect's Test does nothing (the meter moves).
- **Done:** `AnalysisView` rewritten around `Foldable(id, …)` (`AnalysisSettings.Folded`); Core's `Widget.ListCard()`
  re-headed (its rows moved under ours). Name Track: `matched` = `LapTracker.Here()` (race's track, else `Nearest`);
  box filled unless the user's typing; Save renames, Add New tags. Discovered Tracks: DataGrid, `TrackRow` (Name setter
  renames via Dispatcher), Location sorts by Z-order 500 m `Cell`, Nearest by metres; `Fill()` keeps sorts.
  `ProfileCard.UnitBoxes` set by AnalysisApplet. Last lap: in a circuit (lapsEnded > 0, ≥85% of the lap) LastLap
  changing or the clock still 1 s ends it; `Stopped()` no longer needs the 1.5 s data gap for circuits (it did for the
  lost lap: clock straight to 0). Shaker: `ShakerOutput` plays a tested effect through Master's / its own mute
  (cause, proven by loopback: Master muted); `EffectSignal.Nowhere` → notification.
- **Tested:** DaisysDev + `%TEMP%\da328\race_fin.py <port> <x> <z> <lap m> <laps> <s> zero|still|lap` (3 of 3 both),
  `roam.py <port> <x> <z> <s>`; Shaker: `%TEMP%\da328\loop` (`loop.exe loop <device id> <Kind>`, `MUTED=1`; refs the
  built module DLL; `%TEMP%\shakertest` no longer builds). Not clicked: sort headers, Game's funnel.
- **Next:** the user's FH6 report (`docs/UNTESTED.md` 3.28.0). Still offered: iRacing standings, Core-wide widget keys.

## Earlier Analysis releases (all released; details in `CHANGELOG.md`)

- 3.27.1: StartRadius/PlaceRadius 750, `SeriesRadius` 150; a tagged race matching a known track renames it; sprint
  `Progress`; hotkeys `Analysis.Widget.<id>` (`BlockWidget.Key`). Fixtures `%TEMP%\da327\sprint.py`.
- 3.24.7: Forza sends nothing after a finish, so `Stopped()` (data gone ≥1.5 s, `GapMs`, then free roam or a series'
  next race > `SeriesRadius`) ends a sprint (≥85% of a known length, else a quit) or a circuit's last lap; a tagged
  place = the next race's track (15 min, `PendingMinutes`); Export. No published FH race coordinates exist.
  Fixtures: `%TEMP%\da327\` (`sprint.py <port> <x> <z> <roam> <sx> <sz> <secs> <gap> [share]`, `series.py`,
  `restart.py`, `race3.py`).
- 3.24.3: Core `Widget` `opacity: false`, `stacked: true`, `AddSlider`, `Showing` (not `Shown`); widgets
  `Showing = settings.On`; Play colours from `SimData.GameRunning`.
- 3.24.2: `OverlayFrame` card (`Background`, `Title`); PB = car + track, PBC = class + track; FH6 class 6 = R, 7 = X
  (unconfirmed); tyre bands °F 140/210/240. Seeds: `%TEMP%\da3241\seed.py`.
- 3.24.0: car lists (`Telemetry/Cars.json`, built by `%TEMP%\da324db\build.py`), `SimProfile.Version` →
  `CarNames.Edition`; pause hold; Bahnschrift SemiCondensed. `%TEMP%\da324\race2.py <port> <ordinal> <laps>`.

## Analysis design (3.23.1)

- Track = start point within 750 m (150 before 3.27.1) + first-lap length within 6% or 250 m (length re-measured on the first flying
  lap); lap recorded when LapNumber rises (time = LastLap); laps in `%APPDATA%\DaisysApp\AnalysisLaps.jsonl` (outside
  Storage); a new track = a notification (since 3.27.1 the top box only tags the place the car is); car names typed by the user (FH sends only CarOrdinal); PB per
  track + car + class; widgets global. FH offsets: 224 drivetrain, 228 cylinders, CarGroup U32 at 232; dash d+0 pos,
  +16 power W, +20 torque N·m, +24 tyres °F, +40 boost, +48 distance, +52 best/last/current/race, +68 U16 lap, +70 pos.
- Test: `%TEMP%\da323
ace_send.py <port> <x> <z> <lap m> <laps> [s]`. Untested on real FH6: units, CarGroup,
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
