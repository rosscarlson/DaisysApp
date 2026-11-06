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
    public SessionData Data => Analysis.Data;
    public double Length => Xs.Length == 0 ? 0 : Xs[^1];
    public double Seconds => (To - From) * SessionData.Dt;

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

    /// <summary>Lays it out along the distance (never going back) or the time.</summary>
    public void Layout(bool byDistance)
    {
        int n = To - From + 1;
        var xs = new double[n];
        double d0 = double.NaN, last = 0;
        for (int k = 0; k < n; k++)
        {
            if (!byDistance) { xs[k] = k * SessionData.Dt; continue; }
            double d = Data.Distance[From + k];
            if (double.IsNaN(d0) && !double.IsNaN(d)) d0 = d;
            if (!double.IsNaN(d) && !double.IsNaN(d0)) last = Math.Max(last, d - d0);
            xs[k] = last;
        }
        Xs = xs;
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
internal enum Channel { Delta, Speed, Throttle, Brake, Steering }

/// <summary>
/// Two runs (laps or sessions; this one and another, maybe someone else's) drawn over each other, along the distance
/// when both have it (else the time): a lane each for the time gained or lost (Delta, along the distance), speed,
/// throttle, brake and steering. Either run can be hidden, or highlighted white (click its line or its name); the wheel
/// zooms, dragging moves along, a double-click shows it all; the readout follows the mouse.
/// </summary>
internal sealed class CompareChart : FrameworkElement
{
    private Run? a, b;
    private double[]? delta; // along A: B's time at A's distance less A's (+: B slower)
    private double x0, x1;
    private Point? hover, dragFrom;
    private double dragX0;
    private bool dragged;
    private const double Left = 52, AxisHeight = 18, Gap = 6;

    public bool ByDistance { get; private set; }
    public bool ShowA { get; set; } = true;
    public bool ShowB { get; set; } = true;
    /// <summary>0 none, 1 this run, 2 the other: drawn white.</summary>
    public int Highlight { get; set; }
    public HashSet<Channel> Channels { get; } = new() { Channel.Delta, Channel.Speed, Channel.Throttle, Channel.Brake, Channel.Steering };

    /// <summary>A run's line was clicked: which (1 or 2).</summary>
    public event Action<int>? RunClicked;

    public void Show(Run? a, Run? b)
    {
        this.a = a;
        this.b = b;
        ByDistance = a != null && a.HasDistance && (b == null || b.HasDistance);
        a?.Layout(ByDistance);
        b?.Layout(ByDistance);
        delta = ByDistance && a != null && b != null ? Delta(a, b) : null;
        x0 = 0;
        x1 = Math.Max(1, Math.Max(a?.Length ?? 0, b?.Length ?? 0));
        InvalidateVisual();
    }

    /// <summary>The time gained or lost at the end of each part of A (by distance): B's time to there less A's.</summary>
    private static double[] Delta(Run a, Run b)
    {
        var d = new double[a.Xs.Length];
        for (int k = 0; k < d.Length; k++)
        {
            double f = b.IndexAt(a.Xs[k]);
            d[k] = double.IsNaN(f) ? double.NaN : f * SessionData.Dt - k * SessionData.Dt;
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

    public CompareChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        MouseMove += (_, e) =>
        {
            hover = e.GetPosition(this);
            if (dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
            {
                double span = x1 - x0, w = Math.Max(10, ActualWidth - Left - 8);
                if (Math.Abs(from.X - hover.Value.X) > 3) dragged = true;
                double shift = (from.X - hover.Value.X) / w * span;
                SetView(dragX0 + shift, dragX0 + shift + span);
            }
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { hover = null; InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (a == null && b == null) return;
            if (e.ClickCount == 2) { SetView(0, Math.Max(a?.Length ?? 0, b?.Length ?? 0)); return; }
            dragFrom = e.GetPosition(this);
            dragX0 = x0;
            dragged = false;
            CaptureMouse();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            if (dragFrom != null && !dragged) ClickLine(e.GetPosition(this));
            dragFrom = null;
        };
        MouseWheel += (_, e) =>
        {
            if (a == null && b == null) return;
            double at = XAt(e.GetPosition(this).X);
            double f = e.Delta > 0 ? 0.8 : 1.25;
            SetView(at - (at - x0) * f, at + (x1 - at) * f);
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
    }

    private double PlotWidth => Math.Max(10, ActualWidth - Left - 8);
    private double XAt(double px) => x0 + (px - Left) / PlotWidth * (x1 - x0);
    private double Px(double x) => Left + (x - x0) / (x1 - x0) * PlotWidth;

    /// <summary>The lanes shown, top to bottom, with their heights.</summary>
    private List<(Channel Channel, Rect Area)> Lanes()
    {
        var shown = Enum.GetValues<Channel>().Where(c => Channels.Contains(c) && Has(c)).ToList();
        var lanes = new List<(Channel, Rect)>();
        double total = ActualHeight - AxisHeight - 4;
        double weights = shown.Sum(c => c is Channel.Speed ? 1.4 : 1);
        double y = 4;
        foreach (var c in shown)
        {
            double h = (total - Gap * Math.Max(0, shown.Count - 1)) * (c is Channel.Speed ? 1.4 : 1) / Math.Max(1, weights);
            lanes.Add((c, new Rect(Left, y, PlotWidth, Math.Max(10, h))));
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

    private void ClickLine(Point p)
    {
        foreach (var (c, area) in Lanes())
        {
            if (!area.Contains(p) || c == Channel.Delta) continue;
            double x = XAt(p.X), top = c == Channel.Speed ? TopSpeed() : 1;
            double want = (area.Bottom - p.Y) / area.Height * top;
            double da = a != null && ShowA ? Math.Abs(a.ValueAt(Series(a.Data, c), x) - want) : double.NaN;
            double db = b != null && ShowB ? Math.Abs(b.ValueAt(Series(b.Data, c), x) - want) : double.NaN;
            int which = double.IsNaN(db) || da <= db ? 1 : 2;
            double best = which == 1 ? da : db;
            if (double.IsNaN(best) || best > top * 0.12) return;
            RunClicked?.Invoke(which);
            return;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var text = PedalColours.Theme(this, "TextBrush");
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var room = PedalColours.Theme(this, "RoomBrush");
        var gridPen = PedalColours.Pen(dim, 1, 0.18);
        var lanes = Lanes();
        if (a == null || lanes.Count == 0)
        {
            var r = new Rect(Left, 4, PlotWidth, Math.Max(10, h - AxisHeight - 8));
            dc.DrawRectangle(room, null, r);
            dc.DrawText(PedalColours.Text(this, T("Pick something to compare"), 13, dim), new Point(r.Left + 12, r.Top + 12));
            return;
        }
        double speedTop = TopSpeed(), deltaRange = DeltaRange();
        var white = PedalColours.Theme(this, "TextBrush");
        foreach (var (c, area) in lanes)
        {
            dc.DrawRectangle(room, null, area);
            string name = c switch
            {
                Channel.Delta => T("Delta"),
                Channel.Speed => Speeds.Unit,
                Channel.Throttle => T("Throttle"),
                Channel.Brake => T("Brake"),
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
                DrawSeries(dc, a!, delta!, 0, Yd, PedalColours.Pen(Highlight == 0 ? PedalColours.Frozen(PedalColours.RunA) : white, 1.6), area);
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
                var order = Highlight == 1 ? new[] { 2, 1 } : new[] { 1, 2 };
                foreach (int which in order)
                {
                    var r = which == 1 ? a : b;
                    if (r == null || (which == 1 ? !ShowA : !ShowB)) continue;
                    bool hi = Highlight == which, faded = Highlight != 0 && !hi;
                    var colour = which == 1 ? PedalColours.RunA : PedalColours.RunB;
                    var pen = hi ? PedalColours.Pen(white, 2.2) : PedalColours.Pen(PedalColours.Frozen(colour), 1.5, faded ? 0.35 : 1);
                    DrawSeries(dc, r, Series(r.Data, c), r.From, Y, pen, area);
                }
            }
            dc.Pop();
        }

        // the axis
        var bottom = lanes[^1].Area.Bottom;
        double span = x1 - x0;
        double[] steps = ByDistance ? new double[] { 10, 20, 50, 100, 200, 250, 500, 1000, 2000, 5000 } : new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 };
        double step = steps.FirstOrDefault(s => span / s <= 10, steps[^1]);
        for (double x = Math.Ceiling(x0 / step) * step; x <= x1; x += step)
        {
            double px = Px(x);
            foreach (var (_, area) in lanes) dc.DrawLine(gridPen, new Point(px, area.Top), new Point(px, area.Bottom));
            string t = ByDistance ? (x >= 1000 ? $"{x / 1000:0.##} km" : $"{x:0} m") : SessionChart.Clock(x);
            dc.DrawText(PedalColours.Text(this, t, 10, dim), new Point(px - 12, bottom + 2));
        }

        // the readout
        if (hover is { } p && p.X >= Left && p.X <= Left + PlotWidth && dragFrom == null)
        {
            double x = XAt(p.X);
            dc.DrawLine(PedalColours.Pen(text, 1, 0.5), new Point(p.X, lanes[0].Area.Top), new Point(p.X, bottom));
            var lines = new List<string> { ByDistance ? $"{x:0} m" : SessionChart.Clock(x, true) };
            string Pair(Func<SessionData, double[]> of, Func<double, string> fmt)
            {
                string va = a != null && ShowA ? Fmt(a.ValueAt(of(a.Data), x)) : "", vb = b != null && ShowB ? Fmt(b.ValueAt(of(b.Data), x)) : "";
                return vb.Length == 0 ? va : va.Length == 0 ? vb : va + "  /  " + vb;
                string Fmt(double v) => double.IsNaN(v) ? "–" : fmt(v);
            }
            if (delta != null && !double.IsNaN(DeltaAt(x))) lines.Add(F("Delta {0:+0.00;-0.00;0.00} s", DeltaAt(x)));
            if (Has(Channel.Speed)) lines.Add(Pair(d => d.Speed, v => $"{Speeds.Of(v):0} {Speeds.Unit}"));
            lines.Add(T("Throttle") + " " + Pair(d => d.Throttle, v => $"{v * 100:0}%"));
            lines.Add(T("Brake") + " " + Pair(d => d.Brake, v => $"{v * 100:0}%"));
            lines.Add(T("Steering") + " " + Pair(d => d.Steer, v => $"{(v - 0.5) * 450:+0;-0;0}°"));
            var ft = PedalColours.Text(this, string.Join("\n", lines), 11, text);
            double bx = p.X + 12 + ft.Width + 12 > w ? p.X - 12 - ft.Width - 12 : p.X + 12;
            dc.DrawRoundedRectangle(PedalColours.Theme(this, "PopupBrush"), PedalColours.Pen(PedalColours.Theme(this, "ControlBorderBrush"), 1), new Rect(bx, lanes[0].Area.Top + 4, ft.Width + 12, ft.Height + 8), 4, 4);
            dc.DrawText(ft, new Point(bx + 6, lanes[0].Area.Top + 8));
        }
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
