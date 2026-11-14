using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Dash overlay (since 3.33.0), drawn after Forza Horizon's: a round tach (the RPM in thousands round its ring, the
/// redline's marks red, the ring lit up to the needle), the gear large in the middle (flashing red at the shift point),
/// the speed under it, and the TCS and ABS lights. Since 4.17.9 a speedometer at its right (either dial can be off), and
/// a digital look: one low strip (a bar of lights for the RPM, the gear, the speed, the lights). Core's widget (card:
/// false, its Background slider the dial's). Drawn each frame it shows, from the <see cref="OverlayReader"/>'s last frame.
/// </summary>
internal sealed class DashWindow : Widget
{
    /// <summary>DIPs per unit of <see cref="DashView.SpaceOf"/> the first time (a dial 260 across).</summary>
    public const double FirstScale = 1.3;

    private readonly OverlayReader reader;
    private readonly DashView view;
    private long drawn;

    public DashWindow(OverlaySettings settings, OverlayReader reader, Action save, Action delete)
        : base(settings.Dash, save, delete, card: false, firstSize: First(settings.Dash))
    {
        this.reader = reader;
        Title = T("Dash");
        MinWidth = 120;
        MinHeight = 40;
        Layout = DashView.LayoutOf(settings.Dash);
        var space = DashView.SpaceOf(settings.Dash);
        Aspect = space.Height / space.Width;
        view = new DashView(settings, () => BackgroundOpacity);
        Body = view;
        BackgroundChanged += view.InvalidateVisual;
        EditingChanged += () => { view.Editing = Editing; view.InvalidateVisual(); };
        // drawn each screen frame while it shows and the game's frames are coming, and only with a new one (FrameHook)
        _ = new FrameHook(this, reader, dash: true, OnFrame, () => { view.Has = reader.Frame(view.Frame); view.InvalidateVisual(); });
    }

    private static Size First(DashSettings d)
    {
        var s = DashView.SpaceOf(d);
        return new Size(s.Width * FirstScale, s.Height * FirstScale);
    }

    /// <summary>What its shape was made for (<see cref="DashView.LayoutOf"/>): another needs the window made again.</summary>
    public string Layout { get; }

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

/// <summary>
/// The dash itself: analog, a 200 × 200 space a dial (the tach, then the speedometer); digital, a strip 64 high.
/// Scaled to fit. <c>classic</c> (Analysis' exported videos): the tach alone, with the speed under the gear, as before 4.17.9.
/// </summary>
internal sealed class DashView : FrameworkElement
{
    private static readonly Typeface Race = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.SemiCondensed);
    private static readonly Typeface RaceLight = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.SemiCondensed);
    private static readonly Brush White = Ui.Brush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Brush Dim = Ui.Brush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Red = Ui.Brush(Color.FromRgb(0xFF, 0x3B, 0x30));
    private static readonly Brush Amber = Ui.Brush(Color.FromRgb(0xFF, 0xB0, 0x1F));
    private static readonly Brush Green = Ui.Brush(Color.FromRgb(0x3C, 0xD0, 0x70));
    private static readonly Brush Off = Ui.Brush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Unlit = Ui.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
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
    private static readonly Pen ShiftRim = FrozenPen(Color.FromRgb(0xFF, 0x3B, 0x30), 2.5);

    /// <summary>A dial's sweep: from below left, round the top, to below right (degrees, clockwise from 3 o'clock).</summary>
    private const double From = 135, Sweep = 270;

    /// <summary>The digital strip's height, and its parts' widths: the RPM, the gear, the speed, the lights.</summary>
    private const double StripH = 64, Pad = 8, TachW = 214, GearW = 52, SpeedW = 116, LightsW = 34;

    private readonly OverlaySettings settings;
    private readonly Func<double> background;
    private readonly bool classic;
    private double needle, speedNeedle;
    /// <summary>The fastest seen, so the speedometer's scale grows to it.</summary>
    private double fastest;

    public TelemetryFrame Frame { get; } = new();
    public bool Has { get; set; }
    public bool Editing { get; set; }
    /// <summary>The needles where they should be, not eased there (a still picture: the tab's preview).</summary>
    public bool Still { get; set; }

    public DashView(OverlaySettings settings, Func<double> background, bool classic = false)
    {
        this.settings = settings;
        this.background = background;
        this.classic = classic;
    }

    private static bool Tach(DashSettings d) => d.Tach || !d.Speedo;

    /// <summary>The space it's drawn in: what's on, and how.</summary>
    public static Size SpaceOf(DashSettings d)
    {
        if (d.Look == DashLook.Analog) return new Size(d.Speedo && Tach(d) ? 400 : 200, 200);
        double w = Pad + (Tach(d) ? TachW + Pad : 0) + GearW + Pad + (d.Speedo ? SpeedW + Pad : 0) + (d.Lights ? LightsW + Pad : 0);
        return new Size(w, StripH);
    }

    /// <summary>What decides its shape (its window is made again for another).</summary>
    public static string LayoutOf(DashSettings d) =>
        $"{d.Look}:{Tach(d)}:{d.Speedo}:{d.Look == DashLook.Digital && d.Lights}";

    private static Pen FrozenPen(Color c, double w)
    {
        var p = new Pen(Ui.Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static Point At(Point c, double deg, double r) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));

    private static Geometry Arc(Point c, double fromDeg, double toDeg, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, fromDeg, r), false, false);
            ctx.ArcTo(At(c, toDeg, r), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>What every look shares: the RPM, the redline and shift point, the gear and the speed.</summary>
    private readonly record struct Now(bool Has, double Top, double Red, double Shift, double Rpm, string Gear, bool ShiftNow, bool FlashOn,
        bool Kmh, double Speed, bool Tcs, bool Abs);

    private Now Read()
    {
        var f = Frame;
        bool has = Has;
        float max = has && f.MaxRpm > 1000 ? f.MaxRpm : 8000;
        double top = Math.Ceiling(max / 1000) * 1000;
        double shift = has && f.ShiftRpm > 0 ? f.ShiftRpm : max * 0.92;
        double red = Math.Min(shift, max * 0.9);
        double rpm = has ? Math.Clamp(f.Rpm, 0, top) : 0;
        string gear = !has ? "–" : f.GearNo < 0 ? "R" : f.GearNo == 0 ? "N" : f.GearNo.ToString(CultureInfo.InvariantCulture);
        bool shiftNow = has && settings.Dash.ShiftFlash && f.GearNo > 0 && rpm >= shift;
        bool kmh = (settings.Dash.Unit ?? settings.Unit) == SpeedUnit.Kmh;
        double speed = has ? Math.Max(0, f.SpeedMs * (kmh ? 3.6 : 2.23694)) : 0;
        return new Now(has, top, red, shift, rpm, gear, shiftNow, Environment.TickCount64 / 90 % 2 == 0, kmh, speed,
            has && f.Slip > 0.35f && f.Throttle > 0.3f, has && f.Abs > 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var d = settings.Dash;
        var space = classic ? new Size(200, 200) : SpaceOf(d);
        double scale = Math.Min(ActualWidth / space.Width, ActualHeight / space.Height);
        if (scale * space.Height < 20) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.PushTransform(new TranslateTransform((ActualWidth - space.Width * scale) / 2, (ActualHeight - space.Height * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));

        // the face: dark, as see-through as the Background slider (a little there while it's edited)
        double back = Editing ? Math.Max(background(), 0.35) : background();
        var n = Read();
        // (eased, so the needles sweep rather than jump between packets)
        needle = Still ? n.Rpm : needle + (n.Rpm - needle) * 0.45;
        speedNeedle = Still ? n.Speed : speedNeedle + (n.Speed - speedNeedle) * 0.45;
        fastest = Math.Max(fastest, n.Speed);

        if (classic) TachDial(dc, new Point(100, 100), back, n, true, dip);
        else if (d.Look == DashLook.Analog)
        {
            bool tach = Tach(d);
            if (tach) TachDial(dc, new Point(100, 100), back, n, !d.Speedo, dip);
            if (d.Speedo) SpeedDial(dc, new Point(tach ? 300 : 100, 100), back, n, !tach, dip);
        }
        else Strip(dc, space, back, n, dip);
        dc.Pop();
        dc.Pop();
    }

    private static void Face(DrawingContext dc, Point c, double back)
    {
        var face = new RadialGradientBrush(Color.FromArgb((byte)(235 * back), 0x10, 0x12, 0x16), Color.FromArgb((byte)(200 * back), 0x00, 0x00, 0x00));
        dc.DrawEllipse(face, back > 0.02 ? Rim : null, c, 98, 98);
    }

    /// <summary>The tach: the ring lit to the needle, the marks, the gear in the middle; the speed under it when
    /// <paramref name="speed"/> (no speedometer); the TCS and ABS lights.</summary>
    private void TachDial(DrawingContext dc, Point c, double back, Now n, bool speed, double dip)
    {
        Face(dc, c, back);
        double Angle(double r) => From + Sweep * Math.Clamp(r / n.Top, 0, 1);

        // the ring: dim all round, lit up to the needle (red past the redline)
        dc.DrawGeometry(null, Track, Arc(c, From, From + Sweep, 88));
        if (needle > 50) dc.DrawGeometry(null, needle >= n.Red ? LitRed : Lit, Arc(c, From, Angle(needle), 88));

        // the marks: every 1000 large with its number, every 500 small; red from the redline
        for (int k = 0; k <= n.Top / 500; k++)
        {
            double r = k * 500, a = Angle(r);
            bool major = k % 2 == 0, inRed = r >= n.Red;
            dc.DrawLine(major ? (inRed ? MajorRed : Major) : (inRed ? MinorRed : Minor), At(c, a, 80), At(c, a, major ? 70 : 75));
            if (!major) continue;
            var t = Text(((int)(r / 1000)).ToString(CultureInfo.InvariantCulture), Race, 13, inRed ? Red : White, dip);
            var p = At(c, a, 59);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }
        var x1000 = Text("x1000", RaceLight, 7, Dim, dip);
        dc.DrawText(x1000, new Point(c.X - x1000.Width / 2, c.Y + 85));

        // the needle, with a glow, from the gear's ring to the marks
        double na = Angle(needle);
        dc.DrawLine(Glow, At(c, na, 31), At(c, na, 78));
        dc.DrawLine(NeedlePen, At(c, na, 31), At(c, na, 78));

        // the gear, large in the middle: red and flashing at the shift point
        bool lit = n.ShiftNow && n.FlashOn;
        var g = Text(n.Gear, Race, 52, n.ShiftNow ? (n.FlashOn ? Red : White) : White, dip);
        dc.DrawEllipse(null, lit ? LitRed : Rim, new Point(c.X, c.Y - 2), 27, 27);
        dc.DrawText(g, new Point(c.X - g.Width / 2, c.Y - 2 - g.Height / 2));

        // the speed under it, and its units
        if (speed)
        {
            var s = Text(n.Has ? Math.Round(n.Speed).ToString("0", CultureInfo.CurrentCulture) : "–", Race, 30, White, dip);
            dc.DrawText(s, new Point(c.X - s.Width / 2, c.Y + 28));
            var u = Text(n.Kmh ? T("KM/H") : T("MPH"), RaceLight, 8, Dim, dip);
            dc.DrawText(u, new Point(c.X - u.Width / 2, c.Y + 58));
        }
        if (settings.Dash.Lights) Lights(dc, c, n, dip);
    }

    /// <summary>The scale's top and its marks' step: the first that holds the fastest seen.</summary>
    private (double Top, double Step) SpeedScale(bool kmh)
    {
        var scales = kmh ? new[] { (280.0, 40.0), (360, 40), (480, 60) } : new[] { (160.0, 20.0), (200, 20), (240, 40), (320, 40) };
        foreach (var s in scales) if (fastest <= s.Item1) return s;
        return scales[^1];
    }

    /// <summary>The speedometer: the ring lit to the needle, the marks, the speed large in the middle; the gear and the
    /// lights under it when <paramref name="alone"/> (no tach).</summary>
    private void SpeedDial(DrawingContext dc, Point c, double back, Now n, bool alone, double dip)
    {
        Face(dc, c, back);
        var (top, step) = SpeedScale(n.Kmh);
        double Angle(double v) => From + Sweep * Math.Clamp(v / top, 0, 1);
        dc.DrawGeometry(null, Track, Arc(c, From, From + Sweep, 88));
        if (speedNeedle > 0.5) dc.DrawGeometry(null, Lit, Arc(c, From, Angle(speedNeedle), 88));
        for (int k = 0; k <= top / (step / 2); k++)
        {
            double v = k * step / 2, a = Angle(v);
            bool major = k % 2 == 0;
            dc.DrawLine(major ? Major : Minor, At(c, a, 80), At(c, a, major ? 70 : 75));
            if (!major) continue;
            var t = Text(v.ToString("0", CultureInfo.InvariantCulture), Race, v >= 100 ? 11 : 13, White, dip);
            var p = At(c, a, 59);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }
        double na = Angle(speedNeedle);
        dc.DrawLine(Glow, At(c, na, 50), At(c, na, 78));
        dc.DrawLine(NeedlePen, At(c, na, 50), At(c, na, 78));

        var s = Text(n.Has ? Math.Round(n.Speed).ToString("0", CultureInfo.CurrentCulture) : "–", Race, 42, White, dip);
        dc.DrawText(s, new Point(c.X - s.Width / 2, c.Y - s.Height / 2 - 6));
        var u = Text(n.Kmh ? T("KM/H") : T("MPH"), RaceLight, 9, Dim, dip);
        dc.DrawText(u, new Point(c.X - u.Width / 2, c.Y + 20));
        if (!alone) return;
        // (no tach: the gear in the gap at the bottom, the lights beside it)
        bool lit = n.ShiftNow && n.FlashOn;
        var g = Text(n.Gear, Race, 24, lit ? Red : White, dip);
        var box = new Rect(c.X - 15, c.Y + 50, 30, 30);
        dc.DrawRoundedRectangle(null, lit ? ShiftRim : Rim, box, 5, 5);
        dc.DrawText(g, new Point(c.X - g.Width / 2, box.Top + (box.Height - g.Height) / 2));
        if (settings.Dash.Lights)
        {
            Light(dc, "TCS", n.Tcs, new Rect(c.X - 50, c.Y + 70, 26, 12), 8, dip);
            Light(dc, "ABS", n.Abs, new Rect(c.X + 24, c.Y + 70, 26, 12), 8, dip);
        }
    }

    /// <summary>The digital strip, left to right: the RPM's bar of lights and number, the gear, the speed, the lights.</summary>
    private void Strip(DrawingContext dc, Size space, double back, Now n, double dip)
    {
        var d = settings.Dash;
        var face = Ui.Brush(Color.FromArgb((byte)(225 * back), 0x0C, 0x0E, 0x12));
        dc.DrawRoundedRectangle(face, back > 0.02 ? Rim : null, new Rect(0.5, 0.5, space.Width - 1, space.Height - 1), 10, 10);
        double x = Pad;
        bool lit = n.ShiftNow && n.FlashOn;
        if (Tach(d))
        {
            // the bar: green, then amber from 70% of the redline, red from it; all red while the gear flashes
            const int count = 24;
            const double gap = 2, barY = 8, barH = 16;
            double segW = (TachW - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                double at = n.Top * (i + 0.5) / count;
                bool on = needle >= at;
                var brush = !on ? Unlit : lit || at >= n.Red ? Red : at >= n.Red * 0.7 ? Amber : Green;
                dc.DrawRoundedRectangle(brush, null, new Rect(x + i * (segW + gap), barY, segW, barH), 1.5, 1.5);
            }
            var rpm = Text(n.Has ? Math.Round(n.Rpm).ToString("0", CultureInfo.InvariantCulture) : "–", Race, 24, needle >= n.Red ? Red : White, dip);
            dc.DrawText(rpm, new Point(x, StripH - 6 - rpm.Height));
            var label = Text(T("RPM"), RaceLight, 9, Dim, dip);
            dc.DrawText(label, new Point(x + rpm.Width + 5, StripH - 10 - label.Height));
            x += TachW + Pad;
        }
        // the gear in a box: red and flashing at the shift point
        var box = new Rect(x, 6, GearW, StripH - 12);
        dc.DrawRoundedRectangle(null, lit ? ShiftRim : Rim, box, 6, 6);
        var g = Text(n.Gear, Race, 42, n.ShiftNow ? (n.FlashOn ? Red : White) : White, dip);
        dc.DrawText(g, new Point(box.Left + (box.Width - g.Width) / 2, box.Top + (box.Height - g.Height) / 2));
        x += GearW + Pad;
        if (d.Speedo)
        {
            var s = Text(n.Has ? Math.Round(n.Speed).ToString("0", CultureInfo.CurrentCulture) : "–", Race, 44, White, dip);
            var u = Text(n.Kmh ? T("KM/H") : T("MPH"), RaceLight, 9, Dim, dip);
            double right = x + SpeedW;
            dc.DrawText(s, new Point(right - s.Width, (StripH - s.Height) / 2 - 5));
            dc.DrawText(u, new Point(right - u.Width, StripH - 6 - u.Height));
            x += SpeedW + Pad;
        }
        if (d.Lights)
        {
            Light(dc, "TCS", n.Tcs, new Rect(x, 10, LightsW, 18), 10, dip);
            Light(dc, "ABS", n.Abs, new Rect(x, StripH - 28, LightsW, 18), 10, dip);
        }
    }

    /// <summary>TCS (the wheels spinning under power: the game doesn't say when its own works) and ABS, under a dial.</summary>
    private static void Lights(DrawingContext dc, Point c, Now n, double dip)
    {
        Light(dc, "TCS", n.Tcs, new Rect(c.X - 39, c.Y + 70, 26, 12), 8, dip);
        Light(dc, "ABS", n.Abs, new Rect(c.X + 13, c.Y + 70, 26, 12), 8, dip);
    }

    private static void Light(DrawingContext dc, string name, bool on, Rect r, double size, double dip)
    {
        var t = Text(name, Race, size, on ? Brushes.Black : Off, dip);
        dc.DrawRoundedRectangle(on ? Amber : null, on ? null : Ui.Pen(Off, 1), r, 3, 3);
        dc.DrawText(t, new Point(r.Left + (r.Width - t.Width) / 2, r.Top + (r.Height - t.Height) / 2));
    }

    private static FormattedText Text(string s, Typeface face, double size, Brush brush, double dip) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, dip);
}
