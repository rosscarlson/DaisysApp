# Plex

*A module of [Daisy's App](../../../README.md): the **Plex** tab. Switch it on or off in Settings → General → Modules
(each sub-tab too). Its code is in this folder; it's installed to `modules\Plex\`. New in 4.14.0.*

Admin tools for your [Plex Media Server](https://www.plex.tv/), for when something's wrong and before it is: who's
watching and how, what's wrong with the server, its libraries and maintenance jobs, its database checked, repaired and
backed up on a schedule, a 4K / HDR / Dolby Vision banner on many posters at once,
[Tautulli](https://tautulli.com/)'s statistics, and files found and made smaller (duplicates, space by show,
transcoding on the graphics card). Six sub-tabs: **Now Playing**, **Server**, **Database**, **Posters**, **Tautulli**
and **Scanner** (since 4.16.0).

The database tools work on the server's own PC (run Daisy's App there); everything else works from any PC that can
reach the server.

## Setting it up

The setup wizard asks the first time the app starts with this module, with **Skip for now** (it's then in Settings →
Plex) and **Switch off Plex**. Switched on later, it asks in its own window.

- **Plex Media Server**, three ways:
  - **This PC's Plex** (shown when the server is on this PC): `http://127.0.0.1:32400` and the token Plex keeps for
    its owner's account (in this Windows account's registry).
  - **Sign in with Plex…**: plex.tv's sign-in opens in the browser, as Plex's own apps do; once you approve it, pick
    one of your account's servers (the first of its addresses that answers is used, local ones first).
  - By hand: the **Address** (`http://192.168.1.20:32400`; no port means 32400) and your **Token** (X-Plex-Token).
    **Accept a certificate Windows doesn't trust** for an `https://` address with a self-signed certificate.
- **Tautulli** (optional): its address (`http://192.168.1.20:8181`; no port means 8181) and its API key (Tautulli →
  Settings → Web Interface). **This PC's Tautulli** fills both in from Tautulli's `config.ini` when it's on this PC.

**Test connection** tries each. The token and key are kept encrypted for your Windows account.

## Now Playing

Every stream on the server, asked every 5 seconds while the tab shows:

- the poster, the title (a show's episode as S05E07 · title), who's watching, the player (its name and app), local or
  remote and its address, and the bandwidth;
- how it plays: **Playing / Paused / Buffering**, then **Direct Play**, **Direct Stream** (amber) or **Transcode**
  (red; **(hw)** when the graphics card does it), **Audio transcode**, **Throttled**, **Relayed** (through Plex's
  relay: slow), **Not secure**; the source (resolution, codecs, bitrate), what's sent (when transcoding), and the
  subtitles (**burned in** when the server has to draw them onto the picture, a common cause of transcoding);
- how far in, with a bar;
- **Stop** (Confirm?) ends the stream; the viewer sees "The server's admin stopped this stream." (Plex Pass).

The header says how many streams, how many transcode and the total bandwidth. **Server busy:** lists what the server is
doing (library scans, optimising, analysing… with how far). With Tautulli set up, **Recent Plays** lists its last 50:
when, who, what, on which player, how long, how it played, and from where.

## Server

- **Server**: its name, version and system, the account and Plex Pass, remote access (working, with the public address,
  or not), its local address, whether an update is available, and whether it's on this PC. **Refresh** asks again.
- **Checks**: what's wrong, red, amber or blue (information), green when nothing is:
  - not signed in to plex.tv; remote access not working;
  - a Plex update available;
  - Plex's own database backup or weekly optimisation switched off (Scheduled Tasks);
  - items not matched to their metadata; a library not scanned for a week;
  - on this PC: Plex SQLite missing; the database's write-ahead log over 512 MB (**Optimize** beside it: a big one
    means Plex can't fold it into the database, often before corruption); under 10 GB free where Plex keeps its data
    (a full disk corrupts the database); no backup by this module, or the last one over 8 days old; leftovers of past
    repairs over 100 MB; over 50 errors in Plex's log in the last day;
  - Tautulli: unreachable, or a different server identity saved than the server has (its history and lookups then go
    to the wrong place: Verify the server in Tautulli's settings).
- **Libraries**: each with its type, items, unmatched items, when it was last scanned (or **scanning…**) and its
  folders. Pick one or more (Ctrl, Shift), then **Scan** (new and changed files), **Refresh Metadata** (Confirm?: all
  of it again), **Analyze** (the files' media details again) or **Empty Trash** (Confirm?: items whose files are gone).
  **Scan All** scans every library.
- **Maintenance**: Plex's own jobs, started on the server (Now Playing shows them running): **Optimize Database**,
  **Clean Bundles**, **Back Up Database (Plex)** (Plex's own copy, in its backup folder), **Clean Old Cache Files**,
  **Collect Unused Images**, **Check for Plex Update**; **Download Logs…** saves Plex's logs as a zip (what Plex's
  support asks for); **Open Plex Web**.
- **This PC** (on the server's own PC): the data and program folders and how much the database, metadata, thumbnails,
  cache, photo cache and logs take; **Open Data Folder**, **Open Logs**, **Clear Photo Cache** (Confirm?: Plex makes
  them again as needed), **Restart Plex** (Confirm?: streams stop). **Leftovers of Past Repairs**: Plex's
  `*-corrupt-*` databases and `*.broken-*` folders, each with its size and **Delete** (Confirm?). **Errors in Plex's
  Log, Last Day**: grouped by message, the most frequent first, with how many and when the last was.

## Database

On the server's own PC. Plex's database is SQLite with Plex's own additions, so everything here runs **Plex SQLite**,
which comes with Plex Media Server (beside `Plex Media Server.exe`). It shows the library database's and the images
database's sizes and their write-ahead logs.

- **Check**: a full integrity check of both databases, while Plex runs (it only reads them; a minute or two on a big
  one). **Quick Check** is faster and finds most damage.
- **Repair** (Confirm?): closes Plex Media Server (as its tray icon's Exit does; ended if it doesn't close in 45 s),
  copies both databases as they are (with their logs) to **Before repair …** in the backup folder, then for each:
  if it checks OK, folds its write-ahead log in, compacts it (VACUUM) and rebuilds its indexes (REINDEX); if it
  doesn't, writes it out as SQL (`.dump`, or `.recover` when the dump stops at damage), reads that into a new database,
  checks it, and only then puts it in place. Then starts Plex again (if it was running). It needs about three times
  the databases' size free. If anything fails, what was there stays (or is kept in **Before repair …**: Restore puts
  it back).
- **Back Up Now**: what the scheduled backup does, now (Plex keeps running).
- **Schedule**: the **Folder** backups go in (by default **Plex Backups** at the top of the drive Plex keeps its data
  on: not Documents, often synced to OneDrive), how many to **Keep**, and **Back up on a schedule**: **Daily** or
  **Weekly** on a day, at a time. **Backup Log** opens `Backups.log` in the folder. It says when the next one is and
  when the last was.
- **Backups**: this module's backups, Plex's own (its dated copies in its backup folder) and the copies kept before a
  repair or restore (with their write-ahead logs, which Restore puts back too), with their size and whether they
  checked OK. **Restore** (Confirm?) checks the backup first, closes Plex, moves the database as it is to
  **Before restore …** in the backup folder, copies the backup in and starts Plex again. **Show in Explorer**,
  **Delete** (Confirm?), **Refresh**.

**The scheduled backup runs without Daisy's App.** It's a task in Windows' Task Scheduler (`\DaisysApp\Plex Backup`,
run as you, while you're signed in; a missed one runs as soon as it can) that runs a PowerShell script this module
writes, `%LOCALAPPDATA%\DaisysApp\Plex\PlexBackup.ps1`, so it keeps working whether the app is running or not, and
stays if the app is uninstalled. Each backup is a folder `Plex yyyy-MM-dd HHmmss` with both databases copied by Plex
SQLite's `VACUUM INTO` (a consistent copy while Plex runs, about half a minute for a 2.7 GB pair), each copy checked
(`quick_check`), Plex's settings exported from the registry (`Preferences.reg`: they include its token, so keep the
folder private) and `backup.json` (when, and whether the checks passed). The newest **Keep** good backups are kept; a
backup that failed keeps only its `backup.json` and goes after 12 hours, as does a folder a stopped run left.

## Posters

A banner across the top of posters: **4K**, **4K HDR** or **4K DOLBY VISION**, for many titles at once.

- **Titles**: a movie or TV library, and which titles: **4K titles**, **4K HDR**, **4K Dolby Vision**, **4K without
  HDR**, **With a banner** (put on here) or **Everything**; a search box. Each title says what it is (from Plex's own
  filters; a show is Dolby Vision if all its 4K episodes are, HDR if all are HDR or DV, else 4K). Tick them, or **All**
  (every one shown) and **None**.
- **Banner**: the text for each kind, its **Height**, **Background**, **Text** colour, a **Line** under it and
  **Heavy text**; the preview shows the title clicked in the list.
- **Add Banner** puts it on every ticked title: the poster is downloaded at full size, the banner drawn on, and the
  result uploaded to Plex, which selects it. A title that already has one gets it again from the poster under it (the
  one selected before), so changing the style and adding again replaces it. **Remove Banner** (Confirm?) selects the
  poster each had before. **Cancel** stops after the title in hand.

What each banner replaced is kept in the settings. Plex keeps every uploaded poster in the title's list of posters
(Plex Web → Edit → Poster), so taking a banner off leaves its image there, unselected. A metadata refresh that
replaces artwork replaces the banner too.

## Tautulli

Its statistics for the last **7, 30, 90 or 365 days**: the plays (TV, movies, music) and how many are playing now; most
watched and most popular movies, shows and artists, recently watched, most active users, platforms and libraries,
and the most streams at once. **Users**: everyone, with when they were last seen, their plays, how long they've
watched, what they played last, on which player and from where.

## Scanner

Tools that look through the libraries' files (since 4.16.0), in three parts. A library is read once (about 5 seconds for
30,000 episodes on the server's own PC) and kept until **Refresh**; the tools share the reading.

- **Duplicates**: titles with more than one file, a 720p and a 1080p of the same movie say. A movie or TV library (Plex's
  own list of titles with more than one version: quick), and **Across libraries** for the same title in two libraries
  too (every title read). Each title's files are under it, with **Best** ticked (the highest resolution, then bit rate),
  the version, bit rate, size, library, when it was added and the file; any column sorts, and the box searches titles and
  paths. **Pick All But the Best** or **Pick All But the Smallest** picks the others (or pick by hand: Ctrl, Shift);
  **Delete Files** (Confirm?) deletes the picked files from the disk (never a title's last one) and has Plex look at
  their folders again, so the versions go from Plex too. **Show in Explorer** (or a double-click), **Copy Path**.
- **Space**: what a library takes. For TV, a row for each show: its files, size, size per episode, resolutions and
  codecs (1080p ×40 · 720p ×2), its bit rate, hours, and roughly what it would take after a transcode with the video
  profile picked beside the library (**≈ After**, **≈ Saves**; files already that small are left as they are). Click a
  show (or several) for its seasons. For movies, a row for each movie. **Transcode Shows** / **Transcode Seasons** /
  **Transcode Movies** add the picked ones' files to Transcode's queue, leaving out those already that small.
- **Transcode**: makes files smaller with FFmpeg on the graphics card (NVIDIA NVENC, Intel Quick Sync or AMD AMF,
  whichever works here; else the processor, slowly).
  - **FFmpeg**: **Get FFmpeg** downloads it (gyan.dev's essentials build of FFmpeg 7.1, about 90 MB, to
    `%LOCALAPPDATA%\DaisysApp\ffmpeg`), or **Browse…** to an `ffmpeg.exe` (with `ffprobe.exe` beside it); one on the
    PATH is used too. It says which encoder the profile's codec gets. (7.1 because later builds need NVIDIA's driver
    570 or newer, which GTX 10-series cards never got.)
  - **Output**: the **Profile** is one of the app's video profiles, the same list as Video → Recording's (edited in its
    settings): its codec (H.264, HEVC or AV1), height (the picture scaled down to it, never up), average and peak bit
    rate, quality (the encoder's preset) and keyframes; the frame rate stays the source's. **1080p** (HEVC, 4–6 Mbit/s)
    and **720p** (HEVC, 1–2 Mbit/s) are there for this. **Audio as AAC at the profile's rate**, or (off) every audio
    track copied as it is. **Keep the original**: the new file goes beside it as `name - 720p.mkv`, which Plex shows as
    another version (Duplicates can then delete either); off, the original is deleted once the new file checks out.
    **Server path → this PC**: for a server on another PC, where its folders are from here (`/data/media` →
    `\\nas\media`).
  - **Queue**: each file with its source, state (how far, how fast, time left; or why it was skipped or failed) and
    what it saved. **Start** goes through the waiting files one at a time; **Stop After This One**, **Stop Now**
    (Confirm?: the file in hand is left as it was), **Remove** (picked waiting files), **Clear Finished**, **Show in
    Explorer**. A failed file's FFmpeg message shows under the list when it's picked.

  Each file is read with FFprobe first, then written as Matroska beside the original as `name.mkv.part` (Plex ignores
  it), with every audio track, subtitle (MP4 text subtitles as SRT), chapter and attachment kept, and 10-bit and HDR10
  pictures kept 10-bit with their colours (HEVC and AV1). Then it's checked: as long as the original (within half a
  percent) and smaller than it, else it's deleted and the original kept. The new file is `name.mkv` (an `.mp4` becomes
  `.mkv`), and Plex is asked to look at the folder again. Skipped: files already at or under the profile's height and
  peak bit rate, Dolby Vision profile 5 (it has no HDR10 picture under it), and HDR with an H.264 profile. It needs
  free space on the file's drive, runs below normal priority (Plex's own streams come first) and stops with the app
  (the half-written file deleted). The queue isn't kept when the app closes.

## Files

| | |
|---|---|
| `%APPDATA%\DaisysApp\PlexServer.json` | the server, Tautulli, backups' schedule, the banner's look and the posters with a banner (`Plex.json`: the sub-tab showing) |
| `%LOCALAPPDATA%\DaisysApp\Plex\PlexBackup.ps1` | the backup script (written again when the schedule or folder changes) |
| *backup folder*`\Plex yyyy-MM-dd HHmmss\`, `Backups.log` | the backups, and what each run did |
| *backup folder*`\Before repair …`, `\Before restore …` | the database as it was before a repair or restore |
| `%LOCALAPPDATA%\DaisysApp\ffmpeg\` | FFmpeg, when Scanner → Transcode downloaded it |
| `%APPDATA%\DaisysApp\VideoProfiles.json` | the video profiles, shared with Video → Recording |
| `%LOCALAPPDATA%\DaisysApp\logs\Plex-MM-dd-yy.log` | the module's log: connections, jobs started, stops, banners, repairs, files deleted, each transcode (with its FFmpeg command) |

## Code

| File | What it does |
|---|---|
| `PlexApplet.cs` | The tab (`AppletGroup`) and its six sub-tabs (`PlexTool`); the first one made has the setup step and Settings page and connects |
| `PlexSettings.cs` | The settings (`PlexServer.json`), the banner's style, the posters with a banner |
| `PlexService.cs` | The connection: which server (name, version, identifier), whether it's this PC's, the clients; libraries and counts |
| `PlexClient.cs` | Plex Media Server's HTTP API (JSON), and plex.tv's: the PIN sign-in and the account's servers; reading answers |
| `TautulliClient.cs` | Tautulli's API v2 |
| `PlexLocal.cs` | The server on this PC: its folders and program, Plex SQLite, its token, stopping and starting it (by process id: Plex SQLite runs a `Plex Media Server.exe` of its own), running programs; Tautulli's `config.ini` |
| `Database/Backups.cs` | The backups' list, the backup script, and its task in Task Scheduler |
| `Database/DbTools.cs` | Check, Repair and Restore |
| `Posters/PosterTools.cs` | Finding 4K / HDR / Dolby Vision titles, drawing the banner, uploading it and taking it off |
| `Scanner/MediaFiles.cs` | A library's files (`MediaFile`: each version's file, its size, resolution, codec, bit rate), read a few pages at once and kept; Plex asked to rescan a folder |
| `Scanner/Ffmpeg.cs` | FFmpeg: found, downloaded, its version; the graphics card's encoder for a codec (tried once); FFprobe's view of a file |
| `Scanner/Transcoder.cs` | The transcode queue: what's skipped, the size estimate, FFmpeg's arguments, running one file (progress, checks, replacing) |
| `Views/ActivityView.cs` | Now Playing (and `SessionCard`, one stream) |
| `Views/ServerView.cs` | Server: state, checks, libraries, maintenance, this PC |
| `Views/DatabaseView.cs` | Database |
| `Views/PostersView.cs` | Posters |
| `Views/TautulliView.cs` | Tautulli |
| `Views/ScannerView.cs` | Scanner: its three parts; the profile drop-down they share |
| `Views/DuplicatesPanel.cs` | Duplicates |
| `Views/SpacePanel.cs` | Space |
| `Views/TranscodePanel.cs` | Transcode |
| `Views/ConnectionForms.cs` | The server's and Tautulli's forms (on Core's `ConnectionForm`) |
| `Views/SetupPanel.cs` | The setup wizard's step (and its own window), and Settings → Plex |
| `Views/PlexUi.cs` | The module's own pieces: posters, facts lines, tags, findings, small buttons, list rows |
