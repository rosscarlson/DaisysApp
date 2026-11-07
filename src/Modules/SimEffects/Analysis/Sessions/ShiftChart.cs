using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.6.10) The Shifting card's chart: a row per upshift (1→2, 2→3…) and one for downshifts (↓), each gear change
/// a dot at the rpm it was made at: green at the right rpm, amber early, red held on the limiter, hollow at part throttle
/// (not judged); a downshift grey, red when it over-revved. Since 4.7.0 in rpm (a share of the maximum before), from
/// the lowest change to the car's maximum: the limiter a red band at the right, and each row's window to shift in a green
/// band from the best point to just short of the limiter (a line at the best point, its rpm in it). Clicking a dot
/// picks it; the mouse over one says what it was.
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

    private Rect Plot() => new(36, 16, Math.Max(10, ActualWidth - 42), Math.Max(10, ActualHeight - 36));

    /// <summary>The rows: each upshift's gears, then the downshifts.</summary>
    private List<int> Rows()
    {
        var rows = shifts!.Shifts.Where(s => s.Up).Select(s => s.From).Distinct().OrderBy(g => g).ToList();
        if (shifts.Shifts.Any(s => !s.Up)) rows.Add(0);
        return rows;
    }

    /// <summary>The car's maximum rpm (else the highest change's).</summary>
    private double Top => shifts is { MaxRpm: > 0 } s ? s.MaxRpm : Math.Max(1000, shifts?.Shifts.Select(x => x.Rpm).DefaultIfEmpty(1000).Max() ?? 1000);

    /// <summary>The axis' start: just under the lowest change or the lowest shift window, on a round number, so the
    /// part used fills the width.</summary>
    private double Low
    {
        get
        {
            double top = Top;
            if (shifts == null || shifts.Shifts.Count == 0) return top / 2;
            double lowest = shifts.Shifts.Min(s => s.Rpm);
            foreach (var g in shifts.Shifts.Where(s => s.Up).Select(s => s.From).Distinct())
                lowest = Math.Min(lowest, Rpm(shifts.Best.TryGetValue(g, out double b) ? b - ShiftAnalysis.EarlyBy : 70));
            double step = Step(top - lowest) / 2;
            return Math.Clamp(Math.Floor((lowest - step / 2) / step) * step, 0, top - 500);
        }
    }

    /// <summary>The rpm between labels: 500 over a short range, 1,000 over a longer one, 2,000 over a very long one.</summary>
    private static double Step(double span) => span <= 1500 ? 250 : span <= 4000 ? 500 : span <= 9000 ? 1000 : 2000;

    private double Rpm(double pct) => pct / 100 * Top;

    private Point Where(Shift s, Rect plot, List<int> rows, int index)
    {
        int row = rows.IndexOf(s.Up ? s.From : 0);
        double h = plot.Height / rows.Count;
        // spread the dots of a row a little up and down, so the ones at the same rpm don't hide each other
        double jitter = ((index * 37) % 7 - 3) / 3.0 * Math.Min(4, h / 2 - 3);
        double lo = Low, top = Top;
        double x = plot.Left + (Math.Clamp(s.Rpm, lo, top) - lo) / (top - lo) * plot.Width;
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
        double lo = Low, top = Top;
        double X(double rpm) => plot.Left + (Math.Clamp(rpm, lo, top) - lo) / (top - lo) * plot.Width;
        // the limiter, named over it
        double limiter = X(Rpm(ShiftAnalysis.LimiterPct));
        var red = PedalColours.Frozen(PedalColours.Brake);
        dc.DrawRectangle(PedalColours.Frozen(PedalColours.Brake, 0x40), null, new Rect(new Point(limiter, plot.Top), new Point(plot.Right, plot.Bottom)));
        dc.DrawLine(PedalColours.Pen(red, 1.5), new Point(limiter, plot.Top - 12), new Point(limiter, plot.Bottom));
        var limitText = PedalColours.Text(this, T("Limiter"), 10, red);
        dc.DrawText(limitText, new Point(Math.Max(plot.Left, limiter - limitText.Width - 3), plot.Top - 14));
        double step = Step(top - lo), lastRight = double.NegativeInfinity;
        for (double rpm = Math.Ceiling(lo / step) * step; rpm <= top; rpm += step)
        {
            double x = X(rpm);
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = PedalColours.Text(this, rpm.ToString("N0"), 10, dim);
            double lx = Math.Min(x - label.Width / 2, plot.Right - label.Width);
            // (none over the one before)
            if (lx < lastRight + 4) continue;
            dc.DrawText(label, new Point(lx, plot.Bottom + 3));
            lastRight = lx + label.Width;
        }
        var unit = PedalColours.Text(this, T("rpm"), 10, dim);
        dc.DrawText(unit, new Point(2, plot.Bottom + 3));
        var rows = Rows();
        double h = plot.Height / rows.Count;
        var green = PedalColours.Frozen(PedalColours.Throttle);
        var goodBand = PedalColours.Frozen(PedalColours.Throttle, 0x38);
        for (int r = 0; r < rows.Count; r++)
        {
            double rowTop = plot.Top + h * r;
            if (r > 0) dc.DrawLine(grid, new Point(plot.Left, rowTop), new Point(plot.Right, rowTop));
            int g = rows[r];
            string name = g == 0 ? "↓" : $"{g}→{g + 1}";
            var t = PedalColours.Text(this, name, 10, dim);
            dc.DrawText(t, new Point(2, rowTop + h / 2 - t.Height / 2));
            if (g == 0) continue;
            // the window to shift in: from the best point (unknown: 70%, where an upshift stops being early) to the limiter
            bool known = shifts.Best.TryGetValue(g, out double best);
            double from = X(Rpm(known ? best - ShiftAnalysis.EarlyBy : 70));
            dc.DrawRectangle(goodBand, null, new Rect(new Point(from, rowTop + 1), new Point(limiter, rowTop + h - 1)));
            if (!known) continue;
            double bx = X(Rpm(best));
            dc.DrawLine(PedalColours.Pen(green, 1.5), new Point(bx, rowTop + 1), new Point(bx, rowTop + h - 1));
            var at = PedalColours.Text(this, Rpm(best).ToString("N0"), 9, green);
            if (h >= at.Height + 2 && limiter - bx > at.Width + 6) dc.DrawText(at, new Point(bx + 3, rowTop + 1));
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
            string text = F("{0} at {1:N0} rpm", hover.Gears, hover.Rpm) + (what.Length > 0 ? " · " + what : "");
            var t = PedalColours.Text(this, text, 11, PedalColours.Theme(this, "TextBrush"));
            var at = new Point(Math.Max(plot.Left + 2, plot.Right - t.Width - 4), plot.Top + 2);
            dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, new Rect(at.X - 3, at.Y - 1, t.Width + 6, t.Height + 2));
            dc.DrawText(t, at);
        }
    }
}
