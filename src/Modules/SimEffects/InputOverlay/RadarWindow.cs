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
/// in the middle and the others round it (red beside you, amber close, white further), each fading near the edge. The
/// whole of it fades in while a car is within the range and out once none is (unless Always, or while edited).
/// </summary>
internal sealed class RadarView : FrameworkElement
{
    private static readonly Brush Me = Ui.Brush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x30), Amber = Color.FromRgb(0xFF, 0xB0, 0x1F), Far = Color.FromRgb(0xC8, 0xCC, 0xD2);
    private static readonly Pen Rim = Ui.Pen(Ui.Brush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Pen Ring = Ui.Pen(Ui.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Pen Warn = Ui.Pen(Ui.Brush(Color.FromArgb(0xC0, 0xFF, 0x3B, 0x30)), 5);

    private const double R = 96, CarW = 1.9, CarL = 4.6;

    private readonly RadarSettings settings;
    private readonly Func<double> background;
    // each car's place across, eased (iRacing's side comes as a jump)
    private readonly Dictionary<int, double> across = new();
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
        if (shown <= 0.01) { across.Clear(); return; }

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
        // a car beside you: the disc's edge glows red on its side
        if (cars.Any(k => k.Beside && k.Right < 0)) dc.DrawGeometry(null, Warn, Edge(c, 150, 210));
        if (cars.Any(k => k.Beside && k.Right > 0)) dc.DrawGeometry(null, Warn, Edge(c, -30, 30));

        dc.PushClip(new EllipseGeometry(c, R - 1, R - 1));
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

    private static Geometry Edge(Point c, double fromDeg, double toDeg)
    {
        Point At(double deg) => new(c.X + (R - 3) * Math.Cos(deg * Math.PI / 180), c.Y + (R - 3) * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(fromDeg), false, false);
            ctx.ArcTo(At(toDeg), new Size(R - 3, R - 3), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
