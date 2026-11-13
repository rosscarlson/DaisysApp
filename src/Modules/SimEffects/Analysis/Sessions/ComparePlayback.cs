using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.10.3, the scroll bar's place) The whole of Compare's chart in a strip under it: Session 1's speed, its laps'
/// starts, the cursor, and a frame round the part the chart shows (dimmed either side). Dragging the frame moves the
/// chart along, a click elsewhere brings it there, the wheel zooms, a double-click shows it all.
/// </summary>
internal sealed class CompareOverview : LayeredChart
{
    private readonly CompareChart chart;
    /// <summary>Dragging the frame: where along it the mouse took hold (metres or seconds from its start).</summary>
    private double? grab;

    public CompareOverview(CompareChart chart)
    {
        this.chart = chart;
        Height = 40;
        ClipToBounds = true;
        System.Windows.Automation.AutomationProperties.SetName(this, T("Along"));
        chart.ViewChanged += InvalidateVisual;
        // (since 4.15.2 only the cursor drawn again)
        chart.PlayMoved += InvalidateTop;
        MouseLeftButtonDown += (_, e) =>
        {
            if (chart.A == null) return;
            if (e.ClickCount == 2) { chart.ShowAll(); return; }
            double x = XAt(e.GetPosition(this).X);
            // (outside the frame: it's centred there first)
            if (x < chart.ViewFrom || x > chart.ViewFrom + chart.ViewSpan) chart.MoveTo(x - chart.ViewSpan / 2);
            grab = x - chart.ViewFrom;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            double x = XAt(e.GetPosition(this).X);
            Cursor = x >= chart.ViewFrom && x <= chart.ViewFrom + chart.ViewSpan ? Cursors.SizeWE : Cursors.Hand;
            if (grab is { } g && e.LeftButton == MouseButtonState.Pressed) chart.MoveTo(x - g);
        };
        MouseLeftButtonUp += (_, _) => { grab = null; ReleaseMouseCapture(); };
        MouseWheel += (_, e) =>
        {
            chart.ZoomAt(XAt(e.GetPosition(this).X), e.Delta > 0 ? 0.8 : 1.25);
            e.Handled = true;
        };
        MouseRightButtonDown += (_, e) => MenuX = chart.A == null ? double.NaN : XAt(e.GetPosition(this).X);
        chart.LoopChanged += InvalidateVisual;
    }

    private double XAt(double px) => px / Math.Max(1, ActualWidth) * chart.Total;
    /// <summary>(since 4.10.15) Where it was last right-clicked (metres or seconds), for the loop's menu.</summary>
    public double MenuX { get; private set; } = double.NaN;
    private double Px(double x) => x / chart.Total * ActualWidth;

    protected override void RenderChart(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, new Rect(0, 0, w, h));
        var a = chart.A;
        if (a == null || a.Xs.Length < 2) return;
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        // Session 1's speed (else its throttle), a point a pixel
        var d = a.Data;
        double[] series = d.HasSpeed ? d.Speed : d.Throttle;
        double top = 1e-6;
        for (int i = a.From; i <= a.To; i++) if (series[i] > top) top = series[i];
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            int stride = Math.Max(1, (int)(a.Xs.Length / Math.Max(1, w)));
            for (int k = 0; k < a.Xs.Length; k += stride)
            {
                double v = series[a.From + k];
                if (double.IsNaN(v)) { open = false; continue; }
                var pt = new Point(Px(a.Xs[k]), h - 3 - v / top * (h - 8));
                if (!open) { ctx.BeginFigure(pt, false, false); open = true; } else ctx.LineTo(pt, true, false);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, PedalColours.Pen(PedalColours.Frozen(CompareChart.ColourOf(d.HasSpeed ? Channel.Speed : Channel.Throttle)), 1, 0.7), g);
        foreach (var lap in d.Laps)
        {
            if (lap.Start <= a.From || lap.Start > a.To) continue;
            double lx = Px(a.XOf(lap.Start));
            dc.DrawLine(PedalColours.Pen(dim, 1, 0.4), new Point(lx, 0), new Point(lx, h));
        }
        // (since 4.14.1) the pauses: a line where one is cut out, shown ones shaded
        foreach (var p in d.Pauses)
        {
            if (p.End <= a.From || p.From >= a.To) continue;
            double pa = Px(a.XAt(p.From)), pb = Px(a.XAt(p.End));
            if (!p.Hidden) dc.DrawRectangle(PedalColours.Frozen(Colors.Gray, 0x38), null, new Rect(new Point(pa, 0), new Point(Math.Max(pa + 1, pb), h)));
            dc.DrawLine(PedalColours.Pen(PedalColours.Frozen(PedalColours.Warning), 1, 0.7), new Point(pa, 0), new Point(pa, h));
        }
        // the part the chart shows, framed; the rest dimmed
        double fa = Px(chart.ViewFrom), fb = Math.Max(fa + 3, Px(chart.ViewFrom + chart.ViewSpan));
        var shade = PedalColours.Frozen(Colors.Black, 0x70);
        if (fa > 0) dc.DrawRectangle(shade, null, new Rect(0, 0, fa, h));
        if (fb < w) dc.DrawRectangle(shade, null, new Rect(fb, 0, w - fb, h));
        dc.DrawRoundedRectangle(null, PedalColours.Pen(PedalColours.Theme(this, "AccentBrush"), 1.5), new Rect(fa + 0.75, 0.75, Math.Max(1, fb - fa - 1.5), h - 1.5), 3, 3);
        if (!double.IsNaN(chart.LoopFrom) || !double.IsNaN(chart.LoopTo)) CompareChart.DrawLoop(dc, this, Px(chart.LoopFrom), Px(chart.LoopTo), 0, h);
    }

    /// <summary>(since 4.15.2) The cursor, on the top layer.</summary>
    protected override void RenderTop(DrawingContext dc)
    {
        if (chart.A is not { Xs.Length: >= 2 } || double.IsNaN(chart.PlayX)) return;
        double px = Px(chart.PlayX);
        dc.DrawLine(PedalColours.Pen(Brushes.White, 1.5), new Point(px, 0), new Point(px, ActualHeight));
    }
}

/// <summary>
/// (since 4.10.3) Compare's playback: Session 1's run played through from the cursor, the chart, the Track Map and the
/// race's video with it: back and on at twice the speed, in slow motion (half) either way, or as it was (with the
/// video's sound). <see cref="Seek"/> puts the cursor somewhere (a click on the chart). Since 4.10.9 its own class, so
/// the popped-out Video's timeline has controls of the same playback (<see cref="PlaybackBar"/>, one each).
/// </summary>
internal sealed class Playback
{
    private readonly Stopwatch watch = new();
    private Run? run;
    private double position = double.NaN, rate, startedAt;
    private bool rendering;

    /// <summary>The cursor moved (a sample of Session 1's log, fractional), at a rate (0: still); jumped: not by playing.</summary>
    public event Action<double, double, bool>? Moved;
    /// <summary>Its run, rate or place changed (for the bars' buttons and clock).</summary>
    public event Action? Changed;

    public bool Playing => rate != 0;
    public double Rate => rate;
    public double Position => position;
    public Run? Run => run;
    /// <summary>(since 4.15.3) A video playing along waits for more of it (YouTube's): the cursor waits where it is.</summary>
    public Func<bool>? Waiting { get; set; }
    /// <summary>Its bars on screen.</summary>
    public int Bars { get; set; }

    /// <summary>(since 4.10.15) The section to loop (samples of Session 1's log; NaN: not set), set from a chart's menu.</summary>
    public double LoopFrom { get; private set; } = double.NaN;
    public double LoopTo { get; private set; } = double.NaN;
    /// <summary>Both ends set, the start before the end.</summary>
    public bool CanLoop => !double.IsNaN(LoopFrom) && !double.IsNaN(LoopTo) && LoopTo - LoopFrom >= 1;
    /// <summary>Playing round the section: past its end (or its start going back) it starts again.</summary>
    public bool Looping { get; private set; }

    /// <summary>Sets the section's start or end at a sample (NaN: clears both); a start after the end swaps them.</summary>
    public void SetLoop(double sample, bool start)
    {
        if (run == null) return;
        if (double.IsNaN(sample)) LoopFrom = LoopTo = double.NaN;
        else if (start) LoopFrom = Math.Clamp(sample, run.From, run.To);
        else LoopTo = Math.Clamp(sample, run.From, run.To);
        if (LoopFrom > LoopTo) (LoopFrom, LoopTo) = (LoopTo, LoopFrom);
        if (!CanLoop) Looping = false;
        Changed?.Invoke();
    }

    /// <summary>Plays the section round from its start (the other speeds keep to it), or stops doing so.</summary>
    public void Loop()
    {
        if ((Looping && rate != 0) || !CanLoop) { Looping = false; Play(0); Changed?.Invoke(); return; }
        Looping = true;
        Seek(LoopFrom);
        if (rate <= 0) Play(1); else Changed?.Invoke();
    }

    /// <summary>Session 1's run (null: none): stopped, no cursor.</summary>
    public void Show(Run? r)
    {
        Play(0);
        run = r;
        position = double.NaN;
        LoopFrom = LoopTo = double.NaN;
        Looping = false;
        Changed?.Invoke();
    }

    /// <summary>Puts the cursor at a sample of Session 1's log (playing on from there if it plays).</summary>
    public void Seek(double sample)
    {
        if (run == null || double.IsNaN(sample)) return;
        // (since 4.14.1 not in a hidden pause: just after it)
        if (run.Data.HiddenAt(sample) is { } gap) sample = gap.End;
        position = Math.Clamp(sample, run.From, run.To);
        startedAt = position;
        watch.Restart();
        Changed?.Invoke();
        Moved?.Invoke(position, rate, true);
    }

    /// <summary>(since 4.14.1) The run laid out again (a pause hidden or shown): the cursor put where it is again.</summary>
    public void Relaid()
    {
        if (double.IsNaN(position)) { Changed?.Invoke(); return; }
        Seek(position);
    }

    /// <summary>Plays at a rate (negative: back; 0: pauses).</summary>
    public void Play(double r)
    {
        if (run == null) r = 0;
        if (r == rate) return;
        rate = r;
        if (r != 0)
        {
            // (from the cursor; at the end going on, or the start going back, from the other end)
            if (double.IsNaN(position) || (r > 0 && position >= run!.To - 1) || (r < 0 && position <= run!.From + 1)) position = r > 0 ? run!.From : run!.To;
            startedAt = position;
            watch.Restart();
            if (!rendering) { CompositionTarget.Rendering += Frame; rendering = true; }
        }
        else if (rendering) { CompositionTarget.Rendering -= Frame; rendering = false; }
        Changed?.Invoke();
        if (!double.IsNaN(position)) Moved?.Invoke(position, rate, true);
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (run == null || rate == 0) return;
        if (Waiting?.Invoke() == true) { startedAt = position; watch.Restart(); return; }
        double p = startedAt + watch.Elapsed.TotalSeconds * rate / SessionData.Dt;
        // (since 4.10.15 round the section: from its other end, a jump for the video)
        if (Looping && CanLoop && ((rate > 0 && p >= LoopTo && startedAt < LoopTo) || (rate < 0 && p <= LoopFrom && startedAt > LoopFrom)))
        {
            position = startedAt = rate > 0 ? LoopFrom : LoopTo;
            watch.Restart();
            Moved?.Invoke(position, rate, true);
            return;
        }
        // (since 4.14.1) over a hidden pause: on from its other side, a jump for the video
        if (run.Data.HiddenAt(p) is { } gap)
        {
            p = rate > 0 ? gap.End : gap.From - 1;
            position = startedAt = Math.Clamp(p, run.From, run.To);
            watch.Restart();
            Moved?.Invoke(position, rate, true);
            if (p >= run.To || p <= run.From) Play(0);
            return;
        }
        bool end = p >= run.To || p <= run.From;
        position = Math.Clamp(p, run.From, run.To);
        Moved?.Invoke(position, rate, false);
        if (end) Play(0);
    }
}

/// <summary>
/// (since 4.10.3) <see cref="Playback"/>'s controls, under a chart: rewind 2× (since 4.10.10 also 4× and 8×), slow
/// motion back ½×, play/pause, slow motion ½×, fast forward 2× (4×, 8×), and the clock.
/// </summary>
internal sealed class PlaybackBar : DockPanel
{
    private static readonly (double Rate, string Glyph, string Speed, bool Mirror, string Name)[] Rates =
    {
        (-8, "", "8×", false, "Rewind (8×)"),
        (-4, "", "4×", false, "Rewind (4×)"),
        (-2, "", "2×", false, "Rewind (2×)"),
        (-0.5, "", "½×", true, "Slow motion back (½×)"),
        (1, "", "", false, "Play"),
        (0.5, "", "½×", false, "Slow motion (½×)"),
        (2, "", "2×", false, "Fast forward (2×)"),
        (4, "", "4×", false, "Fast forward (4×)"),
        (8, "", "8×", false, "Fast forward (8×)"),
    };

    private readonly Playback playback;
    private readonly Dictionary<double, (Button Button, TextBlock Glyph)> buttons = new();
    private Button loop = null!;
    private readonly TextBlock clock = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 120, Margin = new Thickness(10, 0, 0, 0) };

    public PlaybackBar(Playback playback)
    {
        this.playback = playback;
        Margin = new Thickness(52, 6, 8, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (r, glyph, speed, mirror, name) in Rates)
        {
            // (since 4.10.15) the section set from a chart's menu, played round; greyed out until both its ends are set
            if (r == 1)
            {
                var lg = new TextBlock { Text = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                lg.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                loop = new Button { Content = lg, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 0, 2, 0), MinWidth = 44, ToolTip = T("Loop") };
                ToolTipService.SetShowOnDisabled(loop, true);
                System.Windows.Automation.AutomationProperties.SetName(loop, T("Loop"));
                loop.Click += (_, _) => playback.Loop();
                row.Children.Add(loop);
            }
            var g = new TextBlock { Text = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            if (mirror) { g.RenderTransformOrigin = new Point(0.5, 0.5); g.RenderTransform = new ScaleTransform(-1, 1); }
            var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { g } };
            if (speed.Length > 0) content.Children.Add(new TextBlock { Text = speed, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) });
            var b = new Button { Content = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 0, 2, 0), MinWidth = 44, ToolTip = T(name) };
            System.Windows.Automation.AutomationProperties.SetName(b, T(name));
            double rateOf = r;
            // (the one playing: pauses)
            b.Click += (_, _) => playback.Play(playback.Rate == rateOf ? 0 : rateOf);
            buttons[r] = (b, g);
            row.Children.Add(b);
        }
        clock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        SetDock(clock, Dock.Right);
        Children.Add(clock);
        Children.Add(row);
        // (stopped when no bar's left: Compare's gone and the Video's window closed)
        Loaded += (_, _) => playback.Bars++;
        Unloaded += (_, _) => { if (--playback.Bars <= 0) playback.Play(0); };
        playback.Changed += ShowState;
        playback.Moved += (_, _, _) => ShowClock();
        ShowState();
    }

    private void ShowState()
    {
        IsEnabled = playback.Run != null;
        loop.IsEnabled = playback.CanLoop;
        if (playback.Looping && playback.Playing) loop.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        else loop.ClearValue(Control.ForegroundProperty);
        foreach (var (r, (b, g)) in buttons)
        {
            bool on = r == playback.Rate;
            if (on) b.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            else b.ClearValue(Control.ForegroundProperty);
            // (play shows pause while it plays as it was)
            if (r == 1)
            {
                g.Text = on ? "" : "";
                string name = on ? T("Pause") : T("Play");
                b.ToolTip = name;
                System.Windows.Automation.AutomationProperties.SetName(b, name);
            }
        }
        ShowClock();
    }

    private void ShowClock()
    {
        var run = playback.Run;
        if (run == null) { clock.Text = ""; return; }
        double at = double.IsNaN(playback.Position) ? 0 : run.TimeAt(playback.Position);
        clock.Text = $"{SessionChart.Clock(at, true)} / {SessionChart.Clock(run.Seconds, true)}";
    }
}
