using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DaisysApp.Applets.Support.Networking;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>
/// Support → Stress Tests: the tests to run (ticked by group) and how (length, repeat, logging, which GPU and drive),
/// Start / Stop; while it runs, the test going, its live number and graph, the 3D test's picture and the sensors; then
/// the run's scores and each test's numbers and problems, Export… and Compare…; and every run kept.
/// </summary>
internal sealed class StressView : ContentControl, IDisposable
{
    private readonly StressSettings settings;
    private readonly Runner runner;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Button start;
    private readonly TextBlock estimate = Ui.Secondary();
    private readonly Dictionary<string, System.Windows.Controls.Primitives.ToggleButton> chips = [];

    // the live card
    private readonly Border liveCard;
    private readonly TextBlock liveTitle = Ui.Header(""), liveStep = Ui.Secondary(), liveValue = new() { FontSize = 34, FontWeight = FontWeights.Light };
    private readonly TextBlock liveCount = Ui.Secondary(), liveSensors = Ui.Secondary(wrap: true), liveErrors = new() { FontWeight = FontWeights.SemiBold };
    private readonly ProgressBar testBar = Ui.Bar(6), runBar = Ui.Bar(6);
    private readonly LineGraph graph = new() { Height = 150, Detailed = true, TimeSpans = 3 };
    private readonly Image preview = new() { Stretch = Stretch.Uniform, MaxWidth = 480 };
    private WriteableBitmap? frame;
    private long frameShown = -1;

    // results and history
    private readonly StackPanel results = new();
    private readonly DataGrid history = Ui.Table(260);
    private StressRun? shown;

    public StressView(StressSettings settings, Runner runner)
    {
        this.settings = settings;
        this.runner = runner;
        var page = new StackPanel();

        // ---------------- the tests
        start = Ui.IconButton("", T("Start"), T("Run the ticked tests"), StartStop, accent: true);
        var compare = Ui.IconButton("", T("Compare…"), T("Compare two runs, or one with another PC's"), () => CompareWindow.Show(this, runner, settings));
        var testsPanel = new StackPanel();
        testsPanel.Children.Add(Ui.HeaderRow(Ui.Header(T("Stress Tests")), compare, start));
        var groups = new WrapPanel();
        foreach (var g in Catalog.Groups)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 22, 8) };
            col.Children.Add(Ui.Secondary(Catalog.GroupTitle(g)));
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var t in Catalog.Tests.Where(t => t.Group == g))
            {
                var chip = Ui.Chip(T(t.Name), settings.Tests.Contains(t.Id), T(t.Tip));
                chip.Margin = new Thickness(0, 0, 6, 4);
                Ui.OnToggle(chip, on =>
                {
                    settings.Tests.Remove(t.Id);
                    if (on) settings.Tests.Add(t.Id);
                    settings.Save();
                    UpdateEstimate();
                });
                chips[t.Id] = chip;
                row.Children.Add(chip);
            }
            col.Children.Add(row);
            groups.Children.Add(col);
        }
        testsPanel.Children.Add(groups);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        void Pick(string text, Func<TestInfo, bool> which, string tip)
        {
            var b = Ui.Button(text, () => { foreach (var t in Catalog.Tests) chips[t.Id].IsChecked = which(t); }, tip);
            b.Margin = new Thickness(0, 0, 6, 0);
            quick.Children.Add(b);
        }
        Pick(T("Benchmark"), t => t.Scored, T("Every test with a score"));
        Pick(T("CPU + Memory"), t => t.Group is Catalog.Cpu or Catalog.Memory, T("The CPU and memory tests"));
        Pick(T("GPU + Video"), t => t.Group is Catalog.Gpu or Catalog.Video, T("The graphics card's tests"));
        Pick(T("All"), _ => true, T("Every test, All at Once last"));
        Pick(T("None"), _ => false, T("Untick every test"));
        testsPanel.Children.Add(quick);
        testsPanel.Children.Add(Options());
        estimate.Margin = new Thickness(0, 8, 0, 0);
        testsPanel.Children.Add(estimate);
        page.Children.Add(Ui.Card(testsPanel, new Thickness(0, 0, 0, 12)));

        // ---------------- what's running
        liveCard = Ui.Card(Live(), new Thickness(0, 0, 0, 12));
        liveCard.Visibility = Visibility.Collapsed;
        page.Children.Add(liveCard);

        // ---------------- results
        page.Children.Add(Ui.Card(results, new Thickness(0, 0, 0, 12)));

        // ---------------- every run
        page.Children.Add(Ui.Card(History(), new Thickness(0, 0, 0, 12)));

        Content = Ui.Page(page);
        UpdateEstimate();
        ShowResults(null);
        runner.Changed += () => Dispatcher.BeginInvoke(Refresh);
        timer.Tick += (_, _) => Tick();
        Refresh();
    }

    // ---------------------------------------------------------------- options

    private UIElement Options()
    {
        var wrap = new WrapPanel();
        void Field(string label, FrameworkElement control, string? tip = null)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 16, 6), ToolTip = tip };
            p.Children.Add(Ui.Secondary(label));
            control.Margin = new Thickness(0, 3, 0, 0);
            p.Children.Add(control);
            wrap.Children.Add(p);
        }
        Field(T("Each test"), Ui.Choice(new[] { 10, 20, 30, 60, 120, 300, 600, 1800, 3600 }.Select(s => (Ui.Duration(TimeSpan.FromSeconds(s)), s)), settings.Seconds,
            v => { settings.Seconds = v; settings.Save(); UpdateEstimate(); }, 100), T("How long each test runs: longer finds more problems"));
        Field(T("Repeat"), Ui.Choice(new[] { (T("Once"), 1), ("2×", 2), ("3×", 3), ("5×", 5), ("10×", 10), (T("Until stopped"), 0) }, settings.Loops,
            v => { settings.Loops = v; settings.Save(); UpdateEstimate(); }, 120));
        Field(T("Logging"), Ui.Choice(new[] { (T("Standard"), StressLogging.Standard), (T("Maximum"), StressLogging.Maximum) }, settings.Logging,
            v => { settings.Logging = v; settings.Save(); }, 110), T("Maximum also logs every second's readings and each pass, to find where a crash happened (Support → Logs)"));
        var gpus = new List<(string, string)> { (T("First"), "") };
        try { gpus.AddRange(Graphics.List().Select(a => (a.Spec.Name, a.Spec.Name))); } catch { }
        Field(T("Graphics card"), Ui.Choice(gpus, settings.Gpu, v => { settings.Gpu = v; settings.Save(); }, 230));
        var drives = new List<(string, string)>();
        foreach (var d in DriveInfo.GetDrives())
            try { if (d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady) drives.Add(($"{d.Name.TrimEnd('\\')} {d.VolumeLabel}".Trim(), d.Name)); } catch { }
        if (!drives.Any(d => d.Item2.Equals(settings.Drive, StringComparison.OrdinalIgnoreCase))) drives.Insert(0, (settings.Drive, settings.Drive));
        Field(T("Drive"), Ui.Choice(drives, settings.Drive, v => { settings.Drive = v; settings.Save(); }, 120), T("Where the storage test writes its file"));
        Field(T("Storage file"), Ui.Choice(new[] { 256, 512, 1024, 2048, 4096, 8192 }.Select(m => (m >= 1024 ? $"{m / 1024} GB" : $"{m} MB", m)), settings.StorageMiB,
            v => { settings.StorageMiB = v; settings.Save(); }, 90));
        Field(T("Memory to test"), Ui.Choice(new[] { 25, 40, 60, 75, 85 }.Select(p => (F("{0}% of free", p), p)), settings.MemoryPercent,
            v => { settings.MemoryPercent = v; settings.Save(); }, 120), T("How much of the free memory the integrity test fills"));
        Field(T("VRAM to test"), Ui.Choice(new[] { 50, 70, 85, 90, 95 }.Select(p => (F("{0}% of free", p), p)), settings.VramPercent,
            v => { settings.VramPercent = v; settings.Save(); }, 120), T("How much of the free video memory the VRAM test fills"));
        var stopBox = new CheckBox { Content = T("Stop at the first error"), IsChecked = settings.StopOnError, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };
        Ui.OnToggle(stopBox, on => { settings.StopOnError = on; settings.Save(); });
        wrap.Children.Add(stopBox);
        return wrap;
    }

    private void UpdateEstimate()
    {
        int n = settings.Tests.Count;
        if (n == 0) { estimate.Text = T("Tick a test to run"); return; }
        // each test takes its time and a few seconds to get ready
        var each = TimeSpan.FromSeconds(n * (settings.Seconds + 4));
        estimate.Text = settings.Loops == 0
            ? F("{0} a round, until stopped", Ui.Duration(each))
            : F("About {0}", Ui.Duration(each * settings.Loops));
    }

    private async void StartStop()
    {
        if (runner.Running) { runner.Stop(); return; }
        if (settings.Tests.Count == 0) return;
        try { await runner.RunAsync(); }
        catch (Exception ex) { Logging.Log.Here.Error("Stress Tests: the run failed", ex); }
    }

    // ---------------------------------------------------------------- live

    private UIElement Live()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Ui.HeaderRow(liveTitle, liveCount));
        liveStep.Margin = new Thickness(0, -6, 0, 0);
        left.Children.Add(liveStep);
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal };
        valueRow.Children.Add(liveValue);
        liveErrors.VerticalAlignment = VerticalAlignment.Center;
        liveErrors.Margin = new Thickness(16, 0, 0, 0);
        liveErrors.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        valueRow.Children.Add(liveErrors);
        left.Children.Add(valueRow);
        testBar.Margin = new Thickness(0, 4, 0, 4);
        left.Children.Add(testBar);
        runBar.Margin = new Thickness(0, 0, 0, 8);
        runBar.SetResourceReference(Control.ForegroundProperty, "TextSecondaryBrush");
        runBar.ToolTip = T("The whole run");
        left.Children.Add(runBar);
        left.Children.Add(graph);
        liveSensors.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(liveSensors);
        g.Children.Add(left);
        preview.Margin = new Thickness(16, 0, 0, 0);
        preview.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(preview, 1);
        g.Children.Add(preview);
        return g;
    }

    private void Refresh()
    {
        bool running = runner.Running;
        start.Content = ((Button)Ui.IconButton(running ? "" : "", running ? T("Stop") : T("Start"))).Content;
        System.Windows.Automation.AutomationProperties.SetName(start, running ? T("Stop") : T("Start"));
        start.IsEnabled = running || settings.Tests.Count > 0;
        foreach (var c in chips.Values) c.IsEnabled = !running;
        liveCard.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running) { if (!timer.IsEnabled) timer.Start(); Tick(); }
        else
        {
            timer.Stop();
            frameShown = -1;
            preview.Source = null;
        }
        // the runs list, and the newest run's results once it's done
        var runs = runner.Runs.OrderByDescending(r => r.Started).ToList();
        var picked = history.SelectedItem as RunRow;
        history.ItemsSource = runs.Select(r => new RunRow(r)).ToList();
        if (running && runner.Current is { } cur) ShowResults(cur, live: true);
        else if (shown == null || !runs.Contains(shown)) ShowResults(runs.FirstOrDefault());
        else if (shown == runs.FirstOrDefault()) ShowResults(shown);
        if (picked != null) history.SelectedItem = (history.ItemsSource as List<RunRow>)?.FirstOrDefault(x => x.Run == picked.Run);
    }

    private int shownResults = -1;

    private void Tick()
    {
        if (!runner.Running) return;
        var c = runner.Test;
        int done = runner.Done, total = runner.Total;
        liveTitle.Text = c == null ? T("Getting ready") : $"{Catalog.FullName(c.Info.Group, c.Info.Name)}{(runner.Current?.Loops != 1 ? " · " + F("loop {0}", c.Result.Loop) : "")}";
        liveCount.Text = total > 0 ? F("Test {0} of {1}", Math.Min(done + 1, total), total) : F("Test {0}", done + 1);
        liveStep.Text = c?.Step ?? "";
        liveValue.Text = c == null || double.IsNaN(c.Live) ? "—" : Metric.Format(c.Live, c.LiveUnit);
        int errors = (runner.Current?.Errors ?? 0) + (c?.Errors ?? 0);
        liveErrors.Text = errors > 0 ? P(errors, "{0:#,0} error", "{0:#,0} errors") : "";
        testBar.Value = (c?.Progress ?? 0) * 100;
        runBar.Value = total > 0 ? Math.Min(100, (done + (c?.Progress ?? 0)) / total * 100) : 0;
        runBar.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        liveSensors.Text = runner.Latest?.Text() ?? "";
        if (c != null)
        {
            var trace = c.Result.Trace.ToList();
            var start = DateTime.Today;
            var points = trace.Select((v, i) => (start.AddSeconds(i + 1), v)).ToList();
            if (!double.IsNaN(c.Live)) points.Add((start.AddSeconds(Math.Max(0, c.Elapsed.TotalSeconds)), c.Live));
            graph.Show([new GraphSeries { Name = T(c.Info.Name), Points = points, Metric = new UnitOf(c.LiveUnit) }],
                start, start.AddSeconds(Math.Max(10, c.Length.TotalSeconds)), null, TimeSpan.FromSeconds(3));
        }
        if (runner.Frame is { } f && runner.FrameNumber != frameShown)
        {
            frameShown = runner.FrameNumber;
            if (frame == null || frame.PixelWidth != f.Width || frame.PixelHeight != f.Height)
                frame = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Bgr32, null);
            frame.WritePixels(new Int32Rect(0, 0, f.Width, f.Height), f.Pixels, f.Width * 4, 0);
            preview.Source = frame;
        }
        if (runner.Current is { } run && runner.ResultCount != shownResults) ShowResults(run, live: true);
    }

    private sealed class UnitOf(string unit) : IGraphUnit
    {
        public string Unit => unit;
        public string Text(double value) => Metric.Format(value, unit);
    }

    // ---------------------------------------------------------------- results

    private void ShowResults(StressRun? run, bool live = false)
    {
        shown = live ? shown : run;
        shownResults = live ? runner.ResultCount : run?.Results.Count ?? -1;
        results.Children.Clear();
        var export = Ui.IconButton("", T("Export…"), T("Save this run to compare it on another PC"), () => { if (run != null) Export(run); });
        var compare = Ui.IconButton("", T("Compare…"), T("Compare this run with another"), () => CompareWindow.Show(this, runner, settings, null, run));
        export.IsEnabled = compare.IsEnabled = run != null && !live;
        results.Children.Add(Ui.HeaderRow(Ui.Header(live ? T("This Run") : T("Results")), export, compare));
        if (run == null)
        {
            results.Children.Add(Ui.Secondary(T("Results show here after a run."), wrap: true));
            return;
        }
        if (live) run = runner.Snapshot() ?? run;
        var info = Ui.Secondary($"{run.System.Pc} · {run.Started:g} · {run.System.CpuText} · {run.System.Gpu} · {run.System.RamText}", wrap: true);
        info.Margin = new Thickness(0, -6, 0, 10);
        results.Children.Add(info);

        if (run.Interrupted)
            results.Children.Add(Problem(F("This run stopped during {0}: the app or Windows closed (a crash, a freeze or a restart). What it finished is below.", run.InterruptedDuring ?? "?")));
        else if (run.Stopped) results.Children.Add(Ui.Secondary(T("Stopped before the end.")));

        // the score, and each group's
        var scores = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        var overall = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
        overall.Children.Add(Ui.Secondary(T("Score")));
        overall.Children.Add(new TextBlock { Text = run.Score > 0 ? run.Score.ToString("#,0") : "—", FontSize = 38, FontWeight = FontWeights.Light });
        scores.Children.Add(overall);
        foreach (var g in Catalog.Groups)
        {
            if (!run.GroupScores.TryGetValue(g, out double s)) continue;
            var p = new StackPanel { Margin = new Thickness(0, 8, 24, 0) };
            p.Children.Add(Ui.Secondary(Catalog.GroupTitle(g)));
            p.Children.Add(new TextBlock { Text = s.ToString("#,0"), FontSize = 22 });
            scores.Children.Add(p);
        }
        int errors = run.Errors;
        var verdict = new StackPanel { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var v = new TextBlock
        {
            Text = errors > 0 ? P(errors, "{0:#,0} error found", "{0:#,0} errors found") : run.Interrupted ? T("Didn't finish") : run.Events.Count > 0 ? T("Windows logged problems") : run.Results.Count > 0 ? T("No errors") : "",
            FontWeight = FontWeights.SemiBold, FontSize = 16,
        };
        v.SetResourceReference(TextBlock.ForegroundProperty, errors > 0 || run.Interrupted || run.Events.Count > 0 ? "ErrorTextBrush" : "SuccessBrush");
        verdict.Children.Add(v);
        scores.Children.Add(verdict);
        results.Children.Add(scores);

        // each test
        var table = new Grid();
        foreach (var w in new[] { 190.0, 70, 160, 130, -1, 80 })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Cell(UIElement e, int col, int span = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetColumnSpan(e, span);
            table.Children.Add(e);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = [T("Test"), T("Score"), "", T("Result"), T("Details"), T("Errors")];
        for (int i = 0; i < heads.Length; i++) Cell(Ui.Secondary(heads[i]), i);
        row++;
        var ids = run.Results.Select(r => r.Id).Distinct().ToList();
        double top = Math.Max(1200, ids.Select(id => run.Averaged(id)?.Score ?? 0).DefaultIfEmpty(0).Max());
        foreach (var id in ids)
        {
            var r = run.Averaged(id)!;
            var info2 = Catalog.Find(id);
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = Catalog.FullName(r.Group, r.Name), Margin = new Thickness(0, 6, 8, 2), TextWrapping = TextWrapping.Wrap, ToolTip = r.Device };
            Cell(name, 0);
            Cell(new TextBlock { Text = r.Score > 0 ? r.Score.ToString("#,0") : "—", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 8, 2) }, 1);
            if (r.Score > 0)
            {
                var bar = Ui.Bar(8);
                bar.Maximum = top;
                bar.Value = r.Score;
                bar.Margin = new Thickness(0, 10, 14, 2);
                bar.VerticalAlignment = VerticalAlignment.Top;
                Cell(bar, 2);
            }
            Cell(new TextBlock { Text = r.Failed != null ? T("Couldn't run") : Metric.Format(r.Value, r.Unit), Margin = new Thickness(0, 6, 8, 2) }, 3);
            var details = string.Join(" · ", r.Metrics.Select(m => $"{m.Name} {m.Text}"));
            if (r.Failed != null) details = r.Failed;
            var d = Ui.Selectable(string.Join("\n", new[] { r.Device ?? "", details }.Where(s => s.Length > 0)));
            d.Margin = new Thickness(0, 6, 8, 2);
            d.SetResourceReference(Control.ForegroundProperty, "TextSecondaryBrush");
            Cell(d, 4);
            var e = new TextBlock { Text = r.Errors > 0 ? r.Errors.ToString("#,0") : r.Failed != null ? "" : "0", Margin = new Thickness(0, 6, 0, 2) };
            e.SetResourceReference(TextBlock.ForegroundProperty, r.Errors > 0 ? "ErrorTextBrush" : "TextSecondaryBrush");
            if (r.Errors > 0) e.FontWeight = FontWeights.SemiBold;
            Cell(e, 5);
            row++;
            if (r.Notes.Count > 0 || SensorLine(r.Sensors, r.Group) is { Length: > 0 })
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var notes = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (SensorLine(r.Sensors, r.Group) is { Length: > 0 } sensors) notes.Children.Add(Ui.Secondary(sensors, wrap: true));
                foreach (var n in r.Notes.Take(6))
                {
                    var t = new TextBlock { Text = n, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                    t.SetResourceReference(TextBlock.ForegroundProperty, n.StartsWith('✕') ? "ErrorTextBrush" : "TextSecondaryBrush");
                    notes.Children.Add(t);
                }
                if (r.Notes.Count > 6) notes.Children.Add(Ui.Secondary(F("…and {0} more in the log", r.Notes.Count - 6)));
                Cell(notes, 3, 3);
                row++;
            }
        }
        results.Children.Add(table);

        if (run.Events.Count > 0)
        {
            var head = Ui.Header(T("Windows logged during the run"));
            head.Margin = new Thickness(0, 14, 0, 6);
            results.Children.Add(head);
            foreach (var ev in run.Events)
            {
                var t = Ui.Selectable($"{ev.Time:T}  {ev.Source} {ev.Id}: {ev.Message}");
                t.SetResourceReference(Control.ForegroundProperty, ev.Level is "Error" or "Critical" ? "ErrorTextBrush" : "TextBrush");
                results.Children.Add(t);
            }
        }
    }

    /// <summary>A test's sensor summary: the CPU's for CPU, memory and storage tests, the GPU's for GPU and video ones, both for All at Once.</summary>
    private static string SensorLine(SensorSummary s, string group)
    {
        static string N(double v, string f) => double.IsNaN(v) ? "" : string.Format(f, v);
        bool cpu = group is not (Catalog.Gpu or Catalog.Video), gpu = group is Catalog.Gpu or Catalog.Video or Catalog.Stress;
        return string.Join(" · ", new[]
        {
            cpu ? N(s.CpuClockAvg / 1000, "CPU {0:0.00} GHz") : "", cpu ? N(s.CpuTempMax, "{0:0} °C") : "", cpu ? N(s.CpuPowerMax, "{0:0} W") : "",
            gpu ? N(s.GpuLoadAvg, "GPU {0:0}%") : "", gpu ? N(s.GpuClockAvg, "{0:0} MHz") : "", gpu ? N(s.GpuTempMax, "{0:0} °C") : "", gpu ? N(s.GpuPowerMax, "{0:0} W") : "",
        }.Where(x => x.Length > 0));
    }

    private static Border Problem(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        return new Border { Child = t, Margin = new Thickness(0, 0, 0, 8) };
    }

    private void Export(StressRun run)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = T("Stress test runs") + " (*.json)|*.json", Title = T("Export the Run"),
            FileName = string.Concat($"{run.System.Pc} Stress Test {run.Started:yyyy-MM-dd HH-mm}".Split(Path.GetInvalidFileNameChars())) + ".json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string? file = run.File;
            run.Write(dialog.FileName);
            run.File = file;
            Logging.Log.Here.Info($"Stress Tests: the run of {run.Started:g} exported to {dialog.FileName}");
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, T("Export the Run"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // ---------------------------------------------------------------- history

    private sealed class RunRow(StressRun run)
    {
        public StressRun Run => run;
        public string When => run.Started.ToString("g");
        public DateTime Sort => run.Started;
        public string Pc => run.System.Pc;
        public string Tests => string.Join(", ", run.Results.Select(r => r.Name).Distinct());
        public string Score => run.Score > 0 ? run.Score.ToString("#,0") : "—";
        public double ScoreValue => run.Score;
        public string Length => Ui.Duration((run.Ended > run.Started ? run.Ended : run.Started) - run.Started);
        public string Errors => run.Errors.ToString("#,0");
        public string Status => run.Interrupted ? F("Stopped during {0}", run.InterruptedDuring ?? "?")
            : run.Stopped ? T("Stopped") : run.Events.Count > 0 ? P(run.Events.Count, "{0} Windows event", "{0} Windows events") : T("Finished");
    }

    private UIElement History()
    {
        var p = new StackPanel();
        var export = Ui.IconButton("", T("Export…"), T("Save the run picked to compare it on another PC"), () => { if (history.SelectedItem is RunRow r) Export(r.Run); });
        var delete = ConfirmButton.Make(T("Delete"), () => { if (history.SelectedItem is RunRow r) runner.Delete(r.Run); }, T("Delete the run picked"));
        var open = Ui.IconButton("", "", T("Open the folder of runs"), () => { Directory.CreateDirectory(Runner.Folder); Ui.OpenUrl(Runner.Folder); });
        p.Children.Add(Ui.HeaderRow(Ui.Header(T("Runs")), open, export, delete));
        history.Columns.Add(Ui.Column(T("When"), nameof(RunRow.When), 140, nameof(RunRow.Sort)));
        history.Columns.Add(Ui.Column(T("PC"), nameof(RunRow.Pc), 120));
        history.Columns.Add(Ui.Column(T("Score"), nameof(RunRow.Score), 70, nameof(RunRow.ScoreValue), right: true));
        history.Columns.Add(Ui.Column(T("Errors"), nameof(RunRow.Errors), 60, right: true));
        history.Columns.Add(Ui.Column(T("Took"), nameof(RunRow.Length), 80));
        history.Columns.Add(Ui.Column(T("Status"), nameof(RunRow.Status), 170));
        history.Columns.Add(Ui.Column(T("Tests"), nameof(RunRow.Tests), -1));
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is RunRow r && !runner.Running) ShowResults(r.Run); };
        p.Children.Add(history);
        return p;
    }

    public void Dispose() => timer.Stop();
}
