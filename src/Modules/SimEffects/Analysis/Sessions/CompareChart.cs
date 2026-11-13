using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>A part of a session to compare (a lap, or all of it): samples <see cref="From"/> to <see cref="To"/>, and
/// along it <see cref="Xs"/>, the distance from its start (metres) or the time (seconds).</summary>
internal sealed class Run
{
    public required PedalAnalysis Analysis { get; init; }
    public required int From { get; init; }
    public required int To { get; init; }
    public required string Label { get; init; }
    public double[] Xs { get; private set; } = Array.Empty<double>();
    /// <summary>(since 4.14.1) The time from its start at each sample, less the hidden pauses (seconds).</summary>
    public double[] Ts { get; private set; } = Array.Empty<double>();
    public SessionData Data => Analysis.Data;
    public double Length => Xs.Length == 0 ? 0 : Xs[^1];
    public double Seconds => Ts.Length == 0 ? (To - From) * SessionData.Dt : Ts[^1];

    /// <summary>Has the distance all along it.</summary>
    public bool HasDistance
    {
        get
        {
            if (!Data.HasDistance) return false;
            int known = 0;
            for (int i = From; i <= To; i++) if (!double.IsNaN(Data.Distance[i])) known++;
            return known > (To - From) * 0.9;
        }
    }

    /// <summary>Lays it out along the distance (never going back) or the time (since 4.14.1 less the hidden pauses).</summary>
    public void Layout(bool byDistance)
    {
        int n = To - From + 1;
        var xs = new double[n];
        var ts = new double[n];
        double d0 = double.NaN, last = 0, played = 0;
        for (int k = 0; k < n; k++)
        {
            if (k > 0 && Data.HiddenAt(From + k) == null) played += SessionData.Dt;
            ts[k] = played;
            if (!byDistance) { xs[k] = played; continue; }
            double d = Data.Distance[From + k];
            if (double.IsNaN(d0) && !double.IsNaN(d)) d0 = d;
            if (!double.IsNaN(d) && !double.IsNaN(d0)) last = Math.Max(last, d - d0);
            xs[k] = last;
        }
        Xs = xs;
        Ts = ts;
    }

    /// <summary>(since 4.14.1) The time from its start at a moment of the session (a sample, fractional), less the
    /// hidden pauses.</summary>
    public double TimeAt(double sample)
    {
        if (Ts.Length == 0) return 0;
        double f = Math.Clamp(sample - From, 0, Ts.Length - 1);
        int i = (int)f;
        return i >= Ts.Length - 1 ? Ts[^1] : Ts[i] + (Ts[i + 1] - Ts[i]) * (f - i);
    }

    /// <summary>The sample at <paramref name="x"/> along it (fractional), or NaN past its ends.</summary>
    public double IndexAt(double x)
    {
        if (Xs.Length == 0 || x < Xs[0] || x > Xs[^1]) return double.NaN;
        int lo = 0, hi = Xs.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Xs[mid] <= x) lo = mid; else hi = mid; }
        double span = Xs[hi] - Xs[lo];
        return lo + (span <= 0 ? 0 : (x - Xs[lo]) / span);
    }

    /// <summary>Where a sample of the session is along it (clamped to its ends).</summary>
    public double XOf(int sample) => Xs.Length == 0 ? 0 : Xs[Math.Clamp(sample - From, 0, Xs.Length - 1)];

    /// <summary>(since 4.10.3) Where a moment of the session (a sample, fractional) is along it (clamped to its ends).</summary>
    public double XAt(double sample)
    {
        if (Xs.Length == 0) return 0;
        double f = Math.Clamp(sample - From, 0, Xs.Length - 1);
        int i = (int)f;
        return i >= Xs.Length - 1 ? Xs[^1] : Xs[i] + (Xs[i + 1] - Xs[i]) * (f - i);
    }

    public double ValueAt(double[] series, double x)
    {
        double f = IndexAt(x);
        if (double.IsNaN(f)) return double.NaN;
        int i = (int)f;
        double a = series[From + i], b = series[From + Math.Min(i + 1, To - From)];
        return double.IsNaN(b) ? a : a + (b - a) * (f - i);
    }
}

/// <summary>What the comparison chart draws, a lane each.</summary>
internal enum Channel { Delta, Speed, Throttle, Brake, Steering, Clutch }

/// <summary>
/// Two runs (laps or sessions; this one and another, maybe someone else's) drawn over each other, along the distance
/// when both have it (else the time): a lane each for the time gained or lost (Delta, along the distance), speed,
/// throttle, brake, steering and (since 4.7.0) clutch. Since 4.6.2 each channel has its colour (speed blue, throttle green, brake red, steering
/// white), the other run dotted; each channel of each run can be hidden or highlighted (click its name; its line too until 4.10.3). The
/// wheel zooms, dragging moves along (or the scroll bar under it, since 4.6.9; since 4.10.3 <see cref="CompareOverview"/>), a double-click shows it all; the readout follows the mouse (<see cref="Hovered"/>).
/// Since 4.10.3 a click puts the cursor (the white line, <see cref="Clicked"/>) there, the readout at it when the mouse is
/// off the chart; Compare's <see cref="PlaybackBar"/> moves it.
/// Since 4.7.0 also the Trace (the first run alone when there's no other): its brake zones shaded and numbered (a click
/// on its number picks one), its laps' starts, what's worth a look marked at the top (with a tooltip), and the moment picked (a
/// zone, a shift) as a line. Since 4.13.0 <see cref="Combined"/>: one timeline per run, its channels over each other.
/// </summary>
internal sealed class CompareChart : LayeredChart
{
    private Run? a, b;
    private double[]? delta; // along A: B's time at A's distance less A's (+: B slower)
    private double x0, x1;
    private Point? hover, dragFrom;
    private double dragX0;
    private bool dragged;
    private const double Left = 52, Gap = 12, MarkerRow = 14;
    /// <summary>(since 4.15.0 a second row along the distance: Session 1's time there)</summary>
    private double AxisHeight => ByDistance && a != null ? 30 : 18;
    /// <summary>The moment picked: a line at its start, its span (if any) shaded.</summary>
    private (double At, double? To)? mark;
    /// <summary>(since 4.8.0) Where the race's video is, playing (NaN: not); since 4.10.3 the cursor.</summary>
    private double playX = double.NaN;
    /// <summary>The run whose moments the cursor and the video are: the first.</summary>
    public Run? A => a;
    public double PlayX => playX;
    /// <summary>(since 4.10.15) The section looped, its ends along the axis (NaN: not set).</summary>
    public double LoopFrom { get; private set; } = double.NaN;
    public double LoopTo { get; private set; } = double.NaN;
    /// <summary>(since 4.10.15) Where it was last right-clicked along the axis (NaN: off the plot), for the loop's menu.</summary>
    public double MenuX { get; private set; } = double.NaN;
    public event Action? LoopChanged;

    /// <summary>The first run's brake zone picked (shaded stronger).</summary>
    public BrakeZone? Selected { get; set; }
    /// <summary>A brake zone of the first run was clicked.</summary>
    public event Action<BrakeZone>? ZoneClicked;

    public bool ByDistance { get; private set; }
    /// <summary>The channels of a run (1 this, 2 the other) not drawn.</summary>
    public HashSet<(Channel Channel, int Run)> Hidden { get; } = new();
    /// <summary>The line drawn bold, the others faded (null: none).</summary>
    public (Channel Channel, int Run)? Highlight { get; set; }
    /// <summary>Whether the Delta lane shows.</summary>
    public bool ShowDelta { get; set; } = true;
    /// <summary>(since 4.13.0) One timeline per run, every channel on it, instead of a lane per channel.</summary>
    public bool Combined { get; set; }
    /// <summary>(since 4.13.3) The marks above it not shown (the filter by them); shared by Compare's and the Video's.</summary>
    public HashSet<MarkerKind> HiddenMarkers { get; set; } = new();
    /// <summary>(since 4.13.3) The mark jumped to (Next or Previous Event): its tooltip shown while the mouse is off
    /// the marks; gone when the cursor moves off it.</summary>
    private Marker? pinned;

    /// <summary>(since 4.13.3) The first run's marks shown, in order.</summary>
    public IEnumerable<Marker> Marks => a == null ? Enumerable.Empty<Marker>()
        : a.Analysis.Markers.Where(m => m.At >= a.From && m.At <= a.To && !HiddenMarkers.Contains(m.Kind));

    /// <summary>(since 4.13.3) The mark after (<paramref name="dir"/> 1) or before (-1) a moment of the first run (a
    /// sample, fractional), or null.</summary>
    public Marker? MarkFrom(double sample, int dir) => dir > 0
        ? Marks.FirstOrDefault(m => m.At > sample + 0.5)
        : Marks.LastOrDefault(m => m.At < sample - 0.5);

    /// <summary>(since 4.13.3) A mark jumped to: its tooltip shown, the part shown moved to it if it's off it.</summary>
    public void Pin(Marker m)
    {
        if (a == null) return;
        double x = a.XOf(m.At);
        if (x < x0 || x > x1) MoveTo(x - (x1 - x0) / 2);
        pinned = m;
        InvalidateVisual();
    }

    /// <summary>The mouse along the axis (metres or seconds; NaN: off the chart).</summary>
    public event Action<double>? Hovered;
    /// <summary>(since 4.6.9) The part of the axis shown changed (zoomed or moved): for the scroll bar under it.</summary>
    public event Action? ViewChanged;
    /// <summary>(since 4.10.3) A click (not a drag) along the axis: where (metres or seconds).</summary>
    public event Action<double>? Clicked;
    /// <summary>(since 4.10.3) The cursor moved.</summary>
    public event Action? PlayMoved;
    /// <summary>(since 4.14.1) A pause's icon clicked: it's hidden or shown (the runs to be laid out again).</summary>
    public event Action? PausesChanged;
    /// <summary>(since 4.14.1) Where each pause's icon was drawn, for a click.</summary>
    private readonly List<(Rect Area, Pause Pause)> pauseIcons = new();

    /// <summary>(since 4.14.1) The first run's pauses.</summary>
    private IEnumerable<Pause> Pauses() => a == null ? Enumerable.Empty<Pause>() : a.Data.Pauses.Where(p => p.End > a.From && p.From < a.To);

    /// <summary>(since 4.14.1) Lays the runs out again (a pause hidden or shown), keeping the part shown where it can.</summary>
    public void Relayout()
    {
        a?.Layout(ByDistance);
        b?.Layout(ByDistance);
        delta = ByDistance && a != null && b != null ? Delta(a, b) : null;
        SetView(x0, x1);
    }

    /// <summary>The part shown: from, its span, and the whole length (metres or seconds).</summary>
    public double ViewFrom => x0;
    public double ViewSpan => x1 - x0;
    public double Total => Math.Max(1, Math.Max(a?.Length ?? 0, b?.Length ?? 0));

    /// <summary>Moves the part shown to start at <paramref name="from"/>, keeping its span (the scroll bar).</summary>
    public void MoveTo(double from) => SetView(from, from + (x1 - x0));

    /// <summary>(since 4.10.3) Zooms by <paramref name="f"/> (less than 1: in) round <paramref name="at"/>.</summary>
    public void ZoomAt(double at, double f)
    {
        if (a == null && b == null) return;
        SetView(at - (at - x0) * f, at + (x1 - at) * f);
    }

    public void ShowAll() => SetView(0, Total);

    /// <summary>(since 4.10.3) Playing: the part shown moves on a page when the cursor nears its end (its start going back).</summary>
    public void Follow(double x, bool forward)
    {
        double span = x1 - x0;
        if (double.IsNaN(x) || span >= Total - 1e-6) return;
        if (forward && (x > x0 + span * 0.95 || x < x0)) SetView(x - span * 0.05, x + span * 0.95);
        else if (!forward && (x < x0 + span * 0.05 || x > x1)) SetView(x - span * 0.95, x + span * 0.05);
    }

    /// <summary>A channel's colour (Delta's amber).</summary>
    public static Color ColourOf(Channel c) => c switch
    {
        Channel.Speed => PedalColours.Clutch,
        Channel.Throttle => PedalColours.Throttle,
        Channel.Brake => PedalColours.Brake,
        Channel.Steering => Colors.White,
        Channel.Clutch => PedalColours.Impact,
        _ => PedalColours.Warning,
    };

    public bool Shown(Channel c, int run) => (run == 1 ? a : b) != null && !Hidden.Contains((c, run));

    public void Show(Run? a, Run? b)
    {
        this.a = a;
        this.b = b;
        ByDistance = a != null && a.HasDistance && (b == null || b.HasDistance);
        a?.Layout(ByDistance);
        b?.Layout(ByDistance);
        delta = ByDistance && a != null && b != null ? Delta(a, b) : null;
        mark = null;
        pinned = null;
        Selected = null;
        x0 = 0;
        x1 = Math.Max(1, Math.Max(a?.Length ?? 0, b?.Length ?? 0));
        playX = double.NaN;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>The time gained or lost at the end of each part of A (by distance): B's time to there less A's.</summary>
    private static double[] Delta(Run a, Run b)
    {
        var d = new double[a.Xs.Length];
        for (int k = 0; k < d.Length; k++)
        {
            double f = b.IndexAt(a.Xs[k]);
            d[k] = double.IsNaN(f) ? double.NaN : b.TimeAt(b.From + f) - a.Ts[k];
        }
        return d;
    }

    /// <summary>The gap at a distance (B less A, seconds), NaN where unknown.</summary>
    public double DeltaAt(double x)
    {
        if (delta == null || a == null) return double.NaN;
        double f = a.IndexAt(x);
        return double.IsNaN(f) ? double.NaN : delta[Math.Min(delta.Length - 1, (int)Math.Round(f))];
    }

    /// <summary>Shows a part of the axis (metres or seconds), with a little either side.</summary>
    public void ShowRange(double from, double to)
    {
        double pad = Math.Max(ByDistance ? 40 : 1, (to - from) * 0.4);
        SetView(from - pad, to + pad);
    }

    /// <summary>(since 4.7.0) Shows a moment of the first run (samples of its session): a line at <paramref name="at"/>
    /// (else its start), and the span shaded unless it's a zone (shaded as one).</summary>
    public void ShowSamples(int from, int to, BrakeZone? zone = null, int? at = null)
    {
        if (a == null) return;
        Selected = zone;
        double xa = a.XOf(from), xb = a.XOf(to);
        mark = (at is { } m ? a.XOf(m) : xa, zone == null && at == null ? xb : null);
        ShowRange(xa, xb);
    }

    /// <summary>(since 4.8.0) A line where the race's video is (NaN: none).</summary>
    public void PlayAt(double x)
    {
        if (double.IsNaN(x) && double.IsNaN(playX)) return;
        playX = x;
        // (since 4.15.2 the cursor alone drawn again, unless the mark jumped to lets go)
        if (pinned != null && a != null && (double.IsNaN(x) || Math.Abs(Px(x) - Px(a.XOf(pinned.At))) > 1)) { pinned = null; InvalidateVisual(); }
        else InvalidateTop();
        PlayMoved?.Invoke();
    }

    /// <summary>(since 4.10.15) Shows the section looped (its ends NaN: not set).</summary>
    public void SetLoop(double from, double to)
    {
        if (from.Equals(LoopFrom) && to.Equals(LoopTo)) return;
        LoopFrom = from;
        LoopTo = to;
        InvalidateVisual();
        LoopChanged?.Invoke();
    }

    /// <summary>(since 4.10.15) The section looped: tinted between its ends (both set), a line at each, from
    /// <paramref name="top"/> to <paramref name="bottom"/> (pixels; NaN: an end not set).</summary>
    internal static void DrawLoop(DrawingContext dc, FrameworkElement e, double from, double to, double top, double bottom)
    {
        var accent = PedalColours.Theme(e, "AccentBrush");
        if (!double.IsNaN(from) && !double.IsNaN(to) && accent is SolidColorBrush sb)
            dc.DrawRectangle(PedalColours.Frozen(sb.Color, 0x28), null, new Rect(new Point(from, top), new Point(Math.Max(from + 1, to), bottom)));
        var pen = PedalColours.Pen(accent, 1.5, 0.9);
        if (!double.IsNaN(from)) dc.DrawLine(pen, new Point(from, top), new Point(from, bottom));
        if (!double.IsNaN(to)) dc.DrawLine(pen, new Point(to, top), new Point(to, bottom));
    }

    public CompareChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        MouseMove += (_, e) =>
        {
            hover = e.GetPosition(this);
            Cursor = pauseIcons.Any(i => i.Area.Contains(hover.Value)) ? Cursors.Hand : Cursors.Cross;
            Hovered?.Invoke(hover.Value.X >= Left && hover.Value.X <= Left + PlotWidth && (a != null || b != null) ? XAt(hover.Value.X) : double.NaN);
            if (dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
            {
                double span = x1 - x0, w = Math.Max(10, ActualWidth - Left - 8);
                if (Math.Abs(from.X - hover.Value.X) > 3) dragged = true;
                double shift = (from.X - hover.Value.X) / w * span;
                SetView(dragX0 + shift, dragX0 + shift + span);
            }
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { hover = null; Hovered?.Invoke(double.NaN); InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (a == null && b == null) return;
            if (e.ClickCount == 2) { ShowAll(); return; }
            dragFrom = e.GetPosition(this);
            dragX0 = x0;
            dragged = false;
            CaptureMouse();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            // (since 4.10.3 a click puts the cursor there; a zone's number picks it; lines from their names in the rows)
            var p = e.GetPosition(this);
            if (dragFrom != null && !dragged && !ClickPause(p) && !ClickZone(p) && p.X >= Left && p.X <= Left + PlotWidth) Clicked?.Invoke(XAt(p.X));
            dragFrom = null;
        };
        MouseRightButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            MenuX = a != null && p.X >= Left && p.X <= Left + PlotWidth ? XAt(p.X) : double.NaN;
        };
        MouseWheel += (_, e) =>
        {
            if (a == null && b == null) return;
            ZoomAt(XAt(e.GetPosition(this).X), e.Delta > 0 ? 0.8 : 1.25);
            e.Handled = true;
        };
    }

    private void SetView(double from, double to)
    {
        double max = Math.Max(1, Math.Max(a?.Length ?? 0, b?.Length ?? 0));
        double span = Math.Clamp(to - from, ByDistance ? 20 : 0.5, max);
        from = Math.Clamp(from, 0, Math.Max(0, max - span));
        x0 = from;
        x1 = from + span;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private double PlotWidth => Math.Max(10, ActualWidth - Left - 8);
    private double XAt(double px) => x0 + (px - Left) / PlotWidth * (x1 - x0);
    private double Px(double x) => Left + (x - x0) / (x1 - x0) * PlotWidth;

    /// <summary>A lane: a channel's (<see cref="Run"/> 0) or, combined, a run's (1 or 2), every channel on it.</summary>
    private readonly record struct Lane(Channel Channel, int Run, Rect Area);

    /// <summary>The channels a run's timeline draws, combined.</summary>
    private List<Channel> Overlaid(int run)
    {
        var r = run == 1 ? a : b;
        return r == null ? new() : Enum.GetValues<Channel>().Where(c => c != Channel.Delta && Shown(c, run) && (c != Channel.Speed || r.Data.HasSpeed)).ToList();
    }

    /// <summary>The lanes shown, top to bottom, with their heights.</summary>
    private List<Lane> Lanes()
    {
        var lanes = new List<Lane>();
        double total = ActualHeight - AxisHeight - 4 - MarkerRow;
        if (Combined)
        {
            var runs = new[] { 1, 2 }.Where(r => Overlaid(r).Count > 0).ToList();
            double each = (total - Gap * Math.Max(0, runs.Count - 1)) / Math.Max(1, runs.Count), at = 4 + MarkerRow;
            foreach (int r in runs)
            {
                lanes.Add(new Lane(Channel.Speed, r, new Rect(Left, at, PlotWidth, Math.Max(10, each))));
                at += each + Gap;
            }
            return lanes;
        }
        var shown = Enum.GetValues<Channel>().Where(c => (c == Channel.Delta ? ShowDelta : Shown(c, 1) || Shown(c, 2)) && Has(c)).ToList();
        double weights = shown.Sum(c => c is Channel.Speed ? 1.4 : 1);
        double y = 4 + MarkerRow;
        foreach (var c in shown)
        {
            double h = (total - Gap * Math.Max(0, shown.Count - 1)) * (c is Channel.Speed ? 1.4 : 1) / Math.Max(1, weights);
            lanes.Add(new Lane(c, 0, new Rect(Left, y, PlotWidth, Math.Max(10, h))));
            y += h + Gap;
        }
        return lanes;
    }

    private bool Has(Channel c) => c switch
    {
        Channel.Delta => delta != null,
        Channel.Speed => (a?.Data.HasSpeed ?? false) || (b?.Data.HasSpeed ?? false),
        _ => a != null || b != null,
    };

    private static double[] Series(SessionData d, Channel c) => c switch
    {
        Channel.Speed => d.Speed,
        Channel.Throttle => d.Throttle,
        Channel.Brake => d.Brake,
        Channel.Clutch => d.Clutch,
        _ => d.Steer,
    };

    private double TopSpeed()
    {
        double top = 1;
        foreach (var r in new[] { a, b })
        {
            if (r == null) continue;
            for (int i = r.From; i <= r.To; i++) if (r.Data.Speed[i] > top) top = r.Data.Speed[i];
        }
        return top * 1.05;
    }

    private double DeltaRange()
    {
        double m = 0.2;
        if (delta != null) foreach (double v in delta) if (Math.Abs(v) > m) m = Math.Abs(v);
        return m * 1.1;
    }

    /// <summary>(since 4.14.1) A pause's icon clicked: hidden or shown.</summary>
    private bool ClickPause(Point p)
    {
        foreach (var (area, pause) in pauseIcons)
        {
            if (!area.Contains(p)) continue;
            pause.Hidden = !pause.Hidden;
            PausesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>The first run's brake zone whose number (at the top of the lanes) is clicked, picked.</summary>
    private bool ClickZone(Point p)
    {
        var lanes = Lanes();
        if (a == null || p.X < Left || lanes.Count == 0 || p.Y < lanes[0].Area.Top || p.Y > lanes[0].Area.Top + 16) return false;
        double x = XAt(p.X);
        var z = Zones().FirstOrDefault(z => x >= a.XOf(z.Start) && x <= a.XOf(z.End) && Px(a.XOf(z.End)) - Px(a.XOf(z.Start)) > 16);
        if (z == null) return false;
        Selected = z;
        mark = (a.XOf(z.Start), null);
        InvalidateVisual();
        ZoneClicked?.Invoke(z);
        return true;
    }

    private IEnumerable<BrakeZone> Zones() => a == null ? Enumerable.Empty<BrakeZone>() : a.Analysis.Brakes.Where(z => z.End >= a.From && z.Start <= a.To);

    protected override void RenderChart(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        drawn = null;
        var text = PedalColours.Theme(this, "TextBrush");
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var room = PedalColours.Theme(this, "RoomBrush");
        var gridPen = PedalColours.Pen(dim, 1, 0.18);
        var lanes = Lanes();
        if (a == null || lanes.Count == 0)
        {
            var r = new Rect(Left, 4, PlotWidth, Math.Max(10, h - AxisHeight - 8));
            dc.DrawRectangle(room, null, r);
            dc.DrawText(PedalColours.Text(this, T("Pick a session"), 13, dim), new Point(r.Left + 12, r.Top + 12));
            return;
        }
        double speedTop = TopSpeed(), deltaRange = DeltaRange();
        var white = PedalColours.Theme(this, "TextBrush");
        foreach (var (c, laneRun, area) in lanes)
        {
            dc.DrawRectangle(room, null, area);
            if (laneRun != 0)
            {
                DrawOverlaid(dc, laneRun, area, speedTop, deltaRange, dim, gridPen, white);
                continue;
            }
            string name = c switch
            {
                Channel.Delta => T("Delta"),
                Channel.Speed => Speeds.Unit,
                Channel.Throttle => T("Throttle"),
                Channel.Brake => T("Brake"),
                Channel.Clutch => T("Clutch"),
                _ => T("Steering"),
            };
            var label = PedalColours.Text(this, name, 10, dim);
            label.MaxTextWidth = Left - 6;
            dc.DrawText(label, new Point(2, area.Top + area.Height / 2 - label.Height / 2));
            dc.PushClip(new RectangleGeometry(area));
            if (c == Channel.Delta)
            {
                double Yd(double v) => area.Top + area.Height / 2 - v / deltaRange * area.Height / 2;
                dc.DrawLine(PedalColours.Pen(dim, 1, 0.5), new Point(area.Left, Yd(0)), new Point(area.Right, Yd(0)));
                dc.DrawText(PedalColours.Text(this, F("+{0:0.0} s", deltaRange), 9, dim), new Point(area.Left + 3, area.Top + 1));
                dc.DrawText(PedalColours.Text(this, F("-{0:0.0} s", deltaRange), 9, dim), new Point(area.Left + 3, area.Bottom - 13));
                // above the line: the other run slower (this one gaining), in this one's colour
                DrawSeries(dc, a!, delta!, 0, Yd, PedalColours.Pen(PedalColours.Frozen(ColourOf(c)), 1.6, Highlight != null ? 0.35 : 1), area);
            }
            else
            {
                double top = c == Channel.Speed ? speedTop : 1;
                double Y(double v) => c == Channel.Steering
                    ? area.Bottom - Math.Clamp(v, 0, 1) * area.Height
                    : area.Bottom - Math.Clamp(v / top, 0, 1) * area.Height;
                for (int q = 1; q < 4; q++)
                {
                    double y = area.Top + area.Height * q / 4;
                    dc.DrawLine(c == Channel.Steering && q == 2 ? PedalColours.Pen(dim, 1, 0.45) : gridPen, new Point(area.Left, y), new Point(area.Right, y));
                }
                if (c == Channel.Speed)
                    dc.DrawText(PedalColours.Text(this, $"{Speeds.Of(top):0}", 9, dim), new Point(area.Left + 3, area.Top + 1));
                // the faded one first, the highlighted one on top
                var order = Highlight == (c, 1) ? new[] { 2, 1 } : new[] { 1, 2 };
                foreach (int which in order)
                {
                    var r = which == 1 ? a : b;
                    if (r == null || !Shown(c, which)) continue;
                    bool hi = Highlight == (c, which), faded = Highlight != null && !hi;
                    var brush = c == Channel.Steering ? white : PedalColours.Frozen(ColourOf(c));
                    DrawSeries(dc, r, Series(r.Data, c), r.From, Y, LinePen(brush, hi ? 2.6 : 1.5, faded ? 0.3 : 1, which == 2), area);
                }
            }
            dc.Pop();
        }

        // (since 4.7.0, the Trace's) the first run's brake zones, its laps' starts and the moment picked, over every lane
        double top0 = lanes[0].Area.Top, bottom = lanes[^1].Area.Bottom;
        dc.PushClip(new RectangleGeometry(new Rect(Left, 0, PlotWidth, h)));
        foreach (var z in Zones())
        {
            double za = Px(a.XOf(z.Start)), zb = Px(a.XOf(z.End));
            if (zb < Left || za > Left + PlotWidth) continue;
            bool sel = z == Selected;
            var r = new Rect(new Point(za, top0), new Point(Math.Max(za + 1, zb), bottom));
            dc.DrawRectangle(PedalColours.Frozen(PedalColours.Brake, sel ? (byte)0x34 : (byte)0x10), sel ? PedalColours.Pen(PedalColours.Frozen(PedalColours.Brake), 1) : null, r);
            if (r.Width > 16) dc.DrawText(PedalColours.Text(this, z.Number.ToString(System.Globalization.CultureInfo.CurrentCulture), 10, dim), new Point(r.Left + 3, top0 + 2));
        }
        foreach (var lap in a.Data.Laps)
        {
            if (lap.Start <= a.From || lap.Start > a.To) continue;
            double lx = Px(a.XOf(lap.Start));
            dc.DrawLine(PedalColours.Pen(text, 1, 0.45), new Point(lx, top0), new Point(lx, bottom));
            dc.DrawText(PedalColours.Text(this, F("Lap {0}", lap.Number + 1), 10, text), new Point(lx + 3, bottom - 14));
        }
        if (mark is { } m)
        {
            double mx = Px(m.At);
            if (m.To is { } to) dc.DrawRectangle(PedalColours.Frozen(Colors.White, 0x14), null, new Rect(new Point(mx, top0), new Point(Math.Max(mx + 1, Px(to)), bottom)));
            dc.DrawLine(PedalColours.Pen(PedalColours.Theme(this, "AccentBrush"), 2), new Point(mx, top0 - 2), new Point(mx, bottom));
        }
        if (!double.IsNaN(LoopFrom) || !double.IsNaN(LoopTo)) DrawLoop(dc, this, Px(LoopFrom), Px(LoopTo), top0, bottom);
        // (since 4.14.1) the pauses: shown, shaded; hidden (cut out), a dashed line where they were
        var pausePen = PedalColours.Pen(dim, 1, 0.8);
        foreach (var pause in Pauses())
        {
            double pa = Px(a.XAt(pause.From)), pb = Px(a.XAt(pause.End));
            if (!pause.Hidden && pb - pa > 1) dc.DrawRectangle(PedalColours.Frozen(Colors.Gray, 0x38), null, new Rect(new Point(pa, top0), new Point(pb, bottom)));
            for (double y = top0; y < bottom; y += 6) dc.DrawLine(pausePen, new Point(pa, y), new Point(pa, Math.Min(bottom, y + 3)));
            if (!pause.Hidden && pb - pa > 1) for (double y = top0; y < bottom; y += 6) dc.DrawLine(pausePen, new Point(pb, y), new Point(pb, Math.Min(bottom, y + 3)));
        }
        dc.Pop();
        // what's worth a look, above the lanes
        Marker? near = null;
        foreach (var mk in Marks)
        {
            double mx = Px(a.XOf(mk.At));
            if (mx < Left || mx > Left + PlotWidth) continue;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(mx - 5, 3), true, true);
                ctx.LineTo(new Point(mx + 5, 3), true, false);
                ctx.LineTo(new Point(mx, MarkerRow + 1), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(MarkerBrush(mk.Kind), null, g);
            if (hover is { } hp && dragFrom == null && Math.Abs(hp.X - mx) < 8 && hp.Y < MarkerRow + 8
                && (near == null || Math.Abs(hp.X - mx) < Math.Abs(hp.X - Px(a.XOf(near.At))))) near = mk;
        }

        // their icons above them (with how long), a click hides or shows one
        pauseIcons.Clear();
        var icons = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets");
        Pause? overPause = null;
        double overX = 0;
        foreach (var pause in Pauses())
        {
            double cx = (Px(a.XAt(pause.From)) + Px(a.XAt(pause.End))) / 2;
            if (cx < Left - 6 || cx > Left + PlotWidth + 6) continue;
            var glyph = new FormattedText(pause.Hidden ? "" : "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(icons, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, PedalColours.Frozen(PedalColours.Warning), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var secs = PedalColours.Text(this, SessionChart.Clock(pause.Seconds), 9.5, dim);
            double wide = glyph.Width + 3 + secs.Width, left = cx - wide / 2;
            var area = new Rect(left - 3, 0, wide + 6, MarkerRow + 3);
            dc.DrawRoundedRectangle(PedalColours.Theme(this, "PopupBrush"), PedalColours.Pen(PedalColours.Frozen(PedalColours.Warning), 1, 0.6), area, 3, 3);
            dc.DrawText(glyph, new Point(left, (area.Height - glyph.Height) / 2));
            dc.DrawText(secs, new Point(left + glyph.Width + 3, (area.Height - secs.Height) / 2));
            pauseIcons.Add((area, pause));
            if (hover is { } ph && area.Contains(ph)) { overPause = pause; overX = cx; }
        }

        // the axis
        double span = x1 - x0;
        double[] steps = ByDistance ? new double[] { 10, 20, 50, 100, 200, 250, 500, 1000, 2000, 5000 } : new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 };
        // (since 4.13.3 twice as far apart: up to 5 across)
        double step = steps.FirstOrDefault(s => span / s <= 5, steps[^1]);
        for (double x = Math.Ceiling(x0 / step) * step; x <= x1; x += step)
        {
            double px = Px(x);
            foreach (var (_, _, area) in lanes) dc.DrawLine(gridPen, new Point(px, area.Top), new Point(px, area.Bottom));
            string t = ByDistance ? (x >= 1000 ? $"{x / 1000:0.##} km" : $"{x:0} m") : SessionChart.Clock(x);
            dc.DrawText(PedalColours.Text(this, t, 10, dim), new Point(px - 12, bottom + 2));
            // (since 4.15.0) along the distance, Session 1's time there under it
            if (ByDistance && TimeAlong(a, x) is var ta && !double.IsNaN(ta))
                dc.DrawText(PedalColours.Text(this, SessionChart.Clock(ta, span < 200), 10, dim), new Point(px - 12, bottom + 14));
        }

        // (since 4.13.3) the mark jumped to, its tooltip as the mouse's while the mouse is off the chart
        if (near == null && hover == null && pinned != null && Px(a.XOf(pinned.At)) is var pinX && pinX >= Left && pinX <= Left + PlotWidth) near = pinned;
        // the readout at the mouse (at the cursor: the top layer's)
        drawn = (lanes, bottom, near != null);
        if (hover is { } hp2 && hp2.X >= Left && hp2.X <= Left + PlotWidth && dragFrom == null)
        {
            dc.DrawLine(PedalColours.Pen(text, 1, 0.5), new Point(hp2.X, lanes[0].Area.Top), new Point(hp2.X, bottom));
            Readout(dc, hp2.X, lanes, w, text);
        }
        if (near != null && overPause == null) MarkerTip(dc, near, Px(a.XOf(near.At)));
        if (overPause != null)
        {
            var tip = PedalColours.Text(this, overPause.Hidden ? F("Paused {0} (cut out): show", SessionChart.Clock(overPause.Seconds)) : F("Paused {0}: hide", SessionChart.Clock(overPause.Seconds)), 11, text);
            double tx = Math.Clamp(overX - tip.Width / 2 - 6, 0, Math.Max(0, w - tip.Width - 12));
            dc.DrawRoundedRectangle(PedalColours.Theme(this, "PopupBrush"), PedalColours.Pen(PedalColours.Theme(this, "ControlBorderBrush"), 1), new Rect(tx, MarkerRow + 6, tip.Width + 12, tip.Height + 8), 4, 4);
            dc.DrawText(tip, new Point(tx + 6, MarkerRow + 10));
        }
    }

    /// <summary>(since 4.15.2) What the chart was last drawn with, for its top layer (null: nothing to compare).</summary>
    private (List<Lane> Lanes, double Bottom, bool Tip)? drawn;

    /// <summary>(since 4.15.2) The top layer: the cursor, and (since 4.10.3) the readout at it while the mouse is off the
    /// chart (not under a mark's tooltip).</summary>
    protected override void RenderTop(DrawingContext dc)
    {
        if (drawn is not { } d || a == null || double.IsNaN(playX)) return;
        double px = Px(playX);
        if (px < Left || px > Left + PlotWidth) return;
        dc.DrawLine(PedalColours.Pen(Brushes.White, 1.5), new Point(px, d.Lanes[0].Area.Top - 2), new Point(px, d.Bottom));
        if (hover == null && !d.Tip && dragFrom == null) Readout(dc, px, d.Lanes, ActualWidth, PedalColours.Theme(this, "TextBrush"));
    }

    /// <summary>The readout at a place on the chart (pixels across): where, the time, Delta and each channel's values.</summary>
    private void Readout(DrawingContext dc, double px, List<Lane> lanes, double w, Brush text)
    {
        double x = XAt(px);
        var lines = new List<string> { ByDistance ? $"{x:0} m" + Times(x) : SessionChart.Clock(x, true) };
        string Pair(Channel c, Func<SessionData, double[]> of, Func<double, string> fmt)
        {
            string va = a != null && !Hidden.Contains((c, 1)) ? Fmt(a.ValueAt(of(a.Data), x)) : "", vb = b != null && !Hidden.Contains((c, 2)) ? Fmt(b.ValueAt(of(b.Data), x)) : "";
            return vb.Length == 0 ? va : va.Length == 0 ? vb : va + "  /  " + vb;
            string Fmt(double v) => double.IsNaN(v) ? "–" : fmt(v);
        }
        if (delta != null && !double.IsNaN(DeltaAt(x))) lines.Add(F("Delta {0:+0.00;-0.00;0.00} s", DeltaAt(x)));
        if (Has(Channel.Speed) && (Shown(Channel.Speed, 1) || Shown(Channel.Speed, 2))) lines.Add(Pair(Channel.Speed, d => d.Speed, v => $"{Speeds.Of(v):0} {Speeds.Unit}"));
        lines.Add(T("Throttle") + " " + Pair(Channel.Throttle, d => d.Throttle, v => $"{v * 100:0}%"));
        lines.Add(T("Brake") + " " + Pair(Channel.Brake, d => d.Brake, v => $"{v * 100:0}%"));
        lines.Add(T("Steering") + " " + Pair(Channel.Steering, d => d.Steer, v => $"{(v - 0.5) * 450:+0;-0;0}°"));
        if (Shown(Channel.Clutch, 1) || Shown(Channel.Clutch, 2)) lines.Add(T("Clutch") + " " + Pair(Channel.Clutch, d => d.Clutch, v => $"{v * 100:0}%"));
        var ft = PedalColours.Text(this, string.Join("\n", lines), 11, text);
        double bx = px + 12 + ft.Width + 12 > w ? px - 12 - ft.Width - 12 : px + 12;
        dc.DrawRoundedRectangle(PedalColours.Theme(this, "PopupBrush"), PedalColours.Pen(PedalColours.Theme(this, "ControlBorderBrush"), 1), new Rect(bx, lanes[0].Area.Top + 4, ft.Width + 12, ft.Height + 8), 4, 4);
        dc.DrawText(ft, new Point(bx + 6, lanes[0].Area.Top + 8));
    }

    /// <summary>(since 4.15.0) A run's time (from its start, less the hidden pauses) at a distance along it; NaN past its ends.</summary>
    private static double TimeAlong(Run? r, double x) =>
        r == null || double.IsNaN(r.IndexAt(x)) ? double.NaN : r.TimeAt(r.From + r.IndexAt(x));

    /// <summary>(since 4.15.0) The readout's times at a distance: " · 0:32.45", both runs' with another.</summary>
    private string Times(double x)
    {
        double ta = TimeAlong(a, x), tb = TimeAlong(b, x);
        if (double.IsNaN(ta) && double.IsNaN(tb)) return "";
        string Fmt(double t) => double.IsNaN(t) ? "–" : SessionChart.Clock(t, true);
        return " · " + (b == null ? Fmt(ta) : Fmt(ta) + "  /  " + Fmt(tb));
    }

    /// <summary>(since 4.13.0) A run's timeline, combined: every channel it shows drawn over each other, each on its own
    /// scale, and on the first the Delta.</summary>
    private void DrawOverlaid(DrawingContext dc, int run, Rect area, double speedTop, double deltaRange, Brush dim, Pen gridPen, Brush white)
    {
        var label = PedalColours.Text(this, run == 1 ? T("Session 1") : T("Session 2"), 10, dim);
        label.MaxTextWidth = Left - 6;
        dc.DrawText(label, new Point(2, area.Top + area.Height / 2 - label.Height / 2));
        dc.PushClip(new RectangleGeometry(area));
        for (int q = 1; q < 4; q++)
        {
            double y = area.Top + area.Height * q / 4;
            dc.DrawLine(gridPen, new Point(area.Left, y), new Point(area.Right, y));
        }
        var channels = Overlaid(run);
        if (channels.Contains(Channel.Speed))
            dc.DrawText(PedalColours.Text(this, $"{Speeds.Of(speedTop):0}", 9, dim), new Point(area.Left + 3, area.Top + 1));
        var r = run == 1 ? a! : b!;
        if (run == 1 && ShowDelta && delta != null)
        {
            double Yd(double v) => area.Top + area.Height / 2 - v / deltaRange * area.Height / 2;
            DrawSeries(dc, r, delta, 0, Yd, PedalColours.Pen(PedalColours.Frozen(ColourOf(Channel.Delta)), 1.6, Highlight != null ? 0.35 : 1), area);
        }
        // (the highlighted one on top)
        foreach (var c in channels.OrderBy(c => Highlight == (c, run)))
        {
            double top = c == Channel.Speed ? speedTop : 1;
            double Y(double v) => area.Bottom - Math.Clamp(v / top, 0, 1) * area.Height;
            bool hi = Highlight == (c, run), faded = Highlight != null && !hi;
            var brush = c == Channel.Steering ? white : PedalColours.Frozen(ColourOf(c));
            DrawSeries(dc, r, Series(r.Data, c), r.From, Y, LinePen(brush, hi ? 2.6 : 1.5, faded ? 0.3 : 1, false), area);
        }
        dc.Pop();
    }

    /// <summary>A marker's tooltip, under its arrow: a rounded bubble with a shadow, a stripe and title in the marker's
    /// colour, and what it is (the Trace's until 4.7.0).</summary>
    private void MarkerTip(DrawingContext dc, Marker m, double x)
    {
        var colour = MarkerBrush(m.Kind);
        var title = PedalColours.Text(this, m.Title, 12, colour);
        title.SetFontWeight(FontWeights.SemiBold);
        var body = PedalColours.Text(this, m.Text, 11.5, PedalColours.Theme(this, "TextBrush"));
        body.MaxTextWidth = 280;
        double w = Math.Max(title.Width, body.Width) + 26, h = title.Height + body.Height + 16;
        double left = Math.Clamp(x - w / 2, 2, Math.Max(2, ActualWidth - w - 2)), top = MarkerRow + 8;
        var box = new Rect(left, top, w, h);
        dc.DrawRoundedRectangle(PedalColours.Frozen(Colors.Black, 0x40), null, new Rect(left + 2, top + 3, w, h), 7, 7);
        var pointer = new StreamGeometry();
        using (var ctx = pointer.Open())
        {
            ctx.BeginFigure(new Point(x - 6, top + 0.5), true, true);
            ctx.LineTo(new Point(x, top - 6), true, false);
            ctx.LineTo(new Point(x + 6, top + 0.5), true, false);
        }
        pointer.Freeze();
        var back = PedalColours.Theme(this, "PopupBrush");
        var border = PedalColours.Pen(colour, 1, 0.8);
        dc.DrawRoundedRectangle(back, border, box, 7, 7);
        dc.DrawGeometry(back, border, pointer);
        dc.DrawRectangle(back, null, new Rect(x - 5, top - 0.5, 10, 2));
        dc.DrawRoundedRectangle(colour, null, new Rect(left + 6, top + 7, 3, h - 14), 1.5, 1.5);
        dc.DrawText(title, new Point(left + 16, top + 6));
        dc.DrawText(body, new Point(left + 16, top + 8 + title.Height));
    }

    internal static Brush MarkerBrush(MarkerKind k) => k switch
    {
        MarkerKind.SnapRelease or MarkerKind.Reapply or MarkerKind.Spike or MarkerKind.SlowApply => PedalColours.Frozen(PedalColours.Brake),
        MarkerKind.Overlap => PedalColours.Frozen(PedalColours.Warning),
        MarkerKind.Impact => PedalColours.Frozen(PedalColours.Impact),
        MarkerKind.Crash => PedalColours.Frozen(PedalColours.Crash),
        _ => PedalColours.Frozen(PedalColours.Throttle),
    };

    private static readonly Dictionary<(Brush, double, double, bool), Pen> pens = new();

    /// <summary>A line's pen: the other run's dotted (made once each).</summary>
    public static Pen LinePen(Brush brush, double thick, double opacity, bool dotted)
    {
        if (!dotted) return PedalColours.Pen(brush, thick, opacity);
        var key = (brush, thick, opacity, dotted);
        if (pens.TryGetValue(key, out var p)) return p;
        var b = opacity < 1 ? brush.CloneCurrentValue() : brush;
        if (opacity < 1) b.Opacity = opacity;
        p = new Pen(b, thick) { LineJoin = PenLineJoin.Round, DashStyle = new DashStyle(new double[] { 1.2, 1.6 }, 0), DashCap = PenLineCap.Round };
        p.Freeze();
        if (pens.Count > 200) pens.Clear();
        return pens[key] = p;
    }

    /// <summary>A run's series along the axis; with several samples to a pixel, every few.</summary>
    private void DrawSeries(DrawingContext dc, Run r, double[] series, int offset, Func<double, double> Y, Pen pen, Rect area)
    {
        double fa = r.IndexAt(Math.Max(x0, r.Xs[0])), fb = r.IndexAt(Math.Min(x1, r.Length));
        if (double.IsNaN(fa) || double.IsNaN(fb)) return;
        int from = Math.Max(0, (int)fa - 1), to = Math.Min(r.Xs.Length - 1, (int)Math.Ceiling(fb) + 1);
        int stride = Math.Max(1, (int)((to - from) / Math.Max(1, area.Width * 2)));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            for (int k = from; k <= to; k += stride)
            {
                double v = series[offset + k];
                if (double.IsNaN(v)) { open = false; continue; }
                var pt = new Point(Px(r.Xs[k]), Y(v));
                if (!open) { ctx.BeginFigure(pt, false, false); open = true; } else ctx.LineTo(pt, true, false);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
