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

- **4.5.0 Forza Analysis merged into Analysis** (`Analysis/Sessions/`, namespace `…Analysis.Sessions`; settings
  still `ForzaAnalysis.json` as `SessionSettings`). `RaceLog` (Core 0.58 `SessionRecorder.Manual`, `Begin`/`End`)
  writes a log a race from Analysis' reading thread; `LapTracker.Claim(raceAt)` keeps it and names it on
  `LapRecord`/`RaceRecord.Session`; unclaimed ones deleted 15 s after the next live frames (or at Dispose; a kill
  leaves them). Overlays no longer log. Tab: Widgets card (`Foldable(…, foldedAtFirst)`, no Leaderboard), Position
  blank without a place, the race cards, `ClickToOpen` on Best Laps/Laps/Races (+ tiles). Tested in DaisysDev with
  `%TEMP%\fatest\race.py 8123` (3-lap circuit kept + linked, a quit race dropped; FH profile port 8123,
  GameSwitching off; backups `%TEMP%\fatest\bak45`); a row picked by UIA (`ui.ps1 -Row "Lap = 2, Time"`) opened
  lap 2. Not tried: mouse clicks/right-click menu, a real FH race. Old 4.4 tests: `fatest\gen.py`.
- **4.6.1** layout: `placeCard` (Location, Name Track inside) 1* | Widgets 3*; `SideBySide(…, share)`; Laps' body
  wrapped in a Grid (RenderLaps set `laps.Visibility`, which opened the folded card: a body must never hide itself);
  Discovered Tracks `SelectionMode.Extended`.

## Released before 4.0 (condensed; details in git)

- **3.33.0 Overlays + Dash:** folder/ids unchanged (`InputOverlay/`, `InputOverlay.json`); `OverlayKinds`,
  `OverlaySettings.Overlays`; the applet owns every overlay window (`ShowOverlays`); block widgets read
  `AnalysisApplet.Current`, places in `Analysis.json`, hotkeys `Analysis.Widget.<id>`; Dash = `DashWindow` from
  `OverlayReader.Frame`; TCS = `Slip` > 0.35 with throttle. Fixtures `%TEMP%\da333\fh.py <port> <s>`, `uia.ps1`,
  `winshot.ps1`. SimHub holds UDP 8000 on the rig (DaisysDev's FH profile on 8123 when testing). `SimUi.OnOff` listens
  to Click (UIA Toggle can't switch it).
- **3.32.0 default profiles:** `Profiles/Defaults.json` (from the rig's settings), used when neither file exists.
- **3.31.1 Analysis tidy-up:** `tileSearch`, `Foldable(…, fill)`, nearest start wins when starts overlap. Fixtures
  `%TEMP%\da3304\` (`seed.py`). The app rewrites hand edits of `SimProfiles.json`: set the port on the Profiles tab.
- **Next (from before):** the user tries 3.33.0, 3.31.x, 3.30.3 (`docs/UNTESTED.md`) and reads real FH5 log lines
  ("back after …", "not a finish (…)"); the user is mapping events on the Map (tracks get baked in later). Offered:
  Core-wide widget keys.

## Analysis, earlier (released; details in git and `CHANGELOG.md`)

- Map (3.31.0, `MapView`, anchors 0–1, least squares); finishes judged by `LapTracker.Stopped()` (3.30.3);
  `StartRadius` (3.30.2); Leaderboard iRacing only (3.28.2); races (3.28.1); widgets on Core's `Widget` (3.24.x).
  Track = a start within the radius + first lap within 6% or 250 m. FH offsets: 224 drivetrain, 228 cylinders,
  CarGroup U32 at 232; dash d+0 pos … +68 U16 lap, +70 pos. Fixtures: `%TEMP%\da331\`, `%TEMP%\da3303\fin.py`,
  `%TEMP%\da3282\ir_field.py`, `%TEMP%\da328\race_fin.py`, `%TEMP%\da327\`. Untested on real FH6: units, CarGroup,
  class numbering, rewinds.

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

## Waiting on the user (see `docs/UNTESTED.md`)

Real hardware and games throughout: shakers on the rig, an Arduino with SimHub's sketch, the Fanatec RS rim's LEDs,
the Govee ropes (largest LED count they take), FH6 for Analysis.
