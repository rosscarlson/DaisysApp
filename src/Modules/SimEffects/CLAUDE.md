# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 3.23.4 Profile card, Auto Start, Analysis layouts (released 2026-10-28, tag v3.23.4, Release run passed, 20 assets)

- **Asked:** Profile card: Name a drop-down like the top right's; Edit → box → Save; Edit/New/Delete bottom right
  (Delete via Core's ConfirmButton, it already was). Game Switching → per-profile "Auto Start" Yes/No: program starts →
  play, program ends → stop. Analysis: Tires as a top-down sports car with temps at the wheels; Vehicle in 2 columns
  (Make, Class Index, Group · Drive, HP TQ Motor "500HP 510LB/FT V8", Boost); Laps in pairs (Last·Best, Curr·PB,
  Pos - Lap·Race (lap and dash hidden without one), Track·Make - Class Index).
- **Decisions (mine):** `SimProfile.AutoStart` (bool?, null = from old `GameSwitching` && has programs); No Game
  dropped; stop only if the auto-played profile is still active and playing; New opens the name box (`naming`).
  Cells = `BlockCell` of `CellPart`s, labels joined like values; a part showing "—" is dropped. Widget edit mode: a
  cell's checkboxes replace its label and value (widgets keep a fixed size, values got squeezed). Motor: ≤5 cyl "I",
  else "V" (FH sends only the count). Widget first size 420 wide; tires 240×260, `Viewbox`.
- **Files:** `Profiles/` (ProfileCard, ProfilesApplet (Game Switching card removed), GameSwitcher, SimProfiles,
  README), `Analysis/` (Blocks, BlockView, new CarDrawing, AnalysisApplet's widget first size, README),
  `SimEffects/README.md`, `lang/en.json`.
- **Tested** in DaisysDev (fixture: FH profile on 8011 with PING.EXE; restored, laps file removed): rename via
  Edit/Save, Auto Start play/stop with ping, blocks on the tab and widgets (edit, untick, Done). Not: Auto Start No
  (combo can't be opened by UIA unfocused), real FH6.
- **Next:** the user's report on 3.23.4 and FH6 (`docs/UNTESTED.md`: Analysis 3.23.1 + 3.23.4); maybe an engine
  layout setting per car if "V" is wrong for I6/flat-6; then more analysis tools and other games (asked to grow later).

## Analysis design (3.23.1)

- Track = start point within 150 m + first-lap length within 6% or 250 m (length re-measured on the first flying
  lap); lap recorded when LapNumber rises (time = LastLap); laps in `%APPDATA%\DaisysApp\AnalysisLaps.jsonl` (outside
  Storage); new-track prompt = a card + a notification; car names typed by the user (FH sends only CarOrdinal); PB per
  track + car + class; widgets global. FH offsets: 224 drivetrain, 228 cylinders, CarGroup U32 at 232; dash d+0 pos,
  +16 power W, +20 torque N·m, +24 tyres °F, +40 boost, +48 distance, +52 best/last/current/race, +68 U16 lap, +70 pos.
- Test: `%TEMP%\da323ace_send.py <port> <x> <z> <lap m> <laps> [s]`. Untested on real FH6: units, CarGroup,
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
  `winshot.ps1` (main window PNG), `shot.ps1` (screen rect); `%TEMP%\da323\uia.ps1` adds `set <name> <value>`.
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
