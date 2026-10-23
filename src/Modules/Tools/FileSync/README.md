# Tools: File Sync

*A tool in [Daisy's App](../../../../README.md)'s [Tools](../README.md) tab: its **File Sync** sub-tab, which keeps two folders the same, like [FreeFileSync](https://freefilesync.org/). Its code is in this folder, part of the Tools module.*

## The page

The top row: the saved syncs list, **Save as…** and the bin on the left; **Compare** (green) and **Sync** (blue) on the
right.

- **Source** (left) and **Destination** (right): type or paste a path, **Browse…**, or pick one of the recent ones
  from the list. Under each: how much is free on its drive (and, after comparing, how many files it has). The
  button between them **swaps** them. A destination that doesn't exist yet is made.
- **Mode**:
  - **Mirror**: the destination ends up exactly like the source. New and changed files are copied over, and files and
    folders that are only in the destination are deleted.
  - **Update**: new and changed files are copied; nothing in the destination is deleted, and a file that's newer in
    the destination is left as it is.
- **Compare by**: tick what has to match for two files with the same name to count as the same (any mix):
  - **Name** is always on: files are matched by their name and folder. With nothing else ticked, a file that's on
    both sides is the same, whatever its size or time.
  - **Size**.
  - **Date and time** (within 2 seconds, as FAT drives keep times). Untick it when the times are wrong but the sizes
    right, e.g. after a copy that didn't keep the times. (In Update, a newer destination file is only left alone while
    this is ticked: otherwise the source's wins.)
  - **Contents (slower)**: files of the same size are read on both sides and compared byte by byte.

  Size and date and time are ticked to start. (Before 0.41 it was a choice of Time and size, or Contents; a sync
  saved with Contents keeps meaning Size and Contents.)
- **Deleted files and folders go to the Recycle Bin** (Mirror; on by default). A network drive or a USB stick has no
  Recycle Bin, so there they're deleted for good, and the sync's window says so first.
- **Leave alone**: names or paths no sync touches on either side, separated by `;`: `*` and `?` wildcards, a trailing
  `\` for folders only, and a `\` inside for a path from the top folder (e.g. `Photos\Raw\`). **Usual** puts back the
  usual list (`Thumbs.db`, `desktop.ini`, Office's `~$` files, `*.tmp`, `$RECYCLE.BIN\`, `System Volume Information\`).
- **Compare** (green; once both folders are filled in) reads both folders (counting the folders and files found on
  each side as it goes) and lists what a sync would do under **Sync Details**, without changing anything: each file or folder with its size and time on each side and the action (Copy, Update, Delete,
  Leave, Keep, Same), filtered by the chips over the list (each with its count). Double-click a line to show the file
  in Explorer.
- **Sync** (once a comparison has found something to do) opens the sync's own window (below), with what the
  comparison found. Changing a folder or a setting takes the comparison away: Compare again first.
- **Saved syncs**: the list starts on *Saved syncs* (none picked). Pick one to load its folders and settings; pick
  **New…** and give a name, set the folders and settings, then **Save as…** (which is only available then) keeps it,
  to pick again from the list or run from the tray icon's menu (Tools → *Sync "name"…*). The bin button forgets the
  picked one (its folders aren't touched). Changing a saved sync's folders or settings goes back to *Saved syncs*: to
  keep the change, New… with the same name, then Save as…
- **Syncs in the background**: a sync sent to the background (below) shows just above Recent Syncs, its progress on
  one line (the bar, how far, the speed and the time left). Click it to open it out: the speed graph and the readouts,
  **Pause** and **Stop**. **Pop out**, at its top right, brings back its window. When it ends, the line says how
  (**Close** clears it away).
- **Recent Syncs**: the last ones, with what they did, and under one with problems its problems (the first 50).

Problems (in Recent Syncs, a sync's window and its card in the background) can be selected and copied, and each is
written to the Tools log as an error: a file a sync couldn't copy or delete, a folder Compare couldn't read, a
comparison that failed.

Every change on the page is saved as you make it.

## The sync's window

It's a window of its own, so the app stays free while a long sync runs (you can minimise it, use other tabs, even
start another sync of other folders):

1. **Check before syncing**: the two folders, the mode, how files are compared, what happens to deleted files and
   what's left alone. Then what the sync does, a tick each, so you can still change your mind: **Copy** *n* new
   files, **Make** *n* new folders, **Update** *n* changed files, **Delete** *n* files and *n* folders that are only in
   the destination. Untick any of them and it's left out (files in new folders are only copied when the folders are
   made too); the amount to copy follows. Anything worth a second look is in red: an empty source (mirroring it would
   empty the destination), a sync that deletes most of the destination (the folders the wrong way round?), no Recycle
   Bin, folders that couldn't be read. **Start**, or **Cancel**. (A saved sync run from the tray compares the folders
   here first.)
2. **Syncing** (the window's title is the sync's name and *Sync*, whichever the mode): the speed in large figures with
   the percentage done, then a **speed graph** like Windows' copy dialog but with its scales: the speed (MB/s) against
   the time since it started, the whole run fitted to the width, with a dashed line at the average. Under it the
   readouts: speed, average, peak, time taken, bytes and items done, time left and problems; then a bar and the file
   being copied. **Background** hides the window and shows the sync on the tab instead (above); **Pause** and
   **Resume**, or **Stop**. A problem with one file (open in a program, no permission) is listed and the sync goes on.
3. **Done**: what it did, and any problems. **Open the destination**. The taskbar button flashes if the window was
   minimised.

Closing the window while it syncs asks first; closing the app stops any sync running.

## How it syncs

- Both folders are read at once, each with several threads (`Shared/FastWalk`), so comparing large folders takes
  seconds. Links to other folders (junctions) aren't followed.
- The order is FreeFileSync's: deletions first (frees the space), then new folders, then the files.
- Each file is copied to a temporary name beside the old one (`name.daisysync~`) and swapped in when it's complete,
  so a stopped sync, a full disk or a crash never leaves half a file. Copies keep the file's time and attributes (a
  read-only file stays read-only).
- In Mirror, a file and a folder with the same name are replaced (the destination's goes, the source's is copied); in
  Update they're left as they are.

**Code:** `FileSyncApplet.cs` (the sub-tab, its tray items and the sync windows), `FileSyncView.cs` (the page),
`SyncWindow.cs` (a sync: check, progress, done), `SyncProgressView.cs` (a running sync measured, its speed graph and
readouts, shared by the window and the tab's background card), `SyncPlan.cs` (comparing: what to do with each file, and the Leave
alone list), `SyncRunner.cs` (doing it), `FileSyncSettings.cs` (`FileSync.json`: the page, saved syncs, recent folders
and syncs).

## Code

`FileSyncApplet.cs` (the sub-tab), `FileSyncView.cs` (the page: the pairs and the comparison), `SyncPlan.cs` (what a
sync does with each file and folder), `SyncRunner.cs` (carries out a plan: deletions, then folders, then copies),
`SyncWindow.cs` (a sync in its own window), `SyncProgressView.cs` (its speed graph and totals),
`FileSyncSettings.cs` (`FileSync.json`). Shared with the other tools: `../Shared/` (`FastWalk.cs` folder reading,
`Native.cs` Windows calls, `Sizes.cs`, `Ui.cs`).
