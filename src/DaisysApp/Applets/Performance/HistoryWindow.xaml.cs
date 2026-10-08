using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// History of a tile's metrics, or of one process: the live last 10 minutes (every second) or any period from the
/// log (10-second averages, with optional peaks), with a summary and the busiest processes in that period.
/// </summary>
public partial class HistoryWindow : Window
{
    private const int MaxPoints = 1200;

    public sealed record RangeOption(string Label, Func<(DateTime From, DateTime To)> Span, bool Live);
    public sealed record ProcessTotal(string Name, double AvgCpu, double PeakCpu, double PeakMemoryMB);

    private readonly PerfMonitor monitor;
    private readonly PerfLog log;
    private readonly PerfLimits limits;
    private readonly PerformanceSettings settings;
    private readonly MetricGroup? group;
    private readonly int? pid;
    private readonly string? processName;
    private readonly IReadOnlyList<HwSensor>? sensors;

    private static readonly string[] Palette = { "AccentBrush", "#F7A541", "#7FD07A", "#B48EF0", "#FF7B72", "#F2CC60", "#4CC2FF", "#E58AD8", "#9AA5B1", "#5EEAD4" };
    private string? processPath;

    // what's on screen, for Export
    private List<(DateTime Time, double[] Avg, double[] Max)> shown = new();
    private IReadOnlyList<MetricInfo> shownMetrics = Array.Empty<MetricInfo>();
    private IReadOnlyList<int> shownColumns = Array.Empty<int>();

    // the line picked in each graph's legend, kept while live graphs are redrawn every second
    private readonly Dictionary<string, string?> highlighted = new();

    // process-mode metrics (not part of the system-wide list)
    private static readonly MetricInfo ProcCpu = new(Metric.Cpu, "cpu", "CPU", "%", "0.0", null);
    private static readonly MetricInfo ProcMemory = new(Metric.RamUsed, "memory", T("Memory"), "MB", "#,0", null);
    private static readonly MetricInfo ProcGpu = new(Metric.Gpu, "gpu", "GPU", "%", "0.0", null);
    private static readonly MetricInfo ProcVram = new(Metric.VramUsed, "vram", T("Video memory"), "MB", "#,0", null);
    private static readonly MetricInfo ProcIo = new(Metric.DiskRead, "io", T("Disk"), "MB/s", "0.00", null);

    /// <summary>History of a tile's metrics.</summary>
    public HistoryWindow(PerfMonitor monitor, PerfLog log, PerfLimits limits, PerformanceSettings settings, MetricGroup group)
        : this(monitor, log, limits, settings)
    {
        this.group = group;
        Title = F("{0} history", group.Title);
        TitleText.Text = group.Title;
        // by the group itself, not its title (which is translated)
        SubtitleText.Text =
            group == MetricGroup.Cpu ? F("{0} · {1} cores, {2} threads", monitor.Info.Cpu, monitor.Info.Cores, monitor.Info.Threads)
            : group == MetricGroup.Gpu || group == MetricGroup.Vram ? monitor.Info.Gpu
            : group == MetricGroup.Temperature ? $"{monitor.Info.Cpu} · {monitor.Info.Gpu}"
            : group == MetricGroup.Memory ? F("{0:0.0} GB installed", monitor.Info.RamBytes / MetricInfo.GB)
            : "";
        ProcessCard.Visibility = group.ShowProcesses ? Visibility.Visible : Visibility.Collapsed;
        Ready();
    }

    /// <summary>History of one process (or, with no id, every process with that name).</summary>
    public HistoryWindow(PerfMonitor monitor, PerfLog log, PerfLimits limits, PerformanceSettings settings, int? pid, string name)
        : this(monitor, log, limits, settings)
    {
        this.pid = pid;
        processName = name;
        Title = F("{0} — history", name);
        TitleText.Text = name;
        try
        {
            if (pid is int id)
            {
                using var p = Process.GetProcessById(id);
                processPath = p.MainModule?.FileName;
                SubtitleText.Text = F("Process ID {0}", id) + (processPath != null ? F(" · {0}", processPath) : "") + F(" · started {0:g}", p.StartTime);
            }
            else SubtitleText.Text = T("Every process with this name");
        }
        catch { SubtitleText.Text = pid is int id ? F("Process ID {0} (details need administrator rights, or it has ended)", id) : ""; }
        LocationButton.Visibility = processPath != null ? Visibility.Visible : Visibility.Collapsed;
        Ready();
    }

    /// <summary>History of LibreHardwareMonitor sensors (one, or a piece of hardware's sensors of one kind).</summary>
    public HistoryWindow(PerfMonitor monitor, PerfLog log, PerfLimits limits, PerformanceSettings settings, IReadOnlyList<HwSensor> sensors, string title)
        : this(monitor, log, limits, settings)
    {
        this.sensors = sensors;
        WarningsButton.Visibility = Visibility.Collapsed; // hardware sensors have no warning levels
        Title = title + T(" — history");
        TitleText.Text = title;
        SubtitleText.Text = T("From LibreHardwareMonitor");
        Ready();
    }

    private HistoryWindow(PerfMonitor monitor, PerfLog log, PerfLimits limits, PerformanceSettings settings)
    {
        this.monitor = monitor;
        this.log = log;
        this.limits = limits;
        this.settings = settings;
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        monitor.Sampled += OnSample;
        limits.Changed += Reload;
        Closed += (_, _) =>
        {
            monitor.Sampled -= OnSample;
            limits.Changed -= Reload;
        };
    }

    /// <summary>The warning levels this window's numbers have.</summary>
    private IReadOnlyList<LimitDef> LimitDefs() => group != null
        ? (group == MetricGroup.Cpu ? new[] { "cpu", PerfLimits.CpuCore } : group.AllMetrics.Select(PerfLimits.KeyOf).OfType<string>())
            .Select(PerfLimits.Def).OfType<LimitDef>().ToList()
        : PerfLimits.Processes;

    private void Warnings_Click(object sender, RoutedEventArgs e)
    {
        var defs = LimitDefs();
        string title = group != null ? F("{0} warnings", group.Title) : T("Process warnings");
        new LimitsWindow(limits, defs, title, group == null ? settings : null) { Owner = this }.ShowDialog();
    }

    /// <summary>Dashed orange and red lines for a graph's warning levels.</summary>
    private IReadOnlyList<(double Value, string Color)> LevelsFor(IReadOnlyList<MetricInfo> metrics, IReadOnlyList<int> columns)
    {
        var keys = new List<string>();
        if (group != null) keys.AddRange(metrics.Select(m => PerfLimits.KeyOf(m.Id)).OfType<string>());
        else if (sensors == null)
            keys.AddRange(columns.Select(c => c switch { 0 => "proc.cpu", 1 => "proc.ram", 2 => "proc.gpu", 3 => "proc.vram", 4 => "proc.disk", _ => "" }).Where(k => k.Length > 0));
        var lines = new List<(double, string)>();
        foreach (var k in keys)
        {
            var (warn, critical) = limits.Get(k);
            lines.Add((warn, "#F7A541"));
            lines.Add((critical, "DangerBrush"));
        }
        return lines.Distinct().ToList();
    }

    private void Ready()
    {
        var ranges = new List<RangeOption>
        {
            new(T("Last 10 minutes (live)"), () => (DateTime.Now.AddSeconds(-PerfMonitor.LiveSeconds), DateTime.Now), true),
            new(T("Last hour"), () => (DateTime.Now.AddHours(-1), DateTime.Now), false),
            new(T("Last 6 hours"), () => (DateTime.Now.AddHours(-6), DateTime.Now), false),
            new(T("Last 24 hours"), () => (DateTime.Now.AddDays(-1), DateTime.Now), false),
            new(T("Last 7 days"), () => (DateTime.Now.AddDays(-7), DateTime.Now), false),
            new(T("Last 30 days"), () => (DateTime.Now.AddDays(-30), DateTime.Now), false),
            new(T("Today"), () => (DateTime.Today, DateTime.Now), false),
            new(T("Yesterday"), () => (DateTime.Today.AddDays(-1), DateTime.Today.AddTicks(-1)), false),
        };
        foreach (var day in PerfLog.Days().Where(d => d < DateTime.Today.AddDays(-1)).OrderByDescending(d => d))
            ranges.Add(new(day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), () => (day, day.AddDays(1).AddTicks(-1)), false));
        RangeBox.ItemsSource = ranges;
        RangeBox.SelectedIndex = 0;
    }

    private RangeOption Range => (RangeOption)RangeBox.SelectedItem;

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Reload();
    private void PeaksBox_Changed(object sender, RoutedEventArgs e) => Reload();

    private void OnSample(PerfSample s)
    {
        if (RangeBox.SelectedItem is RangeOption { Live: true }) Reload();
    }

    // ---------------------------------------------------------------- loading

    private void Reload()
    {
        if (RangeBox.SelectedItem is not RangeOption range) return;
        var (from, to) = range.Span();
        PeaksBox.IsEnabled = !range.Live;
        bool peaks = !range.Live && PeaksBox.IsChecked == true;

        if (group != null) LoadGroup(range, from, to, peaks);
        else if (sensors != null) LoadSensors(range, from, to, peaks);
        else LoadProcess(range, from, to, peaks);
    }

    private void LoadGroup(RangeOption range, DateTime from, DateTime to, bool peaks)
    {
        var metrics = group!.AllMetrics.Select(MetricInfo.Of).ToList();
        TimeSpan gap;
        if (range.Live)
        {
            shown = monitor.Live().Select(s => (s.Time, s.Values, s.Values)).ToList();
            gap = TimeSpan.FromSeconds(5);
            StatusText.Text = T("Live: one reading a second, updating.");
        }
        else
        {
            var rows = log.Read(from, to);
            (shown, gap) = Downsample(rows.Select(r => (r.Time, r.Avg, r.Max)).ToList(), from, to);
            StatusText.Text = rows.Count == 0
                ? (log.Enabled ? T("Nothing was logged in this period (the log starts when Daisy's App is running).") : T("Logging is off (Settings → Performance)."))
                : F("{0:#,0} readings, each the average of 10 seconds", rows.Count) + (shown.Count < rows.Count ? F(", shown as {0:#,0} points.", shown.Count) : ".");
        }
        shownMetrics = metrics;
        shownColumns = metrics.Select(m => (int)m.Id).ToList();

        GraphPanel.Children.Clear();
        for (int g = 0; g < group.Graphs.Length; g++)
        {
            var spec = group.Graphs[g];
            var ms = spec.Metrics.Select(MetricInfo.Of).ToList();
            if (g > 0 && !ms.Any(m => shown.Any(r => !double.IsNaN(r.Avg[(int)m.Id])))) continue; // nothing to show (e.g. no fan reading)
            AddGraph(spec.Title, ms, ms.Select(m => (int)m.Id).ToList(), ms.Select(m => PerformanceView.ColorOf(m.Id)).ToList(),
                from, to, gap, peaks, g == 0 ? 260 : 170, fillFirst: ms.Count == 1);
        }

        BuildStats(metrics, shownColumns);
        if (group.ShowProcesses) BuildProcessTotals(range, from, to);
    }

    private void LoadProcess(RangeOption range, DateTime from, DateTime to, bool peaks)
    {
        var metrics = new List<MetricInfo>();
        TimeSpan gap;
        if (range.Live)
        {
            // by time: this process (or every process with the name) added up
            var all = monitor.AllProcessLive();
            var samples = all.Where(p => pid is int id ? p.Key == id : p.Value.Any(x => x.Sample.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(p => p.Value)
                .GroupBy(x => x.Time).OrderBy(g => g.Key)
                .Select(g =>
                {
                    var v = new[] { g.Sum(x => x.Sample.Cpu), g.Sum(x => x.Sample.MemoryMB), g.Max(x => x.Sample.Gpu), g.Sum(x => x.Sample.VramMB), g.Sum(x => x.Sample.IoMBps) };
                    return (g.Key, v, v);
                }).ToList();
            shown = samples;
            metrics.AddRange(new[] { ProcCpu, ProcMemory, ProcGpu, ProcVram, ProcIo });
            gap = TimeSpan.FromSeconds(Math.Max(5, monitor.Watching ? 5 : 25));
            StatusText.Text = T("Live: the last 10 minutes (or since Daisy's App started), updating.");
        }
        else
        {
            // the log keeps the 5 busiest processes by CPU and by memory every 10 seconds; elsewhere there's no reading
            var rows = log.Read(from, to);
            var points = rows.Select(r =>
            {
                double cpu = r.TopCpu.Where(p => p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).DefaultIfEmpty(double.NaN).First();
                double mem = r.TopMemory.Where(p => p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).DefaultIfEmpty(double.NaN).First();
                var v = new[] { cpu, mem };
                return (r.Time, v, v);
            }).ToList();
            (shown, gap) = Downsample(points, from, to);
            metrics.AddRange(new[] { ProcCpu, ProcMemory });
            int present = points.Count(p => !double.IsNaN(p.Item2[0]) || !double.IsNaN(p.Item2[1]));
            StatusText.Text = rows.Count == 0
                ? T("Nothing was logged in this period.")
                : F("The log records the 5 busiest processes (by CPU and by memory) every 10 seconds; {0} was among them in {1:#,0} of {2:#,0} readings. Gaps are times it wasn't.", processName, present, rows.Count);
        }
        shownMetrics = metrics;
        shownColumns = Enumerable.Range(0, metrics.Count).ToList();

        GraphPanel.Children.Clear();
        for (int i = 0; i < metrics.Count; i++)
            if (i == 0 ? shown.Any(r => !double.IsNaN(r.Avg[i])) : shown.Any(r => r.Avg[i] > 0)) // skip graphs that are all zero
                AddGraph(metrics[i].Name, new[] { metrics[i] }, new[] { i }, new[] { Palette[i % Palette.Length] }, from, to, gap, peaks, i == 0 ? 220 : 150);
        BuildStats(metrics, shownColumns);
    }

    private static string FormatFor(string unit) => unit switch
    {
        "V" => "0.000",
        "°C" or "W" or "%" or "A" => "0.0",
        "RPM" or "MHz" or "MB" => "#,0",
        _ => "0.##",
    };

    private void LoadSensors(RangeOption range, DateTime from, DateTime to, bool peaks)
    {
        var list = sensors!;
        var metrics = list.Select(x => new MetricInfo(Metric.Cpu, x.Key, list.Count > 1 ? x.Name : x.Name, x.Unit, FormatFor(x.Unit), x.Unit == "%" ? 100 : null)).ToList();
        TimeSpan gap;
        if (range.Live)
        {
            var series = list.Select(x => monitor.SensorLive(x.Key).ToDictionary(p => p.Time, p => p.Value)).ToList();
            shown = series.SelectMany(d => d.Keys).Distinct().OrderBy(t => t).Select(t =>
            {
                var v = series.Select(d => d.TryGetValue(t, out double x) ? x : double.NaN).ToArray();
                return (t, v, v);
            }).ToList();
            gap = TimeSpan.FromSeconds(8);
            StatusText.Text = T("Live: a reading every 2 seconds, updating.");
        }
        else
        {
            var keys = list.Select(x => x.Key).ToList();
            var rows = monitor.SensorLog.Read(from, to, keys);
            var points = rows.Select(r =>
            {
                var avg = keys.Select(k => r.Values.TryGetValue(k, out var v) ? v.Avg : double.NaN).ToArray();
                var max = keys.Select(k => r.Values.TryGetValue(k, out var v) ? v.Max : double.NaN).ToArray();
                return (r.Time, avg, max);
            }).ToList();
            (shown, gap) = Downsample(points, from, to);
            StatusText.Text = !list.Any(SensorLog.Logs)
                ? T("Only temperatures, fans and power are kept in the log; this sensor is live only (choose Last 10 minutes).")
                : rows.Count == 0 ? T("Nothing was logged in this period.") : F("{0:#,0} readings, each the average of 10 seconds.", rows.Count);
        }
        shownMetrics = metrics;
        shownColumns = Enumerable.Range(0, metrics.Count).ToList();

        // one graph per unit (a fan's speed in RPM and its control in % don't share an axis)
        GraphPanel.Children.Clear();
        foreach (var unit in metrics.Select((m, i) => (m, i)).GroupBy(x => x.m.Unit))
        {
            var group = unit.ToList();
            string title = group.Count == 1 ? group[0].m.Name : TitleText.Text.Split(" — ").Last().FirstUpper() + (unit.Key.Length > 0 ? $" ({unit.Key})" : "");
            AddGraph(title, group.Select(x => x.m).ToList(), group.Select(x => x.i).ToList(), group.Select(x => Palette[x.i % Palette.Length]).ToList(),
                from, to, gap, peaks, metrics.Count > 1 ? 300 : 260, fillFirst: group.Count == 1);
        }
        BuildStats(metrics, shownColumns);
    }

    /// <summary>Averages neighbouring readings so a long period draws at most <see cref="MaxPoints"/> points (peaks keep their highest).</summary>
    private static (List<(DateTime, double[], double[])> Points, TimeSpan Gap) Downsample(List<(DateTime Time, double[] Avg, double[] Max)> rows, DateTime from, DateTime to)
    {
        var step = TimeSpan.FromSeconds(LogAggregator.Seconds);
        if (rows.Count <= MaxPoints) return (rows.Select(r => (r.Time, r.Avg, r.Max)).ToList(), step * 3);
        var bucket = TimeSpan.FromTicks(Math.Max(step.Ticks, (to - from).Ticks / MaxPoints));
        var result = new List<(DateTime, double[], double[])>();
        foreach (var g in rows.GroupBy(r => r.Time.Ticks / bucket.Ticks))
        {
            int n = g.First().Avg.Length;
            var avg = new double[n];
            var max = new double[n];
            for (int i = 0; i < n; i++)
            {
                var a = g.Select(r => r.Avg[i]).Where(v => !double.IsNaN(v)).ToList();
                var m = g.Select(r => r.Max[i]).Where(v => !double.IsNaN(v)).ToList();
                avg[i] = a.Count > 0 ? a.Average() : double.NaN;
                max[i] = m.Count > 0 ? m.Max() : double.NaN;
            }
            result.Add((new DateTime(g.Key * bucket.Ticks), avg, max));
        }
        return (result, bucket * 3);
    }

    // ---------------------------------------------------------------- drawing

    private void AddGraph(string title, IReadOnlyList<MetricInfo> metrics, IReadOnlyList<int> columns, IReadOnlyList<string> colors,
        DateTime from, DateTime to, TimeSpan gap, bool peaks, double height, bool fillFirst = true)
    {
        var series = new List<GraphSeries>();
        for (int k = 0; k < metrics.Count; k++)
        {
            var m = metrics[k];
            int col = columns[k];
            string color = colors[k];
            if (peaks) series.Add(new GraphSeries { Name = m.Name + T(" peak"), Metric = m, Points = shown.Select(r => (r.Time, r.Max[col])).ToList(), Color = color, Faint = true, Fill = false });
            series.Add(new GraphSeries { Name = m.Name, Metric = m, Points = shown.Select(r => (r.Time, r.Avg[col])).ToList(), Color = color, Fill = k == 0 && fillFirst });
        }

        var graph = new LineGraph { Detailed = true, Height = height, Margin = new Thickness(0, 8, 0, 0), Highlight = highlighted.GetValueOrDefault(title), Levels = LevelsFor(metrics, columns) };
        graph.Show(series, from, to, metrics[0].FixedMax, gap);

        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
        var items = new List<(string Name, FrameworkElement Item)>();
        foreach (var s in series.Where(s => !s.Faint))
        {
            var swatch = new Border { Width = 12, Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (s.Color.StartsWith('#')) swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Color));
            else swatch.SetResourceReference(Border.BackgroundProperty, s.Color);
            var text = new TextBlock { Text = s.Name };
            text.SetResourceReference(StyleProperty, "SecondaryText");
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 16, 2),
                Background = Brushes.Transparent, // clickable between the swatch and the text too
                Cursor = Cursors.Hand,
                ToolTip = T("Click to highlight this line; click again to show all"),
            };
            item.Children.Add(swatch);
            item.Children.Add(text);
            legend.Children.Add(item);
            items.Add((s.Name, item));
            string name = s.Name;
            item.MouseLeftButtonUp += (_, _) =>
            {
                graph.Highlight = highlighted[title] = graph.Highlight == name ? null : name;
                ShowLegendState();
            };
        }
        ShowLegendState();

        void ShowLegendState()
        {
            foreach (var (n, el) in items) el.Opacity = graph.Highlight == null || graph.Highlight == n ? 1 : 0.45;
        }

        var titleText = new TextBlock { Text = title, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Top };
        titleText.SetResourceReference(StyleProperty, "CardHeader");
        var header = new DockPanel();
        DockPanel.SetDock(titleText, Dock.Left);
        header.Children.Add(titleText);
        legend.HorizontalAlignment = HorizontalAlignment.Right;
        legend.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(legend);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(graph);
        var card = new Border { Child = stack };
        card.SetResourceReference(StyleProperty, "Card");
        GraphPanel.Children.Add(card);
    }

    private void BuildStats(IReadOnlyList<MetricInfo> metrics, IReadOnlyList<int> columns)
    {
        StatsGrid.Children.Clear();
        StatsGrid.RowDefinitions.Clear();
        StatsGrid.ColumnDefinitions.Clear();
        StatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) StatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        AddRow(new[] { "", T("Lowest"), T("Average"), T("Highest"), T("95% of the time under") }, header: true);
        for (int k = 0; k < metrics.Count; k++)
        {
            var m = metrics[k];
            int col = columns[k];
            var avgs = shown.Select(r => r.Avg[col]).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
            if (avgs.Count == 0) continue;
            double peak = shown.Select(r => r.Max[col]).Where(v => !double.IsNaN(v)).DefaultIfEmpty(avgs[^1]).Max();
            double p95 = avgs[(int)Math.Min(avgs.Count - 1, Math.Ceiling(avgs.Count * 0.95) - 1)];
            AddRow(new[] { m.Name, m.Text(avgs[0]), m.Text(avgs.Average()), m.Text(peak), m.Text(p95) });
        }
        if (StatsGrid.RowDefinitions.Count == 1) AddRow(new[] { T("No readings in this period.") });

        void AddRow(string[] cells, bool header = false)
        {
            int row = StatsGrid.RowDefinitions.Count;
            StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cells.Length; c++)
            {
                var t = new TextBlock { Text = cells[c], Margin = new Thickness(c == 0 ? 0 : 12, 0, 0, 6) };
                if (header) t.SetResourceReference(StyleProperty, "SecondaryText");
                if (c > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
                if (c == 0 && !header) t.FontWeight = FontWeights.SemiBold;
                Grid.SetRow(t, row);
                Grid.SetColumn(t, c);
                if (cells.Length == 1) Grid.SetColumnSpan(t, 5);
                StatsGrid.Children.Add(t);
            }
        }
    }

    private void BuildProcessTotals(RangeOption range, DateTime from, DateTime to)
    {
        List<ProcessTotal> totals;
        if (range.Live)
        {
            var all = monitor.AllProcessLive().Values.SelectMany(v => v).ToList();
            int times = all.Select(x => x.Time).Distinct().Count();
            totals = all.GroupBy(x => x.Sample.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcessTotal(g.Key,
                    g.Sum(x => x.Sample.Cpu) / Math.Max(1, times),
                    g.GroupBy(x => x.Time).Max(t => t.Sum(x => x.Sample.Cpu)),
                    g.GroupBy(x => x.Time).Max(t => t.Sum(x => x.Sample.MemoryMB))))
                .ToList();
            ProcessNote.Text = T("Over the last 10 minutes. Double-click a process for its graphs.");
        }
        else
        {
            var rows = log.Read(from, to);
            var names = rows.SelectMany(r => r.TopCpu.Concat(r.TopMemory).Select(p => p.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
            totals = names.Select(n => new ProcessTotal(n,
                    rows.Sum(r => r.TopCpu.Where(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).Sum(p => p.Value)) / Math.Max(1, rows.Count),
                    rows.SelectMany(r => r.TopCpu).Where(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).DefaultIfEmpty(0).Max(),
                    rows.SelectMany(r => r.TopMemory).Where(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).DefaultIfEmpty(0).Max()))
                .ToList();
            ProcessNote.Text = T("From the log, which keeps the 5 busiest processes by CPU and by memory every 10 seconds (so quieter processes count as 0). Double-click a process for its graphs.");
        }
        bool byMemory = group == MetricGroup.Memory;
        ProcessGrid.ItemsSource = (byMemory ? totals.OrderByDescending(t => t.PeakMemoryMB) : totals.OrderByDescending(t => t.AvgCpu)).Take(25).ToList();
        ProcessHeader.Text = byMemory ? T("Biggest processes in this period") : T("Busiest processes in this period");
    }

    private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessTotal t && e.OriginalSource is FrameworkElement { DataContext: ProcessTotal })
            new HistoryWindow(monitor, log, limits, settings, (int?)null, t.Name) { Owner = this }.Show();
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Location_Click(object sender, RoutedEventArgs e)
    {
        if (processPath != null) Process.Start("explorer.exe", $"/select,\"{processPath}\"");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = T("CSV file (*.csv)|*.csv"),
            InitialDirectory = AppPaths.DocumentsFolder,
            FileName = $"{TitleText.Text} {Range.Label} {DateTime.Now:yyyy-MM-dd HHmm}.csv".Replace("(", "").Replace(")", ""),
        };
        Directory.CreateDirectory(AppPaths.DocumentsFolder);
        if (dialog.ShowDialog(this) != true) return;
        var sb = new StringBuilder("Time");
        bool live = Range.Live;
        foreach (var m in shownMetrics) sb.Append(live ? $",{m.Name} ({m.Unit})" : $",{m.Name} average ({m.Unit}),{m.Name} peak ({m.Unit})");
        sb.AppendLine();
        foreach (var r in shown)
        {
            sb.Append(r.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            foreach (int col in shownColumns)
            {
                sb.Append(',').Append(Num(r.Avg[col]));
                if (!live) sb.Append(',').Append(Num(r.Max[col]));
            }
            sb.AppendLine();
        }
        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString());
            StatusText.Text = F("Saved {0:#,0} rows to {1}.", shown.Count, dialog.FileName);
        }
        catch (Exception ex) { StatusText.Text = T("Couldn't save: ") + ex.Message; }
    }

    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class StringExtensions
{
    public static string FirstUpper(this string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
