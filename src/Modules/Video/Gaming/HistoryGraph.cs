using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// A simple line graph for the history: one or more series against evenly spaced points, with a legend. Hovering shows
/// the nearest point's values (and its label, e.g. when it was) in a box beside the cursor.
/// </summary>
public sealed class HistoryGraph : FrameworkElement
{
    public sealed record Series(string Name, Color Color, double[] Values, bool OwnScale = false, string Unit = "");

    private List<Series> series = new();
    private string leftLabel = "", rightLabel = "";
    private IReadOnlyList<string>? pointLabels;
    private Point? hover;

    public HistoryGraph()
    {
        MouseMove += (_, e) => { hover = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (_, _) => { hover = null; InvalidateVisual(); };
    }

    /// <param name="labels">What each point is (a time, a session's date), for the hover box; null for none.</param>
    public void Show(IEnumerable<Series> data, string firstLabel, string lastLabel, IReadOnlyList<string>? labels = null)
    {
        series = data.ToList();
        leftLabel = firstLabel;
        rightLabel = lastLabel;
        pointLabels = labels;
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
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable everywhere, for the hover box
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

        double X(int i, int n) => plot.Left + (n == 1 ? plot.Width / 2 : plot.Width * i / (n - 1));
        double ScaleOf(Series s) => s.OwnScale ? Nice(s.Values.Where(double.IsFinite).DefaultIfEmpty(1).Max() * 1.1) : max;
        double Y(Series s, double v) => plot.Bottom - plot.Height * Math.Clamp(v / ScaleOf(s), 0, 1);

        double legendX = plot.Left;
        foreach (var s in series)
        {
            var brush = new SolidColorBrush(s.Color);
            brush.Freeze();
            double smax = ScaleOf(s);
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
                    var p = new Point(X(i, n), Y(s, v));
                    if (!started) { g.BeginFigure(p, false, false); started = true; }
                    else g.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            var pen = new Pen(brush, 1.5);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
            if (s.Values.Length <= 60)
                for (int i = 0; i < s.Values.Length; i++)
                    if (double.IsFinite(s.Values[i])) dc.DrawEllipse(brush, null, new Point(X(i, s.Values.Length), Y(s, s.Values[i])), 2.5, 2.5);
        }

        if (hover is Point m && m.X >= plot.Left - 4 && m.X <= plot.Right + 4) DrawHover(dc, plot, m, X, Y, Label, text);
    }

    /// <summary>The nearest point's values: a line through it, its dots marked, and a box with each series' value.</summary>
    private void DrawHover(DrawingContext dc, Rect plot, Point m, Func<int, int, double> X, Func<Series, double, double> Y,
        Func<string, Brush, FormattedText> Label, Brush text)
    {
        int n = series.Max(s => s.Values.Length);
        if (n == 0) return;
        int i = n == 1 ? 0 : (int)Math.Round(Math.Clamp((m.X - plot.Left) / plot.Width, 0, 1) * (n - 1));
        double x = X(i, n);
        dc.DrawLine(new Pen(text, 1) { DashStyle = DashStyles.Dash }, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var lines = new List<FormattedText>();
        var textBrush = (Brush?)TryFindResource("TextBrush") ?? Brushes.White;
        if (pointLabels != null && i < pointLabels.Count && pointLabels[i].Length > 0) lines.Add(Label(pointLabels[i], textBrush));
        foreach (var s in series)
        {
            if (i >= s.Values.Length || !double.IsFinite(s.Values[i])) continue;
            var brush = new SolidColorBrush(s.Color);
            brush.Freeze();
            double v = s.Values[i];
            dc.DrawEllipse(brush, new Pen(textBrush, 1), new Point(x, Y(s, v)), 4, 4);
            // ("GPU %: 2%" reads as "GPU: 2%")
            string name = s.Unit.Length > 0 && s.Name.EndsWith(s.Unit, StringComparison.Ordinal) ? s.Name[..^s.Unit.Length].TrimEnd() : s.Name;
            lines.Add(Label($"{name}: {v.ToString(v < 10 ? "0.#" : "0", CultureInfo.CurrentCulture)}{s.Unit}", brush));
        }
        if (lines.Count == 0) return;
        double bw = lines.Max(l => l.Width) + 16, bh = lines.Sum(l => l.Height) + 10;
        double bx = x + 12 + bw > plot.Right ? x - 12 - bw : x + 12;
        var box = new Rect(Math.Max(0, bx), plot.Top + 4, bw, bh);
        dc.DrawRoundedRectangle((Brush?)TryFindResource("PopupBrush") ?? Brushes.Black, new Pen((Brush?)TryFindResource("CardBorderBrush") ?? Brushes.Gray, 1), box, 6, 6);
        double y = box.Top + 5;
        foreach (var l in lines) { dc.DrawText(l, new Point(box.Left + 8, y)); y += l.Height; }
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
