# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

- **4.11.0 Fanatec → Wheel** (`Wheel/`): Make = Fanatec or Moza (`IWheelLights`). Tool id, profile scope and
  `Fanatec.json` kept as "Fanatec" so settings carry over. Moza = serial on the base's COM port (VID 346E), boxflat's
  `data/serial.yml` protocol; colours via `NotifyRender.Strip` (`IStripColours`); `SimUi.ColourPicker` shared with
  Notify. No Moza here: built from the protocol only; this PC has a CS V2.5 base (found, rim not read).
- **4.0.0 (the audit):** Overlays via `InputOverlay/FrameHook`; Shaker `DeviceVolumes` event-driven; Auto Start is
  `RunningPrograms.Watch`; Arduino retries 3 s ×3 then 15 s. Not tried with hardware.
- `Analysis/Tracks.json`: the user's FH5+FH6 tracks, shipped (3.34.0), merged once per hash.

- **4.5.0 Forza Analysis merged into Analysis** (`Analysis/Sessions/`, settings `ForzaAnalysis.json`). `RaceLog` (Core 0.58 `SessionRecorder.Manual`) logs a race; `LapTracker.Claim` keeps it, named
  on `LapRecord`/`RaceRecord.Session`; unclaimed ones deleted 15 s after the next live frames. 4.6.1: a card's body
  must never hide itself (it opened a folded card: wrap the table in a Grid).
- **4.6.3–4.7.0 (released; details in git):** `RaceStatsView`, Track Map block (`TrackMapView`/`TrackLines`),
  Compare's colours and `CompareHidden`, `Filters` per card, `lineMap` + `compareScroll`, Shifting (`Shifting.cs`,
  Core 0.59 log columns; limiter = 98% of MaxRpm, a guess), the Trace in `CompareChart` (`ShowSamples`, `Mark`;
  Session 1 = the analysed session, `ShowMoment`), `PopOut`, `RowFold`, `Tip.Warn` (red = crashes ≥ 2 or impacts ≥ 4).
  Versions keep 3 parts (`ParseVersion`). Build the module's csproj (the app's build doesn't rebuild modules).
- **4.10.0 (the user's "fully cross-game"):** Analysis in every game. `Game.AssettoCorsa`/`AcEvo`; `CarNames.Key`
  for all ("iRacing", "ACC", "AC", "ACEvo": Edition never null now; `IsForza` guards the car list). Frames: `TrackKey`,
  `Sessions` (iRacing, AC family: `LapTracker.FeedSessions`, a run = `Stint` clock on track), `Checkered`,
  `LapInvalid`; cars/classes numbered by `CarNames.Number` (FNV), names in `AnalysisSettings.GameCars/GameClasses`.
  A lap counts only from a crossing seen (`fromLine`). FM8 TrackOrdinal at 327 keys its tracks in the Forza path.
  EVO offsets from dSyncro/acevo-shared-memory's `wrapper.hpp` (ctypes, pack 4); ACC graphics checked against
  PyAccSharedMemory. iRacing's line is dead reckoning (VelocityX/Y × YawNorth), reset at the line: handedness a guess.
  Fixtures: `%TEMP%\xg\` (`fake_ac.py acc|ac|evo <laps> race|practice`, `fake_ir.py <laps> race|practice`: shared
  memory, a circle of 1257 m at 50 m/s; `run.sh "<profile>" <fake…>` rebuilds, deploys to **DaisysDevX**
  (`%TEMP%\DaisysDevX`, settings `%APPDATA%\DaisysDevX`, profiles for every game), runs, prints the log and laps).
  Its race logs land in the real `ForzaSessions` (delete the test ones).
  Released 2026-11-07. Tested with those fakes only (ACC race and practice, AC practice, AC EVO race, iRacing race:
  part, out and cut laps dropped, results, names, run ends); no real game.
- **4.10.8 (the user: FH5's Mexico, Sierra Nueva and Hot Wheels share coordinates):** `Track.Map` (null = main),
  `AnalysisSettings.Worlds/World/MapOf`; `LapTracker.OnMap` filters every position match. `Terrain` learns each map's
  ground (25 m squares, lowest Y) and switches (logged "on X, not Y"). Forza sends no map id: no better signal found.
  Fixture: `%TEMP%\xg\fake_fh.py roam <y> <s>|race <y> <laps>`, `phase.sh <map|-> <fake…>` (sets `World`, DevX).
  Released 2026-11-07 (Terrain saves each 60 s and at exit).
- **4.13.4 (the user: "Track 122" though every place is set):** Terrain flipped FH5 to Mexico mid-race on Sierra
  Nueva; only the map set was searched. Now a race start with nothing on the map set tries the other maps
  (`LapTracker.raceMap`, `mapFound`, `MapSeen` → `SetMap(byTrack)` = Picked), Terrain ignored while `MapKnown`, `OnTop`
  in free roam (30 m, 3D). Headless harness: `%TEMP%\trk4134\TrkTest` (copies the real Analysis.json into
  `%APPDATA%\TrkTest`, feeds frames; log in `%LOCALAPPDATA%\TrkTest\logs`). The user's StartRadius is 50.
- **4.13.6:** Horizon runs an event's intro (car ~300 m up over the start, clock running, nothing driven) as a race,
  then 30 s of loading, then the race on the grid: the grid's "entry" was the grid itself, Tunnel Run's place 100 m off
  (radius 50). `LapTracker.Intro` keeps a nothing-driven race's entry for a race starting within 10 s. Harness:
  `%TEMP%	rk4136\TrkTest`.
- **4.14.1 (pauses):** `SessionData.Pauses` (gaps ≥ 1 s in the log's ms, after `Rewinds`), `Pause.Hidden` (true at
  first, not saved); `Run.Ts`/`TimeAt` = time less hidden pauses (the time axis, Delta, the clock); `Playback` skips a
  hidden one (a jump for the video); `CompareChart.PausesChanged` → `Relayout` both charts + `Playback.Relaid`.
  `FrictionCircle` caches its dots in a `DrawingGroup`, `At` = the dot. `strings.py --prune` drops keys used through
  variables (`T(name)`) and reorders: add new keys to `en.json` by hand.
- **4.15.0 (YouTube):** Core `Shared/Network/YouTube` (OAuth loopback + PKCE on a `TcpListener`, resumable upload,
  `YouTube.json`, DPAPI) and `YouTubeAccountView` (Core 0.62); Shared `YouTube/YouTubePlayer` (WebView2 1.0.4314.35,
  the HwndHost control: the composition one needs the Windows SDK projection, which net8.0-windows lacks; its window
  cut to scroll viewports with `SetWindowRgn`; `SetLoaderDllFolderPath` to the module's runtimes; the page served from
  a virtual host, polled with `ExecuteScriptAsync`; YouTube's onReady = ready, an unwanted play paused in the page).
  A headless WPF test of the player is `D:\Temp\yttools\yt` (needs System.Text.Json 9 beside it). `RaceVideoLink`
  gained `YouTube`, `Upload`; options in `RaceVideos` (`AnalysisVideos.json`). `video2` in `SessionsView` follows
  `SampleOfB`. Video file "" = deleted, kept on YouTube.
- **4.15.2:** Compare's playback (CompositionTarget.Rendering, 144+ Hz) redrew every chart each frame: the UI thread
  starved (input 0.5 s late), two videos froze the app (Application Hang). Charts now derive from `LayeredChart`
  (what moves on a child DrawingVisual, `InvalidateTop`); `VideoFrames` swaps two buffers (`Take(Action)`). Harness:
  `%TEMP%\playtest` (an off-screen window, the real race and video; needs `InternalsVisibleTo PlayTest` added to the
  csproj for the build only, and `%APPDATA%\PlayTest\AnalysisVideos.json` copied from DaisysApp's).
- **4.15.1:** the app's own OAuth client (Google Cloud project of the user's Workspace account, 2SV on): the
  GitHub secret `YOUTUBE_CLIENT` (base64 of "id\nsecret", made from this PC's `%APPDATA%\DaisysApp\YouTube.json`)
  → release.yml's env → Core's `AssemblyMetadata` → `YouTube.AppClient`; not in the (public) source. A local build
  without the env var asks for the user's own client. Title bar: `RaceUploads.Badge` (`TitleBadges` "YouTubeUpload").
- **Next:** the user tries 4.10.8 in FH5, then 4.10.0 in each game, then 4.7.0, 4.6.10, 4.6.9, 4.6.5, 4.6.4 and 4.6.3 on the rig (`docs/UNTESTED.md`); asked whether Position should drop Lap too
  (only Done went), and whether the Trace's "Both pedals" marks, the zones' "throttle on too" note and the
  split's "Both together" go too (left-foot braking; only the tip went in 4.6.5). Ordinal suffixes ("1st") are English-only.

## Released before 4.0 (condensed; details in git)

- 3.33.0: the applet owns every overlay window; block widgets read `AnalysisApplet.Current`. SimHub holds UDP 8000
  on the rig. `SimUi.OnOff` listens to Click. The app rewrites hand edits of `SimProfiles.json`. FH offsets: 224
  drivetrain, 228 cylinders, CarGroup U32 at 232; dash d+0 pos … +68 U16 lap, +70 pos.

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
  (`Arduino/DaisysSim/`), own Win32 serial. Wheel (Fanatec): HID LEDs `01 F8 09 08 hi lo` (RS rim). AmbiLight: Govee LAN
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
- Shaker tuning (4.13.5): the ForzaSessions CSVs' `susp` column is `max |SuspVel|` at ~60 Hz, the quickest real data
  to tune against (FH repeated the suspension in ~35% of packets: read as 0). The meters show what an effect asks, not
  what's felt: the log's 30 s "last 30 s:" lines per device are what played.

## Waiting on the user (see `docs/UNTESTED.md`)

Real hardware and games throughout: shakers on the rig, an Arduino with SimHub's sketch, the Fanatec RS rim's LEDs,
the Govee ropes (largest LED count they take), FH6 for Analysis.
