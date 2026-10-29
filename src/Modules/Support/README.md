# Support

*A module of [Daisy's App](../../../README.md): the Support tab, tools for working out what's wrong. Switch it on or
off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Support\` next to
`DaisysApp.exe`.*

Four sub-modules, each on its sub-tab at the top of the tab (Network Tools with its own tools on a second row of sub-tabs):

| Sub-module | Its tools | Guide |
|---|---|---|
| **Network Tools** | Connection · Ping · Speed Test · Traceroute · Wi-Fi · Port Scan · Host Scan · Events: troubleshooting a network connection, especially one that's flaky, and looking a device over | [Networking/README.md](Networking/README.md) |
| **Stress Tests** (since 3.22.0) | CPU, memory, storage, GPU and video tests that score the PC and check their work, so a run shows what breaks; logged (Standard or Maximum), kept, exported and compared with another run or PC | [StressTests/README.md](StressTests/README.md) |
| **Logs** | Daisy's App's own logs, Windows' event logs and WSL's journal, each in a tab, with filters, copy, **Clear** (the app's logs; Confirm? first) and export of what's shown (on the Network tab before 3.12.9, the Tools tab until 3.24.9) | [Logs/README.md](Logs/README.md) |
| **Tools** | Buttons for Windows' resets and admin jobs (since 3.20.2; on the Tools tab until 3.24.9): reset the graphics driver (as Win+Ctrl+Shift+B), restart Explorer, flush DNS, renew the IP, reset the network, restart audio, sync the clock, empty the Recycle Bin, repair Windows, power (lock to restart into the BIOS), and Windows' admin tools opened | [QuickTools/README.md](QuickTools/README.md) |

Each of Network Tools' tools is a sub-module of its own in Settings → General → Modules (under Support, each needing Network Tools):
one switched off isn't made at all, from the next start.

Settings → Support → Network Tools has the tools' settings (Ping's and Speed Test's logging, and the rest; see the
guide). The settings are in `%APPDATA%\DaisysApp\Networking.json`; `Support.json` remembers which sub-tab was showing.
The log is `Support-<date>.log`; the tools' own logs (pings, speed tests, events) are in
`%LOCALAPPDATA%\DaisysApp\logs\network\`, and Traceroute's map pieces in `%LOCALAPPDATA%\DaisysApp\MapTiles\` (both
listed in Settings → General → Storage).

Stress Tests' settings are in `%APPDATA%\DaisysApp\StressTests.json`, its runs in `%LOCALAPPDATA%\DaisysApp\logs\stresstests\`
(Settings → General → Storage), and it logs to the Support log too. Logs keeps `Logs.json` (Tools has no settings) and logs to the Support log.

**Code:** `SupportApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, adding Network Tools, Stress
Tests, Logs and Tools); Network Tools' code is in `Networking/`, namespace `DaisysApp.Applets.Support.Networking`; Stress Tests' in
`StressTests/`, namespace `DaisysApp.Applets.Support.StressTests` (Direct3D 11 and Media Foundation through Vortice,
and DaisysApp.Shared's hardware readings). The speed test itself is Core's
(`Shared/Network/SpeedTest.cs`, shared with Hardware → Performance's Speed Test card), and the graphs and widgets are
Core's (`LineGraph`, `Widget`). Logs' code is in `Logs/` and Tools' in `QuickTools/` (with the Tools module's `Shared/Ui.cs` and `Sizes.cs`, linked in `Support.csproj`). The translations are in `lang/`.
