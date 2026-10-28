# Support

*A module of [Daisy's App](../../../README.md): the Support tab, tools for working out what's wrong. Switch it on or
off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Support\` next to
`DaisysApp.exe`.*

Two sub-modules, each on its sub-tab at the top of the tab (Networking Tools with its own tools on a second row of sub-tabs):

| Sub-module | Its tools | Guide |
|---|---|---|
| **Networking Tools** | Connection · Ping · Speed Test · Traceroute · Wi-Fi · Port Scan · Host Scan · Events: troubleshooting a network connection, especially one that's flaky, and looking a device over | [Networking/README.md](Networking/README.md) |
| **Stress Tests** (since 3.22.0) | CPU, memory, storage, GPU and video tests that score the PC and check their work, so a run shows what breaks; logged (Standard or Maximum), kept, exported and compared with another run or PC | [StressTests/README.md](StressTests/README.md) |

Each tool is a sub-module of its own in Settings → General → Modules (under Support, each needing Networking Tools):
one switched off isn't made at all, from the next start.

Settings → Support → Networking Tools has the tools' settings (Ping's and Speed Test's logging, and the rest; see the
guide). The settings are in `%APPDATA%\DaisysApp\Networking.json`; `Support.json` remembers which sub-tab was showing.
The log is `Support-<date>.log`; the tools' own logs (pings, speed tests, events) are in
`%LOCALAPPDATA%\DaisysApp\logs\network\`, and Traceroute's map pieces in `%LOCALAPPDATA%\DaisysApp\MapTiles\` (both
listed in Settings → General → Storage).

Stress Tests' settings are in `%APPDATA%\DaisysApp\StressTests.json`, its runs in `%LOCALAPPDATA%\DaisysApp\logs\stresstests\`
(Settings → General → Storage), and it logs to the Support log too.

**Code:** `SupportApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, adding Networking Tools and Stress
Tests); Networking Tools' code is in `Networking/`, namespace `DaisysApp.Applets.Support.Networking`; Stress Tests' in
`StressTests/`, namespace `DaisysApp.Applets.Support.StressTests` (Direct3D 11 and Media Foundation through Vortice,
and DaisysApp.Shared's hardware readings). The speed test itself is Core's
(`Shared/Network/SpeedTest.cs`, shared with Hardware → Performance's Speed Test card), and the graphs and widgets are
Core's (`LineGraph`, `Widget`). The translations are in `lang/`.
