# Network

*A module of [Daisy's App](../../../README.md): the Network tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Network\` next to `DaisysApp.exe`.*

One tool, on its sub-tab at the top of the tab (Logs was the second until 3.12.9, when it went to the [Tools](../Tools/README.md) tab):

| Tool | What it does | Guide |
|---|---|---|
| **Zabbix** | Your Zabbix server's hosts as a grid of boxes; each host's page with Performance-style tiles, history windows, graphs of several values together, problems and every item; widgets for any of them. Read-only | [Zabbix/README.md](Zabbix/README.md) |

Settings → Network has Zabbix's page: **Use Zabbix** and **Hosts** side by side, the **Server** under them. Zabbix can be switched off by itself, for anyone without
a Zabbix server: **Switch off Zabbix** in its setup step, or **Use Zabbix** in Settings → Network → Zabbix. Off, its
sub-tab and widgets go and nothing is read.

Zabbix keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a module of its own (before 0.39):
`Zabbix.json`; `Network.json` remembers which sub-tab was showing. Their log is `Network-<date>.log`.
Updating from an earlier version moves Zabbix's place among the tabs to Network; if Zabbix was switched off in
Settings → General, Network is on with Zabbix switched off.

**Code:** `NetworkApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the
tools, and hides Zabbix's sub-tab while it's off); Zabbix's code is in `Zabbix/`, namespace
`DaisysApp.Applets.Network.Zabbix`. The translations are in `lang/`.
