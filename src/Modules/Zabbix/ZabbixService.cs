using System.Windows.Threading;
using DaisysApp.Applets.Zabbix.Api;
using DaisysApp.Logging;
using Point = DaisysApp.Applets.Zabbix.Api.ZabbixClient.Point;

namespace DaisysApp.Applets.Zabbix;

/// <summary>
/// The connection to the Zabbix server and what's been read from it: every host, what each host's box shows, the
/// problems, the items of the hosts being looked at, and their recent history. Asks again every
/// <see cref="ZabbixSettings.RefreshSeconds"/> while anything is showing (the tab, a widget, a history window) and not
/// at all otherwise. On the UI thread throughout (the calls are awaited there).
/// </summary>
public sealed class ZabbixService : IDisposable
{
    /// <summary>The most history kept here (the tiles, the widgets and the live history window); longer is read when it's asked for.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromHours(24);

    public ZabbixSettings Settings { get; }
    private readonly DispatcherTimer timer = new();
    private ZabbixClient? client;
    private bool busy, disposed;
    private int failures;

    public ZabbixService(ZabbixSettings settings)
    {
        Settings = settings;
        timer.Tick += (_, _) => _ = RefreshAsync();
        ApplyInterval();
    }

    // ---------------------------------------------------------------- state

    public IReadOnlyList<ZHost> Hosts { get; private set; } = Array.Empty<ZHost>();
    public IReadOnlyList<ZProblem> Problems { get; private set; } = Array.Empty<ZProblem>();
    /// <summary>The items the host boxes show, by host id.</summary>
    public IReadOnlyDictionary<string, List<ZItem>> Summary { get; private set; } = new Dictionary<string, List<ZItem>>();
    public Version? ServerVersion => client?.Version;
    public string? ServerUrl => client?.Url;
    public string? Error { get; private set; }
    public DateTime? Updated { get; private set; }
    public bool Connected => client != null && Error == null;

    /// <summary>Raised on the UI thread after each refresh (and when it failed).</summary>
    public event Action? Changed;

    /// <summary>Tells everything showing to draw again (a setting of what's shown changed).</summary>
    public void RaiseChanged() => Changed?.Invoke();

    public ZHost? Host(string id) => Hosts.FirstOrDefault(h => h.HostId == id);

    public IEnumerable<ZProblem> ProblemsOf(string hostId) => Problems.Where(p => p.HostIds.Contains(hostId));

    /// <summary>The worst problem's severity on a host, or -1 for none.</summary>
    public int WorstSeverity(string hostId) => ProblemsOf(hostId).Select(p => p.Severity).DefaultIfEmpty(-1).Max();

    // ---------------------------------------------------------------- who's looking

    private readonly Dictionary<object, string?> watchers = new(); // each watcher, and the host it wants items of (null: just the boxes)

    /// <summary>Something is showing Zabbix data (of a host, or only the boxes): refresh while it's there.</summary>
    public IDisposable Watch(string? hostId)
    {
        var token = new object();
        watchers[token] = hostId;
        timer.Start();
        bool known = hostId == null || items.ContainsKey(hostId);
        if (!known || Updated == null || DateTime.Now - Updated > Interval) _ = RefreshAsync();
        return new Unwatch(() =>
        {
            watchers.Remove(token);
            if (watchers.Count == 0) timer.Stop();
        });
    }

    private sealed class Unwatch(Action action) : IDisposable
    {
        private Action? a = action;
        public void Dispose() { a?.Invoke(); a = null; }
    }

    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(Settings.RefreshSeconds, 10, 3600));

    public void ApplyInterval() => timer.Interval = Interval;

    // ---------------------------------------------------------------- items and history

    private readonly Dictionary<string, List<ZItem>> items = new(); // by host
    private readonly Dictionary<string, Series> history = new();     // by item id

    private sealed class Series
    {
        public List<Point> Points = new();
        public DateTime From = DateTime.MaxValue; // read from here…
        public DateTime To = DateTime.MinValue;   // …to here
    }

    /// <summary>The host's items, as last read (empty until they've been read).</summary>
    public IReadOnlyList<ZItem> ItemsOf(string hostId) => items.TryGetValue(hostId, out var list) ? list : Array.Empty<ZItem>();

    /// <summary>An item of a host by its key (as saved in graphs and widgets).</summary>
    public ZItem? ItemByKey(string hostId, string key) => ItemsOf(hostId).FirstOrDefault(i => i.Key == key);

    /// <summary>What's kept of an item's history from <paramref name="from"/> on.</summary>
    public IReadOnlyList<Point> HistoryOf(string itemId, DateTime from)
    {
        if (!history.TryGetValue(itemId, out var s)) return Array.Empty<Point>();
        int i = s.Points.FindIndex(p => p.Time >= from);
        return i < 0 ? Array.Empty<Point>() : s.Points.GetRange(i, s.Points.Count - i);
    }

    // item id → the oldest time something showing needs, and when it last asked (what nothing asks for any more is dropped)
    private readonly Dictionary<string, (DateTime From, DateTime Asked)> wanted = new();

    /// <summary>Keep this item's history from <paramref name="from"/> on (read at the next refresh, then kept up to date).</summary>
    public void Want(IEnumerable<ZItem> list, DateTime from)
    {
        var floor = DateTime.Now - Kept;
        if (from < floor) from = floor;
        from = new DateTime(from.Ticks - from.Ticks % TimeSpan.TicksPerMinute); // whole minutes, so everything showing the last hour is one call
        bool missing = false;
        var now = DateTime.Now;
        foreach (var i in list.Where(i => i.IsNumeric))
        {
            wanted[i.ItemId] = (wanted.TryGetValue(i.ItemId, out var w) && w.Asked > now - Interval * 3 && w.From < from ? w.From : from, now);
            if (!history.TryGetValue(i.ItemId, out var s) || s.From > from.AddMinutes(1)) missing = true;
        }
        if (missing && !busy) _ = RefreshHistoryAsync();
    }

    /// <summary>A period's values straight from the server: every value up to two days, hourly trends for longer.</summary>
    public async Task<(Dictionary<string, List<Point>> Points, bool Trends)> ReadAsync(IReadOnlyList<ZItem> list, DateTime from, DateTime to)
    {
        var c = await ClientAsync();
        bool trends = to - from > TimeSpan.FromDays(2);
        var points = trends ? await c.TrendsAsync(list, from, to) : await c.HistoryAsync(list, from, to);
        // trends start an hour or more back: the latest hours from the history, so the graph reaches now
        if (trends)
        {
            var since = points.Values.Where(p => p.Count > 0).Select(p => p[^1].Time.AddHours(1)).DefaultIfEmpty(from).Min();
            if (since < to)
                foreach (var (id, recent) in await c.HistoryAsync(list, since, to))
                {
                    if (!points.TryGetValue(id, out var p)) points[id] = p = new();
                    var last = p.Count > 0 ? p[^1].Time.AddHours(1) : DateTime.MinValue;
                    p.AddRange(recent.Where(r => r.Time >= last));
                }
        }
        return (points, trends);
    }

    // ---------------------------------------------------------------- refreshing

    private async Task<ZabbixClient> ClientAsync()
    {
        if (client != null) return client;
        var c = await ZabbixClient.ConnectAsync(Settings);
        client = c;
        Log.Here.Info($"Connected to Zabbix {c.Version} at {c.Url}");
        if (c.FoundPath != null) Log.Here.Info($"The API was at {c.FoundPath}, not at {Settings.Path}");
        return c;
    }

    /// <summary>Signs in again with the settings as they are now (they changed).</summary>
    public void Reconnect()
    {
        var old = client;
        client = null;
        _ = old?.LogoutAsync(!Settings.UseToken);
        old?.Dispose();
        Hosts = Array.Empty<ZHost>();
        Problems = Array.Empty<ZProblem>();
        Summary = new Dictionary<string, List<ZItem>>();
        items.Clear();
        history.Clear();
        Error = null;
        Updated = null;
        Changed?.Invoke();
        if (watchers.Count > 0) _ = RefreshAsync();
    }

    /// <summary>Reads everything that's showing: the hosts, the problems, the boxes' items, and the watched hosts' items and history.</summary>
    public async Task RefreshAsync()
    {
        if (busy || disposed || !Settings.HasServer) return;
        busy = true;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var c = await ClientAsync();
                    var hosts = await c.HostsAsync();
                    var problems = await c.ProblemsAsync();
                    var summary = (await c.ItemsByKeyAsync(HostMetrics.SummaryKeys)).GroupBy(i => i.HostId).ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var hostId in watchers.Values.OfType<string>().Distinct().ToList())
                        items[hostId] = await c.ItemsAsync(hostId);
                    Hosts = hosts;
                    Problems = problems;
                    Summary = summary;
                    await HistoryStepAsync(c);
                    if (Error != null) Log.Here.Info("Zabbix: reading again");
                    Error = null;
                    failures = 0;
                    Updated = DateTime.Now;
                    if (!Settings.Configured) { Settings.Configured = true; Settings.SetupSkipped = false; Settings.Save(); }
                    break;
                }
                catch (ZabbixException ex) when (attempt == 0 && client != null && IsSignedOut(ex))
                {
                    // the session ended on the server (it was restarted, or it timed out): sign in again, once
                    Log.Here.Info($"Zabbix: signed out ({ex.Message}), signing in again");
                    client.Dispose();
                    client = null;
                }
            }
        }
        catch (Exception ex)
        {
            string message = ex is ZabbixException ? ex.Message : F("Couldn't read from Zabbix: {0}", ex.Message);
            if (message != Error) Log.Here.Warn($"Zabbix: {message}", ex is ZabbixException ? null : ex);
            Error = message;
            // a client that can't sign in is made again next time (the settings may have been fixed)
            if (++failures >= 2 && client != null) { client.Dispose(); client = null; }
        }
        finally
        {
            busy = false;
            if (!disposed) Changed?.Invoke();
        }
    }

    private static bool IsSignedOut(ZabbixException ex) =>
        ex.Message.Contains("Session terminated", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("re-login", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Not authori", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshHistoryAsync()
    {
        if (busy || client == null) return;
        busy = true;
        try { await HistoryStepAsync(client); }
        catch (Exception ex) { Log.Here.Debug(() => $"Zabbix history: {ex.Message}"); }
        finally
        {
            busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>For each item something showing wants: what's missing before what's kept, then everything new since.</summary>
    private async Task HistoryStepAsync(ZabbixClient c)
    {
        var now = DateTime.Now;
        var all = items.Values.SelectMany(l => l).Concat(Summary.Values.SelectMany(l => l)).Where(i => i.IsNumeric).DistinctBy(i => i.ItemId).ToDictionary(i => i.ItemId);
        foreach (var id in wanted.Where(w => w.Value.Asked < now - Interval * 3 - TimeSpan.FromMinutes(1)).Select(w => w.Key).ToList())
        {
            wanted.Remove(id);
            history.Remove(id);
        }
        var wanting = wanted.Where(w => all.ContainsKey(w.Key)).Select(w => new KeyValuePair<string, DateTime>(w.Key, w.Value.From)).ToList();
        if (wanting.Count == 0) return;

        // older than what's kept (or never read): one call per "from"
        foreach (var g in wanting.Where(w => !history.TryGetValue(w.Key, out var s) || s.From > w.Value.AddMinutes(1)).GroupBy(w => w.Value))
        {
            var list = g.Select(w => all[w.Key]).ToList();
            var to = list.Select(i => history.TryGetValue(i.ItemId, out var s) ? s.From : now).Max();
            var read = await c.HistoryAsync(list, g.Key, to);
            foreach (var i in list)
            {
                if (!history.TryGetValue(i.ItemId, out var s)) history[i.ItemId] = s = new Series { To = to };
                var older = read.TryGetValue(i.ItemId, out var p) ? p.Where(x => x.Time < s.From).ToList() : new();
                s.Points.InsertRange(0, older);
                s.From = g.Key;
            }
        }

        // newer than what's kept: one call from the oldest "to"
        var known = wanting.Select(w => w.Key).Where(history.ContainsKey).ToList();
        if (known.Count > 0)
        {
            var since = known.Min(id => history[id].To);
            if (now - since > TimeSpan.FromSeconds(5))
            {
                var read = await c.HistoryAsync(known.Select(id => all[id]).ToList(), since.AddSeconds(1), now);
                foreach (var id in known)
                {
                    var s = history[id];
                    var last = s.Points.Count > 0 ? s.Points[^1].Time : DateTime.MinValue;
                    if (read.TryGetValue(id, out var p)) s.Points.AddRange(p.Where(x => x.Time > last));
                    s.To = now;
                }
            }
        }

        // keep no more than what's wanted
        foreach (var (id, s) in history)
        {
            var from = wanted.TryGetValue(id, out var f) ? f.From : now - Kept;
            int drop = s.Points.FindIndex(p => p.Time >= from);
            if (drop > 0) s.Points.RemoveRange(0, drop);
        }
    }

    /// <summary>The address of the web interface (for Open in Zabbix).</summary>
    public string WebUrl
    {
        get
        {
            string api = client?.Url ?? Settings.ApiUrl();
            return api.EndsWith("api_jsonrpc.php", StringComparison.Ordinal) ? api[..^"api_jsonrpc.php".Length] : api;
        }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        var c = client;
        client = null;
        if (c != null)
        {
            _ = c.LogoutAsync(!Settings.UseToken).ContinueWith(_ => c.Dispose(), TaskScheduler.Default);
        }
    }
}
