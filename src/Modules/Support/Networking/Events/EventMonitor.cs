using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Something that happened to the connection.</summary>
internal sealed record NetEvent(DateTime Time, string Kind, string Text, bool Bad, double Seconds = double.NaN);

/// <summary>
/// Watches for what makes a connection flaky, in the background while the app runs, and keeps a list (30 days, in
/// <c>logs\network\events.csv</c>, and each in the module's log): a Ping target down and back (with how long), an
/// adapter going down or up or changing speed, the internet adapter's address, gateway or DNS changing, Wi-Fi
/// disconnecting, joining, moving to another access point or channel, or its signal going weak, and a speed test failing;
/// and, when Ping targets go down, what that says (<see cref="Diagnose"/>: the router down too, or only the internet, or
/// only some servers). Checks every 5 seconds (and at once when Windows says an address changed).
/// </summary>
internal sealed class EventMonitor : IDisposable
{
    public const string Drop = "Drop", Adapter = "Adapter", Address = "Address", WiFi = "WiFi", Speed = "Speed", Cause = "Cause";
    public static readonly string[] Kinds = [Cause, Drop, Adapter, Address, WiFi, Speed];

    private readonly NetworkingSettings settings;
    private readonly PingMonitor? pings;
    private readonly SpeedTester? speed;
    private readonly Timer timer;
    private readonly object gate = new();
    private readonly List<NetEvent> events = new();
    private Dictionary<string, (OperationalStatus Status, long Speed, string Name)>? adapters;
    private string? addresses;
    private readonly Dictionary<Guid, (WlanLink? Link, int Channel, bool Weak)> wifi = new();
    private DateTime lastSpeed;
    private int busy;
    /// <summary>An outage being looked at (<see cref="Diagnose"/>): when it started, and what it was judged to be.</summary>
    private DateTime? episode;
    private string? judged;
    private bool diagnosing;

    /// <summary>Raised (on any thread) when an event is added or the list cleared.</summary>
    public event Action? Changed;

    public static string FilePath => Path.Combine(PingMonitor.Folder, "events.csv");

    public EventMonitor(NetworkingSettings settings, PingMonitor? pings, SpeedTester? speed)
    {
        this.settings = settings;
        this.pings = pings;
        this.speed = speed;
        timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    public IReadOnlyList<NetEvent> Events
    {
        get { lock (gate) return events.ToList(); }
    }

    public void Start()
    {
        if (pings != null) pings.Outage += OnOutage;
        if (speed != null)
        {
            lastSpeed = speed.Results.LastOrDefault()?.Time ?? DateTime.MinValue;
            speed.Changed += OnSpeed;
        }
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        timer.Change(2000, 5000);
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Task.Delay(1500).ContinueWith(_ => Check());

    private void Add(string kind, string text, bool bad, double seconds = double.NaN)
    {
        if (!settings.EventsOn) return;
        var e = new NetEvent(DateTime.Now, kind, text, bad, seconds);
        lock (gate) events.Add(e);
        if (bad) Log.Here.Warn("Network: " + text); else Log.Here.Info("Network: " + text);
        Append(e);
        Changed?.Invoke();
    }

    private void OnOutage(NetTarget t, bool back, DateTime since, TimeSpan length)
    {
        if (back) Add(Drop, F("{0} answering again after {1}", t.Display, NetUi.Duration(length)), false, length.TotalSeconds);
        else Add(Drop, F("{0} stopped answering ({1}, {2})", t.Display, t.What, Local(t) ? T("local") : T("internet")), true);
        lock (gate)
        {
            if (!back) episode ??= since;
            // (looked at once the other targets have had a round to fail too; again whenever one goes down or comes back)
            if (diagnosing) return;
            diagnosing = true;
        }
        Task.Delay(Math.Clamp(settings.PingSeconds, 1, 15) * 1000 + 1500).ContinueWith(_ => Diagnose());
    }

    /// <summary>A target on this side of the router: the router itself, or a private address (not a provider's
    /// 100.64/10, which is past it).</summary>
    private static bool Local(NetTarget t)
    {
        string a = t.Address.Trim();
        if (a == NetInfo.Gateway()) return true;
        if (!System.Net.IPAddress.TryParse(a, out var ip)) return false;
        var b = ip.GetAddressBytes();
        return NetInfo.IsPrivate(ip) && !(b.Length == 4 && b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    /// <summary>
    /// What the Ping targets down now say: the local ones (the router) down too, the PC's link or the router dropped;
    /// only the internet ones, all of them, the internet connection dropped (past the router); only some, those servers
    /// or the way to them. Added as a Diagnosis when it changes; once every target answers again, how long it lasted.
    /// </summary>
    private void Diagnose()
    {
        lock (gate) diagnosing = false;
        if (pings == null || !settings.EventsOn) return;
        var targets = settings.Targets.Where(t => t.Address.Trim().Length > 0).GroupBy(t => t.Key).Select(g => g.First()).ToList();
        var down = targets.Where(t => pings.DownSince(t.Key) != null).ToList();
        var local = targets.Where(Local).ToList();
        var internet = targets.Except(local).ToList();
        int localDown = down.Count(Local), internetDown = down.Count - localDown;
        string Names(IEnumerable<NetTarget> list) => string.Join(", ", list.Select(t => t.Display));
        string? now;
        if (down.Count == 0) now = null;
        else if (localDown > 0 && localDown == local.Count && internetDown == internet.Count)
            now = F("Everything down, the router too ({0}): the PC's own link (Wi-Fi or cable) or the router dropped", Names(local));
        else if (localDown > 0 && localDown == local.Count)
            now = F("Local targets down ({0}) but internet ones answer: the router isn't answering pings, or it's busy", Names(local));
        else if (localDown > 0)
            now = F("Some local targets down ({0}), others answer: those devices, not the connection", Names(down.Where(Local)));
        else if (internetDown == internet.Count)
            now = local.Count > 0
                ? F("Internet connection dropped: the router answers ({0}), nothing past it does", Names(local))
                : T("Internet connection dropped: every internet target is down (add your router to Ping to tell it from your own link)");
        else
            now = F("Only {0} down; {1} answer: those servers or the way to them, not your connection",
                Names(down), Names(internet.Except(down)));
        DateTime? started;
        string? before;
        lock (gate)
        {
            started = episode;
            before = judged;
            judged = now;
            if (now == null) episode = null;
        }
        if (now != null && now != before) Add(Cause, now, true);
        else if (now == null && before != null && started is DateTime from)
        {
            var length = DateTime.Now - from;
            Add(Cause, F("Over: every target answers again, after {0}", NetUi.Duration(length)), false, length.TotalSeconds);
        }
    }

    private void OnSpeed()
    {
        if (speed == null || speed.Running) return;
        var last = speed.Results.LastOrDefault();
        if (last == null || last.Time <= lastSpeed) return;
        lastSpeed = last.Time;
        if (last.Failed) Add(Speed, F("Speed test failed: {0}", last.Error), true);
    }

    /// <summary>Compares the adapters, addresses and Wi-Fi with last time.</summary>
    private void Check()
    {
        if (!settings.EventsOn || Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            CheckAdapters();
            CheckAddresses();
            CheckWifi();
        }
        catch (Exception ex) { Log.Here.Debug(() => $"Network events: {ex.Message}"); }
        finally { Volatile.Write(ref busy, 0); }
    }

    private void CheckAdapters()
    {
        var now = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) && n.GetPhysicalAddress().GetAddressBytes().Length > 0)
            .ToDictionary(n => n.Id, n => (n.OperationalStatus, n.OperationalStatus == OperationalStatus.Up ? n.Speed : 0L, n.Name));
        if (adapters != null)
            foreach (var (id, (status, linkSpeed, name)) in now)
            {
                if (!adapters.TryGetValue(id, out var before)) { Add(Adapter, F("{0} appeared", name), false); continue; }
                if (before.Status != status)
                    Add(Adapter, status == OperationalStatus.Up ? F("{0} connected ({1})", name, NetUi.LinkSpeed(linkSpeed)) : F("{0} disconnected", name), status != OperationalStatus.Up);
                else if (status == OperationalStatus.Up && before.Speed != linkSpeed && before.Speed > 0 && linkSpeed > 0
                    && NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == id)?.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    // (Wi-Fi's rate changes all the time; a cable's shouldn't)
                    Add(Adapter, F("{0} link speed {1} → {2}", name, NetUi.LinkSpeed(before.Speed), NetUi.LinkSpeed(linkSpeed)), linkSpeed < before.Speed);
            }
        adapters = now;
    }

    private void CheckAddresses()
    {
        var n = NetInfo.Primary();
        string now;
        if (n == null) now = "";
        else
        {
            var ip = n.GetIPProperties();
            now = string.Join(" · ", new[]
            {
                n.Name,
                string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString())),
                F("gateway {0}", string.Join(", ", ip.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString()))),
                F("DNS {0}", string.Join(", ", ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()))),
            });
        }
        if (addresses != null && addresses != now)
            Add(Address, now.Length == 0 ? T("No internet adapter") : F("Now {0}", now), now.Length == 0);
        addresses = now;
    }

    private void CheckWifi()
    {
        foreach (var a in Wlan.Adapters())
        {
            var link = Wlan.Current(a.Id);
            WlanBss? bss = link != null ? Wlan.Networks(a.Id).List.FirstOrDefault(b => b.Bssid == link.Bssid) : null;
            int channel = bss?.Channel ?? 0;
            bool had = wifi.TryGetValue(a.Id, out var before);
            bool weak = bss != null && (before.Weak ? bss.Rssi < settings.WeakSignalDbm + 5 : bss.Rssi < settings.WeakSignalDbm);
            if (had)
            {
                if (before.Link != null && link == null) Add(WiFi, F("Wi-Fi disconnected from {0}", before.Link.Ssid), true);
                else if (before.Link == null && link != null) Add(WiFi, F("Wi-Fi connected to {0} ({1})", link.Ssid, link.Bssid), false);
                else if (before.Link != null && link != null)
                {
                    if (before.Link.Ssid != link.Ssid) Add(WiFi, F("Wi-Fi changed network: {0} → {1}", before.Link.Ssid, link.Ssid), true);
                    else if (before.Link.Bssid != link.Bssid) Add(WiFi, F("Wi-Fi moved to another access point: {0} → {1}", before.Link.Bssid, link.Bssid), false);
                    else if (before.Channel != 0 && channel != 0 && before.Channel != channel) Add(WiFi, F("Wi-Fi channel {0} → {1}", before.Channel, channel), false);
                }
                if (bss != null && weak != before.Weak)
                    Add(WiFi, weak ? F("Wi-Fi signal weak: {0} dBm", bss.Rssi) : F("Wi-Fi signal good again: {0} dBm", bss.Rssi), weak);
            }
            wifi[a.Id] = (link, channel, weak);
        }
    }

    public void Clear()
    {
        lock (gate) events.Clear();
        try { File.Delete(FilePath); } catch { }
        Changed?.Invoke();
    }

    /// <summary>
    /// The events as text to read, or to hand an AI to look at (Export): the PC, the Ping targets (local or internet),
    /// the internet adapter, then every event, oldest first, to the second.
    /// </summary>
    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        var list = Events;
        sb.AppendLine($"Daisy's App network events, {Environment.MachineName}, exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (list.Count > 0) sb.AppendLine($"From {list[0].Time:yyyy-MM-dd HH:mm:ss} to {list[^1].Time:yyyy-MM-dd HH:mm:ss}, {list.Count} events");
        sb.AppendLine();
        sb.AppendLine($"Ping targets (tested every {settings.PingSeconds} s, down after {PingMonitor.DownAfter} missed in a row; Ping {(settings.PingOn ? "on" : "off")}):");
        foreach (var t in settings.Targets.Where(t => t.Address.Trim().Length > 0))
            sb.AppendLine($"  {t.Display}: {t.What} ({(Local(t) ? "local" : "internet")})");
        if (NetInfo.Primary() is { } n)
            sb.AppendLine($"Internet adapter: {n.Name} ({NetInfo.TypeName(n.NetworkInterfaceType)}, {NetUi.LinkSpeed(n.Speed)}), gateway {NetInfo.Gateway() ?? "none"}, DNS {string.Join(", ", NetInfo.DnsServers())}");
        sb.AppendLine();
        sb.AppendLine("Time                 Kind      !  Event");
        foreach (var e in list)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss}  {1,-8}  {2}  {3}{4}", e.Time, e.Kind, e.Bad ? "!" : " ",
                e.Text.ReplaceLineEndings(" "), double.IsNaN(e.Seconds) ? "" : string.Format(CultureInfo.InvariantCulture, " [{0:0.#} s]", e.Seconds)));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- file

    private const string Header = "time,kind,bad,seconds,text";

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var cutoff = DateTime.Now.AddDays(-30);
            int lines = 0;
            foreach (var line in File.ReadLines(FilePath).Skip(1))
            {
                lines++;
                var p = line.Split(',', 5);
                if (p.Length < 5 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < cutoff) continue;
                double s = double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                events.Add(new NetEvent(t, p[1], p[4], p[2] == "1", s));
            }
            if (lines > events.Count + 200) File.WriteAllLines(FilePath, new[] { Header }.Concat(events.Select(Line)));
        }
        catch { }
    }

    private static string Line(NetEvent e) => string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4}",
        e.Time, e.Kind, e.Bad ? 1 : 0, double.IsNaN(e.Seconds) ? "" : e.Seconds.ToString("0.#", CultureInfo.InvariantCulture), e.Text.ReplaceLineEndings(" "));

    private static void Append(NetEvent e)
    {
        try
        {
            Directory.CreateDirectory(PingMonitor.Folder);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
            File.AppendAllText(FilePath, Line(e) + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        timer.Dispose();
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        if (pings != null) pings.Outage -= OnOutage;
        if (speed != null) speed.Changed -= OnSpeed;
    }
}
