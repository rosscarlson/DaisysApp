using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.6.2) Track Map: the line Compare's two runs took, from where the game says the car was (its X and Z, in
/// metres, 60 times a second): this run solid, the other dotted, north up. Both are in the game's own coordinates, so
/// two sessions on a track lie over each other as driven. The mouse on Compare's chart puts a dot on each line
/// (<see cref="HoverAt"/>), a corner picked zooms to it; the wheel zooms, dragging moves, a double-click shows it all.
/// </summary>
internal sealed class LineMap : LayeredChart
{
    private Run? a, b;
    private Point[] la = Array.Empty<Point>(), lb = Array.Empty<Point>();
    /// <summary>The world shown: its middle (X, Z) and metres a pixel.</summary>
    private double cx, cz, scale = 1;
    private double hoverX = double.NaN;
    private Point? dragFrom;
    private (double X, double Z) dragAt;

    public LineMap()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        MouseWheel += (_, e) =>
        {
            if (la.Length == 0 && lb.Length == 0) return;
            var p = e.GetPosition(this);
            var (wx, wz) = World(p);
            scale *= e.Delta > 0 ? 0.8 : 1.25;
            // (the point under the mouse stays put)
            cx = wx - (p.X - ActualWidth / 2) * scale;
            cz = wz + (p.Y - ActualHeight / 2) * scale;
            InvalidateVisual();
            e.Handled = true;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Fit(la.Concat(lb)); return; }
            dragFrom = e.GetPosition(this);
            dragAt = (cx, cz);
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            cx = dragAt.X - (p.X - from.X) * scale;
            cz = dragAt.Z + (p.Y - from.Y) * scale;
            InvalidateVisual();
        };
        MouseLeftButtonUp += (_, _) => { dragFrom = null; ReleaseMouseCapture(); };
        SizeChanged += (_, _) => { if (fitted) Fit(la.Concat(lb)); };
    }

    private bool fitted = true;

    public void Show(Run? a, Run? b)
    {
        this.a = a;
        this.b = b;
        la = Line(a);
        lb = Line(b);
        Fit(la.Concat(lb));
    }

    /// <summary>A run's places (X, Z), 20 a second, where it has them.</summary>
    private static Point[] Line(Run? r)
    {
        if (r == null) return Array.Empty<Point>();
        var points = new List<Point>();
        for (int i = r.From; i <= r.To; i += 5)
        {
            double x = r.Data.X[i], z = r.Data.Z[i];
            if (double.IsNaN(x) || double.IsNaN(z) || (x == 0 && z == 0)) continue;
            points.Add(new Point(x, z));
        }
        return points.ToArray();
    }

    /// <summary>The place each run was at this far along (Compare's axis), or NaN.</summary>
    public void HoverAt(double x)
    {
        hoverX = x;
        // (since 4.15.2 only the dots drawn again)
        InvalidateTop();
    }

    /// <summary>Zooms to this run between two points of Compare's axis (a corner), with some room round it.</summary>
    public void ShowRange(double from, double to)
    {
        if (a == null) return;
        var points = new List<Point>();
        for (double x = from; x <= to; x += Math.Max(1, (to - from) / 40))
            if (Place(a, x) is { } p) points.Add(p);
        if (points.Count > 1) Fit(points, 0.35, 150);
    }

    private void Fit(IEnumerable<Point> points, double margin = 0.08, double least = 50)
    {
        var list = points.ToList();
        fitted = margin < 0.1;
        if (list.Count == 0 || ActualWidth < 10 || ActualHeight < 10) { InvalidateVisual(); return; }
        double minX = list.Min(p => p.X), maxX = list.Max(p => p.X), minZ = list.Min(p => p.Y), maxZ = list.Max(p => p.Y);
        cx = (minX + maxX) / 2;
        cz = (minZ + maxZ) / 2;
        double w = Math.Max(least, maxX - minX), h = Math.Max(least, maxZ - minZ);
        scale = Math.Max(w / (ActualWidth * (1 - 2 * margin)), h / (ActualHeight * (1 - 2 * margin)));
        InvalidateVisual();
    }

    private Point Screen(double x, double z) => new(ActualWidth / 2 + (x - cx) / scale, ActualHeight / 2 - (z - cz) / scale);

    private (double X, double Z) World(Point p) => (cx + (p.X - ActualWidth / 2) * scale, cz - (p.Y - ActualHeight / 2) * scale);

    private static Point? Place(Run r, double x)
    {
        double px = r.ValueAt(r.Data.X, x), pz = r.ValueAt(r.Data.Z, x);
        return double.IsNaN(px) || double.IsNaN(pz) ? null : new Point(px, pz);
    }

    protected override void RenderChart(DrawingContext dc)
    {
        dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        if (la.Length < 2 && lb.Length < 2)
        {
            dc.DrawText(PedalColours.Text(this, a == null ? T("Pick something to compare") : T("No places in this session"), 13, dim), new Point(12, 12));
            return;
        }
        void Draw(Point[] line, Color colour, bool dotted)
        {
            if (line.Length < 2) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(Screen(line[0].X, line[0].Y), false, false);
                for (int i = 1; i < line.Length; i++) ctx.LineTo(Screen(line[i].X, line[i].Y), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, CompareChart.LinePen(PedalColours.Frozen(colour), 2, 1, dotted), g);
        }
        Draw(lb, PedalColours.RunB, true);
        Draw(la, PedalColours.RunA, false);
        // the start
        var first = la.Length > 0 ? la[0] : lb[0];
        var s = Screen(first.X, first.Y);
        dc.DrawRectangle(PedalColours.Theme(this, "TextBrush"), null, new Rect(s.X - 3, s.Y - 3, 6, 6));
        // a scale bar
        double metres = new[] { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }.FirstOrDefault(m => m / scale >= 60, 5000);
        double len = metres / scale, y = ActualHeight - 10;
        dc.DrawLine(PedalColours.Pen(dim, 1.5), new Point(10, y), new Point(10 + len, y));
        dc.DrawText(PedalColours.Text(this, metres >= 1000 ? $"{metres / 1000:0} km" : $"{metres:0} m", 10, dim), new Point(10, y - 15));
    }

    /// <summary>(since 4.15.2) The dot on each line, on the top layer.</summary>
    protected override void RenderTop(DrawingContext dc)
    {
        if (double.IsNaN(hoverX)) return;
        foreach (var (r, colour) in new[] { (a, PedalColours.RunA), (b, PedalColours.RunB) })
        {
            if (r == null || Place(r, hoverX) is not { } p) continue;
            var at = Screen(p.X, p.Y);
            dc.DrawEllipse(PedalColours.Frozen(colour), PedalColours.Pen(Brushes.White, 1.5), at, 5, 5);
        }
    }
}
