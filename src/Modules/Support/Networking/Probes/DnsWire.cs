using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A DNS record from an answer: its name, type and what it says (a name, an address, TXT strings, a service's port…).</summary>
internal sealed record DnsRecord(string Name, int Type, string Data, IReadOnlyList<string> Texts, int Port = 0, string Target = "");

/// <summary>
/// DNS messages built and read by hand, for questions Windows' resolver doesn't ask: a DNS server's version
/// (<c>version.bind</c>, CHAOS class) and multicast DNS (Bonjour / Avahi) asked of one device directly: its name, the
/// services it offers and what they say about it (model, name, features).
/// </summary>
internal static class DnsWire
{
    public const int A = 1, Ptr = 12, Hinfo = 13, Txt = 16, Aaaa = 28, Srv = 33, Any = 255;

    public static byte[] Query(string name, int type, int @class = 1, ushort id = 0, bool recursion = false)
    {
        var b = new List<byte> { (byte)(id >> 8), (byte)id, (byte)(recursion ? 1 : 0), 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            b.Add((byte)bytes.Length);
            b.AddRange(bytes);
        }
        b.Add(0);
        b.AddRange(new[] { (byte)(type >> 8), (byte)type, (byte)(@class >> 8), (byte)@class });
        return b.ToArray();
    }

    /// <summary>Every record in a message's answer, authority and additional sections.</summary>
    public static List<DnsRecord> Parse(byte[] m)
    {
        var list = new List<DnsRecord>();
        try
        {
            int qd = m[4] << 8 | m[5], records = (m[6] << 8 | m[7]) + (m[8] << 8 | m[9]) + (m[10] << 8 | m[11]);
            int p = 12;
            for (int i = 0; i < qd; i++) { ReadName(m, ref p); p += 4; }
            for (int i = 0; i < records && p < m.Length; i++)
            {
                string name = ReadName(m, ref p);
                int type = m[p] << 8 | m[p + 1], len = m[p + 8] << 8 | m[p + 9];
                p += 10;
                int end = p + len, q = p;
                switch (type)
                {
                    case A when len == 4: list.Add(new(name, type, new IPAddress(m.AsSpan(p, 4)).ToString(), [])); break;
                    case Aaaa when len == 16: list.Add(new(name, type, new IPAddress(m.AsSpan(p, 16)).ToString(), [])); break;
                    case Ptr: list.Add(new(name, type, ReadName(m, ref q), [])); break;
                    case Srv:
                        int port = m[p + 4] << 8 | m[p + 5];
                        q = p + 6;
                        string target = ReadName(m, ref q);
                        list.Add(new(name, type, $"{target}:{port}", [], port, target));
                        break;
                    case Txt or Hinfo:
                        var texts = new List<string>();
                        while (q < end) { int n = m[q++]; texts.Add(Encoding.UTF8.GetString(m, q, Math.Min(n, end - q))); q += n; }
                        list.Add(new(name, type, string.Join(" ", texts), texts));
                        break;
                }
                p = end;
            }
        }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentException) { }
        return list;
    }

    private static string ReadName(byte[] m, ref int p)
    {
        var labels = new List<string>();
        int at = p, jumps = 0;
        bool jumped = false;
        while (true)
        {
            int len = m[at];
            if (len == 0) { at++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                if (!jumped) p = at + 2;
                at = (len & 0x3F) << 8 | m[at + 1];
                jumped = true;
                if (++jumps > 20) break;
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(m, at + 1, len));
            at += len + 1;
        }
        if (!jumped) p = at;
        return string.Join(".", labels);
    }

    /// <summary>Sends questions to a device's port and gathers every record it answers with within <paramref name="waitMs"/>.</summary>
    public static async Task<List<DnsRecord>> AskAsync(IPAddress ip, int port, IEnumerable<byte[]> queries, int waitMs, CancellationToken ct)
    {
        var records = new List<DnsRecord>();
        using var udp = new UdpClient(ip.AddressFamily);
        foreach (var q in queries) await udp.SendAsync(q, new IPEndPoint(ip, port), ct);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(waitMs);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(wait.Token);
                if (r.RemoteEndPoint.Address.Equals(ip)) records.AddRange(Parse(r.Buffer));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        return records;
    }

    /// <summary>A DNS server's software and version, if it says (<c>version.bind</c>, often turned off).</summary>
    public static async Task<string?> VersionBindAsync(IPAddress ip, CancellationToken ct)
    {
        var r = await AskAsync(ip, 53, [Query("version.bind", Txt, 3, 0x4441)], 1500, ct);
        return r.FirstOrDefault(x => x.Type == Txt)?.Data is { Length: > 0 } v ? v : null;
    }

    /// <summary>What a device says about itself over multicast DNS, asked of it directly.</summary>
    public sealed class MdnsInfo
    {
        public string? HostName;
        /// <summary>Service types (<c>_airplay._tcp</c>) with their instances' names, ports and TXT strings.</summary>
        public readonly List<(string Type, string Instance, int Port, IReadOnlyList<string> Texts)> Services = new();
        public readonly List<string> Types = new();
        public string? Hinfo;
    }

    public static async Task<MdnsInfo> MdnsAsync(IPAddress ip, CancellationToken ct)
    {
        var info = new MdnsInfo();
        string reverse = ip.AddressFamily == AddressFamily.InterNetwork
            ? string.Join(".", ip.GetAddressBytes().Reverse()) + ".in-addr.arpa"
            : string.Join(".", ip.GetAddressBytes().Reverse().SelectMany(b => new[] { (b & 0xF).ToString("x"), (b >> 4).ToString("x") })) + ".ip6.arpa";
        var first = await AskAsync(ip, 5353, [Query("_services._dns-sd._udp.local", Ptr), Query(reverse, Ptr)], 1200, ct);
        info.HostName = first.FirstOrDefault(r => r.Type == Ptr && r.Name.EndsWith(".arpa", StringComparison.OrdinalIgnoreCase))?.Data;
        foreach (var t in first.Where(r => r.Type == Ptr && r.Name.StartsWith("_services.", StringComparison.OrdinalIgnoreCase)).Select(r => r.Data).Distinct())
            info.Types.Add(t);
        if (info.Types.Count == 0 && info.HostName == null) return info;

        // each service's instances, then what they say (asked together: one message a question)
        var all = new List<DnsRecord>(first);
        if (info.Types.Count > 0) all.AddRange(await AskAsync(ip, 5353, info.Types.Take(40).Select(t => Query(t, Ptr)), 1500, ct));
        var instances = all.Where(r => r.Type == Ptr && info.Types.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).Select(r => (Type: r.Name, Name: r.Data)).Distinct().Take(60).ToList();
        var missing = instances.Where(i => !all.Any(r => r.Type == Txt && r.Name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0) all.AddRange(await AskAsync(ip, 5353, missing.Select(i => Query(i.Name, Any)), 1500, ct));
        if (info.HostName is { } host)
        {
            // Apple's devices say their model here
            string device = host.Split('.')[0] + "._device-info._tcp.local";
            all.AddRange(await AskAsync(ip, 5353, [Query(device, Txt), Query(host, Hinfo)], 800, ct));
            if (all.FirstOrDefault(r => r.Type == Txt && r.Name.Equals(device, StringComparison.OrdinalIgnoreCase)) is { } d)
                info.Services.Add(("_device-info._tcp.local", device, 0, d.Texts));
            info.Hinfo = all.FirstOrDefault(r => r.Type == Hinfo)?.Data;
        }
        foreach (var (type, name) in instances)
        {
            if (info.Services.Any(s => s.Instance.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var srv = all.FirstOrDefault(r => r.Type == Srv && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var txt = all.FirstOrDefault(r => r.Type == Txt && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            info.HostName ??= srv?.Target;
            info.Services.Add((type, name, srv?.Port ?? 0, txt?.Texts.Where(s => s.Length > 0).ToList() ?? (IReadOnlyList<string>)[]));
        }
        return info;
    }
}
