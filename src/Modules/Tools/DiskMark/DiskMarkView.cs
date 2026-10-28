using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Shared;
using DaisysApp.Shared.Charts;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>
/// DiskMark (Tools tab): the drives on the left; for the one picked, its last run as CrystalDiskMark shows it (a row per
/// test, read and write), live while it runs (a speed gauge and the test file lighting up where each request lands),
/// its history over time, and every drive's last result side by side. The options along the top choose how long and
/// how hard a run is.
/// </summary>
internal sealed class DiskMarkView : DockPanel
{
    private readonly DiskMarkSettings settings;
    private readonly Runner runner;
    private readonly Button start;
    private readonly TextBlock estimate = Ui.Secondary();
    private readonly ListBox driveList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock driveTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock driveDetail = Ui.Secondary();
    private readonly TextBlock empty = Ui.Secondary(T("No runs on this drive yet"));
    private readonly Border liveCard, resultsCard, historyCard, compareCard;
    private readonly Gauge gauge = new() { Width = 250, Height = 210 };
    private readonly FileMap map = new() { Height = 120, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock stepText = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock stepDetail = Ui.Secondary();
    private readonly TextBlock timeLeft = Ui.Secondary();
    private readonly ProgressBar stepBar = Ui.Bar(4), totalBar = Ui.Bar(6);
    private readonly TextBlock resultsHead = Ui.Header(T("Results"));
    private readonly TextBlock resultsDetail = Ui.Secondary(wrap: true);
    private readonly Grid table = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock errorText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox historyTest = new() { MinWidth = 150 };
    private readonly LineGraph graph = new() { Height = 220, Detailed = true, TimeSpans = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DataGrid runsGrid = new();
    private Button deleteRun = null!;
    private readonly CompareChart compare = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly ComboBox compareTest = new() { MinWidth = 150 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Queue<(long Tick, long Bytes)> samples = new();
    private readonly List<(Border Cell, BenchResult Result, IoKind Kind)> cells = [];
    private List<DriveItem> drives = [];
    private BenchRun? picked; // a run chosen in the history; null = the drive's latest
    private Bench.Live? sampled;
    private Bench.Worker[]? sampledWorkers;
    private double liveRate, liveIops;
    private bool drawing;
    private ComboBox? profileBox;

    public DiskMarkView(DiskMarkSettings settings, Runner runner)
    {
        this.settings = settings;
        this.runner = runner;

        // ---- the options along the top
        start = Ui.IconButton("", T("Start"), T("Test the drive picked on the left"), (_, _) => StartOrStop(), accent: true);
        start.MinWidth = 96;
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(start);
        bar.Children.Add(Choice(T("Profile"), Profiles.All.Select(p => (Profiles.Title(p), (object)p)), settings.Profile, v => settings.Profile = (string)v));
        bar.Children.Add(Choice(T("Test"), [(T("Read & Write"), BenchMode.ReadWrite), (T("Read"), BenchMode.Read), (T("Write"), BenchMode.Write), (T("Mix 70/30"), BenchMode.Mix)], settings.Mode, v => settings.Mode = (BenchMode)v));
        bar.Children.Add(Choice(T("Runs"), Enumerable.Range(1, 9).Select(n => (n.ToString(), (object)n)), settings.Count, v => settings.Count = (int)v, T("Times each test runs; the best is kept")));
        bar.Children.Add(Choice(T("Size"), new[] { 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 }.Select(m => (MiB(m), (object)m)), settings.SizeMiB, v => settings.SizeMiB = (int)v, T("The test file's size")));
        bar.Children.Add(Choice(T("Time"), new[] { 1, 2, 3, 5, 10, 20, 30, 60 }.Select(s => (F("{0} s", s), (object)s)), settings.Seconds, v => settings.Seconds = (int)v, T("How long each run measures")));
        bar.Children.Add(Choice(T("Interval"), new[] { 0, 1, 3, 5, 10, 30, 60 }.Select(s => (F("{0} s", s), (object)s)), settings.IntervalSeconds, v => settings.IntervalSeconds = (int)v, T("The pause between runs")));
        bar.Children.Add(Choice(T("Data"), [(T("Random"), false), ("0x00", true)], settings.Zeros, v => settings.Zeros = (bool)v, T("What's written")));
        estimate.VerticalAlignment = VerticalAlignment.Center;
        estimate.Margin = new Thickness(4, 0, 12, 6);
        bar.Children.Add(estimate);
        var custom = Ui.IconButton("", T("Tests…"), T("The Custom profile's tests"), (_, _) => EditCustom());
        custom.Margin = new Thickness(0, 0, 8, 6);
        System.Windows.Automation.AutomationProperties.SetName(custom, ((TextBlock)((StackPanel)custom.Content).Children[1]).Text);
        bar.Children.Add(custom);
        var copy = Ui.IconButton("", T("Copy"), T("Copy the results shown as text"), (_, _) => Copy());
        copy.Margin = new Thickness(0, 0, 8, 6);
        System.Windows.Automation.AutomationProperties.SetName(copy, ((TextBlock)((StackPanel)copy.Content).Children[1]).Text);
        bar.Children.Add(copy);
        var schedule = Ui.IconButton("", T("Schedule…"), T("Run tests later or regularly"), (_, _) => DiskMarkApplet.EditSchedule(this, settings, runner));
        schedule.Margin = new Thickness(0, 0, 8, 6);
        System.Windows.Automation.AutomationProperties.SetName(schedule, ((TextBlock)((StackPanel)schedule.Content).Children[1]).Text);
        bar.Children.Add(schedule);
        SetDock(bar, Dock.Top);
        Children.Add(bar);

        // ---- the drives
        driveList.ItemTemplate = DriveTemplate();
        driveList.SelectionChanged += (_, _) =>
        {
            if (drawing || driveList.SelectedItem is not DriveItem d) return;
            settings.Drive = d.Root;
            settings.Save();
            picked = null;
            Draw();
        };
        var drivesHead = Ui.Header(T("Drives"));
        var refresh = Ui.IconButton("", "", T("Look for drives again"), (_, _) => LoadDrives());
        System.Windows.Automation.AutomationProperties.SetName(refresh, T("Look for drives again"));
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        refresh.Padding = new Thickness(6, 2, 6, 2);
        var drivesTop = new Grid { Margin = new Thickness(0, 0, 0, 6), Children = { drivesHead, refresh } };
        var drivesPanel = new DockPanel();
        DockPanel.SetDock(drivesTop, Dock.Top);
        drivesPanel.Children.Add(drivesTop);
        drivesPanel.Children.Add(driveList);
        var drivesCard = Ui.Card(drivesPanel);
        drivesCard.Width = 280;
        drivesCard.Margin = new Thickness(0, 0, 12, 0);
        SetDock(drivesCard, Dock.Left);
        Children.Add(drivesCard);

        // ---- live
        var gaugeBox = new Border { Child = gauge, Margin = new Thickness(0, 0, 16, 0) };
        stepBar.Margin = new Thickness(0, 8, 0, 0);
        totalBar.Margin = new Thickness(0, 12, 0, 0);
        var mapBox = new StackPanel { Children = { stepText, stepDetail, map, stepBar, totalBar, timeLeft } };
        timeLeft.Margin = new Thickness(0, 4, 0, 0);
        var live = new DockPanel();
        DockPanel.SetDock(gaugeBox, Dock.Left);
        live.Children.Add(gaugeBox);
        live.Children.Add(mapBox);
        liveCard = Ui.Card(live, new Thickness(0, 0, 0, 12));

        // ---- results
        resultsDetail.Margin = new Thickness(0, 2, 0, 0);
        errorText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        resultsCard = Ui.Card(new StackPanel { Children = { resultsHead, resultsDetail, table, errorText } }, new Thickness(0, 0, 0, 12));

        // ---- history
        historyTest.SelectionChanged += (_, _) =>
        {
            if (drawing || historyTest.SelectedItem is not string s) return;
            settings.HistoryTest = s;
            settings.Save();
            DrawHistory();
            DrawCompare();
        };
        compareTest.SelectionChanged += (_, _) =>
        {
            if (drawing || compareTest.SelectedItem is not string s) return;
            settings.HistoryTest = s;
            settings.Save();
            DrawHistory();
            DrawCompare();
        };
        var historyHead = new DockPanel { Children = { Ui.Header(T("History")) } };
        historyTest.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(historyTest, Dock.Right);
        historyHead.Children.Insert(0, historyTest);
        BuildRunsGrid();
        historyCard = Ui.Card(new StackPanel { Children = { historyHead, graph, runsGrid, deleteRun } }, new Thickness(0, 0, 0, 12));
        var compareHead = new DockPanel { Children = { Ui.Header(T("All Drives")) } };
        compareTest.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(compareTest, Dock.Right);
        compareHead.Children.Insert(0, compareTest);
        compareCard = Ui.Card(new StackPanel { Children = { compareHead, compare } });

        empty.FontSize = 14;
        empty.Margin = new Thickness(2, 8, 0, 12);
        driveDetail.Margin = new Thickness(0, 0, 0, 12);
        var page = new StackPanel { Children = { driveTitle, driveDetail, empty, liveCard, resultsCard, historyCard, compareCard } };
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(scroll);

        timer.Tick += (_, _) => Animate();
        runner.Changed += OnChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) { LoadDrives(); Draw(); } else timer.Stop(); };
        LoadDrives();
        Draw();
    }

    /// <summary>Draws again (the unit or the bars' scale changed in Settings).</summary>
    public void Refresh() { if (IsVisible) Draw(); }

    public void Shutdown()
    {
        timer.Stop();
        runner.Changed -= OnChanged;
    }

    // ---------------------------------------------------------------- the options

    private FrameworkElement Choice(string label, IEnumerable<(string Text, object Value)> items, object current, Action<object> set, string? tip = null)
    {
        var box = new ComboBox { MinWidth = 70, ToolTip = tip };
        if (label == T("Profile")) profileBox = box;
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, current)) ?? box.Items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not ComboBoxItem { Tag: { } v }) return;
            set(v);
            settings.Save();
            Draw();
        };
        var name = Ui.Secondary(label);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 0, 6, 0);
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 6), Children = { name, box } };
    }

    private static string MiB(int m) => m >= 1024 ? F("{0} GiB", m / 1024) : F("{0} MiB", m);

    private void EditCustom()
    {
        DiskMarkApplet.EditTests(this, settings);
        if (profileBox != null) profileBox.SelectedItem = profileBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, settings.Profile));
        Draw();
    }

    // ---------------------------------------------------------------- the drives

    private void LoadDrives()
    {
        var keep = settings.Drive;
        drives = Drives.List();
        drawing = true;
        driveList.ItemsSource = drives;
        driveList.SelectedItem = drives.FirstOrDefault(d => d.Root.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();
        drawing = false;
        if (driveList.SelectedItem is DriveItem d && !d.Root.Equals(settings.Drive, StringComparison.OrdinalIgnoreCase)) { settings.Drive = d.Root; settings.Save(); }
    }

    private DriveItem? Selected => driveList.SelectedItem as DriveItem;

    private static DataTemplate DriveTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 6, 2, 6));
        var top = new FrameworkElementFactory(typeof(DockPanel));
        var letter = new FrameworkElementFactory(typeof(TextBlock));
        letter.SetBinding(TextBlock.TextProperty, new Binding(nameof(DriveItem.Letter)));
        letter.SetValue(TextBlock.FontSizeProperty, 16.0);
        letter.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        letter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        top.AppendChild(letter);
        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetBinding(TextBlock.TextProperty, new Binding(nameof(DriveItem.Kind)));
        kind.SetValue(DockPanel.DockProperty, Dock.Right);
        kind.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        kind.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        kind.SetValue(TextBlock.FontSizeProperty, 11.5);
        top.AppendChild(kind);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(DriveItem.Label)));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        top.AppendChild(label);
        root.AppendChild(top);
        var model = new FrameworkElementFactory(typeof(TextBlock));
        model.SetBinding(TextBlock.TextProperty, new Binding(nameof(DriveItem.Model)));
        model.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        model.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        model.SetValue(TextBlock.FontSizeProperty, 11.5);
        root.AppendChild(model);
        var track = new FrameworkElementFactory(typeof(ProgressBar));
        track.SetValue(FrameworkElement.HeightProperty, 4.0);
        track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 2));
        track.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        track.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        track.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        track.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(DriveItem.Used)) { Mode = BindingMode.OneWay });
        root.AppendChild(track);
        var free = new FrameworkElementFactory(typeof(TextBlock));
        free.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            Converter = new FreeText(),
            Bindings = { new Binding(nameof(DriveItem.Free)), new Binding(nameof(DriveItem.Total)), new Binding(nameof(DriveItem.Format)) },
        });
        free.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        free.SetValue(TextBlock.FontSizeProperty, 11.5);
        root.AppendChild(free);
        return new DataTemplate { VisualTree = root };
    }

    private sealed class FreeText : IMultiValueConverter
    {
        public object Convert(object[] v, Type t, object p, System.Globalization.CultureInfo c) =>
            v is [long free, long total, string format] ? F("{0} free of {1} · {2}", Sizes.Bytes(free), Sizes.Bytes(total), format) : "";
        public object[] ConvertBack(object v, Type[] t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- running

    private void StartOrStop()
    {
        if (runner.Running) { runner.Cancel(); return; }
        if (Selected is not { } d) return;
        picked = null;
        _ = runner.RunAsync([d], scheduled: false);
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Draw(); }, DispatcherPriority.Background);

    /// <summary>Speeds as the settings say: GB/s or MB/s.</summary>
    private (string Number, string Unit) Speed(double bytesPerSecond)
    {
        if (settings.Unit == "MB/s") return ((bytesPerSecond / 1e6).ToString(bytesPerSecond >= 1e8 ? "#,0.0" : "0.00"), "MB/s");
        double g = bytesPerSecond / 1e9;
        return (g.ToString(g >= 10 ? "0.0" : g >= 1 ? "0.00" : g >= 0.01 ? "0.000" : "0.0000"), "GB/s");
    }

    private string SpeedText(double mbps) { var (n, u) = Speed(mbps * 1e6); return $"{n} {u}"; }

    private void Animate()
    {
        if (!runner.Running) { timer.Stop(); return; }
        var live = runner.Live;
        if (!ReferenceEquals(sampled, live) || runner.Now != Runner.Step.Measuring || !ReferenceEquals(sampledWorkers, live.Workers))
        {
            samples.Clear();
            sampled = live;
            sampledWorkers = live.Workers;
            liveRate = liveIops = 0;
        }
        long now = Stopwatch.GetTimestamp();
        if (runner.Now == Runner.Step.Measuring)
        {
            long bytes = live.Bytes;
            samples.Enqueue((now, bytes));
            while (samples.Count > 2 && (now - samples.Peek().Tick) > Stopwatch.Frequency / 2) samples.Dequeue();
            if (samples.Count >= 2)
            {
                var first = samples.Peek();
                double secs = (double)(now - first.Tick) / Stopwatch.Frequency;
                if (secs > 0.05)
                {
                    liveRate = (bytes - first.Bytes) / secs;
                    double block = (runner.Test?.BlockKiB ?? 4) * 1024.0;
                    liveIops = liveRate / block;
                }
            }
            gauge.Target = liveRate;
            gauge.Iops = liveIops;
            gauge.Idle = false;
        }
        else if (runner.Now == Runner.Step.Preparing)
        {
            gauge.Idle = true;
        }
        else gauge.Target = 0;
        gauge.Kind = runner.Now == Runner.Step.Preparing ? IoKind.Write : runner.Kind;
        gauge.Step();
        map.Kind = gauge.Kind;
        map.Preparing = runner.Now == Runner.Step.Preparing;
        map.Step(live, runner.Prepared);

        // the step's own bar and the time left
        double stepDone = runner.Now == Runner.Step.Preparing ? runner.Prepared
            : runner.StepSeconds > 0 ? Math.Clamp((DateTime.Now - runner.StepStarted).TotalSeconds / runner.StepSeconds, 0, 1) : 0;
        stepBar.Value = stepDone * 100;
        totalBar.Value = runner.Done * 100;
        if (runner.Now != Runner.Step.Preparing)
        {
            var left = settings.Estimate() * (1 - runner.Done) - TimeSpan.FromSeconds(runner.StepSeconds * stepDone);
            timeLeft.Text = F("About {0} left", Sizes.Duration(left < TimeSpan.Zero ? TimeSpan.Zero : left))
                + (runner.DrivesLeft > 0 ? " · " + P(runner.DrivesLeft, "{0} more drive", "{0} more drives") : "");
        }
        else timeLeft.Text = "";

        // the cell being measured shows the live speed and pulses
        double pulse = 0.55 + 0.45 * Math.Sin(Environment.TickCount64 / 180.0);
        foreach (var (cell, result, kind) in cells)
        {
            bool now2 = runner.Now == Runner.Step.Measuring && runner.Test == result.Test && runner.Kind == kind;
            bool next = runner.Now == Runner.Step.Waiting && runner.Test == result.Test && runner.Kind == kind;
            cell.BorderBrush = now2 || next ? Ink.Of(this, kind) : null;
            cell.BorderThickness = new Thickness(now2 || next ? 2 : 0);
            cell.Opacity = now2 ? 1 : next ? 0.6 + 0.4 * pulse : 1;
            if (now2 && cell.Tag is TextBlock big && result.Of(kind) == null) { big.Text = liveRate > 0 ? Speed(liveRate).Number : "…"; big.Opacity = 0.75; }
        }
    }

    // ---------------------------------------------------------------- drawing

    private void Draw()
    {
        drawing = true;
        try { DrawPage(); }
        finally { drawing = false; }
    }

    private void DrawPage()
    {
        bool running = runner.Running;
        SetButton(running);
        var est = settings.Estimate();
        estimate.Text = F("≈ {0} a drive", Sizes.Duration(est + TimeSpan.FromSeconds(settings.SizeMiB / 500.0)));

        var drive = Selected;
        driveTitle.Text = drive == null ? T("No drives") : drive.Label.Length > 0 ? $"{drive.Letter}  {drive.Label}" : drive.Letter;
        driveDetail.Text = drive == null ? "" : string.Join(" · ", new[] { drive.Model, drive.Kind, Sizes.Bytes(drive.Total) }.Where(s => s.Length > 0));
        var all = drive == null ? [] : runner.RunsOf(drive.Root);
        bool here = running && drive != null && drive.Root.Equals(runner.Drive, StringComparison.OrdinalIgnoreCase);
        if (picked != null && !all.Contains(picked)) picked = null;
        var run = here ? runner.Current : picked ?? all.LastOrDefault();

        liveCard.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        if (here)
        {
            if (!timer.IsEnabled) { map.Reset(); timer.Start(); }
            var t = runner.Test;
            (stepText.Text, stepDetail.Text) = runner.Now switch
            {
                Runner.Step.Preparing => (T("Writing the test file"), F("{0} · {1}", MiB(settings.SizeMiB), Sizes.Percent(runner.Prepared))),
                Runner.Step.Waiting => (F("Next: {0} {1}", t?.Name ?? "", KindName(runner.Kind)), F("Run {0} of {1} · pausing", runner.Pass, runner.Count)),
                _ => ($"{t?.Name} · {KindName(runner.Kind)}", F("Run {0} of {1}", runner.Pass, runner.Count) + " · " + Describe(t)),
            };
            stepText.Foreground = Ink.Of(this, runner.Now == Runner.Step.Preparing ? IoKind.Write : runner.Kind);
            gauge.Caption = runner.Now == Runner.Step.Measuring && t != null ? t.Kind : "";
        }
        else if (!running) timer.Stop();
        if (running && !here) { gauge.Idle = true; }

        empty.Visibility = run == null && !here ? Visibility.Visible : Visibility.Collapsed;
        resultsCard.Visibility = run != null ? Visibility.Visible : Visibility.Collapsed;
        if (run != null) DrawResults(run, here);
        DrawHistory();
        DrawCompare();
        if (here) Animate();
    }

    private void SetButton(bool running)
    {
        if (start.Content is StackPanel { Children: [TextBlock icon, TextBlock text] })
        {
            icon.Text = running ? "" : "";
            text.Text = running ? T("Stop") : T("Start");
        }
        start.ToolTip = running ? T("Stop the test") : T("Test the drive picked on the left");
        System.Windows.Automation.AutomationProperties.SetName(start, running ? T("Stop") : T("Start"));
    }

    private static string KindName(IoKind k) => k switch { IoKind.Read => T("Read"), IoKind.Write => T("Write"), _ => T("Mix") };

    private static string Describe(TestDef? t) => t == null ? ""
        : (t.Pattern == Pattern.Seq ? T("sequential") : T("random")) + " " + F("{0} blocks", t.Block + "iB") + " · "
          + P(t.Queues, "{0} request waiting", "{0} requests waiting") + " · " + P(t.Threads, "{0} thread", "{0} threads");

    private static IoKind[] KindsOf(BenchMode m) => m switch
    {
        BenchMode.Read => [IoKind.Read],
        BenchMode.Write => [IoKind.Write],
        BenchMode.Mix => [IoKind.Mix],
        _ => [IoKind.Read, IoKind.Write],
    };

    /// <summary>CrystalDiskMark's table: a row per test, a column for read and for write, each cell's bar its share of the run's best.</summary>
    private void DrawResults(BenchRun run, bool live)
    {
        resultsHead.Text = live ? T("Results (running)") : run == runner.RunsOf(run.Drive).LastOrDefault() ? T("Last Run") : T("Run");
        resultsDetail.Text = string.Join(" · ", new[]
        {
            run.Time.ToString("g"), Profiles.Title(run.Profile), $"{run.Count} × {MiB(run.SizeMiB)}", F("{0} s", run.Seconds),
            run.Zeros ? "0x00" : T("random data"), run.Scheduled ? T("scheduled") : "", run.Cancelled ? T("stopped") : "",
            !live && run.TookSeconds > 0 ? F("took {0}", Sizes.Duration(TimeSpan.FromSeconds(run.TookSeconds))) : "",
        }.Where(s => s.Length > 0));
        errorText.Text = run.Error ?? "";
        errorText.Visibility = run.Error != null ? Visibility.Visible : Visibility.Collapsed;

        var kinds = KindsOf(run.Mode);
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();
        cells.Clear();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        foreach (var _ in kinds) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int k = 0; k < kinds.Length; k++)
        {
            var h = new TextBlock { Text = $"{KindName(kinds[k])} ({settings.Unit})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 4), Foreground = Ink.Of(this, kinds[k]) };
            Grid.SetColumn(h, k + 1);
            table.Children.Add(h);
        }
        double best = run.Results.SelectMany(r => kinds.Select(k => r.Of(k)?.MBps ?? 0)).DefaultIfEmpty(0).Max();
        for (int i = 0; i < run.Results.Count; i++)
        {
            var r = run.Results[i];
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new Border
            {
                CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(10, 6, 10, 6),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = r.Test.Kind, FontSize = 16, FontWeight = FontWeights.SemiBold },
                        Ui.Secondary(r.Test.Depth),
                    },
                },
            };
            name.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
            Grid.SetRow(name, i + 1);
            table.Children.Add(name);
            for (int k = 0; k < kinds.Length; k++)
            {
                var cell = Cell(r, kinds[k], best);
                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, k + 1);
                table.Children.Add(cell);
            }
        }
    }

    private Border Cell(BenchResult r, IoKind kind, double best)
    {
        var s = r.Of(kind);
        var color = Ink.ColorOf(this, kind);
        double share = s == null || best <= 0 ? 0 : settings.LogScale
            ? Math.Clamp(Math.Log10(Math.Max(s.MBps, 0.1) / 0.1) / Math.Log10(Math.Max(best, 1) / 0.1), 0.02, 1)
            : Math.Clamp(s.MBps / best, 0.02, 1);
        var fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(Color.FromArgb(40, color.R, color.G, color.B), Color.FromArgb(120, color.R, color.G, color.B), 0),
        };
        var big = new TextBlock { Text = s == null ? "—" : Speed(s.MBps * 1e6).Number, FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        if (s == null) big.Opacity = 0.35;
        var small = Ui.Secondary(s == null ? " " : F("{0:#,0} IOPS · {1} µs", s.Iops, s.LatencyUs.ToString(s.LatencyUs >= 100 ? "#,0" : "0.0")));
        small.HorizontalAlignment = HorizontalAlignment.Right;
        small.FontSize = 11.5;
        var text = new StackPanel { Margin = new Thickness(12, 6, 12, 6), Children = { big, small } };
        var grid = new Grid { Children = { fill, text } };
        var cell = new Border { Child = grid, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 3, 0, 3), Tag = big, ClipToBounds = true };
        cell.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        cell.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * share;
        if (s != null) cell.ToolTip = $"{SpeedText(s.MBps)} · {s.MBps:#,0.00} MB/s · {s.Iops:#,0} IOPS · {s.LatencyUs:#,0.0} µs";
        cells.Add((cell, r, kind));
        return cell;
    }

    // ---------------------------------------------------------------- history and every drive

    private sealed record RunRow(BenchRun Run, string When, string Profile, string Size, string Read, string Write, string Note);

    private void BuildRunsGrid()
    {
        runsGrid.AutoGenerateColumns = false;
        runsGrid.IsReadOnly = true;
        runsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        runsGrid.SelectionMode = DataGridSelectionMode.Single;
        runsGrid.CanUserSortColumns = false;
        runsGrid.MaxHeight = 260;
        runsGrid.Margin = new Thickness(0, 10, 0, 0);
        runsGrid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        DataGridTextColumn Col(string head, string path, double width) => new() { Header = head, Binding = new Binding(path), Width = width < 0 ? new DataGridLength(-width, DataGridLengthUnitType.Star) : new DataGridLength(width) };
        runsGrid.Columns.Add(Col(T("Time"), nameof(RunRow.When), 140));
        runsGrid.Columns.Add(Col(T("Profile"), nameof(RunRow.Profile), 120));
        runsGrid.Columns.Add(Col(T("Size"), nameof(RunRow.Size), 115));
        runsGrid.Columns.Add(Col(T("Read"), nameof(RunRow.Read), 100));
        runsGrid.Columns.Add(Col(T("Write"), nameof(RunRow.Write), 100));
        runsGrid.Columns.Add(Col("", nameof(RunRow.Note), -1));
        runsGrid.SelectionChanged += (_, _) =>
        {
            deleteRun.IsEnabled = runsGrid.SelectedItem != null;
            if (drawing || runsGrid.SelectedItem is not RunRow row) return;
            picked = row.Run;
            Draw();
        };
        var show = new MenuItem { Header = T("Show") };
        show.Click += (_, _) => { if (runsGrid.SelectedItem is RunRow row) { picked = row.Run; Draw(); } };
        runsGrid.ContextMenu = new ContextMenu { Items = { show } };
        deleteRun = ConfirmButton.Make(T("Delete"), () =>
        {
            if (runsGrid.SelectedItem is not RunRow row) return;
            if (picked == row.Run) picked = null;
            runner.Delete(row.Run);
        }, T("Delete the run picked above"));
        deleteRun.HorizontalAlignment = HorizontalAlignment.Left;
        deleteRun.Margin = new Thickness(0, 8, 0, 0);
    }

    /// <summary>Every test name in the runs given, the default four first.</summary>
    private static List<string> TestNames(IEnumerable<BenchRun> runs) =>
        Profiles.DefaultTests().Select(t => t.Name).Concat(runs.SelectMany(r => r.Results.Select(x => x.Test.Name))).Distinct()
            .Where(n => runs.Any(r => r.Results.Any(x => x.Test.Name == n))).ToList();

    private static void Fill(ComboBox box, List<string> names, string? current)
    {
        if (!box.Items.Cast<string>().SequenceEqual(names))
        {
            box.Items.Clear();
            foreach (var n in names) box.Items.Add(n);
        }
        box.SelectedItem = names.Contains(current ?? "") ? current : names.FirstOrDefault();
    }

    private void DrawHistory()
    {
        var drive = Selected;
        var all = drive == null ? [] : runner.RunsOf(drive.Root);
        historyCard.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0) return;
        var names = TestNames(all);
        bool was = drawing;
        drawing = true;
        Fill(historyTest, names, settings.HistoryTest);
        drawing = was;
        string test = historyTest.SelectedItem as string ?? "";

        Score? Of(BenchRun r, IoKind k) => r.Results.FirstOrDefault(x => x.Test.Name == test)?.Of(k);
        var series = new List<GraphSeries>();
        foreach (var k in new[] { IoKind.Read, IoKind.Write, IoKind.Mix })
        {
            var points = all.Select(r => (r.Time, V: Of(r, k)?.MBps ?? double.NaN)).Where(p => !double.IsNaN(p.V))
                .Select(p => (p.Time, settings.Unit == "MB/s" ? p.V : p.V / 1000)).ToList();
            if (points.Count == 0) continue;
            series.Add(new GraphSeries
            {
                Name = KindName(k), Points = points, Metric = new SpeedUnit(settings.Unit), Fill = k == IoKind.Read,
                Color = k == IoKind.Read ? "AccentBrush" : k == IoKind.Write ? Ink.Write : Ink.Mix,
            });
        }
        var from = all[0].Time;
        var to = all[^1].Time;
        if ((to - from).TotalHours < 1) { from = from.AddMinutes(-30); to = to.AddMinutes(30); }
        else { var pad = TimeSpan.FromTicks((to - from).Ticks / 20); from -= pad; to += pad; }
        graph.Show(series, from, to, null, TimeSpan.FromDays(3650));
        graph.Visibility = series.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var rows = all.AsEnumerable().Reverse().Select(r => new RunRow(r, r.Time.ToString("g"), Profiles.Title(r.Profile), $"{r.Count} × {MiB(r.SizeMiB)}",
            Of(r, IoKind.Read) is { } rd ? SpeedText(rd.MBps) : Of(r, IoKind.Mix) is { } mx ? SpeedText(mx.MBps) : "",
            Of(r, IoKind.Write) is { } wr ? SpeedText(wr.MBps) : "",
            string.Join(" · ", new[] { r.Scheduled ? T("scheduled") : "", r.Cancelled ? T("stopped") : "", r.Error ?? "" }.Where(s => s.Length > 0)))).ToList();
        drawing = true;
        runsGrid.ItemsSource = rows;
        runsGrid.SelectedItem = picked == null ? null : rows.FirstOrDefault(x => x.Run == picked);
        deleteRun.IsEnabled = runsGrid.SelectedItem != null;
        drawing = was;
    }

    private void DrawCompare()
    {
        var latest = drives.Select(d => (Drive: d, Run: runner.RunsOf(d.Root).LastOrDefault(r => r.Results.Any(x => x.Read != null || x.Write != null || x.Mix != null))))
            .Where(x => x.Run != null).ToList();
        compareCard.Visibility = latest.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (latest.Count < 2) return;
        var names = TestNames(latest.Select(x => x.Run!));
        bool was = drawing;
        drawing = true;
        Fill(compareTest, names, settings.HistoryTest);
        drawing = was;
        string test = compareTest.SelectedItem as string ?? "";
        compare.Show(latest.Select(x =>
        {
            var r = x.Run!.Results.FirstOrDefault(y => y.Test.Name == test);
            return new CompareChart.Row($"{x.Drive.Letter} {x.Drive.Label}".Trim(), x.Drive.Model, r?.Read?.MBps ?? r?.Mix?.MBps, r?.Write?.MBps);
        }).ToList(), v => SpeedText(v), settings.LogScale);
    }

    private sealed class SpeedUnit(string unit) : IGraphUnit
    {
        public string Unit => unit;
        public string Text(double value) => unit == "MB/s" ? $"{value:#,0.0} MB/s" : $"{value:0.000} GB/s";
    }

    // ---------------------------------------------------------------- copy

    private void Copy()
    {
        var drive = Selected;
        if (drive == null) return;
        var run = picked ?? runner.RunsOf(drive.Root).LastOrDefault();
        if (run == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{AppPaths.DisplayName} DiskMark {VersionText()}");
        sb.AppendLine($"{run.Drive.TrimEnd('\\')} {run.Label} · {run.Model} · {run.Kind}".Replace("  ", " "));
        sb.AppendLine($"{run.Time:g} · {Profiles.Title(run.Profile)} · {run.Count} × {MiB(run.SizeMiB)} · {run.Seconds} s · {(run.Zeros ? "0x00" : T("random data"))}");
        foreach (var k in KindsOf(run.Mode))
        {
            sb.AppendLine();
            sb.AppendLine($"[{KindName(k)}]");
            foreach (var r in run.Results)
                if (r.Of(k) is { } s)
                    sb.AppendLine($"  {r.Test.Name,-14} {s.MBps,10:0.00} MB/s [{s.Iops,10:0.0} IOPS] <{s.LatencyUs,10:0.00} us>");
        }
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }

    private static string VersionText() => typeof(DiskMarkView).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}

/// <summary>Every drive's last result for one test, as horizontal bars: read (or mixed) and write.</summary>
internal sealed class CompareChart : FrameworkElement
{
    public sealed record Row(string Name, string Model, double? Read, double? Write);

    private List<Row> rows = [];
    private Func<double, string> text = v => v.ToString("0");
    private bool log;
    private const double RowHeight = 46, NameWidth = 170;

    public void Show(List<Row> rows, Func<double, string> text, bool log)
    {
        this.rows = rows;
        this.text = text;
        this.log = log;
        Height = rows.Count * RowHeight + 4;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < NameWidth + 60 || rows.Count == 0) return;
        double max = rows.SelectMany(r => new[] { r.Read ?? 0, r.Write ?? 0 }).DefaultIfEmpty(1).Max();
        double barW = w - NameWidth - 110;
        var textBrush = Ink.Theme(this, "TextBrush");
        var secondary = Ink.Theme(this, "TextSecondaryBrush");
        var room = Ink.Theme(this, "RoomBrush");
        double Share(double v) => max <= 0 ? 0 : log ? Math.Clamp(Math.Log10(Math.Max(v, 0.1) / 0.1) / Math.Log10(Math.Max(max, 1) / 0.1), 0, 1) : v / max;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            double y = i * RowHeight + 2;
            dc.DrawText(Trim(Ink.Text(this, r.Name, 13, textBrush, bold: true)), new Point(0, y + 2));
            dc.DrawText(Trim(Ink.Text(this, r.Model, 11, secondary)), new Point(0, y + 21));
            Bar(r.Read, IoKind.Read, y + 3);
            Bar(r.Write, IoKind.Write, y + 23);

            void Bar(double? v, IoKind k, double top)
            {
                dc.DrawRoundedRectangle(room, null, new Rect(NameWidth, top, barW, 16), 4, 4);
                if (v is not double value) return;
                dc.DrawRoundedRectangle(Ink.Of(this, k), null, new Rect(NameWidth, top, Math.Max(3, barW * Share(value)), 16), 4, 4);
                dc.DrawText(Ink.Text(this, text(value), 11.5, textBrush), new Point(NameWidth + barW + 8, top));
            }
        }

        FormattedText Trim(FormattedText t)
        {
            t.MaxTextWidth = NameWidth - 10;
            t.MaxLineCount = 1;
            t.Trimming = TextTrimming.CharacterEllipsis;
            return t;
        }
    }
}
