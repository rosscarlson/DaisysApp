# Hardware: Performance

*A tool in [Daisy's App](../../../../README.md)'s [Hardware](../README.md) tab: its **Performance** sub-tab. Switch Hardware on or off in Settings → General → Applets. Its code is in this folder, part of the Hardware module (installed to `modules\Hardware\` next to `DaisysApp.exe`).*

The PC's performance at a glance, with history.

**Tiles** (each with a 2-minute live graph; **click one for its history**):

| Tile | Shows |
|---|---|
| CPU | Load (as Task Manager counts it), clock speed, number of processes and threads |
| GPU | Load, clock, power, model |
| RAM | Use, GB in use of installed, committed memory |
| VRAM | Use, GB in use of the card's total |
| Disk | Active time of the busiest disk (and which one), read and write speed of all disks together |
| Network | Download and upload speed (all adapters) |
| Temps | CPU and GPU temperature (the hotter one in big), CPU and GPU power, GPU fan |
| CPU Cores | A bar per logical processor |

**Press and hold a tile** (rather than click it) to pick it up, then drag it among the others to change their order
(saved).

**Hardware sensors** — every temperature, fan, voltage and power sensor from LibreHardwareMonitor — have their own tab,
**Sensors** (see [its README](../Sensors/README.md)). The Temps tile shows the CPU's from it too.

**Warnings.** A tile turns **orange** or **red** when it's over its warning levels (judged on a 3-second average, so a
single spike doesn't): CPU 80 / 95 %, busiest CPU core 90 / 98 %, GPU 90 / 98 %, memory 80 / 90 %, video memory 85 /
95 %, busiest disk 80 / 95 %, download 400 / 800 and upload 200 / 400 Mbit/s, CPU temperature 80 / 90 °C, GPU
temperature 80 / 88 °C. Each history window's **Warnings** button changes its levels, and its graphs show them as
dashed lines. In the process list, a process over its levels is highlighted orange or red (CPU 25 / 50 %, RAM 4 / 8 GB,
GPU 80 / 95 %, VRAM 4 / 8 GB, disk 100 / 300 MB/s); the **gear** next to the search box sets those and how often the
list refreshes (0.5 to 5 seconds, 1 by default).

Below: the **process** list — **Apps** (programs with a window) first, then **Background processes**, like Task
Manager — with CPU, RAM (private memory), GPU, **GPU engine** (which graphics card and engine it's using, e.g. "GPU 0 -
3D"), VRAM, disk/network I/O and threads; sortable, live, searchable; **double-click a process for its graphs**.

**A program's processes are one row** (since 1.9.0): a browser runs a process per tab, and they show as one row, for
example **msedge (32)**:

- **The row adds them up:** CPU, RAM, GPU, VRAM, disk and threads. Its GPU engine is the busiest one's, and its PID
  is the main process's.
- **The arrow** at its left (or a double-click, or → and ←) opens it: each process is listed under it, indented, with
  its own figures.
- **The main process** comes first, in bold. It's the one not started by another of the same name: the program, not
  its tabs.

It stays open until you close it, and it's sorted with the rest. Processes alone under their name are rows as before.
On a group, **End task** ends all of them (it asks first), and **End process tree** ends the main one and what it
started.

**Right-click a process** for what Task Manager offers (since 1.5.1):

- **Switch to**: brings its window to the front (only if it has one).
- **End task** (or **Del**) and **End process tree**: the tree is the process and everything it started. Ending a part
  of Windows (csrss, winlogon, svchost, explorer…) or Daisy's App itself is asked about first. If Windows won't let it
  be ended (another user's, or an elevated program), it offers to try as administrator, which asks Windows for
  permission and uses taskkill.
- **Set priority**: realtime, high, above normal, normal, below normal or low, with the current one ticked. Realtime is
  asked about first.
- **Open file location**, **Properties** (the file's Windows properties), **Search online**.
- **Copy name**, **Copy PID**, **Copy path**.
- **History…**: what a double-click does.

Beside it **Network Tests**, **Speed Test**, **Storage** (each drive's free space) and **System** (processor, cores, memory,
graphics card and driver, Windows version, uptime).

**Network Tests** pings each host once a second while Daisy's App runs (in the tray too) and graphs the last 5 minutes,
a line per host, with the latest response time and the last minute's average, worst and lost pings (a gap in a line
is no answer). The graph goes from 0 to 50 ms (a slower answer runs along the top). Each host has an alert level,
**Max** (50 ms by default): an answer slower than that turns its line bold and red, and its time red, until it has
been back under it for **Red for** seconds (15 by default). Click a host to highlight its line in the graph and dim the
others; click it again to show all. The gear sets the hosts (a name, an IP address or hostname, Max and Red for for
each) and the graph's top (0 fits it to the answers). On first run they're your router, Cloudflare 1.1.1.1 and Google
8.8.8.8, and **Add my router** adds the router back.

Both network cards have a checkbox by their name: untick it and the card dims and stops completely — no pings, or no
speed tests at all (scheduled or Run now) — until you tick it again. Handy while gaming. On a new install the Speed
Test card starts unticked: tick it to use it.

**Speed Test** measures latency, then downloads and then uploads on six connections at once against the nearest
[speedtest.net](https://www.speedtest.net) server: the app takes the nearest few from speedtest.net's public list, keeps
the one that answers fastest (picked again every few hours or after a failure), and falls back to Cloudflare's speed
test if none answers. A server that refuses (e.g. "too many requests") is skipped for the next one. The server each
result came from is kept with it. The card shows the latest download, upload and latency side by side, and under them
when the test ran (and if anything was slow) and when the next test is due; while a test runs, its progress. The
graph's top is fixed (2,500 Mbit/s by default, set in its gear; 0 fits it to the results), so results compare at a
glance; its scale reads in Gbit/s (2.5 Gb, 1.8 Gb…) when it goes that high. **Run now** runs one, and its gear sets:
- **Schedule:** on or off, every 5 minutes to once a day (once a day by default).
- **Test length:** 1 to 30 seconds each way (5 by default). The clock starts when data starts arriving, so a slow
  start doesn't shorten the test; the first fifth of it (at most a second) is left out of the speed while TCP gets up
  to speed. Very short tests read low on a fast connection.
- **Information:** the server the last test used, and **Data used** — what that test downloaded and uploaded
  together, which counts towards a data cap — with the total of the day's tests.
- **Warnings:** the card turns orange or red, like the tiles, when the latest result's download or upload is below a
  level or its latency above one (0 turns a level off).

If the server doesn't answer, or no data arrives (or it stops arriving) for 10 seconds, the test fails: the card turns
red and says why, and the failure is a gap in the graph. The graph shows the last 24 hours of results; they're kept,
failures included, for 30 days in `speedtest.csv` in the performance log folder. **Mind the data:** a test fills your
connection while it runs, and on a fast connection it moves a lot — about 1.2 GB per 10 seconds at 1 Gbit/s, each way
— so a test every 10 minutes can come to well over 100 GB a day. The settings window estimates it from your last result, and
the card shows what each test and the day used.

**History windows** show the last 10 minutes live (a reading a second), or the last hour, 6 hours, 24 hours, 7 days,
30 days, today, yesterday or any logged day. Hover over a graph to read its values; click a name in a graph's legend
to highlight that line in white (click again for all); **Show peaks** adds the highest value of each period as a faint
line. Related lines share a graph (CPU and GPU temperature; CPU and GPU power). The summary stays at the bottom while
the graphs scroll. Each window has a **Summary** (lowest, average, highest, and the value it stayed
under 95% of the time), folded away until you click it: it opens at the bottom, the graphs making room above it, the CPU and Memory windows list the busiest processes over the period (double-click for that
process's history), and **Export…** saves the numbers as a CSV file.

**The top of a graph.** Graphs that aren't 0–100 % (clocks, power, disk and network speed…) fit their top to the
readings, rounded up a little (a 6.1 GHz clock tops out at 7 GHz). **Click the scale** on a graph's left to set the top
yourself, e.g. your card's highest clock, or to put it back to fitting the data; it's used everywhere that metric is
drawn (history windows, its tile and widgets).

**More than one graphics card?** The GPU window shows a chip per card: pick one or more and the load graph draws each
in its own colour.

**Widgets.** A history window's **Widget** button pops its main graph out into a small window of its own (the last few
minutes, live, with the current value) to keep anywhere on the screen, like a Mini Mirror. When it's new, drag it into
place and stretch it from any edge or corner; the strip at its bottom sets its **opacity**, whether it stays **on top**
of other windows (it costs nothing extra), and how much time it shows (1 to 10 minutes); **Done** fixes it. **Right-click**
a widget to change it again. The **Widgets** card under Processes lists them, to change or delete each. They're saved,
and come back where you left them when Daisy's App starts.

**The log.** While Daisy's App runs (also in the tray) it records, every 10 seconds, the average and peak of every
graph plus the 5 busiest processes by CPU and by memory: about 150 KB a day (some 50 MB a year) in
`%LOCALAPPDATA%\DaisysApp\logs\performance` (the Sensors sub-tab keeps its own log there too). Settings → Hardware → Performance
turns the log off, sets how long it's kept (7 days to 2 years, or forever; a year by default), opens the folder or
deletes it. After a restart the tiles and live graphs pick up the last 10 minutes from the log, so they don't start
empty.

**Where the numbers come from.** Windows' performance counters (the same as Task Manager and Performance Monitor);
NVIDIA's driver for the GPU's load, clock, power, fan, temperature and memory (other graphics cards get load and
memory from Windows, and their temperature, power and fan from LibreHardwareMonitor); and **LibreHardwareMonitor**,
through the Sensors sub-tab's connection, for the CPU's temperature and power (set it up on the Sensors sub-tab; without it the
Temps tile shows the GPU only).
Memory and disk sizes are in GB as Windows counts them (1 GB = 1024³ bytes).

## Code

| File | What it is |
|---|---|
| `PerformanceApplet.cs` | The sub-tab: creates the monitor, log, tests and widgets |
| `PerformanceView.Processes.cs` | Processes sharing a name as one row (totals, the arrow, the main one from each process's parent); the process list's right-click menu: end task / tree (elevated through taskkill when needed), priority, file location, properties, search, copy |
| `PerfMonitor.cs` | Samples the PC once a second on a background thread (CPU, memory, GPU, disk, network, temperatures) and the processes every few seconds; the last 10 minutes in memory |
| `PerfLog.cs` | The log: a 10-second summary per row (averages, peaks, busiest processes), kept for the days set |
| `PerformanceView.xaml(.cs)`, `PerformanceView.Network.cs` | The page: tiles, per-core load, the process table, system details; the Network tests and Speed test cards |
| `NetTests.cs` | The pings and the speed test (schedule, results) |
| `HistoryWindow.xaml(.cs)` | A tile's or a process's history: live, or any period from the log |
| `WidgetManager.cs`, `WidgetWindow.cs` | Widgets: a tile's graph in its own small window |
| `Metrics.cs`, `Limits.cs`, `LimitsWindow.xaml(.cs)`, `ScaleWindow.cs` | What each metric is, its warning levels, and its graph's top |
| `NetSettingsWindow.xaml(.cs)`, `SpeedSettingsWindow.xaml(.cs)` | The network tests' and speed test's settings |
| `ProcessRow.cs`, `SystemInfo.cs` | A process table row; the PC's fixed facts |
| `PerformanceSettingsView.xaml(.cs)` | `Performance.json`, and Settings → Hardware → Performance |

GPU, Windows counters and the sensors come from `DaisysApp.Shared.Hardware` (Core); the graphs are Core's `LineGraph`.
