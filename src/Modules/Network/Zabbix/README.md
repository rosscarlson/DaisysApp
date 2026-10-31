# Network: Zabbix

*A tool in [Daisy's App](../../../../README.md)'s [Network](../README.md) tab: its **Zabbix** sub-tab, your [Zabbix](https://www.zabbix.com) server's hosts at a glance. It only reads; nothing on the server is changed. Its code is in this folder, part of the Network module (installed to `modules\Network\` next to `DaisysApp.exe`). Before 0.39 it was a tab of its own.*

## Setting it up

The setup wizard (after installing or updating) has a Zabbix step until a server has answered. Fill in:

- **Server**: the IP address or name of the machine with Zabbix's web interface (e.g. `192.168.1.10`).
- **Port**: empty for the usual one (80, or 443 with **HTTPS**).
- **Path**: where the web interface is: `/zabbix` for most installs, `/` for Docker and the appliance. If the API isn't
  there, the usual places are tried and the one that answers is kept.
- **Accept a certificate Windows doesn't trust**, for HTTPS with a self-signed certificate.
- **Sign in with** an **API token** (Zabbix 5.4 and later: User settings → API tokens) or a **user name and
  password**. A user that can only read is enough. Both are kept encrypted for your Windows account.

**Test connection** says which Zabbix version answered and how many hosts it sees. Not using Zabbix? **Skip for now**
(it's in Settings → Network → Zabbix whenever you want it) or **Switch off Zabbix**, which hides its sub-tab and its
widgets and reads nothing (**Use Zabbix** in Settings → Network → Zabbix switches it back on). If the Network tab is
switched on later, the same step opens in its own window the first time. All of it is also in **Settings → Network →
Zabbix**, with how often to read (**Read every**, 30 seconds by default) and which hosts the grid shows.

Zabbix 5.0 and later are supported; from 6.4 the sign-in goes in an `Authorization` header, before that in the request.

## The grid

A box per host: a dot (green when Zabbix reaches it, red when it can't, grey when nothing checks it), its address and
groups, CPU and memory now and the CPU over the last hour, the number of problems in the worst one's colour (the box's
edge too, from Warning up; red when it's unreachable), and how long it's been up or what's wrong. A host Zabbix only
pings (a router, a printer) shows its response time and loss instead. Filter by name, address or group (the search),
by host group, or **Problems only**. Right-click a box to make it a widget or hide it (Settings → Network → Zabbix shows hidden
ones again). Click it for the host's page.

The boxes are grouped (since 3.1.1): an outer box per host group, headed by its name and a coloured circle per status
with how many of its hosts are in it, worst first (*3* in green: three fine; *1* in red and *2* in green: one with a
Disaster problem, two fine; colours as Zabbix's severities, red for unreachable, grey for not monitored). A host in
several groups is in each; hosts in none are under *No host group*. First comes **Alerts**: every host with a problem
or that can't be reached, worst first (unreachable, Disaster, High, Average, Warning, Information, Not classified),
copies of the boxes in their groups. The host group filter shows one group (and its hosts among the Alerts).

The groups share one grid (since 3.1.8): each is as many columns wide as it has hosts, left to right, so small groups
sit side by side; a group with more hosts than the row has room for takes what's left of the row and goes on at the
start of the next (its pieces' facing corners square, the later piece's name faded). Alerts always come first; drag a
group by its name onto another to put it there (the order is kept, `GroupOrder`). **Display Grid Size**, in Settings →
Network → Zabbix beside Server: **Columns** (0: as many ~250-wide boxes as fit) and **Rows** (that many rows share the
tab's height, the boxes stretched to fill it, never cut short; 0: as tall as they need) (`GridColumns`, `GridRows`).

## A host's page

- **Tiles** like Performance's: **CPU** (utilization, load, processors), **Memory** (utilization, in use, swap),
  **Disks** (the fullest filesystem; each one's space used, reads and writes), **Network** (all interfaces' traffic in
  and out), **Temperature** and **Ping** (response time and loss), each where the host has those items (found by the
  keys Zabbix's own templates use: `system.cpu.util`, `vm.memory.utilization`, `vfs.fs.dependent.size[…,pused]`,
  `net.if.in/out[…]`, `icmppingsec`…). Click one for its history; right-click to make it a widget.
- **New graph**: tick any of the host's values to draw them on one graph, e.g. both network cards' traffic in and out
  (four lines). Once one is ticked, values in another unit are greyed out, since they can't share its scale. The graph
  stays on the page (right-click to change it, make it a widget or delete it; double-click for its history).
- **Problems**: everything firing on the host, by severity, and for how long.
- **Details**: each interface and whether it's available (with Zabbix's error), groups, system, uptime, agent.
- **Items**: everything Zabbix collects for it, with the last value and when it was read. Double-click a number for
  its history; right-click for a widget or to copy its key.
- **Widget** puts the host's box in a widget; **Open in Zabbix** shows its latest data in the web interface;
  **Hide Device** (the last, top right; since 3.1.1) hides the host as right-click → Hide this host does, and goes
  back to the grid.

## History

The same window as Performance's: the **last hour live** (updated at each read), or the last 6 or 24 hours, 7 or 30
days, today or yesterday. Up to two days is every value Zabbix kept; longer is its hourly trends, with each hour's
highest as the peak (**Show peaks**). Click a line's name to pick it out, click the scale to set the top of graphs in
that unit, **Summary** for the lowest, average, highest and 95th percentile, **Export…** for a CSV file, and
**Widget**.

## Widgets

A host's box, a tile's graph, a graph made with New graph or one item, in a small borderless window anywhere on the
screen, like Performance's widgets: right-click one to move or stretch it, set its opacity, whether it stays on top and
how much time it shows (30 minutes to 24 hours), then **Done**. The Zabbix sub-tab lists them, to edit or delete.

## How much it does

It asks the server for anything only while something is showing: the sub-tab, a widget or a history window. Each read is
a handful of calls (hosts, problems, the boxes' values, the items of hosts being looked at, and the history that's new
since the last read). The last 24 hours of what's on screen is kept in memory; nothing is written to disk but the
settings (`%APPDATA%\DaisysApp\Zabbix.json`) and the log.

## Code

| File / folder | What it is |
|---|---|
| `ZabbixApplet.cs` | The sub-tab: setup step, first-run window, on / off |
| `ZabbixService.cs` | The connection and what's been read: hosts, boxes' values, problems, items and recent history; reads only while something's watching |
| `ZabbixSettings.cs` | `Zabbix.json` (the token and password encrypted for the Windows account) |
| `Api/ZabbixClient.cs` | Zabbix's JSON-RPC API: sign-in (token or user, 5.0–7.x), hosts, items, problems, history and trends |
| `Api/ZabbixModels.cs`, `ZUnit.cs`, `HostMetrics.cs` | Hosts, items and problems; units as Zabbix shows them; which items make the tiles |
| `Views/ZabbixView.cs`, `HostBox.cs`, `HostPage.cs` | The grid of hosts, a host's box, a host's page |
| `Views/ZabbixHistoryWindow.cs`, `GraphPickerWindow.cs`, `ScalePrompt.cs` | History windows, New graph, a graph's scale |
| `Views/ConnectionForm.cs`, `ZabbixSetupPanel.cs`, `ZabbixSettingsView.cs` | The server form, the setup step, Settings → Network → Zabbix |
| `Views/ZabbixUi.cs` | The graph legend and times (the rest is Core's `Shared/Ui`) |
| `Widgets/` | The widgets (`ZabbixWidgets`, `ZabbixWidgetWindow`: a host's box or graph in Core's `Shared/Widgets/Widget`, as Performance's) |
