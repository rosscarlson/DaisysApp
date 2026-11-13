using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// The friction circle (a g-g diagram): the car's sideways g across, braking down and accelerating up, a dot every few
/// hundredths of a second, red while braking, green on the throttle, grey coasting, with rings each half g and the
/// peaks. A smooth driver's dots join up round the edge (braking into the turn, then the throttle out of it); jerky
/// pedals leave them in a cross. Brake and throttle can be hidden. Since 4.14.1 a large white dot at Compare's cursor
/// (<see cref="At"/>), moving as it plays; the rest drawn once and kept.
/// </summary>
internal sealed class FrictionCircle : LayeredChart
{
    private PedalAnalysis? a;
    private double maxG = 1.5;
    private bool showBrake = true, showThrottle = true;
    private double at = double.NaN;
    /// <summary>(since 4.14.1) The dots, rings and peaks, drawn for a size and theme (null: again).</summary>
    private DrawingGroup? cache;
    private (Size, Brush) cacheFor;

    public bool ShowBrake { get => showBrake; set { showBrake = value; cache = null; } }
    public bool ShowThrottle { get => showThrottle; set { showThrottle = value; cache = null; } }

    /// <summary>(since 4.14.1) The moment of the session the dot is at (a sample, fractional; NaN: none).</summary>
    public double At
    {
        get => at;
        set
        {
            if (value.Equals(at) || (double.IsNaN(value) && double.IsNaN(at))) return;
            at = value;
            // (since 4.15.2 only the dot drawn again)
            InvalidateTop();
        }
    }

    public PedalAnalysis? Analysis
    {
        get => a;
        set
        {
            a = value;
            cache = null;
            at = double.NaN;
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

    protected override void RenderChart(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double r = Math.Max(10, Math.Min(w, h - 20) / 2 - 8);
        var c = new Point(w / 2, r + 8);
        var key = (new Size(w, h), PedalColours.Theme(this, "RoomBrush"));
        if (cache == null || !cacheFor.Equals(key))
        {
            cache = new DrawingGroup();
            using (var g = cache.Open()) Draw(g, w, r, c);
            cache.Freeze();
            cacheFor = key;
        }
        dc.DrawDrawing(cache);
        centre = c;
        radius = r;
    }

    /// <summary>(since 4.15.2) Where the circle was last drawn, for the dot.</summary>
    private Point centre;
    private double radius;

    /// <summary>(since 4.14.1) The cursor's moment, (since 4.15.2) on the top layer.</summary>
    protected override void RenderTop(DrawingContext dc)
    {
        var c = centre;
        double r = radius;
        if (r <= 0 || a == null || !a.Data.HasG || double.IsNaN(at)) return;
        int i = Math.Clamp((int)Math.Round(at), 0, a.Data.Count - 1);
        double x = a.Data.LatG[i], y = a.Data.LonG[i];
        if (double.IsNaN(x) || double.IsNaN(y)) return;
        var pt = new Point(c.X + Math.Clamp(x / maxG, -1, 1) * r, c.Y - Math.Clamp(y / maxG, -1, 1) * r);
        dc.DrawEllipse(Brushes.White, PedalColours.Pen(Brushes.Black, 1.5, 0.7), pt, 7, 7);
    }

    private void Draw(DrawingContext dc, double w, double r, Point c)
    {
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
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
