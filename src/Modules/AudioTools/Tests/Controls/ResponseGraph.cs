using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Applets.AudioTools.Tests.Eq;

namespace DaisysApp.Applets.AudioTools.Tests.Controls;

/// <summary>One line on the <see cref="ResponseGraph"/>.</summary>
public sealed record GraphCurve(double[] Db, string Brush, double Thickness = 1.6, bool Dashed = false, double Opacity = 1);

/// <summary>Frequency responses on a log axis (20 Hz–20 kHz) in dB, with the corrected range highlighted.</summary>
public sealed class ResponseGraph : FrameworkElement
{
    private static readonly double[] Ticks = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
    private IReadOnlyList<GraphCurve> curves = [];
    private double from = 20, to = 20000;

    public void Show(IReadOnlyList<GraphCurve> curves, double from, double to)
    {
        this.curves = curves;
        this.from = from;
        this.to = to;
        InvalidateVisual();
    }

    private Brush BrushFor(string key, double opacity = 1)
    {
        Brush b = key.StartsWith('#')
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(key))
            : TryFindResource(key) as Brush ?? Brushes.Gray;
        if (opacity < 1) { b = b.CloneCurrentValue(); b.Opacity = opacity; }
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = BrushFor("TextSecondaryBrush");
        var gridPen = new Pen(BrushFor("CardBorderBrush"), 1);
        var plot = new Rect(40, 6, w - 48, h - 28);

        // dB range: ±15, wider if something doesn't fit
        var values = curves.SelectMany(c => c.Db).Where(v => !double.IsNaN(v)).ToList();
        double top = Math.Max(15, Math.Ceiling((values.Count > 0 ? values.Max() : 0) / 5) * 5);
        double bottom = Math.Min(-15, Math.Floor((values.Count > 0 ? values.Min() : 0) / 5) * 5);
        top = Math.Min(top, 30);
        bottom = Math.Max(bottom, -40);

        double X(double f) => plot.Left + plot.Width * Math.Log(f / 20) / Math.Log(1000);
        double Y(double db) => plot.Top + plot.Height * (top - Math.Clamp(db, bottom, top)) / (top - bottom);

        // the range the EQ corrects
        dc.DrawRectangle(BrushFor("AccentBrush", 0.07), null, new Rect(new Point(X(Math.Max(from, 20)), plot.Top), new Point(X(Math.Min(to, 20000)), plot.Bottom)));

        double step = top - bottom > 40 ? 10 : 5;
        for (double db = Math.Ceiling(bottom / step) * step; db <= top; db += step)
        {
            var pen = db == 0 ? new Pen(BrushFor("ControlBorderStrongBrush"), 1) : gridPen;
            dc.DrawLine(pen, new Point(plot.Left, Y(db)), new Point(plot.Right, Y(db)));
            DrawText(dc, db.ToString("+0;−0;0", CultureInfo.CurrentCulture), new Point(plot.Left - 6, Y(db)), text, dpi, right: true);
        }
        foreach (double f in Ticks)
        {
            dc.DrawLine(gridPen, new Point(X(f), plot.Top), new Point(X(f), plot.Bottom));
            DrawText(dc, f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}", new Point(X(f), plot.Bottom + 10), text, dpi, centre: true);
        }

        var grid = Response.Grid;
        foreach (var c in curves)
        {
            var pen = new Pen(BrushFor(c.Brush, c.Opacity), c.Thickness) { LineJoin = PenLineJoin.Round };
            if (c.Dashed) pen.DashStyle = new DashStyle([4, 3], 0);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                bool open = false;
                for (int i = 0; i < grid.Length && i < c.Db.Length; i++)
                {
                    if (double.IsNaN(c.Db[i])) { open = false; continue; }
                    var p = new Point(X(grid[i]), Y(c.Db[i]));
                    if (!open) { g.BeginFigure(p, false, false); open = true; }
                    else g.LineTo(p, true, true);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private static void DrawText(DrawingContext dc, string s, Point at, Brush brush, double dpi, bool right = false, bool centre = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, brush, dpi);
        double x = right ? at.X - ft.Width : centre ? at.X - ft.Width / 2 : at.X;
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }
}
