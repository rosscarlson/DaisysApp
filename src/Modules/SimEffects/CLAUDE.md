# Session notes: SimEffects

- **3.34.0 (asked 2026-10-31: ship the user's mapped FH5+FH6 `tracks.json`):** `Analysis/Tracks.json` embedded,
  `KnownTracks.Merge` at `AnalysisSettings.Load` once per file hash (`KnownTracks`); 178 of 187 added in DaisysDev
  (the rest matched by name or start).

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Where things stand (2026-10-31)

3.33.0 is released (Overlays, Dash); nothing of this work is open. **Next:** the user tries it
(`docs/UNTESTED.md` 3.33.0, 3.31.1, 3.31.0, 3.30.3); read their next real FH5 log lines ("back after …", "not a finish (…)").
The user is still mapping events on the Map; later the tracks get baked into the app. Offered: Core-wide widget keys.

## 3.33.0 Overlays (Input Overlay renamed), Dash

- **Asked:** rename Input Overlay to "Overlays", many kinds: Analysis' widgets moved here, a list with
  create/edit/delete at the left and the picked one's settings at the right, Core widgets; a new "Dash" (analog tach,
  speed, gear, TC/ABS lights), FH5/FH6 look if possible.
- **Done:** folder and ids unchanged (`InputOverlay/`, tool id `InputOverlay`, `InputOverlay.json`). `OverlayKinds`
  (Inputs, Dash, block ids); `OverlaySettings.Overlays` (list), `Inputs` (scoped), `Dash` (`DashSettings`, global).
  The applet owns every overlay window (`ShowOverlays`: open while made + ticked, `Showing` = tool On); block widgets
  read `AnalysisApplet.Current` (`Ticked`, `Wanted`, `Now()`), places still in `Analysis.json`; hotkey ids kept
  (`Analysis.Widget.<id>`). `FromBefore` (no `Overlays` in the file): list = Inputs + shown blocks; profiles with
  Analysis on and the overlay off get Enabled true, Inputs false (JSON edited before `Register`). Dash = `DashWindow`
  (Widget card:false, `BackgroundOpacity`), drawn from `OverlayReader.Frame` (last live frame). TCS = `Slip` > 0.35
  with throttle (Forza has no TC flag). A block pane's change closes and reopens its widget (`Redraw`).
- **Tested:** DaisysDev + `%TEMP%\da333\fh.py <port> <s>` (fake FH 324-byte, sweeping revs/gears, ABS/slip blips),
  `uia.ps1`, `winshot.ps1`, `shot.ps1`. SimHub holds UDP 8000 on the rig: DaisysDev's FH profile set to 8123 while
  testing (restored). `SimUi.OnOff` listens to Click: UIA Toggle can't switch it (used Stop/Play).

## 3.32.0 default profiles

- **Asked:** "default profiles are missing: use my current settings (the rig's) as a new install's defaults."
- **Done:** `Profiles/Defaults.json` (embedded, made from the rig's `SimProfiles.json` + `BassShaker.json` effects
  without `Key`/`Off`/`Levels`/`Keys`), `SimDefaults`; `SimProfiles.Load` uses it when neither file exists (Playing
  false: Auto Start plays it); `DefaultProfiles.For` takes a game's tuned effects first (new profile, Reset Effects).
  No Desktop profile (the rig has none). Input Overlay's strip has its own Background (the tab's, per profile).
- **Tested:** DaisysDev with both files moved away: 4 profiles, FH played (FH6 was running), Impacts 95%.

## 3.31.1 Analysis tidy-up, overlapping starts

- **Asked:** blocks on one row; Best Times search; Best Laps title, date, filters on a row, tables ending level; no
  Widgets card; focus back in Name Track after any button; nearest track when starts overlap.
- **Files:** `Analysis/AnalysisView.cs`, `LapTracker.cs`, `README.md`, `lang/en.json`.
- **Done:** `tileSearch` (track name, else only that car's laps); `Foldable(…, fill)` (DockPanel so tables stretch; only
  the lap cards, blocks would rescale); `laps.MaxHeight` = best's + the filters' height, or Laps stops short when both
  are full. `ThenName` queued at Input (a file dialog takes the focus back). `LapTracker`: at the start a place nearer
  than `near[0]` wins; `Find` takes the nearest start that fits, not the closest length; a tagged place nearer than the
  fit isn't merged into it.
- **Decisions:** Date = the day of the best lap, Last driven kept (the user asked for a column, not a swap). Add New with
  the race starting nearer the old track and its length fitting: the race is the old track's and the place goes, as
  "closest" says (told to the user).
- **Tested:** seeded tracks + `race_fin.py`, on screen. Not tried: real game, Save's and Export's focus by click.
  The app rewrites hand edits of `SimProfiles.json`: set the port (8123) on the Profiles tab. Fixtures `%TEMP%\da3304\`
  (`seed.py` / `seed.py restore`, `focus.ps1`, `scroll.ps1`).

## Analysis, earlier (all released; details in `CHANGELOG.md`)

- 3.31.0 Map: `MapView` (`AnalysisSettings.Maps`, `GameMap`, `MapAnchor`s as 0–1 fractions, per-axis least squares,
  `Track.Type`, `Kind`, `Form`). Fixtures `%TEMP%\da331\` (`setup.py [noanchor]`, `combo.ps1`, `set.ps1`, `pick.ps1`).
  PostMessage clicks don't reach WPF: Place on map's click, wheel and drag untested.
- 3.30.3: `LapTracker.furthest` judges `Stopped()`: after a finish Forza sends live frames, the clock as it was and
  Distance near 0 elsewhere (inferred, no packets captured). FH5's lap bump at the line finishes there. Fixture
  `%TEMP%\da3303\fin.py <port> sprint|circuit`.
- 3.30.2: `AnalysisSettings.StartRadius` (500, 50–2000) is StartRadius and PlaceRadius; Vehicle and Laps blocks gone.
  **Lesson:** OneDrive left `.git/objects/pack` and sources cloud-only ("mmap failed"): worked with a bare clone
  (`git --git-dir=%TEMP%\da-git --work-tree=.`); a stale DaisysDev gets a full build.
- 3.28.2 Leaderboard: iRacing only. `TelemetryFrame.Field`, `IRacingSource.Field` 4/s, `Blocks.Around`/`GapText`,
  `BoardRows`. Fixture `%TEMP%\da3282\ir_field.py <s> race|practice|grid [pace step]`; kill old fakes first (two on one
  map give nonsense). Not tested: real iRacing, multi-class, lapped cars, grid mode.
- 3.28.1: `RaceRecord`, `LapStore.Races`, `LapTracker.Result()`. 3.28.0: `Foldable`, Name Track `matched` = `Here()`,
  Discovered Tracks a DataGrid (`TrackRow`, `Cell`, `Fill()` keeps sorts), `ProfileCard.UnitBoxes`; a circuit's last
  lap. Fixtures `%TEMP%\da328\race_fin.py <port> <x> <z> <lap m> <laps> <s> zero|still|lap`, `roam.py <port> <x> <z> <s>`.
- 3.27.1: `SeriesRadius` 150, sprint `Progress`, hotkeys `Analysis.Widget.<id>`. 3.24.7: `Stopped()`, `PendingMinutes`,
  Export (no published FH race coordinates exist). Fixtures `%TEMP%\da327\` (`sprint.py`, `series.py`, `restart.py`).
- 3.24.x: Core `Widget` `Showing` = `settings.On`; `OverlayFrame`; PB = car + track, PBC = class + track; FH6 class 6 =
  R, 7 = X (unconfirmed); car lists `Telemetry/Cars.json` (`%TEMP%\da324db\build.py`), `SimProfile.Version`.
- Design (3.23.1): track = start within the radius + first lap within 6% or 250 m; lap recorded when LapNumber rises.
  FH offsets: 224 drivetrain, 228 cylinders, CarGroup U32 at 232; dash d+0 pos, +16 power W, +20 torque N·m, +24 tyres
  °F, +40 boost, +48 distance, +52 best/last/current/race, +68 U16 lap, +70 pos. Untested on real FH6: units, CarGroup,
  class numbering, rewinds, track Delete, Metric.

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
