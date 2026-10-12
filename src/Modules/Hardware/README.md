# Hardware

*A module of [Daisy's App](../../../README.md): the Hardware tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\Hardware\` next to `DaisysApp.exe`.*

Three tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Performance** | Live graphs of CPU, GPU, memory, video memory, disk, network and temperatures, per-core load, processes and system details, network tests and a speed test, with a log and history windows | [Performance/README.md](Performance/README.md) |
| **USB Monitor** | Every device connect, disconnect and status change, as it happens, with a log file | [UsbMonitor/README.md](UsbMonitor/README.md) |
| **Sensors** | Every temperature, fan, voltage, power, clock and load sensor from LibreHardwareMonitor, with history graphs and a log | [Sensors/README.md](Sensors/README.md) |

Settings → Hardware has a sub-tab for each of them.

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a tab of its own (before 0.22):
`Performance.json`, `UsbMonitor.json` and `Sensors.json`; `Hardware.json` remembers which tool was showing. Their log
is `Hardware-<date>.log`. Updating from an earlier version moves the tabs' place, on/off state and the last tab open
to Hardware.

**Code:** `HardwareApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the
tools); each tool's code is in its folder, `Performance/`, `UsbMonitor/` and `Sensors/`, with its own namespace
(`DaisysApp.Applets.Hardware.Performance` …). The translations for all three are in `lang/`.
