using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// A deeper look at one open TCP port, as nmap's version detection and default scripts do: what's really speaking on it
/// (web server, TLS, SSH, FTP, mail, SMB, Remote Desktop, VNC, databases, printers…), its product and version, its
/// certificate, and whatever it gives away. With <c>logins</c>, the checks that try getting in without a password
/// (anonymous FTP, Redis, MQTT, VNC). Only on devices you own or may test.
/// </summary>
internal static class ServiceProbe
{
    private const int WaitMs = 4000;
    private static readonly int[] TlsOnly = [465, 636, 853, 990, 993, 995, 5061, 8883];
    private static readonly int[] TlsFirst = [443, 2376, 3269, 4443, 5001, 5986, 6443, 7443, 8006, 8443, 9443, 10443];
    /// <summary>Ports that aren't tried as a web server (they speak something else, and some don't like surprises).</summary>
    private static readonly int[] NotHttp = [21, 22, 23, 25, 53, 110, 135, 139, 143, 445, 587, 1433, 1521, 1883, 3306, 3389, 5432, 5555, 5900, 6379, 9100, 11211, 27017];

    public static async Task<ServiceInfo> ProbeAsync(IPAddress ip, string hostName, PortResult port, bool logins, CancellationToken ct)
    {
        var info = new ServiceInfo { Raw = port.RawBanner };
        string banner = port.RawBanner;
        int p = port.Port;
        try
        {
            if (banner.StartsWith("SSH-")) await SshAsync(ip, p, info, ct);
            else if (banner.StartsWith("RFB ")) await VncAsync(ip, p, banner, info, logins, ct);
            else if (banner.StartsWith("220") && (p is 21 or 2121 || banner.Contains("FTP", StringComparison.OrdinalIgnoreCase))) await FtpAsync(ip, p, banner, info, logins, ct);
            else if (banner.StartsWith("220") && (p is 25 or 587 or 2525 || banner.Contains("SMTP", StringComparison.OrdinalIgnoreCase))) await SmtpAsync(ip, p, banner, info, ct);
            else if (banner.StartsWith("+OK")) Mail(info, "POP3", banner, p != 995);
            else if (banner.StartsWith("* OK")) Mail(info, "IMAP", banner, p != 993);
            else if (p == 23 || banner.Contains("login:", StringComparison.OrdinalIgnoreCase) && p != 22) Telnet(info, banner);
            else if (p == 445) await Smb.ProbeAsync(ip, info, ct);
            else if (p == 3389) await RdpAsync(ip, p, info, ct);
            else if (p == 6379) await RedisAsync(ip, p, info, logins, ct);
            else if (p == 11211) await MemcachedAsync(ip, p, info, ct);
            else if (p is 1883) await MqttAsync(ip, p, info, logins, ct);
            else if (p == 9100) await PjlAsync(ip, p, info, ct);
            else if (p == 5555) Adb(info);
            else if (p == 53) await DnsAsync(ip, info, ct);
            else if (p is 135 or 139) Windows(info, p);
            else if (p is 3306 or 5432 or 1433 or 1521 or 27017) Database(info, p, banner);
            else if (TlsOnly.Contains(p)) await TlsAsync(ip, hostName, p, info, ct);
            else if (!NotHttp.Contains(p))
            {
                bool tls = TlsFirst.Contains(p);
                if (!await WebAsync(ip, hostName, p, tls, info, ct) && !await WebAsync(ip, hostName, p, !tls, info, ct) && !tls)
                    await TlsAsync(ip, hostName, p, info, ct);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { info.Add(T("Probe"), T("No answer in time")); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { info.Add(T("Probe"), ex.Message); }
        if (info.Product.Length > 0 && info.Version.Length > 0)
            info.Facts.Add(new Fact(T("Known vulnerabilities"), F("Search NVD for {0}", info.ProductText), ServiceInfo.NvdUrl(info.ProductText)));
        return info;
    }

    // ---------------------------------------------------------------- talking to a port

    private static async Task<Stream> OpenAsync(IPAddress ip, int port, CancellationToken ct)
    {
        var client = new TcpClient(ip.AddressFamily);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(WaitMs);
        try { await client.ConnectAsync(ip, port, wait.Token); }
        catch { client.Dispose(); throw; }
        return new OwnedStream(client);
    }

    /// <summary>A TcpClient's stream that closes the client with it.</summary>
    private sealed class OwnedStream(TcpClient client) : Stream
    {
        private readonly NetworkStream s = client.GetStream();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => s.Flush();
        public override int Read(byte[] buffer, int offset, int count) => s.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => s.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => s.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => s.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => s.WriteAsync(buffer, ct);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => s.WriteAsync(buffer, offset, count, ct);
        protected override void Dispose(bool disposing) { if (disposing) { s.Dispose(); client.Dispose(); } base.Dispose(disposing); }
    }

    /// <summary>Reads what comes within the wait (up to <paramref name="max"/> bytes; stops early once <paramref name="done"/> says so).</summary>
    public static async Task<byte[]> ReadAsync(Stream s, CancellationToken ct, int max = 65536, int waitMs = WaitMs, Func<List<byte>, bool>? done = null)
    {
        var all = new List<byte>();
        var buffer = new byte[8192];
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(waitMs);
        try
        {
            while (all.Count < max)
            {
                int n = await s.ReadAsync(buffer, wait.Token);
                if (n == 0) break;
                all.AddRange(buffer.AsSpan(0, n).ToArray());
                if (done?.Invoke(all) == true) break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (IOException) { }
        return all.ToArray();
    }

    private static async Task<string> AskLineAsync(Stream s, string send, CancellationToken ct, int waitMs = 2500)
    {
        await s.WriteAsync(Encoding.ASCII.GetBytes(send), ct);
        var reply = await ReadAsync(s, ct, 8192, waitMs, b => b.Count > 2 && b[^1] == '\n' && !LastLineContinues(b));
        return Encoding.Latin1.GetString(reply);
    }

    /// <summary>A multi-line FTP/SMTP reply goes on while its last line is "250-…" rather than "250 …".</summary>
    private static bool LastLineContinues(List<byte> b)
    {
        var lines = Encoding.Latin1.GetString(b.ToArray()).TrimEnd('\r', '\n').Split('\n');
        string last = lines[^1];
        return last.Length >= 4 && char.IsDigit(last[0]) && last[3] == '-';
    }

    // ---------------------------------------------------------------- web servers and TLS

    private sealed record TlsResult(X509Certificate2? Cert, SslPolicyErrors Errors, SslProtocols Protocol, string Cipher);

    private static async Task<(Stream Stream, TlsResult Tls)> TlsStreamAsync(IPAddress ip, string hostName, int port, CancellationToken ct)
    {
        var raw = await OpenAsync(ip, port, ct);
        X509Certificate2? cert = null;
        var errors = SslPolicyErrors.None;
        var ssl = new SslStream(raw, false);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(WaitMs);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = IPAddress.TryParse(hostName, out _) ? "" : hostName,
                // every certificate is accepted: it's looked at, not trusted
                RemoteCertificateValidationCallback = (_, c, _, e) => { if (c != null) cert = new X509Certificate2(c); errors = e; return true; },
            }, wait.Token);
        }
        catch { await ssl.DisposeAsync(); throw; }
        return (ssl, new TlsResult(cert, errors, ssl.SslProtocol, ssl.NegotiatedCipherSuite.ToString()));
    }

    private static async Task TlsAsync(IPAddress ip, string hostName, int port, ServiceInfo info, CancellationToken ct)
    {
        try
        {
            var (stream, tls) = await TlsStreamAsync(ip, hostName, port, ct);
            await stream.DisposeAsync();
            info.Protocol = "TLS";
            Certificate(info, tls, hostName);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException) { }
    }

    /// <summary>A web page: GET / (or <paramref name="path"/>), over TLS or not. False if nothing that speaks HTTP answered.</summary>
    private static async Task<bool> WebAsync(IPAddress ip, string hostName, int port, bool tls, ServiceInfo info, CancellationToken ct, string path = "/")
    {
        Stream s;
        TlsResult? t = null;
        try
        {
            if (tls) (s, t) = await TlsStreamAsync(ip, hostName, port, ct);
            else s = await OpenAsync(ip, port, ct);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested) { return false; }
        string response;
        await using (s)
        {
            string host = port is 80 or 443 ? hostName : $"{hostName}:{port}";
            await s.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) DaisysApp\r\nAccept: */*\r\nConnection: close\r\n\r\n"), ct);
            response = Encoding.UTF8.GetString(await ReadAsync(s, ct, 262144));
        }
        if (!response.StartsWith("HTTP/")) return false;
        info.Protocol = tls ? "HTTPS" : "HTTP";
        if (t != null) Certificate(info, t, hostName);
        info.Url = $"{(tls ? "https" : "http")}://{(ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString())}:{port}/";
        int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string head = split > 0 ? response[..split] : response, body = split > 0 ? response[(split + 4)..] : "";
        var lines = head.Split("\r\n");
        var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(a => a.Length == 2)
            .GroupBy(a => a[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(a => a[1].Trim())), StringComparer.OrdinalIgnoreCase);
        string H(string name) => headers.GetValueOrDefault(name) ?? "";
        string title = WebUtility.HtmlDecode(Regex.Match(body, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value).Trim();
        if (title.Length > 150) title = title[..150] + "…";
        info.Add(T("Answer"), lines[0]);
        info.Add(T("Server"), H("Server"));
        info.Add(T("Powered by"), H("X-Powered-By"));
        info.Add(T("Page title"), title);
        info.Add(T("Goes to"), H("Location"));
        info.Add(T("Asks for a login"), H("WWW-Authenticate"));
        var cookies = Regex.Matches(H("Set-Cookie"), @"(?:^|,\s*)([A-Za-z0-9_.-]+)=").Select(m => m.Groups[1].Value).Distinct().ToList();
        if (cookies.Count > 0) info.Add(T("Cookies"), string.Join(", ", cookies));
        foreach (var name in new[] { "Strict-Transport-Security", "Content-Security-Policy", "X-Frame-Options" })
            if (H(name).Length > 0) info.Add(name, H(name));
        info.Raw = head + (body.Length > 0 ? "\n\n" + body[..Math.Min(body.Length, 4000)] : "");

        string server = H("Server");
        var m = Regex.Match(server, @"^([A-Za-z][\w .-]*?)[/ ]v?(\d[\w.-]*)");
        if (m.Success) { info.Product = m.Groups[1].Value.Trim(); info.Version = m.Groups[2].Value; }
        else if (server.Length > 0) info.Product = server;
        if (m.Success) info.Findings.Add(new Finding(Severity.Info, T("Web server says its version"), F("{0}: an attacker knows what to look up.", server)));
        if (!tls && H("WWW-Authenticate").StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
            info.Findings.Add(new Finding(Severity.Medium, T("Password sent unencrypted"), F("Port {0} asks for a Basic login over plain HTTP.", port)));
        else if (!tls && Regex.IsMatch(body, @"type\s*=\s*[""']?password", RegexOptions.IgnoreCase))
            info.Findings.Add(new Finding(Severity.Medium, T("Login page without encryption"), F("Port {0} has a password box on a plain HTTP page.", port)));
        if (Regex.IsMatch(title, @"^Index of /", RegexOptions.IgnoreCase))
            info.Findings.Add(new Finding(Severity.Low, T("Folder listing"), F("Port {0} lists its files to anyone.", port)));
        if (body.Contains("\"cluster_name\"") && body.Contains("You Know, for Search"))
        {
            info.Product = "Elasticsearch";
            info.Version = Regex.Match(body, @"""number""\s*:\s*""([^""]+)""").Groups[1].Value;
            info.Findings.Add(new Finding(Severity.High, T("Elasticsearch without a password"), T("Anyone on the network can read and change its data.")));
        }
        Hints(info, string.Join(" ", server, title, H("WWW-Authenticate"), H("X-Powered-By")));

        // what some devices say on pages of their own
        if (port == 8008 && path == "/") await WebAsync(ip, hostName, port, false, Extra(info, "Chromecast"), ct, "/setup/eureka_info?params=name,device_info,build_info");
        if (port == 8060 && path == "/") await WebAsync(ip, hostName, port, false, Extra(info, "Roku"), ct, "/query/device-info");
        if (port == 2375 && path == "/")
        {
            info.Findings.Add(new Finding(Severity.High, T("Docker's API without TLS"), T("Anyone who can reach it controls the host."), "https://docs.docker.com/engine/security/protect-access/"));
            await WebAsync(ip, hostName, port, false, Extra(info, "Docker"), ct, "/version");
        }
        if (path != "/") DeviceJson(info, body);
        return true;
    }

    /// <summary>A second page's facts go in the same service, marked by what it is.</summary>
    private static ServiceInfo Extra(ServiceInfo info, string what)
    {
        info.Product = info.Product.Length == 0 ? what : info.Product;
        return info;
    }

    /// <summary>A device's own page (Chromecast, Roku, Docker): its name, model and software.</summary>
    private static void DeviceJson(ServiceInfo info, string body)
    {
        foreach (var (key, label) in new[] { ("name", T("Name")), ("model_name", T("Model")), ("manufacturer", T("Maker")), ("cast_build_revision", T("Software")),
            ("friendly-device-name", T("Name")), ("model-name", T("Model")), ("vendor-name", T("Maker")), ("software-version", T("Software")), ("serial-number", T("Serial")),
            ("Version", T("Version")), ("Os", T("System")), ("KernelVersion", T("Kernel")) })
        {
            var m = Regex.Match(body, $@"""{Regex.Escape(key)}""\s*:\s*""([^""]+)""|<{Regex.Escape(key)}>([^<]+)</{Regex.Escape(key)}>");
            if (m.Success) info.Add(label, m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[2].Value);
        }
        if (body.Contains("<device-info>")) info.DeviceHints.Add(("tv", "Roku"));
        if (body.Contains("cast_build_revision") || body.Contains("\"ssdp_udn\"")) info.DeviceHints.Add(("tv", "Chromecast"));
    }

    /// <summary>Words in a web server's name or page title that say what the device is.</summary>
    private static readonly (string Pattern, string Kind)[] WebWords =
    [
        (@"router|gateway|openwrt|dd-wrt|tomato|pfsense|opnsense|mikrotik|routeros|unifi|edgeos|netgear|linksys|tp-link|asuswrt|fritz!box|ubiquiti|eero|orbi|archer|livebox|xfinity", "router"),
        (@"printer|laserjet|officejet|deskjet|envy|epson|brother|canon|kyocera|xerox|lexmark|ricoh|\bews\b|cups", "printer"),
        (@"synology|diskstation|qnap|truenas|freenas|unraid|readynas|wd my cloud|nas\b", "nas"),
        (@"ipcam|hikvision|dahua|\bnvr\b|\bdvr\b|camera|reolink|axis|amcrest|foscam|blue iris", "camera"),
        (@"home assistant|homebridge|hubitat|smartthings|philips hue|tasmota|esphome|shelly|tuya|openhab|node-red", "smarthome"),
        (@"plex|jellyfin|emby|kodi|sonos|roku|chromecast|bravia|webos|tizen|smart ?tv|vizio|android tv|fire tv", "tv"),
        (@"microsoft-iis|microsoft-httpapi", "windows"),
        (@"idrac|ilo\b|ipmi|supermicro|proxmox|esxi|vmware|vcenter", "server"),
        (@"xbox|playstation|nintendo", "console"),
    ];

    private static void Hints(ServiceInfo info, string text)
    {
        foreach (var (pattern, kind) in WebWords)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            if (kind == "windows") info.OsHints.Add("Windows");
            else info.DeviceHints.Add((kind, m.Value));
        }
    }

    private static void Certificate(ServiceInfo info, TlsResult t, string hostName)
    {
        info.Add(T("TLS"), $"{Protocol(t.Protocol)} · {t.Cipher}");
#pragma warning disable SYSLIB0039 // (looking for them)
        if (t.Protocol is SslProtocols.Tls or SslProtocols.Tls11)
#pragma warning restore SYSLIB0039
            info.Findings.Add(new Finding(Severity.Medium, T("Old TLS"), F("It agreed to {0}, which is no longer safe.", Protocol(t.Protocol))));
        if (t.Cert is not { } c) return;
        info.Add(T("Certificate for"), c.GetNameInfo(X509NameType.SimpleName, false));
        info.Add(T("Subject"), c.Subject);
        info.Add(T("Issued by"), c.Issuer);
        info.Add(T("Valid"), F("{0:d} to {1:d}", c.NotBefore, c.NotAfter));
        var san = c.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
        if (san != null) info.Add(T("Other names"), san.Format(false));
        string key = c.PublicKey.Oid.FriendlyName ?? "";
        int bits = c.GetRSAPublicKey()?.KeySize ?? c.GetECDsaPublicKey()?.KeySize ?? 0;
        info.Add(T("Key"), bits > 0 ? $"{key} {bits}" : key);
        info.Add(T("Signature"), c.SignatureAlgorithm.FriendlyName);
        info.Add(T("Fingerprint (SHA-256)"), Convert.ToHexString(c.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)));
        bool selfSigned = c.Subject == c.Issuer;
        if (c.NotAfter < DateTime.Now)
            info.Findings.Add(new Finding(Severity.Medium, T("Certificate expired"), F("It ran out on {0:d}.", c.NotAfter)));
        if (selfSigned)
            info.Findings.Add(new Finding(Severity.Low, T("Self-signed certificate"), T("Browsers warn about it, and can't tell it from an impostor's.")));
        else if (t.Errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            info.Findings.Add(new Finding(Severity.Low, T("Certificate not trusted"), T("Windows doesn't trust who issued it.")));
        if (c.GetRSAPublicKey() is { KeySize: < 2048 } rsa)
            info.Findings.Add(new Finding(Severity.Medium, T("Weak key"), F("An RSA key of {0} bits: 2048 or more is safe.", rsa.KeySize)));
        if (c.SignatureAlgorithm.FriendlyName?.Contains("sha1", StringComparison.OrdinalIgnoreCase) == true || c.SignatureAlgorithm.FriendlyName?.Contains("md5", StringComparison.OrdinalIgnoreCase) == true)
            info.Findings.Add(new Finding(Severity.Medium, T("Weak signature"), F("Signed with {0}.", c.SignatureAlgorithm.FriendlyName)));
        Hints(info, c.Subject + " " + c.Issuer);
    }

    private static string Protocol(SslProtocols p) => p switch
    {
        SslProtocols.Tls13 => "TLS 1.3",
        SslProtocols.Tls12 => "TLS 1.2",
#pragma warning disable SYSLIB0039 // (named to say they're old)
        SslProtocols.Tls11 => "TLS 1.1",
        SslProtocols.Tls => "TLS 1.0",
#pragma warning restore SYSLIB0039
        _ => p.ToString(),
    };

    // ---------------------------------------------------------------- SSH

    private static readonly string[] WeakSsh = ["diffie-hellman-group1-sha1", "diffie-hellman-group-exchange-sha1", "ssh-dss", "arcfour", "3des-cbc", "blowfish-cbc", "hmac-md5", "hmac-sha1-96", "aes128-cbc", "aes256-cbc", "aes192-cbc"];

    private static async Task SshAsync(IPAddress ip, int port, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "SSH";
        await using var s = await OpenAsync(ip, port, ct);
        var hello = await ReadAsync(s, ct, 4096, 2500, b => b.Contains((byte)'\n'));
        string banner = Encoding.Latin1.GetString(hello).Split('\n')[0].Trim();
        info.Add(T("Says"), banner);
        var m = Regex.Match(banner, @"^SSH-[\d.]+-([A-Za-z]+)[_-]?([\w.]*)\s*(.*)$");
        if (m.Success)
        {
            info.Product = m.Groups[1].Value == "OpenSSH" && banner.Contains("for_Windows") ? "OpenSSH for Windows" : m.Groups[1].Value;
            info.Version = m.Groups[2].Value.Replace("for_Windows_", "");
            string rest = m.Groups[3].Value;
            foreach (var os in new[] { "Ubuntu", "Debian", "Raspbian", "FreeBSD", "Fedora", "CentOS", "Red Hat" })
                if (rest.Contains(os, StringComparison.OrdinalIgnoreCase)) info.OsHints.Add(os);
            if (banner.Contains("Windows", StringComparison.OrdinalIgnoreCase)) info.OsHints.Add("Windows");
            if (info.Product.Equals("dropbear", StringComparison.OrdinalIgnoreCase)) info.DeviceHints.Add(("router", "Dropbear SSH"));
            if (banner.Contains("Cisco", StringComparison.OrdinalIgnoreCase) || banner.Contains("ROSSSH")) info.DeviceHints.Add(("router", banner));
        }
        // what it offers: its key exchange, host keys, ciphers and MACs (sent right after the names are swapped)
        await s.WriteAsync(Encoding.ASCII.GetBytes("SSH-2.0-DaisysApp_Scan\r\n"), ct);
        int nl = Array.IndexOf(hello, (byte)'\n');
        var rest2 = hello.Skip(nl + 1).ToList();
        if (rest2.Count < 6) rest2.AddRange(await ReadAsync(s, ct, 16384, 2500, b => b.Count >= 4 && b.Count >= 4 + (b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3])));
        var k = rest2.ToArray();
        if (k.Length < 22 || k[5] != 20) return;
        int at = 6 + 16;
        string List()
        {
            int len = k[at] << 24 | k[at + 1] << 16 | k[at + 2] << 8 | k[at + 3];
            string v = Encoding.ASCII.GetString(k, at + 4, Math.Min(len, k.Length - at - 4));
            at += 4 + len;
            return v;
        }
        string kex = List(), hostKeys = List(), enc = List(); List(); string mac = List();
        info.Add(T("Key exchange"), kex.Replace(",", ", "));
        info.Add(T("Host keys"), hostKeys.Replace(",", ", "));
        info.Add(T("Ciphers"), enc.Replace(",", ", "));
        info.Add(T("MACs"), mac.Replace(",", ", "));
        var weak = string.Join(",", kex, hostKeys, enc, mac).Split(',').Where(a => WeakSsh.Any(w => a.StartsWith(w))).Distinct().ToList();
        if (weak.Count > 0)
            info.Findings.Add(new Finding(Severity.Medium, T("Weak SSH algorithms"), string.Join(", ", weak), "https://www.ssh-audit.com/hardening_guides.html"));
    }

    // ---------------------------------------------------------------- FTP, mail, Telnet

    private static async Task FtpAsync(IPAddress ip, int port, string banner, ServiceInfo info, bool logins, CancellationToken ct)
    {
        info.Protocol = "FTP";
        info.Add(T("Says"), PortProbe.FirstLine(banner));
        Product(info, banner, @"(vsFTPd|ProFTPD|Pure-FTPd|FileZilla Server|Microsoft FTP Service|Serv-U)[ /]*v?([\d.]*)");
        info.Findings.Add(new Finding(Severity.Medium, T("FTP unencrypted"), T("Logins and files cross the network unencrypted: SFTP or FTPS instead.")));
        if (!logins) return;
        await using var s = await OpenAsync(ip, port, ct);
        await ReadAsync(s, ct, 4096, 2500, b => b.Count > 2 && b[^1] == '\n' && !LastLineContinues(b));
        string user = await AskLineAsync(s, "USER anonymous\r\n", ct);
        string pass = user.StartsWith("331") ? await AskLineAsync(s, "PASS scan@example.com\r\n", ct) : user;
        bool anonymous = pass.StartsWith("230");
        info.Add(T("Anonymous login"), anonymous ? T("Allowed") : T("Refused"));
        if (anonymous) info.Findings.Add(new Finding(Severity.High, T("Anonymous FTP"), T("Anyone can log in without a password."), "https://nmap.org/nsedoc/scripts/ftp-anon.html"));
        try { await AskLineAsync(s, "QUIT\r\n", ct, 500); } catch { }
    }

    private static async Task SmtpAsync(IPAddress ip, int port, string banner, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "SMTP";
        info.Add(T("Says"), PortProbe.FirstLine(banner));
        Product(info, banner, @"(Postfix|Exim|Sendmail|Microsoft ESMTP MAIL Service|hMailServer|MailEnable)[ /]*v?([\d.]*)");
        await using var s = await OpenAsync(ip, port, ct);
        await ReadAsync(s, ct, 4096, 2500, b => b.Count > 2 && b[^1] == '\n' && !LastLineContinues(b));
        string ehlo = await AskLineAsync(s, "EHLO scan.local\r\n", ct);
        var ext = ehlo.Split('\n').Skip(1).Select(l => l.Length > 4 ? l[4..].Trim() : "").Where(l => l.Length > 0).ToList();
        info.Add(T("Offers"), string.Join(", ", ext));
        if (port != 465 && !ext.Any(e => e.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase)))
            info.Findings.Add(new Finding(Severity.Low, T("Mail without encryption"), T("It doesn't offer STARTTLS.")));
        try { await AskLineAsync(s, "QUIT\r\n", ct, 500); } catch { }
    }

    private static void Mail(ServiceInfo info, string protocol, string banner, bool plain)
    {
        info.Protocol = protocol;
        info.Add(T("Says"), PortProbe.FirstLine(banner));
        Product(info, banner, @"(Dovecot|Courier|Cyrus|Microsoft Exchange)[ /]*v?([\d.]*)");
        if (plain) info.Findings.Add(new Finding(Severity.Low, F("{0} unencrypted", protocol), T("Passwords cross the network in the clear unless it's switched to TLS first.")));
    }

    private static void Telnet(ServiceInfo info, string banner)
    {
        info.Protocol = "Telnet";
        info.Add(T("Says"), PortProbe.FirstLine(banner));
        info.Findings.Add(new Finding(Severity.High, T("Telnet"), T("Logins cross the network unencrypted: switch it off, use SSH."), "https://www.speedguide.net/port.php?port=23"));
        info.DeviceHints.Add(("router", "Telnet"));
    }

    private static void Product(ServiceInfo info, string banner, string pattern)
    {
        var m = Regex.Match(banner, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return;
        info.Product = m.Groups[1].Value;
        info.Version = m.Groups[2].Value.TrimEnd('.');
        if (m.Groups[1].Value.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) info.OsHints.Add("Windows");
    }

    // ---------------------------------------------------------------- Remote Desktop, VNC

    private static async Task RdpAsync(IPAddress ip, int port, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "RDP";
        info.OsHints.Add("Windows");
        info.DeviceHints.Add(("computer", "Remote Desktop"));
        // asks for the old RDP security (no TLS, no NLA), then TLS without NLA: what it agrees to says how it's set
        async Task<(int Type, int Value)> Ask(byte protocols)
        {
            await using var s = await OpenAsync(ip, port, ct);
            byte[] req = [3, 0, 0, 19, 14, 0xE0, 0, 0, 0, 0, 0, 1, 0, 8, 0, protocols, 0, 0, 0];
            await s.WriteAsync(req, ct);
            var r = await ReadAsync(s, ct, 64, 3000, b => b.Count >= 4 && b.Count >= (b[2] << 8 | b[3]));
            if (r.Length < 11) return (-1, 0);
            if (r.Length < 19) return (0, 0); // an old server: no negotiation at all
            return (r[11], r[15] | r[16] << 8 | r[17] << 16 | r[18] << 24);
        }
        var plain = await Ask(0);
        bool nla;
        if (plain.Type == 2 || plain.Type == 0)
        {
            info.Findings.Add(new Finding(Severity.Medium, T("Remote Desktop's old security"), T("It accepts RDP's own encryption, without TLS or Network Level Authentication."), "https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/clients/remote-desktop-allow-access"));
            nla = false;
        }
        else
        {
            var tls = plain.Type == 3 && plain.Value == 1 ? await Ask(1) : plain;
            nla = !(tls.Type == 2 && tls.Value == 1);
        }
        info.Add(T("Network Level Authentication"), nla ? T("Required") : T("Not required"));
        if (!nla) info.Findings.Add(new Finding(Severity.Medium, T("Remote Desktop without NLA"), T("Anyone can reach the login screen without a password first (and its flaws, like BlueKeep).")));
        try
        {
            // TLS (or NLA, which starts with TLS) agreed, then the handshake: its certificate carries the computer's name
            await using var s = await OpenAsync(ip, port, ct);
            await s.WriteAsync(new byte[] { 3, 0, 0, 19, 14, 0xE0, 0, 0, 0, 0, 0, 1, 0, 8, 0, 3, 0, 0, 0 }, ct);
            var r = await ReadAsync(s, ct, 64, 3000, b => b.Count >= 4 && b.Count >= (b[2] << 8 | b[3]));
            if (r.Length >= 19 && r[11] == 2 && r[15] is 1 or 2)
            {
                X509Certificate2? cert = null;
                await using var ssl = new SslStream(s, true, (_, c, _, _) => { if (c != null) cert = new X509Certificate2(c); return true; });
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(WaitMs);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "" }, wait.Token);
                if (cert != null) info.Add(T("Computer"), cert.GetNameInfo(X509NameType.SimpleName, false));
                info.Add(T("TLS"), $"{Protocol(ssl.SslProtocol)} · {ssl.NegotiatedCipherSuite}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
    }

    private static async Task VncAsync(IPAddress ip, int port, string banner, ServiceInfo info, bool logins, CancellationToken ct)
    {
        info.Protocol = "VNC";
        info.Product = "VNC";
        info.Version = banner.Length > 4 ? banner[4..].Trim() : "";
        await using var s = await OpenAsync(ip, port, ct);
        var hello = await ReadAsync(s, ct, 12, 2500, b => b.Count >= 12);
        if (hello.Length < 12) return;
        await s.WriteAsync(hello, ct); // the same version back
        var r = await ReadAsync(s, ct, 64, 2500, b => b.Count >= 1 && (b[0] == 0 || b.Count >= 1 + b[0]));
        if (r.Length == 0) return;
        var types = Encoding.Latin1.GetString(hello).StartsWith("RFB 003.003") ? new List<int> { r.Length >= 4 ? r[3] : 0 } : r.Skip(1).Take(r[0]).Select(b => (int)b).ToList();
        info.Add(T("Security"), string.Join(", ", types.Select(t => t switch { 1 => T("none"), 2 => T("VNC password"), 5 or 6 => "RA2", 16 => "Tight", 18 => "TLS", 19 => "VeNCrypt", 30 or 35 => "Apple", _ => t.ToString() })));
        if (types.Contains(1)) info.Findings.Add(new Finding(Severity.High, T("VNC without a password"), T("Anyone on the network can see and control the screen.")));
        else if (types.Contains(2)) info.Findings.Add(new Finding(Severity.Low, T("VNC password"), T("VNC's own password is at most 8 characters and easily guessed; use a VPN or SSH.")));
        _ = logins;
    }

    // ---------------------------------------------------------------- databases, caches, messages

    private static async Task RedisAsync(IPAddress ip, int port, ServiceInfo info, bool logins, CancellationToken ct)
    {
        info.Protocol = "Redis";
        info.Product = "Redis";
        if (!logins) return;
        await using var s = await OpenAsync(ip, port, ct);
        string r = await AskLineAsync(s, "INFO server\r\n", ct);
        if (r.StartsWith("-NOAUTH") || r.Contains("Authentication required")) { info.Add(T("Password"), T("Required")); return; }
        info.Version = Regex.Match(r, @"redis_version:([\d.]+)").Groups[1].Value;
        info.Add(T("System"), Regex.Match(r, @"os:([^\r\n]+)").Groups[1].Value);
        if (r.Contains("redis_version")) info.Findings.Add(new Finding(Severity.High, T("Redis without a password"), T("Anyone on the network can read and change its data (and often run commands)."), "https://redis.io/docs/latest/operate/oss_and_stack/management/security/"));
    }

    private static async Task MemcachedAsync(IPAddress ip, int port, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "Memcached";
        info.Product = "Memcached";
        await using var s = await OpenAsync(ip, port, ct);
        string r = await AskLineAsync(s, "version\r\n", ct);
        if (!r.StartsWith("VERSION")) return;
        info.Version = r[7..].Trim();
        info.Findings.Add(new Finding(Severity.Medium, T("Memcached open"), T("Anyone on the network can read what it holds.")));
    }

    private static async Task MqttAsync(IPAddress ip, int port, ServiceInfo info, bool logins, CancellationToken ct)
    {
        info.Protocol = "MQTT";
        info.Product = "MQTT";
        info.DeviceHints.Add(("smarthome", "MQTT"));
        info.Findings.Add(new Finding(Severity.Low, T("MQTT unencrypted"), T("Messages and passwords cross the network in the clear: port 8883 uses TLS.")));
        if (!logins) return;
        var id = Encoding.ASCII.GetBytes("daisysapp-scan");
        var body = new List<byte> { 0, 4, (byte)'M', (byte)'Q', (byte)'T', (byte)'T', 4, 2, 0, 30, 0, (byte)id.Length };
        body.AddRange(id);
        var packet = new List<byte> { 0x10, (byte)body.Count };
        packet.AddRange(body);
        await using var s = await OpenAsync(ip, port, ct);
        await s.WriteAsync(packet.ToArray(), ct);
        var r = await ReadAsync(s, ct, 4, 3000, b => b.Count >= 4);
        if (r.Length < 4 || r[0] != 0x20) return;
        info.Add(T("Without a password"), r[3] == 0 ? T("Accepted") : T("Refused"));
        if (r[3] == 0) info.Findings.Add(new Finding(Severity.Medium, T("MQTT accepts anyone"), T("Anyone on the network can read and send its messages (and switch things).")));
        try { await s.WriteAsync(new byte[] { 0xE0, 0 }, ct); } catch { }
    }

    private static void Database(ServiceInfo info, int port, string banner)
    {
        (info.Protocol, info.Product) = port switch
        {
            3306 => ("MySQL", banner.Contains("MariaDB") ? "MariaDB" : "MySQL"),
            5432 => ("PostgreSQL", "PostgreSQL"),
            1433 => ("SQL Server", "SQL Server"),
            1521 => ("Oracle", "Oracle"),
            _ => ("MongoDB", "MongoDB"),
        };
        var v = Regex.Match(banner, @"(\d+\.\d+\.\d+[\w.-]*)");
        if (v.Success) info.Version = v.Groups[1].Value;
        if (banner.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)) info.OsHints.Add("Ubuntu");
        if (banner.Contains("debian", StringComparison.OrdinalIgnoreCase)) info.OsHints.Add("Debian");
        if (port == 1433) info.OsHints.Add("Windows");
        info.Findings.Add(new Finding(Severity.Low, F("{0} reachable", info.Product), T("A database open to the whole network: only the programs that use it need to reach it.")));
        info.DeviceHints.Add(("server", info.Product));
    }

    // ---------------------------------------------------------------- printers, phones, DNS, Windows

    private static async Task PjlAsync(IPAddress ip, int port, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "PJL";
        info.DeviceHints.Add(("printer", T("Raw printing port")));
        await using var s = await OpenAsync(ip, port, ct);
        await s.WriteAsync(Encoding.ASCII.GetBytes("\x1B%-12345X@PJL INFO ID\r\n\x1B%-12345X\r\n"), ct);
        string r = Encoding.Latin1.GetString(await ReadAsync(s, ct, 2048, 3000, b => b.Contains((byte)'\f')));
        var model = Regex.Match(r, "\"([^\"]+)\"").Groups[1].Value;
        if (model.Length == 0) model = PortProbe.Printable(r.Replace("@PJL INFO ID", ""));
        info.Product = model;
        info.Add(T("Model"), model);
        info.Findings.Add(new Finding(Severity.Low, T("Raw printing open"), T("Anyone on the network can print, and send it PJL commands.")));
    }

    private static void Adb(ServiceInfo info)
    {
        info.Protocol = "ADB";
        info.DeviceHints.Add(("tv", "Android debug bridge"));
        info.OsHints.Add("Android");
        info.Findings.Add(new Finding(Severity.High, T("Android debugging over the network"), T("Anyone who connects can install apps and run commands: switch off network (wireless) debugging.")));
    }

    private static async Task DnsAsync(IPAddress ip, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "DNS";
        info.DeviceHints.Add(("router", T("DNS server")));
        if (await DnsWire.VersionBindAsync(ip, ct) is { } v)
        {
            info.Add(T("Says it's"), v);
            info.Product = v;
            info.Findings.Add(new Finding(Severity.Info, T("DNS server says its version"), v));
        }
    }

    private static void Windows(ServiceInfo info, int port)
    {
        info.Protocol = port == 135 ? "MS RPC" : "NetBIOS";
        info.OsHints.Add("Windows");
        info.DeviceHints.Add(("computer", port == 135 ? "MS RPC" : "NetBIOS"));
    }
}
