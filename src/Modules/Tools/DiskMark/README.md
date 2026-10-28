# Tools: DiskMark

*A tool in [Daisy's App](../../../../README.md)'s [Tools](../README.md) tab: its **DiskMark** sub-tab, which measures a drive's read and write speeds as [CrystalDiskMark](https://crystalmark.info/en/software/crystaldiskmark/) 8 does. Its code is in this folder, part of the Tools module (since 3.21.0).*

## Using it

The **drives** are on the left: each with its letter and name, the disk under it (its model, and how it's connected:
NVMe, SATA, USB, RAID…; SSD or HDD) and how full it is. Pick one: the page shows that drive's results, and nothing
but *No runs on this drive yet* until it has some. **Start** tests it; **Stop** stops it (what was measured so far is
kept, marked *stopped*).

**The options** along the top choose how long and how hard a run is (as CrystalDiskMark's do):

| Option | What it does |
|---|---|
| **Profile** | Which tests: **Default** (SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1, RND4K Q1T1), **NVMe SSD** (SEQ1M Q8T1, SEQ128K Q32T1, RND4K Q32T16, RND4K Q1T1), **Peak Performance** (SEQ1M Q8T1, RND4K Q32T16), **Real World** (SEQ1M Q1T1, RND4K Q1T1), or **Custom** (**Tests…**: up to six, each sequential or random, its block size, its queue Q and threads T) |
| **Test** | **Read & Write** (every read test, then every write test, as CrystalDiskMark 8), **Read**, **Write**, or **Mix 70/30** (70% reads, 30% writes at once) |
| **Runs** | Times each test runs (1–9, 5 by default); the best is kept |
| **Size** | The test file: 16 MiB to 64 GiB (1 GiB by default). A bigger file gets past a drive's cache, so it shows what the drive keeps up, not what it bursts |
| **Time** | How long each run measures (1–60 s, 5 by default) |
| **Interval** | The pause between runs (0–60 s, 5 by default), letting a drive cool and empty its cache |
| **Data** | What's written: **Random** bytes, or **0x00** (drives that compress score higher on zeros) |

Beside them, **≈** how long a run takes on one drive. A test is named as CrystalDiskMark names it: **SEQ1M Q8T1** is
sequential, 1 MiB blocks, 8 requests waiting at once (the queue depth), 1 thread; **RND4K Q32T16** random 4 KiB
blocks, 32 requests waiting on each of 16 threads.

**While it runs**: a gauge with the live speed (on a logarithmic scale, 1 MB/s to 20 GB/s, so a random 4K run and a
sequential one both move it) in blue for reads, orange for writes, purple for a mix, and the operations a second
under it; beside it, the **test file as a grid of cells**, each lighting up as requests land in it and fading after:
a sequential test sweeps a band through the file (one per thread), a random one sparkles everywhere, densely at a
deep queue; the writing of the file fills it first. Under it, the step's bar, the whole run's bar and the time left.
The results table fills in as each test finishes, its cell being measured outlined and showing the live speed. The
title bar shows a green **DiskMark** bubble.

**The results**: a row per test, a column for read and one for write (or for the mix), each with its speed large
(GB/s or MB/s, as set in Settings), its operations a second and the average time a request took (µs), and a bar for
its share of the run's best (linear, or logarithmic so small ones still show; Settings). Point at one for all its
numbers. **Copy** puts the results shown on the clipboard as text, as CrystalDiskMark's Copy does.

**History**: the drive's results over time for the test picked at the right (read and write lines; point at the
graph to read a run), and every run in a table: click one to show it in the results above, **Delete** it (Confirm?
first). **All Drives** (once two drives have results): each drive's last result for the test picked, as bars.

## How it measures

As DiskSpd does under CrystalDiskMark: the test file is written first (its whole size, so reads find real data) in a
folder at the top of the drive (`<drive>\DaisysApp-DiskMark\`; on the system drive, in TEMP if Windows won't allow
it there), opened without Windows' cache (unbuffered, write-through), and deleted after, even if the app ends during
a run (it's opened delete-on-close). Each thread opens the file and keeps its Q requests waiting on its own I/O
completion port, sending the next as each one finishes, for the measure time; sequential tests walk each thread's
share of the file in order, random ones pick any block. Speeds are in decimal units (1 MB/s = 1,000,000 bytes a
second), as CrystalDiskMark's. The drive needs room for the file plus 64 MB.

## Schedule

Off at first. **Schedule…** (or Settings → Tools → DiskMark): **Run tests on a schedule**, the **First run** (date
and time), **Repeat** (once, every hour, 6 or 12 hours, day, week or 30 days) and the **Drives** to test, one after
another, with the page's options as they are. If the app wasn't running when one was due, it runs once when the app
starts. A run started by the schedule is marked *scheduled*.

## Settings

Settings → Tools → DiskMark: **Speeds in** GB/s (the default) or MB/s, **Bars** linear or logarithmic, and the
schedule. The page's options and the drive picked are kept in `DiskMark.json`. Each run is a file in
`%LOCALAPPDATA%\DaisysApp\logs\diskmark\` (`diskmark-<drive>-<date>-<time>.json`), shown in Settings → General →
Storage as **Disk benchmarks**; a summary of each also goes in the Tools log.

## Code

`DiskMarkApplet.cs` (the sub-tab, its Settings page, the schedule and Custom tests windows, the half-minute schedule
check), `DiskMarkView.cs` (the page: drives, options, live card, results table, history and `CompareChart`),
`Activity.cs` (`Gauge` and `FileMap`, drawn directly, and `Ink`, the colours), `Bench.cs` (the tests and profiles,
and the measuring: unbuffered overlapped I/O on a completion port per thread, the file's writing),
`Runner.cs` (a run on each drive: the order, best of N, interval, the results files, the schedule),
`Drives.cs` (the drives, and each one's model, bus and SSD/HDD from `IOCTL_STORAGE_QUERY_PROPERTY`, which needs no
administrator rights), `DiskMarkSettings.cs` (`DiskMark.json`).
