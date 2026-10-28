using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Traceroute: every router between this PC and a host, in a table (address, name by reverse
/// lookup, loss and times, place and network), and on a map, roughly where each is. Repeat keeps tracing once a second
/// (as mtr does), so a hop that loses packets shows. A click on a hop shows it on the map (and a click on a marker picks its
/// hop); a double-click opens everything about it (<see cref="HopWindow"/>).
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
    private CancellationTokenSource? cts;
    private bool fitPending;

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
        foreach (var e in new FrameworkElement[] { hostBox, Ui.Secondary(T("Hops")), hopsBox, repeatBox, run })
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
        table.Columns.Add(Ui.CopyColumn(T("Address"), nameof(Hop.Address), 130));
        table.Columns.Add(Ui.CopyColumn(T("Name"), nameof(Hop.Name), -1.4));
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
        Ui.ItemMenu(table, o => o is Hop h ? h.Others.Prepend(h.Address).Append(h.Name) : [], (T("Details…"), o => Details(o as Hop)));
        table.MouseDoubleClick += (_, e) => Details(Ui.ItemOf(e, table) as Hop);
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
        var fit = Ui.IconButton("", "", T("Fit the route"), map.Fit);
        var mapCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Map")), fit), map } }, new Thickness(0, 12, 0, 0));
        Content = Ui.Page(new StackPanel { Children = { tableCard, mapCard } });
        status.Margin = new Thickness(0, 0, 0, 8);
        map.Show(Array.Empty<MapPoint>(), true);
    }

    private void Details(Hop? hop)
    {
        if (hop != null && hop.Address.Length > 0) HopWindow.Open(hop, this);
    }

    private async Task StartAsync()
    {
        if (cts != null) return;
        string host = hostBox.Text.Trim();
        if (host.Length == 0) return;
        if (!int.TryParse(hopsBox.Text.Trim(), out int max) || max < 1 || max > 64) max = 30;
        hopsBox.Text = max.ToString();
        settings.TraceHost = host;
        settings.TraceMaxHops = max;
        settings.TraceRepeat = repeatBox.IsChecked == true;
        settings.TraceRecent.Remove(host);
        settings.TraceRecent.Insert(0, host);
        if (settings.TraceRecent.Count > 12) settings.TraceRecent.RemoveRange(12, settings.TraceRecent.Count - 12);
        settings.Save();
        hostBox.Items.Clear();
        foreach (var h in settings.TraceRecent) hostBox.Items.Add(h);
        hostBox.Text = host;

        cts = new CancellationTokenSource();
        run.Content = T("Stop");
        hops.Clear();
        table.Items.Refresh();
        status.Text = F("Looking up {0}…", host);
        fitPending = true;
        try
        {
            if (await NetInfo.ResolveAsync(host, cts.Token) is not { } target) { status.Text = F("Couldn't find {0}.", host); return; }
            Logging.Log.Here.Info($"Traceroute to {host} ({target}){(settings.TraceRepeat ? ", repeating" : "")}");
            status.Text = target.ToString() == host ? F("Tracing {0}…", host) : F("Tracing {0} ({1})…", host, target);
            var me = Geo.MineAsync(cts.Token);
            await Tracer.RunAsync(target, max, settings.TraceRepeat, hops, () => Dispatcher.BeginInvoke(() => Round(me)), cts.Token);
            await Task.Delay(300);
            Done(target);
        }
        catch (OperationCanceledException) { status.Text = T("Stopped") + " · " + Summary(); }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            cts?.Dispose();
            cts = null;
            run.Content = T("Trace");
        }
    }

    private void Done(IPAddress target)
    {
        var last = hops.LastOrDefault();
        status.Text = (last?.Destination == true ? F("Reached {0} in {1} hops", target, hops.Count) : F("{0} didn't answer", target)) + " · " + Summary();
    }

    private string Summary()
    {
        // a hop losing packets matters when the ones after it lose them too (routers often don't bother answering)
        var lossy = hops.Where((h, i) => h.Loss >= 10 && h.Sent > 0 && hops.Skip(i + 1).Where(n => n.Sent > 0 && n.Address.Length > 0).All(n => n.Loss >= h.Loss * 0.5)).ToList();
        return lossy.Count == 0 ? T("no loss along the way") : F("loss from hop {0} on", lossy[0].Number);
    }

    /// <summary>After each round: the table, then the names and places of new addresses, then the map.</summary>
    private async void Round(Task<GeoInfo?> me)
    {
        table.Items.Refresh();
        var unknown = hops.Where(h => h.Address.Length > 0 && h.Name.Length == 0 && h.Geo == null && h.Place.Length == 0).ToList();
        if (unknown.Count == 0) return;
        foreach (var h in unknown) h.Place = "…";
        var geo = await Geo.LookupAsync(unknown.Select(h => h.Address));
        foreach (var h in unknown)
        {
            if (geo.TryGetValue(h.Address, out var g)) { h.Geo = g; h.Place = g.Place; h.Network = g.As.Length > 0 ? g.As : g.Isp; }
            else h.Place = IPAddress.TryParse(h.Address, out var ip) && NetInfo.IsPrivate(ip) ? T("private") : "";
        }
        await Task.WhenAll(unknown.Select(async h => h.Name = await Names.ReverseAsync(h.Address) ?? ""));
        ShowMap(await me);
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

    public void Dispose() => cts?.Cancel();
}
