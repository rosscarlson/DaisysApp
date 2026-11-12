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
