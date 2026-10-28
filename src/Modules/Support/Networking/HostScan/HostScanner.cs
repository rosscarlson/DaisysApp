using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Everything a Host Scan found about one device.</summary>
internal sealed class HostReport
{
    public string Host = "", Ip = "";
    public bool IsPrivate, IsGateway, IsThisPc;
    public DateTime When = DateTime.Now;
    public TimeSpan Took;
    public (int Ttl, double Ms)? Ping;
    public string? Mac, Vendor;
    public bool RandomMac;
    public readonly List<string> DnsNames = new();
    public List<Discovery.NbName> NetBios = new();
    public DnsWire.MdnsInfo? Mdns;
    public List<Discovery.UpnpDevice> Upnp = new();
    public (string Community, List<Fact> Facts)? Snmp;
    public readonly List<(PortResult Port, ServiceInfo Info)> Open = new();
    public int Scanned, Closed, Filtered;
    public Nmap.Result? Nmap;
    public readonly List<Finding> Findings = new();

    /// <summary>What it is: a key (<see cref="DeviceKinds"/>), how sure (percent), and why.</summary>
    public string Kind = "unknown";
    public int Confidence;
    public readonly List<string> KindWhy = new();
    public string Os = "";
    public string OsWhy = "";
    public string Name = "", Model = "";
    public readonly List<string> Log = new();

    public void Note(string text)
    {
        lock (Log) Log.Add($"{DateTime.Now:T}  {text}");
    }
}

/// <summary>
/// Host Scan: everything that can be found out about one device on the network, the ways nmap and network inventory
/// tools do it. Pings (and its TTL), ARP (its MAC and who made it), names (DNS, NetBIOS, mDNS), what it announces
/// (mDNS services, UPnP), SNMP with the default community names, a TCP port scan, then a closer look at every open port
/// (<see cref="ServiceProbe"/>), and nmap too when it's installed. From all that: what kind of device it is, its system,
/// and what's worth fixing. Only for devices you own or may test.
/// </summary>
internal static class HostScanner
{
    /// <summary>Ports devices use that the common list doesn't have: phones, TVs, speakers, smart-home devices.</summary>
    public static readonly int[] DevicePorts = [62078, 8060, 1400, 7000, 7100, 3689, 8009, 8001, 8002, 3001, 9080, 5357, 2869, 49152, 49153, 1935, 6053, 6668, 9999, 55443, 8899, 10001, 4070, 5683, 9295, 9123];

    public static List<int> PortsFor(string choice) => (choice switch
    {
        "WellKnown" => Enumerable.Range(1, 1024).Concat(Ports.Common).Concat(DevicePorts),
        "All" => Enumerable.Range(1, 65535),
        _ => Ports.Common.Concat(DevicePorts),
    }).Distinct().OrderBy(p => p).ToList();

    /// <param name="progress">What it's doing and how far along (0–1), on any thread.</param>
    public static async Task<HostReport> ScanAsync(string host, IPAddress ip, NetworkingSettings settings, Action<string, double> progress, CancellationToken ct)
    {
        var report = new HostReport { Host = host, Ip = ip.ToString(), IsPrivate = NetInfo.IsPrivate(ip), IsGateway = NetInfo.Gateway() == ip.ToString() };
        report.IsThisPc = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(ip)) || IPAddress.IsLoopback(ip);
        var sw = Stopwatch.StartNew();
        bool logins = settings.HostLogins;
        report.Note(F("Scanning {0} ({1})", host, ip));
        Logging.Log.Here.Info($"Host scan of {host} ({ip}), ports {settings.HostPorts}, logins {logins}, nmap {settings.HostNmap}");

        // nmap runs alongside (it takes longest)
        Task<Nmap.Result>? nmap = null;
        if (settings.HostNmap && Nmap.Find() is { } exe)
        {
            report.Note(T("nmap started"));
            nmap = Nmap.RunAsync(exe, ip.ToString(), settings.HostPorts == "All", ct);
        }

        // what it says about itself, all at once
        progress(T("Asking it about itself…"), 0.02);
        var ping = Timed(report, T("Ping"), Discovery.PingAsync(ip, ct), r => r is { } p ? F("{0} ms, TTL {1}", p.Ms.ToString("0.#"), p.Ttl) : T("no answer"));
        var reverse = Timed(report, T("Reverse DNS"), Names.ReverseAsync(ip.ToString()), r => r ?? T("none"));
        var netbios = report.IsPrivate ? Timed(report, "NetBIOS", Discovery.NetBiosAsync(ip, ct), r => r.Names.Count > 0 ? string.Join(", ", r.Names.Select(n => n.Name).Distinct()) : T("no answer")) : null;
        var mdns = report.IsPrivate ? Timed(report, "mDNS", DnsWire.MdnsAsync(ip, ct), r => r.Types.Count > 0 || r.HostName != null ? F("{0}, {1} services", r.HostName ?? "—", r.Services.Count) : T("no answer")) : null;
        var upnp = report.IsPrivate ? Timed(report, "UPnP", Discovery.UpnpAsync(ip, ct), r => r.Count > 0 ? string.Join(", ", r.Select(d => d.Facts.FirstOrDefault()?.Value ?? d.Server)) : T("no answer")) : null;
        var snmp = logins ? Timed(report, "SNMP", Discovery.SnmpAsync(ip, ct), r => r is { } s ? F("answers to \"{0}\"", s.Community) : T("no answer")) : null;

        // the ports
        var ports = PortsFor(settings.HostPorts);
        report.Scanned = ports.Count;
        int done = 0, timeout = Math.Clamp(settings.ScanTimeoutMs, 100, 10000);
        var open = new List<PortResult>();
        using (var gate = new SemaphoreSlim(Math.Clamp(settings.ScanParallel, 1, 1000)))
        {
            var last = Stopwatch.StartNew();
            await Task.WhenAll(ports.Select(async port =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var r = await PortProbe.ProbeAsync(ip, port, timeout, ct, 1500);
                    lock (open)
                    {
                        done++;
                        if (r.State == PortState.Open) open.Add(r);
                        else if (r.State == PortState.Closed) report.Closed++;
                        else report.Filtered++;
                        if (last.ElapsedMilliseconds > 150) { last.Restart(); progress(F("Ports: {0} of {1} · {2} open", done, ports.Count, open.Count), 0.05 + 0.5 * done / ports.Count); }
                    }
                }
                finally { gate.Release(); }
            }));
        }
        open.Sort((a, b) => a.Port.CompareTo(b.Port));
        report.Note(F("Ports: {0} tried, {1} open ({2}), {3} closed, {4} no answer", ports.Count, open.Count, string.Join(", ", open.Select(o => o.Port)), report.Closed, report.Filtered));

        // a closer look at each open port
        int looked = 0;
        using (var gate = new SemaphoreSlim(6))
        {
            var results = await Task.WhenAll(open.Select(async p =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    progress(F("Looking at port {0}…", p.Port), 0.55 + 0.3 * looked / Math.Max(open.Count, 1));
                    var s = Stopwatch.StartNew();
                    var info = await ServiceProbe.ProbeAsync(ip, host, p, logins, ct);
                    report.Note(F("Port {0}: {1} ({2:0.0} s)", p.Port, string.Join(" ", new[] { info.Protocol, info.ProductText }.Where(x => x.Length > 0).DefaultIfEmpty(T("not recognised"))), s.Elapsed.TotalSeconds));
                    Interlocked.Increment(ref looked);
                    return (p, info);
                }
                finally { gate.Release(); }
            }));
            report.Open.AddRange(results);
        }

        progress(T("Gathering answers…"), 0.88);
        report.Ping = await ping;
        if (await reverse is { } name) report.DnsNames.Add(name);
        if (netbios != null) { var nb = await netbios; report.NetBios = nb.Names; report.Mac ??= nb.Mac; }
        if (mdns != null) report.Mdns = await mdns;
        if (upnp != null) report.Upnp = await upnp;
        if (snmp != null) report.Snmp = await snmp;
        if (report.IsPrivate && !report.IsThisPc && Discovery.Mac(ip) is { } mac) report.Mac = mac;
        if (report.Mac != null)
        {
            report.RandomMac = Discovery.IsRandom(report.Mac);
            if (!report.RandomMac) report.Vendor = await Timed(report, T("Maker (MAC)"), Discovery.VendorAsync(report.Mac, ct), r => r ?? T("unknown"));
        }
        if (nmap != null)
        {
            progress(T("Waiting for nmap…"), 0.92);
            report.Nmap = await nmap;
            report.Note(report.Nmap.Error != null ? F("nmap: {0}", report.Nmap.Error.Split('\n')[0]) : F("nmap: {0} ports, {1} system guesses, {2} scripts", report.Nmap.Ports.Count, report.Nmap.Os.Count, report.Nmap.Scripts.Count));
        }

        Conclude(report);
        report.Took = sw.Elapsed;
        report.Note(F("Done in {0}", Ui.Duration(sw.Elapsed)));
        Logging.Log.Here.Info($"Host scan of {host}: {report.Kind} ({report.Confidence}%), {report.Os}, {report.Open.Count} open, {report.Findings.Count} findings");
        progress(T("Done"), 1);
        return report;
    }

    private static async Task<TR> Timed<TR>(HostReport report, string what, Task<TR> task, Func<TR, string> say)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await task;
            report.Note($"{what}: {say(r)} ({sw.Elapsed.TotalSeconds:0.0} s)");
            return r;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            report.Note($"{what}: {ex.Message}");
            return default!;
        }
    }

    // ---------------------------------------------------------------- what it is

    /// <summary>The kinds of device, each with its name and icon.</summary>
    public static readonly Dictionary<string, (string Name, string Icon)> DeviceKinds = new()
    {
        ["router"] = (T("Router / network equipment"), ""),
        ["printer"] = (T("Printer"), ""),
        ["tv"] = (T("TV / streaming device"), ""),
        ["phone"] = (T("Phone"), ""),
        ["tablet"] = (T("Tablet"), ""),
        ["computer"] = (T("Computer"), ""),
        ["laptop"] = (T("Laptop"), ""),
        ["server"] = (T("Server"), ""),
        ["nas"] = (T("Network storage (NAS)"), ""),
        ["camera"] = (T("Camera"), ""),
        ["smarthome"] = (T("Smart home device"), ""),
        ["console"] = (T("Game console"), ""),
        ["speaker"] = (T("Speaker"), ""),
        ["unknown"] = (T("Unknown device"), ""),
    };

    private static readonly (string Pattern, string Kind, int Score)[] Makers =
    [
        (@"netgear|tp-link|tp link|asustek|linksys|ubiquiti|mikrotik|routerboard|cisco|arris|technicolor|sagemcom|avm gmbh|eero|juniper|aruba|ruckus|zyxel|d-link|actiontec|calix|commscope|plume|nokia|fortinet|sonicwall|draytek|tenda|netgate", "router", 3),
        (@"brother|seiko epson|epson|canon|xerox|lexmark|kyocera|ricoh|konica|sharp corp|oki electric", "printer", 4),
        (@"hewlett packard|hp inc", "printer", 2),
        (@"synology|qnap|western digital|buffalo|asustor|drobo", "nas", 4),
        (@"espressif|tuya|shelly|allterco|itead|signify|philips lighting|ecobee|nest labs|ring llc|wyze|smartthings|lutron|belkin|wemo|ikea|aqara|lumi united|sonoff|govee|meross|wiz connected|leviton|chamberlain|myq", "smarthome", 4),
        (@"hikvision|hangzhou|dahua|axis comm|reolink|amcrest|foscam|ubiquiti.*protect|arlo|eufy", "camera", 4),
        (@"sonos|bose|harman|bang & olufsen|denon|marantz|yamaha|sound united", "speaker", 4),
        (@"roku|vizio|tcl|hisense|lg electronics|lg innotek|funai|sharp|panasonic|philips tv|tp vision|universal electronics", "tv", 3),
        (@"nintendo|sony interactive", "console", 6),
        (@"raspberry pi", "computer", 3),
        (@"intel corp|realtek|micro-star|dell|lenovo|gigabyte|asrock|liteon|azurewave|rivet|killer|hon hai|foxconn|quanta|wistron|pegatron|compal|framework", "computer", 3),
        (@"murata|samsung|xiaomi|oneplus|motorola|huawei|oppo|vivo|google|htc|zte|nothing tech", "phone", 2),
        (@"amazon", "smarthome", 2),
    ];

    private static readonly (string Service, string Kind, int Score)[] MdnsServices =
    [
        ("_googlecast._tcp", "tv", 5), ("_androidtvremote", "tv", 6), ("_androidtvremote2", "tv", 6), ("_nvstream", "tv", 4), ("_amzn-wplay", "tv", 3), ("_airplay._tcp", "tv", 2),
        ("_raop._tcp", "speaker", 2), ("_spotify-connect", "speaker", 2), ("_sonos", "speaker", 6),
        ("_ipp._tcp", "printer", 6), ("_ipps._tcp", "printer", 6), ("_printer._tcp", "printer", 6), ("_pdl-datastream", "printer", 6), ("_scanner._tcp", "printer", 4), ("_uscan._tcp", "printer", 4),
        ("_hap._tcp", "smarthome", 4), ("_hap._udp", "smarthome", 4), ("_matter._tcp", "smarthome", 3), ("_matterc._udp", "smarthome", 4), ("_esphomelib", "smarthome", 6), ("_hue._tcp", "smarthome", 5), ("_home-assistant", "smarthome", 6), ("_miio", "smarthome", 5), ("_shelly", "smarthome", 6),
        ("_apple-mobdev2", "phone", 4), ("_companion-link", "phone", 1),
        ("_smb._tcp", "computer", 2), ("_afpovertcp", "computer", 1), ("_rfb._tcp", "computer", 2), ("_ssh._tcp", "computer", 1), ("_sftp-ssh", "computer", 1), ("_workstation", "computer", 2), ("_adisk._tcp", "nas", 3),
    ];

    private static readonly (int Port, string Kind, int Score, string Why)[] PortKinds =
    [
        (62078, "phone", 8, "iPhone / iPad sync (62078)"), (9100, "printer", 4, "raw printing (9100)"), (631, "printer", 3, "IPP printing (631)"), (515, "printer", 3, "LPD printing (515)"),
        (8060, "tv", 7, "Roku (8060)"), (8008, "tv", 2, "Chromecast (8008)"), (8009, "tv", 3, "Cast (8009)"), (8001, "tv", 4, "Samsung TV (8001)"), (8002, "tv", 4, "Samsung TV (8002)"), (3001, "tv", 2, "LG webOS (3001)"), (7000, "tv", 2, "AirPlay (7000)"),
        (1400, "speaker", 7, "Sonos (1400)"), (3389, "computer", 4, "Remote Desktop (3389)"), (445, "computer", 2, "file sharing (445)"), (135, "computer", 2, "Windows RPC (135)"), (139, "computer", 1, "NetBIOS (139)"), (5357, "computer", 2, "network discovery (5357)"),
        (554, "camera", 4, "RTSP video (554)"), (8291, "router", 7, "MikroTik Winbox (8291)"), (53, "router", 3, "DNS (53)"), (5000, "nas", 1, "Synology (5000)"), (5001, "nas", 1, "Synology (5001)"),
        (6053, "smarthome", 6, "ESPHome (6053)"), (6668, "smarthome", 5, "Tuya (6668)"), (9999, "smarthome", 3, "TP-Link Kasa (9999)"), (55443, "smarthome", 5, "Yeelight (55443)"), (8123, "smarthome", 5, "Home Assistant (8123)"), (5683, "smarthome", 3, "CoAP (5683)"),
        (32400, "server", 3, "Plex (32400)"), (3306, "server", 2, "MySQL (3306)"), (5432, "server", 2, "PostgreSQL (5432)"), (2375, "server", 3, "Docker (2375)"), (9090, "server", 2, "Cockpit (9090)"), (10000, "server", 2, "Webmin (10000)"),
    ];

    /// <summary>What kind of device it is, its system, its name and model, and every finding, from all the answers.</summary>
    private static void Conclude(HostReport r)
    {
        var score = new Dictionary<string, int>();
        void Add(string kind, int points, string why)
        {
            score[kind] = score.GetValueOrDefault(kind) + points;
            if (!r.KindWhy.Contains(why)) r.KindWhy.Add(why);
        }
        var models = new List<string>();
        var osHints = new List<(string Os, string Why, int Weight)>();

        if (r.IsThisPc) Add("computer", 20, T("It's this PC"));
        if (r.IsGateway) Add("router", 12, T("It's your router (the gateway)"));

        // its maker
        if (r.RandomMac && !r.IsGateway) { Add("phone", 3, T("Random MAC address (phones and laptops use one per network)")); score["laptop"] = score.GetValueOrDefault("laptop") + 1; }
        if (r.Vendor is { } vendor)
            foreach (var (pattern, kind, points) in Makers)
                if (Regex.IsMatch(vendor, pattern, RegexOptions.IgnoreCase)) { Add(kind, points, F("Made by {0}", vendor)); break; }

        // what it announces
        foreach (var d in r.Upnp)
        {
            string maker = d.Facts.FirstOrDefault(f => f.Label == T("Maker"))?.Value ?? "", model = d.Facts.FirstOrDefault(f => f.Label == T("Model"))?.Value ?? "";
            if (model.Length > 0) models.Add($"{maker} {model}".Trim());
            foreach (var t in d.DeviceTypes)
                switch (t)
                {
                    case "InternetGatewayDevice" or "WANDevice" or "WANConnectionDevice": Add("router", 6, F("UPnP: {0}", t)); break;
                    case "MediaRenderer" when Regex.IsMatch(maker, "sonos|bose|denon|yamaha|harman", RegexOptions.IgnoreCase): Add("speaker", 5, F("UPnP: {0}", t)); break;
                    case "MediaRenderer": Add("tv", 4, F("UPnP: {0}", t)); break;
                    case "ZonePlayer": Add("speaker", 6, F("UPnP: {0}", t)); break;
                    case "Printer": Add("printer", 6, F("UPnP: {0}", t)); break;
                    case "MediaServer": Add("nas", 1, F("UPnP: {0}", t)); break;
                }
            if (d.Services.Any(s => s.Contains("dial", StringComparison.OrdinalIgnoreCase))) Add("tv", 5, T("UPnP: DIAL (casting to a TV)"));
            foreach (var (pattern, kind, points) in Makers)
                if (maker.Length > 0 && Regex.IsMatch(maker, pattern, RegexOptions.IgnoreCase)) { Add(kind, points, F("UPnP maker: {0}", maker)); break; }
            if (Regex.IsMatch(d.Server, "windows", RegexOptions.IgnoreCase)) osHints.Add(("Windows", "UPnP", 1));
        }
        if (r.Mdns is { } m)
        {
            foreach (var t in m.Types)
                foreach (var (service, kind, points) in MdnsServices)
                    if (t.StartsWith(service, StringComparison.OrdinalIgnoreCase)) Add(kind, points, F("Offers {0}", t.Replace(".local", "")));
            foreach (var (type, instance, _, texts) in m.Services)
            {
                string? V(string key) => texts.FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..];
                // (Samba says "MacSamba", so Macs show its shares as a Mac's)
                if ((V("model") ?? V("rpMd") ?? V("am")) is { Length: > 0 } apple && apple != "MacSamba") AppleModel(apple, Add, models, osHints);
                if (V("md") is { Length: > 0 } cast)
                {
                    models.Add(cast);
                    if (Regex.IsMatch(cast, "nest mini|home mini|nest audio|google home|nest hub", RegexOptions.IgnoreCase)) Add(Regex.IsMatch(cast, "hub", RegexOptions.IgnoreCase) ? "smarthome" : "speaker", 6, F("Cast model: {0}", cast));
                    else Add("tv", 5, F("Cast model: {0}", cast));
                }
                if (V("fn") is { Length: > 0 } friendly && r.Name.Length == 0) r.Name = friendly;
                if (V("ty") is { Length: > 0 } printer) { models.Add(printer); Add("printer", 4, F("Printer: {0}", printer)); }
                if (V("usb_MDL") is { Length: > 0 } usb) models.Add(usb);
                if (V("osxvers") is { Length: > 0 } osx) osHints.Add(($"macOS ({osx})", "mDNS", 5));
            }
            if (m.Hinfo is { Length: > 0 } hinfo) osHints.Add((hinfo, "mDNS HINFO", 3));
        }
        if (r.NetBios.Count > 0)
        {
            Add("computer", 2, T("NetBIOS names (Windows or Samba)"));
            if (r.Name.Length == 0 && r.NetBios.FirstOrDefault(n => n.Suffix == 0 && !n.Group) is { } nb) r.Name = nb.Name;
        }
        if (r.Snmp is { } snmp)
        {
            string descr = snmp.Facts.FirstOrDefault()?.Value ?? "";
            if (Regex.IsMatch(descr, "printer|laserjet|officejet|jetdirect|epson|brother|canon|kyocera|xerox|lexmark|ricoh", RegexOptions.IgnoreCase)) Add("printer", 5, T("SNMP says it's a printer"));
            else if (Regex.IsMatch(descr, "routeros|cisco ios|junos|edgeos|edgeswitch|procurve|switch|router|openwrt|dd-wrt|fortigate|pfsense|vyos|unifi", RegexOptions.IgnoreCase)) Add("router", 5, T("SNMP says it's network equipment"));
            else if (Regex.IsMatch(descr, "synology|qnap|diskstation", RegexOptions.IgnoreCase)) Add("nas", 5, T("SNMP says it's a NAS"));
            else if (Regex.IsMatch(descr, "windows", RegexOptions.IgnoreCase)) Add("computer", 3, T("SNMP says it's Windows"));
            string snmpOs = SnmpSystem(descr, snmp.Facts.FirstOrDefault(f => f.Label == T("Name"))?.Value ?? "");
            if (snmpOs.Length > 0) osHints.Add((snmpOs, "SNMP", 6));
        }

        // its ports and services
        foreach (var (port, kind, points, why) in PortKinds)
            if (r.Open.Any(o => o.Port.Port == port)) Add(kind, points, F("Port open: {0}", why));
        foreach (var (_, info) in r.Open)
        {
            foreach (var (kind, why) in info.DeviceHints.Distinct()) Add(kind, 3, F("{0}: {1}", info.Protocol.Length > 0 ? info.Protocol : T("Service"), why));
            foreach (var os in info.OsHints) osHints.Add((os, info.Protocol, os.StartsWith("Windows ") ? 10 : 4));
            if (info.Facts.FirstOrDefault(f => f.Label == T("Model")) is { } model) models.Add(model.Value);
            if (info.Facts.FirstOrDefault(f => f.Label == T("Computer name")) is { } cn && r.Name.Length == 0) r.Name = cn.Value;
        }
        if (r.Ping is { } ping && Discovery.FromTtl(ping.Ttl) is var (start, system) && start > 0 && !r.IsThisPc)
        {
            if (start == 128) Add("computer", 2, T("Ping's TTL is Windows'"));
            if (start == 255) Add("router", 2, T("Ping's TTL is network equipment's"));
            osHints.Add((system, F("ping TTL {0}", ping.Ttl), 1));
        }
        if (r.Open.Count == 0 && r.IsPrivate && !r.IsGateway) Add("phone", 1, T("No open ports (phones and tablets keep them shut)"));

        // nmap's guesses
        if (r.Nmap is { } nmap)
        {
            if (nmap.Os.FirstOrDefault() is { Name.Length: > 0 } best) osHints.Add(($"{best.Name} ({best.Accuracy}%)", "nmap", best.Accuracy >= 90 ? 6 : 3));
            foreach (var p in nmap.Ports)
            {
                if (p.DeviceType.Length > 0)
                {
                    string kind = p.DeviceType switch
                    {
                        var t when t.Contains("printer") => "printer",
                        var t when t.Contains("router") || t.Contains("WAP") || t.Contains("switch") || t.Contains("firewall") => "router",
                        var t when t.Contains("media") => "tv",
                        var t when t.Contains("webcam") || t.Contains("camera") => "camera",
                        var t when t.Contains("phone") => "phone",
                        var t when t.Contains("storage") => "nas",
                        _ => "",
                    };
                    if (kind.Length > 0) Add(kind, 3, F("nmap: {0}", p.DeviceType));
                }
                if (p.Extra.Contains("Windows")) osHints.Add(("Windows", "nmap", 2));
            }
        }

        // the winner (a laptop or tablet is a computer or phone with something more to say)
        var ranked = score.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();
        if (ranked.Count == 0 || ranked[0].Value < 3) { r.Kind = "unknown"; r.Confidence = 0; }
        else
        {
            r.Kind = ranked[0].Key;
            int total = ranked.Sum(s => s.Value);
            r.Confidence = Math.Clamp((int)Math.Round(100.0 * ranked[0].Value / total + Math.Min(ranked[0].Value, 20)), 30, 98);
            if (r.Kind == "phone" && models.Any(x => Regex.IsMatch(x, "ipad|tablet|tab ", RegexOptions.IgnoreCase))) r.Kind = "tablet";
            if (r.Kind == "computer" && (models.Any(x => x.StartsWith("MacBook")) || score.GetValueOrDefault("laptop") > 0 && r.RandomMac)) r.Kind = "laptop";
        }
        r.Model = models.Where(x => x.Trim().Length > 0).GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
        if (r.Name.Length == 0) r.Name = r.Upnp.SelectMany(d => d.Facts).FirstOrDefault(f => f.Label == T("Name"))?.Value
            ?? r.Mdns?.HostName?.Replace(".local", "") ?? r.DnsNames.FirstOrDefault() ?? "";
        var bestOs = osHints.Where(o => o.Os.Length > 0).OrderByDescending(o => o.Weight).FirstOrDefault();
        r.Os = bestOs.Os ?? "";
        r.OsWhy = bestOs.Why ?? "";

        // findings: each service's, then the device's own
        foreach (var (port, info) in r.Open)
            foreach (var f in info.Findings)
                r.Findings.Add(f with { Title = F("{0} · port {1}", f.Title, port.Port) });
        if (r.Snmp is { } s2)
            r.Findings.Add(s2.Community == "private"
                ? new Finding(Severity.High, T("SNMP answers to \"private\""), T("The default write community: anyone on the network may change its settings."), "https://www.cisa.gov/news-events/alerts/2017/06/05/reducing-risk-snmp-abuse")
                : new Finding(Severity.Medium, T("SNMP answers to \"public\""), T("Anyone on the network can read its settings and details: change the community or use SNMPv3."), "https://www.cisa.gov/news-events/alerts/2017/06/05/reducing-risk-snmp-abuse"));
        if (r.Upnp.Any(d => d.DeviceTypes.Contains("InternetGatewayDevice")))
            r.Findings.Add(new Finding(Severity.Low, T("UPnP on the router"), T("Any device inside can open ports to the internet without asking."), "https://www.speedguide.net/faq/what-is-upnp-and-should-i-disable-it-on-my-router-526"));
        if (r.Mdns is { Types.Count: > 0 })
            r.Findings.Add(new Finding(Severity.Info, T("Announces itself (mDNS)"), T("Its name, model and services are visible to everything on the network.")));
        if (!r.IsPrivate && r.Open.Count > 0)
            r.Findings.Add(new Finding(Severity.Medium, T("Reachable from the internet"), F("{0} open ports on a public address.", r.Open.Count)));
    }

    /// <summary>A system from SNMP's description ("Linux router 6.1.99 #1 SMP…" → "Linux 6.1.99", with OpenWrt when it's that).</summary>
    private static string SnmpSystem(string descr, string name)
    {
        string first = descr.Split('\n')[0].Trim();
        var linux = Regex.Match(first, @"^Linux \S+ (\d[\w.-]*)");
        string system = linux.Success ? "Linux " + linux.Groups[1].Value : first.Length > 80 ? first[..80] + "…" : first;
        foreach (var distro in new[] { "OpenWrt", "DD-WRT", "pfSense", "OPNsense", "RouterOS", "EdgeOS", "VyOS", "Synology", "QNAP" })
            if ((descr + " " + name).Contains(distro, StringComparison.OrdinalIgnoreCase) && !system.Contains(distro, StringComparison.OrdinalIgnoreCase))
                return linux.Success ? $"{distro} ({system})" : $"{distro} · {system}";
        return system;
    }

    /// <summary>Apple's model names (MacBookPro18,3, iPhone14,2, AppleTV6,2, AudioAccessory5,1).</summary>
    private static void AppleModel(string model, Action<string, int, string> add, List<string> models, List<(string, string, int)> os)
    {
        models.Add(model);
        if (model.StartsWith("MacBook")) { add("laptop", 8, F("Apple model {0}", model)); add("computer", 8, F("Apple model {0}", model)); os.Add(("macOS", "mDNS", 5)); }
        else if (Regex.IsMatch(model, "^(iMac|Macmini|MacPro|Mac\\d|MacStudio)")) { add("computer", 8, F("Apple model {0}", model)); os.Add(("macOS", "mDNS", 5)); }
        else if (model.StartsWith("iPhone")) { add("phone", 8, F("Apple model {0}", model)); os.Add(("iOS", "mDNS", 5)); }
        else if (model.StartsWith("iPad")) { add("phone", 8, F("Apple model {0}", model)); os.Add(("iPadOS", "mDNS", 5)); }
        else if (model.StartsWith("AppleTV")) { add("tv", 8, F("Apple model {0}", model)); os.Add(("tvOS", "mDNS", 5)); }
        else if (model.StartsWith("AudioAccessory")) { add("speaker", 8, F("Apple model {0}", model)); os.Add(("HomePod software", "mDNS", 5)); }
    }
}
