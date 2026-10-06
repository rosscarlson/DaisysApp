using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DaisysApp.Applets.Performance;

public partial class PerformanceView : UserControl
{
    private const int TileSeconds = 120;

    private readonly PerfMonitor monitor;
    private readonly PerfLog log;
    private readonly List<PerfSample> recent = new();
    private readonly List<Tile> tiles = new();
    private readonly ObservableCollection<ProcessRow> processRows = new();
    private readonly Dictionary<int, ProcessRow> rowsByPid = new();
    private readonly ListCollectionView processView;
    private readonly DispatcherTimer slowTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private TextBlock? uptimeText;
    private CoresTile? coresTile;

    public PerformanceView(PerfMonitor monitor, PerfLog log)
    {
        this.monitor = monitor;
        this.log = log;
        InitializeComponent();

        processView = new ListCollectionView(processRows) { IsLiveSorting = true, IsLiveFiltering = true };
        foreach (var p in new[] { nameof(ProcessRow.Cpu), nameof(ProcessRow.MemoryMB), nameof(ProcessRow.Gpu), nameof(ProcessRow.VramMB), nameof(ProcessRow.IoMBps), nameof(ProcessRow.Threads) })
            processView.LiveSortingProperties.Add(p);
        processView.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.Cpu), ListSortDirection.Descending));
        ProcessGrid.ItemsSource = processView;
        ProcessGrid.Loaded += (_, _) =>
        {
            if (ProcessGrid.Columns.FirstOrDefault(c => c.SortMemberPath == nameof(ProcessRow.Cpu)) is { } cpuColumn) cpuColumn.SortDirection = ListSortDirection.Descending;
        };

        BuildTiles();
        monitor.Sampled += OnSample;
        monitor.ProcessesSampled += OnProcesses;
        foreach (var s in monitor.Live().TakeLast(TileSeconds)) recent.Add(s);

        slowTimer.Tick += (_, _) => RefreshDrives();
        IsVisibleChanged += (_, _) =>
        {
            monitor.Watching = IsVisible;
            if (IsVisible) { RefreshDrives(); slowTimer.Start(); } else slowTimer.Stop();
        };
    }

    // ---------------------------------------------------------------- tiles

    /// <summary>Theme brush key or colour for each metric's line.</summary>
    public static string ColorOf(Metric m) => m switch
    {
        Metric.Gpu or Metric.GpuClock => "#7FD07A",
        Metric.Ram or Metric.RamUsed or Metric.Commit => "#B48EF0",
        Metric.Vram or Metric.VramUsed => "#F7A541",
        Metric.DiskWrite or Metric.NetUp => "#F7A541",
        Metric.GpuTemp => "#FF7B72",
        Metric.CpuTemp or Metric.CpuPower => "AccentBrush",
        Metric.GpuPower => "#F2CC60",
        Metric.GpuFan => "#7FD07A",
        _ => "AccentBrush",
    };

    private void BuildTiles()
    {
        Add(new Tile(MetricGroup.Cpu, s => Pct(s[Metric.Cpu]), s => new[]
        {
            Join(MetricInfo.Of(Metric.CpuClock).Text(s[Metric.CpuClock])),
            s.Processes > 0 ? $"{s.Processes} processes · {s.Threads:#,0} threads" : "",
        }));
        Add(new Tile(MetricGroup.Gpu, s => Pct(s[Metric.Gpu]), s => new[]
        {
            Join(MetricInfo.Of(Metric.GpuClock).Text(s[Metric.GpuClock]), MetricInfo.Of(Metric.GpuPower).Text(s[Metric.GpuPower])),
            monitor.Info.Gpu,
        }));
        Add(new Tile(MetricGroup.Memory, s => Pct(s[Metric.Ram]), s => new[]
        {
            $"{Gb(s[Metric.RamUsed])} of {Gb(monitor.Info.RamBytes / MetricInfo.GB)} in use",
            $"Committed {Gb(s[Metric.Commit])}",
        }));
        Add(new Tile(MetricGroup.Vram, s => Pct(s[Metric.Vram]), s => new[]
        {
            $"{Gb(s[Metric.VramUsed])} of {Gb(monitor.Info.VramBytes / MetricInfo.GB)} in use",
            "",
        }));
        Add(new Tile(MetricGroup.Disk, s => Pct(s[Metric.DiskActive]), s => new[]
        {
            $"Read {MetricInfo.Of(Metric.DiskRead).Text(s[Metric.DiskRead])}",
            $"Write {MetricInfo.Of(Metric.DiskWrite).Text(s[Metric.DiskWrite])}",
        }, valueTip: "Active time"));
        Add(new Tile(MetricGroup.Network, s => double.IsNaN(s[Metric.NetDown]) ? "—" : $"{s[Metric.NetDown]:0.0} Mbit/s", s => new[]
        {
            $"Down {MetricInfo.Of(Metric.NetDown).Text(s[Metric.NetDown])}",
            $"Up {MetricInfo.Of(Metric.NetUp).Text(s[Metric.NetUp])}",
        }, valueTip: "Download, Mbit/s"));
        Add(new Tile(MetricGroup.Temperature, s => Hottest(s), s => new[]
        {
            Join(Temp("CPU", s[Metric.CpuTemp]), Temp("GPU", s[Metric.GpuTemp])) is { Length: > 0 } temps ? temps : "No temperature sensors",
            monitor.HasCpuSensors || monitor.Info.HasGpuSensors
                ? Join(Watts("CPU", s[Metric.CpuPower]), Watts("GPU", s[Metric.GpuPower]), double.IsNaN(s[Metric.GpuFan]) ? "—" : $"fan {s[Metric.GpuFan]:0}%")
                : "CPU needs LibreHardwareMonitor (Settings)",
        }, valueTip: "The hottest of the CPU and GPU"));

        coresTile = new CoresTile(OpenHistory);
        Tiles.Children.Add(coresTile.Root);
    }

    private void Add(Tile t)
    {
        t.Clicked += OpenHistory;
        tiles.Add(t);
        Tiles.Children.Add(t.Root);
    }

    private static string Pct(double v) => double.IsNaN(v) ? "—" : $"{v:0}%";
    private static string Temp(string what, double v) => double.IsNaN(v) ? "—" : $"{what} {v:0}°C";
    private static string Watts(string what, double v) => double.IsNaN(v) ? "—" : $"{what} {v:0} W";

    private static string Hottest(PerfSample s)
    {
        double t = new[] { s[Metric.CpuTemp], s[Metric.GpuTemp] }.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
        return double.IsNaN(t) ? "—" : $"{t:0}°";
    }
    private static string Gb(double v) => double.IsNaN(v) ? "—" : $"{v:0.0} GB";
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => p != "—" && !p.StartsWith("— ")));

    private void OnSample(PerfSample s)
    {
        recent.Add(s);
        if (recent.Count > TileSeconds) recent.RemoveRange(0, recent.Count - TileSeconds);
        if (!IsVisible) return;
        foreach (var t in tiles) t.Update(s, recent);
        coresTile?.Update(s);
        if (uptimeText != null) uptimeText.Text = Uptime();
        if (InfoGrid.Children.Count == 0 && monitor.Info.Threads > 0) BuildInfo();
    }

    private void OpenHistory(MetricGroup group) =>
        new HistoryWindow(monitor, log, group) { Owner = Window.GetWindow(this) }.Show();

    // ---------------------------------------------------------------- processes

    private void OnProcesses(IReadOnlyList<ProcessSample> list)
    {
        var seen = new HashSet<int>();
        foreach (var p in list)
        {
            seen.Add(p.Pid);
            if (!rowsByPid.TryGetValue(p.Pid, out var row))
            {
                row = new ProcessRow(p.Pid, p.Name);
                rowsByPid[p.Pid] = row;
                processRows.Add(row);
            }
            row.Update(p);
        }
        foreach (var gone in rowsByPid.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            processRows.Remove(rowsByPid[gone]);
            rowsByPid.Remove(gone);
        }
        ProcessCount.Text = $"{processRows.Count}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = SearchBox.Text.Trim();
        processView.Filter = q.Length == 0 ? null : o => o is ProcessRow r && r.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Numbers sort biggest first on the first click.</summary>
    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        string path = e.Column.SortMemberPath;
        bool text = path == nameof(ProcessRow.Name);
        var dir = e.Column.SortDirection switch
        {
            null => text ? ListSortDirection.Ascending : ListSortDirection.Descending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => ListSortDirection.Ascending,
        };
        foreach (var c in ProcessGrid.Columns) if (c != e.Column) c.SortDirection = null;
        e.Column.SortDirection = dir;
        processView.SortDescriptions.Clear();
        processView.SortDescriptions.Add(new SortDescription(path, dir));
        if (!processView.LiveSortingProperties.Contains(path)) processView.LiveSortingProperties.Add(path);
    }

    private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessRow row && e.OriginalSource is FrameworkElement { DataContext: ProcessRow })
            new HistoryWindow(monitor, log, row.Pid, row.Name) { Owner = Window.GetWindow(this) }.Show();
    }

    // ---------------------------------------------------------------- system and storage

    private void BuildInfo()
    {
        var i = monitor.Info;
        AddInfo("Processor", i.Cpu);
        AddInfo("Cores", $"{i.Cores} cores, {i.Threads} threads");
        AddInfo("Memory", Gb(i.RamBytes / MetricInfo.GB));
        AddInfo("Graphics", i.Gpu);
        AddInfo("Video memory", Gb(i.VramBytes / MetricInfo.GB));
        if (i.GpuDriver.Length > 0) AddInfo("Driver", i.GpuDriver);
        AddInfo("Windows", i.Windows);
        uptimeText = AddInfo("Up for", Uptime());
    }

    private TextBlock AddInfo(string label, string value)
    {
        int row = InfoGrid.RowDefinitions.Count;
        InfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, Style = (Style)FindResource("InfoLabel") };
        var v = new TextBlock { Text = value, Style = (Style)FindResource("InfoValue") };
        Grid.SetRow(l, row);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        InfoGrid.Children.Add(l);
        InfoGrid.Children.Add(v);
        return v;
    }

    private static string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes} min" : $"{t.Hours} h {t.Minutes} min";
    }

    public sealed record DriveRow(string Name, string Free, double Used, Brush BarBrush);

    private void RefreshDrives()
    {
        var list = new List<DriveRow>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                double total = d.TotalSize, free = d.AvailableFreeSpace;
                double used = total > 0 ? (total - free) / total : 0;
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "Removable" : "Local disk") : d.VolumeLabel;
                var brush = (Brush)FindResource(used > 0.9 ? "DangerBrush" : "AccentBrush");
                list.Add(new DriveRow($"{d.Name.TrimEnd('\\')}  {label}", $"{Size(free)} free of {Size(total)}", used, brush));
            }
        }
        catch { }
        DriveList.ItemsSource = list;
    }

    private static string Size(double bytes) => bytes >= 1024 * MetricInfo.GB ? $"{bytes / (1024 * MetricInfo.GB):0.0} TB" : $"{bytes / MetricInfo.GB:0} GB";

    public void Shutdown()
    {
        monitor.Sampled -= OnSample;
        monitor.ProcessesSampled -= OnProcesses;
        slowTimer.Stop();
    }

    // ---------------------------------------------------------------- tile controls

    private sealed class Tile
    {
        private readonly MetricGroup group;
        private readonly Func<PerfSample, string> value;
        private readonly Func<PerfSample, string[]> lines;
        private readonly TextBlock valueText, line1, line2;
        private readonly LineGraph graph = new() { Height = 64, Margin = new Thickness(0, 10, 0, 0) };

        public Tile(MetricGroup group, Func<PerfSample, string> value, Func<PerfSample, string[]> lines, string? valueTip = null)
        {
            this.group = group;
            this.value = value;
            this.lines = lines;
            valueText = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Text = "—", ToolTip = valueTip };
            var title = new TextBlock { Text = group.Title, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
            title.Margin = new Thickness(0);
            line1 = SubText();
            line2 = SubText();
            var header = new DockPanel();
            DockPanel.SetDock(valueText, Dock.Right);
            header.Children.Add(valueText);
            header.Children.Add(title);
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(line1);
            stack.Children.Add(line2);
            stack.Children.Add(graph);
            Root = new Border { Child = stack, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, ToolTip = $"Click for {group.Title.ToLowerInvariant()} history" };
            Root.SetResourceReference(FrameworkElement.StyleProperty, "Card");
            Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(group);
        }

        public Border Root { get; }
        public event Action<MetricGroup>? Clicked;

        private static TextBlock SubText()
        {
            var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            return t;
        }

        public void Update(PerfSample s, IReadOnlyList<PerfSample> recent)
        {
            valueText.Text = value(s);
            var l = lines(s);
            line1.Text = l.ElementAtOrDefault(0) ?? "";
            line2.Text = l.ElementAtOrDefault(1) ?? "";
            var series = group.Graph.Select((m, i) => new GraphSeries
            {
                Name = MetricInfo.Of(m).Name,
                Metric = MetricInfo.Of(m),
                Points = recent.Select(x => (x.Time, x[m])).ToList(),
                Color = ColorOf(m),
                Fill = i == 0,
            }).ToList();
            var first = MetricInfo.Of(group.Graph[0]);
            graph.Show(series, s.Time.AddSeconds(-TileSeconds), s.Time, first.FixedMax, TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>A bar per logical processor (click for CPU history).</summary>
    private sealed class CoresTile
    {
        private readonly UniformGrid bars = new() { Rows = 1, Height = 96, Margin = new Thickness(0, 12, 0, 0) };
        private readonly TextBlock summary;
        private readonly List<ScaleTransform> scales = new();

        public CoresTile(Action<MetricGroup> open)
        {
            var title = new TextBlock { Text = "CPU cores", Margin = new Thickness(0) };
            title.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
            summary = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
            summary.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(summary);
            stack.Children.Add(bars);
            Root = new Border { Child = stack, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, ToolTip = "Each logical processor's load. Click for CPU history." };
            Root.SetResourceReference(FrameworkElement.StyleProperty, "Card");
            Root.MouseLeftButtonUp += (_, _) => open(MetricGroup.Cpu);
        }

        public Border Root { get; }

        public void Update(PerfSample s)
        {
            if (scales.Count != s.Cores.Length) Build(s.Cores.Length);
            for (int i = 0; i < scales.Count; i++) scales[i].ScaleY = s.Cores[i] / 100;
            summary.Text = s.Cores.Length == 0 ? "" : $"Busiest {s.Cores.Max():0}% · least busy {s.Cores.Min():0}%";
        }

        private void Build(int n)
        {
            bars.Children.Clear();
            scales.Clear();
            bars.Columns = Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                var scale = new ScaleTransform(1, 0);
                var fill = new Rectangle { RadiusX = 2, RadiusY = 2, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale };
                fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
                var back = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(1.5, 0, 1.5, 0), Child = fill, ToolTip = $"Logical processor {i}" };
                back.SetResourceReference(Border.BackgroundProperty, "ControlBorderBrush");
                bars.Children.Add(back);
                scales.Add(scale);
            }
        }
    }
}
