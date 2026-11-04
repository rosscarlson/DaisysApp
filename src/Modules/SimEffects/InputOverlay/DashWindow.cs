using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Dash overlay (since 3.33.0), drawn after Forza Horizon's: a round tach (the RPM in thousands round its ring, the
/// redline's marks red, the ring lit up to the needle), the gear large in the middle (flashing red at the shift point),
/// the speed under it, and the TCS and ABS lights. Core's widget (card: false, its Background slider the dial's).
/// Drawn each frame it shows, from the <see cref="OverlayReader"/>'s last frame.
/// </summary>
internal sealed class DashWindow : Widget
{
    private readonly OverlayReader reader;
    private readonly DashView view;
    private long drawn;

    public DashWindow(OverlaySettings settings, OverlayReader reader, Action save, Action delete)
        : base(settings.Dash, save, delete, card: false, firstSize: new Size(260, 260))
    {
        this.reader = reader;
        Title = T("Dash");
        MinWidth = MinHeight = 120;
        Aspect = 1;
        view = new DashView(settings, () => BackgroundOpacity);
        Body = view;
        BackgroundChanged += view.InvalidateVisual;
        EditingChanged += () => { view.Editing = Editing; view.InvalidateVisual(); };
        // drawn each screen frame while it shows and the game's frames are coming, and only with a new one (FrameHook)
        _ = new FrameHook(this, reader, dash: true, OnFrame, () => { view.Has = reader.Frame(view.Frame); view.InvalidateVisual(); });
    }

    /// <summary>Its settings changed on the tab: drawn again.</summary>
    public void Changed() => view.InvalidateVisual();

    private void OnFrame()
    {
        // (60 a second at most: a needle doesn't need more)
        long t = Environment.TickCount64;
        if (t - drawn < 15) return;
        drawn = t;
        view.Has = reader.Frame(view.Frame);
        view.InvalidateVisual();
    }

}

/// <summary>The dial itself, drawn in a 200 × 200 space scaled to fit.</summary>
internal sealed class DashView : FrameworkElement
{
    private static readonly Typeface Race = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.SemiCondensed);
    private static readonly Typeface RaceLight = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.SemiCondensed);
    private static readonly Brush White = Ui.Brush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Brush Dim = Ui.Brush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Red = Ui.Brush(Color.FromRgb(0xFF, 0x3B, 0x30));
    private static readonly Brush Amber = Ui.Brush(Color.FromRgb(0xFF, 0xB0, 0x1F));
    private static readonly Brush Off = Ui.Brush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Pen Track = FrozenPen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 5);
    private static readonly Pen Lit = FrozenPen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 5);
    private static readonly Pen LitRed = FrozenPen(Color.FromRgb(0xFF, 0x3B, 0x30), 5);
    private static readonly Pen Major = FrozenPen(Color.FromRgb(0xF4, 0xF4, 0xF4), 2.4);
    private static readonly Pen Minor = FrozenPen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 1.2);
    private static readonly Pen MajorRed = FrozenPen(Color.FromRgb(0xFF, 0x3B, 0x30), 2.4);
    private static readonly Pen MinorRed = FrozenPen(Color.FromRgb(0xFF, 0x3B, 0x30), 1.2);
    private static readonly Pen NeedlePen = FrozenPen(Color.FromRgb(0xFF, 0x5A, 0x1F), 3);
    private static readonly Pen Glow = FrozenPen(Color.FromArgb(0x50, 0xFF, 0x5A, 0x1F), 9);
    private static readonly Pen Rim = FrozenPen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 1);

    /// <summary>The tach's sweep: from below left, round the top, to below right (degrees, clockwise from 3 o'clock).</summary>
    private const double From = 135, Sweep = 270;

    private readonly OverlaySettings settings;
    private readonly Func<double> background;
    private double needle;

    public TelemetryFrame Frame { get; } = new();
    public bool Has { get; set; }
    public bool Editing { get; set; }

    public DashView(OverlaySettings settings, Func<double> background)
    {
        this.settings = settings;
        this.background = background;
    }


    private static Pen FrozenPen(Color c, double w)
    {
        var p = new Pen(Ui.Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static Point At(double deg, double r) => new(100 + r * Math.Cos(deg * Math.PI / 180), 100 + r * Math.Sin(deg * Math.PI / 180));

    private static Geometry Arc(double fromDeg, double toDeg, double r)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(At(fromDeg, r), false, false);
            c.ArcTo(At(toDeg, r), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 200, size / 200));

        // the dial: dark, as see-through as the Background slider (a little there while it's edited)
        double back = Editing ? Math.Max(background(), 0.35) : background();
        var face = new RadialGradientBrush(Color.FromArgb((byte)(235 * back), 0x10, 0x12, 0x16), Color.FromArgb((byte)(200 * back), 0x00, 0x00, 0x00));
        dc.DrawEllipse(face, back > 0.02 ? Rim : null, new Point(100, 100), 98, 98);

        var f = Frame;
        bool has = Has;
        float max = has && f.MaxRpm > 1000 ? f.MaxRpm : 8000;
        double top = Math.Ceiling(max / 1000) * 1000;
        double shift = has && f.ShiftRpm > 0 ? f.ShiftRpm : max * 0.92;
        double red = Math.Min(shift, max * 0.9);
        double rpm = has ? Math.Clamp(f.Rpm, 0, top) : 0;
        // (eased, so the needle sweeps rather than jumps between packets)
        needle += (rpm - needle) * 0.45;
        double Angle(double r) => From + Sweep * Math.Clamp(r / top, 0, 1);

        // the ring: dim all round, lit up to the needle (red past the redline)
        dc.DrawGeometry(null, Track, Arc(From, From + Sweep, 88));
        if (needle > 50) dc.DrawGeometry(null, needle >= red ? LitRed : Lit, Arc(From, Angle(needle), 88));

        // the marks: every 1000 large with its number, every 500 small; red from the redline
        for (int k = 0; k <= top / 500; k++)
        {
            double r = k * 500, a = Angle(r);
            bool major = k % 2 == 0, inRed = r >= red;
            dc.DrawLine(major ? (inRed ? MajorRed : Major) : (inRed ? MinorRed : Minor), At(a, 80), At(a, major ? 70 : 75));
            if (!major) continue;
            var n = Text(((int)(r / 1000)).ToString(CultureInfo.InvariantCulture), Race, 13, inRed ? Red : White, dip);
            var p = At(a, 59);
            dc.DrawText(n, new Point(p.X - n.Width / 2, p.Y - n.Height / 2));
        }
        var x1000 = Text("x1000", RaceLight, 7, Dim, dip);
        dc.DrawText(x1000, new Point(100 - x1000.Width / 2, 185));

        // the needle, with a glow, from the gear's ring to the marks
        double na = Angle(needle);
        dc.DrawLine(Glow, At(na, 31), At(na, 78));
        dc.DrawLine(NeedlePen, At(na, 31), At(na, 78));

        // the gear, large in the middle: red and flashing at the shift point
        string gear = !has ? "–" : f.GearNo < 0 ? "R" : f.GearNo == 0 ? "N" : f.GearNo.ToString(CultureInfo.InvariantCulture);
        bool shiftNow = has && settings.Dash.ShiftFlash && f.GearNo > 0 && rpm >= shift;
        bool flashOn = Environment.TickCount64 / 90 % 2 == 0;
        var g = Text(gear, Race, 52, shiftNow ? (flashOn ? Red : White) : White, dip);
        dc.DrawEllipse(null, shiftNow && flashOn ? LitRed : Rim, new Point(100, 98), 27, 27);
        dc.DrawText(g, new Point(100 - g.Width / 2, 98 - g.Height / 2));

        // the speed under it, and its units
        bool kmh = (settings.Dash.Unit ?? settings.Unit) == SpeedUnit.Kmh;
        string speed = has ? Math.Round(f.SpeedMs * (kmh ? 3.6 : 2.23694)).ToString("0", CultureInfo.CurrentCulture) : "–";
        var s = Text(speed, Race, 30, White, dip);
        dc.DrawText(s, new Point(100 - s.Width / 2, 128));
        var u = Text(kmh ? T("KM/H") : T("MPH"), RaceLight, 8, Dim, dip);
        dc.DrawText(u, new Point(100 - u.Width / 2, 158));

        // TCS (the wheels spinning under power: the game doesn't say when its own works) and ABS
        if (settings.Dash.Lights)
        {
            bool tcs = has && f.Slip > 0.35f && f.Throttle > 0.3f, abs = has && f.Abs > 0;
            Light(dc, "TCS", tcs, 74, dip);
            Light(dc, "ABS", abs, 126, dip);
        }
        dc.Pop();
        dc.Pop();
    }

    private static void Light(DrawingContext dc, string name, bool on, double x, double dip)
    {
        var t = Text(name, Race, 8, on ? Brushes.Black : Off, dip);
        var r = new Rect(x - 13, 170, 26, 12);
        dc.DrawRoundedRectangle(on ? Amber : null, on ? null : Ui.Pen(Off, 1), r, 3, 3);
        dc.DrawText(t, new Point(x - t.Width / 2, r.Top + (r.Height - t.Height) / 2));
    }

    private static FormattedText Text(string s, Typeface face, double size, Brush brush, double dip) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, dip);
}
