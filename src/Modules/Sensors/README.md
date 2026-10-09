# Sensors

*A module of [Daisy's App](../../../README.md): the Sensors tab. Switch it on or off in Settings → General → Applets.
Its code is in this folder; it's installed to `modules\Sensors\` next to `DaisysApp.exe`.*

Every sensor **LibreHardwareMonitor** reports — temperatures, fans, voltages, power, clocks, loads — grouped by
hardware (motherboard, CPU, each memory module, graphics card, each drive), with each one's value now and its minimum
and maximum since LibreHardwareMonitor started. Chips at the top filter by kind (Temperatures, Fans, Power, Voltages,
Clocks, Load, Other), with how many of each there are.

**Click a sensor for its history**, or a hardware name for all of its sensors of that kind on one graph (sensors with
different units, e.g. a fan's RPM and its control %, get a graph each). The history window shows the last 10 minutes
live, or the last hour, 6 hours, 24 hours, 7 days, 30 days, today, yesterday or any logged day; hover over a graph to
read its values, click a name in the legend to pick out its line (again for all), and **Show peaks** adds each period's
highest as a faint line. Under the graphs: each sensor's lowest, average and highest in the period.

## Getting the sensors

Windows doesn't give programs the CPU's, motherboard's, memory's or fans' sensors without a hardware-monitoring
driver. LibreHardwareMonitor (free, open source) has one, so Daisy's App reads its numbers while it's running with its
web server on:

1. [Download LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest) and run it
   (as administrator, so its driver can read everything).
2. In LibreHardwareMonitor: **Options → Remote Web Server → Run** (port 8085).
3. Optionally, Options → **Run On Windows Startup** and **Start Minimized**, so it's always there.

Without it, the tab shows a banner with **Get more sensor data…**, which walks through these steps and turns green as
soon as LibreHardwareMonitor answers. It's asked every 2 seconds while it answers, and every 15 seconds while it
doesn't. If you changed its port, set the address in Settings → Sensors.

The app has one connection to LibreHardwareMonitor, which the Performance tab shares: its **Temperatures** tile shows
the CPU's temperature and power from it (and, for graphics cards other than NVIDIA, the GPU's).

## The log

Every 10 seconds, the average and peak of each **temperature, fan and power** sensor, for the history windows'
longer periods: `sensors-<date>.csv` (a column pair per sensor) and `sensors.json` (what each column is), in
`%LOCALAPPDATA%\DaisysApp\logs\performance\` — where they were before sensors had their own tab, so older history
carries on. A few MB a day. Voltages, clocks and loads are live only (the last 10 minutes).

## Settings (Settings → Sensors)

- **LibreHardwareMonitor web server:** its address (`http://localhost:8085` unless you changed the port), whether it's
  answering now, and **How to set it up…**.
- **Log:** on or off, how long it's kept (7 days to a year, 30 by default), open the folder, delete it.

Saved in `%APPDATA%\DaisysApp\Sensors.json`. The first time, the address and log settings are taken from the
Performance tab's settings, where they used to be.
