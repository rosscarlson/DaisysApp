using System.Text.Json.Serialization;
using DaisysApp.Settings;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Something the Ping tool tests: a host pinged, a DNS server asked for a name, or a TCP port connected to.
/// </summary>
public sealed class NetTarget
{
    public const string PingKind = "Ping", DnsKind = "DNS", TcpKind = "TCP";

    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary><see cref="PingKind"/>, <see cref="DnsKind"/> or <see cref="TcpKind"/>.</summary>
    public string Kind { get; set; } = PingKind;
    /// <summary>TCP: the port connected to.</summary>
    public int Port { get; set; } = 443;
    /// <summary>DNS: the name looked up.</summary>
    public string Query { get; set; } = "www.google.com";
    /// <summary>A response slower than this (ms) shows red.</summary>
    public int AlertMs { get; set; } = 100;

    [JsonIgnore] public string Display => Name.Length > 0 ? Name : Address;

    /// <summary>What's tested, for the list: "1.1.1.1", "DNS 1.1.1.1 → www.google.com", "TCP example.com:443".</summary>
    [JsonIgnore] public string What => Kind switch
    {
        DnsKind => F("DNS {0} → {1}", Address.Trim(), Query.Trim()),
        TcpKind => F("TCP {0}:{1}", Address.Trim(), Port),
        _ => Address.Trim(),
    };

    /// <summary>One key per thing tested (two the same are tested once).</summary>
    [JsonIgnore] public string Key => Kind switch
    {
        DnsKind => $"dns|{Address.Trim()}|{Query.Trim()}".ToLowerInvariant(),
        TcpKind => $"tcp|{Address.Trim()}|{Port}".ToLowerInvariant(),
        _ => $"ping|{Address.Trim()}".ToLowerInvariant(),
    };

    /// <summary>The first run's: your router, your DNS servers (pinged, and the first asked for a name), Cloudflare and Google.</summary>
    public static List<NetTarget> Defaults()
    {
        var list = new List<NetTarget>();
        if (NetInfo.Gateway() is { } gw) list.Add(new NetTarget { Name = T("Router"), Address = gw, AlertMs = 20 });
        var dns = NetInfo.DnsServers().Where(d => d != NetInfo.Gateway() && d is not ("1.1.1.1" or "8.8.8.8")).Take(2).ToList(); // (those two are below anyway)
        for (int i = 0; i < dns.Count; i++) list.Add(new NetTarget { Name = dns.Count > 1 ? F("DNS server {0}", i + 1) : T("DNS server"), Address = dns[i] });
        list.Add(new NetTarget { Name = "Cloudflare", Address = "1.1.1.1" });
        list.Add(new NetTarget { Name = "Google", Address = "8.8.8.8" });
        var first = NetInfo.DnsServers().FirstOrDefault();
        if (first != null) list.Add(new NetTarget { Name = T("DNS lookup"), Address = first, Kind = DnsKind, AlertMs = 150 });
        return list;
    }
}

/// <summary>A widget of the Ping or Speed Test tool: where it is, and how much time its graph shows.</summary>
public sealed class NetWidgetPlace : IWidgetPlace
{
    public const string PingKind = "Ping", SpeedKind = "Speed";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = PingKind;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    /// <summary>Ping: minutes of graph; Speed Test: hours.</summary>
    public int Span { get; set; }
}

/// <summary>Saved in %APPDATA%\DaisysApp\Networking.json: every Networking tool's settings.</summary>
public sealed class NetworkingSettings : ISpeedTestSettings
{
    private const string FileName = "Networking";

    /// <summary>The tool's sub-tab showing.</summary>
    public string? Tab { get; set; }

    // ---------------------------------------------------------------- Ping

    /// <summary>The Ping card's box: off sends nothing at all.</summary>
    public bool PingOn { get; set; } = true;
    public List<NetTarget> Targets { get; set; } = NetTarget.Defaults();
    /// <summary>Seconds between rounds (every target tested at once).</summary>
    public int PingSeconds { get; set; } = 1;
    /// <summary>Each round written to a CSV file a day (logs\network\ping-*.csv).</summary>
    public bool PingLog { get; set; }
    /// <summary>Logging stops by itself at this time (null: when it's switched off).</summary>
    public DateTime? PingLogUntil { get; set; }
    /// <summary>How long logging runs once it's switched on, hours (0: until it's switched off).</summary>
    public int PingLogHours { get; set; }
    /// <summary>How much time the Ping graph and statistics cover, minutes.</summary>
    public int PingSpanMinutes { get; set; } = 5;
    /// <summary>Log files older than this are deleted, days.</summary>
    public int KeepDays { get; set; } = 30;

    // ---------------------------------------------------------------- Speed Test

    /// <summary>The Speed Test card's box: off runs no tests at all.</summary>
    public bool SpeedOn { get; set; } = true;
    /// <summary>Tests on a schedule (each one logged in speedtest.csv).</summary>
    public bool SpeedLog { get; set; }
    public DateTime? SpeedLogUntil { get; set; }
    public int SpeedLogHours { get; set; }
    public int SpeedMinutes { get; set; } = 60;
    public int SpeedSeconds { get; set; } = 5;
    /// <summary>A result below these speeds (Mbit/s) or above this latency (ms) shows orange / red; 0 turns one off.</summary>
    public double SpeedWarnDown { get; set; } = 100;
    public double SpeedBadDown { get; set; } = 25;
    public double SpeedWarnUp { get; set; } = 20;
    public double SpeedBadUp { get; set; } = 5;
    public double SpeedWarnLatency { get; set; } = 100;
    public double SpeedBadLatency { get; set; } = 250;
    /// <summary>How much time the Speed Test graph covers, hours.</summary>
    public int SpeedSpanHours { get; set; } = 24;

    bool ISpeedTestSettings.SpeedTestOn => SpeedOn;
    bool ISpeedTestSettings.SpeedTestEnabled => SpeedLog && !(SpeedLogUntil < DateTime.Now);
    int ISpeedTestSettings.SpeedTestMinutes => SpeedMinutes;
    int ISpeedTestSettings.SpeedTestSeconds => SpeedSeconds;

    // ---------------------------------------------------------------- Wi-Fi

    /// <summary>The Wi-Fi adapter that scans (its GUID); null: the first.</summary>
    public string? WifiAdapter { get; set; }
    /// <summary>Scan again every few seconds while the Wi-Fi tab is showing.</summary>
    public bool WifiAuto { get; set; } = true;
    /// <summary>The channel graph's band: "2.4", "5" or "6".</summary>
    public string WifiBand { get; set; } = "2.4";

    // ---------------------------------------------------------------- Traceroute

    public string TraceHost { get; set; } = "8.8.8.8";
    public int TraceMaxHops { get; set; } = 30;
    /// <summary>Keep tracing (every hop once a second), as mtr does.</summary>
    public bool TraceRepeat { get; set; }
    public List<string> TraceRecent { get; set; } = new();

    // ---------------------------------------------------------------- Port Scan

    public string ScanHost { get; set; } = "";
    /// <summary>"Common", "WellKnown", "All" or "Custom".</summary>
    public string ScanPorts { get; set; } = "Common";
    public string ScanCustom { get; set; } = "1-1024";
    public int ScanTimeoutMs { get; set; } = 600;
    public int ScanParallel { get; set; } = 200;
    /// <summary>List closed and unanswered ports too, not only open ones.</summary>
    public bool ScanShowAll { get; set; }

    // ---------------------------------------------------------------- Host Scan

    public string HostScanHost { get; set; } = "";
    public List<string> HostScanRecent { get; set; } = new();
    /// <summary>"Common" (with the ports devices use), "WellKnown" or "All".</summary>
    public string HostPorts { get; set; } = "Common";
    /// <summary>Try getting in without a password (anonymous FTP, SNMP's default communities, Redis, MQTT).</summary>
    public bool HostLogins { get; set; } = true;
    /// <summary>Run nmap too, when it's installed.</summary>
    public bool HostNmap { get; set; } = true;

    // ---------------------------------------------------------------- Events

    /// <summary>Watch for drops, adapter and Wi-Fi changes in the background.</summary>
    public bool EventsOn { get; set; } = true;
    /// <summary>A Wi-Fi signal under this (dBm) is logged as weak.</summary>
    public int WeakSignalDbm { get; set; } = -75;
    /// <summary>Kinds of event left out of the list.</summary>
    public List<string> EventsHidden { get; set; } = new();

    // ---------------------------------------------------------------- widgets

    public List<NetWidgetPlace> Widgets { get; set; } = new();

    public static NetworkingSettings Load() => JsonStore.Load<NetworkingSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
