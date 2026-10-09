using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Shared.Charts;

/// <summary>What a graph's values are: the unit for the axis, and how a value reads in the mouse readout.</summary>
public interface IGraphUnit
{
    string Unit { get; }
    string Text(double value);
}

/// <summary>One line on a <see cref="LineGraph"/>.</summary>
public sealed class GraphSeries
{
    public required string Name { get; init; }
    public required IReadOnlyList<(DateTime T, double V)> Points { get; init; }
    public required IGraphUnit Metric { get; init; }

    /// <summary>A theme brush key ("AccentBrush") or a colour ("#F7A541").</summary>
    public string Color { get; init; } = "AccentBrush";
    public bool Fill { get; init; } = true;
    public bool Faint { get; init; }
}

/// <summary>
/// A time graph drawn directly (no chart library): lines for each series, a soft fill under the first, light grid
/// lines, and — when <see cref="Detailed"/> — value and time labels plus a readout that follows the mouse. A gap in
/// the data (the PC was asleep, the app wasn't running) breaks the line instead of joining across it.
/// </summary>
public sealed class LineGraph : FrameworkElement
{
    private IReadOnlyList<GraphSeries> series = Array.Empty<GraphSeries>();
    private DateTime from, to;
    private double? fixedMax;
    private TimeSpan gap = TimeSpan.FromSeconds(5);
    private Point? hover;

    public bool Detailed { get; set; }

    /// <summary>How many spans the time axis of a detailed graph is split into (one label each side of each): fewer
    /// for a narrow graph.</summary>
    public int TimeSpans { get; set; } = 5;

    private string? highlight;

    /// <summary>Warning levels drawn as dashed lines (value, colour), when they're within the graph's range.</summary>
    public IReadOnlyList<(double Value, string Color)> Levels { get; set; } = Array.Empty<(double, string)>();

    /// <summary>The series drawn in the text colour (white in the dark theme) with the others dimmed; null = none.</summary>
    public string? Highlight
    {
        get => highlight;
        set { highlight = value; InvalidateVisual(); }
    }

    /// <summary>Raised when the value scale (left of a detailed graph) is clicked, if anything handles it: to set its top.</summary>
    public event Action? ScaleClicked;

    private const double AxisWidth = 48;

    public LineGraph()
    {
        SnapsToDevicePixels = true;
        MouseMove += (_, e) =>
        {
            if (!Detailed) return;
            hover = e.GetPosition(this);
            Cursor = ScaleClicked != null && hover.Value.X < AxisWidth ? Cursors.Hand : null;
            InvalidateVisual();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (Detailed && ScaleClicked != null && e.GetPosition(this).X < AxisWidth) { ScaleClicked(); e.Handled = true; }
        };
        MouseLeave += (_, _) => { hover = null; InvalidateVisual(); };
    }

    /// <summary>Sets what to draw. <paramref name="gapAfter"/>: points further apart than this aren't joined.</summary>
    public void Show(IReadOnlyList<GraphSeries> series, DateTime from, DateTime to, double? fixedMax, TimeSpan gapAfter)
    {
        this.series = series;
        this.from = from;
        this.to = to <= from ? from.AddSeconds(1) : to;
        this.fixedMax = fixedMax;
        gap = gapAfter;
        InvalidateVisual();
    }

    private Brush BrushFor(string color, double opacity = 1)
    {
        Brush b = color.StartsWith('#')
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            : TryFindResource(color) as Brush ?? Brushes.DodgerBlue;
        if (opacity < 1) { b = b.CloneCurrentValue(); b.Opacity = opacity; }
        return b;
    }

    private double Top()
    {
        if (fixedMax is double f) return f;
        double max = series.SelectMany(s => s.Points).Select(p => p.V).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        return NiceCeiling(Math.Max(max * 1.1, 1));
    }

    /// <summary>A round number at or above <paramref name="v"/>: 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 7 or 8 × a power of ten, so the top isn't far above the data (a 6.1 GHz clock tops out at 7, not 10).</summary>
    private static double NiceCeiling(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 10 }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textBrush = BrushFor("TextSecondaryBrush");
        var gridPen = new Pen(BrushFor("CardBorderBrush"), 1);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable everywhere, for the readout

        double left = Detailed ? AxisWidth : 0, bottom = Detailed ? 20 : 0;
        var plot = new Rect(left, 2, Math.Max(1, w - left - 2), Math.Max(1, h - bottom - 4));
        double top = Top();
        var unit = series.Count > 0 ? series[0].Metric : null;

        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (Detailed && unit != null)
                DrawText(dc, AxisText(unit, top * i / 4), new Point(plot.Left - 6, y), textBrush, dpi, alignRight: true, middle: true);
        }
        if (Detailed) DrawTimeLabels(dc, plot, textBrush, gridPen, dpi);

        foreach (var (value, color) in Levels)
        {
            if (value <= 0 || value > top) continue;
            double ly = plot.Bottom - plot.Height * value / top;
            dc.DrawLine(new Pen(BrushFor(color, 0.8), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, new Point(plot.Left, ly), new Point(plot.Right, ly));
        }

        double X(DateTime t) => plot.Left + plot.Width * ((t - from).TotalSeconds / (to - from).TotalSeconds);
        double Y(double v) => plot.Bottom - plot.Height * Math.Clamp(v / top, 0, 1);

        // the highlighted series (and its peak line) last, so it's on top
        bool IsLit(GraphSeries s) => highlight != null && (s.Name == highlight || s.Name == highlight + T(" peak"));
        // points outside the time span (e.g. older ones loaded from a log) stay inside the plot
        dc.PushClip(new RectangleGeometry(new Rect(plot.Left, plot.Top - 2, plot.Width, plot.Height + 4)));
        foreach (var s in series.Reverse().OrderBy(IsLit))
        {
            bool lit = IsLit(s), dimmed = highlight != null && !lit;
            var stroke = lit ? BrushFor("TextBrush", s.Faint ? 0.6 : 1) : BrushFor(s.Color, (s.Faint ? 0.45 : 1) * (dimmed ? 0.3 : 1));
            var pen = new Pen(stroke, lit && !s.Faint ? 2.2 : s.Faint ? 1 : 1.6) { LineJoin = PenLineJoin.Round };
            foreach (var run in Runs(s.Points))
            {
                if (run.Count == 0) continue;
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(new Point(X(run[0].T), Y(run[0].V)), false, false);
                    g.PolyLineTo(run.Skip(1).Select(p => new Point(X(p.T), Y(p.V))).ToList(), true, true);
                }
                line.Freeze();
                if ((s.Fill || lit) && !s.Faint && !dimmed)
                {
                    var area = new StreamGeometry();
                    using (var g = area.Open())
                    {
                        g.BeginFigure(new Point(X(run[0].T), plot.Bottom), true, true);
                        g.PolyLineTo(run.Select(p => new Point(X(p.T), Y(p.V))).Append(new Point(X(run[^1].T), plot.Bottom)).ToList(), false, true);
                    }
                    area.Freeze();
                    dc.DrawGeometry(lit ? BrushFor("TextBrush", 0.16) : BrushFor(s.Color, 0.18), null, area);
                }
                if (run.Count == 1) dc.DrawEllipse(stroke, null, new Point(X(run[0].T), Y(run[0].V)), 1.5, 1.5);
                else dc.DrawGeometry(null, pen, line);
            }
        }
        dc.Pop();

        if (Detailed && hover is Point m && plot.Contains(m)) DrawReadout(dc, plot, m, dpi, textBrush);
    }

    /// <summary>Axis values with the decimals they need (12.5 W, 0.25 MB), not the metric's usual rounding.</summary>
    private static string AxisText(IGraphUnit m, double v)
    {
        string n = v.ToString("#,0.##", CultureInfo.CurrentCulture);
        return m.Unit == "%" ? n + "%" : n + " " + m.Unit;
    }

    /// <summary>The points split where there's a gap in time (or a missing value).</summary>
    private IEnumerable<List<(DateTime T, double V)>> Runs(IReadOnlyList<(DateTime T, double V)> points)
    {
        var run = new List<(DateTime T, double V)>();
        DateTime? last = null;
        foreach (var p in points)
        {
            if (double.IsNaN(p.V) || (last is DateTime l && p.T - l > gap))
            {
                if (run.Count > 0) yield return run;
                run = new();
            }
            if (!double.IsNaN(p.V)) run.Add(p);
            last = p.T;
        }
        if (run.Count > 0) yield return run;
    }

    private void DrawTimeLabels(DrawingContext dc, Rect plot, Brush textBrush, Pen gridPen, double dpi)
    {
        var span = to - from;
        string format = span.TotalDays > 2 ? "ddd d MMM" : from.Date != to.AddTicks(-1).Date ? "ddd HH:mm" : span.TotalMinutes > 30 ? "HH:mm" : "HH:mm:ss";
        int n = Math.Max(1, TimeSpans);
        for (int i = 0; i <= n; i++)
        {
            double x = plot.Left + plot.Width * i / n;
            var t = from + TimeSpan.FromTicks(span.Ticks * i / n);
            if (i > 0 && i < n) dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var ft = Text(t.ToString(format, CultureInfo.CurrentCulture), textBrush, dpi);
            double tx = i == 0 ? x : i == n ? x - ft.Width : x - ft.Width / 2;
            dc.DrawText(ft, new Point(tx, plot.Bottom + 4));
        }
    }

    private void DrawReadout(DrawingContext dc, Rect plot, Point m, double dpi, Brush textBrush)
    {
        var t = from + TimeSpan.FromSeconds((m.X - plot.Left) / plot.Width * (to - from).TotalSeconds);
        dc.DrawLine(new Pen(textBrush, 1) { DashStyle = DashStyles.Dash }, new Point(m.X, plot.Top), new Point(m.X, plot.Bottom));
        var lines = new List<(string Text, Brush Brush)> { (t.ToString((to - from).TotalDays > 1 ? "ddd d MMM HH:mm" : "HH:mm:ss", CultureInfo.CurrentCulture), BrushFor("TextBrush")) };
        foreach (var s in series)
        {
            var nearest = s.Points.Count == 0 ? default : s.Points.MinBy(p => Math.Abs((p.T - t).Ticks));
            if (s.Points.Count == 0 || (nearest.T - t).Duration() > gap) continue;
            lines.Add(($"{s.Name}: {s.Metric.Text(nearest.V)}", BrushFor(s.Color, s.Faint ? 0.7 : 1)));
        }
        var texts = lines.Select(l => Text(l.Text, l.Brush, dpi)).ToList();
        double bw = texts.Max(x => x.Width) + 16, bh = texts.Sum(x => x.Height) + 10;
        double bx = m.X + 12 + bw > plot.Right ? m.X - 12 - bw : m.X + 12;
        var box = new Rect(bx, plot.Top + 6, bw, bh);
        dc.DrawRoundedRectangle(BrushFor("PopupBrush"), new Pen(BrushFor("CardBorderBrush"), 1), box, 6, 6);
        double y = box.Top + 5;
        foreach (var ft in texts) { dc.DrawText(ft, new Point(box.Left + 8, y)); y += ft.Height; }
    }

    private void DrawText(DrawingContext dc, string text, Point at, Brush brush, double dpi, bool alignRight = false, bool middle = false)
    {
        var ft = Text(text, brush, dpi);
        dc.DrawText(ft, new Point(alignRight ? at.X - ft.Width : at.X, middle ? at.Y - ft.Height / 2 : at.Y));
    }

    private static FormattedText Text(string text, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, dpi);
}
