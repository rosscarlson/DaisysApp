# Performance

*A module of [Daisy's App](../../../README.md): the Performance tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\Performance\` next to `DaisysApp.exe`.*

The PC's performance at a glance, with history.

**Tiles** (each with a 2-minute live graph; **click one for its history**):

| Tile | Shows |
|---|---|
| CPU | Load (as Task Manager counts it), clock speed, number of processes and threads |
| GPU | Load, clock, power, model |
| Memory | Use, GB in use of installed, committed memory |
| Video memory | Use, GB in use of the card's total |
| Disk | Active time of the busiest disk (and which one), read and write speed of all disks together |
| Network | Download and upload speed (all adapters) |
| Temperatures | CPU and GPU temperature (the hotter one in big), CPU and GPU power, GPU fan |
| CPU cores | A bar per logical processor |

**Hardware sensors** (with LibreHardwareMonitor, below): every sensor it reports, grouped by hardware — motherboard,
CPU, each memory module, graphics card, each drive — with its value now and its minimum and maximum, filtered by kind
(Temperatures, Fans, Power, Voltages, Clocks, Load, Other). Click a sensor for its history, or a hardware name for all
its sensors of that kind on one graph. Without LibreHardwareMonitor a banner at the top offers **Get more sensor
data…**, which explains how to set it up and checks for it while it's open.

**Warnings.** A tile turns **orange** or **red** when it's over its warning levels (judged on a 3-second average, so a
single spike doesn't): CPU 80 / 95 %, busiest CPU core 90 / 98 %, GPU 90 / 98 %, memory 80 / 90 %, video memory 85 /
95 %, busiest disk 80 / 95 %, download 400 / 800 and upload 200 / 400 Mbit/s, CPU temperature 80 / 90 °C, GPU
temperature 80 / 88 °C. Each history window's **Warnings** button changes its levels, and its graphs show them as
dashed lines. In the process list, a process over its levels is highlighted orange or red (CPU 25 / 50 %, RAM 4 / 8 GB,
GPU 80 / 95 %, VRAM 4 / 8 GB, disk 100 / 300 MB/s); the **gear** next to the search box sets those and how often the
list refreshes (0.5 to 5 seconds, 1 by default).

Below: the **process** list — **Apps** (programs with a window) first, then **Background processes**, like Task
Manager — with CPU, RAM (private memory), GPU, **GPU engine** (which graphics card and engine it's using, e.g. "GPU 0 -
3D"), VRAM, disk/network I/O and threads; sortable, live, searchable; **double-click a process for its graphs**. Beside
it **Storage** (each drive's free space), **Network Tests**, **Speed Test** and **System** (processor, cores, memory,
graphics card and driver, Windows version, uptime).

**Network Tests** pings each host once a second while Daisy's App runs (in the tray too) and graphs the last 5 minutes,
a line per host, with the latest response time and the last minute's average, worst and lost pings (a gap in a line
is no answer). Click a host to highlight its line in the graph and dim the others; click it again to show all. The gear
sets the hosts: a name and an IP address or hostname. On first run they're your router, Cloudflare 1.1.1.1 and Google
8.8.8.8, and **Add my router** adds the router back.

Both network cards have a checkbox by their name: untick it and the card dims and stops completely — no pings, or no
speed tests at all (scheduled or Run now) — until you tick it again. Handy while gaming.

**Speed Test** measures latency, then downloads and then uploads on six connections at once against the nearest
[speedtest.net](https://www.speedtest.net) server: the app takes the nearest few from speedtest.net's public list, keeps
the one that answers fastest (picked again every few hours or after a failure), and falls back to Cloudflare's speed
test if none answers. A server that refuses (e.g. "too many requests") is skipped for the next one. The server each
result came from is shown on the card and kept with the result. **Run now** runs one, and its gear sets:
- **Schedule:** on or off, every 5 minutes to once a day (every 10 minutes by default).
- **Test length:** 1 to 30 seconds each way (10 by default). The clock starts when data starts arriving, so a slow
  start doesn't shorten the test; the first fifth of it (at most a second) is left out of the speed while TCP gets up
  to speed. Very short tests read low on a fast connection.
- **Warnings:** the card turns orange or red, like the tiles, when the latest result's download or upload is below a
  level or its latency above one (0 turns a level off).

If the server doesn't answer, or no data arrives (or it stops arriving) for 10 seconds, the test fails: the card turns
red and says why, and the failure is a gap in the graph. The graph shows the last 24 hours of results; they're kept,
failures included, for 30 days in `speedtest.csv` in the performance log folder. **Mind the data:** a test fills your
connection while it runs, and on a fast connection it moves a lot — about 1.2 GB per 10 seconds at 1 Gbit/s, each way
— so every 10 minutes can come to well over 100 GB a day. The settings window estimates it from your last result, and
the card shows what each test and the day used.

**History windows** show the last 10 minutes live (a reading a second), or the last hour, 6 hours, 24 hours, 7 days,
30 days, today, yesterday or any logged day. Hover over a graph to read its values; click a name in a graph's legend
to highlight that line in white (click again for all); **Show peaks** adds the highest value of each period as a faint
line. Related lines share a graph (CPU and GPU temperature; CPU and GPU power). The summary stays at the bottom while
the graphs scroll. Each window has a summary (lowest, average, highest, and the value it stayed
under 95% of the time), the CPU and Memory windows list the busiest processes over the period (double-click for that
process's history), and **Export…** saves the numbers as a CSV file.

**The log.** While Daisy's App runs (also in the tray) it records, every 10 seconds, the average and peak of every
graph plus the 5 busiest processes by CPU and by memory: about 1 MB a day in
`%LOCALAPPDATA%\DaisysApp\logs\performance`. With LibreHardwareMonitor, its temperatures, fans and power are logged
the same way (`sensors-<date>.csv`, a few MB a day); voltages, clocks and loads are live only. Settings → Performance
turns the log off, sets how long it's kept (7 days to a
year, 30 by default), opens the folder or deletes it.

**Where the numbers come from.** Windows' performance counters (the same as Task Manager and Performance Monitor);
NVIDIA's driver for the GPU's load, clock, power, fan, temperature and memory (other graphics cards get load and
memory from Windows, without temperature or power); and **LibreHardwareMonitor** for everything else. Windows doesn't
give programs the CPU's, motherboard's or fans' sensors without a driver; LibreHardwareMonitor has one. Run it with its
web server on (Options → Remote Web Server → Run, port 8085 — the same server Zabbix and similar tools read) and the
CPU appears on the Temperatures tile, the Hardware sensors section appears, and both go in the log. A different
address can be set in Settings → Performance. For graphics cards other than NVIDIA, the GPU's temperature, power and
fan come from LibreHardwareMonitor too.
Memory and disk sizes are in GB as Windows counts them (1 GB = 1024³ bytes).
