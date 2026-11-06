using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// How smooth each brake and throttle application and release was, over the session: a dot each (filled: on, hollow:
/// off) at its score, with the running average of each as a line, and the green (85+) and amber (65+) bands. Brake,
/// throttle, on and off can each be hidden; pointing at a dot says what it is, clicking it picks it.
/// </summary>
internal sealed class SmoothnessChart : FrameworkElement
{
    private sealed record Dot(int At, double Score, bool Brake, bool On, object Item, string Text);

    private PedalAnalysis? a;
    private List<Dot> dots = new();
    private Point? hover;

    public bool ShowBrake { get; set; } = true;
    public bool ShowThrottle { get; set; } = true;
    public bool ShowOn { get; set; } = true;
    public bool ShowOff { get; set; } = true;

    /// <summary>A dot was clicked: its brake zone or throttle move.</summary>
    public event Action<object>? Picked;

    public PedalAnalysis? Analysis
    {
        get => a;
        set
        {
            a = value;
            dots = new();
            if (a != null)
            {
                foreach (var z in a.Brakes.Where(z => z.Peak >= 0.15))
                {
                    dots.Add(new Dot(z.Start, z.ApplyScore, true, true, z, F("Brake zone {0}: application {1:0}", z.Number, z.ApplyScore)));
                    dots.Add(new Dot(z.ReleaseFrom, z.ReleaseScore, true, false, z, F("Brake zone {0}: release {1:0}", z.Number, z.ReleaseScore)));
                }
                foreach (var m in a.Throttle)
                    dots.Add(new Dot(m.Start, m.Score, false, m.Up, m, m.Up ? F("Throttle on {0}: {1:0}", m.Number, m.Score) : F("Throttle off {0}: {1:0}", m.Number, m.Score)));
                dots.Sort((x, y) => x.At.CompareTo(y.At));
            }
            InvalidateVisual();
        }
    }

    public SmoothnessChart()
    {
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        MouseMove += (_, e) => { hover = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (_, _) => { hover = null; InvalidateVisual(); };
        MouseLeftButtonUp += (_, e) => { if (Near(e.GetPosition(this)) is { } d) Picked?.Invoke(d.Item); };
    }

    private bool Shown(Dot d) => (d.Brake ? ShowBrake : ShowThrottle) && (d.On ? ShowOn : ShowOff);
    private Rect Plot() => new(30, 6, Math.Max(10, ActualWidth - 36), Math.Max(10, ActualHeight - 24));
    private double X(Rect p, double at) => p.Left + at / Math.Max(1, (a?.Data.Count ?? 1) - 1) * p.Width;
    private static double Y(Rect p, double score) => p.Bottom - Math.Clamp(score, 0, 100) / 100 * p.Height;

    private Dot? Near(Point m)
    {
        var p = Plot();
        Dot? best = null;
        double bestD = 8;
        foreach (var d in dots)
        {
            if (!Shown(d)) continue;
            double dist = Math.Sqrt(Math.Pow(X(p, d.At) - m.X, 2) + Math.Pow(Y(p, d.Score) - m.Y, 2));
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Plot();
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, p);
        dc.DrawRectangle(PedalColours.Frozen(PedalColours.Throttle, 0x14), null, new Rect(p.Left, Y(p, 100), p.Width, Y(p, 85) - Y(p, 100)));
        dc.DrawRectangle(PedalColours.Frozen(PedalColours.Warning, 0x10), null, new Rect(p.Left, Y(p, 85), p.Width, Y(p, 65) - Y(p, 85)));
        foreach (int s in new[] { 0, 25, 50, 75, 100 })
        {
            dc.DrawLine(PedalColours.Pen(dim, 1, 0.15), new Point(p.Left, Y(p, s)), new Point(p.Right, Y(p, s)));
            dc.DrawText(PedalColours.Text(this, s.ToString(), 9, dim), new Point(4, Y(p, s) - 6));
        }
        if (a == null || dots.Count == 0)
        {
            dc.DrawText(PedalColours.Text(this, T("Nothing yet"), 12, dim), new Point(p.Left + 10, p.Top + 10));
            return;
        }
        double minutes = a.Data.Seconds / 60;
        double step = new[] { 1.0, 2, 5, 10, 15, 30, 60 }.FirstOrDefault(s => minutes / s <= 6, 120);
        for (double t = step; t < minutes; t += step)
        {
            double x = X(p, t * 60 * SessionData.Hz);
            dc.DrawText(PedalColours.Text(this, SessionChart.Clock(t * 60), 9, dim), new Point(x - 10, p.Bottom + 3));
        }
        // the running averages, then the dots
        foreach (bool brake in new[] { true, false })
            foreach (bool on in new[] { true, false })
            {
                var series = dots.Where(d => d.Brake == brake && d.On == on && Shown(d)).ToList();
                if (series.Count < 3) continue;
                var colour = PedalColours.Frozen(brake ? PedalColours.Brake : PedalColours.Throttle);
                var pen = new Pen(colour, 1.6) { DashStyle = on ? null : DashStyles.Dash };
                pen.Freeze();
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    int w = Math.Max(3, series.Count / 12);
                    for (int i = 0; i < series.Count; i++)
                    {
                        double avg = series.Skip(Math.Max(0, i - w / 2)).Take(w).Average(d => d.Score);
                        var pt = new Point(X(p, series[i].At), Y(p, avg));
                        if (i == 0) ctx.BeginFigure(pt, false, false); else ctx.LineTo(pt, true, false);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        foreach (var d in dots)
        {
            if (!Shown(d)) continue;
            var colour = PedalColours.Frozen(d.Brake ? PedalColours.Brake : PedalColours.Throttle, 0xC0);
            var c = new Point(X(p, d.At), Y(p, d.Score));
            if (d.On) dc.DrawEllipse(colour, null, c, 2.6, 2.6);
            else dc.DrawEllipse(null, PedalColours.Pen(colour, 1.2), c, 2.6, 2.6);
        }
        if (hover is { } h && Near(h) is { } near)
        {
            var c = new Point(X(p, near.At), Y(p, near.Score));
            dc.DrawEllipse(null, PedalColours.Pen(PedalColours.Theme(this, "TextBrush"), 1.5), c, 5, 5);
            var ft = PedalColours.Text(this, near.Text + "  ·  " + SessionChart.Clock(near.At * SessionData.Dt), 11, PedalColours.Theme(this, "TextBrush"));
            double bx = Math.Clamp(c.X - ft.Width / 2 - 6, 0, Math.Max(0, ActualWidth - ft.Width - 12));
            double by = c.Y - ft.Height - 16 < 0 ? c.Y + 10 : c.Y - ft.Height - 16;
            dc.DrawRoundedRectangle(PedalColours.Theme(this, "PopupBrush"), PedalColours.Pen(PedalColours.Theme(this, "ControlBorderBrush"), 1), new Rect(bx, by, ft.Width + 12, ft.Height + 8), 5, 5);
            dc.DrawText(ft, new Point(bx + 6, by + 4));
        }
    }
}

/// <summary>
/// The friction circle (a g-g diagram): the car's sideways g across, braking down and accelerating up, a dot every few
/// hundredths of a second, red while braking, green on the throttle, grey coasting, with rings each half g and the
/// peaks. A smooth driver's dots join up round the edge (braking into the turn, then the throttle out of it); jerky
/// pedals leave them in a cross. Brake and throttle can be hidden.
/// </summary>
internal sealed class FrictionCircle : FrameworkElement
{
    private PedalAnalysis? a;
    private double maxG = 1.5;

    public bool ShowBrake { get; set; } = true;
    public bool ShowThrottle { get; set; } = true;

    public PedalAnalysis? Analysis
    {
        get => a;
        set
        {
            a = value;
            maxG = 1.5;
            if (a != null && a.Data.HasG)
            {
                var d = a.Data;
                var all = new List<double>();
                for (int i = 0; i < d.Count; i += 3)
                    if (!double.IsNaN(d.LatG[i]) && !double.IsNaN(d.LonG[i])) all.Add(Math.Sqrt(d.LatG[i] * d.LatG[i] + d.LonG[i] * d.LonG[i]));
                all.Sort();
                // the 99.5th percentile, so a crash doesn't squash the rest
                if (all.Count > 0) maxG = Math.Max(1, Math.Ceiling(all[(int)(all.Count * 0.995)] * 2) / 2);
            }
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double r = Math.Max(10, Math.Min(w, h - 20) / 2 - 8);
        var c = new Point(w / 2, r + 8);
        dc.DrawEllipse(PedalColours.Theme(this, "RoomBrush"), null, c, r, r);
        for (double g = 0.5; g <= maxG + 0.001; g += 0.5)
        {
            double rr = g / maxG * r;
            dc.DrawEllipse(null, PedalColours.Pen(dim, 1, g % 1 == 0 ? 0.35 : 0.15), c, rr, rr);
            if (g % 1 == 0) dc.DrawText(PedalColours.Text(this, $"{g:0} g", 9, dim), new Point(c.X + 3, c.Y - rr + 1));
        }
        dc.DrawLine(PedalColours.Pen(dim, 1, 0.25), new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(PedalColours.Pen(dim, 1, 0.25), new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        if (a == null || !a.Data.HasG)
        {
            var t = PedalColours.Text(this, T("No telemetry"), 12, dim);
            dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y + 8));
            return;
        }
        var d = a.Data;
        var brake = PedalColours.Frozen(PedalColours.Brake, 0x50);
        var throttle = PedalColours.Frozen(PedalColours.Throttle, 0x50);
        var coast = PedalColours.Frozen(Colors.Gray, 0x40);
        int stride = Math.Max(2, d.Count / 8000);
        double lat = 0, braking = 0, accel = 0;
        for (int i = 0; i < d.Count; i += stride)
        {
            double x = d.LatG[i], y = d.LonG[i];
            if (double.IsNaN(x) || double.IsNaN(y) || (d.HasSpeed && !(d.Speed[i] > 3))) continue;
            if (Math.Sqrt(x * x + y * y) >= Incident.CrashG) continue;
            bool b = a.SmoothBrake[i] > 0.05, th = a.SmoothThrottle[i] > 0.05;
            if (b ? !ShowBrake : th ? !ShowThrottle : !(ShowBrake && ShowThrottle)) continue;
            lat = Math.Max(lat, Math.Abs(x));
            braking = Math.Max(braking, -y);
            accel = Math.Max(accel, y);
            var pt = new Point(c.X + Math.Clamp(x / maxG, -1, 1) * r, c.Y - Math.Clamp(y / maxG, -1, 1) * r);
            dc.DrawRectangle(b ? brake : th ? throttle : coast, null, new Rect(pt.X - 1, pt.Y - 1, 2, 2));
        }
        var peaks = PedalColours.Text(this, F("Lateral {0:0.00} g · Braking {1:0.00} g · Accel {2:0.00} g", lat, braking, accel), 10.5, dim);
        dc.DrawText(peaks, new Point(Math.Max(0, c.X - peaks.Width / 2), c.Y + r + 4));
    }
}
