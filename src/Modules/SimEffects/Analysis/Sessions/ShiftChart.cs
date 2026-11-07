using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.6.10) The Shifting card's chart: a row per upshift (1→2, 2→3…) and one for downshifts (↓), each gear change
/// a dot at the rpm it was made at (a share of the engine's maximum): green at the right rpm, amber early, red held on
/// the limiter, hollow at part throttle (not judged); a downshift grey, red when it over-revved. Each row's best point
/// is a green band, the limiter a red one at the right. Clicking a dot picks it; the mouse over one says what it was.
/// </summary>
internal sealed class ShiftChart : FrameworkElement
{
    private ShiftAnalysis? shifts;
    private string? empty;
    private Shift? hover;

    public Shift? Selected { get; set; }

    /// <summary>A dot was clicked: its shift.</summary>
    public event Action<Shift>? Picked;

    public void Show(ShiftAnalysis? s, string? emptyText = null)
    {
        shifts = s;
        empty = emptyText;
        Selected = hover = null;
        InvalidateVisual();
    }

    public ShiftChart()
    {
        ClipToBounds = true;
        MouseMove += (_, e) =>
        {
            var near = Nearest(e.GetPosition(this));
            Cursor = near != null ? Cursors.Hand : null;
            if (near != hover) { hover = near; InvalidateVisual(); }
        };
        MouseLeave += (_, _) => { if (hover != null) { hover = null; InvalidateVisual(); } };
        MouseLeftButtonUp += (_, e) =>
        {
            var near = Nearest(e.GetPosition(this));
            if (near == null) return;
            Selected = near;
            InvalidateVisual();
            Picked?.Invoke(near);
        };
    }

    private Rect Plot() => new(36, 6, Math.Max(10, ActualWidth - 42), Math.Max(10, ActualHeight - 26));

    /// <summary>The rows: each upshift's gears, then the downshifts.</summary>
    private List<int> Rows()
    {
        var rows = shifts!.Shifts.Where(s => s.Up).Select(s => s.From).Distinct().OrderBy(g => g).ToList();
        if (shifts.Shifts.Any(s => !s.Up)) rows.Add(0);
        return rows;
    }

    private double Low => shifts == null || shifts.Shifts.Count == 0 ? 50
        : Math.Min(50, Math.Floor(shifts.Shifts.Min(s => s.Pct) / 10) * 10);

    private Point Where(Shift s, Rect plot, List<int> rows, int index)
    {
        int row = rows.IndexOf(s.Up ? s.From : 0);
        double h = plot.Height / rows.Count;
        // spread the dots of a row a little up and down, so the ones at the same rpm don't hide each other
        double jitter = ((index * 37) % 7 - 3) / 3.0 * Math.Min(4, h / 2 - 3);
        double lo = Low;
        double x = plot.Left + (Math.Clamp(s.Pct, lo, 100) - lo) / (100 - lo) * plot.Width;
        return new Point(x, plot.Top + h * (row + 0.5) + jitter);
    }

    private Shift? Nearest(Point p)
    {
        if (shifts == null || shifts.Shifts.Count == 0) return null;
        var plot = Plot();
        var rows = Rows();
        Shift? best = null;
        double bestD = 8;
        for (int i = 0; i < shifts.Shifts.Count; i++)
        {
            var q = Where(shifts.Shifts[i], plot, rows, i);
            double dist = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            if (dist < bestD) { bestD = dist; best = shifts.Shifts[i]; }
        }
        return best;
    }

    private static Color ColourOf(ShiftKind k) => k switch
    {
        ShiftKind.Good => PedalColours.Throttle,
        ShiftKind.Early => PedalColours.Warning,
        ShiftKind.Limiter or ShiftKind.OverRev => PedalColours.Brake,
        _ => Colors.Gray,
    };

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot();
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var grid = PedalColours.Pen(dim, 1, 0.18);
        dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, plot);
        if (shifts == null || !shifts.HasData || shifts.Shifts.Count == 0)
        {
            string text = empty ?? (shifts == null ? T("Nothing to show yet") : !shifts.HasData ? T("No gears in this race's log") : T("No gear changes"));
            dc.DrawText(PedalColours.Text(this, text, 12, dim), new Point(plot.Left + 10, plot.Top + 10));
            return;
        }
        double lo = Low;
        double X(double pct) => plot.Left + (Math.Clamp(pct, lo, 100) - lo) / (100 - lo) * plot.Width;
        // the limiter
        dc.DrawRectangle(PedalColours.Frozen(PedalColours.Brake, 0x30), null, new Rect(new Point(X(ShiftAnalysis.LimiterPct), plot.Top), new Point(plot.Right, plot.Bottom)));
        for (double pct = Math.Ceiling(lo / 10) * 10; pct <= 100; pct += 10)
        {
            double x = X(pct);
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = PedalColours.Text(this, pct.ToString("0") + "%", 10, dim);
            dc.DrawText(label, new Point(Math.Min(x - label.Width / 2, plot.Right - label.Width), plot.Bottom + 3));
        }
        var rows = Rows();
        double h = plot.Height / rows.Count;
        var goodBand = PedalColours.Frozen(PedalColours.Throttle, 0x2C);
        for (int r = 0; r < rows.Count; r++)
        {
            double top = plot.Top + h * r;
            if (r > 0) dc.DrawLine(grid, new Point(plot.Left, top), new Point(plot.Right, top));
            int g = rows[r];
            string name = g == 0 ? "↓" : $"{g}→{g + 1}";
            var t = PedalColours.Text(this, name, 10, dim);
            dc.DrawText(t, new Point(2, top + h / 2 - t.Height / 2));
            if (g > 0 && shifts.Best.TryGetValue(g, out double best))
                dc.DrawRectangle(goodBand, null, new Rect(new Point(X(best - ShiftAnalysis.EarlyBy), top + 1), new Point(X(Math.Min(best + 1, 100)), top + h - 1)));
        }
        var accent = PedalColours.Theme(this, "AccentBrush");
        for (int i = 0; i < shifts.Shifts.Count; i++)
        {
            var s = shifts.Shifts[i];
            var p = Where(s, plot, rows, i);
            var c = s.Up ? ColourOf(s.Kind) : s.Kind == ShiftKind.OverRev ? PedalColours.Brake : Colors.Gray;
            bool hollow = s.Kind == ShiftKind.PartThrottle;
            dc.DrawEllipse(hollow ? null : PedalColours.Frozen(c, 0xD0), hollow ? PedalColours.Pen(PedalColours.Frozen(c), 1.2) : null, p, 3.5, 3.5);
            if (s == Selected || s == hover) dc.DrawEllipse(null, PedalColours.Pen(accent, 2), p, 6, 6);
        }
        if (hover != null)
        {
            string what = hover.Kind switch
            {
                ShiftKind.Good => T("right"),
                ShiftKind.Early => T("early"),
                ShiftKind.Limiter => T("on the limiter"),
                ShiftKind.PartThrottle => T("part throttle"),
                ShiftKind.OverRev => T("over-revved"),
                _ => "",
            };
            string text = F("{0} at {1:N0} rpm ({2:0}%)", hover.Gears, hover.Rpm, hover.Pct) + (what.Length > 0 ? " · " + what : "");
            var t = PedalColours.Text(this, text, 11, PedalColours.Theme(this, "TextBrush"));
            var at = new Point(Math.Max(plot.Left + 2, plot.Right - t.Width - 4), plot.Top + 2);
            dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, new Rect(at.X - 3, at.Y - 1, t.Width + 6, t.Height + 2));
            dc.DrawText(t, at);
        }
    }
}
