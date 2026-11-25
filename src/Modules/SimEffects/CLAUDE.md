# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## 4.21.9: nothing deleted, for now (2026-11-25)

- The user checks FH6 tracks one race at a time: `RaceLog.KeepAll` (true) leaves a race not kept in ForzaSessions and
  its video in the folder (link removed, so not uploaded); fix by hand (records, `RaceVideos` link) and share from
  Uploads. Set it false when that's done. 17:35 (4.21.7) "Festival Sprint" kept as Nangan Cross Country: started 41 m
  from Irokawa Circuit's start, 6,013 m vs its 5,534 → Nangan (Length 0); 4.21.8's replay keeps it as Irokawa.

## 4.21.8: Horizon's Fits (2026-11-25)

- Festival Sprint 17:29 (4.21.5): 5,734 m vs its 5,399 (an online race's, Tracks.json): over `LengthSlack` → no track,
  dropped. `Fits` is now per instance: in `KnownTracks.Games` any `Whole` lap fits (start decides). Other FH file
  lengths 5,000–5,900: Panoramica Sprint (FH5), Irokawa Circuit, Oyashirazu Trail (FH6).

## 4.21.7: entries far from the start (2026-11-25)

- Edo Circuit 17:21 kept as The Gauntlet: the car sat at The Gauntlet's marker (Length 0: `EntryOf` = its marker),
  Edo picked from the map 12 km away, `Entered` matched by the entry alone. Fix: `Entered` only tracks within
  `EntryReach` (2 km) of the race's start; `EntryOf` ignores an Entry further than that from its track; `Add` keeps
  only a near one. `KnownTracks.Merge`: a learnt start (file Length 0) on another file track's known start is learnt
  again. Tracks.json: Edo's lap (5981.6), far Entries dropped. Rig's jsonl: the race moved to Edo (`edo-bak`).

## 4.21.6: races left with whole laps shared (2026-11-25)

- The user: a race not finished is still uploaded (the server shows only whole laps in leaderboards/analysis, the whole
  session on the driver's page); "3 laps, quit after 2" is shared. Done: `Unfinished` deletes (DeleteIncomplete) only
  with 0 laps; `Share`/`Wanted` take Incomplete (`finished` false; `lapTimes` are only ended laps); `Withdraw` gone.
  Open, asked: a race left with no whole lap (Wanted needs Laps > 0; solo runs with no lap still dropped as "no lap or
  race kept"), and whether the server hides `finished: false` from leaderboards per lap (the Server agent's).

## 4.21.5: the results screen's gap (2026-11-25)

- 4.21.3's "clock run on = app missed frames" lost Bandai Asuma 16:47 (4.21.4): finish 136.96 s, 35 s of !Live frames
  (results), then 2.5 s Live with the clock at 165.8, then free roam: no gap at the end → "not a finish". Fix: `notLiveAt`
  (Feed's !Live frames); a gap with the clock run on ends the race if the game sent !Live frames in it, or after a
  whole FH sprint (`Whole(furthestDriven)`), wherever the car is. lapreplay scenarios `touge`, `touge2`.

## 4.21.4: Horizon's start radius floor (2026-11-25)

- Rig's StartRadius is 50: places (Tracks.json, Length 0) are event markers, races start 120–615 m off, so six races
  (Bandai Asuma twice, likely Shikisai Sprint, Ruriko-ji XC, Cedar Run) fitted nothing: log and video deleted. Fix: in
  `KnownTracks.Games` StartRadius at least the default (500), PlaceRadius at least 750; `Entered` stays on the setting
  (a floor matched Bandai's race to Takashiro: the event was picked from the map near Takashiro's entry). Open: an
  entered track whose known start is km from the race's (Shimanoyama Charge 15:19 started 9.8 km from its start).

## 4.21.3: restarts and short laps (2026-11-25)

- Rig: a Rivals restart (back at the start, clock as it was) was "back somewhere else" → finish → half-lap first race
  set the place's Length; the whole run after fitted nothing (dropped, log and video deleted). A 5.9 s UI stall with the
  clock running on also ended a race. Fix: `LapTracker.HorizonWhole` (5,000; FH Distance is progress, ~5,950 a lap)
  for data-stop and clock-stop finishes; "back somewhere else" needs the clock as it was; `KnownTracks.Merge` drops
  learnt FH lengths under it. Tracks.json: the user said the place named Stadium XC was Takashiro XC and Soni
  Highlands' was Stadium XC; Soni got Takashiro's old place (a guess). Short lengths zeroed, rig's FH6 lengths added.
- Tested: `%LOCALAPPDATA%\Temp\lapreplay` (replays a session csv through LapTracker; settings in `%APPDATA%\LapReplay`).

## 4.21.2: Forza's race times exact (2026-11-25)

- The user: Wind Farm (Nacho's, 2:06.945 kept) showed 2:37.000 in the lap box, and "2:07.144 in the video". 2:07.144
  was the **Rival** (CMDRExigeous) on the results screen; Nacho's own was 2:07.056. Checked on 3 videos (yt-dlp +
  ffmpeg in `%LOCALAPPDATA%\Temp\ytv`, clips in `ytclips`): game time = last frame sent + one frame (the 5-frame
  average step, rounded to ms) exactly, at 90 Hz and 60 Hz; middle laps too (`LastLap` = last row + a frame).
- Causes: tracker fed by the 10/s tick (`furthest` up to 0.1 s old) → `LapTracker.See` every frame from the reading
  thread (under `gate`) and `AFrameOn`; `FindLaps` ran a lap over the post-finish gap and loading frames (lap 1, or the
  same lap with no time) → trimmed to its last timed row, raw `ends` (time, step) per lap. One-time `ShortLaps`/`Mend`
  (`LastLapsMended`): last laps 0.0005–0.25 s short of their log. `Blocks.LapTime`/`LapClock` truncated floats.
- Data: the rig's sign-ins had all expired (remote copy failed); Nacho's 139 shared sessions fetched anonymously from
  ForzaLeaderBoard (`%LOCALAPPDATA%\Temp\da-flb`: `flbfetch list|get`, `cmp.py`). Tested in DaisysDev: the mend
  (128.637 → 128.705) and a replay of the race (`da-fakefheplay.py`, port 8123, Playing on) → 127.056.
- Not done: sessions already on the server keep their old `lapTimes`/`best` (no op to change them; the user has the
  server's session on it). Old records far from their logs (0.4.x, some 4.18) left alone.

## 4.20.2: only whole races shared (2026-11-23)

- Asked: verify a session is complete before upload, or delete incomplete uploads; client-side preferred. Done: the
  tracker's `Undecided` (race not finished or left) keeps it out of `ShareQueue.Wanted` and `Share`; `Left` (quit,
  incomplete) → `ShareQueue.Withdraw` unshares; `Close()` at Dispose judges a race in progress; `lapsRaised`: a race
  that set its track's `Laps` can't be "a finish missed". Meta `finished` sent (server can check).
- Not covered: a Forza race led from P1 the whole way then quit (`racers` stays 1, looks like Rivals); a first race
  whose finish wasn't detected now counts as left. Harness: `%LOCALAPPDATA%\Temp\da-verify-tools\TrkVerify`.

## 4.20.1: the Radar's cars to the side (2026-11-22)

- The user: a car stays behind, pops to the side as it passes, then back in front; better position data? iRacing has no
  place across for other cars (327 variables looked at live on GAMER11; Lat/Lon only in .ibt). Lap pct is exact at 60 Hz;
  `CarLeftRight` fires at about 3.9 m (Ray FF1600). `CarIdxSteer` is sent for every car: `CarsAcross`.
- Measured on his AI practice (Navarra, 18 alongside moments; read-only capture and scripts in `%LOCALAPPDATA%\Temp\radarlive`,
  `rep.py` replays it through the class): 0.1 s before the spotter, right side by > 0.7 m 12, wrong 0, neither 6; left to
  move 1.36 m of 3. Tuned on the same moments, so optimistic. His own steering carries most of it (alone 11/2); the
  others' alone 8/4, and on the AI race (not tuned on, he wasn't driving) no better than chance at the moment of overlap,
  so it counts half (`Theirs`). Steering past 0.4 rad: useless. Not seen on screen in the game; other cars' steering online untested.
- The CarIdx arrays are 72 now (was 64 here).

## 4.20.0: Recordings (2026-11-22)

- Asked: Record (between Play and its shortcut button, bindable) and a Recordings tab playing one as if the game ran, with its
  video. Decided: a race with Record each race on ends a recording (kept); Record needs Video → Recording; Stop playback
  restores the profile before; frames, not packets, recorded (every game, `FrameFile.cs`'s reflection); Loop, Sync, pop-out added.
- A new reader of `SimData` must check `TelemetryFrame.Playback` before keeping or learning anything.
- Tested in a copy (DaisysRec: only SimEffects and Video; fake Forza `%LOCALAPPDATA%\Temp\rectest\fake_fh.py`, `ui.ps1`
  UIA): record, play from Desktop and back, loop, pause, seek, pop-out, Shaker meters, a race ending it, killed while
  recording (its 20 s play), delete. Store Python writes to a virtual AppData: copy settings in with bash. Not run: a
  real game, the shortcut, shakers' feel.

## 4.18.14: Overlays → Radar, iRacing (2026-11-22)

- Asked: FH6-style radar fading in as drivers near. Forza sends no other cars; ACC/EVO give every car's world place
  (`carCoordinates`) but not done: the heading's sign wasn't checkable here (offer it). iRacing: `IRacingSource.Near`
  (lap pct × track length; `CarLeftRight` sides to the nearest overlapping cars, lane kept to 30 m) → `TelemetryFrame.Nearby`.
- Tested in DaisysDev with `%LOCALAPPDATA%\Temp\radartest\fake_ir.py` (a fake irsdk memory map; `shot`/`crop.ps1`):
  pass on the left, alongside on the right, drop back, fade out; the tab's box. Not run in iRacing.

## 4.18.9: the app's tracks only (2026-11-19)

- The user: clients use the current tracks, no track editing (asked: lock app tracks vs. no editing at all → none).
  `Tracks.json` unchanged: the rig's `Analysis.json` (copied 2026-11-19 by a remote session on GAMER11, account
  RossCarlson: Ross's sign-in there had expired) matched it exactly. `KnownTracks.Merge` every start: file games'
  (`Games`) tracks matched by Found, name+map, start <1 m, then <50 m with the lap; set to the file's; the rest taken
  off (laps orphaned, "Unknown track"). `LapTracker.Add` returns null in those games (laps not kept); `NameHere`,
  pending places, `AdoptPlaces`, `TagHere`/`Rename`, Delete, the rows' Map picker gone. Keyed games and FM7 still add.
- Tested in DaisysDev: a renamed, a moved (map too), a deleted and a friend's own FH5 track → file's 333 exactly,
  iRacing's kept; second start no change; Known Tracks plain text, no Delete; Current Location's box read-only.
  To add tracks now: map them on the rig with an older build or edit `Tracks.json` (Export's format).

## 4.18.6: Tracks.json from GAMER11 (2026-11-18)

- Regenerated from the rig's `Analysis.json` (FH4/5/6, named, no Key; `Entry` kept). The merge matches the file's own
  tracks by `Found`: a track deleted and made again (La Cantera) keeps the old file's `Found` so friends' copies move.
  Not decided: server-side track matching (the log lacks the entry point and map; told the user, 2026-11-18).

## 4.18.0: Share Session on ForzaLeaderBoard (2026-11-17)

- **4.18.3, the user (a remote client got *Can't reach*; ping and the ports were fine):** www's 38900 is
  Daisy's Server on PLEX (key mismatch); ForzaLeaderBoard is on 38901 (192.168.1.5 now listens only there).
  Built-in port 38901; Account's **Port** box beside Server. Tested in DaisysDev: www:38901 connected anonymously.
- **4.18.2, the user (on the LAN, www's public address fails there):** Account's **Server** box back, filled with `Link.Host`; `SetUpAsync` is given the built-in key, so any address is pinned to
  ForzaLeaderBoard's key; no user/password allowed only when the address changed (anonymous). Tested in DaisysDev:
  www failed (*Can't reach*), 192.168.1.5 anonymous connected, `ForzaLeaderBoard.json` Host 192.168.1.5 + the key.
- **4.18.1, the user:** users deal only with a user and password, never a server key: Account's Server section
  (address, port, key) removed; key problems shown as *Can't reach ForzaLeaderBoard* (`SessionServer.Friendly`). Testing
  another server: write `Host`/`Port`/`ServerKey` into `ForzaLeaderBoard.json` by hand. Settings → General's Daisy's
  Server card still shows its Server key row (4.17.27): not asked about; offered.

- The user's brief (from the ForzaLeaderBoard server's agent): Share Session on its own server, key pinned, account
  optional, `daisysapp://session/<id>?server=&key=` links. Core 0.66: `ServerLink`'s code is now `ServerConnection`
  (an instance per server; `ServerLink.Connection` is Daisy's Server's; one device id, `ServerLink.Device()`), and
  `OpenFiles.Links` (the shell hands `daisysapp:` args over FileHandoff's pipe). Module: `SessionServer.Leaderboard`,
  `SessionServer.Shared` (an instance now), `FetchAsync`, `AccountWindow`, `SessionLinks`.
- **Host is `www.forzaleaderboard.com`**: on 2026-11-17 the bare domain had no A record (pi.hole and 1.1.1.1), and the
  public IP's 38900 reached Daisy's Server on PLEX (key `DXjD02…`), not ForzaLeaderBoard (192.168.1.5); the pinned key
  refuses it. Links name `forzaleaderboard.com`: a link is "ours" by its key, whatever the host. Told the user.
- Tested: DaisysDev with `ForzaLeaderBoard.json` pointing at 192.168.1.5 (same key): links at start and handed to the
  running copy (downloaded, Session 1, Session 2 emptied), a missing id (*isn't on the server any more*), a bad link,
  the manager's list (52 sessions), Account rendered; harness (`D:\Temp\flb-harness`): wrong password, wrong key,
  anonymous, built-in key refused at www. Not tested: signing in with a real account; share/edit/delete on the public
  server (unchanged code); the website's button in a browser; another server's question.

## 4.17.23: the readout on the mouse's left (not run: the user gaming)

- `CompareChart.Readout`: left of the mouse unless it would go past 0; the user: never on the right (it covers what's coming).

## 4.17.22: Tips half, class in the drop-downs, no app names (not run: the user gaming)

- Tips: `SplitTips` from `ShowLegend(a, b)`: column 4 2.05* comparing (the 24 px of gaps are left), 1.5* alone.
- "[A] " in `Item.Title` (the drop-downs, labels, title row); only when `Blocks.ClassName` names one.
- The user: no "Daisy's App/Server" anywhere in the module; generic ("Share to Server"). YouTube credit and tag gone.

## 4.17.21: Shared Session Manager (not run: the user gaming)

- **Asked:** Driver by the server's display name (store only the user name, look it up at runtime, by id ideally);
  a double-click into Session 1, Session 2 emptied (its list the matches, or says none); a search box at Clear's
  right in every column filter with more than 5 choices, in Core.
- **Done:** `HeaderFilter` moved to Core (`Shared/HeaderFilter.cs`, public; `NoWidth` keeps the box from widening
  it). `ServerSession.DisplayName`/`Name`; a share sends the server user name as `# driver`, no meta `driver`; the
  downloaded copy's `# driver` rewritten to the display name. `CompareDownloaded(key, 1, alone)` → `emptyOther`,
  consumed in `FillOtherSessions` once that's Session 1. **Server 0.2.4** (DaisysServer repo, committed locally,
  not pushed or released): `ServerUser.DisplayName`, `SessionStore` adds `displayName` by `userId` when sending.

## Latest released: 4.17.20, Profiles: Activate (2026-11-14)

- **Asked:** no checkbox on the blocks: Use becomes Activate / Active (the blue edge); a game's processes make its
  profile active too; Processes scroll after 3; clicking in the box shows the running programs (typing narrows),
  in place of Add a running program…; Defaults gone; publish.
- **Done:** `SimProfile.Enabled` and `SetEnabled` removed (old files' `Enabled` ignored); `SimProfiles.Load` makes
  any game's or Desktop's profile that's missing (`New(game)`, Auto Start Yes with programs). `ProfileCard.Build(game,
  p)`: p never null; Active is an accent Button (plays if stopped). Core `Ui.ProgramList(..., rows, runningInBox)`
  (opt-in; `RunningDropDown`: a Popup ListBox, not focusable, picked on mouse-up; ↓/↑ Enter, Esc).
- **Tested** in DaisysDev via UIA (`%TEMP%\p4120\drop.ps1`: focus the box, list the "Running programs" popup, type,
  Add). PrintWindow doesn't show the popup.

## 4.17.17, Profiles as a block per game

- `ProfileCard.Build(game, profile)` per `Games.All` + Desktop (null game); a game's block = its first profile (the
  active one if it's that game's). Options and Modules fold (`Fold`, state in static `open`); Modules are `Ui.Chip`s.
  Columns: 3, or 4 for 4/7/8 blocks, fewer under 300 px each. Cards `VerticalAlignment.Top` (an unfolded block
  mustn't stretch its row). One profile per game (Edit/New/Delete/rename went; old extra profiles stay in the
  drop-down); Desktop kept so its tools (Arduino, Notify) don't vanish; defaults in `Games.Port`/`Programs`.
- UIA shot script: `%TEMP%\prof4117\shot.ps1 -path SimEffects,Profiles,<names…>` (selects/invokes/toggles by Name,
  PrintWindow PNG). The Write/Edit tools turn `"\uE70D"` into the literal glyph: harmless.

## 4.17.18: one title bar bubble, Short Name, scores one tile, Delta always listed (not run: the user gaming)

- `Profiles/SimBadges` gathers the Shaker's, Overlays', Wheel's (new), AmbiLight's and Arduino's parts (`SimBadges.Set(toolId,
  text, tip)` instead of `TitleBadges.Set`; Analysis' upload/share bubbles stay apart). `SimProfile.ShortName`
  (`DefaultShort` at Load/Add) in a block's Options. Scores: `ScoreGroup` one `Tile` of all parts. Delta: `Entry`
  lists it whenever `both`, disabled while `!CompareChart.HasDelta` (it showed only with a Session 2 by distance).
- **Lesson:** a release worktree in `D:\Temp` may be another session's (4.17.17's was `da-prof`): recreating it reset
  its index, and its next commit (5218ac3) put 4.17.17 back to 4.17.16 (restored in 022f9d6). Use a new name each time.

## How the module fits together

- `AppletGroup` tab: Profiles, Shaker (⚠ `BassShaker/`), Arduino, Notify (needs Arduino), Wheel (⚠ id "Fanatec":
  Fanatec or Moza, `IWheelLights`), AmbiLight, Overlays (⚠ `InputOverlay/`), Analysis. `HeaderRight` = Profile
  drop-down + Play/Stop + shortcut.
- **Profiles:** `SimProfiles` (static, `SimProfiles.json`); each tool `Register`s a `ToolScope` **before building its
  view**; its settings' Save calls `SimProfiles.Saved`. `On => Enabled && SimProfiles.Playing`: runtime uses `On`, UI
  `Enabled`. Hardware (devices, boards, lights, widget places, redlines) is global. The app rewrites hand edits of
  `SimProfiles.json`.
- **Telemetry:** `SimData.Open()` over `TelemetryHub` (one reader per game + port). Every game since 4.10.0:
  `TrackKey`, `Sessions`, `Checkered`, `LapInvalid`; cars/classes by `CarNames.Number` (FNV). EVO offsets from
  dSyncro/acevo-shared-memory; iRacing's line is dead reckoning (handedness a guess). SimHub holds UDP 8000 on the rig.
- **Widgets:** always Core's `Widget`; a closed one lets its body go.
- **Analysis:** `LapTracker` (maps per world: `Track.Map`, `Terrain`, `raceMap`/`MapSeen`, `Intro` for Horizon's
  event intros), `Sessions/` (race logs via `RaceLog`, `SessionsView`, Compare: `CompareChart` on `LayeredChart`,
  `Playback`, pauses hidden, YouTube via Shared `YouTubePlayer` (WebView2 HwndHost), `RaceUploads`), sharing
  (`SessionShares.cs`: `SessionServer`, `ShareQueue`, `ShareWindow` with ticked sessions, "n of m" badge). The app's
  YouTube OAuth client is the `YOUTUBE_CLIENT` GitHub secret → Core metadata; not in source. (4.18.5: the secret has a UTF-8 BOM; Core strips it, so 4.15.1–4.18.4 had no app client. Check: the installed exe's `YouTubeClient` metadata decodes.) `Tracks.json` ships the
  user's FH4/5/6 tracks (merged once per hash, never deletes).
- **YouTube limits (4.20.3):** the friend's channel (unverified) took 9 uploads, then `uploadLimitExceeded` on every
  one; it looked signed in with nothing going. Core's `YouTubeLimitException` (that and `quotaExceeded`) holds
  `RaceUploads` an hour (amber bubble, a notification a day). A share whose server copy is gone is forgotten.
- **Shaker:** effects per wheel, `ShakerOutput` (BigHz for BST-300EX resonance, 32 Hz), `DeviceVolumes`,
  `SpectrumMeter` (a marker per column always, on its right; the master's red left of the first device, 4.18.7), Highlight / Effect ± keys, Level Wizard keys only while it's open.
- Arduino: SimHub's sketch (ARQ) or DaisysSim; Wheel: Fanatec HID `01 F8 09 08 hi lo`, Moza serial (untested, no
  Moza here). AmbiLight: Govee LAN `razer` stream, DXGI duplication. Overlays: 9 boxes, Dash layouts.

## Test fixtures and tools

- DaisysDev (`%TEMP%\DaisysDev`, settings `%APPDATA%\DaisysDev`): copy the build's
  `modules\SimEffects\DaisysApp.SimEffects.dll` (and `lang\en.json`; Core changes: `DaisysApp.Core.dll`) in;
  `taskkill /F /IM DaisysDev.exe` to restart. Back up settings you change. The other session may use it too.
  Analysis test data: copy `%APPDATA%\DaisysApp\Analysis*.jsonl` into `%APPDATA%\DaisysDev`.
- Fakes: `%TEMP%\xg\` (`fake_ac.py`, `fake_ir.py`, `fake_fh.py`, `run.sh` → DaisysDevX), `%TEMP%\da317\fh_send.py`;
  harnesses `%TEMP%\trk4136\TrkTest`, `%TEMP%\playtest`, `%TEMP%\sharetest` (fake share server), `D:\Temp\shakerlisten`
  (WASAPI loopback). UIA helpers: `%TEMP%\da317\uia.ps1`, `%TEMP%\da324\*.ps1`, `%TEMP%\share419\`.
  `PING.EXE` as a profile program stands in for a game.
- Build the module's csproj (the app's build doesn't rebuild modules). Race logs from DaisysDev land in the real
  `ForzaSessions`: delete test ones.

## Lessons

- A UIA Toggle raises no `Click`: use `Checked`/`Unchecked`.
- `strings.py --prune` drops keys used through variables and rewrites line endings: add new keys to `en.json` by
  hand (HEAD's blob plus yours). It also reads `*-GAMER11.cs`.
- Build shared docs (CHANGELOG, README, UNTESTED) from the very commit the new one goes on (4.17.11 undid 4.17.10's).
- A field-held element put into a box that's rebuilt must leave its old parent first (4.17.12).
- A card's body must never hide itself; don't name a Window method `Show()`; UIA "Delete" can hit the wrong one.
- Edit files with Python, not `sed -i` over globs (it once mangled `AccSource.cs`'s BEL char).
- A HID write to the Fanatec base must never block (4.17.19): with ForzaWheel's FFB on it, the base stopped taking
  output reports and a plain `WriteFile` waited for ever on the Lights thread. Overlapped, 250 ms, `CancelIoEx`.
- Shaker tuning: the ForzaSessions CSVs' `susp` column is the quickest real data; the meters show what's asked,
  the log's "last 30 s" lines what played.

## Waiting on the user (`docs/UNTESTED.md`)

4.17.20's Profiles (the drop-down picked by mouse / ↓ Enter, its look, a game activating its profile); Analysis on real games (4.10.x in each, FH5 maps), sharing from the window and
auto share; real hardware throughout (shakers, Arduino with SimHub's sketch, Fanatec RS LEDs, Moza, Govee ropes).
Open questions to the user: whether Position drops Lap too; the Trace's "Both pedals" marks. Ordinal suffixes
("1st") are English-only.
