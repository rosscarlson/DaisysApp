using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>The pedal colours, as the ForzaWheel overlay draws them.</summary>
internal static class PedalColours
{
    public static readonly Color Throttle = Color.FromRgb(0x3C, 0xD0, 0x70);
    public static readonly Color Brake = Color.FromRgb(0xFF, 0x4D, 0x4D);
    public static readonly Color Clutch = Color.FromRgb(0x4D, 0xA3, 0xFF);
    public static readonly Color Warning = Color.FromRgb(0xFF, 0xB1, 0x3B);

    public static Brush Frozen(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>A theme brush (it follows light and dark), or grey.</summary>
    public static Brush Theme(FrameworkElement e, string key) => e.TryFindResource(key) as Brush ?? Brushes.Gray;

    public static Pen Pen(Brush b, double thick, double opacity = 1)
    {
        var brush = opacity < 1 ? b.CloneCurrentValue() : b;
        if (opacity < 1) brush.Opacity = opacity;
        var p = new Pen(brush, thick) { LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    public static FormattedText Text(FrameworkElement e, string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(e).PixelsPerDip);
}

/// <summary>
/// A session's trace: throttle, brake (and steering, clutch) over time, the brake zones shaded and what's worth a look
/// marked at the top; under it the whole session, with the part shown boxed. The wheel zooms (about the mouse), dragging
/// moves along, a double-click shows it all; clicking a brake zone picks it, and the readout follows the mouse.
/// </summary>
internal sealed class SessionChart : FrameworkElement
{
    private PedalAnalysis? a;
    private double v0, v1; // the samples shown
    private Point? hover;
    private Point? dragFrom;
    private double dragV0;
    private bool dragged, overviewDrag;
    private const double OverviewHeight = 34, AxisHeight = 18, MarkerRow = 14;

    public bool ShowSteering { get; set; } = true;
    public bool ShowClutch { get; set; }
    public BrakeZone? Selected { get; private set; }

    /// <summary>A brake zone was clicked.</summary>
    public event Action<BrakeZone>? ZoneClicked;

    public PedalAnalysis? Analysis
    {
        get => a;
        set
        {
            a = value;
            Selected = null;
            v0 = 0;
            v1 = Math.Max(1, (a?.Data.Count ?? 1) - 1);
            // a long session opens on its first two minutes
            if (v1 > 120 * SessionData.Hz) v1 = 120 * SessionData.Hz;
            InvalidateVisual();
        }
    }

    public SessionChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        MouseMove += (_, e) =>
        {
            hover = e.GetPosition(this);
            if (dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed && a != null)
            {
                var plot = Plot();
                if (overviewDrag) CentreAt(OverviewSample(hover.Value.X));
                else
                {
                    double span = v1 - v0;
                    double shift = (from.X - hover.Value.X) / plot.Width * span;
                    if (Math.Abs(from.X - hover.Value.X) > 3) dragged = true;
                    SetView(dragV0 + shift, dragV0 + shift + span);
                }
            }
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { hover = null; InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (a == null) return;
            var p = e.GetPosition(this);
            if (e.ClickCount == 2) { SetView(0, a.Data.Count - 1); return; }
            dragFrom = p;
            dragV0 = v0;
            dragged = false;
            overviewDrag = p.Y >= ActualHeight - OverviewHeight;
            if (overviewDrag) CentreAt(OverviewSample(p.X));
            CaptureMouse();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            var p = e.GetPosition(this);
            if (dragFrom != null && !dragged && !overviewDrag && a != null)
            {
                double at = SampleAt(p.X);
                var z = a.Brakes.FirstOrDefault(z => at >= z.Start && at <= z.End);
                if (z != null) { Selected = z; ZoneClicked?.Invoke(z); InvalidateVisual(); }
            }
            dragFrom = null;
        };
        MouseWheel += (_, e) =>
        {
            if (a == null) return;
            double at = SampleAt(e.GetPosition(this).X);
            double f = e.Delta > 0 ? 0.8 : 1.25;
            SetView(at - (at - v0) * f, at + (v1 - at) * f);
            e.Handled = true;
        };
    }

    /// <summary>Shows a part (samples), with a little either side, and marks the zone if it's one.</summary>
    public void ShowRange(int from, int to, BrakeZone? zone = null)
    {
        Selected = zone;
        double pad = Math.Max(SessionData.Hz, (to - from) * 0.6);
        SetView(from - pad, to + pad);
    }

    private void CentreAt(double at)
    {
        double span = v1 - v0;
        SetView(at - span / 2, at + span / 2);
    }

    private void SetView(double from, double to)
    {
        if (a == null) return;
        double max = a.Data.Count - 1;
        double span = Math.Clamp(to - from, SessionData.Hz, Math.Max(SessionData.Hz, max));
        from = Math.Clamp(from, 0, Math.Max(0, max - span));
        v0 = from;
        v1 = from + span;
        InvalidateVisual();
    }

    private Rect Plot() => new(44, MarkerRow + 2, Math.Max(10, ActualWidth - 52), Math.Max(10, ActualHeight - OverviewHeight - AxisHeight - MarkerRow - 8));
    private double SampleAt(double x) { var p = Plot(); return v0 + (x - p.Left) / p.Width * (v1 - v0); }
    private double OverviewSample(double x) { var p = Plot(); return (x - p.Left) / p.Width * Math.Max(1, (a?.Data.Count ?? 1) - 1); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var text = PedalColours.Theme(this, "TextBrush");
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var gridPen = PedalColours.Pen(dim, 1, 0.18);
        var room = PedalColours.Theme(this, "RoomBrush");
        var plot = Plot();
        dc.DrawRectangle(room, null, plot);
        if (a == null || a.Data.Count < 2)
        {
            dc.DrawText(PedalColours.Text(this, T("Pick a session"), 13, dim), new Point(plot.Left + 12, plot.Top + 12));
            return;
        }
        var d = a.Data;
        double X(double i) => plot.Left + (i - v0) / (v1 - v0) * plot.Width;
        double Y(double v) => plot.Bottom - Math.Clamp(v, 0, 1) * plot.Height;

        // the grid: quarters, and a time line every so often
        for (int q = 0; q <= 4; q++)
        {
            double y = plot.Top + plot.Height * q / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(PedalColours.Text(this, (100 - q * 25) + "%", 10, dim), new Point(4, y - 7));
        }
        double spanS = (v1 - v0) * SessionData.Dt;
        double step = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200 }.FirstOrDefault(s => spanS / s <= 10, 1800);
        for (double t = Math.Ceiling(v0 * SessionData.Dt / step) * step; t <= v1 * SessionData.Dt; t += step)
        {
            double x = X(t * SessionData.Hz);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawText(PedalColours.Text(this, Clock(t), 10, dim), new Point(x - 12, plot.Bottom + 2));
        }

        dc.PushClip(new RectangleGeometry(plot));
        // the brake zones
        foreach (var z in a.Brakes)
        {
            if (z.End < v0 || z.Start > v1) continue;
            bool sel = z == Selected;
            var r = new Rect(new Point(X(z.Start), plot.Top), new Point(X(z.End), plot.Bottom));
            dc.DrawRectangle(PedalColours.Frozen(PedalColours.Brake, sel ? (byte)0x40 : (byte)0x16), sel ? PedalColours.Pen(PedalColours.Frozen(PedalColours.Brake), 1) : null, r);
            if (r.Width > 16) dc.DrawText(PedalColours.Text(this, z.Number.ToString(CultureInfo.CurrentCulture), 10, dim), new Point(r.Left + 3, plot.Top + 2));
        }
        double thick = 1.6;
        if (ShowSteering && a.HasSteer)
        {
            dc.DrawLine(PedalColours.Pen(dim, 1, 0.35), new Point(plot.Left, Y(0.5)), new Point(plot.Right, Y(0.5)));
            Line(dc, d.Steer, PedalColours.Pen(text, 1.2, 0.55), X, Y, plot);
        }
        if (ShowClutch) Line(dc, d.Clutch, PedalColours.Pen(PedalColours.Frozen(PedalColours.Clutch), thick), X, Y, plot);
        Line(dc, d.Throttle, PedalColours.Pen(PedalColours.Frozen(PedalColours.Throttle), thick), X, Y, plot);
        Line(dc, d.Brake, PedalColours.Pen(PedalColours.Frozen(PedalColours.Brake), thick), X, Y, plot);
        dc.Pop();

        // the markers, above the plot
        Marker? near = null;
        foreach (var m in a.Markers)
        {
            if (m.At < v0 || m.At > v1) continue;
            double x = X(m.At);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x - 5, 2), true, true);
                ctx.LineTo(new Point(x + 5, 2), true, false);
                ctx.LineTo(new Point(x, MarkerRow), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(MarkerBrush(m.Kind), null, g);
            if (hover is { } hp && Math.Abs(hp.X - x) < 8) near = m;
        }

        // the whole session, under it
        var ov = new Rect(plot.Left, h - OverviewHeight, plot.Width, OverviewHeight - 4);
        dc.DrawRectangle(room, null, ov);
        double OX(double i) => ov.Left + i / (d.Count - 1) * ov.Width;
        double OY(double v) => ov.Bottom - Math.Clamp(v, 0, 1) * ov.Height;
        Envelope(dc, d.Throttle, 0, d.Count - 1, PedalColours.Pen(PedalColours.Frozen(PedalColours.Throttle), 1, 0.7), OX, OY, ov);
        Envelope(dc, d.Brake, 0, d.Count - 1, PedalColours.Pen(PedalColours.Frozen(PedalColours.Brake), 1, 0.8), OX, OY, ov);
        var box = new Rect(new Point(OX(v0), ov.Top), new Point(Math.Max(OX(v0) + 2, OX(v1)), ov.Bottom));
        dc.DrawRectangle(PedalColours.Frozen(Colors.Gray, 0x30), PedalColours.Pen(PedalColours.Theme(this, "AccentBrush"), 1.5), box);

        // the readout
        if (hover is { } p && plot.Contains(p) && dragFrom == null)
        {
            int i = (int)Math.Round(SampleAt(p.X));
            if (i >= 0 && i < d.Count)
            {
                dc.DrawLine(PedalColours.Pen(text, 1, 0.5), new Point(p.X, plot.Top), new Point(p.X, plot.Bottom));
                var lines = new List<string> { Clock(i * SessionData.Dt, true) };
                if (!double.IsNaN(d.Throttle[i])) lines.Add(F("Throttle {0:0}%", d.Throttle[i] * 100));
                if (!double.IsNaN(d.Brake[i])) lines.Add(F("Brake {0:0}%", d.Brake[i] * 100));
                if (!double.IsNaN(d.Steer[i])) lines.Add(F("Steering {0:+0;-0;0}°", (d.Steer[i] - 0.5) * 450));
                if (ShowClutch && !double.IsNaN(d.Clutch[i])) lines.Add(F("Clutch {0:0}%", d.Clutch[i] * 100));
                if (near != null) lines.Add(near.Text);
                var ft = PedalColours.Text(this, string.Join("\n", lines), 11, text);
                double bx = p.X + 12 + ft.Width + 12 > plot.Right ? p.X - 12 - ft.Width - 12 : p.X + 12;
                var back = PedalColours.Theme(this, "PopupBrush");
                dc.DrawRoundedRectangle(back, PedalColours.Pen(PedalColours.Theme(this, "ControlBorderBrush"), 1), new Rect(bx, plot.Top + 6, ft.Width + 12, ft.Height + 8), 4, 4);
                dc.DrawText(ft, new Point(bx + 6, plot.Top + 10));
            }
        }
    }

    private static Brush MarkerBrush(MarkerKind k) => k switch
    {
        MarkerKind.SnapRelease or MarkerKind.Reapply or MarkerKind.Spike or MarkerKind.SlowApply => PedalColours.Frozen(PedalColours.Brake),
        MarkerKind.Overlap => PedalColours.Frozen(PedalColours.Warning),
        _ => PedalColours.Frozen(PedalColours.Throttle),
    };

    private void Line(DrawingContext dc, double[] x, Pen pen, Func<double, double> X, Func<double, double> Y, Rect plot)
    {
        int from = Math.Max(0, (int)Math.Floor(v0) - 1), to = Math.Min(x.Length - 1, (int)Math.Ceiling(v1) + 1);
        Envelope(dc, x, from, to, pen, X, Y, plot);
    }

    /// <summary>A series drawn as a line; with more than two samples to a pixel, as each pixel's low and high.</summary>
    internal static void Envelope(DrawingContext dc, double[] x, int from, int to, Pen pen, Func<double, double> X, Func<double, double> Y, Rect area)
    {
        if (to <= from) return;
        double perPixel = (to - from) / Math.Max(1, area.Width);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            if (perPixel <= 2)
            {
                for (int i = from; i <= to; i++)
                {
                    double v = x[i];
                    if (double.IsNaN(v)) { open = false; continue; }
                    var pt = new Point(X(i), Y(v));
                    if (!open) { ctx.BeginFigure(pt, false, false); open = true; }
                    else ctx.LineTo(pt, true, false);
                }
            }
            else
            {
                double i0 = from;
                for (int px = 0; px < (int)Math.Ceiling(area.Width); px++)
                {
                    int a = (int)i0, b = Math.Min(to, (int)(i0 + perPixel));
                    i0 += perPixel;
                    double lo = double.MaxValue, hi = double.MinValue;
                    for (int i = a; i <= b; i++) { double v = x[i]; if (double.IsNaN(v)) continue; lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                    if (lo > hi) { open = false; continue; }
                    double xx = X((a + b) / 2.0);
                    if (!open) { ctx.BeginFigure(new Point(xx, Y(lo)), false, false); open = true; }
                    else ctx.LineTo(new Point(xx, Y(lo)), true, false);
                    if (hi > lo) ctx.LineTo(new Point(xx, Y(hi)), true, false);
                }
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    /// <summary>"1:05" (or "1:05.25"), "1:02:05".</summary>
    public static string Clock(double seconds, bool fine = false)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        string s = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        return fine ? s + "." + (t.Milliseconds / 10).ToString("00", CultureInfo.InvariantCulture) : s;
    }
}
