using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Theming;
using Microsoft.Win32;
using Point = DaisysApp.Applets.Network.Zabbix.Api.ZabbixClient.Point;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>
/// The history of a host's tile, a graph made with New graph, or one item, as Performance's history window has it: the
/// last hour live (kept up to date) or any period read from the server (every value up to two days, Zabbix's hourly
/// trends for longer), with peaks, a summary, Export and Widget. What to draw is asked for again at each refresh, so
/// it follows the host's items.
/// </summary>
internal sealed class ZabbixHistoryWindow : Window
{
    private const int MaxPoints = 1200;

    private sealed record RangeOption(string Label, Func<(DateTime From, DateTime To)> Span, bool Live)
    {
        public override string ToString() => Label;
    }

    private readonly ZabbixService service;
    private readonly string hostId;
    private readonly Func<IReadOnlyList<GraphDef>> graphsOf;
    private readonly ComboBox rangeBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox peaksBox = new() { Content = T("Show peaks"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), ToolTip = T("Also draw the highest value in each period (a faint line), not just the average") };
    private readonly StackPanel graphPanel = new();
    private readonly Grid stats = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly Dictionary<string, string?> highlighted = new();
    private IDisposable? watch;
    private int loadId;

    // what's on screen, for Export: each line's name and unit, and its points (average, peak)
    private List<(string Name, ZUnit Unit, List<(DateTime T, double Avg, double Max)> Points)> shown = new();

    public ZabbixHistoryWindow(ZabbixService service, string hostId, string title, string subtitle, Func<IReadOnlyList<GraphDef>> graphs, Action? makeWidget)
    {
        this.service = service;
        this.hostId = hostId;
        graphsOf = graphs;
        Title = F("{0} history", title);
        Width = 1000;
        Height = 820;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(peaksBox);
        right.Children.Add(rangeBox);
        right.Children.Add(Ui.IconButton("", T("Export…"), T("Save these numbers as a CSV file (opens in Excel)"), (_, _) => Export()));
        if (makeWidget != null)
            right.Children.Add(Ui.IconButton("", T("Widget"), T("Put this graph in its own small window you can place anywhere, e.g. on another monitor"), (_, _) => makeWidget()));
        var close = new Button { Content = T("Close"), MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Esc") };
        close.Click += (_, _) => Close();
        right.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        var sub = Ui.Secondary(subtitle);
        sub.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(sub);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(titles);

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { graphPanel } } };

        // the summary below the graphs, folded away until it's wanted
        var summaryBody = new ScrollViewer { MaxHeight = 210, Margin = new Thickness(0, 10, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stats, Visibility = Visibility.Collapsed };
        var chevron = Ui.Icon("", 11);
        chevron.Margin = new Thickness(0, 0, 8, 0);
        var summaryHead = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = T("Lowest, average and highest in this period") };
        summaryHead.Children.Add(chevron);
        var summaryTitle = Ui.Header(T("Summary"));
        summaryTitle.Margin = new Thickness(0);
        summaryHead.Children.Add(summaryTitle);
        summaryHead.MouseLeftButtonUp += (_, _) =>
        {
            bool open = summaryBody.Visibility != Visibility.Visible;
            summaryBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "" : "";
        };
        var summary = Ui.Card(new StackPanel { Children = { summaryHead, summaryBody } }, new Thickness(0, 12, 0, 0));
        summary.Padding = new Thickness(16, 10, 16, 10);

        var grid = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) grid.RowDefinitions.Add(new RowDefinition { Height = h });
        Grid.SetRow(scroller, 1);
        Grid.SetRow(summary, 2);
        Grid.SetRow(status, 3);
        status.Margin = new Thickness(0, 10, 0, 0);
        grid.Children.Add(header);
        grid.Children.Add(scroller);
        grid.Children.Add(summary);
        grid.Children.Add(status);
        Content = grid;

        rangeBox.ItemsSource = new List<RangeOption>
        {
            new(T("Last hour (live)"), () => (DateTime.Now.AddHours(-1), DateTime.Now), true),
            new(T("Last 6 hours"), () => (DateTime.Now.AddHours(-6), DateTime.Now), false),
            new(T("Last 24 hours"), () => (DateTime.Now.AddDays(-1), DateTime.Now), false),
            new(T("Last 7 days"), () => (DateTime.Now.AddDays(-7), DateTime.Now), false),
            new(T("Last 30 days"), () => (DateTime.Now.AddDays(-30), DateTime.Now), false),
            new(T("Today"), () => (DateTime.Today, DateTime.Now), false),
            new(T("Yesterday"), () => (DateTime.Today.AddDays(-1), DateTime.Today.AddTicks(-1)), false),
        };
        rangeBox.SelectedIndex = 0;
        rangeBox.SelectionChanged += (_, _) => Reload();
        peaksBox.Checked += (_, _) => Reload();
        peaksBox.Unchecked += (_, _) => Reload();

        watch = service.Watch(hostId);
        service.Changed += OnChanged;
        Closed += (_, _) =>
        {
            service.Changed -= OnChanged;
            watch?.Dispose();
            watch = null;
        };
        Loaded += (_, _) => Reload();
    }

    private RangeOption? Range => rangeBox.SelectedItem as RangeOption;

    private void OnChanged()
    {
        if (Range is { Live: true }) Reload();
    }

    private async void Reload()
    {
        if (Range is not { } range) return;
        int id = ++loadId;
        var (from, to) = range.Span();
        peaksBox.IsEnabled = !range.Live;
        bool peaks = !range.Live && peaksBox.IsChecked == true;
        var graphs = graphsOf();
        var items = graphs.SelectMany(g => g.Lines).Select(l => l.Item).DistinctBy(i => i.ItemId).ToList();
        if (items.Count == 0)
        {
            graphPanel.Children.Clear();
            status.Text = service.Error ?? T("Reading the host's items…");
            return;
        }

        Dictionary<string, List<(DateTime T, double Avg, double Max)>> data;
        TimeSpan gap;
        if (range.Live)
        {
            service.Want(items, from);
            data = items.ToDictionary(i => i.ItemId, i => service.HistoryOf(i.ItemId, from).Select(p => (p.Time, p.Value, p.Value)).ToList());
            gap = Gap(data.Values);
            status.Text = (service.Error is { } e ? e + " " : "") + F("Live: updated every {0}.", Every(service.Settings.RefreshSeconds));
        }
        else
        {
            status.Text = T("Reading…");
            Dictionary<string, List<Point>> read;
            bool trends;
            try { (read, trends) = await service.ReadAsync(items, from, to); }
            catch (Exception ex)
            {
                if (id == loadId) status.Text = ex.Message;
                return;
            }
            if (id != loadId) return; // another period was picked meanwhile
            int count = read.Values.Sum(p => p.Count);
            var bucket = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(1).Ticks, (to - from).Ticks / MaxPoints));
            data = new();
            foreach (var i in items)
            {
                var points = read.TryGetValue(i.ItemId, out var p) ? p : new();
                data[i.ItemId] = points.Count <= MaxPoints && !trends
                    ? points.Select(x => (x.Time, x.Value, x.Max)).ToList()
                    : points.GroupBy(x => x.Time.Ticks / bucket.Ticks).Select(g => (new DateTime(g.Key * bucket.Ticks),
                        g.Select(x => x.Value).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average(),
                        g.Select(x => x.Max).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max())).ToList();
            }
            gap = trends ? TimeSpan.FromHours(3) : Gap(data.Values);
            status.Text = count == 0 ? T("Zabbix has nothing for this period.")
                : trends ? F("{0:#,0} hourly averages from Zabbix's trends (with each hour's highest as the peak).", count)
                : F("{0:#,0} values from Zabbix's history", count) + (data.Values.Sum(d => d.Count) < count ? F(", shown as {0:#,0} points.", data.Values.Sum(d => d.Count)) : ".");
        }

        shown = new();
        graphPanel.Children.Clear();
        for (int g = 0; g < graphs.Count; g++)
        {
            var def = graphs[g];
            var lines = def.Lines.Select(l => (l.Item, l.Color, Points: data.TryGetValue(l.Item.ItemId, out var d) ? d : new())).ToList();
            AddGraph(def.Title, lines, from, to, gap, peaks, g == 0 ? 260 : 170);
            foreach (var (item, _, points) in lines) shown.Add((item.Name, item.Unit, points));
        }
        BuildStats();
    }

    private static string Every(int seconds) => seconds < 60 ? F("{0} seconds", seconds) : P(seconds / 60, "{0} minute", "{0} minutes");

    /// <summary>Points further apart than three times the usual step aren't joined (Zabbix missed them, or the host was down).</summary>
    private static TimeSpan Gap(IEnumerable<List<(DateTime T, double Avg, double Max)>> series)
    {
        var steps = series.SelectMany(s => s.Zip(s.Skip(1), (a, b) => (b.T - a.T).TotalSeconds)).Where(d => d > 0).OrderBy(d => d).ToList();
        double median = steps.Count > 0 ? steps[steps.Count / 2] : 60;
        return TimeSpan.FromSeconds(Math.Max(median * 3, 30));
    }

    private void AddGraph(string title, List<(ZItem Item, string Color, List<(DateTime T, double Avg, double Max)> Points)> lines,
        DateTime from, DateTime to, TimeSpan gap, bool peaks, double height)
    {
        var series = new List<GraphSeries>();
        foreach (var (item, color, points) in lines)
        {
            if (peaks) series.Add(new GraphSeries { Name = item.Name + T(" peak"), Metric = item.Unit, Points = points.Select(p => (p.T, p.Max)).ToList(), Color = color, Faint = true, Fill = false });
            series.Add(new GraphSeries { Name = item.Name, Metric = item.Unit, Points = points.Select(p => (p.T, p.Avg)).ToList(), Color = color, Fill = lines.Count == 1 });
        }
        var unit = lines[0].Item.Unit;
        var graph = new LineGraph { Detailed = true, Height = height, Margin = new Thickness(0, 8, 0, 0), Highlight = highlighted.GetValueOrDefault(title), AxisFormat = unit.Axis };
        graph.Show(series, from, to, TopOf(unit), gap);
        if (!unit.IsPercent)
        {
            graph.ScaleClicked += () => EditScale(unit);
            graph.ToolTip = T("Click the scale on the left to set the top of graphs in this unit");
        }
        var legend = Ui.Legend(graph, lines.Select(l => (l.Item.Name, l.Color)), h => highlighted[title] = h);
        legend.HorizontalAlignment = HorizontalAlignment.Right;
        var titleText = Ui.Header(title);
        titleText.Margin = new Thickness(0, 0, 24, 0);
        var header = new DockPanel();
        DockPanel.SetDock(titleText, Dock.Left);
        header.Children.Add(titleText);
        header.Children.Add(legend);
        graphPanel.Children.Add(Ui.Card(new StackPanel { Children = { header, graph } }));
    }

    /// <summary>The top of a unit's graphs: 100 for a percentage, the one set by clicking the scale, else fit to the data.</summary>
    private double? TopOf(ZUnit unit) => unit.IsPercent ? 100 : service.Settings.ScaleMax.TryGetValue(unit.Kind, out double v) ? v : null;

    private void EditScale(ZUnit unit)
    {
        var current = service.Settings.ScaleMax.TryGetValue(unit.Kind, out double v) ? v : (double?)null;
        if (ScalePrompt.Ask(this, unit, current) is not { } r) return;
        if (r <= 0) service.Settings.ScaleMax.Remove(unit.Kind); else service.Settings.ScaleMax[unit.Kind] = r;
        service.Settings.Save();
        ScaleChanged?.Invoke();
        Reload();
    }

    /// <summary>Raised after a graph's top was changed, so the host page and the widgets follow.</summary>
    public static event Action? ScaleChanged;

    private void BuildStats()
    {
        stats.Children.Clear();
        stats.RowDefinitions.Clear();
        stats.ColumnDefinitions.Clear();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        AddRow(new[] { "", T("Lowest"), T("Average"), T("Highest"), T("95% of the time under") }, header: true);
        foreach (var (name, unit, points) in shown.DistinctBy(s => s.Name))
        {
            var avgs = points.Select(p => p.Avg).Where(double.IsFinite).OrderBy(v => v).ToList();
            if (avgs.Count == 0) continue;
            double peak = points.Select(p => p.Max).Where(double.IsFinite).DefaultIfEmpty(avgs[^1]).Max();
            double p95 = avgs[(int)Math.Min(avgs.Count - 1, Math.Ceiling(avgs.Count * 0.95) - 1)];
            AddRow(new[] { name, unit.Text(avgs[0]), unit.Text(avgs.Average()), unit.Text(peak), unit.Text(p95) });
        }
        if (stats.RowDefinitions.Count == 1) AddRow(new[] { T("No values in this period.") });

        void AddRow(string[] cells, bool header = false)
        {
            int row = stats.RowDefinitions.Count;
            stats.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cells.Length; c++)
            {
                var t = new TextBlock { Text = cells[c], Margin = new Thickness(c == 0 ? 0 : 12, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
                if (header) t.SetResourceReference(StyleProperty, "SecondaryText");
                if (c > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
                if (c == 0 && !header) t.FontWeight = FontWeights.SemiBold;
                Grid.SetRow(t, row);
                Grid.SetColumn(t, c);
                if (cells.Length == 1) Grid.SetColumnSpan(t, 5);
                stats.Children.Add(t);
            }
        }
    }

    private void Export()
    {
        if (Range is not { } range) return;
        var dialog = new SaveFileDialog
        {
            Filter = T("CSV file (*.csv)|*.csv"),
            InitialDirectory = AppPaths.DocumentsFolder,
            FileName = string.Concat($"{Title} {range.Label} {DateTime.Now:yyyy-MM-dd HHmm}.csv".Split(Path.GetInvalidFileNameChars())).Replace("(", "").Replace(")", ""),
        };
        Directory.CreateDirectory(AppPaths.DocumentsFolder);
        if (dialog.ShowDialog(this) != true) return;
        bool live = range.Live;
        var lines = shown.DistinctBy(s => s.Name).ToList();
        var times = lines.SelectMany(l => l.Points.Select(p => p.T)).Distinct().OrderBy(t => t).ToList();
        var sb = new StringBuilder("Time");
        foreach (var (name, unit, _) in lines) sb.Append(live ? $",{Csv(name)} ({unit.Unit})" : $",{Csv(name)} average ({unit.Unit}),{Csv(name)} peak ({unit.Unit})");
        sb.AppendLine();
        var lookup = lines.Select(l => l.Points.GroupBy(p => p.T).ToDictionary(g => g.Key, g => g.First())).ToList();
        foreach (var t in times)
        {
            sb.Append(t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            foreach (var l in lookup)
            {
                bool has = l.TryGetValue(t, out var p);
                sb.Append(',').Append(has ? Num(p.Avg) : "");
                if (!live) sb.Append(',').Append(has ? Num(p.Max) : "");
            }
            sb.AppendLine();
        }
        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString());
            status.Text = F("Saved {0:#,0} rows to {1}.", times.Count, dialog.FileName);
        }
        catch (Exception ex) { status.Text = T("Couldn't save: ") + ex.Message; }
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
}
