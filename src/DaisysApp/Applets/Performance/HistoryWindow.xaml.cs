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
    private readonly MetricGroup? group;
    private readonly int? pid;
    private readonly string? processName;
    private string? processPath;

    // what's on screen, for Export
    private List<(DateTime Time, double[] Avg, double[] Max)> shown = new();
    private IReadOnlyList<MetricInfo> shownMetrics = Array.Empty<MetricInfo>();

    // process-mode metrics (not part of the system-wide list)
    private static readonly MetricInfo ProcCpu = new(Metric.Cpu, "cpu", "CPU", "%", "0.0", null);
    private static readonly MetricInfo ProcMemory = new(Metric.RamUsed, "memory", "Memory", "MB", "#,0", null);
    private static readonly MetricInfo ProcGpu = new(Metric.Gpu, "gpu", "GPU", "%", "0.0", null);
    private static readonly MetricInfo ProcVram = new(Metric.VramUsed, "vram", "Video memory", "MB", "#,0", null);
    private static readonly MetricInfo ProcIo = new(Metric.DiskRead, "io", "Disk", "MB/s", "0.00", null);

    /// <summary>History of a tile's metrics.</summary>
    public HistoryWindow(PerfMonitor monitor, PerfLog log, MetricGroup group) : this(monitor, log)
    {
        this.group = group;
        Title = $"{group.Title} history";
        TitleText.Text = group.Title;
        SubtitleText.Text = group.Title switch
        {
            "CPU" => $"{monitor.Info.Cpu} · {monitor.Info.Cores} cores, {monitor.Info.Threads} threads",
            "GPU" or "Video memory" => monitor.Info.Gpu,
            "Temperatures" => $"{monitor.Info.Cpu} · {monitor.Info.Gpu}",
            "Memory" => $"{monitor.Info.RamBytes / MetricInfo.GB:0.0} GB installed",
            _ => "",
        };
        ProcessCard.Visibility = group.ShowProcesses ? Visibility.Visible : Visibility.Collapsed;
        Ready();
    }

    /// <summary>History of one process (or, with no id, every process with that name).</summary>
    public HistoryWindow(PerfMonitor monitor, PerfLog log, int? pid, string name) : this(monitor, log)
    {
        this.pid = pid;
        processName = name;
        Title = $"{name} — history";
        TitleText.Text = name;
        try
        {
            if (pid is int id)
            {
                using var p = Process.GetProcessById(id);
                processPath = p.MainModule?.FileName;
                SubtitleText.Text = $"Process ID {id}" + (processPath != null ? $" · {processPath}" : "") + $" · started {p.StartTime:g}";
            }
            else SubtitleText.Text = "Every process with this name";
        }
        catch { SubtitleText.Text = pid is int id ? $"Process ID {id} (details need administrator rights, or it has ended)" : ""; }
        LocationButton.Visibility = processPath != null ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.Visibility = Visibility.Collapsed;
        Ready();
    }

    private HistoryWindow(PerfMonitor monitor, PerfLog log)
    {
        this.monitor = monitor;
        this.log = log;
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        monitor.Sampled += OnSample;
        Closed += (_, _) => monitor.Sampled -= OnSample;
    }

    private void Ready()
    {
        var ranges = new List<RangeOption>
        {
            new("Last 10 minutes (live)", () => (DateTime.Now.AddSeconds(-PerfMonitor.LiveSeconds), DateTime.Now), true),
            new("Last hour", () => (DateTime.Now.AddHours(-1), DateTime.Now), false),
            new("Last 6 hours", () => (DateTime.Now.AddHours(-6), DateTime.Now), false),
            new("Last 24 hours", () => (DateTime.Now.AddDays(-1), DateTime.Now), false),
            new("Last 7 days", () => (DateTime.Now.AddDays(-7), DateTime.Now), false),
            new("Last 30 days", () => (DateTime.Now.AddDays(-30), DateTime.Now), false),
            new("Today", () => (DateTime.Today, DateTime.Now), false),
            new("Yesterday", () => (DateTime.Today.AddDays(-1), DateTime.Today.AddTicks(-1)), false),
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
        else LoadProcess(range, from, to, peaks);
    }

    private void LoadGroup(RangeOption range, DateTime from, DateTime to, bool peaks)
    {
        var metrics = group!.Graph.Concat(group.Extra).Select(MetricInfo.Of).ToList();
        TimeSpan gap;
        if (range.Live)
        {
            shown = monitor.Live().Select(s => (s.Time, s.Values, s.Values)).ToList();
            gap = TimeSpan.FromSeconds(5);
            StatusText.Text = "Live: one reading a second, updating.";
        }
        else
        {
            var rows = log.Read(from, to);
            (shown, gap) = Downsample(rows.Select(r => (r.Time, r.Avg, r.Max)).ToList(), from, to);
            StatusText.Text = rows.Count == 0
                ? (log.Enabled ? "Nothing was logged in this period (the log starts when Daisy's App is running)." : "Logging is off (Settings → Performance).")
                : $"{rows.Count:#,0} readings, each the average of 10 seconds" + (shown.Count < rows.Count ? $", shown as {shown.Count:#,0} points." : ".");
        }
        shownMetrics = metrics;

        GraphPanel.Children.Clear();
        AddGraph(group.GraphTitle, group.Graph.Select(MetricInfo.Of).ToList(), from, to, gap, peaks, 260);
        foreach (var m in group.Extra.Select(MetricInfo.Of))
            if (shown.Any(r => !double.IsNaN(r.Avg[(int)m.Id])))
                AddGraph(m.Name, new[] { m }, from, to, gap, peaks, 150);

        BuildStats(metrics);
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
            StatusText.Text = "Live: the last 10 minutes (or since Daisy's App started), updating.";
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
                ? "Nothing was logged in this period."
                : $"The log records the 5 busiest processes (by CPU and by memory) every 10 seconds; {processName} was among them in {present:#,0} of {rows.Count:#,0} readings. Gaps are times it wasn't.";
        }
        shownMetrics = metrics;

        GraphPanel.Children.Clear();
        for (int i = 0; i < metrics.Count; i++)
            if (i == 0 ? shown.Any(r => !double.IsNaN(r.Avg[i])) : shown.Any(r => r.Avg[i] > 0)) // skip graphs that are all zero
                AddGraph(metrics[i].Name, new[] { metrics[i] }, from, to, gap, peaks, i == 0 ? 220 : 150, index: i);
        BuildStats(metrics, processMode: true);
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

    private void AddGraph(string title, IReadOnlyList<MetricInfo> metrics, DateTime from, DateTime to, TimeSpan gap, bool peaks, double height, int? index = null)
    {
        var series = new List<GraphSeries>();
        for (int k = 0; k < metrics.Count; k++)
        {
            var m = metrics[k];
            int col = index ?? (int)m.Id;
            string color = group != null ? PerformanceView.ColorOf(m.Id) : (index switch { 1 => "#B48EF0", 2 => "#7FD07A", 3 => "#F7A541", 4 => "#4CC2FF", _ => "AccentBrush" });
            if (peaks) series.Add(new GraphSeries { Name = m.Name + " peak", Metric = m, Points = shown.Select(r => (r.Time, r.Max[col])).ToList(), Color = color, Faint = true, Fill = false });
            series.Add(new GraphSeries { Name = m.Name, Metric = m, Points = shown.Select(r => (r.Time, r.Avg[col])).ToList(), Color = color, Fill = k == 0 });
        }

        var graph = new LineGraph { Detailed = true, Height = height, Margin = new Thickness(0, 8, 0, 0) };
        graph.Show(series, from, to, metrics[0].FixedMax, gap);

        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
        foreach (var s in series.Where(s => !s.Faint))
        {
            var swatch = new Border { Width = 12, Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (s.Color.StartsWith('#')) swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Color));
            else swatch.SetResourceReference(Border.BackgroundProperty, s.Color);
            var text = new TextBlock { Text = s.Name };
            text.SetResourceReference(StyleProperty, "SecondaryText");
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            item.Children.Add(swatch);
            item.Children.Add(text);
            legend.Children.Add(item);
        }

        var header = new DockPanel();
        DockPanel.SetDock(legend, Dock.Right);
        legend.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(legend);
        var titleText = new TextBlock { Text = title, Margin = new Thickness(0) };
        titleText.SetResourceReference(StyleProperty, "CardHeader");
        header.Children.Add(titleText);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(graph);
        var card = new Border { Child = stack };
        card.SetResourceReference(StyleProperty, "Card");
        GraphPanel.Children.Add(card);
    }

    private void BuildStats(IReadOnlyList<MetricInfo> metrics, bool processMode = false)
    {
        StatsGrid.Children.Clear();
        StatsGrid.RowDefinitions.Clear();
        StatsGrid.ColumnDefinitions.Clear();
        StatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) StatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        AddRow(new[] { "", "Lowest", "Average", "Highest", "95% of the time under" }, header: true);
        for (int k = 0; k < metrics.Count; k++)
        {
            var m = metrics[k];
            int col = processMode ? k : (int)m.Id;
            var avgs = shown.Select(r => r.Avg[col]).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
            if (avgs.Count == 0) continue;
            double peak = shown.Select(r => r.Max[col]).Where(v => !double.IsNaN(v)).DefaultIfEmpty(avgs[^1]).Max();
            double p95 = avgs[(int)Math.Min(avgs.Count - 1, Math.Ceiling(avgs.Count * 0.95) - 1)];
            AddRow(new[] { m.Name, m.Text(avgs[0]), m.Text(avgs.Average()), m.Text(peak), m.Text(p95) });
        }
        if (StatsGrid.RowDefinitions.Count == 1) AddRow(new[] { "No readings in this period." });

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
            ProcessNote.Text = "Over the last 10 minutes. Double-click a process for its graphs.";
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
            ProcessNote.Text = "From the log, which keeps the 5 busiest processes by CPU and by memory every 10 seconds (so quieter processes count as 0). Double-click a process for its graphs.";
        }
        bool byMemory = group == MetricGroup.Memory;
        ProcessGrid.ItemsSource = (byMemory ? totals.OrderByDescending(t => t.PeakMemoryMB) : totals.OrderByDescending(t => t.AvgCpu)).Take(25).ToList();
        ProcessHeader.Text = byMemory ? "Biggest processes in this period" : "Busiest processes in this period";
    }

    private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessTotal t && e.OriginalSource is FrameworkElement { DataContext: ProcessTotal })
            new HistoryWindow(monitor, log, null, t.Name) { Owner = this }.Show();
    }

    // ---------------------------------------------------------------- buttons

    private void Location_Click(object sender, RoutedEventArgs e)
    {
        if (processPath != null) Process.Start("explorer.exe", $"/select,\"{processPath}\"");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
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
            foreach (var m in shownMetrics)
            {
                sb.Append(',').Append(Num(r.Avg[(int)m.Id]));
                if (!live) sb.Append(',').Append(Num(r.Max[(int)m.Id]));
            }
            sb.AppendLine();
        }
        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString());
            StatusText.Text = $"Saved {shown.Count:#,0} rows to {dialog.FileName}.";
        }
        catch (Exception ex) { StatusText.Text = "Couldn't save: " + ex.Message; }
    }

    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
}
