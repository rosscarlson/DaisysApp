# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

- **4.11.0 Fanatec → Wheel** (`Wheel/`): Make = Fanatec or Moza (`IWheelLights`). Tool id, profile scope and
  `Fanatec.json` kept as "Fanatec" so settings carry over. Moza = serial on the base's COM port (VID 346E), boxflat's
  `data/serial.yml` protocol; colours via `NotifyRender.Strip` (`IStripColours`); `SimUi.ColourPicker` shared with
  Notify. No Moza here: built from the protocol only; this PC has a CS V2.5 base (found, rim not read).
- **4.0.0 (the audit):** Overlays via `InputOverlay/FrameHook`; Shaker `DeviceVolumes` event-driven; Auto Start is
  `RunningPrograms.Watch`; Arduino retries 3 s ×3 then 15 s. Not tried with hardware.
- `Analysis/Tracks.json`: the user's FH4+FH5+FH6 tracks, shipped (3.34.0), merged once per hash.

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
- **4.17.8 (no Session box; scores, the user's Temple Cross Country 11/13 12:09 run):** `SessionsView`'s
  `headButtons` (picker, delete, place, export, manage) + Share on Compare's card, `headCard` is that card (title
  "Session", found by `Titled` and kept as `about`; `ShowTitle` from `ShowCompare`/`ShowSession`, the Item's `Best`
  when the log's lap isn't whole). Compare's rows in a 3-column grid, `scores` a one-row `UniformGrid` spanning both,
  `split` in row 3. `PedalAnalysis`: no `SteeringScore`; `ConsistencyScore` from lap times. Impacts/Crashes tiles keep
  the contact tip's thresholds (crash = hard hit, walls mostly: the user's "impacts = walls" guess reversed, as they
  asked). That race: 5 impacts, 2 crashes on an off-road track (jumps may count as jolts; not changed). Seen in
  DaisysDev (`%TEMP%\share417\pickname.ps1` picks the title-row picker by name).
- **4.17.6 (share status in the title bar; Channels stays open):** `ShareQueue` sets Core's `TitleBadges`
  ("AnalysisShare", blue `#58A6FF` while going, green/red for 10 s via `Done`/`turn`; Waiting for YouTube from
  `Consider`). Compare's Channels popup `StaysOpen = true`, closed by its button, the window's Deactivated/LocationChanged
  or the button going; `Entry` reopens it when the legend is rebuilt (`toggles.Tag` holds the popup). Seen in DaisysDev
  with the fake server and the user's race list copied in (blue Uploading, green, red, cleared); Channels not tried (its
  button is a Border: no UIA invoke, and no mouse on DaisysDev).
- **4.17.4 (Share Session laid out again, the user's list):** `ShareWindow` picks any own session (`pick`, refilled on
  `view.Listed`), Share hidden once shared (no re-send from the UI; `ShareQueue.Share`'s edit path stays), Auto a
  toggle icon (`AccentButton` style while on), no search prefill or count, double-click/Enter compare and close;
  `SessionsView.Export(Item)` saves a whole race. Seen in DaisysDev with the fake server (layout, Auto on/off saved);
  the drop-down with sessions and the double-click not tried (DaisysDev has no races; no mouse on its windows).
- **4.17.0 (Share Session, "Remote Logging"):** `Sessions/SessionShares.cs` (`SessionServer`: the server's `sessions`
  channel, rid-matched asks, gzip export file in 512 KB `put`s, `get` 1 MB; `ShareQueue`: one at a time, auto from
  `AutoSince`, waits while YouTube's going or marked `Upload` and not failed, and 60 s after the log's last write;
  driven by `SessionsView.Refresh`, `RaceUploads.Changed` and a 1-minute timer) and `ShareWindow.cs`. The export file
  is `SessionsView.ExportText`. Not sealed (every user reads them). Server side: the brief in the 4.17.0 session;
  Daisy's Server had no `sessions` module at release. Tested with a fake server (`%TEMP%\sharetest`: `ShareTest.exe`
  runs the protocol; `serve <csv>` keeps a fake up for DaisysDev, copy `%APPDATA%\ShareTest\DaisysServer.json` to
  `%APPDATA%\DaisysDev`): upload, list, download identical, edit, delete, pushes; Compare loaded the download. Not
  tried: Share from the window and auto share (DaisysDev has no races of its own).
- **4.16.1:** `Entry` now puts every channel in `MoreChannels` ("Channels"; the user: all of them in the menu, not
  the row). Video's chip dims (`ShowVideo`, from `ShowOnOff`) when on and the profile is stopped or Analysis is off.
- **4.15.5:** `Tracks.json` rebuilt from this PC's `Analysis.json` (FH4/5/6, 324; Export's fields, `Type` as stored,
  `Entry` when set; sorted by game, name). Nexus and Watersled Speed Circuit (FH5) were dropped on this PC: users who
  had them keep them (Merge never deletes).
- **4.15.4:** the log is resampled to 100 Hz from whatever rate it was logged at: the friction circle's noise
  was the bumps, so it's smoothed when drawn (`FrictionCircle.Smooth`, a centred average; `FrictionSmooth`, 0.25 s),
  not by logging less. `Channel.Rpm`/`Gear` (hidden at first: not in old `CompareChannels`; always hidden on the
  Video's timeline) in `MoreChannels`' popup; `CompareChart.Logged` says which a log has. `HighlightRun` (S1/S2's
  click; the picker on right-click). YouTube's page has a div over the iframe counting clicks (`c` in the poll →
  `YouTubePlayer.Clicked`), so YouTube never sees a click; `RaceVideoView.screen` holds the margin for both sources.
- **4.15.3:** YouTube in Compare stuttered: the poll's `s==1` test made buffering (3) look stopped, so Follow called
  `tube.Play` (a seek) every poll and it buffered again. Now Playing includes buffering, drift uses `Now` (Time moved
  on since heard), a loaded video is primed (muted play, pause, seek 0) before `Ready`, and `Playback.Waiting` holds the
  cursor while a YouTube video buffers. The IFrame API can't download a whole video: YouTube buffers ahead itself.
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

- **4.16.2 Shaker:** device columns in `SpectrumMeter` (`Update(devices, effects)`; device peaks taken in `Tick` before
  the visibility check, so the widget gets them too). `EffectPicker` (prefix `ShakerPick.`): Isolate saves mutes like
  the wizard; `Restore` on wizard open, profile change and `view.Closing()`. Tested in `%TEMP%/DaisysDevS` (its own
  settings, the real devices' list without keys, Enabled off; `%TEMP%/dashk/ui.ps1`, `key.ps1 <n>` sends Ctrl+Alt+Shift+Fn).
- **4.17.1 Shaker:** Isolate became Highlight (several at once; 4.16.2's `Isolate` key read once as `Highlight`), with
  Effect + / - (`EffectKeys.Up/Down`); a highlight changes only its own effect's mute, so Mute in between holds.
- **4.17.5 Shaker, volume markers always:** the user wanted every effect's marker shown all the time (grey when muted),
  so 4.17.3's shortcut-only fade (`Adjusted` events, `Marker`) went; `SpectrumMeter` draws one per column.
- **4.17.2 Shaker, Big Effects:** the user's "intense" feel (FH6, one car at low revs, Engine alone, BST-300EX on the
  Plugable "Buttkicker" device) recorded by WASAPI loopback of that device (`D:\Temp\shakerlisten`: `listen.exe
  <id part> <s> <wav>`, `analyse.py`): a clean sine 30–33.5 Hz at only ~6% level, so it's the rig's resonance.
  `TransducerType.BigHz` (BST-300EX 32, PeakHz 32 too), `ShakerDevice.BigHz` (null: the type's; 0: off); Curbs,
  Impacts, Crashes at it in `ShakerOutput` (hits not swept; curb gate ≤ Big/4). Wait for the game to say "driving"
  (the log) before cueing a recording: the first one caught a menu.

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
