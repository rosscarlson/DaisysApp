using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly PerfLimits limits;
    private readonly PerformanceSettings settings;
    private string sortPath = nameof(ProcessRow.Cpu);
    private ListSortDirection sortDirection = ListSortDirection.Descending;
    private readonly List<PerfSample> recent = new();
    private readonly List<Tile> tiles = new();
    private readonly ObservableCollection<ProcessRow> processRows = new();
    private readonly Dictionary<int, ProcessRow> rowsByPid = new();
    private readonly ListCollectionView processView;
    private readonly DispatcherTimer slowTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private TextBlock? uptimeText;
    private CoresTile? coresTile;
    private readonly SensorPanel sensorPanel;

    public PerformanceView(PerfMonitor monitor, PerfLog log, PerfLimits limits, PerformanceSettings settings, PingMonitor pings, SpeedTester speed)
    {
        this.monitor = monitor;
        this.log = log;
        this.limits = limits;
        this.settings = settings;
        InitializeComponent();

        // Apps first, then background processes (like Task Manager). The rows are kept in order by moving them in
        // the collection (ApplyOrder): WPF's live sorting stops reordering once the list is grouped.
        processView = new ListCollectionView(processRows);
        // named up front, so the groups are in this order whichever kind of process shows up first
        var byGroup = new PropertyGroupDescription(nameof(ProcessRow.Group));
        byGroup.GroupNames.Add(T("Apps"));
        byGroup.GroupNames.Add(T("Background processes"));
        processView.GroupDescriptions.Add(byGroup);
        ProcessGrid.ItemsSource = processView;
        limits.Changed += () =>
        {
            foreach (var r in processRows) r.Severity = limits.ProcessSeverity(r);
            monitor.RefreshProcessesSoon();
        };
        ProcessGrid.Loaded += (_, _) =>
        {
            if (ProcessGrid.Columns.FirstOrDefault(c => c.SortMemberPath == nameof(ProcessRow.Cpu)) is { } cpuColumn) cpuColumn.SortDirection = ListSortDirection.Descending;
        };

        BuildTiles();
        sensorPanel = new SensorPanel(SensorFilters, SensorGroups, OpenSensors);
        monitor.SensorsUpdated += OnSensors;
        monitor.Sampled += OnSample;
        monitor.ProcessesSampled += OnProcesses;
        foreach (var s in monitor.Live().TakeLast(TileSeconds)) recent.Add(s);

        slowTimer.Tick += (_, _) => RefreshDrives();
        InitNetwork(pings, speed);
        IsVisibleChanged += (_, _) =>
        {
            monitor.Watching = IsVisible;
            if (IsVisible) { RefreshDrives(); slowTimer.Start(); monitor.RefreshProcessesSoon(); } else slowTimer.Stop();
        };
    }

    // ---------------------------------------------------------------- tiles

    /// <summary>Theme brush key or colour for each metric's line.</summary>
    public static string ColorOf(Metric m) => m switch
    {
        Metric.Gpu or Metric.GpuClock => "#7FD07A",
        Metric.Ram or Metric.RamUsed => "#B48EF0",
        Metric.Commit => "#E58AD8",
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
        Add(new Tile(MetricGroup.Cpu, new[] { "cpu" }, s => Pct(s[Metric.Cpu]), s => new[]
        {
            Join(MetricInfo.Of(Metric.CpuClock).Text(s[Metric.CpuClock])),
            s.Processes > 0 ? F("{0} processes · {1:#,0} threads", s.Processes, s.Threads) : "",
        }));
        Add(new Tile(MetricGroup.Gpu, new[] { "gpu" }, s => Pct(s[Metric.Gpu]), s => new[]
        {
            Join(MetricInfo.Of(Metric.GpuClock).Text(s[Metric.GpuClock]), MetricInfo.Of(Metric.GpuPower).Text(s[Metric.GpuPower])),
            monitor.Info.Gpu,
        }));
        Add(new Tile(MetricGroup.Memory, new[] { "ram" }, s => Pct(s[Metric.Ram]), s => new[]
        {
            F("{0} of {1} in use", Gb(s[Metric.RamUsed]), Gb(monitor.Info.RamBytes / MetricInfo.GB)),
            F("Committed {0}", Gb(s[Metric.Commit])),
        }));
        Add(new Tile(MetricGroup.Vram, new[] { "vram" }, s => Pct(s[Metric.Vram]), s => new[]
        {
            F("{0} of {1} in use", Gb(s[Metric.VramUsed]), Gb(monitor.Info.VramBytes / MetricInfo.GB)),
            "",
        }));

        coresTile = new CoresTile(OpenHistory);
        Tiles.Children.Add(coresTile.Root);
        Add(new Tile(MetricGroup.Disk, new[] { "diskActive" }, s => Pct(s[Metric.DiskActive]), s => new[]
        {
            F("Read {0} · write {1}", MetricInfo.Of(Metric.DiskRead).Text(s[Metric.DiskRead]), MetricInfo.Of(Metric.DiskWrite).Text(s[Metric.DiskWrite])),
            s.BusiestDisk.Length > 0 ? F("Busiest: {0}", s.BusiestDisk) : "",
        }, valueTip: T("Active time of the busiest disk (reads and writes are all disks together)")));
        Add(new Tile(MetricGroup.Network, new[] { "netDown", "netUp" }, s => double.IsNaN(s[Metric.NetDown]) ? "—" : $"{s[Metric.NetDown]:0.0} Mbit/s", s => new[]
        {
            F("Down {0}", MetricInfo.Of(Metric.NetDown).Text(s[Metric.NetDown])),
            F("Up {0}", MetricInfo.Of(Metric.NetUp).Text(s[Metric.NetUp])),
        }, valueTip: T("Download, Mbit/s")));
        Add(new Tile(MetricGroup.Temperature, new[] { "cpuTemp", "gpuTemp" }, s => Hottest(s), s => new[]
        {
            Join(Temp("CPU", s[Metric.CpuTemp]), Temp("GPU", s[Metric.GpuTemp])) is { Length: > 0 } temps ? temps : T("No temperature sensors"),
            monitor.HasCpuSensors || monitor.Info.HasGpuSensors
                ? Join(Watts("CPU", s[Metric.CpuPower]), Watts("GPU", s[Metric.GpuPower]), double.IsNaN(s[Metric.GpuFan]) ? "—" : F("fan {0:0}%", s[Metric.GpuFan]))
                : T("CPU needs LibreHardwareMonitor (Settings)"),
        }, valueTip: T("The hottest of the CPU and GPU")));
    }

    /// <summary>A limit's value judged on the last 3 seconds' average, so a single spike doesn't colour a tile.</summary>
    private static double Recent(string key, IReadOnlyList<PerfSample> recent)
    {
        Func<PerfSample, double> value = key == PerfLimits.CpuCore
            ? s => s.Cores.Length > 0 ? s.Cores.Max() : double.NaN
            : MetricInfo.All.FirstOrDefault(m => m.Key == key) is { } m ? s => s[m.Id] : _ => double.NaN;
        var values = recent.TakeLast(3).Select(value).Where(v => !double.IsNaN(v)).ToList();
        return values.Count > 0 ? values.Average() : double.NaN;
    }

    private int TileSeverity(IEnumerable<string> keys) => keys.Select(k => limits.Severity(k, Recent(k, recent))).DefaultIfEmpty(0).Max();

    /// <summary>Orange or red border and value for a tile over its levels.</summary>
    private static void ShowSeverity(Border tile, TextBlock value, int severity)
    {
        if (severity == 0)
        {
            tile.ClearValue(Border.BorderBrushProperty);
            tile.ClearValue(Border.BorderThicknessProperty);
            value.ClearValue(TextBlock.ForegroundProperty);
            return;
        }
        Brush brush = severity == 2 ? (Brush)tile.FindResource("DangerBrush") : new SolidColorBrush(Color.FromRgb(0xF7, 0xA5, 0x41));
        tile.BorderBrush = brush;
        tile.BorderThickness = new Thickness(2);
        value.Foreground = brush;
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
        foreach (var t in tiles)
        {
            t.Update(s, recent);
            ShowSeverity(t.Root, t.ValueText, TileSeverity(t.LimitKeys));
        }
        if (coresTile != null)
        {
            coresTile.Update(s);
            ShowSeverity(coresTile.Root, coresTile.Summary, TileSeverity(new[] { PerfLimits.CpuCore }));
        }
        if (uptimeText != null) uptimeText.Text = Uptime();
        if (InfoGrid.Children.Count == 0 && monitor.Info.Threads > 0) BuildInfo();
    }

    // ---------------------------------------------------------------- hardware sensors (LibreHardwareMonitor)

    private void OnSensors(HwSnapshot? snapshot)
    {
        SensorCard.Visibility = snapshot != null ? Visibility.Visible : Visibility.Collapsed;
        SensorBanner.Visibility = snapshot == null ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot == null)
            SensorBannerText.Text = HardwareMonitor.ProcessRunning()
                ? T("LibreHardwareMonitor is running, but its web server is off, so Daisy's App can't read its sensors (temperatures, fans, voltages, power).")
                : T("CPU, motherboard, memory and drive temperatures, fan speeds, voltages and power need LibreHardwareMonitor.");
        sensorPanel.Update(snapshot);
    }

    private void MoreData_Click(object sender, RoutedEventArgs e) =>
        new SensorSetupWindow(monitor) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void OpenSensors(IReadOnlyList<HwSensor> sensors, string title) =>
        new HistoryWindow(monitor, log, limits, settings, sensors, title) { Owner = Window.GetWindow(this) }.Show();

    private void OpenHistory(MetricGroup group) =>
        new HistoryWindow(monitor, log, limits, settings, group) { Owner = Window.GetWindow(this) }.Show();

    // ---------------------------------------------------------------- processes

    private void OnProcesses(IReadOnlyList<ProcessSample> list)
    {
        var appNames = AppNames(list);
        var seen = new HashSet<int>();
        foreach (var p in list)
        {
            seen.Add(p.Pid);
            bool isApp = appNames.Contains(p.Name);
            if (!rowsByPid.TryGetValue(p.Pid, out var row))
            {
                row = new ProcessRow(p.Pid, p.Name) { IsApp = isApp };
                rowsByPid[p.Pid] = row;
                processRows.Add(row);
            }
            else if (row.IsApp != isApp)
            {
                // moving between Apps and Background processes: the grouped view only regroups added rows
                processRows.Remove(row);
                row.IsApp = isApp;
                processRows.Add(row);
            }
            row.Update(p);
            row.GpuName = GpuNameFor(p.GpuEngine);
            row.Severity = limits.ProcessSeverity(row);
        }
        foreach (var gone in rowsByPid.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            processRows.Remove(rowsByPid[gone]);
            rowsByPid.Remove(gone);
        }
        ApplyOrder();
        ProcessCount.Text = $"{processRows.Count}";
    }

    /// <summary>Puts the rows in the chosen order (Apps first), moving only the ones that are out of place.</summary>
    private void ApplyOrder()
    {
        Func<ProcessRow, IComparable> key = sortPath switch
        {
            nameof(ProcessRow.Name) => r => r.Name.ToLowerInvariant(),
            nameof(ProcessRow.Pid) => r => r.Pid,
            nameof(ProcessRow.MemoryMB) => r => r.MemoryMB,
            nameof(ProcessRow.Gpu) => r => r.Gpu,
            nameof(ProcessRow.GpuEngine) => r => r.GpuEngine,
            nameof(ProcessRow.VramMB) => r => r.VramMB,
            nameof(ProcessRow.IoMBps) => r => r.IoMBps,
            nameof(ProcessRow.Threads) => r => r.Threads,
            _ => r => r.Cpu,
        };
        var grouped = processRows.OrderBy(r => r.GroupOrder);
        var wanted = (sortDirection == ListSortDirection.Ascending ? grouped.ThenBy(key) : grouped.ThenByDescending(key)).ThenBy(r => r.Pid).ToList();
        for (int i = 0; i < wanted.Count; i++)
            if (!ReferenceEquals(processRows[i], wanted[i])) processRows.Move(processRows.IndexOf(wanted[i]), i);
    }

    private void ProcessSettings_Click(object sender, RoutedEventArgs e) =>
        new LimitsWindow(limits, PerfLimits.Processes, T("Process warnings"), settings) { Owner = Window.GetWindow(this) }.ShowDialog();

    private string GpuNameFor(string engine)
    {
        if (engine.Length == 0) return "";
        foreach (var (index, name) in monitor.Info.Adapters.Values)
            if (engine.StartsWith($"GPU {index} ")) return name;
        return "";
    }

    /// <summary>
    /// Names of processes with a window on screen (visible, not owned by another window, not a tool window, not
    /// hidden by Windows), as Task Manager's Apps. Every process sharing such a name counts, so a browser's helper
    /// processes are listed with it.
    /// </summary>
    private static HashSet<string> AppNames(IReadOnlyList<ProcessSample> list)
    {
        var pids = new HashSet<int>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;
            if ((GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            pids.Add((int)pid);
            return true;
        }, IntPtr.Zero);
        return list.Where(p => pids.Contains(p.Pid) && p.Name is not ("explorer" or "TextInputHost" or "ApplicationFrameHost"))
            .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private const int GW_OWNER = 4, GWL_EXSTYLE = -20, DWMWA_CLOAKED = 14;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

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
        sortPath = path;
        sortDirection = dir;
        ApplyOrder();
    }

    private void ProcessGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessRow row && e.OriginalSource is FrameworkElement { DataContext: ProcessRow })
            new HistoryWindow(monitor, log, limits, settings, row.Pid, row.Name) { Owner = Window.GetWindow(this) }.Show();
    }

    // ---------------------------------------------------------------- system and storage

    private void BuildInfo()
    {
        var i = monitor.Info;
        AddInfo(T("Processor"), i.Cpu);
        AddInfo(T("Cores"), F("{0} cores, {1} threads", i.Cores, i.Threads));
        AddInfo(T("Memory"), Gb(i.RamBytes / MetricInfo.GB));
        AddInfo(T("Graphics"), i.Gpu);
        AddInfo(T("Video memory"), Gb(i.VramBytes / MetricInfo.GB));
        if (i.GpuDriver.Length > 0) AddInfo(T("Driver"), i.GpuDriver);
        AddInfo(T("Windows"), i.Windows);
        uptimeText = AddInfo(T("Up for"), Uptime());
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
        return t.TotalDays >= 1 ? F("{0} d {1} h {2} min", (int)t.TotalDays, t.Hours, t.Minutes) : F("{0} h {1} min", t.Hours, t.Minutes);
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
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? T("Removable") : T("Local disk")) : d.VolumeLabel;
                var brush = (Brush)FindResource(used > 0.9 ? "DangerBrush" : "AccentBrush");
                list.Add(new DriveRow($"{d.Name.TrimEnd('\\')}  {label}", F("{0} free of {1}", Size(free), Size(total)), used, brush));
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
        monitor.SensorsUpdated -= OnSensors;
        ShutdownNetwork();
        slowTimer.Stop();
    }

    // ---------------------------------------------------------------- tile controls

    private sealed class Tile
    {
        private readonly MetricGroup group;
        private readonly Func<PerfSample, string> value;
        public IReadOnlyList<string> LimitKeys { get; }
        public TextBlock ValueText => valueText;
        private readonly Func<PerfSample, string[]> lines;
        private readonly TextBlock valueText, line1, line2;
        private readonly LineGraph graph = new() { Height = 64, Margin = new Thickness(0, 10, 0, 0) };

        public Tile(MetricGroup group, IReadOnlyList<string> limitKeys, Func<PerfSample, string> value, Func<PerfSample, string[]> lines, string? valueTip = null)
        {
            this.group = group;
            LimitKeys = limitKeys;
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
            Root = new Border { Child = stack, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, ToolTip = F("Click for {0} history", group.Title.ToLowerInvariant()) };
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
        public TextBlock Summary => summary;

        public CoresTile(Action<MetricGroup> open)
        {
            var title = new TextBlock { Text = T("CPU cores"), Margin = new Thickness(0) };
            title.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
            summary = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
            summary.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(summary);
            stack.Children.Add(bars);
            Root = new Border { Child = stack, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, ToolTip = T("Each logical processor's load. Click for CPU history.") };
            Root.SetResourceReference(FrameworkElement.StyleProperty, "Card");
            Root.MouseLeftButtonUp += (_, _) => open(MetricGroup.Cpu);
        }

        public Border Root { get; }

        public void Update(PerfSample s)
        {
            if (scales.Count != s.Cores.Length) Build(s.Cores.Length);
            for (int i = 0; i < scales.Count; i++) scales[i].ScaleY = s.Cores[i] / 100;
            summary.Text = s.Cores.Length == 0 ? "" : F("Busiest {0:0}% · least busy {1:0}%", s.Cores.Max(), s.Cores.Min());
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
                var back = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(1.5, 0, 1.5, 0), Child = fill, ToolTip = F("Logical processor {0}", i) };
                back.SetResourceReference(Border.BackgroundProperty, "ControlBorderBrush");
                bars.Children.Add(back);
                scales.Add(scale);
            }
        }
    }
}
