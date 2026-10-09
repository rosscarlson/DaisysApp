using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Shared.Charts;
using DaisysApp.Shared.Hardware;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Sensors;

/// <summary>A sensor's unit, for the graph's axis and readout.</summary>
internal sealed record SensorUnit(string Unit) : IGraphUnit
{
    public string Text(double value) => HwSensor.FormatValue(value, Unit);
}

/// <summary>
/// History of one sensor, or of a piece of hardware's sensors of one kind: the live last 10 minutes, or any period
/// from the sensor log (10-second averages, optionally with peaks), with each sensor's lowest, average and highest.
/// Built in code (it's a toolbar, graphs and a table).
/// </summary>
internal sealed class SensorHistoryWindow : Window
{
    private const int MaxPoints = 1200;
    private static readonly string[] Palette = { "AccentBrush", "#F7A541", "#7FD07A", "#B48EF0", "#FF7B72", "#F2CC60", "#4CC2FF", "#E58AD8", "#9AA5B1", "#5EEAD4" };

    private sealed record RangeOption(string Label, Func<(DateTime From, DateTime To)> Span, bool Live)
    {
        public override string ToString() => Label;
    }

    private readonly SensorsService service;
    private readonly IReadOnlyList<HwSensor> sensors;
    private readonly ComboBox rangeBox = new() { Width = 220, Margin = new Thickness(0, 0, 16, 0) };
    private readonly CheckBox peaksBox = new() { Content = T("Show peaks"), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel graphs = new();
    private readonly Grid stats = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public SensorHistoryWindow(SensorsService service, IReadOnlyList<HwSensor> sensors, string title)
    {
        this.service = service;
        this.sensors = sensors;
        Title = title + T(" — history");
        Width = 900;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        var sub = new TextBlock { Text = T("From LibreHardwareMonitor") };
        sub.SetResourceReference(StyleProperty, "SecondaryText");
        header.Children.Add(sub);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        bar.Children.Add(rangeBox);
        bar.Children.Add(peaksBox);

        status.SetResourceReference(StyleProperty, "SecondaryText");
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(header);
        body.Children.Add(bar);
        body.Children.Add(graphs);
        body.Children.Add(status);
        body.Children.Add(stats);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };

        var ranges = new List<RangeOption>
        {
            new(T("Last 10 minutes (live)"), () => (DateTime.Now.AddSeconds(-SensorsService.LiveSeconds), DateTime.Now), true),
            new(T("Last hour"), () => (DateTime.Now.AddHours(-1), DateTime.Now), false),
            new(T("Last 6 hours"), () => (DateTime.Now.AddHours(-6), DateTime.Now), false),
            new(T("Last 24 hours"), () => (DateTime.Now.AddDays(-1), DateTime.Now), false),
            new(T("Last 7 days"), () => (DateTime.Now.AddDays(-7), DateTime.Now), false),
            new(T("Last 30 days"), () => (DateTime.Now.AddDays(-30), DateTime.Now), false),
            new(T("Today"), () => (DateTime.Today, DateTime.Now), false),
            new(T("Yesterday"), () => (DateTime.Today.AddDays(-1), DateTime.Today.AddTicks(-1)), false),
        };
        foreach (var day in SensorLog.Days().Where(d => d < DateTime.Today.AddDays(-1)).OrderByDescending(d => d))
            ranges.Add(new(day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), () => (day, day.AddDays(1).AddTicks(-1)), false));
        rangeBox.ItemsSource = ranges;
        rangeBox.SelectedIndex = 0;
        rangeBox.SelectionChanged += (_, _) => Reload();
        peaksBox.Checked += (_, _) => Reload();
        peaksBox.Unchecked += (_, _) => Reload();
        liveTimer.Tick += (_, _) => { if (rangeBox.SelectedItem is RangeOption { Live: true }) Reload(); };
        liveTimer.Start();
        Closed += (_, _) => liveTimer.Stop();
        Reload();
    }

    private void Reload()
    {
        if (rangeBox.SelectedItem is not RangeOption range) return;
        var (from, to) = range.Span();
        peaksBox.IsEnabled = !range.Live;
        bool peaks = !range.Live && peaksBox.IsChecked == true;
        var keys = sensors.Select(s => s.Key).ToList();

        // per sensor: (time, average, peak)
        List<List<(DateTime T, double Avg, double Max)>> data;
        TimeSpan gap;
        if (range.Live)
        {
            data = sensors.Select(s => service.Live(s.Key).Select(p => (p.Time, p.Value, p.Value)).ToList()).ToList();
            gap = TimeSpan.FromSeconds(8);
            status.Text = T("Live: a reading every 2 seconds, updating.");
        }
        else
        {
            var rows = service.Log.Read(from, to, keys);
            var bucket = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(SensorLog.Seconds).Ticks, (to - from).Ticks / MaxPoints));
            data = keys.Select(k => rows
                .Where(r => r.Values.ContainsKey(k))
                .GroupBy(r => r.Time.Ticks / bucket.Ticks)
                .Select(g => (g.First().Time, g.Select(r => r.Values[k].Avg).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average(),
                    g.Select(r => r.Values[k].Max).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max()))
                .ToList()).ToList();
            gap = bucket * 3;
            status.Text = !sensors.Any(SensorLog.Logs)
                ? T("Only temperatures, fans and power are kept in the log; this sensor is live only (choose Last 10 minutes).")
                : !service.Settings.LogEnabled ? T("The sensor log is off (Settings → Sensors).")
                : rows.Count == 0 ? T("Nothing was logged in this period.")
                : F("{0:#,0} readings, each the average of 10 seconds.", rows.Count);
        }

        // one graph per unit (a fan's speed in RPM and its control in % don't share an axis)
        graphs.Children.Clear();
        foreach (var unit in sensors.Select((s, i) => (s, i)).GroupBy(x => x.s.Unit))
        {
            var members = unit.ToList();
            var series = new List<GraphSeries>();
            foreach (var (s, i) in members)
            {
                string color = Palette[i % Palette.Length];
                series.Add(new GraphSeries { Name = s.Name, Points = data[i].Select(p => (p.T, p.Avg)).ToList(), Metric = new SensorUnit(s.Unit), Color = color, Fill = members.Count == 1 });
                if (peaks) series.Add(new GraphSeries { Name = s.Name + T(" peak"), Points = data[i].Select(p => (p.T, p.Max)).ToList(), Metric = new SensorUnit(s.Unit), Color = color, Fill = false, Faint = true });
            }
            string caption = members.Count == 1 ? members[0].s.Name : (unit.Key.Length > 0 ? unit.Key : T("Values"));
            var label = new TextBlock { Text = caption, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, graphs.Children.Count > 0 ? 14 : 0, 0, 4) };
            graphs.Children.Add(label);
            var graph = new LineGraph { Detailed = true, Height = sensors.Count > 1 ? 260 : 300 };
            double? fixedMax = unit.Key == "%" ? 100 : null;
            if (members.Count > 1) graphs.Children.Add(Legend(members, graph));
            graph.Highlight = highlighted;
            graph.Show(series, from, to, fixedMax, gap);
            graphs.Children.Add(graph);
        }
        BuildStats(data);
    }

    // the line picked in the legend, kept while the live graph redraws
    private string? highlighted;

    /// <summary>A swatch and name per sensor; click one to pick out its line (again for all).</summary>
    private FrameworkElement Legend(List<(HwSensor s, int i)> members, LineGraph graph)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (s, i) in members)
        {
            string color = Palette[i % Palette.Length];
            var swatch = new System.Windows.Shapes.Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (color.StartsWith('#')) swatch.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!;
            else swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, color);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = s.Name, Opacity = highlighted == null || highlighted == s.Name ? 1 : 0.5 });
            item.MouseLeftButtonUp += (_, _) =>
            {
                highlighted = highlighted == s.Name ? null : s.Name;
                Reload();
            };
            panel.Children.Add(item);
        }
        return panel;
    }

    /// <summary>Lowest, average and highest of each sensor in the period.</summary>
    private void BuildStats(List<List<(DateTime T, double Avg, double Max)>> data)
    {
        stats.Children.Clear();
        stats.RowDefinitions.Clear();
        stats.ColumnDefinitions.Clear();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int c = 0; c < 3; c++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        void Row(string[] texts, bool header)
        {
            int r = stats.RowDefinitions.Count;
            stats.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < texts.Length; c++)
            {
                var t = new TextBlock { Text = texts[c], Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                if (header) t.SetResourceReference(StyleProperty, "SecondaryText");
                Grid.SetRow(t, r);
                Grid.SetColumn(t, c);
                stats.Children.Add(t);
            }
        }
        Row(new[] { "", T("Lowest"), T("Average"), T("Highest") }, true);
        for (int i = 0; i < sensors.Count; i++)
        {
            var s = sensors[i];
            var avg = data[i].Select(p => p.Avg).Where(double.IsFinite).ToList();
            var max = data[i].Select(p => p.Max).Where(double.IsFinite).ToList();
            Row(new[]
            {
                s.Name,
                avg.Count > 0 ? s.Text(avg.Min()) : "—",
                avg.Count > 0 ? s.Text(avg.Average()) : "—",
                max.Count > 0 ? s.Text(max.Max()) : "—",
            }, false);
        }
    }
}
