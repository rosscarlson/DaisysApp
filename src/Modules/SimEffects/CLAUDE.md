# Session notes: SimEffects

Read only when working in this folder. Current state and lessons; history is in git and `CHANGELOG.md`. Keep under ~8 KB.

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
- **Shaker:** effects per wheel, `ShakerOutput` (BigHz for BST-300EX resonance, 32 Hz), `DeviceVolumes`,
  `SpectrumMeter` (a marker per column always, on its right; the master's red left of the first device, 4.18.7), Highlight / Effect ± keys, Level Wizard keys only while it's open.
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
