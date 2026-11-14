# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

## Latest: 4.17.17, Profiles as a block per game (released 2026-11-14)

- **Asked:** replace the Profile and Auto Start Modules cards with a block per supported game, 3 or 4 across; a
  checkbox at the top left switches the game on; its options in a part that folds; its modules picked compactly;
  each game's own defaults (port, programs); publish.
- **Done** (`Profiles/ProfileCard.cs` rewritten, `ProfilesApplet.cs` `ProfilesView`, `SimProfiles.cs`,
  `GameSwitcher.cs`, `GameProfile.cs`, `Telemetry/Telemetry.cs`, `lang/en.json`, both READMEs): `ProfileCard.Build(game,
  profile)` per `Games.All` + Desktop (null game). A game's block = its first profile (the active one if it's that
  game's). `SimProfile.Enabled` (default true; off: not in `Picker`, skipped by `GameSwitcher`).
  `SimProfiles.SetEnabled(game, on)`: makes one with `Add(p, use: false)`; unticking the active one = `Use(first on,
  play: false)`. Options and Modules fold (`Fold`, state in static `open`); Modules are `Ui.Chip`s. Units moved above
  the grid. Columns: 3, or 4 for 4/7/8 blocks, fewer under 300 px each. Cards `VerticalAlignment.Top` (an unfolded
  block mustn't stretch its row).
- **Decisions:** one profile per game, so Edit/New/Delete/rename went (old extra profiles stay in the drop-down);
  Desktop kept as a block so its tools (Arduino, Notify) don't vanish; defaults in one place, `Games.Port`/`Programs`
  (FH 8001 + fm4.exe/azimuth.exe, as `Defaults.json`; existing profiles untouched, **Defaults** puts them back).
- **Tested** in DaisysDev with a copy of the real `SimProfiles.json`: grid, folds, ticking AC, unticking FH in use
  and ticking it again. Not tried: Use, a chip on the active profile, Defaults, narrow window, Auto Start (UNTESTED).
- UIA shot script: `%TEMP%\prof4117\shot.ps1 -path SimEffects,Profiles,<names…>` (selects/invokes/toggles by Name,
  PrintWindow PNG). The Write/Edit tools turn `"\uE70D"` into the literal glyph: harmless.

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
- Shaker tuning: the ForzaSessions CSVs' `susp` column is the quickest real data; the meters show what's asked,
  the log's "last 30 s" lines what played.

## Waiting on the user (`docs/UNTESTED.md`)

4.17.17's Profiles blocks with a game; Analysis on real games (4.10.x in each, FH5 maps), sharing from the window and
auto share; real hardware throughout (shakers, Arduino with SimHub's sketch, Fanatec RS LEDs, Moza, Govee ropes).
Open questions to the user: whether Position drops Lap too; the Trace's "Both pedals" marks. Ordinal suffixes
("1st") are English-only.
