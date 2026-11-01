using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Traceroute: every router between this PC and a host, in a table (address, name by reverse
/// lookup, loss and times, place and network), and on a map, roughly where each is. Repeat keeps tracing once a second
/// (as mtr does), so a hop that loses packets shows. A click on a hop shows it on the map (and a click on a marker picks its
/// hop); a double-click opens everything about it (<see cref="HopWindow"/>). Every trace is logged (<see cref="TraceRun"/>),
/// and Past traces shows one again; Ping's Trace when down traces a target here when it stops answering (since 3.34.0).
/// </summary>
internal sealed class TraceView : UserControl, IDisposable
{
    private readonly NetworkingSettings settings;
    private readonly ComboBox hostBox = new() { IsEditable = true, Width = 260, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox hopsBox = new() { Width = 46, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly CheckBox repeatBox = new() { Content = T("Repeat"), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Keep tracing, once a second, until Stop") };
    private readonly Button run;
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly DataGrid table = Ui.Table();
    private readonly MapView map = new() { Height = 460 };
    private readonly List<Hop> hops = new();
    /// <summary>The hops each of the map's markers stands for (the first, "You", none).</summary>
    private readonly List<List<int>> markerHops = new();
    private readonly ComboBox pastBox = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>The hosts being traced now, here or in the background (Trace when down's).</summary>
    private readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource closing = new();
    private CancellationTokenSource? cts;
    private bool fitPending, picking;

    public TraceView(NetworkingSettings settings)
    {
        this.settings = settings;
        hostBox.Text = settings.TraceHost;
        foreach (var h in settings.TraceRecent) hostBox.Items.Add(h);
        hostBox.ToolTip = T("Host name or IP address");
        hostBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = StartAsync(); } };
        hopsBox.Text = settings.TraceMaxHops.ToString();
        hopsBox.ToolTip = T("Most hops");
        repeatBox.IsChecked = settings.TraceRepeat;
        run = Ui.Button(T("Trace"), () => { if (cts != null) cts.Cancel(); else _ = StartAsync(); }, accent: true);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        // (since 3.34.0) every trace is logged: one picked shows again
        pastBox.ToolTip = T("Past traces");
        System.Windows.Automation.AutomationProperties.SetName(pastBox, T("Past traces"));
        pastBox.DropDownOpened += (_, _) => FillPast();
        pastBox.SelectionChanged += (_, _) =>
        {
            if (picking || pastBox.SelectedItem is not TraceRecord r) return;
            ShowPast(r);
        };
        TraceLog.Logged += OnLogged;
        foreach (var e in new FrameworkElement[] { hostBox, Ui.Secondary(T("Hops")), hopsBox, repeatBox, run, Ui.Secondary(T("Past")), pastBox })
        {
            e.Margin = new Thickness(0, 0, 10, 0);
            e.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(e);
        }
        var common = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (text, host) in new[] { ("Google DNS", "8.8.8.8"), ("Cloudflare", "1.1.1.1"), ("google.com", "google.com"), ("amazon.com", "amazon.com") })
        {
            var b = Ui.Button(text, () => { hostBox.Text = host; _ = StartAsync(); });
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Padding = new Thickness(8, 2, 8, 2);
            common.Children.Add(b);
        }

        table.Columns.Add(Ui.Column("#", nameof(Hop.Number), 40, right: true));
        table.Columns.Add(NetUi.CopyColumn(T("Address"), nameof(Hop.Address), 130));
        table.Columns.Add(NetUi.CopyColumn(T("Name"), nameof(Hop.Name), -1.4));
        table.Columns.Add(Ui.Column(T("Loss"), nameof(Hop.LossText), 60, nameof(Hop.Loss), right: true));
        table.Columns.Add(Ui.Column(T("Sent"), nameof(Hop.Sent), 50, right: true));
        table.Columns.Add(Ui.Column(T("Last"), nameof(Hop.LastText), 66, right: true));
        table.Columns.Add(Ui.Column(T("Average"), nameof(Hop.AvgText), 72, nameof(Hop.AvgNumber), right: true));
        table.Columns.Add(Ui.Column(T("Best"), nameof(Hop.BestText), 66, right: true));
        table.Columns.Add(Ui.Column(T("Worst"), nameof(Hop.WorstText), 66, right: true));
        table.Columns.Add(Ui.Column(T("Jitter"), nameof(Hop.StDevText), 66, right: true));
        table.Columns.Add(Ui.Column(T("Location"), nameof(Hop.Place), -1));
        table.Columns.Add(Ui.Column(T("Network"), nameof(Hop.Network), -1.2));
        table.ItemsSource = hops;
        table.MaxHeight = 560;
        NetUi.ItemMenu(table, o => o is Hop h ? h.Others.Prepend(h.Address).Append(h.Name) : [], (T("Details…"), o => Details(o as Hop)));
        table.MouseDoubleClick += (_, e) => Details(NetUi.ItemOf(e, table) as Hop);
        table.SelectionChanged += (_, _) =>
        {
            if (table.SelectedItem is not Hop h) { map.Selected = null; return; }
            int i = markerHops.FindIndex(m => m.Contains(h.Number));
            map.Selected = i >= 0 ? i : null;
        };
        map.Picked += i =>
        {
            if (i >= markerHops.Count || markerHops[i].Count == 0) return;
            var hop = hops.FirstOrDefault(h => h.Number == markerHops[i][0]);
            if (hop == null) return;
            table.SelectedItem = hop;
            table.ScrollIntoView(hop);
        };

        var tableCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Traceroute")), controls, common, status, table } }, new Thickness(0));
        var fit = NetUi.IconButton("", "", T("Fit the route"), map.Fit);
        var mapCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Map")), fit), map } }, new Thickness(0, 12, 0, 0));
        Content = Ui.Page(new StackPanel { Children = { tableCard, mapCard } });
        status.Margin = new Thickness(0, 0, 0, 8);
        map.Show(Array.Empty<MapPoint>(), true);
    }

    private void Details(Hop? hop)
    {
        if (hop != null && hop.Address.Length > 0) HopWindow.Open(hop, this);
    }

    /// <summary>
    /// Traces what's in the host box (<see cref="TraceRun"/>, so it's logged); <paramref name="why"/> is set for one Ping's
    /// Trace when down started, which leaves the box's history and the Repeat setting alone.
    /// </summary>
    private async Task StartAsync(string why = "")
    {
        if (cts != null) return;
        string host = hostBox.Text.Trim();
        if (host.Length == 0) return;
        if (!int.TryParse(hopsBox.Text.Trim(), out int max) || max < 1 || max > 64) max = 30;
        hopsBox.Text = max.ToString();
        bool repeat = why.Length == 0 && repeatBox.IsChecked == true;
        if (why.Length == 0)
        {
            settings.TraceHost = host;
            settings.TraceMaxHops = max;
            settings.TraceRepeat = repeat;
            settings.TraceRecent.Remove(host);
            settings.TraceRecent.Insert(0, host);
            if (settings.TraceRecent.Count > 12) settings.TraceRecent.RemoveRange(12, settings.TraceRecent.Count - 12);
            settings.Save();
            hostBox.Items.Clear();
            foreach (var h in settings.TraceRecent) hostBox.Items.Add(h);
            hostBox.Text = host;
        }

        cts = new CancellationTokenSource();
        running.Add(host);
        run.Content = T("Stop");
        ShowPast(null);
        pastBox.IsEnabled = false;
        hops.Clear();
        table.Items.Refresh();
        status.Text = (why.Length > 0 ? why + " · " : "") + F("Looking up {0}…", host);
        fitPending = true;
        var watch = new TraceRun.Watch();
        watch.Resolved = target => Dispatcher.BeginInvoke(() =>
            status.Text = (why.Length > 0 ? why + " · " : "") + (target.ToString() == host ? F("Tracing {0}…", host) : F("Tracing {0} ({1})…", host, target)));
        watch.Changed = () => Dispatcher.BeginInvoke(() => Round(watch.Me));
        try
        {
            var record = await TraceRun.RunAsync(host, max, repeat, why, hops, watch, cts.Token);
            if (record == null) { status.Text = F("Couldn't find {0}.", host); return; }
            await Task.Delay(300);
            Round(watch.Me);
            status.Text = (why.Length > 0 ? why + " · " : "")
                + (record.Reached ? F("Reached {0} in {1} hops", record.Target, hops.Count) : F("{0} didn't answer", record.Target)) + " · " + record.Summary;
        }
        catch (OperationCanceledException) { status.Text = T("Stopped") + " · " + TraceRun.Summary(hops); }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            cts?.Dispose();
            cts = null;
            running.Remove(host);
            run.Content = T("Trace");
            pastBox.IsEnabled = true;
        }
    }

    /// <summary>
    /// Ping's Trace when down: <paramref name="t"/> stopped answering. Traced here if nothing is being traced, else in the
    /// background (logged, for Past traces); never twice at once.
    /// </summary>
    public void TraceDown(NetTarget t) => Dispatcher.BeginInvoke(() =>
    {
        string host = t.Address.Trim(), why = F("{0} stopped answering", t.Display);
        if (host.Length == 0 || running.Contains(host)) return;
        if (cts == null)
        {
            hostBox.Text = host;
            _ = StartAsync(why);
            return;
        }
        running.Add(host);
        if (!int.TryParse(hopsBox.Text.Trim(), out int max) || max < 1 || max > 64) max = 30;
        _ = Task.Run(async () =>
        {
            try { await TraceRun.RunAsync(host, max, false, why, new List<Hop>(), null, closing.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logging.Log.Here.Warn($"Traceroute to {host}: {ex.Message}"); }
            finally { _ = Dispatcher.BeginInvoke(() => running.Remove(host)); }
        });
    });

    /// <summary>A logged trace shown in the table and on the map (null: back to the live one, emptied).</summary>
    private void ShowPast(TraceRecord? r)
    {
        if (r == null)
        {
            if (pastBox.SelectedIndex >= 0) { picking = true; pastBox.SelectedIndex = -1; picking = false; }
            return;
        }
        hops.Clear();
        hops.AddRange(r.Hops.Select(Hop.From));
        if (hops.Count > 0 && r.Reached) hops[^1].Destination = true;
        table.Items.Refresh();
        status.Text = string.Join(" · ", new[] { r.Time.ToString("G"), r.Target == r.Host ? r.Host : $"{r.Host} ({r.Target})", r.Why,
            r.Reached ? F("reached in {0} hops", r.Hops.Count) : T("didn't answer"), r.Summary }.Where(s => s.Length > 0));
        fitPending = true;
        ShowMap(double.IsNaN(r.MeLat) ? null : new GeoInfo("", r.MeLat, r.MeLon, r.MePlace, "", "", "", "", ""));
    }

    private void FillPast()
    {
        picking = true;
        var picked = pastBox.SelectedItem as TraceRecord;
        pastBox.Items.Clear();
        foreach (var r in TraceLog.All().Take(500)) pastBox.Items.Add(r);
        if (picked != null) pastBox.SelectedItem = pastBox.Items.OfType<TraceRecord>().FirstOrDefault(r => r.Time == picked.Time && r.Host == picked.Host);
        picking = false;
    }

    /// <summary>After each round (and its lookups): the table, then the map.</summary>
    private async void Round(Task<GeoInfo?> me)
    {
        table.Items.Refresh();
        if (hops.Any(h => h.Geo != null)) ShowMap(await me);
    }


    /// <summary>The located hops on the map, those at one place as one marker ("3–5"), starting from this PC's provider.</summary>
    private void ShowMap(GeoInfo? me)
    {
        var located = hops.Where(h => h.Geo != null && !double.IsNaN(h.Geo.Lat)).ToList();
        var points = new List<MapPoint>();
        markerHops.Clear();
        if (me != null && !double.IsNaN(me.Lat)) { points.Add(new MapPoint(T("You"), me.Lat, me.Lon, me.Place)); markerHops.Add(new()); }
        foreach (var h in located)
        {
            var g = h.Geo!;
            if (points.Count > 0 && Math.Abs(points[^1].Lat - g.Lat) < 0.05 && Math.Abs(points[^1].Lon - g.Lon) < 0.05 && points[^1].Label != T("You"))
            {
                string first = points[^1].Label.Split('–')[0];
                points[^1] = points[^1] with { Label = $"{first}–{h.Number}" };
                markerHops[^1].Add(h.Number);
            }
            else { points.Add(new MapPoint(h.Number.ToString(), g.Lat, g.Lon, g.Place)); markerHops.Add([h.Number]); }
        }
        map.Show(points, fitPending);
        if (table.SelectedItem is Hop picked && markerHops.FindIndex(m => m.Contains(picked.Number)) is int at and >= 0) map.Selected = at;
        if (points.Count > 1) fitPending = false;
    }

    private void OnLogged(TraceRecord r) => Dispatcher.BeginInvoke(() => { if (!pastBox.IsDropDownOpen) FillPast(); });

    public void Dispose()
    {
        TraceLog.Logged -= OnLogged;
        cts?.Cancel();
        closing.Cancel();
    }
}
