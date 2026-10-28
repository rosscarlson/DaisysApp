using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Asking a device about itself the ways devices on a home network answer: its MAC address (ARP) and who made it, ping's
/// TTL (which says roughly what system it runs), NetBIOS names (Windows, Samba, NAS), UPnP's description (TVs, routers,
/// speakers, consoles: maker, model, serial) and SNMP (routers, printers, switches: the system's own description).
/// </summary>
internal static class Discovery
{
    private static readonly HttpClient http = MakeClient();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(Discovery).Assembly.GetName().Version?.ToString(3)}");
        return h;
    }

    // ---------------------------------------------------------------- ping

    /// <summary>Four pings: the replies' TTL and the average time (null if it doesn't answer pings).</summary>
    public static async Task<(int Ttl, double Ms)?> PingAsync(IPAddress ip, CancellationToken ct)
    {
        var times = new List<double>();
        int ttl = 0;
        using var ping = new Ping();
        for (int i = 0; i < 4 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(ip, 1000);
                if (r.Status == IPStatus.Success) { times.Add(r.RoundtripTime); ttl = r.Options?.Ttl ?? ttl; }
            }
            catch (PingException) { }
        }
        return times.Count == 0 ? null : (ttl, times.Average());
    }

    /// <summary>What a reply's TTL says: systems start at 64 (Linux, Android, macOS, iOS), 128 (Windows) or 255 (network gear); each router on the way takes one off.</summary>
    public static (int Start, string System) FromTtl(int ttl) => ttl switch
    {
        <= 0 => (0, ""),
        <= 64 => (64, T("Linux, Android, macOS, iOS or a small device")),
        <= 128 => (128, "Windows"),
        _ => (255, T("Network equipment (router, switch, printer)")),
    };

    // ---------------------------------------------------------------- ARP and makers

    /// <summary>A device's MAC address from ARP (only on this PC's own network), or null.</summary>
    public static string? Mac(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return null;
        var mac = new byte[6];
        uint len = 6;
        try
        {
            if (SendARP(BitConverter.ToUInt32(ip.GetAddressBytes()), 0, mac, ref len) != 0 || len == 0) return null;
        }
        catch { return null; }
        return string.Join(":", mac.Take((int)len).Select(b => b.ToString("X2")));
    }

    /// <summary>A MAC address chosen at random (phones and computers do this per network, so it names no maker).</summary>
    public static bool IsRandom(string mac) => mac.Length >= 2 && (Convert.ToByte(mac[..2], 16) & 2) != 0;

    private static readonly ConcurrentDictionary<string, string?> vendors = new();

    /// <summary>Who made a network card, from its MAC address's first half (macvendors.com), remembered.</summary>
    public static async Task<string?> VendorAsync(string mac, CancellationToken ct)
    {
        string oui = mac.Replace(":", "")[..6];
        if (vendors.TryGetValue(oui, out var v)) return v;
        try
        {
            using var r = await http.GetAsync($"https://api.macvendors.com/{mac}", ct);
            v = r.IsSuccessStatusCode ? (await r.Content.ReadAsStringAsync(ct)).Trim() : null;
            if (r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound) vendors[oui] = v;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.Log.Here.Debug(() => $"MAC vendor: {ex.Message}"); v = null; }
        return v;
    }

    /// <summary>Devices this PC has talked to lately on its own networks (Windows' ARP table): address and MAC.</summary>
    public static List<(string Ip, string Mac)> Neighbours()
    {
        var list = new List<(string, string)>();
        try
        {
            int size = 0;
            GetIpNetTable(IntPtr.Zero, ref size, true);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buffer, ref size, true) != 0) return list;
                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf<MibIpNetRow>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibIpNetRow>(buffer + 4 + i * rowSize);
                    if (row.Type is not (3 or 4) || row.PhysAddrLen != 6) continue; // dynamic or static only
                    var ip = new IPAddress(row.Addr);
                    var b = ip.GetAddressBytes();
                    if (b[0] >= 224 || b[3] == 255) continue; // multicast, broadcast
                    list.Add((ip.ToString(), string.Join(":", row.PhysAddr.Take(6).Select(x => x.ToString("X2")))));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"ARP table: {ex.Message}"); }
        return list.DistinctBy(n => n.Item1).OrderBy(n => Version.TryParse(n.Item1, out var v) ? v : new Version()).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpNetRow
    {
        public int Index;
        public int PhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PhysAddr;
        public uint Addr;
        public int Type;
    }

    [DllImport("iphlpapi.dll")] private static extern int SendARP(uint destIp, uint srcIp, byte[] mac, ref uint len);
    [DllImport("iphlpapi.dll")] private static extern int GetIpNetTable(IntPtr table, ref int size, bool order);

    // ---------------------------------------------------------------- NetBIOS

    /// <summary>A NetBIOS name: the name, its kind (from the 16th byte) and whether it's a group (a workgroup or domain).</summary>
    public sealed record NbName(string Name, int Suffix, bool Group)
    {
        public string Kind => (Suffix, Group) switch
        {
            (0x00, false) => T("computer"),
            (0x00, true) => T("workgroup / domain"),
            (0x20, _) => T("file sharing"),
            (0x03, _) => T("messenger"),
            (0x1B, _) => T("domain master browser"),
            (0x1C, _) => T("domain controllers"),
            (0x1D, _) => T("master browser"),
            (0x1E, _) => T("browser election"),
            _ => $"<{Suffix:X2}>",
        };
    }

    /// <summary>A device's NetBIOS names and MAC address (NBSTAT on UDP 137), as nbtstat -A gets them.</summary>
    public static async Task<(List<NbName> Names, string? Mac)> NetBiosAsync(IPAddress ip, CancellationToken ct)
    {
        var q = new List<byte> { 0x44, 0x41, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0x20, (byte)'C', (byte)'K' };
        for (int i = 0; i < 30; i++) q.Add((byte)'A');
        q.AddRange(new byte[] { 0, 0, 0x21, 0, 1 });
        var names = new List<NbName>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            await udp.SendAsync(q.ToArray(), new IPEndPoint(ip, 137), ct);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(1500);
            var r = (await udp.ReceiveAsync(wait.Token)).Buffer;
            int count = r[56], p = 57;
            for (int i = 0; i < count && p + 18 <= r.Length; i++, p += 18)
                names.Add(new NbName(Encoding.ASCII.GetString(r, p, 15).Trim(), r[p + 15], (r[p + 16] & 0x80) != 0));
            string? mac = p + 6 <= r.Length && r.Skip(p).Take(6).Any(b => b != 0) ? string.Join(":", r.Skip(p).Take(6).Select(b => b.ToString("X2"))) : null;
            return (names, mac);
        }
        catch (Exception ex) when (ex is OperationCanceledException && !ct.IsCancellationRequested || ex is SocketException or IndexOutOfRangeException) { return (names, null); }
    }

    // ---------------------------------------------------------------- UPnP

    /// <summary>A UPnP device's description: what it says it is.</summary>
    public sealed class UpnpDevice
    {
        public string Location = "", Server = "";
        public readonly List<Fact> Facts = new();
        public readonly List<string> DeviceTypes = new();
        public readonly List<string> Services = new();
        public string? Presentation;
    }

    /// <summary>Asks a device for its UPnP descriptions (SSDP M-SEARCH, sent to it and to the network), then reads each.</summary>
    public static async Task<List<UpnpDevice>> UpnpAsync(IPAddress ip, CancellationToken ct)
    {
        var found = new Dictionary<string, UpnpDevice>();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return [];
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n");
            await udp.SendAsync(search, new IPEndPoint(ip, 1900), ct);
            await udp.SendAsync(search, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(2500);
            try
            {
                while (true)
                {
                    var r = await udp.ReceiveAsync(wait.Token);
                    if (!r.RemoteEndPoint.Address.Equals(ip)) continue;
                    string text = Encoding.UTF8.GetString(r.Buffer);
                    string H(string n) => Regex.Match(text, $@"^{n}:\s*(.+?)\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    string location = H("LOCATION");
                    if (location.Length > 0 && !found.ContainsKey(location)) found[location] = new UpnpDevice { Location = location, Server = H("SERVER") };
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        catch (SocketException) { }
        foreach (var d in found.Values.Take(4)) await DescribeAsync(d, ct);
        return found.Values.Where(d => d.Facts.Count > 0 || d.Server.Length > 0).Take(4).ToList();
    }

    private static async Task DescribeAsync(UpnpDevice d, CancellationToken ct)
    {
        try
        {
            var xml = XDocument.Parse(await http.GetStringAsync(d.Location, ct));
            var device = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "device");
            if (device == null) return;
            string V(string n) => device.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim() ?? "";
            foreach (var (n, label) in new[] { ("friendlyName", T("Name")), ("manufacturer", T("Maker")), ("modelName", T("Model")), ("modelNumber", T("Model number")),
                ("modelDescription", T("Description")), ("serialNumber", T("Serial")), ("UDN", "UDN") })
                if (V(n).Length > 0) d.Facts.Add(new Fact(label, V(n)));
            if (V("manufacturerURL") is { Length: > 0 } url && Uri.IsWellFormedUriString(url, UriKind.Absolute)) d.Facts.Add(new Fact(T("Maker's site"), url, url));
            d.Presentation = V("presentationURL") is { Length: > 0 } p ? p : null;
            foreach (var t in xml.Descendants().Where(e => e.Name.LocalName == "deviceType")) d.DeviceTypes.Add(Short(t.Value));
            foreach (var s in xml.Descendants().Where(e => e.Name.LocalName == "serviceType")) d.Services.Add(Short(s.Value));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.Log.Here.Debug(() => $"UPnP {d.Location}: {ex.Message}"); }
    }

    /// <summary>"urn:schemas-upnp-org:device:MediaRenderer:1" → "MediaRenderer".</summary>
    private static string Short(string urn)
    {
        var parts = urn.Trim().Split(':');
        return parts.Length >= 2 ? parts[^2] : urn.Trim();
    }

    // ---------------------------------------------------------------- SNMP

    private static (string Oid, string Label)[] SystemOids =>
    [
        ("1.3.6.1.2.1.1.1.0", T("Description")), ("1.3.6.1.2.1.1.5.0", T("Name")), ("1.3.6.1.2.1.1.6.0", T("Location")),
        ("1.3.6.1.2.1.1.4.0", T("Contact")), ("1.3.6.1.2.1.1.3.0", T("Up for")), ("1.3.6.1.2.1.1.2.0", T("Object ID")),
    ];

    /// <summary>SNMP's system group (description, name, location, contact, uptime), asked with the default community names "public" then "private".</summary>
    public static async Task<(string Community, List<Fact> Facts)?> SnmpAsync(IPAddress ip, CancellationToken ct)
    {
        foreach (var community in new[] { "public", "private" })
        {
            foreach (int version in new[] { 1, 0 }) // v2c, then v1
            {
                var facts = await SnmpGetAsync(ip, community, version, ct);
                if (facts != null) return (community, facts);
            }
        }
        return null;
    }

    private static async Task<List<Fact>?> SnmpGetAsync(IPAddress ip, string community, int version, CancellationToken ct)
    {
        var binds = SystemOids.SelectMany(o => Smb.Der(0x30, Oid(o.Oid).Concat(new byte[] { 5, 0 }).ToArray())).ToArray();
        var pdu = Smb.Der(0xA0, Smb.Der(2, [0x12, 0x34]).Concat(new byte[] { 2, 1, 0, 2, 1, 0 }).Concat(Smb.Der(0x30, binds)).ToArray());
        var message = Smb.Der(0x30, new byte[] { 2, 1, (byte)version }.Concat(Smb.Der(4, Encoding.ASCII.GetBytes(community))).Concat(pdu).ToArray());
        try
        {
            using var udp = new UdpClient(ip.AddressFamily);
            await udp.SendAsync(message, new IPEndPoint(ip, 161), ct);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(1500);
            var r = (await udp.ReceiveAsync(wait.Token)).Buffer;
            var values = new List<(string Oid, byte Tag, byte[] Value)>();
            Walk(r, 0, r.Length, values);
            var facts = new List<Fact>();
            foreach (var (oid, label) in SystemOids)
            {
                var v = values.FirstOrDefault(x => x.Oid == oid);
                if (v.Value == null || v.Tag is 0x80 or 0x81 or 0x82 or 5) continue;
                string text = v.Tag switch
                {
                    0x43 => Ui.Duration(TimeSpan.FromSeconds(Number(v.Value) / 100.0)),
                    6 => OidText(v.Value),
                    2 => Number(v.Value).ToString(),
                    _ => PortProbe.Printable(Encoding.UTF8.GetString(v.Value)),
                };
                if (text.Length > 0) facts.Add(new Fact(label, text));
            }
            return facts.Count > 0 ? facts : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException && !ct.IsCancellationRequested || ex is SocketException or IndexOutOfRangeException or ArgumentException) { return null; }
    }

    private static byte[] Oid(string oid)
    {
        var parts = oid.Split('.').Select(int.Parse).ToArray();
        var b = new List<byte> { (byte)(parts[0] * 40 + parts[1]) };
        foreach (int n in parts.Skip(2))
        {
            var chunk = new Stack<byte>();
            int v = n;
            chunk.Push((byte)(v & 0x7F));
            while ((v >>= 7) > 0) chunk.Push((byte)(v & 0x7F | 0x80));
            b.AddRange(chunk);
        }
        return Smb.Der(6, b.ToArray());
    }

    private static string OidText(byte[] v)
    {
        var parts = new List<long> { v[0] / 40, v[0] % 40 };
        long n = 0;
        foreach (var b in v.Skip(1)) { n = n << 7 | (uint)(b & 0x7F); if ((b & 0x80) == 0) { parts.Add(n); n = 0; } }
        return string.Join(".", parts);
    }

    private static long Number(byte[] v) => v.Aggregate(0L, (a, b) => a << 8 | b);

    /// <summary>Finds every (OID, value) pair in a BER message.</summary>
    private static void Walk(byte[] m, int from, int to, List<(string, byte, byte[])> found)
    {
        int p = from;
        string? lastOid = null;
        while (p + 2 <= to)
        {
            byte tag = m[p++];
            int len = m[p++];
            if ((len & 0x80) != 0)
            {
                int n = len & 0x7F;
                len = 0;
                for (int i = 0; i < n; i++) len = len << 8 | m[p++];
            }
            if (p + len > to) return;
            if (tag is 0x30 or 0xA2 or 0xA0) Walk(m, p, p + len, found);
            else if (tag == 6 && lastOid == null && p + len <= to) lastOid = OidText(m[p..(p + len)]);
            else if (lastOid != null) { found.Add((lastOid, tag, m[p..(p + len)])); lastOid = null; }
            p += len;
        }
    }
}
