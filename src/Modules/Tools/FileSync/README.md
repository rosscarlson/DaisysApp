# Tools: File Sync

*A tool in [Daisy's App](../../../../README.md)'s [Tools](../README.md) tab: its **File Sync** sub-tab, which keeps two folders the same, like [FreeFileSync](https://freefilesync.org/). Its code is in this folder, part of the Tools module.*

## The page

- **Source** (left) and **Destination** (right): type or paste a path, **Browse…**, or pick one of the recent ones
  from the list. Under each: how much is free on its drive (and, after comparing, how many files it has). The
  button between them **swaps** them. A destination that doesn't exist yet is made.
- **Mode**:
  - **Mirror**: the destination ends up exactly like the source. New and changed files are copied over, and files and
    folders that are only in the destination are deleted.
  - **Update**: new and changed files are copied; nothing in the destination is deleted, and a file that's newer in
    the destination is left as it is.
- **Compare by**: **Time and size** (files differ when either does, times within 2 seconds counting as the same, as
  FAT drives keep them), or **Contents** (files of the same size are read on both sides and compared byte by byte).
- **Deleted files and folders go to the Recycle Bin** (Mirror; on by default). A network drive or a USB stick has no
  Recycle Bin, so there they're deleted for good, and the sync's window says so first.
- **Leave alone**: names or paths no sync touches on either side, separated by `;`: `*` and `?` wildcards, a trailing
  `\` for folders only, and a `\` inside for a path from the top folder (e.g. `Photos\Raw\`). **Usual** puts back the
  usual list (`Thumbs.db`, `desktop.ini`, Office's `~$` files, `*.tmp`, `$RECYCLE.BIN\`, `System Volume Information\`).
- **Compare** reads both folders and lists what a sync would do, without changing anything: each file or folder with
  its size and time on each side and the action (Copy, Update, Delete, Leave, Keep, Same), filtered by the chips
  over the list (each with its count). Double-click a line to show the file in Explorer.
- **Synchronize…** opens the sync's own window (below).
- **Saved syncs**: **Save as…** keeps the folders and settings under a name, to pick again from the list, or run from
  the tray icon's menu (Tools → *Sync "name"…*); the bin button forgets one (its folders aren't touched).
- **Recent syncs**: the last ones, with what they did.

Every change on the page is saved as you make it.

## The sync's window

It's a window of its own, so the app stays free while a long sync runs (you can minimise it, use other tabs, even
start another sync of other folders):

1. **Check before syncing**: the two folders, the mode, what happens to deleted files and what's left alone. The
   folders are compared then, and it says what the sync will do: *Copy 120 new files, update 5 files, delete 7
   files…*, how much is to copy, and anything worth a second look in red: an empty source (mirroring it would empty
   the destination), a sync that deletes most of the destination (the folders the wrong way round?), no Recycle Bin,
   folders that couldn't be read. **Start**, or **Cancel**.
2. **Progress**: a bar, the files and bytes done, the speed and the time left, and the file being copied. **Pause**
   and **Resume**, or **Stop**. A problem with one file (open in a program, no permission) is listed and the sync goes on.
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
`SyncWindow.cs` (a sync: check, progress, done), `SyncPlan.cs` (comparing: what to do with each file, and the Leave
alone list), `SyncRunner.cs` (doing it), `FileSyncSettings.cs` (`FileSync.json`: the page, saved syncs, recent folders
and syncs).
