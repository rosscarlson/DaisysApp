using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>Colours DiskMark draws reads, writes and mixed runs in.</summary>
internal static class Ink
{
    public const string Write = "#F7A541", Mix = "#B48CF2";

    public static Brush Of(FrameworkElement on, IoKind k) => k switch
    {
        IoKind.Write => Hex(Write),
        IoKind.Mix => Hex(Mix),
        _ => on.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
    };

    public static Color ColorOf(FrameworkElement on, IoKind k) => Of(on, k) is SolidColorBrush s ? s.Color : Colors.DodgerBlue;

    public static SolidColorBrush Hex(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    public static Brush Theme(FrameworkElement on, string key) => on.TryFindResource(key) as Brush ?? Brushes.Gray;

    public static FormattedText Text(FrameworkElement on, string s, double size, Brush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(on).PixelsPerDip);
}

/// <summary>
/// The live speed: an arc on a logarithmic scale from 1 MB/s to 20 GB/s (a random 4K read and a sequential one both
/// show), in the colour of what's measured, the number in the middle and the operations a second under it.
/// </summary>
internal sealed class Gauge : FrameworkElement
{
    private const double Low = 1e6, High = 2e10, Sweep = 260;
    private double shown; // bytes a second, eased towards the target
    public double Target { get; set; }
    public double Iops { get; set; }
    public IoKind Kind { get; set; }
    public bool Idle { get; set; } = true;
    public string Caption { get; set; } = "";
    public Func<double, (string Number, string Unit)> Format { get; set; } = v => ((v / 1e9).ToString("0.00"), "GB/s");

    public void Step()
    {
        shown += (Target - shown) * 0.18;
        if (Math.Abs(Target - shown) < 1) shown = Target;
        InvalidateVisual();
    }

    private static double Fraction(double v) => v <= Low ? 0 : Math.Clamp(Math.Log(v / Low) / Math.Log(High / Low), 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;
        double r = Math.Min(w / 2, h / 1.75) - 10;
        var c = new Point(w / 2, r + 10);
        var track = new Pen(Ink.Theme(this, "ControlBrush"), 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double start = 90 + (360 - Sweep) / 2; // degrees, clockwise from +x (screen coordinates)
        dc.DrawGeometry(null, track, Arc(c, r, start, Sweep));

        var secondary = Ink.Theme(this, "TextSecondaryBrush");
        // ticks at each power of ten
        for (double v = Low; v <= High * 1.01; v *= 10)
        {
            double a = (start + Sweep * Fraction(v)) * Math.PI / 180;
            var p1 = new Point(c.X + Math.Cos(a) * (r - 12), c.Y + Math.Sin(a) * (r - 12));
            var p2 = new Point(c.X + Math.Cos(a) * (r - 18), c.Y + Math.Sin(a) * (r - 18));
            dc.DrawLine(new Pen(secondary, 1), p1, p2);
            string label = v >= 1e9 ? $"{v / 1e9:0}G" : $"{v / 1e6:0}M";
            var t = Ink.Text(this, label, 9.5, secondary);
            var lp = new Point(c.X + Math.Cos(a) * (r - 34) - t.Width / 2, c.Y + Math.Sin(a) * (r - 34) - t.Height / 2);
            dc.DrawText(t, lp);
        }

        var color = Ink.ColorOf(this, Kind);
        double f = Idle ? 0 : Fraction(shown);
        if (f > 0.002)
        {
            var brush = new LinearGradientBrush(Color.FromArgb(110, color.R, color.G, color.B), color, new Point(0, 1), new Point(1, 0));
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)), 24) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var arc = Arc(c, r, start, Sweep * f);
            dc.DrawGeometry(null, glow, arc);
            dc.DrawGeometry(null, new Pen(brush, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            double a = (start + Sweep * f) * Math.PI / 180;
            dc.DrawEllipse(Brushes.White, null, new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r), 4, 4);
        }

        var text = Ink.Theme(this, "TextBrush");
        var (num, unit) = Idle ? ("—", "") : Format(shown);
        var big = Ink.Text(this, num, 30, text, bold: true);
        dc.DrawText(big, new Point(c.X - big.Width / 2, c.Y - big.Height / 2 - 6));
        var u = Ink.Text(this, unit, 12, secondary);
        dc.DrawText(u, new Point(c.X - u.Width / 2, c.Y + big.Height / 2 - 6));
        if (!Idle && Iops > 0)
        {
            var io = Ink.Text(this, F("{0:#,0} IOPS", Iops), 11, secondary);
            dc.DrawText(io, new Point(c.X - io.Width / 2, c.Y + big.Height / 2 + 10));
        }
        if (Caption.Length > 0)
        {
            var cap = Ink.Text(this, Caption, 12, Ink.Of(this, Kind), bold: true);
            dc.DrawText(cap, new Point(c.X - cap.Width / 2, Math.Min(h - cap.Height - 2, c.Y + r * 0.78)));
        }
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        sweepDeg = Math.Min(sweepDeg, 359.9);
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r), false, false);
            ctx.ArcTo(new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>
/// The test file as a grid of cells, start top left: each request lights the cell it lands in, fading after. A
/// sequential test draws a band sweeping through each thread's part of the file, a random one sparkles everywhere
/// (densely at a deep queue); the file's writing fills it in first.
/// </summary>
internal sealed class FileMap : FrameworkElement
{
    private const int Columns = 64, Rows = Bench.HeatCells / Columns;
    private readonly double[] glow = new double[Bench.HeatCells];
    private int[]? heat;
    private double prepared;
    public IoKind Kind { get; set; }
    public bool Preparing { get; set; }

    public void Reset()
    {
        Array.Clear(glow);
        prepared = 0;
        InvalidateVisual();
    }

    /// <summary>Takes the requests sent since last time from <paramref name="live"/> and fades the rest.</summary>
    public void Step(Bench.Live? live, double preparedFraction)
    {
        prepared = preparedFraction;
        if (live != null && !ReferenceEquals(heat, live.Heat)) heat = live.Heat;
        if (heat != null)
        {
            int max = 1;
            var taken = new int[heat.Length];
            for (int i = 0; i < heat.Length; i++) { taken[i] = Interlocked.Exchange(ref heat[i], 0); if (taken[i] > max) max = taken[i]; }
            for (int i = 0; i < glow.Length; i++)
            {
                double v = taken[i] == 0 ? 0 : 0.45 + 0.55 * Math.Sqrt((double)taken[i] / max);
                glow[i] = Math.Max(glow[i] * 0.86, v);
            }
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 10) return;
        double gap = 2, cw = (w - gap * (Columns - 1)) / Columns, ch = (h - gap * (Rows - 1)) / Rows;
        if (cw <= 0 || ch <= 0) return;
        var empty = Ink.Theme(this, "ControlBrush");
        var color = Ink.ColorOf(this, Kind);
        var written = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B));
        written.Freeze();
        int filled = Preparing ? (int)(prepared * Bench.HeatCells) : Bench.HeatCells;
        var brushes = new SolidColorBrush[11];
        for (int i = 0; i < Bench.HeatCells; i++)
        {
            int col = i % Columns, row = i / Columns;
            var rect = new Rect(col * (cw + gap), row * (ch + gap), cw, ch);
            Brush fill = i < filled ? written : empty;
            dc.DrawRoundedRectangle(fill, null, rect, 1.5, 1.5);
            double g = glow[i];
            if (g > 0.03)
            {
                int k = (int)Math.Round(g * 10);
                var b = brushes[k] ??= Freeze(new SolidColorBrush(Color.FromArgb((byte)(25 * k), Lift(color.R, k), Lift(color.G, k), Lift(color.B, k))));
                dc.DrawRoundedRectangle(b, null, rect, 1.5, 1.5);
            }
        }

        static byte Lift(byte c, int k) => (byte)(c + (255 - c) * Math.Max(0, k - 7) / 6);
        static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
