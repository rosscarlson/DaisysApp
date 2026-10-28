# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.24.2 widgets' look, Stop hides them, PBC, Tracks tiles, tyre colours (released 2026-10-28, tag v3.24.2, Release run passed, 20 assets)

- **Asked:** all overlays: hide the title (and its room), less padding, background opacity; Timing PB = this car on
  this track, new PBC = any car of the class on this track ("—" until one); a Tracks block (tile per track, 4 across:
  name + laps, Overall, each class's best with car); delete Lap, Position shows Lap beside it (centred alone with no
  laps); Tires red/orange/green on the tyre and the text, temps in Settings; bug: Stop left widgets showing.
- **Decisions (mine):** `OverlayFrame` (SimEffects/) draws the card for Analysis' widgets and the Shaker meters (Core
  `Widget` with `card: false`, so Core untouched): `IOverlayPlace.Background` (default 1) and `Title` (null = default:
  off for Timing/Big), a drop shadow when Background < 0.5. Input Overlay already had Background and went off with Stop.
  Stop/Play: tools' `Loaded` hides/shows the windows (not deleted; one being edited stays). PB = car + track any class;
  session best still car + class. FH6 class 6 = R, 7 = X (web: FH6 added R above S2; unconfirmed in data); others 6 = X.
  Tyre bands °F 140/210/240 (below green: no colour). Old Tracks list kept as "Track names" (rename/delete).
  `HasLaps` = TotalLaps > 1 or LapNumber > 0. BlockView updates ticked lines even when collapsed (Lap re-appears).
- **Tested** in DaisysDev (FH profile moved to 8011 for the test, laps seeded; `%TEMP%\da3241\seed.py`,
  `scroll.ps1 <pct>` scrolls the main window): widgets with 30% bg, no title, coloured tyres, Position+Lap; Stop hid all
  3 (+ Shaker meters at 40%), Play showed them; edit strip Title/Background; Tracks tiles (FH5 track has no R);
  Settings °C 110 → 230 °F saved. Fixture restored. Not: a sprint (Position alone), real FH6.
- **Next:** the user's FH6 report (`docs/UNTESTED.md` 3.24.2 and 3.24.0): class numbers, tyre bands, readability at 0%.

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
  settings file changed. The other session also tests in DaisysDev and may stop it mid-test.
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
