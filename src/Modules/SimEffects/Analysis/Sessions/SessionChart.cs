using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>The pedal colours, as the ForzaWheel overlay draws them.</summary>
internal static class PedalColours
{
    public static readonly Color Throttle = Color.FromRgb(0x3C, 0xD0, 0x70);
    public static readonly Color Brake = Color.FromRgb(0xFF, 0x4D, 0x4D);
    public static readonly Color Clutch = Color.FromRgb(0x4D, 0xA3, 0xFF);
    public static readonly Color Warning = Color.FromRgb(0xFF, 0xB1, 0x3B);
    public static readonly Color Speed = Color.FromRgb(0x4D, 0xD0, 0xE1);
    public static readonly Color Impact = Color.FromRgb(0xB0, 0x7B, 0xFF);
    public static readonly Color Crash = Color.FromRgb(0xFF, 0x4D, 0xC4);
    /// <summary>The two runs of a comparison: this one, and the one compared with it.</summary>
    public static readonly Color RunA = Color.FromRgb(0x4D, 0xA3, 0xFF);
    public static readonly Color RunB = Color.FromRgb(0xFF, 0x9F, 0x2E);

    public static Brush Frozen(Color c, byte alpha = 0xFF) => Ui.Brush(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>A theme brush (it follows light and dark), or grey.</summary>
    public static Brush Theme(FrameworkElement e, string key) => e.TryFindResource(key) as Brush ?? Brushes.Gray;

    // (made once per brush, width and opacity: charts draw several a frame)
    public static Pen Pen(Brush b, double thick, double opacity = 1)
    {
        var key = (b, thick, opacity);
        lock (pens)
        {
            if (pens.TryGetValue(key, out var p)) return p;
            var brush = opacity < 1 ? b.CloneCurrentValue() : b;
            if (opacity < 1) brush.Opacity = opacity;
            p = new Pen(brush, thick) { LineJoin = PenLineJoin.Round };
            p.Freeze();
            if (pens.Count > 500) pens.Clear();
            return pens[key] = p;
        }
    }

    private static readonly Dictionary<(Brush, double, double), Pen> pens = new();

    public static FormattedText Text(FrameworkElement e, string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(e).PixelsPerDip);
}

/// <summary>The trace's drawing helpers (the Trace itself went into Compare's chart in 4.7.0): a series as a line or
/// its envelope, and times as a clock.</summary>
internal static class SessionChart
{
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
