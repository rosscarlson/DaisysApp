# Session notes: Plex

Past work on this module (Claude Code reads this file only when it works in this folder). Keep it short.

- **4.14.0 (asked 2026-11-12: new module Plex; setup wizard asks how to connect; Tautulli as a sub-tab or merged in;
  database integrity check, repair and especially backups, scheduled in Windows' Task Scheduler so it doesn't depend
  on the app; a "4K HDR" banner on many posters at once; who's playing; other admin tools for when there are
  problems; "publish as 4.14"):** `src/Modules/Plex/`, an `AppletGroup` (Order 48, E8B2) of Now Playing, Server,
  Database, Posters, Tautulli. Decisions (mine): Tautulli both merged (Now Playing's Recent Plays, a Server check)
  and its own sub-tab (stats, users); database work only on the server's own PC (`PlexService.IsLocal`: /identity's
  machineIdentifier = registry `ProcessedMachineIdentifier`), with Plex's own `Plex SQLite.exe`; backups with
  `VACUUM INTO` (consistent while Plex runs), not `.backup` (restarts on every write by another process); repair =
  DBRepair's idea (check → VACUUM/REINDEX, or .dump/.recover → new db → check → swap), Plex closed, a copy kept;
  banner undo = PUT `/poster?url=` the poster selected before (kept in settings), not a local copy of the image;
  default backup folder `<Plex data drive>\Plex Backups` (Documents is OneDrive here).
- **Found on this PC (it's the PLEX server):** PMS 1.43.4 installed in `Y:\Plex Media Server` (registry
  InstallFolder says `C:\Data\…`: the running process wins), data `Y:\Plex Media Server` (LocalAppDataPath `Y:\`),
  Tautulli 2.18.2 on 8181 (`%LOCALAPPDATA%\Tautulli\config.ini`). A corrupt db on 2026-11-10 (`*-corrupt-*`,
  `*.broken-*` 9.6 GB), a 1.2 GB WAL, Tautulli's saved pms_identifier stale (5d889c… vs cb800b…).
  Filters that work: `resolution=4k`, `hdr=1`, `dovi=1`, `unmatched=1` (`videoResolution=` doesn't: it errors in
  PMS's log).
- **Lessons:** `Plex SQLite.exe` runs a child named **`Plex Media Server.exe`**: find the server by parent process
  (`PlexLocal.ServerPids`), stop it by PID. The group's own `Plex.json` (sub-tab) overwrote settings saved under the
  same name: settings are `PlexServer.json`. The task needs `<Priority>4</Priority>` (the default 7 made the check of a
  fresh copy crawl, 0.25 MB/s on D:). `strings.py` takes `[Applet]`'s second literal as the title: use a literal id.
  WPF ListBox rows are white on white without `PlexUi.ListRow`.
- **Tested:** harness `%TEMP%\plexh` (`plexh.exe errors tautulli posters check backup schedule list`, live server,
  ALL PASSED; backups to `D:\Temp\plexprobe\backups`): discovery, connect/IsLocal, 401 and refused, Tautulli bad
  key/cmd, 4K list (1,222: 82 4K, 878 HDR, 262 DV; 60 shows) in 2 s, banner render, live quick_check, backup (30 s,
  both copies OK, prefs exported), pruning (keeps N good, drops stale incomplete), weekly task registered, run by Task
  Scheduler, removed. DaisysDev: wizard (This PC's Plex / Tautulli → connected), every sub-tab on screen, Quick Check,
  a banner added to and removed from one title (100 Nights of Hero: original poster selected again, byte-identical;
  its poster list keeps the upload). **Not tested:** Repair and Restore end to end (they close Plex; users were
  streaming), Stop on a stream, Sign in with Plex, a remote server, a scheduled run at its time.
- **4.16.0 (asked 2026-11-12: Submodule Scanner, tools to find things; first duplicates (titles with more than one
  file), sortable, filterable, with links to delete files; usage by show; a transcoder on the local GPU, probably
  ffmpeg, for whole seasons (1080p → 720p); output settings from Video's recording presets, moved to Core if only
  there, adding a 720p ~1 Mbit/s one; "publish as next subversion"):** sub-tab `Scanner` (E721) = `Views/ScannerView`
  (chips: `DuplicatesPanel`, `SpacePanel`, `TranscodePanel`) over `Scanner/` (`MediaFiles` reads `/all?type=1|4`
  1000 a page, 3 at once, kept per library; `Ffmpeg`; `Transcoder`). Core 0.64 `Shared/Video/VideoProfiles`
  (`RecordingProfile`, `RateControl` moved from Video's `GamingSettings`; `VideoProfiles.json`, read from
  Gaming.json's Profiles the first time, adding 1080p/720p). Decisions (mine): FFmpeg not Media Foundation (MKV,
  TrueHD/DTS, PGS); pinned to gyan's **7.1** (7.1.1+ and 9.x need NVENC API 13 = driver 570+; this PC's GTX 1080 Ti
  has 560.94 and Pascal gets no newer); output always MKV beside the original as `.mkv.part`, checked (duration
  ±0.5 %, smaller) before replacing; frame rate kept; skip if ≤ height and ≤ 1.1× peak rate, DV profile 5, HDR with
  H.264; deletes are permanent (space is the point), never a title's last file; one server→PC path pair for remote
  servers; queue not saved across restarts. Version 4.16.0 (a new sub-tab = minor).
- **Lessons (4.16.0):** a file holding two episodes (S01E01-E02) is listed for each: Space dedupes by path. Plex's list
  calls are fast (32,612 episodes in 4.9 s); PowerShell's parsing made it look slow (13 s / 5,000). The Edit/Write
  tools turn `\uE721` into the glyph: run `%TEMP%\plexs\fixglyph.py <files>` on new files only (it also converts
  glyphs other modules keep as characters: don't run it on them). Bash heredocs eat `\f`, `\r`, `\t` in paths.
- **Tested (4.16.0):** harness `%TEMP%\plexs` (`plexs.exe dups all transcode [keep] cancel shutdown`, copies in
  `D:\Temp\ffm\t`, deleted after: copy an episode and a 4K clip there again; FFmpeg 7.1 in `D:\Temp\ffm`), all passed;
  test app copy `%TEMP%\DaisysPlex` (settings were copied from DaisysDev, then deleted; `ui.ps1` drives it by UIA): Duplicates, Space, seasons, queue, Get FFmpeg. **Not tested:** deleting
  or transcoding library files, a remote server, QSV/AMF.
- **Now:** 4.16.0 released. **Next:** the user tries `docs/UNTESTED.md`'s 4.16.0 and 4.14.0 entries. Ideas not done:
  a saved queue; more Scanner tools (missing episodes, files Plex doesn't know, bit-rate outliers); delete uploaded
  banner images; Tautulli graphs.
- **4.21.0 (asked 2026-11-23, watching 3 Body Problem 4K DV, 12.6 Mbps Direct Play, on the Fire TV Stick 4K (AFTKM,
  Wi-Fi) with 15 s+ pauses: look now, or build something as a Plex sub-tab):** found live: the stick "buffering" 2+
  minutes with 0 bytes sent (`/statistics/bandwidth?timespan=6`) and D: idle; pings 1 ms → 390–540 ms with drops
  during its bursts (60–110 Mbps): its Wi-Fi, not Plex. PMS's log is warnings only (no requests). Built sub-tab
  `Streams` (E9D2) = `Views/StreamsView` over `Streams/StreamMonitor` (module-level, made in `PlexApplet`, so
  **Watch in background**, on by default, records on other tabs; 15 s idle). Decisions (mine): no disk counters (PDH is
  in `DaisysApp.Shared`, Plex depends only on Core); causes from sent-before/during, pings, transcode speed (see the
  README's table); stalls in the first 10 s or within 10 s of a seek (offset jump > 15 s) not counted.
- **Lessons (4.21.0):** `timespan=6` = per-second points, about 2.5 minutes kept, seconds with nothing sent missing
  (filled with 0 up to the newest second any device has); Device `clientIdentifier` = Player `machineIdentifier`;
  `timespan` is a code (6 s, 4 hour, 3 day…), not seconds. `tools/strings.py` (even `--help`) rewrites every
  module's en.json: keep only the Plex one.
- **Tested (4.21.0):** harness `%LOCALAPPDATA%\Temp\plexstall` (10 made-up streams through StreamTrack/Stall by
  reflection: every cause, start and seek not counted; ALL PASSED); DaisysDev on the live server (the Fire TV and a
  remote Plex Web transcode: graphs, pings, Watch in background). **Not tested:** a real stall listed.
