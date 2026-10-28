# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.24.3 strip rows, Analysis shortcut, Play colours, widgets follow on/off

- **Asked:** OverlayFrame widgets' strip: Background slider only (no Opacity), On top + Title, Done + Delete bottom
  right; Analysis on/off shortcut; drop "Waiting for X on port N" everywhere, Play colour = game state; a tool off hides
  its widgets, through Core's widget code.
- **Decisions (mine):** Core `Widget`: `opacity: false` (window always 1), `stacked: true` (rows), `AddSlider`,
  `Showing` (Hide/Show, stays in `Open`/ListCard; edited one stays till Done). Named `Showing`: `Shown` clashes with
  `OverlayWindow.Shown(int)`. Analysis/Shaker widgets `Showing = settings.On`. Game running = profile's Programs (or
  `Games.Programs`) in the process list, `GameSwitcher` every 3 s + on `SimProfiles.Changed` → `SimData.GameRunning`.
  Play: white/black glyph stopped no game; green stopped + game; blue #2F6FEB playing + game; red playing, no game
  (my reading of "red being stopped"). `SimData.Status` returns "" while not receiving.
- **Tested** in DaisysDev (FH profile Programs + PING.EXE, AutoStart false; fixtures restored): the 4 Play colours,
  the strip layout, Stop/Play and Analysis off/on (via the Profiles tile: the ON chip only reacts to Click, UIA can't)
  hide/show the Timing widget, shortcut button shown. Not: Shaker meters' hide on Shaker off, the shortcut pressed.

## 3.24.2 (released) widgets' look, Stop hides them, PBC, Tracks tiles, tyre colours

- `OverlayFrame` draws the card for Analysis' widgets and the Shaker meters (`card: false`): `Background` (default 1),
  `Title` (null = off for Timing/Big), shadow below 0.5. PB = car + track any class; PBC = class + track; session best
  car + class. FH6 class 6 = R, 7 = X (unconfirmed); others 6 = X. Tyre bands °F 140/210/240. `HasLaps` = TotalLaps > 1
  or LapNumber > 0. Seeds: `%TEMP%\da3241\seed.py`, `scroll.ps1 <pct>`.
- **Next:** the user's FH6 report (`docs/UNTESTED.md` 3.24.3, 3.24.2, 3.24.0).

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
