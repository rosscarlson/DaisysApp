# Session notes: Support

Claude Code reads this file only when it works in this folder. Keep it under about 8 KB: what each release did is in
`CHANGELOG.md` and git (`git log -- src/Modules/Support`); here only what a session can't get from the code.

## Decisions and how things work

- **Network Scan, was Device Scan (4.2.0, asked 2026-11-05: new Network Tools sub-tab; scan a subnet, this PC's by default, group
  devices by type to find one; Full Scan opens Host Scan with the IP filled in):** `DeviceScan/`. Classification is
  Host Scan's `Conclude` (made internal) on a `HostReport` built from the quick answers (no ServiceProbe), so both tools
  agree; 4.2.0 added name rules (`NamePatterns`), more smart-home makers, router makers without admin ports skipped,
  DNS on a non-gateway = server. Sweep: SendARP on dedicated threads (64) + ping, a second ARP pass for misses; ports
  1.2 s (700 ms lost a printer's under load); makers 1/s, cached in `DeviceScan.json`; the TTL-only system guess is left
  out (its text "…printer" matched searches). **4.2.1** (user: Alexa devices in their own group; 3 phones + 3 tablets
  missing; rename to "Network Scan"): display name only, code/folder/`DeviceScan.json`/switch id stay `DeviceScan`.
  Echo: ports 55442+55443 (55443 alone: Yeelight); kind `alexa`. The missing six didn't answer
  ARP at all (asleep), but the router's DNS (via pi.hole) had their names: `PtrAsync` sends every quiet address's PTR
  query to the DNS server at once (GetHostEntry tried NetBIOS, 4 s each);
  private nets ≤ /22 only (Pi-hole limits 1,000/min). A kind of phone with any tablet name score → tablet. Tested on
  PLEX's LAN: 53 in 45 s, all four Echos (`dp-…` names are Echos too), all six. 8 stay Unknown ("Private" OUI, TTL 255).
- **Layout (3.19.0):** `SupportApplet` (AppletGroup) → `Networking/NetworkingApplet` (Core `SubTabs`; each tool
  `ToolSwitches.Declare("Support", id, title, needs: "Networking")`). "Network Tools" (3.23.0) is display-only: code
  id, namespace and `Networking.json` stay `Networking`. Logs and Tools moved here from Tools in 3.24.9
  (`ModuleMoves.ToolsToSupport`, once). The speed test is Core's `Shared/Network/SpeedTest.cs`. Toggles use
  Checked/Unchecked (`Ui.OnToggle`): UIA's Toggle raises no Click.
- **Map/places:** our own OSM tile control (`MapView`, User-Agent, attribution, tiles cached 30 days in `MapTiles`);
  ip-api.com batch (HTTP only on the free plan); RDAP via rdap.org; MAC makers api.macvendors.com.
- **Traceroute:** a warm-up round isn't counted (the first counted ~157 ms on every hop); probes `ConfigureAwait(false)`
  (timed on the UI thread they read ~200 ms). `TraceRun` is the one trace code for the button and Ping's Trace when
  down (3.34.0); `TraceLog` = `traces.jsonl`; unreached traces trimmed to last answering hop + 1. Since 3.36.3
  (user: 30+ rows, mostly empty) `TraceView.Fill` builds the table's `shown` from `hops`: a run of address-less hops is
  one `Hop` with `Hidden` = count (`NumberText` "6–30", figures blank), named "Hiding n Lines". Tested in DaisysDev
  (amazon.com: "6–30 · Hiding 25 Lines" live, "6 · Hiding 1 Line" at the end).
- **Events (3.32–3.34):** `EventMonitor.Diagnose` (kind `Cause`) runs `PingSeconds` + 1.5 s after an outage; local =
  gateway or private (not 100.64/10). `EventReport` HTML (outages from Drop events, with Cause lines). `EventsChart`.
  A Diagnosis is added on `TraceLog.Logged` for auto traces.
- **Host Scan (3.20.0):** no brute force or exploits (anonymous/default checks only, `HostLogins`); SMB dialects up to
  3.0.2; NTLM build 0 = Samba; nmap optional (-O retried without when not admin). **Deep Scan (3.23.0)**: origin
  discovery behind a CDN from public data (`Networking/DeepScan/`), candidates dialled by IP with the real Host/SNI,
  scored vs the proxied site; keyed sources skipped without a key. Never tested.
- **Wi-Fi graph (3.22.2):** 5/6 GHz shows only the channel stretches in use (`ChannelGraph.Segments`), labels placed
  off each other (`Place`).
- **Stress Tests (3.22.0, `StressTests/` README):** each test on its own thread (`TestContext`); `running.json` before
  each test recovers a crash at next start; scores = 1,000 × value / this dev PC's Release figures. Lessons: a module
  built alone with `-c Release` still goes to bin\Debug's modules (`-p:ModulesOut=`); Debug runs CPU tests at ½ (AVX
  ⅙) speed, so calibrate on Release; `DedicatedVideoMemory` → `(long)(nuint)`; `ClearUnorderedAccessView(Int4)` for
  structured buffers; open files `Asynchronous` for parallel reads; Windows' `GPU Adapter Memory` counter for all
  processes' VRAM.
- **Process Analyzer (4.1.0, asked 2026-11-04: new sub-module; running processes vs a stored database of known ones,
  unknown ones looked up/classified and stored, export to merge clients' databases, Daisy's Server as the master
  database when set up (server priority for a new client, versions tracked), details with threat/use/company, ties to
  Hardware's process history, suggestions for unnecessary/risky/heavy ones; a brief for the server's agent):**
  `ProcessAnalyzer/` (its README has the protocol). Decisions (mine): keyed by exe name (lower, no .exe), expected
  folders + publisher per reviewed entry catch impostors; history = reading Hardware's `perf-*.csv` top_cpu/top_memory
  (modules can't reference each other) plus its own per-PC figures in the DB; "look it up" = VirusTotal by hash (user's
  free key) or its page/web search, no AI; DB not sealed on the server (programs' names, not user data; the user asked
  for a master DB the server merges), one DB per server. Lessons: a static `Db { get; } = Load()` above the static JSON
  options ran with them null (seeds silently empty): Lazy. Non-admin can't OpenProcess services: NtQuerySystemInformation
  class 5 (figures) and 88 (image path by pid, NT path → QueryDosDevice), unmanaged buffer (names point into it).
  Tested: DaisysDev + harness `%TEMP%\pa\h` (scanner, seeds, merge rules); seed generator `%TEMP%\pa\gen.py`
  (known.json is the source now). Server side not written: the brief went to the user (and Downloads).

## Testing

- DaisysDev (`%TEMP%\DaisysDev`): copy `bin\Debug\…\modules\Support` in while it's stopped (closing the window only
  hides it to the tray: stop the DaisysDev process, never DaisysApp). UIA by name: `%TEMP%\aied\tr.ps1 -Tabs
  "Support","Network Tools","Traceroute" -Invoke "amazon.com" -Wait 40 -Shot x.png` (tabs by SelectionItem, buttons
  by Invoke, PrintWindow shot). Save dialogs: Win32 `WM_SETTEXT` on the Edit under FloatNotifySink, then IDOK.
- A fake unreachable target (203.0.113.1) drives Events, Diagnosis and Trace when down. Harnesses: `%TEMP%\da334\h`,
  `%TEMP%\da333\rep`, `%TEMP%\sh`/`sh2` (Stress Tests), `%TEMP%\wifih` (Wi-Fi graph).
- Another session works in this module in parallel (an uncommitted `Gpu/` tool, its strings in `lang/en.json`,
  `Support.csproj`, `SupportApplet.cs` on 2026-11-01): commit only your own files; for `en.json` stage a blob of HEAD
  plus your keys (`git hash-object -w` + `update-index --cacheinfo`) and commit through a temporary `GIT_INDEX_FILE`.

## Next

- The user to try (`docs/UNTESTED.md`): 4.2.1's Network Scan on GAMER11 (wrong groups / named Unknowns → add to
  `HostScanner.NamePatterns` or `Makers`); 4.1.0's Process Analyzer on the rig (released, tag v4.1.0: are the
  suggestions sensible? false Highs → fix `known.json` or `Classifier`); its server side (`processes` module) is for a
  DaisysServer session (brief: `C:\Users\ross\Downloads\DaisysServer-processes-brief.md`), then test sync with
  DaisysDev. Also: 3.36.3's Traceroute rows on their routes; 3.23.0's Deep Scan on a domain
  behind Cloudflare; 3.22.0's Stress Tests on the rig (references may need retuning); a real drop in Events (router-
  and internet-down texts); Wi-Fi scan (no Wi-Fi here); mouse-only bits (double-clicks, menus, map drag).
- Ideas not built (offer if asked): Stress Tests settings page, CSV export, scheduled runs; UDP port scan, WS-Discovery, an MTU test.
