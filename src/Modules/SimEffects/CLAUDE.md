# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

- **4.0.0 (the audit):** Overlays draw through `InputOverlay/FrameHook` (only while shown and live, on a new
  `OverlayReader.Version`); Shaker `DeviceVolumes` event-driven (`Edited`, Shared's `EndpointVolume`); Auto Start is
  Core's `RunningPrograms.Watch`; Arduino retries 3 s ×3 then 15 s, at once on a COM port arriving. Not tried: overlays
  with a game, Shaker volumes by hand, an Arduino.
- `Analysis/Tracks.json`: the user's FH5+FH6 tracks, shipped (3.34.0), merged once per hash.

- **4.5.0 Forza Analysis merged into Analysis** (`Analysis/Sessions/`, settings `ForzaAnalysis.json`). `RaceLog` (Core 0.58 `SessionRecorder.Manual`) logs a race; `LapTracker.Claim` keeps it, named
  on `LapRecord`/`RaceRecord.Session`; unclaimed ones deleted 15 s after the next live frames. 4.6.1: a card's body
  must never hide itself (it opened a folded card: wrap the table in a Grid).
- **4.6.3–4.6.4 (released):** `RaceStatsView(open)` (Last races → `SessionsView.Open`); Track Map block
  (`BlockKind.Map`, `TrackMapView`/`TrackLines`); Compare a colour per channel, run 2 dotted (`Hidden`/`Highlight`,
  kept in `CompareHidden`); Current Location 1:2 with Widgets (`Icon` buttons, `tagBox`); `Filters` per card;
  Position without `DoneOf`; Inputs overlay `Trace.Add` clears after a >0.5 s gap. Tested in **DaisysDev3** (a copy
  of `%TEMP%\DaisysDev`, exe renamed, settings `%APPDATA%\DaisysDev3`) with `%TEMP%\da462\seed.py` (fake
  `da462-*.csv` logs in the real ForzaSessions; `clean` after), `uia3.ps1`, `scroll3.ps1 <pct>`, `combo3.ps1 With 2`,
  `shot3.ps1`.
- **4.6.5 (released):** Compare's This its own (`thisSession`, `mine`/`mineItem`, `UseThis` from `ShowSession`,
  `PickThis`); With filtered by `OnTrackOfThis` (track id, else `StartOf` file's first place vs `StartsOf` within
  `StartRadius`); `Part.Best` + `PartTemplate`; overlap tip removed. `seed2.py` (+ `clean`): an imported "Sam",
  another track. Build the module's csproj (the app's build doesn't rebuild modules).
- **4.6.9 (released):** `lineMap` in `BuildCompare` (3*:1* with `corners`); `compareScroll` fed by
  `CompareChart.ViewChanged`. Tested with `%TEMP%\da462\corner.ps1 <row>`, `along.ps1 <metres>`.
- **4.6.10 (released; asked for 4.6.9.1, but `UpdateService.ParseVersion` keeps 3 parts, so 4.6.10):** Shifting card
  (`Sessions/Shifting.cs` `ShiftAnalysis` in `PedalAnalysis.Shifting`, tips via `Tips()`; `ShiftChart.cs`), fourth
  in the Brake Point row. Core 0.59: `SessionTelemetry` Rpm/MaxRpm/Gear/Power (optional params), 4 log columns
  (`SessionData` 18 columns, gear stepped, `HasGear`). Limiter = 98% of Forza's MaxRpm (a guess: check on the rig).
  Tested in DaisysDev3: `seed.py` then `%TEMP%\da4610\gears.py` (adds gears to da462-0.csv); hover/click not tried.
- **4.7.0 (released; asked "4.7"):** Trace merged into `CompareChart` (zones/laps/markers of run 1, `ShowSamples`,
  `Mark`, `ZoneClicked`; `SessionChart` now only `Envelope`/`Clock`; `Channel.Clutch` hidden at first via
  `CompareChannels`). Session 1 = the analysed session (`Pick(Item)` sets both boxes; `PickThis` gone);
  `ShowMoment` switches `thisPart` to the lap holding the moment. `PopOut` (Video placeholder, LineMap) in `Ui.Dialog`
  windows. `RowFold` in AnalysisView (blue chevron, `Fold` per card; SessionsView gets a `row` delegate). ShiftChart in
  rpm (`ShiftAnalysis.MaxRpm` median). `Tip.Warn` (orange): red = crashes ≥ 2 or impacts ≥ 4 (crash ≈ wall, impact ≈
  car: a guess). Next: the video itself (synced to the log). Tested in DaisysDev3 (`seed.py`, `seed2.py`, `gears.py`;
  `%TEMP%\da47\row.ps1 <n> <header>` picks a row of any grid). DaisysDev3 was also open on Support's GPU (the other
  session's?): its settings were backed up and put back.
- **Next:** the user tries 4.7.0, 4.6.10, 4.6.9, 4.6.5, 4.6.4 and 4.6.3 on the rig (`docs/UNTESTED.md`); asked whether Position should drop Lap too
  (only Done went), and whether the Trace's "Both pedals" marks, the zones' "throttle on too" note and the
  split's "Both together" go too (left-foot braking; only the tip went in 4.6.5). Ordinal suffixes ("1st") are English-only.

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
- Harnesses: `%TEMP%\shakertest`, `simtest` (fake boards), `ambitest`, `ambisample`.

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
