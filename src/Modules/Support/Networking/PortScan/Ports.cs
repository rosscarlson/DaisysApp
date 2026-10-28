namespace DaisysApp.Applets.Support.Networking;

/// <summary>Well-known TCP ports: each one's service and a line about it. More about any port: <see cref="InfoUrl"/>.</summary>
internal static class Ports
{
    public sealed record Known(int Port, string Service, string About);

    /// <summary>A page about the port (SpeedGuide's port database: what uses it, and known risks).</summary>
    public static string InfoUrl(int port) => $"https://www.speedguide.net/port.php?port={port}";

    public static Known? Of(int port) => byPort.GetValueOrDefault(port);

    /// <summary>The ports "Common" scans: these, in order.</summary>
    public static IReadOnlyList<int> Common => list.Select(k => k.Port).ToList();

    private static readonly Known[] list =
    [
        new(20, "FTP data", "File transfers' data channel"),
        new(21, "FTP", "File Transfer Protocol: unencrypted file transfers and logins"),
        new(22, "SSH", "Secure Shell: encrypted remote logins, SFTP and tunnels"),
        new(23, "Telnet", "Unencrypted remote logins: shouldn't be open"),
        new(25, "SMTP", "Mail servers sending mail to each other"),
        new(53, "DNS", "Domain name lookups (over TCP for large answers and zone transfers)"),
        new(67, "DHCP", "Hands out addresses on a network (UDP)"),
        new(69, "TFTP", "Trivial file transfers, for network boot and devices"),
        new(80, "HTTP", "Web pages, unencrypted; often a device's setup page"),
        new(81, "HTTP (alt)", "Another web server, often a camera or router"),
        new(88, "Kerberos", "Windows domain logins"),
        new(110, "POP3", "Collecting mail, unencrypted"),
        new(111, "RPC bind", "Unix remote procedure calls (NFS)"),
        new(119, "NNTP", "Usenet news"),
        new(123, "NTP", "Clock time (UDP)"),
        new(135, "MS RPC", "Windows remote procedure calls"),
        new(137, "NetBIOS name", "Old Windows naming (UDP)"),
        new(139, "NetBIOS session", "Old Windows file sharing"),
        new(143, "IMAP", "Mail folders, unencrypted"),
        new(161, "SNMP", "Device monitoring (UDP)"),
        new(179, "BGP", "Routers telling each other their routes"),
        new(389, "LDAP", "Directories (Active Directory), unencrypted"),
        new(443, "HTTPS", "Encrypted web pages"),
        new(445, "SMB", "Windows file and printer sharing"),
        new(465, "SMTPS", "Sending mail, encrypted"),
        new(500, "IKE", "VPN key exchange (IPsec, UDP)"),
        new(515, "LPD", "Printing (Unix line printer)"),
        new(548, "AFP", "Apple file sharing"),
        new(554, "RTSP", "Streaming video, often cameras"),
        new(587, "Submission", "Sending mail from a mail program"),
        new(631, "IPP", "Printing over the network"),
        new(636, "LDAPS", "Directories, encrypted"),
        new(853, "DNS over TLS", "Encrypted DNS lookups"),
        new(873, "rsync", "File synchronisation"),
        new(902, "VMware", "VMware's remote console"),
        new(989, "FTPS data", "Encrypted FTP's data channel"),
        new(990, "FTPS", "Encrypted FTP"),
        new(993, "IMAPS", "Mail folders, encrypted"),
        new(995, "POP3S", "Collecting mail, encrypted"),
        new(1080, "SOCKS", "A proxy"),
        new(1194, "OpenVPN", "OpenVPN"),
        new(1433, "SQL Server", "Microsoft SQL Server"),
        new(1521, "Oracle", "Oracle database"),
        new(1701, "L2TP", "VPN (L2TP)"),
        new(1723, "PPTP", "Old VPN (PPTP): not secure"),
        new(1883, "MQTT", "Messages between smart-home devices, unencrypted"),
        new(1900, "SSDP / UPnP", "Devices announcing themselves on the network"),
        new(2049, "NFS", "Unix file sharing"),
        new(2082, "cPanel", "Web hosting control panel"),
        new(2083, "cPanel (TLS)", "Web hosting control panel, encrypted"),
        new(2375, "Docker", "Docker's API, unencrypted: shouldn't be open"),
        new(2376, "Docker (TLS)", "Docker's API, encrypted"),
        new(3000, "Dev web", "Development web servers (Node, Grafana)"),
        new(3128, "Squid", "A web proxy"),
        new(3260, "iSCSI", "Network disks"),
        new(3306, "MySQL", "MySQL / MariaDB database"),
        new(3389, "RDP", "Windows Remote Desktop"),
        new(3478, "STUN", "Finding the way through NAT (calls, games)"),
        new(4444, "Metasploit", "Often malware or test tools"),
        new(5000, "UPnP / dev", "Synology's web page, Flask, AirPlay"),
        new(5001, "Synology (TLS)", "Synology's web page, encrypted"),
        new(5060, "SIP", "Internet phone calls"),
        new(5061, "SIP (TLS)", "Internet phone calls, encrypted"),
        new(5353, "mDNS", "Bonjour / local names (UDP)"),
        new(5432, "PostgreSQL", "PostgreSQL database"),
        new(5555, "ADB", "Android's debug bridge: shouldn't be open"),
        new(5800, "VNC (web)", "VNC remote control in a browser"),
        new(5900, "VNC", "VNC remote control"),
        new(5985, "WinRM", "Windows remote management"),
        new(5986, "WinRM (TLS)", "Windows remote management, encrypted"),
        new(6379, "Redis", "Redis database"),
        new(6443, "Kubernetes", "Kubernetes' API"),
        new(6881, "BitTorrent", "BitTorrent"),
        new(7000, "AirPlay", "Apple AirPlay"),
        new(8000, "HTTP (alt)", "Another web server"),
        new(8008, "HTTP (alt)", "Another web server; Chromecast"),
        new(8009, "AJP / Cast", "Tomcat's connector; Chromecast"),
        new(8080, "HTTP proxy / alt", "A web proxy or another web server"),
        new(8081, "HTTP (alt)", "Another web server"),
        new(8086, "InfluxDB", "InfluxDB database"),
        new(8123, "Home Assistant", "Home Assistant's web page"),
        new(8291, "Winbox", "MikroTik routers' setup"),
        new(8443, "HTTPS (alt)", "Another encrypted web server; often a device's setup"),
        new(8883, "MQTT (TLS)", "Messages between smart-home devices, encrypted"),
        new(8888, "HTTP (alt)", "Another web server; Jupyter"),
        new(9000, "Portainer / dev", "Portainer, PHP-FPM, SonarQube"),
        new(9090, "Cockpit / Prometheus", "Server dashboards"),
        new(9100, "Printing (raw)", "Printers' raw printing port"),
        new(9200, "Elasticsearch", "Elasticsearch"),
        new(10000, "Webmin", "Webmin server admin"),
        new(10050, "Zabbix agent", "Zabbix's agent"),
        new(10051, "Zabbix server", "Zabbix's server"),
        new(11211, "Memcached", "Memcached"),
        new(25565, "Minecraft", "Minecraft server"),
        new(27017, "MongoDB", "MongoDB database"),
        new(32400, "Plex", "Plex Media Server"),
        new(38863, "Daisy's App", "Daisy's App's remote control (AI)"),
        new(51820, "WireGuard", "WireGuard VPN (UDP)"),
    ];

    private static readonly Dictionary<int, Known> byPort = list.ToDictionary(k => k.Port);

    /// <summary>"22, 80, 8000-8100" → those ports, in order, each once; null if it doesn't read.</summary>
    public static List<int>? Parse(string text)
    {
        var ports = new SortedSet<int>();
        foreach (var part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (range.Length == 1 && int.TryParse(range[0], out int p) && p is >= 1 and <= 65535) ports.Add(p);
            else if (range.Length == 2 && int.TryParse(range[0], out int a) && int.TryParse(range[1], out int b) && a is >= 1 and <= 65535 && b >= a && b <= 65535)
                for (int i = a; i <= b; i++) ports.Add(i);
            else return null;
        }
        return ports.Count > 0 ? ports.ToList() : null;
    }
}
