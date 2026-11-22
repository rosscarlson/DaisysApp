using System.Windows;
using System.Windows.Media;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Radar overlay (since 4.18.14): the cars round yours from above, yours in the middle, ahead up, fading in as one
/// comes within its range and out as they all go. From the game's <see cref="TelemetryFrame.Nearby"/> (iRacing's).
/// Core's widget (card: false, its Background slider the disc's), drawn each new reading while it shows.
/// </summary>
internal sealed class RadarWindow : Widget
{
    private readonly OverlayReader reader;
    private readonly RadarView view;

    public RadarWindow(OverlaySettings settings, OverlayReader reader, Action save, Action delete)
        : base(settings.Radar, save, delete, card: false, firstSize: new Size(220, 220))
    {
        this.reader = reader;
        Title = T("Radar");
        MinWidth = MinHeight = 80;
        Aspect = 1;
        view = new RadarView(settings.Radar, () => BackgroundOpacity);
        Body = view;
        BackgroundChanged += view.InvalidateVisual;
        EditingChanged += () => { view.Editing = Editing; view.InvalidateVisual(); };
        _ = new FrameHook(this, reader, dash: true, Draw, Draw);
    }

    /// <summary>Its settings changed on the tab: drawn again.</summary>
    public void Changed() => view.InvalidateVisual();

    private void Draw()
    {
        view.Has = reader.Frame(view.Frame);
        view.InvalidateVisual();
    }
}

/// <summary>
/// The radar itself, in a 200 × 200 space: a disc its range across in each direction, a ring at half of it, your car
/// in the middle and the others round it (red beside you, amber close, white further), each fading near the edge; (since
/// 4.19.1) a red wedge ahead, behind, left or right of yours, stronger the closer the nearest car on that side. The
/// whole of it fades in while a car is within the range and out once none is (unless Always, or while edited).
/// </summary>
internal sealed class RadarView : FrameworkElement
{
    private static readonly Brush Me = Ui.Brush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x30), Amber = Color.FromRgb(0xFF, 0xB0, 0x1F), Far = Color.FromRgb(0xC8, 0xCC, 0xD2);
    private static readonly Pen Rim = Ui.Pen(Ui.Brush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Pen Ring = Ui.Pen(Ui.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);

    private const double R = 96, CarW = 1.9, CarL = 4.6;

    private readonly RadarSettings settings;
    private readonly Func<double> background;
    // each car's place across, eased (iRacing's side comes as a jump)
    private readonly Dictionary<int, double> across = new();
    // each wedge's strength (ahead, right, behind, left), eased
    private readonly double[] zones = new double[4];
    private double shown;
    private long last;

    public TelemetryFrame Frame { get; } = new();
    public bool Has { get; set; }
    public bool Editing { get; set; }
    /// <summary>Drawn as it is, not faded or eased (the tab's preview).</summary>
    public bool Still { get; set; }

    public RadarView(RadarSettings settings, Func<double> background)
    {
        this.settings = settings;
        this.background = background;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 200;
        if (scale * 200 < 20) return;
        var cars = Has ? Frame.Nearby ?? Array.Empty<NearCar>() : Array.Empty<NearCar>();
        double range = Math.Max(10, settings.Range);

        // fades in as a car comes within the range (fully by 70% of it), out once none is: quicker in than out
        double nearest = cars.Count == 0 ? double.MaxValue : cars.Min(c => Math.Sqrt(c.Ahead * c.Ahead + c.Right * c.Right));
        double want = Editing || settings.Always || Still ? 1 : Math.Clamp((range - nearest) / (range * 0.3), 0, 1);
        long now = Environment.TickCount64;
        double dt = last == 0 ? 0 : Math.Min(0.25, (now - last) / 1000.0);
        last = now;
        // (the game stopped: gone at once, not left half faded)
        shown = Still || (!Has && want == 0) ? want : want > shown ? Math.Min(want, shown + dt / 0.25) : Math.Max(want, shown - dt / 0.8);
        if (shown <= 0.01) { across.Clear(); Array.Clear(zones); return; }

        dc.PushTransform(new TranslateTransform((ActualWidth - 200 * scale) / 2, (ActualHeight - 200 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushOpacity(shown);
        var c = new Point(100, 100);
        double back = Editing ? Math.Max(background(), 0.35) : background();
        var face = new RadialGradientBrush(Color.FromArgb((byte)(150 * back), 0x18, 0x1C, 0x22), Color.FromArgb((byte)(235 * back), 0x00, 0x00, 0x00));
        dc.DrawEllipse(face, back > 0.02 ? Rim : null, c, R, R);
        dc.DrawEllipse(null, Ring, c, R / 2, R / 2);
        dc.DrawLine(Ring, new Point(c.X, c.Y - R), new Point(c.X, c.Y + R));
        dc.DrawLine(Ring, new Point(c.X - R, c.Y), new Point(c.X + R, c.Y));

        double px = R / range;
        dc.PushClip(new EllipseGeometry(c, R - 1, R - 1));
        if (settings.Zones) DrawZones(dc, c, cars, range, dt);

        var seen = new HashSet<int>();
        foreach (var k in cars)
        {
            seen.Add(k.Id);
            double x = across.TryGetValue(k.Id, out double was) && !Still ? was + (k.Right - was) * Math.Min(1, dt * 10) : k.Right;
            across[k.Id] = x;
            double dist = Math.Sqrt(k.Ahead * k.Ahead + x * x);
            double fade = Math.Clamp((range - dist) / (range * 0.25), 0, 1);
            if (fade <= 0) continue;
            var color = k.Beside || Math.Abs(k.Ahead) < CarL ? Red : dist < Math.Min(12, range * 0.4) ? Amber : Far;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(230 * fade), color.R, color.G, color.B));
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, Car(c, x, k.Ahead, px), 2, 2);
        }
        foreach (int id in across.Keys.Where(id => !seen.Contains(id)).ToList()) across.Remove(id);
        dc.Pop();

        dc.DrawRoundedRectangle(Me, null, Car(c, 0, 0, px), 2, 2);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    /// <summary>A car's shape, at least a few units each way so it shows at a wide range.</summary>
    private static Rect Car(Point c, double right, double ahead, double px)
    {
        double w = Math.Max(5, CarW * px), l = Math.Max(11, CarL * px);
        return new Rect(c.X + right * px - w / 2, c.Y - ahead * px - l / 2, w, l);
    }

    /// <summary>
    /// The wedges: the nearest car on each side (beside you left or right, else by where it is from yours) makes that
    /// side's wedge show, faint at the half range and full within a car length, red fading out from your car.
    /// </summary>
    private void DrawZones(DrawingContext dc, Point c, IReadOnlyList<NearCar> cars, double range, double dt)
    {
        Span<double> want = stackalloc double[4];
        double reach = range / 2;
        foreach (var k in cars)
        {
            double x = across.TryGetValue(k.Id, out double was) ? was : k.Right;
            int side = k.Beside || Math.Abs(x) > Math.Abs(k.Ahead) ? (x > 0 ? 1 : 3) : k.Ahead >= 0 ? 0 : 2;
            double gap = Math.Max(0, Math.Sqrt(k.Ahead * k.Ahead + x * x) - CarL);
            want[side] = Math.Max(want[side], k.Beside ? 1 : Math.Clamp(1 - gap / reach, 0, 1));
        }
        for (int i = 0; i < 4; i++)
        {
            zones[i] = Still ? want[i] : want[i] > zones[i] ? Math.Min(want[i], zones[i] + dt / 0.15) : Math.Max(want[i], zones[i] - dt / 0.5);
            if (zones[i] <= 0.01) continue;
            byte a = (byte)(200 * zones[i]);
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = R, RadiusY = R,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(a, Red.R, Red.G, Red.B), 0.1),
                    new GradientStop(Color.FromArgb((byte)(a / 2), Red.R, Red.G, Red.B), 0.55),
                    new GradientStop(Color.FromArgb(0, Red.R, Red.G, Red.B), 1),
                },
            };
            brush.Freeze();
            dc.DrawGeometry(brush, null, Wedge(c, i * 90 - 90, 32));
        }
    }

    /// <summary>A slice of the disc from its middle, <paramref name="half"/> degrees each side of <paramref name="deg"/> (0 right, clockwise).</summary>
    private static Geometry Wedge(Point c, double deg, double half)
    {
        Point At(double d) => new(c.X + R * Math.Cos(d * Math.PI / 180), c.Y + R * Math.Sin(d * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true, true);
            ctx.LineTo(At(deg - half), false, false);
            ctx.ArcTo(At(deg + half), new Size(R, R), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }
}
