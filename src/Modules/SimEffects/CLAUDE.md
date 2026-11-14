# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

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
  YouTube OAuth client is the `YOUTUBE_CLIENT` GitHub secret → Core metadata; not in source. `Tracks.json` ships the
  user's FH4/5/6 tracks (merged once per hash, never deletes).
- **Shaker:** effects per wheel, `ShakerOutput` (BigHz for BST-300EX resonance, 32 Hz), `DeviceVolumes`,
  `SpectrumMeter` (a marker per effect always), Highlight / Effect ± keys, Level Wizard keys only while it's open.
- Arduino: SimHub's sketch (ARQ) or DaisysSim; Wheel: Fanatec HID `01 F8 09 08 hi lo`, Moza serial (untested, no
  Moza here). AmbiLight: Govee LAN `razer` stream, DXGI duplication. Overlays: 8 boxes, Dash layouts.

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
