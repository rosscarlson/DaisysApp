using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>What a proxied site answers with, to compare a candidate origin against.</summary>
internal sealed record Baseline(int Status, string Title, string Server, string BodyHash, int BodyLen);

/// <summary>A possible origin address Deep Scan turned up, and how well it matches the real site.</summary>
internal sealed class DeepCandidate
{
    public string Ip = "";
    /// <summary>Where it came from ("crt.sh", "subdomain mail.", "SPF", "Shodan", "SecurityTrails"…).</summary>
    public string Source = "";
    public string? HostName;
    public bool Cdn;
    public string? CdnOwner;
    public int Score;
    public string Why = "";
    public int? Status;
    public string? Title;
}

/// <summary>Everything a Deep Scan found: the proxied site's fingerprint, the candidates ranked, and a plain verdict.</summary>
internal sealed class DeepReport
{
    public string Domain = "";
    public Baseline? Site;
    /// <summary>The CDN the domain currently resolves behind, if it's behind one.</summary>
    public string? FrontCdn;
    public readonly List<DeepCandidate> Candidates = new();
    public readonly List<(string Host, string Ip, bool Cdn)> Subdomains = new();
    public readonly List<Fact> Mail = new();
    public readonly List<string> Sources = new();
    public readonly List<string> Log = new();
    public string Verdict = "";
    public Severity VerdictLevel = Severity.Info;

    public void Note(string text) { lock (Log) Log.Add($"{DateTime.Now:T}  {text}"); }
}

/// <summary>
/// Deep Scan: origin-server discovery. Finds the real address of a site hidden behind a reverse proxy or CDN
/// (Cloudflare and the like) using only public data — certificate transparency (crt.sh), subdomain enumeration,
/// mail records (MX, SPF), and, with keys, passive-DNS history (SecurityTrails, ViewDNS) and internet-scan data
/// (Shodan, Censys). Every candidate address is then asked for the site directly (<c>Host:</c> set to the real
/// domain, the certificate not checked) and scored against the proxied site's known-good answer. A hardened origin
/// (firewall allowing only the CDN's addresses, Authenticated Origin Pulls) leaks nothing: Deep Scan says so rather
/// than guessing. Only for domains you own or may test.
/// </summary>
internal static class DeepScanner
{
    /// <summary>Subdomains that often point straight at the origin, past the proxy.</summary>
    private static readonly string[] Leaky =
    [
        "direct", "origin", "dev", "staging", "test", "mail", "webmail", "smtp", "mx", "ftp", "cpanel", "whm",
        "cpcontacts", "autodiscover", "autoconfig", "vpn", "remote", "server", "host", "ns1", "ns2", "admin",
        "portal", "api", "blog", "shop", "m", "mobile", "beta", "old", "backup", "db", "sql", "www",
    ];

    public static async Task<DeepReport> ScanAsync(string host, NetworkingSettings settings, Action<string, double> progress, CancellationToken ct)
    {
        var report = new DeepReport { Domain = host.Trim().TrimEnd('.') };
        if (IPAddress.TryParse(report.Domain, out _))
        {
            report.Verdict = T("Deep Scan needs a domain name, not an address.");
            return report;
        }
        report.Note(F("Deep Scan of {0}", report.Domain));
        Logging.Log.Here.Info($"Deep Scan of {report.Domain}");

        // the proxied site as it is, to compare against, and the CDN it's behind
        progress(T("Reading the site as it is…"), 0.03);
        report.Site = await FetchAsync(report.Domain, null, ct);
        if (report.Site != null) report.Note(F("Site: HTTP {0}, title \"{1}\", server {2}", report.Site.Status, report.Site.Title, report.Site.Server.Length > 0 ? report.Site.Server : "—"));
        var current = await ResolveAllAsync(report.Domain, ct);
        foreach (var ip in current)
        {
            string? cdn = await Cdn.OwnerAsync(ip, ct);
            if (cdn != null) { report.FrontCdn ??= cdn; report.Note(F("{0} → {1} ({2})", report.Domain, ip, cdn)); }
            else report.Note(F("{0} → {1} (not a known CDN)", report.Domain, ip));
        }

        // gather candidate addresses from every source
        var found = new Dictionary<string, (string Source, string? Host)>();
        void Add(IPAddress ip, string source, string? hostName = null)
        {
            string key = ip.ToString();
            if (!found.ContainsKey(key)) found[key] = (source, hostName);
        }
        foreach (var ip in current) Add(ip, T("current DNS"));

        progress(T("Certificate transparency (crt.sh)…"), 0.1);
        var names = await CrtShAsync(report, ct);

        progress(T("Resolving subdomains…"), 0.3);
        await SubdomainsAsync(report, names, (ip, host2) => Add(ip, F("subdomain {0}", host2), host2), ct);

        progress(T("Mail records…"), 0.55);
        await MailAsync(report, (ip, why) => Add(ip, why), ct);

        if (settings.DeepSecurityTrailsKey.Length > 0) { progress(T("SecurityTrails history…"), 0.6); await SecurityTrailsAsync(report, settings.DeepSecurityTrailsKey, Add, ct); }
        if (settings.DeepViewDnsKey.Length > 0) { progress(T("ViewDNS history…"), 0.62); await ViewDnsAsync(report, settings.DeepViewDnsKey, Add, ct); }
        if (settings.DeepShodanKey.Length > 0) { progress(T("Shodan…"), 0.64); await ShodanAsync(report, settings.DeepShodanKey, Add, ct); }
        if (settings.DeepCensysId.Length > 0 && settings.DeepCensysSecret.Length > 0) { progress(T("Censys…"), 0.66); await CensysAsync(report, settings.DeepCensysId, settings.DeepCensysSecret, Add, ct); }

        report.Note(F("{0} candidate addresses from {1} sources", found.Count, report.Sources.Count));

        // which are a CDN, then validate the rest against the site
        progress(T("Checking candidates…"), 0.7);
        var candidates = new List<DeepCandidate>();
        foreach (var (ip, (source, hostName)) in found)
        {
            var addr = IPAddress.Parse(ip);
            string? cdn = await Cdn.OwnerAsync(addr, ct);
            candidates.Add(new DeepCandidate { Ip = ip, Source = source, HostName = hostName, Cdn = cdn != null, CdnOwner = cdn });
        }
        var toCheck = candidates.Where(c => !c.Cdn).Take(40).ToList();
        int done = 0;
        using (var gate = new SemaphoreSlim(8))
        {
            await Task.WhenAll(toCheck.Select(async c =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await ValidateAsync(report, c, ct);
                    int n = Interlocked.Increment(ref done);
                    progress(F("Checking candidate {0} of {1}…", n, toCheck.Count), 0.7 + 0.25 * n / Math.Max(toCheck.Count, 1));
                }
                finally { gate.Release(); }
            }));
        }
        report.Candidates.AddRange(candidates.OrderByDescending(c => !c.Cdn).ThenByDescending(c => c.Score).ThenBy(c => c.Ip));

        Conclude(report);
        progress(T("Done"), 1);
        report.Note(report.Verdict);
        Logging.Log.Here.Info($"Deep Scan of {report.Domain}: {report.Verdict}");
        return report;
    }

    // ---------------------------------------------------------------- the verdict

    private static void Conclude(DeepReport r)
    {
        var best = r.Candidates.Where(c => !c.Cdn).OrderByDescending(c => c.Score).FirstOrDefault();
        int checked2 = r.Candidates.Count(c => !c.Cdn);
        if (best is { Score: >= 70 })
        {
            r.Verdict = F("Likely origin: {0}{1} — {2}% match ({3}).", best.Ip, best.HostName is { Length: > 0 } h ? $" ({h})" : "", best.Score, best.Why);
            r.VerdictLevel = Severity.High;
        }
        else if (best is { Score: >= 40 })
        {
            r.Verdict = F("Possible origin: {0} — {1}% match. Not certain; confirm it before trusting it.", best.Ip, best.Score);
            r.VerdictLevel = Severity.Medium;
        }
        else if (r.FrontCdn != null)
        {
            r.Verdict = F("No origin found. The site is behind {0} and nothing public points past it. A hardened origin (firewall allowing only the CDN, Authenticated Origin Pulls) can't be recovered this way.", r.FrontCdn);
            r.VerdictLevel = Severity.Info;
        }
        else
        {
            r.Verdict = checked2 > 0
                ? F("No strong match. {0} addresses checked, none matched the site well. It may not be behind a proxy, or the origin is hidden.", checked2)
                : T("Nothing to check: no candidate addresses were found.");
            r.VerdictLevel = Severity.Info;
        }
    }

    // ---------------------------------------------------------------- candidate validation

    /// <summary>Asks a candidate for the site directly and scores how well its answer matches the real site's.</summary>
    private static async Task ValidateAsync(DeepReport r, DeepCandidate c, CancellationToken ct)
    {
        if (r.Site == null) { c.Why = T("no known-good site to compare"); return; }
        var got = await FetchAsync(r.Domain, IPAddress.Parse(c.Ip), ct);
        if (got == null) { c.Why = T("no HTTPS answer"); return; }
        c.Status = got.Status;
        c.Title = got.Title;
        int score = 0;
        var why = new List<string>();
        if (got.Status == r.Site.Status) { score += 25; why.Add(T("same status")); }
        if (got.BodyHash == r.Site.BodyHash && got.BodyLen > 0) { score += 50; why.Add(T("identical page")); }
        else if (r.Site.BodyLen > 0 && Math.Abs(got.BodyLen - r.Site.BodyLen) <= r.Site.BodyLen * 0.05) { score += 15; why.Add(T("page about the same size")); }
        if (got.Title.Length > 0 && got.Title == r.Site.Title) { score += 25; why.Add(T("same title")); }
        if (got.Server.Length > 0 && got.Server == r.Site.Server) { score += 15; why.Add(T("same server")); }
        c.Score = Math.Clamp(score, 0, 100);
        c.Why = why.Count > 0 ? string.Join(", ", why) : T("answered, but nothing matched");
    }

    /// <summary>GETs <c>https://domain/</c>, either through normal DNS (<paramref name="via"/> null) or straight at one address.</summary>
    private static async Task<Baseline?> FetchAsync(string domain, IPAddress? via, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        if (via != null)
            handler.ConnectCallback = async (_, c) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await socket.ConnectAsync(via, 443, c); } catch { socket.Dispose(); throw; }
                return new NetworkStream(socket, true);
            };
        try
        {
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) {AppPaths.ShortName}");
            using var resp = await http.GetAsync($"https://{domain}/", HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await ReadCappedAsync(await resp.Content.ReadAsStreamAsync(ct), 262144, ct);
            string body = Encoding.UTF8.GetString(bytes);
            string title = WebUtility.HtmlDecode(Regex.Match(body, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value).Trim();
            if (title.Length > 150) title = title[..150];
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            return new Baseline((int)resp.StatusCode, title, resp.Headers.TryGetValues("Server", out var s) ? string.Join(" ", s) : "", hash, bytes.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
        finally { handler.Dispose(); }
    }

    private static async Task<byte[]> ReadCappedAsync(Stream s, int max, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while (ms.Length < max && (n = await s.ReadAsync(buffer, ct)) > 0) ms.Write(buffer, 0, n);
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- sources

    private static HttpClient MakeClient(int seconds = 20)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(DeepScanner).Assembly.GetName().Version?.ToString(3)}");
        return h;
    }

    private static void Source(DeepReport r, string name) { if (!r.Sources.Contains(name)) r.Sources.Add(name); }

    /// <summary>Hostnames for the domain from certificate transparency logs (crt.sh), the apex and its wildcards.</summary>
    private static async Task<HashSet<string>> CrtShAsync(DeepReport r, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var http = MakeClient();
            string json = await http.GetStringAsync($"https://crt.sh/?q={Uri.EscapeDataString("%." + r.Domain)}&output=json", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("name_value", out var nv) && nv.GetString() is { } value)
                    foreach (var name in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string n = name.TrimStart('*', '.').ToLowerInvariant();
                        if (n.EndsWith(r.Domain, StringComparison.OrdinalIgnoreCase) && !n.Contains(' ')) names.Add(n);
                    }
            Source(r, "crt.sh");
            r.Note(F("crt.sh: {0} names", names.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("crt.sh: {0}", ex.Message)); }
        return names;
    }

    /// <summary>Resolves every name from crt.sh plus a brute list, flagging any that doesn't land on the CDN.</summary>
    private static async Task SubdomainsAsync(DeepReport r, HashSet<string> names, Action<IPAddress, string> add, CancellationToken ct)
    {
        foreach (var leak in Leaky) names.Add($"{leak}.{r.Domain}");
        names.Add(r.Domain);
        Source(r, "subdomains");
        using var gate = new SemaphoreSlim(20);
        var rows = new List<(string Host, string Ip, bool Cdn)>();
        await Task.WhenAll(names.Take(400).Select(async name =>
        {
            await gate.WaitAsync(ct);
            try
            {
                foreach (var ip in await ResolveAllAsync(name, ct))
                {
                    bool cdn = await Cdn.IsCdnAsync(ip, ct);
                    lock (rows) rows.Add((name, ip.ToString(), cdn));
                    if (!cdn) add(ip, name);
                }
            }
            finally { gate.Release(); }
        }));
        lock (rows) r.Subdomains.AddRange(rows.DistinctBy(x => $"{x.Host}|{x.Ip}").OrderBy(x => x.Cdn).ThenBy(x => x.Host));
        r.Note(F("Subdomains: {0} resolved, {1} off the CDN", rows.Select(x => x.Host).Distinct().Count(), rows.Count(x => !x.Cdn)));
    }

    /// <summary>MX hosts and SPF networks: mail servers are often the unproxied origin or share its network.</summary>
    private static async Task MailAsync(DeepReport r, Action<IPAddress, string> add, CancellationToken ct)
    {
        var resolver = NetInfo.DnsServers().Select(IPAddress.Parse).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Parse("1.1.1.1");
        try
        {
            Source(r, "mail");
            var mx = await DnsWire.AskAsync(resolver, 53, [DnsWire.Query(r.Domain, DnsWire.Mx, recursion: true)], 3000, ct);
            foreach (var host in mx.Where(x => x.Type == DnsWire.Mx).Select(x => x.Target).Distinct())
            {
                r.Mail.Add(new Fact("MX", host));
                foreach (var ip in await ResolveAllAsync(host, ct))
                    if (!await Cdn.IsCdnAsync(ip, ct)) add(ip, F("MX {0}", host));
            }
            var txt = await DnsWire.AskAsync(resolver, 53, [DnsWire.Query(r.Domain, DnsWire.Txt, recursion: true)], 3000, ct);
            foreach (var spf in txt.Where(x => x.Type == DnsWire.Txt && x.Data.Contains("v=spf1", StringComparison.OrdinalIgnoreCase)).Select(x => x.Data))
            {
                r.Mail.Add(new Fact("SPF", spf));
                foreach (var m in Regex.Matches(spf, @"ip4:(\d{1,3}(?:\.\d{1,3}){3})(?:/(\d{1,2}))?").Cast<Match>())
                    if ((m.Groups[2].Value is "" or "32") && IPAddress.TryParse(m.Groups[1].Value, out var ip) && !await Cdn.IsCdnAsync(ip, ct))
                        add(ip, "SPF ip4");
                foreach (var m in Regex.Matches(spf, @"include:([^\s]+)").Cast<Match>())
                    foreach (var ip in await ResolveAllAsync(m.Groups[1].Value, ct))
                        if (!await Cdn.IsCdnAsync(ip, ct)) add(ip, F("SPF include:{0}", m.Groups[1].Value));
            }
            r.Note(F("Mail: {0} records", r.Mail.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("Mail: {0}", ex.Message)); }
    }

    private static async Task SecurityTrailsAsync(DeepReport r, string key, Action<IPAddress, string, string?> add, CancellationToken ct)
    {
        try
        {
            using var http = MakeClient();
            http.DefaultRequestHeaders.Add("APIKEY", key);
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.securitytrails.com/v1/history/{r.Domain}/dns/a", ct));
            int n = 0;
            if (doc.RootElement.TryGetProperty("records", out var records))
                foreach (var rec in records.EnumerateArray())
                    if (rec.TryGetProperty("values", out var values))
                        foreach (var v in values.EnumerateArray())
                            if (v.TryGetProperty("ip", out var ipv) && IPAddress.TryParse(ipv.GetString(), out var ip)) { add(ip, "SecurityTrails", null); n++; }
            Source(r, "SecurityTrails");
            r.Note(F("SecurityTrails: {0} historical addresses", n));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("SecurityTrails: {0}", ex.Message)); }
    }

    private static async Task ViewDnsAsync(DeepReport r, string key, Action<IPAddress, string, string?> add, CancellationToken ct)
    {
        try
        {
            using var http = MakeClient();
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.viewdns.info/iphistory/?domain={Uri.EscapeDataString(r.Domain)}&apikey={key}&output=json", ct));
            int n = 0;
            if (doc.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("records", out var records))
                foreach (var rec in records.EnumerateArray())
                    if (rec.TryGetProperty("ip", out var ipv) && IPAddress.TryParse(ipv.GetString(), out var ip)) { add(ip, "ViewDNS", null); n++; }
            Source(r, "ViewDNS");
            r.Note(F("ViewDNS: {0} historical addresses", n));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("ViewDNS: {0}", ex.Message)); }
    }

    private static async Task ShodanAsync(DeepReport r, string key, Action<IPAddress, string, string?> add, CancellationToken ct)
    {
        try
        {
            using var http = MakeClient();
            string q = Uri.EscapeDataString($"ssl.cert.subject.cn:\"{r.Domain}\",hostname:\"{r.Domain}\"");
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.shodan.io/shodan/host/search?key={key}&query={q}", ct));
            int n = 0;
            if (doc.RootElement.TryGetProperty("matches", out var matches))
                foreach (var mm in matches.EnumerateArray())
                    if (mm.TryGetProperty("ip_str", out var ipv) && IPAddress.TryParse(ipv.GetString(), out var ip)) { add(ip, "Shodan", null); n++; }
            Source(r, "Shodan");
            r.Note(F("Shodan: {0} hosts", n));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("Shodan: {0}", ex.Message)); }
    }

    private static async Task CensysAsync(DeepReport r, string id, string secret, Action<IPAddress, string, string?> add, CancellationToken ct)
    {
        try
        {
            using var http = MakeClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{id}:{secret}")));
            string q = Uri.EscapeDataString($"services.tls.certificates.leaf_data.names: {r.Domain}");
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://search.censys.io/api/v2/hosts/search?q={q}&per_page=50", ct));
            int n = 0;
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("hits", out var hits))
                foreach (var hit in hits.EnumerateArray())
                    if (hit.TryGetProperty("ip", out var ipv) && IPAddress.TryParse(ipv.GetString(), out var ip)) { add(ip, "Censys", null); n++; }
            Source(r, "Censys");
            r.Note(F("Censys: {0} hosts", n));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { r.Note(F("Censys: {0}", ex.Message)); }
    }

    /// <summary>A host's IPv4 addresses (empty when it doesn't resolve); each lookup is given a few seconds.</summary>
    private static async Task<List<IPAddress>> ResolveAllAsync(string host, CancellationToken ct)
    {
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(4000);
            var all = await Dns.GetHostAddressesAsync(host, wait.Token);
            return all.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(); }
    }
}
