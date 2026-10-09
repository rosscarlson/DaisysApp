using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming;

/// <summary>A simple line graph for the history: one or more series against evenly spaced points, with a legend.</summary>
public sealed class HistoryGraph : FrameworkElement
{
    public sealed record Series(string Name, Color Color, double[] Values, bool OwnScale = false, string Unit = "");

    private List<Series> series = new();
    private string leftLabel = "", rightLabel = "";

    public void Show(IEnumerable<Series> data, string firstLabel, string lastLabel)
    {
        series = data.ToList();
        leftLabel = firstLabel;
        rightLabel = lastLabel;
        InvalidateVisual();
    }

    public void Clear() => Show(Array.Empty<Series>(), "", "");

    protected override void OnRender(DrawingContext dc)
    {
        var text = (Brush?)TryFindResource("TextSecondaryBrush") ?? Brushes.Gray;
        var grid = (Brush?)TryFindResource("CardBorderBrush") ?? Brushes.DimGray;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Label(string s, Brush b) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, b, dpi);

        double w = ActualWidth, h = ActualHeight;
        const double left = 44, bottom = 18, top = 18;
        var plot = new Rect(left, top, Math.Max(10, w - left - 8), Math.Max(10, h - top - bottom));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (series.Count == 0 || series.All(s => s.Values.Length == 0)) return;

        // shared scale for the series without their own: 0 to the top value
        double max = series.Where(s => !s.OwnScale).SelectMany(s => s.Values).Where(double.IsFinite).DefaultIfEmpty(1).Max();
        max = Nice(max * 1.1);
        var gridPen = new Pen(grid, 1);
        gridPen.Freeze();
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Label((max * i / 4).ToString("0"), text);
            dc.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }
        var l1 = Label(leftLabel, text);
        dc.DrawText(l1, new Point(plot.Left, plot.Bottom + 2));
        var l2 = Label(rightLabel, text);
        dc.DrawText(l2, new Point(plot.Right - l2.Width, plot.Bottom + 2));

        double legendX = plot.Left;
        foreach (var s in series)
        {
            var brush = new SolidColorBrush(s.Color);
            brush.Freeze();
            double smax = s.OwnScale ? Nice(s.Values.Where(double.IsFinite).DefaultIfEmpty(1).Max() * 1.1) : max;
            var legend = Label(s.Name + (s.OwnScale ? $" (0–{smax:0}{s.Unit})" : ""), brush);
            dc.DrawText(legend, new Point(legendX, 0));
            legendX += legend.Width + 16;

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                int n = s.Values.Length;
                for (int i = 0; i < n; i++)
                {
                    double v = s.Values[i];
                    if (!double.IsFinite(v)) { started = false; continue; }
                    double x = plot.Left + (n == 1 ? plot.Width / 2 : plot.Width * i / (n - 1));
                    double y = plot.Bottom - plot.Height * Math.Clamp(v / smax, 0, 1);
                    if (!started) { g.BeginFigure(new Point(x, y), false, false); started = true; }
                    else g.LineTo(new Point(x, y), true, true);
                }
            }
            geo.Freeze();
            var pen = new Pen(brush, 1.5);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
            if (s.Values.Length <= 60)
                for (int i = 0; i < s.Values.Length; i++)
                {
                    double v = s.Values[i];
                    if (!double.IsFinite(v)) continue;
                    double x = plot.Left + (s.Values.Length == 1 ? plot.Width / 2 : plot.Width * i / (s.Values.Length - 1));
                    dc.DrawEllipse(brush, null, new Point(x, plot.Bottom - plot.Height * Math.Clamp(v / smax, 0, 1)), 2.5, 2.5);
                }
        }
    }

    private static double Nice(double v)
    {
        if (!double.IsFinite(v) || v <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
            if (v <= m * p) return m * p;
        return 10 * p;
    }
}
