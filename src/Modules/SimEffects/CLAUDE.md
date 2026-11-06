# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

- **4.0.0 (the audit):** Overlays draw through `InputOverlay/FrameHook` (Rendering hooked only while shown and
  `OverlayReader.Live`, a draw only on a new `OverlayReader.Version`). The Shaker's `DeviceVolumes` polls nothing:
  `ShakerDevice.Volume/Muted` raise `Edited` → one sync on the UI thread, Windows' side through Shared's
  `EndpointVolume`, plugged-in devices by `DeviceService.DevicesChanged`. Auto Start (`GameSwitcher`) is Core's
  `RunningPrograms.Watch` (3 s, `Recheck` on `SimProfiles.Changed`). Arduino retries 3 s ×3 then 15 s, at once on a
  COM port arriving (Core's `DeviceChanges`). `SimProfiles.Save` is atomic. Brushes are Core's `Ui.Brush` (the
  `Frozen` copies gone). Tested: a fake ForzaHorizon5.exe played and stopped the FH profile in DaisysDev. Not tried:
  the overlays drawing with a game, the Shaker volumes by hand, an Arduino (`docs/UNTESTED.md`).
- 3.34.0 ships the user's FH5+FH6 tracks (`Analysis/Tracks.json`, merged once per hash); 3.35.0 widget trash cans
  pass `Delete(id)`.

- **4.5.0 Forza Analysis merged into Analysis** (`Analysis/Sessions/`; settings still `ForzaAnalysis.json` as
  `SessionSettings`). `RaceLog` (Core 0.58 `SessionRecorder.Manual`) logs a race; `LapTracker.Claim` keeps it, named
  on `LapRecord`/`RaceRecord.Session`; unclaimed ones deleted 15 s after the next live frames. 4.6.1: a card's body
  must never hide itself (it opened a folded card: wrap the table in a Grid).
- **4.6.3 (released; the user's 13-point Analysis request):** Racing Statistics card (`RaceStatsView`, under the top
  row; Races lost its stats line); Track Map block/overlay (`BlockKind.Map`, `TrackMapView` + `TrackLines`: the
  track's best lap X/Z read from its log, `Reading.TrackId`) and Sessions' `LineMap` card beside Compare (2:1, hover
  dot via `CompareChart.Hovered`, corner zoom); Compare a colour per channel (speed blue, throttle green, brake red,
  steering white, Delta amber), run 2 dotted (`CompareChart.LinePen`), 8 lines hidden/highlighted (`Hidden`,
  `Highlight`; kept in `CompareHidden`, old `CompareChannels` lacks = hidden both); Current Location one row, greyed
  "Unknown" without a place; Best Laps / All Laps / Races a third each, `Filters` per card (also Best Times; tracks and
  cars A–Z, classes descending); "Session Analysis"; folder icon; Brake Point / Brake Release / Throttle cards
  (`AlignRelease` gone); Smoothness/Tips under Compare, tips boxed red/white/green, steering tip removed; Map above
  Discovered Tracks; Brake/Throttle Analysis: double-click opens `MomentWindow` (with Close). Files: `Analysis/`
  AnalysisView, Blocks, BlockView, AnalysisApplet, RaceStatsView, TrackMapView; `Sessions/` SessionsView,
  CompareChart, LineMap, MomentWindow, PedalAnalysis, SessionSettings; READMEs (Analysis, InputOverlay).
  Tested in **DaisysDev3** (a copy of `%TEMP%\DaisysDev`, exe renamed; settings `%APPDATA%\DaisysDev3`) with
  `%TEMP%\da462\seed.py` (2 fake 3-lap logs `da462-*.csv` in the real ForzaSessions + 12 races; `seed.py clean`
  removes the logs, done), `uia3.ps1`, `scroll3.ps1 <pct>`, `combo3.ps1 With 2`, `shot3.ps1`. Not tried: the live
  Track Map block (no game sending), highlight clicks with the mouse.
- **4.6.4 (released):** Current Location 1:2 with Widgets, two rows (`Icon` buttons, track name only in `tagBox`,
  `carName` always, greyed); `RaceStatsView(open)` (Last races click → `SessionsView.Open`; Finishes in row 0 beside
  the tiles, bars by star rows); Position's `DoneOf` row removed (`Reading.Progress` still computed); Discovered Tracks
  `OrderByDescending(Found)`; Inputs overlay `Trace.Add` clears the history after a >0.5 s gap.
- **Next:** the user tries 4.6.4 and 4.6.3 on the rig (`docs/UNTESTED.md`); Ordinal suffixes ("1st") are English-only.

## Released before 4.0 (condensed; details in git)

- **3.33.0 Overlays + Dash:** `OverlayKinds`; the applet owns every overlay window (`ShowOverlays`); block widgets
  read `AnalysisApplet.Current`, places in `Analysis.json`, hotkeys `Analysis.Widget.<id>`. SimHub holds UDP 8000 on
  the rig (DaisysDev's FH profile on 8123 when testing). `SimUi.OnOff` listens to Click (UIA Toggle can't switch it).
  3.32.0: `Profiles/Defaults.json` when neither file exists. The app rewrites hand edits of `SimProfiles.json`.
- Analysis: Map (3.31.0, `MapView`, least squares); finishes by `LapTracker.Stopped()` (3.30.3); Track = a start
  within `StartRadius` + first lap within 6% or 250 m. FH offsets: 224 drivetrain, 228 cylinders, CarGroup U32 at
  232; dash d+0 pos … +68 U16 lap, +70 pos. Fixtures: `%TEMP%\da3304\seed.py`, `da328\race_fin.py`,
  `da3282\ir_field.py`, `da327\`. Untested on real FH6: units, CarGroup, class numbering, rewinds. The user is
  mapping events on the Map (tracks baked in later).

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
  `card: false` (session logs until 4.5.0).

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
- DaisysDev may be in use by the other session (it was testing Devices): check before killing it; test in DaisysDev3.
- `strings.py` also reads `*-GAMER11.cs`: drop the keys only those files use; restore other modules' en.json it touched.

## Waiting on the user (see `docs/UNTESTED.md`)

Real hardware and games throughout: shakers on the rig, an Arduino with SimHub's sketch, the Fanatec RS rim's LEDs,
the Govee ropes (largest LED count they take), FH6 for Analysis.
