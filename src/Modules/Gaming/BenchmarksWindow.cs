using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Every benchmark in a table (names editable). Tick one to see its report; once one is ticked only the runs that can
/// be compared with it are listed (same game and length), and ticking more shows them side by side, with how each
/// differs from the first and their frame rates over time.
/// </summary>
internal sealed class BenchmarksWindow : Window
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x4C, 0xD9, 0x64), Color.FromRgb(0x40, 0xC8, 0xFF), Color.FromRgb(0xFF, 0xB0, 0x20),
        Color.FromRgb(0xE0, 0x6C, 0xFF), Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xC8, 0xC8, 0xC8),
    };

    private readonly GamingService service;
    private readonly List<Row> rows = new();
    private readonly List<Row> ticked = new(); // in the order they were ticked: the first is what the rest are compared with
    private readonly DataGrid grid = new();
    private readonly ICollectionView view;
    private readonly TextBlock filterText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button clear = new() { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button delete = new() { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel report = new();

    /// <summary>A run in the table.</summary>
    public sealed class Row : INotifyPropertyChanged
    {
        private readonly BenchmarkStore store;
        private bool compare;
        public BenchmarkResult R { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<Row>? CompareChanged;

        public Row(BenchmarkResult r, BenchmarkStore store) { R = r; this.store = store; }

        public bool Compare
        {
            get => compare;
            set { if (compare == value) return; compare = value; PropertyChanged?.Invoke(this, new(nameof(Compare))); CompareChanged?.Invoke(this); }
        }

        public string Name
        {
            get => R.Name;
            set
            {
                value = value.Trim();
                if (value.Length == 0 || value == R.Name) return;
                R.Name = value;
                store.Save();
                PropertyChanged?.Invoke(this, new(nameof(Name)));
            }
        }

        public string Game => R.Game;
        public string Date => R.Started.ToString("g");
        public string Length => LengthText(R);
        public string Resolution => R.Resolution;
        public string Avg => R.AvgFps.ToString("0");
        public string Low1 => R.Low1Fps.ToString("0");
        public string Low01 => R.Low01Fps.ToString("0");
    }

    public BenchmarksWindow(GamingService service)
    {
        this.service = service;
        Title = T("Benchmarks");
        Width = 1040;
        Height = 760;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        foreach (var r in service.Benchmarks.Runs) rows.Add(Hook(new Row(r, service.Benchmarks)));
        view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = o => o is Row r && Visible(r);

        // the table
        grid.ItemsSource = view;
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = grid.CanUserDeleteRows = false;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        grid.BorderThickness = new Thickness(0);
        grid.Background = Brushes.Transparent;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        grid.SetResourceReference(DataGrid.CellStyleProperty, "EventGridCell");
        grid.SetResourceReference(DataGrid.ColumnHeaderStyleProperty, "EventGridColumnHeader");
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("Compare"), CellTemplate = CheckTemplate(), Width = 70 });
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("Name (click to rename)"), CellTemplate = NameTemplate(), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(TextColumn(T("Game"), nameof(Row.Game), 1.4));
        grid.Columns.Add(TextColumn(T("Date"), nameof(Row.Date), 1.2));
        grid.Columns.Add(TextColumn(T("Length"), nameof(Row.Length), 0, 90));
        grid.Columns.Add(TextColumn(T("Resolution"), nameof(Row.Resolution), 0, 110));
        grid.Columns.Add(TextColumn(T("Average FPS"), nameof(Row.Avg), 0, 100));
        grid.Columns.Add(TextColumn(T("1% low"), nameof(Row.Low1), 0, 72));
        grid.Columns.Add(TextColumn(T("0.1% low"), nameof(Row.Low01), 0, 80));
        // a click on a row ticks it (and again unticks it), except on its name (that's for renaming) or the box itself
        grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            DependencyObject? d = e.OriginalSource as DependencyObject;
            DataGridCell? cell = null;
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is TextBox or CheckBox) return;
                if (d is DataGridCell c) { cell = c; break; }
            }
            if (cell?.DataContext is Row r) r.Compare = !r.Compare;
        };

        clear.Content = T("Untick all");
        clear.Click += (_, _) => { foreach (var r in ticked.ToList()) r.Compare = false; };
        delete.Content = T("Delete ticked…");
        delete.Click += (_, _) => DeleteTicked();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(clear);
        buttons.Children.Add(delete);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        filterText.SetResourceReference(StyleProperty, "SecondaryText");
        top.Children.Add(filterText);

        var tableCard = Card(grid);
        var reportCard = Card(new ScrollViewer { Content = report, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });

        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star), MinHeight = 160 });
        Grid.SetRow(tableCard, 1);
        Grid.SetRow(reportCard, 2);
        layout.Children.Add(top);
        layout.Children.Add(tableCard);
        layout.Children.Add(reportCard);
        Content = layout;

        service.BenchmarkFinished += Added;
        Closed += (_, _) => service.BenchmarkFinished -= Added;
        Refresh();
    }

    /// <summary>Ticks this run (e.g. the one just finished) so its report shows.</summary>
    public void Show(BenchmarkResult r)
    {
        var row = rows.FirstOrDefault(x => x.R == r);
        if (row == null) return;
        foreach (var other in ticked.ToList()) other.Compare = false;
        row.Compare = true;
    }

    private Row Hook(Row r)
    {
        r.CompareChanged += row =>
        {
            if (row.Compare) { if (!ticked.Contains(row)) ticked.Add(row); }
            else ticked.Remove(row);
            Refresh();
        };
        return r;
    }

    private void Added(BenchmarkResult r)
    {
        rows.Insert(0, Hook(new Row(r, service.Benchmarks)));
        Refresh();
    }

    /// <summary>Nothing ticked: every run. Otherwise the ticked ones and those comparable with the first.</summary>
    private bool Visible(Row r) => ticked.Count == 0 || ticked.Contains(r) || r.R.ComparableWith(ticked[0].R);

    private void Refresh()
    {
        view.Refresh();
        clear.IsEnabled = delete.IsEnabled = ticked.Count > 0;
        filterText.Text = rows.Count == 0
            ? T("No benchmarks yet. Start one from the Gaming tab, or with its shortcut while playing.")
            : ticked.Count == 0
                ? T("Click a run to see its report. Once one is ticked, only the runs it can be compared with are listed (the same game and length): click more of them to compare.")
                : F("Showing the runs that can be compared with {0} ({1}, {2}).", ticked[0].R.Name, ticked[0].R.Game, LengthText(ticked[0].R));
        BuildReport();
    }

    private void DeleteTicked()
    {
        if (ticked.Count == 0) return;
        if (MessageBox.Show(this, P(ticked.Count, "Delete {0} benchmark?", "Delete {0} benchmarks?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var r in ticked.ToList())
        {
            service.Benchmarks.Runs.Remove(r.R);
            rows.Remove(r);
        }
        ticked.Clear();
        service.Benchmarks.Save();
        service.NotifyChanged();
        Refresh();
    }

    // ---- the report / comparison ----

    private sealed record Metric(string Label, Func<BenchmarkResult, double> Value, string Format, bool? HigherIsBetter);

    private static IEnumerable<Metric> Metrics() => new[]
    {
        new Metric(T("Average FPS"), r => r.AvgFps, "0.0", true),
        new Metric(T("1% low FPS"), r => r.Low1Fps, "0.0", true),
        new Metric(T("0.1% low FPS"), r => r.Low01Fps, "0.0", true),
        new Metric(T("Slowest second (FPS)"), r => r.MinFps, "0", true),
        new Metric(T("Fastest second (FPS)"), r => r.MaxFps, "0", true),
        new Metric(T("Average frame time (ms)"), r => r.AvgFrametimeMs, "0.00", false),
        new Metric(T("Worst frame time (ms)"), r => r.MaxFrametimeMs, "0.0", false),
        new Metric(T("GPU load (%)"), r => r.GpuPercent, "0", null),
        new Metric(T("GPU temperature, average (°C)"), r => r.GpuTempC, "0", false),
        new Metric(T("GPU temperature, highest (°C)"), r => r.GpuTempMaxC, "0", false),
        new Metric(T("GPU clock (MHz)"), r => r.GpuClockMHz, "0", null),
        new Metric(T("GPU power (W)"), r => r.GpuPowerW, "0", null),
        new Metric(T("Video memory, highest (GB)"), r => r.VramMaxMB / 1024, "0.0", null),
        new Metric(T("CPU load (%)"), r => r.CpuPercent, "0", null),
        new Metric(T("Game's CPU (%)"), r => r.GameCpuPercent, "0", null),
        new Metric(T("Memory, highest (GB)"), r => r.RamMaxMB / 1024, "0.0", null),
        new Metric(T("Frames"), r => r.Frames, "0", null),
    };

    private void BuildReport()
    {
        report.Children.Clear();
        if (ticked.Count == 0)
        {
            report.Children.Add(Secondary(T("Click a run above to see its report.")));
            return;
        }
        var runs = ticked.Select(t => t.R).ToList();
        report.Children.Add(new TextBlock
        {
            Text = runs.Count == 1 ? F("Report: {0}", runs[0].Name) : P(runs.Count, "Comparing {0} run", "Comparing {0} runs"),
            FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8),
        });

        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < runs.Count; i++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 130 });
        int row = 0;
        void AddRow(string label, Func<int, FrameworkElement> cell, bool header = false)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 2, 18, 2), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
            if (!header) l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(l, row);
            table.Children.Add(l);
            for (int i = 0; i < runs.Count; i++)
            {
                var c = cell(i);
                c.Margin = new Thickness(0, 2, 16, 2);
                Grid.SetRow(c, row);
                Grid.SetColumn(c, i + 1);
                table.Children.Add(c);
            }
            row++;
        }
        AddRow("", i =>
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Palette[i % Palette.Length]), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = runs[i].Name, FontWeight = FontWeights.SemiBold, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = runs[i].Name });
            return p;
        }, header: true);
        AddRow(T("Date"), i => new TextBlock { Text = runs[i].Started.ToString("g") });
        AddRow(T("Length"), i => new TextBlock { Text = LengthText(runs[i]) });
        AddRow(T("Resolution"), i => new TextBlock { Text = runs[i].Resolution });
        AddRow(T("Graphics card"), i => new TextBlock { Text = runs[i].Gpu, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = runs[i].Gpu });

        foreach (var m in Metrics())
        {
            double baseValue = m.Value(runs[0]);
            AddRow(m.Label, i =>
            {
                double v = m.Value(runs[i]);
                var p = new StackPanel { Orientation = Orientation.Horizontal };
                p.Children.Add(new TextBlock { Text = double.IsFinite(v) ? v.ToString(m.Format) : "–", FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal });
                // how it differs from the first ticked run: green when better, red when worse
                if (i > 0 && double.IsFinite(v) && double.IsFinite(baseValue) && baseValue != 0)
                {
                    double pct = (v - baseValue) / Math.Abs(baseValue) * 100;
                    if (Math.Abs(pct) >= 0.05)
                    {
                        var d = new TextBlock { Text = $"  {(pct > 0 ? "+" : "")}{pct:0.0}%", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                        bool better = m.HigherIsBetter == true ? pct > 0 : m.HigherIsBetter == false && pct < 0;
                        bool worse = m.HigherIsBetter == true ? pct < 0 : m.HigherIsBetter == false && pct > 0;
                        d.SetResourceReference(TextBlock.ForegroundProperty, better ? "SuccessBrush" : worse ? "DangerBrush" : "TextSecondaryBrush");
                        p.Children.Add(d);
                    }
                }
                return p;
            });
        }
        report.Children.Add(table);

        // frame rate over the run
        var graph = new HistoryGraph { Height = 190, Margin = new Thickness(0, 14, 0, 0) };
        int longest = runs.Max(r => r.FpsPerSecond.Length);
        graph.Show(runs.Select((r, i) => new HistoryGraph.Series(r.Name, Palette[i % Palette.Length],
                r.FpsPerSecond.Concat(Enumerable.Repeat(double.NaN, longest - r.FpsPerSecond.Length)).ToArray())),
            "0:00", Clock(TimeSpan.FromSeconds(longest)));
        report.Children.Add(Secondary(T("Frame rate, second by second")));
        report.Children.Add(graph);
    }

    // ---- pieces ----

    private static string LengthText(BenchmarkResult r) =>
        r.Timed ? F("{0} (set)", Clock(TimeSpan.FromSeconds(r.PlannedSeconds))) : Clock(TimeSpan.FromSeconds(r.Seconds));

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>A column sharing the spare width (stars), or of a set width in pixels (stars 0).</summary>
    private static DataGridTextColumn TextColumn(string header, string path, double stars, double pixels = 0) =>
        new() { Header = header, Binding = new Binding(path), IsReadOnly = true,
            Width = stars > 0 ? new DataGridLength(stars, DataGridLengthUnitType.Star) : new DataGridLength(pixels) };

    private static DataTemplate CheckTemplate()
    {
        var f = new FrameworkElementFactory(typeof(CheckBox));
        f.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Row.Compare)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        f.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        return new DataTemplate { VisualTree = f };
    }

    private static DataTemplate NameTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBox));
        f.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Name)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        f.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        f.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
        f.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        f.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        f.SetValue(TextBox.PaddingProperty, new Thickness(0));
        f.AddHandler(KeyDownEvent, new System.Windows.Input.KeyEventHandler((s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && s is TextBox tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }));
        return new DataTemplate { VisualTree = f };
    }

    private static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }
}
