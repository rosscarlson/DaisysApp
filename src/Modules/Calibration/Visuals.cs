using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Calibration;

/// <summary>Theme colours for the drawn controls (looked up at each draw, so Dark / Light follow).</summary>
internal static class Paint
{
    public static Brush Of(FrameworkElement e, string key, Brush fallback) => e.TryFindResource(key) as Brush ?? fallback;

    public static Brush Faded(Brush b, double opacity)
    {
        var c = b.Clone();
        c.Opacity = opacity;
        c.Freeze();
        return c;
    }
}

/// <summary>
/// A bar for one reading: filled up to <see cref="Value"/> (0 to 1), or with only a line there (<see cref="Fill"/>
/// false), with <see cref="Span"/> shaded and <see cref="Marks"/> as ticks (the calibration's min, centre and max).
/// </summary>
internal sealed class AxisBar : FrameworkElement
{
    private double value = double.NaN;
    private (double Low, double High)? span;
    private double[] marks = Array.Empty<double>();

    public bool Fill { get; init; } = true;

    public AxisBar()
    {
        Height = 12;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public double Value
    {
        get => value;
        set
        {
            if (double.IsNaN(value) ? double.IsNaN(this.value) : Math.Abs(value - this.value) < 0.002) return;
            this.value = value;
            InvalidateVisual();
        }
    }

    public (double Low, double High)? Span
    {
        get => span;
        set { if (value == span) return; span = value; InvalidateVisual(); }
    }

    public double[] Marks
    {
        get => marks;
        set { if (value.SequenceEqual(marks)) return; marks = value; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var accent = Paint.Of(this, "AccentBrush", Brushes.DodgerBlue);
        dc.DrawRoundedRectangle(Paint.Of(this, "ControlBrush", Brushes.DimGray), new Pen(Paint.Of(this, "ControlBorderBrush", Brushes.Gray), 1), new Rect(0, 0, w, h), 3, 3);
        if (span is { } s)
            dc.DrawRectangle(Paint.Faded(accent, 0.35), null, new Rect(X(s.Low, w), 1, Math.Max(1, X(s.High, w) - X(s.Low, w)), h - 2));
        if (!double.IsNaN(value))
        {
            if (Fill) dc.DrawRoundedRectangle(accent, null, new Rect(0, 0, X(value, w), h), 3, 3);
            else dc.DrawRectangle(Paint.Of(this, "TextBrush", Brushes.White), null, new Rect(Math.Clamp(X(value, w) - 1.5, 0, w - 3), 0, 3, h));
        }
        var pen = new Pen(Paint.Of(this, "TextSecondaryBrush", Brushes.Gray), 1);
        foreach (double m in marks)
        {
            double x = Math.Round(X(m, w)) + 0.5;
            dc.DrawLine(pen, new Point(x, -2), new Point(x, h + 2));
        }
    }

    private static double X(double v, double w) => Math.Clamp(v, 0, 1) * w;
}

/// <summary>
/// What a calibration does: raw readings across (the axis's whole range), what games get up (0 to 100%). The line
/// is flat to min, goes up through the centre (halfway) to max, and is flat after it; the dashed line is Windows'
/// default (the whole range), the dot is where the axis is now.
/// </summary>
internal sealed class CalGraph : FrameworkElement
{
    private Axis? axis;
    private Cal cal;
    private int? live;
    private (int Low, int High)? seen;

    public Axis? Axis { get => axis; set { axis = value; InvalidateVisual(); } }
    public Cal Cal { get => cal; set { if (value == cal) return; cal = value; InvalidateVisual(); } }

    /// <summary>The axis's reading now, or null (not connected).</summary>
    public int? Live
    {
        get => live;
        set { if (value == live) return; live = value; InvalidateVisual(); }
    }

    /// <summary>The wizard's lowest and highest readings, shaded.</summary>
    public (int Low, int High)? Seen { get => seen; set { if (value == seen) return; seen = value; InvalidateVisual(); } }

    public CalGraph()
    {
        Height = 220;
        MinWidth = 260;
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double pad = 8, bottom = 18;
        double w = ActualWidth - 2 * pad, h = ActualHeight - pad - bottom;
        if (w <= 0 || h <= 0 || axis == null) return;
        var a = axis;
        var accent = Paint.Of(this, "AccentBrush", Brushes.DodgerBlue);
        var textBrush = Paint.Of(this, "TextBrush", Brushes.White);
        var secondary = Paint.Of(this, "TextSecondaryBrush", Brushes.Gray);
        var grid = new Pen(Paint.Of(this, "ControlBorderBrush", Brushes.Gray), 1);
        double range = Math.Max(1, a.High - a.Low);
        Point P(double raw, double output) => new(pad + (raw - a.Low) / range * w, pad + (1 - output) * h);

        dc.DrawRectangle(Paint.Of(this, "ControlBrush", Brushes.Black), grid, new Rect(pad, pad, w, h));
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(pad + h * i / 4) + 0.5, x = Math.Round(pad + w * i / 4) + 0.5;
            dc.DrawLine(grid, new Point(pad, y), new Point(pad + w, y));
            dc.DrawLine(grid, new Point(x, pad), new Point(x, pad + h));
        }
        if (seen is { } s)
            dc.DrawRectangle(Paint.Faded(accent, 0.18), null, new Rect(P(s.Low, 1), P(s.High, 0)));
        dc.DrawLine(new Pen(secondary, 1) { DashStyle = DashStyles.Dash }, P(a.Low, 0), P(a.High, 1));
        if (cal.Valid)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(a.Low, 0), false, false);
                c.PolyLineTo(new[] { P(cal.Min, 0), P(cal.Center, 0.5), P(cal.Max, 1), P(a.High, 1) }, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, g);
            var tick = new Pen(secondary, 1);
            foreach (int v in new[] { cal.Min, cal.Center, cal.Max })
            {
                double x = P(v, 0).X;
                dc.DrawLine(tick, new Point(x, pad + h), new Point(x, pad + h + 4));
            }
            Label(dc, cal.Min.ToString(), P(cal.Min, 0).X, pad + h + 3, secondary, TextAlignment.Left);
            Label(dc, cal.Max.ToString(), P(cal.Max, 0).X, pad + h + 3, secondary, TextAlignment.Right);
        }
        if (live is int r)
        {
            double o = cal.Valid ? cal.Cook(r) : (r - a.Low) / range;
            var p = P(Math.Clamp(r, a.Low, a.High), o);
            var guide = new Pen(Paint.Faded(textBrush, 0.4), 1);
            dc.DrawLine(guide, new Point(p.X, pad + h), p);
            dc.DrawLine(guide, new Point(pad, p.Y), p);
            dc.DrawEllipse(textBrush, new Pen(accent, 2), p, 5, 5);
            Label(dc, $"{r} → {o * 100:0.0}%", pad + 6, pad + 4, textBrush, TextAlignment.Left);
        }
    }

    private void Label(DrawingContext dc, string text, double x, double y, Brush brush, TextAlignment align)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (align == TextAlignment.Right) x -= ft.Width;
        dc.DrawText(ft, new Point(Math.Clamp(x, 0, Math.Max(0, ActualWidth - ft.Width)), y));
    }
}
