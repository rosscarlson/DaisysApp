using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>The PC's network adapters: the one the internet goes through, its gateway and DNS servers.</summary>
internal static class NetInfo
{
    /// <summary>The adapter Windows sends internet traffic through (its route to 8.8.8.8), or the first that's up with a gateway.</summary>
    public static NetworkInterface? Primary()
    {
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
            if (GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes()), out uint index) == 0)
                if (all.FirstOrDefault(n => IndexOf(n) == index) is { } best) return best;
            return all.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)));
        }
        catch { return null; }
    }

    public static int IndexOf(NetworkInterface n)
    {
        try { return n.GetIPProperties().GetIPv4Properties()?.Index ?? -1; }
        catch { return -1; }
    }

    /// <summary>The internet adapter's IPv4 default gateway (your router).</summary>
    public static string? Gateway()
    {
        try
        {
            return Primary()?.GetIPProperties().GatewayAddresses.Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
        }
        catch { return null; }
    }

    /// <summary>The internet adapter's DNS servers, IPv4 first.</summary>
    public static List<string> DnsServers()
    {
        try
        {
            return Primary()?.GetIPProperties().DnsAddresses
                .Where(a => !a.IsIPv6SiteLocal || a.AddressFamily == AddressFamily.InterNetwork)
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .Select(a => a.ToString()).Distinct().ToList() ?? new();
        }
        catch { return new(); }
    }

    /// <summary>A private, loopback or link-local address (no place on a map, nobody to look up).</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] <= 31
            || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] >= 64 && b[1] <= 127; // (100.64/10: a provider's own NAT)
    }

    public static string TypeName(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => T("Wi-Fi"),
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => T("Ethernet"),
        NetworkInterfaceType.Ppp => "PPP",
        NetworkInterfaceType.Tunnel => T("Tunnel"),
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => T("Mobile broadband"),
        _ => t.ToString(),
    };

    public static string Mac(NetworkInterface n)
    {
        var b = n.GetPhysicalAddress().GetAddressBytes();
        return b.Length == 0 ? "" : string.Join(":", b.Select(x => x.ToString("X2")));
    }

    /// <summary>Resolves a name to an IPv4 address (an address is returned as it is); null if it can't.</summary>
    public static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct = default)
    {
        host = host.Trim();
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try
        {
            var all = await Dns.GetHostAddressesAsync(host, ct);
            return all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.FirstOrDefault();
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    [DllImport("iphlpapi.dll")] private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);
}

/// <summary>Reverse lookups (an address's name), each asked once and remembered.</summary>
internal static class Names
{
    private static readonly ConcurrentDictionary<string, Task<string?>> cache = new();

    public static Task<string?> ReverseAsync(string ip) => cache.GetOrAdd(ip, Lookup);

    private static async Task<string?> Lookup(string ip)
    {
        try
        {
            var t = Dns.GetHostEntryAsync(IPAddress.Parse(ip));
            if (await Task.WhenAny(t, Task.Delay(4000)) != t)
            {
                // Given up on: its "no such host" later would be an unobserved task exception.
                _ = t.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return null;
            }
            string name = (await t).HostName;
            return string.IsNullOrEmpty(name) || name == ip ? null : name;
        }
        catch { return null; }
    }
}

/// <summary>Where an address is, roughly (its provider's city), from ip-api.com.</summary>
internal sealed record GeoInfo(string Ip, double Lat, double Lon, string City, string Region, string Country, string CountryCode, string Isp, string As)
{
    public string Place => string.Join(", ", new[] { City, Region != City ? Region : "", CountryCode }.Where(s => s.Length > 0));
}

/// <summary>
/// Address locations from ip-api.com (free, no account, up to 100 addresses a request, 15 requests a minute), each
/// asked once and remembered. Private addresses aren't asked. A provider's location, not the PC's, and only roughly.
/// </summary>
internal static class Geo
{
    private const string Fields = "status,country,countryCode,regionName,city,lat,lon,isp,as,query";
    private static readonly HttpClient http = MakeClient();
    private static readonly ConcurrentDictionary<string, GeoInfo?> cache = new();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(Geo).Assembly.GetName().Version?.ToString(3)}");
        return h;
    }

    /// <summary>The locations of these addresses (those that have one).</summary>
    public static async Task<Dictionary<string, GeoInfo>> LookupAsync(IEnumerable<string> ips, CancellationToken ct = default)
    {
        var wanted = ips.Distinct().Where(ip => IPAddress.TryParse(ip, out var a) && !NetInfo.IsPrivate(a)).ToList();
        var ask = wanted.Where(ip => !cache.ContainsKey(ip)).Take(100).ToList();
        if (ask.Count > 0)
        {
            try
            {
                using var body = new StringContent(JsonSerializer.Serialize(ask), System.Text.Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync($"http://ip-api.com/batch?fields={Fields}", body, ct);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (Parse(e) is var (ip, info)) cache[ip] = info;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.Log.Here.Debug(() => $"Address locations: {ex.Message}"); }
        }
        return wanted.Select(ip => cache.GetValueOrDefault(ip)).Where(g => g != null).ToDictionary(g => g!.Ip, g => g!);
    }

    /// <summary>This PC's public address and where its provider puts it.</summary>
    public static async Task<GeoInfo?> MineAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"http://ip-api.com/json/?fields={Fields}", ct));
            return Parse(doc.RootElement)?.Info;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logging.Log.Here.Debug(() => $"Public address: {ex.Message}");
            return null;
        }
    }

    private static (string Ip, GeoInfo? Info)? Parse(JsonElement e)
    {
        string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double D(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
        string ip = S("query");
        if (ip.Length == 0) return null;
        if (S("status") != "success") return (ip, null);
        return (ip, new GeoInfo(ip, D("lat"), D("lon"), S("city"), S("regionName"), S("country"), S("countryCode"), S("isp"), S("as")));
    }
}
