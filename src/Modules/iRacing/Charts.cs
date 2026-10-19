using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// The colours and drawing every iRacing graph shares: a fixed order of eight series colours (each its own step for
/// the dark and the light theme, chosen to stay apart for colour-blind eyes), a single blue for one series and its
/// shades for heat maps, text and grid lines in the theme's own colours, and a readout box that follows the mouse.
/// </summary>
internal abstract class ChartBase : FrameworkElement
{
    private static readonly string[] DarkSlots = { "#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767" };
    private static readonly string[] LightSlots = { "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948" };
    private static readonly Dictionary<string, Brush> brushes = new();

    /// <summary>The categories' colours never change with what's shown.</summary>
    private static readonly string[] CategoryOrder = { "Sports Car", "Formula Car", "Oval", "Dirt Oval", "Dirt Road", "Road" };

    protected Point? Hover { get; private set; }

    protected ChartBase()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        MouseMove += (_, e) => { Hover = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (_, _) => { Hover = null; InvalidateVisual(); };
        ThemeManager.ThemeChanged += InvalidateVisual;
    }

    /// <summary>A series colour by its place in the order; -1 is the neutral grey (the rest, "other").</summary>
    public static Brush Slot(int i) => i < 0 ? Hex("#898781", ThemeManager.IsDark ? 0.75 : 0.6) : Hex((ThemeManager.IsDark ? DarkSlots : LightSlots)[i % 8]);

    public static int CategorySlot(string category)
    {
        int i = Array.FindIndex(CategoryOrder, c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : 6;
    }

    public static Brush Hex(string hex, double opacity = 1)
    {
        string key = hex + "/" + opacity.ToString("0.00", CultureInfo.InvariantCulture);
        if (brushes.TryGetValue(key, out var b)) return b;
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B));
        brush.Freeze();
        return brushes[key] = brush;
    }

    /// <summary>The one-series blue at a strength from 0 to 1 (heat maps).</summary>
    public static Brush Heat(double level) => level <= 0 ? Hex(ThemeManager.IsDark ? "#3a3a37" : "#e1e0d9") : Hex(ThemeManager.IsDark ? "#3987e5" : "#2a78d6", 0.22 + 0.78 * Math.Clamp(level, 0, 1));

    protected Brush TextBrush => TryFindResource("TextBrush") as Brush ?? Brushes.White;
    protected Brush MutedBrush => TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
    protected Brush GridBrush => Hex(ThemeManager.IsDark ? "#2c2c2a" : "#e1e0d9");
    protected Brush AxisBrush => Hex(ThemeManager.IsDark ? "#383835" : "#c3c2b7");
    protected Brush SurfaceBrush => TryFindResource("CardBrush") as Brush ?? Brushes.Black;

    protected Pen Line(Brush b, double thick = 1)
    {
        var p = new Pen(b, thick) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    protected FormattedText Text(string text, double size = 11, Brush? brush = null, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush ?? MutedBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Text cut to fit a width, with an ellipsis.</summary>
    protected FormattedText Fit(string text, double width, double size = 11, Brush? brush = null)
    {
        var t = Text(text, size, brush);
        if (t.Width <= width || text.Length < 3) return t;
        int lo = 1, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Text(text[..mid] + "…", size, brush).Width <= width) lo = mid; else hi = mid - 1;
        }
        return Text(text[..lo] + "…", size, brush);
    }

    /// <summary>A rectangle rounded at its data end only (the top of a column, the right of a bar).</summary>
    protected static Geometry Bar(Rect r, bool horizontal, double radius = 3)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            if (horizontal)
            {
                c.BeginFigure(new Point(r.Left, r.Top), true, true);
                c.LineTo(new Point(r.Right - radius, r.Top), true, false);
                c.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right, r.Bottom - radius), true, false);
                c.ArcTo(new Point(r.Right - radius, r.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Left, r.Bottom), true, false);
            }
            else
            {
                c.BeginFigure(new Point(r.Left, r.Bottom), true, true);
                c.LineTo(new Point(r.Left, r.Top + radius), true, false);
                c.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right - radius, r.Top), true, false);
                c.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right, r.Bottom), true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>The readout: a few lines in a box near the mouse, kept inside the graph.</summary>
    protected void Tip(DrawingContext dc, Point at, params string[] lines)
    {
        lines = lines.Where(l => !string.IsNullOrEmpty(l)).ToArray();
        if (lines.Length == 0) return;
        var texts = lines.Select((l, i) => Text(l, 12, TextBrush, i == 0)).ToList();
        double w = texts.Max(t => t.Width) + 16, h = texts.Sum(t => t.Height) + 10;
        double x = at.X + 14, y = at.Y + 14;
        if (x + w > ActualWidth) x = Math.Max(0, at.X - w - 10);
        if (y + h > ActualHeight) y = Math.Max(0, ActualHeight - h);
        var bg = TryFindResource("PopupBrush") as Brush ?? SurfaceBrush;
        dc.DrawRoundedRectangle(bg, Line(AxisBrush), new Rect(x, y, w, h), 5, 5);
        double ty = y + 5;
        foreach (var t in texts) { dc.DrawText(t, new Point(x + 8, ty)); ty += t.Height; }
    }

    protected static string N(double v) => v.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>A row of a bar list or a column of a column graph: its label, its value (or values, stacked), and its readout.</summary>
internal sealed record BarItem(string Label, double[] Values, string[] Tip, int Slot = 0, string? Shown = null)
{
    public BarItem(string label, double value, params string[] tip) : this(label, new[] { value }, tip) { }
    public double Total => Values.Sum();
}

/// <summary>Horizontal bars, a row each, the label on the left and the value at the end: the most laps at a track, say.</summary>
internal sealed class BarList : ChartBase
{
    private const double RowHeight = 24;
    private IReadOnlyList<BarItem> items = Array.Empty<BarItem>();
    private Func<double, string> format = v => N(v);
    private string[] legend = Array.Empty<string>();

    /// <summary>The rows; how a value reads; and, for stacked bars, each part's name (a legend above them).</summary>
    public void Show(IReadOnlyList<BarItem> items, Func<double, string>? format = null, string[]? legend = null)
    {
        this.items = items;
        if (format != null) this.format = format;
        this.legend = legend ?? Array.Empty<string>();
        Height = Math.Max(RowHeight, items.Count * RowHeight + 4) + Top;
        InvalidateVisual();
    }

    private double Top => legend.Length > 0 ? 22 : 0;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (items.Count == 0) { dc.DrawText(Text(T("Nothing to show yet.")), new Point(0, 4)); return; }
        double labelW = Math.Min(230, ActualWidth * 0.38), valueW = 64;
        double barW = Math.Max(10, ActualWidth - labelW - valueW - 8);
        double max = Math.Max(1e-9, items.Max(i => i.Total));
        double lx = labelW;
        for (int s = 0; s < legend.Length; s++)
        {
            dc.DrawRoundedRectangle(Slot(items[0].Slot + s), null, new Rect(lx, 4, 10, 10), 2, 2);
            var t = Text(legend[s], 11, TextBrush);
            dc.DrawText(t, new Point(lx + 14, 9 - t.Height / 2));
            lx += t.Width + 30;
        }
        int hoverRow = Hover is { } h && h.Y >= Top ? (int)((h.Y - Top) / RowHeight) : -1;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double y = Top + i * RowHeight;
            if (i == hoverRow) dc.DrawRectangle(TryFindResource("RowHoverBrush") as Brush, null, new Rect(0, y, ActualWidth, RowHeight));
            var label = Fit(item.Label, labelW - 10, 12, TextBrush);
            dc.DrawText(label, new Point(labelW - 10 - label.Width, y + (RowHeight - label.Height) / 2));
            double x = labelW;
            for (int s = 0; s < item.Values.Length; s++)
            {
                double w = item.Values[s] / max * barW;
                if (w <= 0) continue;
                bool last = item.Values.Skip(s + 1).All(v => v <= 0);
                var r = new Rect(x, y + 5, Math.Max(1, w - (last ? 0 : 2)), RowHeight - 10);
                if (last) dc.DrawGeometry(Slot(item.Slot + s), null, Bar(r, true));
                else dc.DrawRectangle(Slot(item.Slot + s), null, r);
                x += w;
            }
            var value = Text(item.Shown ?? format(item.Total), 12, TextBrush);
            dc.DrawText(value, new Point(x + 6, y + (RowHeight - value.Height) / 2));
        }
        if (Hover is { } p && hoverRow >= 0 && hoverRow < items.Count) Tip(dc, p, items[hoverRow].Tip);
    }
}

/// <summary>Columns along the bottom (each stacked from its values), with a light grid: races each month, places.</summary>
internal sealed class ColumnChart : ChartBase
{
    private IReadOnlyList<BarItem> items = Array.Empty<BarItem>();
    private string[] seriesNames = Array.Empty<string>();
    private int[] slots = { 0 };
    private const double Left = 40, Bottom = 22, Top = 22;

    /// <summary>The columns, and (stacked) each part's name and colour slot.</summary>
    public void Show(IReadOnlyList<BarItem> items, string[]? series = null, int[]? slots = null)
    {
        this.items = items;
        seriesNames = series ?? Array.Empty<string>();
        this.slots = slots ?? new[] { 0, 1, 2, 3 };
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (items.Count == 0) { dc.DrawText(Text(T("Nothing to show yet.")), new Point(0, 4)); return; }
        double top = seriesNames.Length > 1 ? Top : 6;
        var plot = new Rect(Left, top, Math.Max(10, ActualWidth - Left - 4), Math.Max(10, ActualHeight - top - Bottom));
        double step = Step(Math.Max(1, items.Max(i => i.Total)), whole: true);
        int lines = (int)Math.Ceiling(Math.Max(1, items.Max(i => i.Total)) / step);
        double max = step * lines;
        for (int g = 0; g <= lines; g++)
        {
            double y = plot.Bottom - plot.Height * g / lines;
            dc.DrawLine(Line(g == 0 ? AxisBrush : GridBrush), new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text(N(step * g));
            dc.DrawText(t, new Point(plot.Left - t.Width - 6, y - t.Height / 2));
        }
        // the legend (stacked), in the order the parts stack
        double lx = plot.Left;
        for (int s = 0; s < seriesNames.Length && seriesNames.Length > 1; s++)
        {
            dc.DrawRoundedRectangle(Slot(slots[s % slots.Length]), null, new Rect(lx, 4, 10, 10), 2, 2);
            var t = Text(seriesNames[s], 11, TextBrush);
            dc.DrawText(t, new Point(lx + 14, 9 - t.Height / 2));
            lx += t.Width + 30;
        }
        double slot = plot.Width / items.Count;
        double gap = Math.Min(Math.Max(1, slot * 0.25), 8);
        double colW = Math.Max(1, slot - gap);
        int hover = Hover is { } h && plot.Contains(new Point(h.X, Math.Clamp(h.Y, plot.Top, plot.Bottom))) ? (int)((h.X - plot.Left) / slot) : -1;
        // labels: as many as fit
        double labelW = items.Max(i => Text(i.Label).Width) + 8;
        int every = Math.Max(1, (int)Math.Ceiling(labelW / slot));
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double x = plot.Left + i * slot + gap / 2;
            if (i == hover) dc.DrawRectangle(TryFindResource("RowHoverBrush") as Brush, null, new Rect(plot.Left + i * slot, plot.Top, slot, plot.Height));
            double y = plot.Bottom;
            for (int s = 0; s < item.Values.Length; s++)
            {
                double hgt = item.Values[s] / max * plot.Height;
                if (hgt <= 0) continue;
                bool last = item.Values.Skip(s + 1).All(v => v <= 0);
                var r = new Rect(x, y - hgt, colW, Math.Max(0.5, hgt - (last ? 0 : Math.Min(2, hgt / 3))));
                var brush = item.Values.Length == 1 ? Slot(item.Slot) : Slot(slots[s % slots.Length]);
                if (last) dc.DrawGeometry(brush, null, Bar(r, false, Math.Min(3, colW / 2)));
                else dc.DrawRectangle(brush, null, r);
                y -= hgt;
            }
            if (i % every == 0)
            {
                var t = Text(item.Label);
                dc.DrawText(t, new Point(x + colW / 2 - t.Width / 2, plot.Bottom + 4));
            }
        }
        if (Hover is { } p && hover >= 0 && hover < items.Count) Tip(dc, p, items[hover].Tip);
    }

    /// <summary>A round step for about four grid lines up to <paramref name="span"/> (1, 2, 2.5 or 5 times a power of ten).</summary>
    public static double Step(double span, bool whole = false)
    {
        double raw = span / 4, mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = 10 * mag;
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * mag >= raw) { step = m * mag; break; }
        return whole ? Math.Max(1, Math.Round(step)) : step;
    }
}

/// <summary>Lines over time, one per series (iRating per category), a crosshair and readout at the mouse, each line
/// named at its end.</summary>
internal sealed class TimeChart : ChartBase
{
    public sealed record Series(string Name, IReadOnlyList<ChartPoint> Points, int Slot);

    private IReadOnlyList<Series> series = Array.Empty<Series>();
    private Func<double, string> format = v => N(v);
    private const double Left = 46, Bottom = 22, Right = 150, Top = 8;

    public void Show(IReadOnlyList<Series> series, Func<double, string>? format = null)
    {
        this.series = series.Where(s => s.Points.Count > 0).ToList();
        if (format != null) this.format = format;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (series.Count == 0) { dc.DrawText(Text(T("Nothing to show yet.")), new Point(0, 4)); return; }
        var plot = new Rect(Left, Top, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - Bottom));
        var all = series.SelectMany(s => s.Points).ToList();
        DateTime t0 = all.Min(p => p.When), t1 = all.Max(p => p.When);
        if (t1 <= t0) t1 = t0.AddDays(1);
        double lo = all.Min(p => p.Value), hi = all.Max(p => p.Value);
        double pad = Math.Max((hi - lo) * 0.05, Math.Abs(hi) * 0.01 + 0.01);
        double step = ColumnChart.Step(hi - lo + 2 * pad);
        lo = Math.Floor((lo - pad) / step) * step;
        hi = Math.Ceiling((hi + pad) / step) * step;
        int gridLines = Math.Max(1, (int)Math.Round((hi - lo) / step));
        double X(DateTime t) => plot.Left + (t - t0).TotalSeconds / (t1 - t0).TotalSeconds * plot.Width;
        double Y(double v) => plot.Bottom - (v - lo) / (hi - lo) * plot.Height;

        for (int g = 0; g <= gridLines; g++)
        {
            double v = lo + step * g, y = Y(v);
            dc.DrawLine(Line(g == 0 ? AxisBrush : GridBrush), new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text(format(v));
            dc.DrawText(t, new Point(plot.Left - t.Width - 6, y - t.Height / 2));
        }
        // years (or months) along the bottom
        bool years = (t1 - t0).TotalDays > 540;
        var tick = years ? new DateTime(t0.Year + 1, 1, 1) : new DateTime(t0.Year, t0.Month, 1).AddMonths(1);
        int monthStep = years ? 1 : Math.Max(1, (int)Math.Ceiling((t1 - t0).TotalDays / 30 / Math.Max(1, plot.Width / 70)));
        for (; tick < t1; tick = years ? tick.AddYears(1) : tick.AddMonths(monthStep))
        {
            double x = X(tick);
            dc.DrawLine(Line(GridBrush), new Point(x, plot.Top), new Point(x, plot.Bottom));
            var t = Text(years ? tick.Year.ToString() : tick.ToString("MMM yy"));
            dc.DrawText(t, new Point(x - t.Width / 2, plot.Bottom + 4));
        }

        var ends = new List<(double Y, Series S, double V)>();
        foreach (var s in series)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(X(s.Points[0].When), Y(s.Points[0].Value)), false, false);
                for (int i = 1; i < s.Points.Count; i++) c.LineTo(new Point(X(s.Points[i].When), Y(s.Points[i].Value)), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, Line(Slot(s.Slot), 2), g);
            var last = s.Points[^1];
            ends.Add((Y(last.Value), s, last.Value));
        }
        // the names at the ends, kept apart
        ends.Sort((a, b) => a.Y.CompareTo(b.Y));
        double prev = double.MinValue;
        foreach (var (y0, s, v) in ends)
        {
            double y = Math.Max(y0, prev + 14);
            prev = y;
            var t = Fit($"{s.Name} {format(v)}", Right - 22, 11, TextBrush);
            dc.DrawRoundedRectangle(Slot(s.Slot), null, new Rect(plot.Right + 6, y - 4, 8, 8), 2, 2);
            dc.DrawText(t, new Point(plot.Right + 18, y - t.Height / 2));
        }

        if (Hover is { } h && h.X >= plot.Left && h.X <= plot.Right)
        {
            var at = t0 + TimeSpan.FromSeconds((h.X - plot.Left) / plot.Width * (t1 - t0).TotalSeconds);
            dc.DrawLine(Line(AxisBrush), new Point(h.X, plot.Top), new Point(h.X, plot.Bottom));
            var lines = new List<string> { at.ToLocalTime().ToString("d") };
            foreach (var s in series)
            {
                var near = s.Points.LastOrDefault(p => p.When <= at);
                if (near == null) continue;
                double y = Y(near.Value);
                dc.DrawEllipse(Slot(s.Slot), Line(SurfaceBrush, 2), new Point(X(near.When), y), 4.5, 4.5);
                lines.Add($"{s.Name}: {format(near.Value)}");
            }
            Tip(dc, h, lines.ToArray());
        }
    }
}

/// <summary>A ring of parts with a legend beside it (races per category, how races ended).</summary>
internal sealed class Donut : ChartBase
{
    public sealed record Part(string Label, double Value, int Slot);

    private IReadOnlyList<Part> parts = Array.Empty<Part>();
    private string centre = "", centreLabel = "";

    public void Show(IReadOnlyList<Part> parts, string centre, string centreLabel)
    {
        this.parts = parts.Where(p => p.Value > 0).ToList();
        this.centre = centre;
        this.centreLabel = centreLabel;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double total = parts.Sum(p => p.Value);
        if (total <= 0) { dc.DrawText(Text(T("Nothing to show yet.")), new Point(0, 4)); return; }
        double r = Math.Min(ActualHeight, ActualWidth * 0.45) / 2 - 4, thick = r * 0.32;
        var c = new Point(r + 4, ActualHeight / 2);
        int hover = -1;
        if (Hover is { } h)
        {
            double dx = h.X - c.X, dy = h.Y - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= r && d >= r - thick)
            {
                double a = (Math.Atan2(dx, -dy) + 2 * Math.PI) % (2 * Math.PI) / (2 * Math.PI) * total, acc = 0;
                for (int i = 0; i < parts.Count; i++) { acc += parts[i].Value; if (a <= acc) { hover = i; break; } }
            }
        }
        double start = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            double sweep = parts[i].Value / total;
            var g = Arc(c, i == hover ? r + 3 : r, r - thick, start, start + sweep, parts.Count > 1 ? 0.012 : 0);
            dc.DrawGeometry(Slot(parts[i].Slot), null, g);
            start += sweep;
        }
        var big = Text(centre, 20, TextBrush, true);
        dc.DrawText(big, new Point(c.X - big.Width / 2, c.Y - big.Height / 2 - 6));
        var small = Text(centreLabel, 11);
        dc.DrawText(small, new Point(c.X - small.Width / 2, c.Y + big.Height / 2 - 6));

        // legend
        double lx = c.X + r + 18, ly = Math.Max(2, c.Y - parts.Count * 10);
        foreach (var p in parts)
        {
            dc.DrawRoundedRectangle(Slot(p.Slot), null, new Rect(lx, ly + 4, 10, 10), 2, 2);
            var t = Fit($"{p.Label}  {N(p.Value)} ({p.Value / total * 100:0}%)", Math.Max(20, ActualWidth - lx - 16), 12, TextBrush);
            dc.DrawText(t, new Point(lx + 16, ly + 9 - t.Height / 2));
            ly += 20;
        }
        if (Hover is { } hp && hover >= 0) Tip(dc, hp, parts[hover].Label, F("{0} ({1:0.0}%)", N(parts[hover].Value), parts[hover].Value / total * 100));
    }

    private static Geometry Arc(Point c, double r, double inner, double from, double to, double gap)
    {
        if (to - from >= 0.9999) { var ring = new GeometryGroup { FillRule = FillRule.EvenOdd }; ring.Children.Add(new EllipseGeometry(c, r, r)); ring.Children.Add(new EllipseGeometry(c, inner, inner)); ring.Freeze(); return ring; }
        from += gap / 2; to -= gap / 2;
        if (to <= from) to = from + 0.001;
        Point At(double f, double rad) => new(c.X + rad * Math.Sin(f * 2 * Math.PI), c.Y - rad * Math.Cos(f * 2 * Math.PI));
        bool large = to - from > 0.5;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(from, r), true, true);
            ctx.ArcTo(At(to, r), new Size(r, r), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(At(to, inner), true, false);
            ctx.ArcTo(At(from, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>A year of days as squares, a column a week (as GitHub's): the darker, the more races that day.</summary>
internal sealed class CalendarHeat : ChartBase
{
    private Dictionary<DateTime, int> days = new();
    private DateTime end = DateTime.Today;
    private const double Left = 30, Top = 16;

    public CalendarHeat() => Height = Top + 7 * 14 + 4;

    public void Show(Dictionary<DateTime, int> perDay, DateTime end)
    {
        days = perDay;
        this.end = end.Date;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        int weeks = Math.Max(10, (int)((ActualWidth - Left) / 14));
        double cell = Math.Min(14, (ActualWidth - Left) / weeks);
        var lastMonday = end.AddDays(-(((int)end.DayOfWeek + 6) % 7));
        var first = lastMonday.AddDays(-7 * (weeks - 1));
        int max = Math.Max(1, days.Where(d => d.Key >= first && d.Key <= end).Select(d => d.Value).DefaultIfEmpty(0).Max());
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        foreach (int d in new[] { 0, 2, 4 })
        {
            var t = Text(names[(d + 1) % 7], 10);
            dc.DrawText(t, new Point(0, Top + d * cell + (cell - t.Height) / 2));
        }
        (DateTime Day, int N)? hover = null;
        for (int w = 0; w < weeks; w++)
        {
            var monday = first.AddDays(7 * w);
            if (monday.Day <= 7)
                dc.DrawText(Text(monday.ToString("MMM"), 10), new Point(Left + w * cell, 0));
            for (int d = 0; d < 7; d++)
            {
                var day = monday.AddDays(d);
                if (day > end) break;
                int n = days.GetValueOrDefault(day);
                var r = new Rect(Left + w * cell, Top + d * cell, cell - 2, cell - 2);
                dc.DrawRoundedRectangle(Heat(n == 0 ? 0 : Math.Ceiling(4.0 * n / max) / 4), null, r, 2, 2);
                if (Hover is { } h && r.Contains(h)) hover = (day, n);
            }
        }
        if (Hover is { } p && hover is { } x) Tip(dc, p, x.Day.ToString("D"), P(x.N, "{0} race", "{0} races"));
    }
}

/// <summary>When you race: the days of the week down the side, the hours along the top, darker for more races.</summary>
internal sealed class WeekHeat : ChartBase
{
    private int[,] counts = new int[7, 24];
    private const double Left = 36, Top = 16;

    public WeekHeat() => Height = Top + 7 * 18 + 4;

    public void Show(int[,] week) { counts = week; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double cw = (ActualWidth - Left) / 24, ch = 18;
        int max = 1;
        foreach (int n in counts) max = Math.Max(max, n);
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        for (int h = 0; h < 24; h += 3)
            dc.DrawText(Text(new DateTime(2000, 1, 1, h, 0, 0).ToString("%H"), 10), new Point(Left + h * cw + 2, 0));
        (int D, int H, int N)? hover = null;
        for (int d = 0; d < 7; d++)
        {
            var t = Text(names[(d + 1) % 7], 10);
            dc.DrawText(t, new Point(0, Top + d * ch + (ch - t.Height) / 2));
            for (int h = 0; h < 24; h++)
            {
                var r = new Rect(Left + h * cw, Top + d * ch, Math.Max(1, cw - 2), ch - 2);
                int n = counts[d, h];
                dc.DrawRoundedRectangle(Heat(n == 0 ? 0 : (double)n / max), null, r, 2, 2);
                if (Hover is { } p && r.Contains(p)) hover = (d, h, n);
            }
        }
        if (Hover is { } hp && hover is { } x)
            Tip(dc, hp, $"{CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)((x.D + 1) % 7))} {new DateTime(2000, 1, 1, x.H, 0, 0):t}", P(x.N, "{0} race", "{0} races"));
    }
}
