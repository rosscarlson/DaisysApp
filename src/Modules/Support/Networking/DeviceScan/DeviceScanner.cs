using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A device a Device Scan found: what Host Scan's classifier made of its answers.</summary>
public sealed class FoundDevice
{
    public string Ip { get; set; } = "";
    public string? Mac { get; set; }
    public string? Vendor { get; set; }
    public bool RandomMac { get; set; }
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Os { get; set; } = "";
    /// <summary>A key of <see cref="HostScanner.DeviceKinds"/>.</summary>
    public string Kind { get; set; } = "unknown";
    public int Confidence { get; set; }
    public List<string> Why { get; set; } = new();
    public List<int> Ports { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public double? PingMs { get; set; }
    public bool IsGateway { get; set; }
    public bool IsThisPc { get; set; }

    [JsonIgnore] public string Group => DeviceScanner.GroupOf(this);
    [JsonIgnore] public string Title => Name.Length > 0 ? Name : Ip;

    /// <summary>Everything the search box looks in.</summary>
    [JsonIgnore] public string SearchText => string.Join(" ", new[] { Ip, Mac, Vendor, Name, Model, Os, HostScanner.DeviceKinds[Kind].Name }
        .Concat(Services).Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>The last Device Scan, kept in %APPDATA%\DaisysApp\DeviceScan.json so the tab shows it next time.</summary>
public sealed class DeviceScanResults
{
    private const string FileName = "DeviceScan";

    public string Subnet { get; set; } = "";
    public DateTime When { get; set; }
    public List<FoundDevice> Devices { get; set; } = new();
    /// <summary>Makers already looked up, by the MAC's first half (macvendors.com allows about one a second).</summary>
    public Dictionary<string, string> Vendors { get; set; } = new();

    public static DeviceScanResults Load() => JsonStore.Load<DeviceScanResults>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>
/// Device Scan: every device on a subnet found (ARP on this PC's own networks, so ones that ignore pings are found too;
/// ping, and a few ports, on others), then each asked what it is the quick ways Host Scan asks (names, NetBIOS, mDNS,
/// UPnP, the ports that give a device away, its maker), and classified by Host Scan's own rules.
/// </summary>
internal static class DeviceScanner
{
    /// <summary>The groups the tab shows, in order, each with the kinds in it.</summary>
    public static readonly (string Id, string Name, string[] Kinds)[] Groups =
    [
        ("network", T("Network"), ["router"]),
        ("computers", T("Computers"), ["computer", "laptop"]),
        ("phones", T("Phones & Tablets"), ["phone", "tablet"]),
        ("media", T("TV & Media"), ["tv", "speaker", "console"]),
        ("smarthome", T("Smart Home"), ["smarthome"]),
        ("cameras", T("Cameras"), ["camera"]),
        ("printers", T("Printers"), ["printer"]),
        ("servers", T("Servers & Storage"), ["server", "nas"]),
        ("unknown", T("Unknown"), ["unknown"]),
    ];

    public static string GroupOf(FoundDevice d) => Groups.FirstOrDefault(g => g.Kinds.Contains(d.Kind)).Id ?? "unknown";

    /// <summary>The ports that say most about what a device is (Host Scan's), plus the web and SSH.</summary>
    private static readonly int[] TellingPorts = [62078, 9100, 631, 515, 8060, 8008, 8009, 8001, 8002, 3001, 7000, 1400, 3389, 445, 135, 139, 5357,
        554, 8291, 53, 5000, 5001, 6053, 6668, 9999, 55443, 8123, 32400, 2375, 9090, 22, 80, 443, 8080, 1883, 5683];

    /// <summary>The biggest subnet scanned (/20: 4,094 addresses).</summary>
    public const int MinPrefix = 20;

    // ---------------------------------------------------------------- subnets

    /// <summary>"192.168.1.0/24", "192.168.1.7/24" or "192.168.1.7 255.255.255.0" → its network and prefix length.</summary>
    public static bool TryParse(string text, out uint network, out int prefix)
    {
        network = 0;
        prefix = 32;
        var parts = text.Trim().Split(['/', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[1], out int p) && p is >= 0 and <= 32) prefix = p;
            else if (IPAddress.TryParse(parts[1], out var mask) && mask.AddressFamily == AddressFamily.InterNetwork) prefix = Bits(Number(mask));
            else return false;
        }
        network = Number(ip) & Mask(prefix);
        return true;
    }

    public static string Text(uint network, int prefix) => $"{Address(network)}/{prefix}";

    /// <summary>This PC's IPv4 subnets, the internet adapter's first; one bigger than <see cref="MinPrefix"/> as the /24 around this PC.</summary>
    public static List<string> LocalSubnets()
    {
        var list = new List<string>();
        var primary = NetInfo.Primary();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => n.Id == primary?.Id ? 0 : 1))
            foreach (var a in n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.")))
            {
                int prefix = Math.Max(a.PrefixLength, a.PrefixLength < MinPrefix ? 24 : 0);
                string s = Text(Number(a.Address) & Mask(prefix), prefix);
                if (!list.Contains(s)) list.Add(s);
            }
        return list;
    }

    private static uint Number(IPAddress ip) { var b = ip.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    private static IPAddress Address(uint n) => new([(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n]);
    private static uint Mask(int prefix) => prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
    private static int Bits(uint mask) => System.Numerics.BitOperations.PopCount(mask);

    // ---------------------------------------------------------------- the scan

    /// <summary>What's been found about one device so far, made into a <see cref="FoundDevice"/> by Host Scan's rules.</summary>
    private sealed class Answers
    {
        public IPAddress Ip = IPAddress.None;
        public (int Ttl, double Ms)? Ping;
        public string? Mac, Vendor, Reverse;
        public List<Discovery.NbName> NetBios = new();
        public DnsWire.MdnsInfo? Mdns;
        public List<Discovery.UpnpDevice> Upnp = new();
        public readonly List<PortResult> Open = new();
        public bool IsGateway, IsThisPc;

        public FoundDevice Classify()
        {
            lock (this) return Make();
        }

        private FoundDevice Make()
        {
            var r = new HostReport { Host = Ip.ToString(), Ip = Ip.ToString(), IsPrivate = NetInfo.IsPrivate(Ip), IsGateway = IsGateway, IsThisPc = IsThisPc, Ping = Ping, Mac = Mac, Vendor = Vendor };
            r.RandomMac = Mac != null && Discovery.IsRandom(Mac);
            if (Reverse != null) r.DnsNames.Add(Reverse);
            r.NetBios = NetBios;
            r.Mdns = Mdns;
            r.Upnp = Upnp;
            r.Open.AddRange(Open.Select(p => (p, new ServiceInfo())));
            HostScanner.Conclude(r);
            return new FoundDevice
            {
                Ip = r.Ip, Mac = Mac, Vendor = Vendor, RandomMac = r.RandomMac, Name = r.Name, Model = r.Model, Os = r.OsWhy.Contains("TTL") ? "" : r.Os, Kind = r.Kind, // (a guess from ping's TTL says too little)
                Confidence = r.Confidence, Why = r.KindWhy.ToList(), Ports = Open.Select(p => p.Port).ToList(),
                Services = (Mdns?.Types.Select(t => t.Replace(".local", "")) ?? []).Concat(Upnp.SelectMany(u => u.DeviceTypes)).Distinct().ToList(),
                PingMs = Ping?.Ms, IsGateway = IsGateway, IsThisPc = IsThisPc,
            };
        }
    }

    /// <param name="found">Each device as it's found, and again as more is learnt about it (on any thread).</param>
    /// <param name="progress">What it's doing and how far along (0–1), on any thread.</param>
    public static async Task<List<FoundDevice>> ScanAsync(uint network, int prefix, DeviceScanResults cache, Action<FoundDevice> found, Action<string, double> progress, CancellationToken ct)
    {
        uint size = prefix >= 31 ? 1u << (32 - prefix) : (1u << (32 - prefix)) - 2, first = prefix >= 31 ? network : network + 1;
        var hosts = Enumerable.Range(0, (int)size).Select(i => Address(first + (uint)i)).ToList();
        uint mask = Mask(prefix);
        var mine = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => (n, a)))
            .Where(x => (Number(x.a.Address) & mask) == network).ToList();
        bool onLink = mine.Count > 0;
        string? gateway = NetInfo.Gateway();
        Logging.Log.Here.Info($"Device scan of {Text(network, prefix)}: {hosts.Count} addresses, {(onLink ? "on this PC's network (ARP)" : "not on this PC's network (ping, ports)")}");

        // UPnP asked of the whole network at once, while the sweep runs
        var upnp = UpnpAllAsync(ct);

        // the sweep
        var alive = new ConcurrentDictionary<string, Answers>();
        int done = 0;
        foreach (var (n, a) in mine)
            alive[a.Address.ToString()] = new Answers { Ip = a.Address, IsThisPc = true, Mac = NetInfo.Mac(n), Ping = (64, 0) };
        using (var gate = new SemaphoreSlim(64))
        {
            await Task.WhenAll(hosts.Select(async ip =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (alive.ContainsKey(ip.ToString())) return;
                    var ping = OnePingAsync(ip);
                    var arp = onLink ? Task.Factory.StartNew(() => Discovery.Mac(ip), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default) : Task.FromResult<string?>(null);
                    var p = await ping;
                    var mac = await arp;
                    List<PortResult> open = [];
                    if (p == null && mac == null && !onLink)
                        open = (await Task.WhenAll(new[] { 80, 443, 22, 445, 3389 }.Select(port => PortProbe.ProbeAsync(ip, port, 500, ct, 0)))).Where(r => r.State != PortState.Filtered).ToList();
                    if (p != null || mac != null || open.Count > 0)
                    {
                        var ans = new Answers { Ip = ip, Ping = p, Mac = mac, IsGateway = ip.ToString() == gateway };
                        ans.Open.AddRange(open.Where(o => o.State == PortState.Open));
                        alive[ip.ToString()] = ans;
                        found(ans.Classify());
                    }
                }
                finally
                {
                    gate.Release();
                    int d = Interlocked.Increment(ref done);
                    if (d % 8 == 0) progress(F("Looking for devices: {0} of {1} · {2} found", d, hosts.Count, alive.Count), 0.4 * d / hosts.Count);
                }
            }));
        }
        // ARP asked again of the rest (a device asleep, or a reply lost among so many)
        if (onLink)
        {
            progress(T("Looking again…"), 0.4);
            using var gate = new SemaphoreSlim(64);
            await Task.WhenAll(hosts.Where(ip => !alive.ContainsKey(ip.ToString())).Select(async ip =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (await Task.Factory.StartNew(() => Discovery.Mac(ip), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default) is { } mac)
                        alive[ip.ToString()] = new Answers { Ip = ip, Mac = mac, IsGateway = ip.ToString() == gateway };
                }
                finally { gate.Release(); }
            }));
        }
        // ones Windows has talked to that didn't answer just now
        foreach (var (ip, mac) in Discovery.Neighbours())
            if (IPAddress.TryParse(ip, out var a) && (Number(a) & mask) == network && !alive.ContainsKey(ip) && hosts.Count > 0 && hosts.Contains(a))
                alive[ip] = new Answers { Ip = a, Mac = mac, IsGateway = ip == gateway };
        foreach (var ans in alive.Values) found(ans.Classify());
        Logging.Log.Here.Info($"Device scan: {alive.Count} devices on {Text(network, prefix)}");

        // their makers, one a second (macvendors.com's free limit), while they're asked about themselves
        var makers = MakersAsync(alive.Values.ToList(), cache, found, ct);

        // what each says about itself
        var upnpFound = await upnp;
        int asked = 0;
        using (var gate = new SemaphoreSlim(16))
        {
            await Task.WhenAll(alive.Values.Select(async ans =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var ip = ans.Ip;
                    var reverse = Names.ReverseAsync(ip.ToString());
                    var netbios = Discovery.NetBiosAsync(ip, ct);
                    var mdns = DnsWire.MdnsAsync(ip, ct);
                    var ports = Task.WhenAll(TellingPorts.Where(p => !ans.Open.Any(o => o.Port == p)).Select(p => PortProbe.ProbeAsync(ip, p, 1200, ct, 0)));
                    string? name = await reverse;
                    var nb = await netbios;
                    DnsWire.MdnsInfo? m = null;
                    try { m = await mdns; } catch (SocketException) { }
                    var open = (await ports).Where(r => r.State == PortState.Open).ToList();
                    lock (ans)
                    {
                        ans.Reverse = name;
                        ans.NetBios = nb.Names;
                        ans.Mac ??= nb.Mac;
                        ans.Mdns = m;
                        ans.Open.AddRange(open);
                        ans.Open.Sort((a, b) => a.Port.CompareTo(b.Port));
                        if (upnpFound.TryGetValue(ip.ToString(), out var u)) ans.Upnp = u;
                    }
                    found(ans.Classify());
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logging.Log.Here.Debug(() => $"Device scan of {ans.Ip}: {ex.Message}"); }
                finally
                {
                    gate.Release();
                    int a = Interlocked.Increment(ref asked);
                    progress(F("Asking each what it is: {0} of {1}", a, alive.Count), 0.4 + 0.5 * a / Math.Max(alive.Count, 1));
                }
            }));
        }
        progress(T("Finding makers…"), 0.92);
        await makers;
        progress(T("Done"), 1);
        return alive.Values.Select(a => a.Classify()).OrderBy(d => Number(IPAddress.Parse(d.Ip))).ToList();
    }

    private static async Task<(int Ttl, double Ms)?> OnePingAsync(IPAddress ip)
    {
        try
        {
            using var ping = new Ping();
            for (int i = 0; i < 2; i++)
            {
                var r = await ping.SendPingAsync(ip, 700);
                if (r.Status == IPStatus.Success) return (r.Options?.Ttl ?? 0, r.RoundtripTime);
            }
        }
        catch (PingException) { }
        return null;
    }

    private static async Task MakersAsync(List<Answers> devices, DeviceScanResults cache, Action<FoundDevice> found, CancellationToken ct)
    {
        // (a MAC is known for most now; NetBIOS may add one later, so wait for those briefly)
        var asked = new HashSet<string>();
        for (int round = 0; round < 2; round++)
        {
            foreach (var d in devices.Where(d => d.Mac != null && d.Vendor == null && !Discovery.IsRandom(d.Mac)))
            {
                string oui = d.Mac!.Replace(":", "")[..6];
                string? known;
                lock (cache.Vendors) cache.Vendors.TryGetValue(oui, out known);
                if (known != null) { lock (d) d.Vendor = known.Length > 0 ? known : null; found(d.Classify()); continue; }
                if (!asked.Add(oui)) continue;
                var v = await Discovery.VendorAsync(d.Mac, ct);
                lock (cache.Vendors) cache.Vendors[oui] = v ?? "";
                foreach (var same in devices.Where(x => x.Mac?.Replace(":", "").StartsWith(oui) == true)) { lock (same) same.Vendor = v; found(same.Classify()); }
                await Task.Delay(1100, ct);
            }
            if (round == 0) await Task.Delay(3000, ct);
        }
    }

    /// <summary>SSDP's search sent to the whole network once: every device's UPnP descriptions, by address.</summary>
    private static async Task<Dictionary<string, List<Discovery.UpnpDevice>>> UpnpAllAsync(CancellationToken ct)
    {
        var byIp = new Dictionary<string, Dictionary<string, Discovery.UpnpDevice>>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: ssdp:all\r\n\r\n");
            for (int i = 0; i < 2; i++) await udp.SendAsync(search, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(3500);
            try
            {
                while (true)
                {
                    var r = await udp.ReceiveAsync(wait.Token);
                    string text = Encoding.UTF8.GetString(r.Buffer);
                    string H(string n) => Regex.Match(text, $@"^{n}:\s*(.+?)\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    string location = H("LOCATION"), ip = r.RemoteEndPoint.Address.ToString();
                    var mine = byIp.TryGetValue(ip, out var m) ? m : byIp[ip] = new();
                    if (location.Length > 0 && !mine.ContainsKey(location) && mine.Count < 4) mine[location] = new Discovery.UpnpDevice { Location = location, Server = H("SERVER") };
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        catch (SocketException) { }
        await Task.WhenAll(byIp.Values.SelectMany(d => d.Values).Select(d => Discovery.DescribeAsync(d, ct)));
        return byIp.ToDictionary(p => p.Key, p => p.Value.Values.Where(d => d.Facts.Count > 0 || d.Server.Length > 0).ToList());
    }
}
