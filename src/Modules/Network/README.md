# Network

*A module of [Daisy's App](../../../README.md): the Network tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\Network\` next to `DaisysApp.exe`.*

Two tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Zabbix** | Your Zabbix server's hosts as a grid of boxes; each host's page with Performance-style tiles, history windows, graphs of several values together, problems and every item; widgets for any of them. Read-only | [Zabbix/README.md](Zabbix/README.md) |
| **Logs** | Daisy's App's own logs, Windows' event logs and WSL's journal, each in a tab, with filters, copy and export | [Logs/README.md](Logs/README.md) |

Settings → Network has Zabbix's page (Logs has no settings). Zabbix can be switched off by itself, for anyone without
a Zabbix server: **Switch off Zabbix** in its setup step, or **Use Zabbix** in Settings → Network → Zabbix. Off, its
sub-tab and widgets go and nothing is read; Logs stays.

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a module of its own (before 0.39):
`Zabbix.json` and `Logs.json`; `Network.json` remembers which tool was showing. Their log is `Network-<date>.log`.
Updating from an earlier version moves Zabbix's place among the tabs to Network; if Zabbix was switched off in
Settings → General, Network is on (for Logs) with Zabbix switched off.

**Code:** `NetworkApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the
tools, and hides Zabbix's sub-tab while it's off); each tool's code is in its folder, `Zabbix/` and `Logs/`, with its
own namespace (`DaisysApp.Applets.Network.Zabbix` …). The translations for both are in `lang/`.
