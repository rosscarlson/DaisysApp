using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>
/// Every brake zone (or throttle application) of a session drawn over each other, lined up at their start (or, for the
/// brake, at the start of the release), so their shapes can be compared: faint lines for each, the average dashed, the
/// picked one bold. Clicking picks the line nearest the mouse.
/// </summary>
internal sealed class OverlayChart : FrameworkElement
{
    /// <summary>One line: where it's lined up (sample), and what it is.</summary>
    public sealed record Curve(int Align, int End, object Item);

    private double[]? values;
    private IReadOnlyList<Curve> curves = Array.Empty<Curve>();
    private Color colour = PedalColours.Brake;
    private double before, after; // seconds shown either side of the alignment

    public object? Selected { get; set; }

    /// <summary>A line was clicked: its item.</summary>
    public event Action<object>? Picked;

    public void Show(double[]? values, IReadOnlyList<Curve> curves, Color colour, double before, double after)
    {
        this.values = values;
        this.curves = curves;
        this.colour = colour;
        this.before = before;
        this.after = after;
        InvalidateVisual();
    }

    public OverlayChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (values == null || curves.Count == 0) return;
            var p = e.GetPosition(this);
            var plot = Plot();
            double t = -before + (p.X - plot.Left) / plot.Width * (before + after);
            double want = (plot.Bottom - p.Y) / plot.Height;
            Curve? best = null;
            double bestD = 0.08;
            foreach (var c in curves)
            {
                int i = c.Align + (int)Math.Round(t * SessionData.Hz);
                if (i < 0 || i >= values.Length || i > c.End + SessionData.Hz / 5) continue;
                double dist = Math.Abs(values[i] - want);
                if (dist < bestD) { bestD = dist; best = c; }
            }
            if (best != null) { Selected = best.Item; InvalidateVisual(); Picked?.Invoke(best.Item); }
        };
    }

    private Rect Plot() => new(36, 6, Math.Max(10, ActualWidth - 42), Math.Max(10, ActualHeight - 26));

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot();
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var grid = PedalColours.Pen(dim, 1, 0.18);
        dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, plot);
        for (int q = 0; q <= 4; q++)
        {
            double y = plot.Top + plot.Height * q / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(PedalColours.Text(this, (100 - q * 25) + "%", 10, dim), new Point(2, y - 7));
        }
        double span = before + after;
        if (span <= 0) return;
        double X(double i) => plot.Left + (i * SessionData.Dt + before) / span * plot.Width;
        double Y(double v) => plot.Bottom - Math.Clamp(v, 0, 1) * plot.Height;
        for (double t = -Math.Floor(before * 2) / 2; t <= after + 0.001; t += 0.5)
        {
            double x = X(t * SessionData.Hz);
            dc.DrawLine(t == 0 ? PedalColours.Pen(dim, 1, 0.6) : grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawText(PedalColours.Text(this, (Math.Abs(t) < 0.001 ? 0 : t).ToString("0.0") + " s", 10, dim), new Point(x - 12, plot.Bottom + 3));
        }
        if (values == null || curves.Count == 0)
        {
            dc.DrawText(PedalColours.Text(this, T("Nothing to compare yet"), 12, dim), new Point(plot.Left + 10, plot.Top + 10));
            return;
        }
        int from = -(int)(before * SessionData.Hz), to = (int)(after * SessionData.Hz);
        var faint = PedalColours.Pen(PedalColours.Frozen(colour), 1.2, curves.Count > 20 ? 0.18 : 0.3);
        dc.PushClip(new RectangleGeometry(plot));
        var sum = new double[to - from + 1];
        var count = new int[to - from + 1];
        Curve? sel = null;
        foreach (var c in curves)
        {
            if (Equals(c.Item, Selected)) sel = c;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool open = false;
                for (int k = from; k <= to; k++)
                {
                    int i = c.Align + k;
                    if (i < 0 || i >= values.Length || double.IsNaN(values[i])) { open = false; continue; }
                    if (k > 0 && i > c.End + SessionData.Hz / 5) break;
                    var pt = new Point(X(k), Y(values[i]));
                    if (!open) { ctx.BeginFigure(pt, false, false); open = true; } else ctx.LineTo(pt, true, false);
                    sum[k - from] += values[i];
                    count[k - from]++;
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, faint, g);
        }
        // the average, where at least half of them still go
        var avg = new StreamGeometry();
        using (var ctx = avg.Open())
        {
            bool open = false;
            for (int k = from; k <= to; k++)
            {
                if (count[k - from] * 2 < curves.Count) { open = false; continue; }
                var pt = new Point(X(k), Y(sum[k - from] / count[k - from]));
                if (!open) { ctx.BeginFigure(pt, false, false); open = true; } else ctx.LineTo(pt, true, false);
            }
        }
        avg.Freeze();
        var avgPen = new Pen(PedalColours.Theme(this, "TextBrush"), 1.5) { DashStyle = DashStyles.Dash };
        avgPen.Freeze();
        dc.DrawGeometry(null, avgPen, avg);
        if (sel != null)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool open = false;
                for (int k = from; k <= to; k++)
                {
                    int i = sel.Align + k;
                    if (i < 0 || i >= values.Length || double.IsNaN(values[i])) { open = false; continue; }
                    if (k > 0 && i > sel.End + SessionData.Hz / 5) break;
                    var pt = new Point(X(k), Y(values[i]));
                    if (!open) { ctx.BeginFigure(pt, false, false); open = true; } else ctx.LineTo(pt, true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, PedalColours.Pen(PedalColours.Theme(this, "AccentBrush"), 2.5), g);
        }
        dc.Pop();
    }
}
