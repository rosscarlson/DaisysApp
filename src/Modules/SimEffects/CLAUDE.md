# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.27.1 Analysis: New track = live place tagger, 750 m, sprint Done %, widget shortcuts (2026-10-30)

- **Asked:** the New track box cycled through every unnamed track: should just show where I am and tag it on Save,
  then be ready for the next; starts not found (grid 100–500 m from the marker): 750 m radius; Laps widget: % done in
  a sprint (as the HUD), lap in 2+ lap races; standings of all drivers with gaps (Forza sends no other cars: not done,
  offered for iRacing, whose CarIdx arrays have them; Analysis doesn't read iRacing yet); a shortcut per widget.
- **Done:** StartRadius/PlaceRadius 750, `SeriesRadius` 150 (the next race of a series); `Near()` nearest first; a
  tagged race that matches a known track renames it (if unnamed) and drops the place. `Progress` = distance since the
  race start / Track.Length while lapsEnded 0 and Track.Laps ≤ 1. Hotkeys `Analysis.Widget.<id>` (`BlockWidget.Key`):
  open → `RemoveWidget`, closed → `ShowWidget`. Name button removed. Tested with `%TEMP%\da327\sprint.py`
  (fixtures restored; DaisysDev's `AnalysisLaps.jsonl` deleted).

## 3.24.7 Analysis: sprints by the data stopping, last laps, Name → next race, Export (released 2026-10-28)

- **Asked:** sprints still not recorded (real FH6); Name should make its place the next race's track; Export (flag
  icon) the tracks as JSON; search online first for FH6/5/4 race coordinates (none published: map sites have pixel
  markers only; FH6 sends no TrackOrdinal), so Name stays.
- **Cause:** Forza's FH6 Data Out guide: nothing is sent after finishing a race (3.24.5's clock-stop / LastLap checks
  never fire). Now `Stopped()`: the data gone ≥1.5 s (`GapMs`) then back in free roam, or in a race starting > 150 m
  from this one's start (a series) → a sprint finished at its last rt (>100 m driven; ≥85% of a known length, else
  "not a finish" = quit); a circuit's last lap the same, time = CurrentLap. New race also when rt goes back after a
  gap. Name → `pending` place = the next race's track wherever it starts (15 min, `PendingMinutes`), taken once.
- **Tested** in DaisysDev (`%TEMP%\da327\`: `sprint.py <port> <x> <z> <roam> <sx> <sz> <secs> <gap> [share]`,
  `series.py`, `restart.py`, `race3.py` = race2 + gap + free roam; `export.ps1` + `dlgsave.ps1` drive the Save As by
  UIA/WM_COMMAND; the AI tab also has an "Export" button: pick by HelpText). DaisysDev needs Core 0.44 since 3.24.6
  (copied). Fixtures restored.
- **Next:** the user's FH6 report (`docs/UNTESTED.md` 3.24.7); perhaps an Import for a shared track list.

## 3.24.5 (released) sprint finish by clock stop / LastLap, Name places (300 m, Length 0 tracks; `Near` skips them)

## 3.24.3 (released) strip rows, Analysis shortcut, Play colours, widgets follow on/off

- Core `Widget`: `opacity: false`, `stacked: true`, `AddSlider`, `Showing` (not `Shown`: clashes with
  `OverlayWindow.Shown(int)`). Analysis/Shaker widgets `Showing = settings.On`. Game running = profile's Programs in
  the process list (`GameSwitcher`, 3 s) → `SimData.GameRunning`. Play: white stopped, green stopped + game, blue
  playing + game, red playing no game. Untested: Shaker meters' hide, the shortcut pressed.

## 3.24.2 (released) widgets' look, Stop hides them, PBC, Tracks tiles, tyre colours

- `OverlayFrame` draws the card for Analysis' widgets and the Shaker meters (`card: false`): `Background` (default 1),
  `Title` (null = off for Timing/Big), shadow below 0.5. PB = car + track any class; PBC = class + track; session best
  car + class. FH6 class 6 = R, 7 = X (unconfirmed); others 6 = X. Tyre bands °F 140/210/240.
  Seeds: `%TEMP%\da3241\seed.py`, `scroll.ps1 <pct>`.
- **Next:** the user's FH6 report (`docs/UNTESTED.md` 3.24.2, 3.24.0).

## 3.24.0 (released) Profiles tiles, car names, pause hold, race widgets

- Car lists from HDR's gists built by `%TEMP%\da324db\build.py` into `Telemetry/Cars.json` (embedded; ordinals are
  global). `SimProfile.Version` (null = Auto: GameSwitcher sees ForzaHorizonN.exe) → `CarNames.Edition` ("FH6"). Old
  "FH" laps/tracks/names = FH6. Pause: Analysis keeps the last Live frame (`driven`), Lights hold `held` (no frames
  500 ms → off), Input Overlay `Paused`; Shaker quiet. "Of" counts: Racers = max position seen, Laps = most recorded.
  Font: Bahnschrift + FontStretch SemiCondensed (WPF lists only the family). `%TEMP%\da324\race2.py <port> <ordinal>
  <laps> [lap s] [pause s]` sends a race with a zero-packet pause (its clock runs 20×: back-to-back restarts unseen).

## Analysis design (3.23.1)

- Track = start point within 150 m + first-lap length within 6% or 250 m (length re-measured on the first flying
  lap); lap recorded when LapNumber rises (time = LastLap); laps in `%APPDATA%\DaisysApp\AnalysisLaps.jsonl` (outside
  Storage); new-track prompt = a card + a notification; car names typed by the user (FH sends only CarOrdinal); PB per
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
