using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>
/// A running sync measured four times a second: its speed over the whole run (for the graph), and its totals. The
/// sync's window owns it; its own progress and the tab's background card both show it.
/// </summary>
internal sealed class SyncMonitor
{
    private readonly SyncRunner runner;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Queue<(double T, long Bytes)> recent = new(), longer = new();

    /// <summary>The speed every quarter second since it started: seconds in, bytes a second.</summary>
    public List<(double T, double Rate)> Samples { get; } = new();

    public double Seconds { get; private set; }
    /// <summary>Over the last second and a half (the graph's and the readout's).</summary>
    public double Rate { get; private set; }
    public double Average => Seconds > 0.5 ? Done / Seconds : 0;
    public double Peak { get; private set; }
    public long Done { get; private set; }
    public long Total => runner.State.BytesTotal;
    public long ItemsDone => runner.State.ItemsDone;
    public long ItemsTotal => runner.State.ItemsTotal;
    public int Errors => runner.State.Errors.Count;
    public string Current => runner.State.Current;
    public bool Paused => !runner.Running.IsSet;
    public double Fraction => Total > 0 ? (double)Done / Total : ItemsTotal > 0 ? (double)ItemsDone / ItemsTotal : 0;

    /// <summary>Time left at the last five seconds' speed, or null while that's unknown.</summary>
    public TimeSpan? Left { get; private set; }

    public string Stage => runner.State.Stage switch
    {
        "deleting" => T("Deleting"),
        "folders" => T("Making folders"),
        _ => T("Copying"),
    };

    public event Action? Updated;

    public SyncMonitor(SyncRunner runner)
    {
        this.runner = runner;
        timer.Tick += (_, _) => Sample();
    }

    public void Start() { Sample(); timer.Start(); }

    public void Stop() { timer.Stop(); Sample(); }

    private void Sample()
    {
        double t = clock.Elapsed.TotalSeconds;
        long done = Interlocked.Read(ref runner.State.BytesDone);
        Seconds = t;
        Done = done;
        static double RateOver(Queue<(double T, long Bytes)> q, double t, long done, double window)
        {
            q.Enqueue((t, done));
            while (q.Count > 2 && t - q.Peek().T > window) q.Dequeue();
            var first = q.Peek();
            return t - first.T > 0.2 ? Math.Max(0, (done - first.Bytes) / (t - first.T)) : 0;
        }
        Rate = RateOver(recent, t, done, 1.5);
        double five = RateOver(longer, t, done, 5);
        Peak = Math.Max(Peak, Rate);
        Left = five > 1 && Total > done ? TimeSpan.FromSeconds((Total - done) / five) : null;
        Samples.Add((t, Rate));
        Updated?.Invoke();
    }
}

/// <summary>
/// The speed graph, as Windows' copy dialog has it but with its scales: the speed (bytes a second) against the time
/// since the sync started, the whole run fitted to the width, with a dashed line at the average.
/// </summary>
internal sealed class SpeedGraph : FrameworkElement
{
    private static readonly Typeface Mono = new("Consolas");
    private readonly SyncMonitor monitor;

    public SpeedGraph(SyncMonitor monitor)
    {
        this.monitor = monitor;
        Height = 150;
        SnapsToDevicePixels = true;
    }

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    /// <summary>The top of the speed scale: a round number (1, 2, 2.5 or 5 times a power of ten) in the unit it's shown in.</summary>
    private static double Top(double bytes)
    {
        double unit = 1;
        while (bytes / unit >= 1000 && unit < Math.Pow(1024, 3)) unit *= 1024;
        return Nice(bytes / unit) * unit;
    }

    private static double Nice(double v)
    {
        if (v <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    /// <summary>A speed's label on the scale: "250 MB/s" (decimal steps of 1,024).</summary>
    private static string Axis(double bytes)
    {
        if (bytes <= 0) return "0";
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int u = 0;
        while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
        return (bytes >= 10 ? bytes.ToString("0", CultureInfo.CurrentCulture) : bytes.ToString("0.#", CultureInfo.CurrentCulture)) + " " + units[u];
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private FormattedText Label(string s, Brush b, double size = 10.5) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 60) return;
        var text = Res("TextSecondaryBrush", Brushes.Gray);
        var back = Res("WindowBrush", Brushes.Black);
        var border = Res("CardBorderBrush", Brushes.DimGray);
        var accent = (Res("AccentBrush", Brushes.DodgerBlue) as SolidColorBrush)?.Color ?? Colors.DodgerBlue;

        var samples = monitor.Samples;
        double span = Math.Max(10, samples.Count > 0 ? samples[^1].T : 0);
        double top = Top(Math.Max(monitor.Peak, 1024) * 1.08);

        // the plot area, with room for the speed scale on the left and the time scale under it
        double left = Label(Axis(top), text).Width + 10, bottom = 18, topPad = 6;
        var plot = new Rect(left, topPad, Math.Max(10, w - left - 1), Math.Max(10, h - topPad - bottom));
        dc.DrawRectangle(back, new Pen(border, 1), plot);

        var gridLine = text.Clone();
        gridLine.Opacity = 0.35;
        var grid = new Pen(gridLine, 1) { DashStyle = new DashStyle(new double[] { 1, 3 }, 0) };

        // the speed scale: 4 steps
        for (int i = 0; i <= 4; i++)
        {
            double v = top * i / 4, y = plot.Bottom - plot.Height * i / 4;
            if (i > 0 && i < 4) dc.DrawLine(grid, new Point(plot.Left, Math.Round(y) + 0.5), new Point(plot.Right, Math.Round(y) + 0.5));
            var l = Label(Axis(v), text);
            dc.DrawText(l, new Point(left - 6 - l.Width, Math.Clamp(y - l.Height / 2, 0, h - l.Height)));
        }

        // the time scale: a round step that gives at most six labels
        double step = new double[] { 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400 }.FirstOrDefault(s => span / s <= 6, 28800);
        for (double s = 0; s <= span + 0.01; s += step)
        {
            double x = plot.Left + plot.Width * s / span;
            if (s > 0) dc.DrawLine(grid, new Point(Math.Round(x) + 0.5, plot.Top), new Point(Math.Round(x) + 0.5, plot.Bottom));
            var l = Label(Clock(s), text);
            dc.DrawText(l, new Point(Math.Clamp(x - l.Width / 2, plot.Left, w - l.Width), plot.Bottom + 3));
        }

        if (samples.Count < 2) return;
        // each column of pixels: the average speed of the samples in it
        int columns = Math.Max(2, (int)plot.Width);
        var sums = new double[columns];
        var counts = new int[columns];
        foreach (var (t, rate) in samples)
        {
            int c = Math.Clamp((int)(t / span * (columns - 1)), 0, columns - 1);
            sums[c] += rate;
            counts[c]++;
        }
        int last = (int)Math.Clamp(samples[^1].T / span * (columns - 1), 0, columns - 1);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            bool started = false;
            double x = plot.Left;
            for (int c = 0; c <= last; c++)
            {
                if (counts[c] == 0) continue; // (straight on to the next column with a sample)
                double v = sums[c] / counts[c];
                x = plot.Left + plot.Width * c / (columns - 1);
                var p = new Point(x, plot.Bottom - Math.Min(1, v / top) * plot.Height);
                if (!started)
                {
                    lc.BeginFigure(p, false, false);
                    ac.BeginFigure(new Point(x, plot.Bottom), true, true);
                    ac.LineTo(p, false, false);
                    started = true;
                }
                else { lc.LineTo(p, true, true); ac.LineTo(p, false, false); }
            }
            ac.LineTo(new Point(x, plot.Bottom), false, false);
        }
        line.Freeze();
        area.Freeze();
        var fill = new LinearGradientBrush(Color.FromArgb(150, accent.R, accent.G, accent.B), Color.FromArgb(25, accent.R, accent.G, accent.B), 90);
        fill.Freeze();
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.6) { LineJoin = PenLineJoin.Round }, line);

        // the average, dashed, with its value at the right
        double avg = monitor.Average;
        if (avg > 0)
        {
            double y = Math.Round(plot.Bottom - Math.Min(1, avg / top) * plot.Height) + 0.5;
            var avgBrush = Res("TextBrush", Brushes.White);
            dc.DrawLine(new Pen(avgBrush, 1) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) }, new Point(plot.Left, y), new Point(plot.Right, y));
            var l = Label("avg " + Axis(avg), avgBrush, 10);
            double ly = y - l.Height - 1 < plot.Top ? y + 2 : y - l.Height - 1;
            dc.DrawRectangle(back, null, new Rect(plot.Right - l.Width - 8, ly, l.Width + 6, l.Height));
            dc.DrawText(l, new Point(plot.Right - l.Width - 5, ly));
        }
        // where it is now
        double nx = plot.Left + plot.Width * last / (columns - 1);
        dc.DrawEllipse(new SolidColorBrush(accent), null, new Point(nx, plot.Bottom - Math.Min(1, monitor.Rate / top) * plot.Height), 3, 3);
        dc.Pop();
    }
}

/// <summary>
/// A running sync's progress: the speed in large figures, the graph, then a table of readouts (in a fixed-width face),
/// the bar and the file being copied. Shown in the sync's window and, while it runs in the background, on the tab.
/// </summary>
internal sealed class SyncProgressView : StackPanel
{
    private readonly SyncMonitor monitor;
    private readonly SpeedGraph graph;
    private readonly TextBlock speed = new() { FontSize = 26, FontWeight = FontWeights.Light, FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock stage = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 5) };
    private readonly TextBlock percent = new() { FontSize = 26, FontWeight = FontWeights.Light, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ProgressBar bar = Ui.Bar(6);
    private readonly TextBlock current = Ui.Secondary();
    private readonly TextBox errors = Ui.CopyText();
    private readonly Dictionary<string, TextBlock> values = new();
    private readonly Func<SyncRunner.Progress> state;

    public SyncProgressView(SyncMonitor monitor, Func<SyncRunner.Progress> state)
    {
        this.monitor = monitor;
        this.state = state;
        graph = new SpeedGraph(monitor);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(percent, Dock.Right);
        head.Children.Add(percent);
        head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { speed, stage } });
        stage.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Children.Add(head);
        Children.Add(graph);

        // the readouts: four columns of a label over its value
        var table = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (int c = 0; c < 4; c++) table.ColumnDefinitions.Add(new ColumnDefinition());
        string[] keys = ["speed", "average", "peak", "elapsed", "done", "items", "left", "problems"];
        string[] labels = [T("SPEED"), T("AVERAGE"), T("PEAK"), T("ELAPSED"), T("TRANSFERRED"), T("ITEMS"), T("TIME LEFT"), T("PROBLEMS")];
        for (int i = 0; i < keys.Length; i++)
        {
            if (i % 4 == 0) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = labels[i], FontSize = 10, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var value = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13.5, Text = "—", TextTrimming = TextTrimming.CharacterEllipsis };
            values[keys[i]] = value;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 8), Children = { label, value } };
            Grid.SetRow(cell, i / 4);
            Grid.SetColumn(cell, i % 4);
            table.Children.Add(cell);
        }
        Children.Add(table);
        bar.Maximum = 1;
        bar.Margin = new Thickness(0, 4, 0, 6);
        Children.Add(bar);
        Children.Add(current);
        errors.Margin = new Thickness(0, 8, 0, 0);
        errors.Visibility = Visibility.Collapsed;
        Children.Add(errors);

        Loaded += (_, _) => { monitor.Updated -= Show; monitor.Updated += Show; Show(); };
        Unloaded += (_, _) => monitor.Updated -= Show;
    }

    private void Show()
    {
        var m = monitor;
        speed.Text = m.Paused ? T("Paused") : Sizes.Speed(m.Rate);
        stage.Text = F("{0}: {1} of {2}", m.Stage, Sizes.Count(m.ItemsDone), Sizes.Count(m.ItemsTotal));
        percent.Text = Sizes.Percent(m.Fraction);
        values["speed"].Text = Sizes.Speed(m.Rate);
        values["average"].Text = Sizes.Speed(m.Average);
        values["peak"].Text = Sizes.Speed(m.Peak);
        values["elapsed"].Text = Sizes.Duration(TimeSpan.FromSeconds(m.Seconds));
        values["done"].Text = F("{0} / {1}", Sizes.Bytes(m.Done), Sizes.Bytes(m.Total));
        values["items"].Text = F("{0} / {1}", Sizes.Count(m.ItemsDone), Sizes.Count(m.ItemsTotal));
        values["left"].Text = m.Paused ? T("paused") : m.Left is { } l ? Sizes.Duration(l) : "—";
        values["problems"].Text = Sizes.Count(m.Errors);
        if (m.Errors > 0) values["problems"].SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        bar.Value = m.Fraction;
        current.Text = m.Current;
        SyncWindow.ShowErrors(errors, state());
        graph.InvalidateVisual();
    }
}
