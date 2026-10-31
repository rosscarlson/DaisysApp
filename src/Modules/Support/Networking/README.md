# Support → Network Tools

*A sub-module of [Support](../README.md): tools for troubleshooting a network connection, especially one that's
"flaky" (fine one minute, dropping the next). Each tool is on a sub-tab of its own, and each can be switched off in
Settings → General → Modules.*

| Tool | What it does |
|---|---|
| [Connection](#connection) | This PC's connection: adapter, speed, addresses, Wi-Fi link, public address, error counters, every adapter |
| [Ping](#ping) | Response times to your router, DNS servers and anything else, live and logged, with loss, jitter and outages |
| [Speed Test](#speed-test) | Download, upload and latency, now or on a schedule, logged |
| [Traceroute](#traceroute) | Every router on the way to a host: a table with names, loss and times, and a map |
| [Wi-Fi](#wi-fi) | The Wi-Fi access points around you, on a graph of channels by signal (as Android's Wi-Fi analysers show them) and over time |
| [Port Scan](#port-scan) | Which TCP ports a host answers on, what each is, and a link to more |
| [Host Scan](#host-scan) | Everything about one device: what it is, its system, its open ports and services, and what's worth fixing |
| [Events](#events) | A log of what went wrong and when: drops, disconnects, address and Wi-Fi changes, failed speed tests |

Ping, Speed Test and Events keep working in the background while the app runs (each has its own on / off); the other
tools only do anything while their tab is showing.

**Copying:** every IP address and host name can be selected and copied (Ctrl+C), and a right-click on it, or on a
table's row, has **Copy IP** and **Copy Host** (one of each for every address and name in it).

## Connection

The adapter Windows sends internet traffic through (its route to 8.8.8.8): its name and type, link speed, IPv4 and
IPv6 addresses, gateway, DNS servers, DHCP (and its server), MTU and MAC. When it's Wi-Fi, a **Wi-Fi** card: the
network, the access point (BSSID), signal (percent and dBm, red under -75 dBm), channel, band and width, the Wi-Fi
standard, receive and send rates, and the security. **Public Address**: your address on the internet, your provider,
its network (AS) and roughly where it is (from ip-api.com; ↻ looks it up again). **Errors**: packets and data sent and
received, packets with errors and discarded since the adapter started, and how many came while you've been watching
(red when any have: a bad cable, port or driver shows here). **Adapters**: every adapter, up or not. Refreshed every
2 seconds while it's showing.

## Ping

Like Hardware → Performance's Network Tests card, made bigger. Every **target** is tested at once each round: a
**Ping**, a **DNS** lookup (a name asked of that DNS server directly, so Windows' cache doesn't answer it, timed until
its answer), or a **TCP** connection to a port (for hosts that don't answer pings). The first run's targets are your
router, your DNS servers, Cloudflare (1.1.1.1), Google (8.8.8.8) and a DNS lookup on your first DNS server; the gear
edits them (**Add Router**, **Add DNS Servers**, **Defaults**), each with the time (ms) over which it shows red.

The card: each target's latest time (red when it failed, with why, or is over its level), its average and worst, and
any loss and outages, beside a graph of them all over the time picked (**5 min** to **24 h**; a long span shows each
stretch's average). Click a target to highlight its line. **Statistics** has, for the same span: sent, lost, average,
best, worst, jitter (the average change between answers in a row), outages (3 or more missed in a row) and the
longest. The **Ping** box stops everything (nothing is sent). Right-click the card for **Make Widget**: the targets'
times and the graph, on the desktop (its edit strip picks 1 minute to 1 hour).

**Logging** (Settings → Support → Network Tools → Ping): **Log every round** writes each round (time, target, ms
or why it failed) to `logs\network\ping-<date>.csv`; **Every** is how often a round runs (1 second to a minute, which
is also how often the card updates); **For** stops logging by itself after 1 hour to 7 days (or Until switched off);
**Keep files** deletes older ones (7 days to a year). The card's header says when logging is on and until when.

## Speed Test

Hardware → Performance's speed test (the same code, Core's): a few requests for latency, then downloads on several
connections at once, then uploads, each for the test length, against the nearest of speedtest.net's servers that
answers (Cloudflare's if none does). **Run Now** (Stop while it runs), the last result in large figures (orange or red
below the levels set, and the card's border with them), when it ran and the server, when the next one is due, a graph
of the results over **24 h**, **7 days** or **30 days**, and **Results**: every test (failures too, with why). Right-click
the card for **Make Widget**.

**Logging** (the card's gear, or Settings → Support → Network Tools → Speed Test): **Test on a schedule**, **Every** 5 minutes to a day,
**For** (as Ping's), the **Test length** (Run Now's too) and the orange / red levels. Every result is kept 30 days in
`logs\network\speedtest.csv` (Performance's card keeps its own). **Mind the data:** each test fills the connection for
twice its length.

## Traceroute

Type a host (or pick a recent one, or a button: Google DNS, Cloudflare, google.com, amazon.com) and **Trace**: every
distance from 1 hop up is pinged at once each round, and each router answers from its distance. Three rounds, or with
**Repeat** one a second until **Stop** (as mtr does), so loss and times add up. A click on a hop shows it on the map (in orange), and a click on a marker picks its hop in the table. A **double-click**
on a hop (or right-click → **Details…**) opens everything about its address: its figures, its name and whether the name
leads back to it, where it is on a map (place, postcode, time zone), its network (provider, organisation, AS, whether
it's a mobile, VPN / proxy or hosting address, from ip-api.com), its registration (RDAP, the registries' WHOIS: network
name, range, owner, abuse contact, dates) and links to more (Hurricane Electric BGP, PeeringDB, ipinfo.io, AbuseIPDB,
Shodan, Censys, VirusTotal, ARIN).

The table: hop, address, **name**
(reverse lookup), loss, sent, last, average, best, worst, jitter (how much its times vary), **location** (city, region,
country) and **network** (AS and provider). The status line says whether the host was reached and where loss starts
(a router that drops only its own answers, with the hops after it fine, isn't counted: many routers don't bother
answering).

**Map**: the route on OpenStreetMap, from your provider's place ("You") through each located hop, hops at one place as
one marker ("3–5"). It's rough: an address's location is where its owner registered it (from ip-api.com), not where
the router is. Drag to move, the wheel to zoom, the button to fit the route again. Map pieces are downloaded from
OpenStreetMap as they're needed and kept 30 days.

## Wi-Fi

Three cards side by side (since 3.22.2). **Access Points**: every one the picked **Wi-Fi adapter** hears (the
drop-down, when there's more than one), strongest first: network, signal (dBm), channel and band, the one you're
connected to ticked. Pick one for its details under the table (access point, signal in dBm and percent, channel, band
and width, Wi-Fi standard, security: Open, WEP, WPA, WPA2, WPA3, Enterprise), highlighted on both graphs (the graph
switches to its band). **Auto** scans every 10 seconds while the tab shows; **Scan** now.

**Networks**: one band's channels (**2.4 GHz**, **5 GHz** or **6 GHz**) across and signal (dBm) up: each access point
a hump as wide as its channel (20 to 160 MHz, read from what it broadcasts) and as tall as its signal, named at the
top; the one you're connected to is bold. 5 and 6 GHz draw only the stretches of channels in use, side by side with a
break (`//`) between, so each is wide enough to see; names that would sit on top of each other (access points on one
channel with much the same signal) are moved below or above, out of each other's way. Under it, the **least crowded**
channels for that band. **Signal**: each of the strongest ones' signal over the last 5 minutes.

Since Windows 11 24H2, Windows lists Wi-Fi networks to programs only with **Let desktop apps access your location** on
(Settings → Privacy & security → Location); without it the tab says so and offers the setting.

## Port Scan

Type a host (your router and this PC are in the list) and **Scan**: each port is tried with a TCP connection, many at
once: **open** (it accepted), **closed** (refused) or **no answer** (a firewall dropped it). Ports: **Common** (about
100 well-known ones), **1–1024**, **All**, or **Custom** (`22, 80, 8000-8100`). Open ports are listed with their
service, what it is, the **banner** (the first line it sends, if it sends one within a second: an SSH or FTP server's
version, say), the time to connect, and **Details**: the port's page at speedguide.net. **Show Closed** lists the
rest too. A **double-click** on a port (or right-click → **Details…**) looks closer at it, as Host Scan does (below):
what's really speaking on it, its software and version, its certificate, its settings and findings, what it said, and
links (SpeedGuide, IANA, the NVD's vulnerabilities for its version). Settings: how long to wait for an answer (600 ms)
and how many ports at once (200). Only scan your own devices, or ones you're allowed to.

## Host Scan

Everything that can be found out about one device, for hardening your own (and testing an IDS against them): type a host
(the drop-down has recent ones, your router and the devices this PC has talked to lately) and **Scan**. Ports:
**Common** (the common list and the ports phones, TVs, speakers and smart-home devices use), **1–1024** or **All**. **Try
without a password** also tries getting in the ways attackers try first (anonymous FTP, SNMP's default communities
"public" and "private", Redis, MQTT). **nmap**: when nmap is installed (nmap.org), it runs alongside (version detection,
default scripts, and OS detection when it's allowed to). Only scan devices you own or may test.

What it asks: ping (time and TTL), ARP (MAC address and its maker, from macvendors.com), reverse DNS, NetBIOS names,
mDNS (Bonjour: its name, services and what they say, such as Apple models and Cast devices), UPnP (its description:
maker, model, serial), SNMP (system description, name, location, contact, uptime), then a TCP scan and a closer look at
every open port: web servers (status, server, title, login, cookies, security headers; Chromecast, Roku and Docker's
own pages), TLS (version, cipher, certificate: names, issuer, dates, key, signature), SSH (version, system, key
exchange, ciphers and MACs), FTP, mail, Telnet, SMB (version, signing, SMB 1, and from NTLM: computer name, domain and
Windows' version and build, or Samba), Remote Desktop (NLA, computer name), VNC (security types), databases, Redis,
Memcached, MQTT, printers (PJL model), Android debugging and DNS (`version.bind`).

The report: **what it is** (router, printer, TV, phone, tablet, computer, laptop, server, NAS, camera, smart-home
device, game console or speaker), how sure, and **why**; its name, model, system and maker; a **Summary** (open ports,
findings by level, ping); **Findings**, worst first (High, Medium, Low, Info), each with what it means and a link;
**Open Ports** as tiles (red or orange when a port has a finding; a click opens the port's details); **Identity**
(names, MAC, TTL); **What It Announces** (mDNS, UPnP, SNMP); nmap's guesses, ports and scripts; for a public address,
the same details as Traceroute's (map, network, registration, links); and the **Scan log**. **Copy Report** copies it
all as text.

**Deep Scan** (the checkbox) looks for the real address of a site that sits behind a reverse proxy or CDN (Cloudflare
and the like), from public data only. When it's on, a **Deep Scan** box appears beside the Summary. It reads the
proxied site once as a known-good answer, then gathers candidate addresses from certificate transparency logs
([crt.sh](https://crt.sh)), subdomains (crt.sh names plus a list of ones that often leak the origin — `mail.`, `ftp.`,
`cpanel.`, `direct.`, `origin.`, `dev.`…, each resolved), mail records (MX hosts and SPF networks, often the
unproxied origin), and — with keys — passive-DNS history (SecurityTrails, ViewDNS) and internet-scan data (Shodan,
Censys). The CDN's own addresses are left out, using the CDNs' published range lists (Cloudflare, Fastly, CloudFront,
fetched and kept a week, refreshed, with a built-in list for a few that don't publish one). Each candidate off the CDN
is then asked for the site directly (`https://<address>/` with the `Host:` header set to the real domain, the
certificate not checked) and scored against the known-good answer (status, page, title, server); the box lists them
ranked, with the subdomains and mail records it found and the sources it used. **It says honestly when it finds
nothing**: a hardened origin (its firewall allowing only the CDN, and Authenticated Origin Pulls) leaks nothing and no
public method recovers it. The API keys are in Settings → Support → Network Tools → Deep Scan. Only use it on domains
you own or may test.

## Events

Watches in the background (every 5 seconds, and at once when Windows says an address changed) and lists, newest first:
a Ping target **stopped answering** (3 missed in a row) and **answering again** after how long; an adapter
disconnecting, connecting or appearing, or a cable's link speed changing; the internet adapter's address, gateway or
DNS changing; Wi-Fi disconnecting, connecting, changing network, moving to another access point or channel, or its
signal going **weak** (under -75 dBm, set in Settings, back once it's 5 dBm better); a speed test failing. Problems are
marked ⚠ (and are warnings in the log). **Last 24 Hours** counts them (drops and the longest, Wi-Fi problems,
disconnects, address changes). The chips show or hide each kind; **Clear** (Confirm?) deletes them all. Kept 30 days in
`logs\network\events.csv`. The **Events** box stops watching.

## Settings

Settings → Support → Network Tools: **Ping** (log, every, for, keep files, Open Folder), **Speed Test** (on a
schedule, every, for, test length, orange / red levels: 0 turns one off), **Events** (weak Wi-Fi level), **Port
Scan and Host Scan** (wait, ports at once) and **Deep Scan** (its optional API keys: SecurityTrails, ViewDNS, Shodan,
Censys ID and secret). Saved in `%APPDATA%\DaisysApp\Networking.json` as they change.

## Code

Namespace `DaisysApp.Applets.Support.Networking`.

| File | What it does |
|---|---|
| `NetworkingApplet.cs` | The sub-module: its tools' sub-tabs (Core's `SubTabs`), each a sub-module of Support (`ToolSwitches`, needing Network Tools); starts and stops Ping, Speed Test, Events and the widgets |
| `NetworkingSettings.cs` | `Networking.json`: every tool's settings, `NetTarget` (a Ping target), `NetWidgetPlace` (a widget's place) |
| `NetworkingSettingsView.cs` | Settings → Support → Network Tools |
| `NetInfo.cs` | The internet adapter (`GetBestInterface`), gateway, DNS servers, private addresses; `Names` (reverse lookups, cached); `Geo` (ip-api.com locations, cached, a batch at a time) |
| `NetWidgets.cs` | The Ping and Speed Test widgets (Core's `Widget`): opened at start, redrawn as results come |
| `NetUi.cs` | The tools' own pieces: values (`Ms`, `Mbps`…), label/value `Fields`, selectable text and Copy IP / Copy Host (the rest is Core's `Shared/Ui`) |
| `IpDetails.cs` | An address's public details (ip-api.com, RDAP) and `IpDetailsView` (names, map, network, registration, links) |
| `Connection/ConnectionView.cs` | Connection |
| `Ping/PingMonitor.cs` | The rounds (ping through Core's `Shared/Network/Pings.cs`, one ping to an address shared with Hardware → Performance; DNS query over UDP, TCP connect), 24 hours of results, statistics, outages, the CSV log |
| `Ping/PingView.cs` | Ping's card, graph and Statistics; `Thin` (a long span averaged to ~400 points) |
| `Ping/TargetsWindow.cs` | Ping → gear: the targets |
| `SpeedTest/SpeedView.cs` | Speed Test's card and Results (the test is Core's `Shared/Network/SpeedTest.cs`) |
| `Traceroute/Tracer.cs` | The trace (every distance at once each round; `Hop`'s loss and times) |
| `Traceroute/TraceView.cs` | Traceroute's table, names and places, and the map's route |
| `Traceroute/MapView.cs` | The map: OpenStreetMap's tiles (downloaded, kept in `MapTiles`), Web Mercator, drag and wheel, markers and the route, the selected marker, a click on one |
| `Traceroute/HopWindow.cs` | A hop's details window |
| `WiFi/Wlan.cs` | Windows' Native Wi-Fi API: adapters, the connection, scans, each access point's width and security from its beacon |
| `WiFi/ChannelGraph.cs` | The channel graph: 5 and 6 GHz only where they're used (`Segments`), names kept apart (`Place`) |
| `WiFi/WiFiView.cs` | Wi-Fi's tab, three cards side by side: the table and the picked one's details, the channel graph and the least crowded channels, the signal graph; scans |
| `PortScan/Ports.cs` | Well-known ports (service and what it is), the custom list's parser, the link to more |
| `PortScan/PortScanView.cs` | Port Scan: the scan, the table |
| `PortScan/PortWindow.cs` | A port's details window (Port Scan, Host Scan) |
| `Probes/PortProbe.cs` | A TCP connection to a port, and its banner |
| `Probes/ServiceProbe.cs` | A closer look at an open port: web, TLS, SSH, FTP, mail, Telnet, RDP, VNC, databases, Redis, Memcached, MQTT, PJL, ADB, DNS |
| `Probes/Smb.cs` | SMB: version, signing, SMB 1, NTLM's names and Windows version |
| `Probes/Discovery.cs` | Ping and TTL, ARP and makers, the ARP table, NetBIOS, UPnP (SSDP and descriptions), SNMP |
| `Probes/DnsWire.cs` | DNS messages by hand: mDNS asked of one device, `version.bind`, and A / MX / TXT for Deep Scan |
| `Probes/Nmap.cs` | nmap, when it's installed: run, its XML read |
| `Probes/Findings.cs` | `Finding` (level, title, detail, link), `Fact`, `ServiceInfo` |
| `HostScan/HostScanner.cs` | Host Scan: every probe, then what the device is, its system, and the findings (`HostReport`) |
| `HostScan/HostScanView.cs` | Host Scan's tab and report |
| `HostScan/Cards.cs` | Findings, facts and raw text as cards (Host Scan, the port window) |
| `DeepScan/DeepScanner.cs` | Deep Scan: origin discovery behind a proxy / CDN — crt.sh, subdomains, mail (MX/SPF), and, with keys, SecurityTrails, ViewDNS, Shodan, Censys; each candidate asked for the site directly and scored (`DeepReport`, `DeepCandidate`, `Baseline`) |
| `DeepScan/Cdn.cs` | Which addresses are a CDN's / proxy's, from the CDNs' published range lists (Cloudflare, Fastly, CloudFront), fetched and kept a week in `cache\cdn-ranges.json`, with a built-in list for a few more |
| `DeepScan/DeepScanCard.cs` | The Deep Scan box in Host Scan's report: progress, the verdict, candidates ranked, subdomains, mail, sources and the log |
| `Events/EventMonitor.cs` | The watching: outages from Ping, adapters, addresses, Wi-Fi, speed tests; `events.csv` |
| `Events/EventsView.cs` | Events' list and Last 24 Hours |
