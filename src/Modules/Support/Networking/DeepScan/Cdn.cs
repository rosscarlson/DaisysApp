using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Which addresses belong to a CDN or reverse proxy (Cloudflare, Fastly, CloudFront…), so Deep Scan can leave them out:
/// a proxy's address isn't the origin it hides. The ranges come from the CDNs' own published lists, fetched once and
/// kept (a week) in <c>cache\cdn-ranges.json</c>, so they stay current without a hardcoded snapshot. A few that don't
/// publish a machine-readable list are matched by their well-known ranges.
/// </summary>
internal static class Cdn
{
    /// <summary>A CIDR block (IPv4 or IPv6), with the CDN it belongs to.</summary>
    private readonly record struct Block(byte[] Network, int Bits, bool V6, string Owner)
    {
        public bool Has(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if ((b.Length == 16) != V6 || b.Length != Network.Length) return false;
            for (int i = 0, bits = Bits; i < b.Length && bits > 0; i++, bits -= 8)
            {
                int mask = bits >= 8 ? 0xFF : 0xFF << (8 - bits) & 0xFF;
                if ((b[i] & mask) != (Network[i] & mask)) return false;
            }
            return true;
        }
    }

    private static readonly SemaphoreSlim gate = new(1, 1);
    private static List<Block>? blocks;

    /// <summary>The CDN or proxy an address belongs to, or null when it's none we know (so a possible origin).</summary>
    public static async Task<string?> OwnerAsync(IPAddress ip, CancellationToken ct)
    {
        var all = await LoadAsync(ct);
        return all.FirstOrDefault(b => b.Has(ip)).Owner;
    }

    public static async Task<bool> IsCdnAsync(IPAddress ip, CancellationToken ct) => await OwnerAsync(ip, ct) != null;

    private static string CachePath => Path.Combine(Path.GetDirectoryName(AppPaths.LogFolder)!, "cache", "cdn-ranges.json");

    private static async Task<List<Block>> LoadAsync(CancellationToken ct)
    {
        if (blocks != null) return blocks;
        await gate.WaitAsync(ct);
        try
        {
            if (blocks != null) return blocks;
            var cidrs = ReadCache() ?? await FetchAsync(ct);
            var list = new List<Block>();
            foreach (var (cidr, owner) in cidrs)
                if (Parse(cidr) is { } b) list.Add(b with { Owner = owner });
            list.AddRange(Static);
            blocks = list;
            return list;
        }
        finally { gate.Release(); }
    }

    /// <summary>The cached list (CIDR + owner), or null when it's missing or over a week old.</summary>
    private static List<(string Cidr, string Owner)>? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) > TimeSpan.FromDays(7)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var list = doc.RootElement.EnumerateArray()
                .Select(e => (e.GetProperty("cidr").GetString() ?? "", e.GetProperty("owner").GetString() ?? ""))
                .Where(x => x.Item1.Length > 0).ToList();
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    private static void WriteCache(List<(string Cidr, string Owner)> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(list.Select(x => new { cidr = x.Cidr, owner = x.Owner })));
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"CDN ranges cache: {ex.Message}"); }
    }

    private static async Task<List<(string Cidr, string Owner)>> FetchAsync(CancellationToken ct)
    {
        var list = new List<(string, string)>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(Cdn).Assembly.GetName().Version?.ToString(3)}");

        async Task<string?> Get(string url) { try { return await http.GetStringAsync(url, ct); } catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.Log.Here.Debug(() => $"CDN list {url}: {ex.Message}"); return null; } }

        // Cloudflare: one CIDR a line
        foreach (var url in new[] { "https://www.cloudflare.com/ips-v4", "https://www.cloudflare.com/ips-v6" })
            if (await Get(url) is { } text)
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (line.Contains('/')) list.Add((line, "Cloudflare"));

        // Fastly: { "addresses": [...], "ipv6_addresses": [...] }
        if (await Get("https://api.fastly.com/public-ip-list") is { } fastly)
            try
            {
                using var doc = JsonDocument.Parse(fastly);
                foreach (var name in new[] { "addresses", "ipv6_addresses" })
                    if (doc.RootElement.TryGetProperty(name, out var arr))
                        foreach (var e in arr.EnumerateArray())
                            if (e.GetString() is { } c) list.Add((c, "Fastly"));
            }
            catch (Exception ex) { Logging.Log.Here.Debug(() => $"Fastly list: {ex.Message}"); }

        // AWS: CloudFront's prefixes out of the whole list
        if (await Get("https://ip-ranges.amazonaws.com/ip-ranges.json") is { } aws)
            try
            {
                using var doc = JsonDocument.Parse(aws);
                void Take(string arrName, string field) { if (doc.RootElement.TryGetProperty(arrName, out var arr)) foreach (var e in arr.EnumerateArray()) if (e.TryGetProperty("service", out var s) && s.GetString() == "CLOUDFRONT" && e.TryGetProperty(field, out var p) && p.GetString() is { } c) list.Add((c, "CloudFront")); }
                Take("prefixes", "ip_prefix");
                Take("ipv6_prefixes", "ipv6_prefix");
            }
            catch (Exception ex) { Logging.Log.Here.Debug(() => $"AWS list: {ex.Message}"); }

        if (list.Count > 0) WriteCache(list);
        else Logging.Log.Here.Info("CDN ranges: nothing fetched (offline?), using the built-in list only");
        return list;
    }

    /// <summary>A CIDR string ("1.2.3.0/24", "2a06:98c0::/29") to a block, or null if it can't be read.</summary>
    private static Block? Parse(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out int bits)) return null;
        bool v6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
        return new Block(ip.GetAddressBytes(), Math.Clamp(bits, 0, v6 ? 128 : 32), v6, "");
    }

    /// <summary>CDNs and proxies that don't publish a list: their well-known ranges (enough to flag, not exhaustive).</summary>
    private static readonly Block[] Static = new[]
    {
        // Akamai
        ("23.192.0.0/11", "Akamai"), ("23.32.0.0/11", "Akamai"), ("104.64.0.0/10", "Akamai"), ("184.24.0.0/13", "Akamai"), ("2.16.0.0/13", "Akamai"),
        // Google / GCP front ends
        ("34.64.0.0/10", "Google"), ("35.184.0.0/13", "Google"), ("130.211.0.0/22", "Google"), ("35.190.0.0/17", "Google"),
        // Imperva / Incapsula
        ("45.60.0.0/16", "Imperva"), ("149.126.72.0/21", "Imperva"), ("199.83.128.0/21", "Imperva"),
        // Sucuri
        ("192.124.249.0/24", "Sucuri"), ("185.93.228.0/22", "Sucuri"),
        // StackPath / Highwinds
        ("151.139.0.0/16", "StackPath"),
        // Microsoft Azure Front Door / CDN (partial)
        ("147.243.0.0/16", "Azure"),
    }.Select(x => Parse(x.Item1) is { } b ? b with { Owner = x.Item2 } : default).Where(b => b.Network != null).ToArray();
}
