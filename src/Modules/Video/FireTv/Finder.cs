using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>A device found on the network: its address, its name (from DIAL, when it says), and whether ADB answers.</summary>
internal sealed record Found(IPAddress Address, string Name, bool Adb, bool Amazon);

/// <summary>
/// Finds Fire TVs on this PC's networks: every address of each local IPv4 subnet (/24 at most, so at most 254 per
/// network card) tried on ADB's port 5555, and an SSDP search for DIAL (casting), which Fire TVs answer with their
/// name even when ADB is off.
/// </summary>
internal static partial class Finder
{
    private static readonly Logging.Logger log = Logging.Log.Here;

    public static async Task<List<Found>> FindAsync(CancellationToken ct)
    {
        var dial = DialAsync(ct);
        var adb = ScanAsync(ct);
        var named = await dial;
        var open = await adb;
        var list = new List<Found>();
        foreach (var ip in open.Union(named.Keys))
        {
            named.TryGetValue(ip, out var n);
            if (n.Name == null && !open.Contains(ip)) continue;
            list.Add(new Found(ip, n.Name ?? "", open.Contains(ip), n.Amazon));
        }
        // (Amazon's devices first, then those with ADB on)
        list = list.Where(f => f.Adb || f.Amazon)
            .OrderByDescending(f => f.Amazon).ThenByDescending(f => f.Adb).ThenBy(f => f.Address.GetAddressBytes()[3]).ToList();
        log.Info($"Find: {open.Count} with ADB, {named.Count} answering DIAL, {list.Count} shown");
        return list;
    }

    /// <summary>Every address of the PC's own IPv4 subnets (/24 at most) to try.</summary>
    private static List<IPAddress> Targets()
    {
        var targets = new HashSet<IPAddress>();
        foreach (var a in Locals())
        {
            var b = a.Address.GetAddressBytes();
            int prefix = Math.Max(a.PrefixLength, 24);
            uint ip = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            uint mask = prefix >= 32 ? uint.MaxValue : ~(uint.MaxValue >> prefix);
            uint net = ip & mask, count = ~mask;
            for (uint i = 1; i < count; i++)
            {
                uint t = net | i;
                if (t != ip) targets.Add(new IPAddress(new[] { (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t }));
            }
        }
        return targets.ToList();
    }

    /// <summary>The PC's own IPv4 addresses (cards that are up; not loopback or link-local).</summary>
    private static IEnumerable<UnicastIPAddressInformation> Locals() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)
                && !(a.Address.GetAddressBytes() is [169, 254, ..]));

    private static IEnumerable<IPAddress> LocalAddresses() => Locals().Select(a => a.Address).Distinct();

    private static async Task<HashSet<IPAddress>> ScanAsync(CancellationToken ct)
    {
        var open = new HashSet<IPAddress>();
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(Targets().Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var tcp = new TcpClient();
                using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
                time.CancelAfter(600);
                await tcp.ConnectAsync(ip, FireTvDevice.Port, time.Token);
                lock (open) open.Add(ip);
            }
            catch { }
            finally { gate.Release(); }
        }));
        return open;
    }

    [GeneratedRegex(@"^LOCATION:\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Location();

    [GeneratedRegex(@"<friendlyName>([^<]*)</friendlyName>", RegexOptions.IgnoreCase)]
    private static partial Regex FriendlyName();

    [GeneratedRegex(@"<manufacturer>([^<]*)</manufacturer>", RegexOptions.IgnoreCase)]
    private static partial Regex Manufacturer();

    /// <summary>DIAL devices (SSDP M-SEARCH), by address: their friendly name and whether Amazon made them.</summary>
    private static async Task<Dictionary<IPAddress, (string? Name, bool Amazon)>> DialAsync(CancellationToken ct)
    {
        var found = new Dictionary<IPAddress, (string?, bool)>();
        var locations = new Dictionary<IPAddress, string>();
        // (from each of the PC's addresses: bound to Any, the search goes out one card only, maybe WSL's or a VPN's)
        await Task.WhenAll(LocalAddresses().Select(async local =>
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(local, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                var ask = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                    "ST: urn:dial-multiscreen-org:service:dial:1\r\n\r\n");
                var to = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                for (int i = 0; i < 2; i++) await udp.SendAsync(ask, to, ct);
                using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
                time.CancelAfter(3000);
                try
                {
                    while (true)
                    {
                        var r = await udp.ReceiveAsync(time.Token);
                        var m = Location().Match(Encoding.ASCII.GetString(r.Buffer));
                        if (m.Success) lock (locations) locations.TryAdd(r.RemoteEndPoint.Address, m.Groups[1].Value);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
            catch (SocketException ex) { log.Warn($"Find: SSDP search from {local} failed: {ex.Message}"); }
        }));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        await Task.WhenAll(locations.Select(async l =>
        {
            string? name = null;
            bool amazon = false;
            try
            {
                var xml = await http.GetStringAsync(l.Value, ct);
                name = WebUtility.HtmlDecode(FriendlyName().Match(xml).Groups[1].Value).Trim();
                amazon = Manufacturer().Match(xml).Groups[1].Value.Contains("Amazon", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            lock (found) found[l.Key] = (string.IsNullOrEmpty(name) ? null : name, amazon);
        }));
        return found;
    }
}
