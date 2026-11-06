using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Controllers;
using K = DaisysApp.Applets.GameDevices.DeviceKinds;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// The Device Widget's body: a small picture of the device's main control, which always shows (a wheel's steering, a
/// stick, a throttle's lever, a handbrake; nothing for pedals and the rest), then a tile for each other axis that moved, the buttons
/// pressed and the hat pushed, side by side in the order they came. Each tile goes <see cref="Hold"/> after it last
/// changed, fading out. Drawn in a space <see cref="H"/> high, scaled to the widget's height (and narrower if it's full).
/// </summary>
internal sealed class DeviceActivity : FrameworkElement
{
    private const double H = 100, Gap = 8, TileW = 58;
    /// <summary>A tile stays this long after its control last changed, then fades out over <see cref="Fade"/>.</summary>
    private const long Hold = 2500, Fade = 600;
    /// <summary>A move smaller than this (of the axis's range) is jitter: not activity, as the tab's green dot.</summary>
    private const double Jitter = 0.015;
    private const string ThrottleColor = "#3FB950", BrakeColor = "#F85149", ClutchColor = "#58A6FF";

    private enum Main { None, Wheel, Stick, Lever, StickAndLever, Handbrake }

    private readonly ControllerState state = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly double[] rest = new double[8];
    private readonly long[] axisAt = new long[8];
    private readonly bool[] down = new bool[GameControllers.MaxButtons];
    private readonly List<int> buttonsShown = new();
    private readonly List<string> order = new();
    private long buttonsAt = long.MinValue / 2, hatAt = long.MinValue / 2;
    private int hatShown = -1, lastPov = -1;
    private string? device;
    private bool primed, connected, read;
    private Device? d;
    private int shownHash;

    // set for the time of a draw
    private DrawingContext dc = null!;
    private Brush text = null!, muted = null!, control = null!, edge = null!, accent = null!, success = null!;
    private double dip = 1;

    public DeviceActivity() => Array.Fill(axisAt, long.MinValue / 2);

    private static Main MainOf(DeviceKind kind) => kind switch
    {
        DeviceKind.WheelBase or DeviceKind.AllInOne => Main.Wheel,
        DeviceKind.Joystick or DeviceKind.Yoke => Main.Stick,
        DeviceKind.Hotas => Main.StickAndLever,
        DeviceKind.Throttle => Main.Lever,
        DeviceKind.Handbrake => Main.Handbrake,
        _ => Main.None,
    };

    /// <summary>The roles the main picture shows, which get no tile of their own.</summary>
    private static string[] MainRoles(Main main) => main switch
    {
        Main.Wheel => new[] { K.Steering },
        Main.Stick => new[] { K.StickX, K.StickY },
        Main.Lever => new[] { K.Throttle },
        Main.StickAndLever => new[] { K.StickX, K.StickY, K.Throttle },
        Main.Handbrake => new[] { K.Handbrake },
        _ => Array.Empty<string>(),
    };

    /// <summary>Roles that rest in the middle: their tile fills from there.</summary>
    private static bool Centred(string? role) =>
        role is K.Steering or K.StickX or K.StickY or K.Twist or K.Rudder or K.LeftX or K.LeftY or K.RightX or K.RightY or K.Triggers;

    /// <summary>Reads the device now (<paramref name="device"/> null: none) and draws again if anything shown changed.</summary>
    public void Show(Device? device, DevicesHub hub)
    {
        d = device;
        if (device?.Key != this.device)
        {
            this.device = device?.Key;
            primed = false;
            order.Clear();
        }
        connected = device?.Connected == true;
        read = device != null && hub.Read(device, state);
        long now = clock.ElapsedMilliseconds;
        if (read) Track(device!, now);

        var hash = new HashCode();
        hash.Add(device?.Kind);
        hash.Add(connected);
        hash.Add(read);
        foreach (var id in Visible(now)) { hash.Add(id); hash.Add((int)(Alpha(id, now) * 20)); }
        if (read && device != null)
        {
            foreach (var role in MainRoles(MainOf(device.Kind)))
                if (device.Roles.TryGetValue(role, out int a) && a >= 0) hash.Add((int)(state.Axes[a] * 400));
            foreach (int a in Enumerable.Range(0, 8).Where(a => axisAt[a] + Hold + Fade > now)) hash.Add((int)(state.Axes[a] * 200));
        }
        foreach (int b in buttonsShown) hash.Add(b);
        hash.Add(down.Count(b => b));
        hash.Add(hatShown);
        int value = hash.ToHashCode();
        if (value == shownHash) return;
        shownHash = value;
        InvalidateVisual();
    }

    /// <summary>What changed since the last reading, and when.</summary>
    private void Track(Device device, long now)
    {
        int buttons = Math.Min(device.Info?.Buttons ?? 0, down.Length);
        if (!primed)
        {
            // the first reading is where things are: nothing has moved yet
            primed = true;
            for (int a = 0; a < 8; a++) { rest[a] = state.Axes[a]; axisAt[a] = long.MinValue / 2; }
            for (int b = 0; b < down.Length; b++) down[b] = b < buttons && state.Buttons[b];
            buttonsShown.Clear();
            buttonsAt = hatAt = long.MinValue / 2;
            hatShown = -1;
            lastPov = (device.Info?.Povs ?? 0) > 0 ? state.Povs[0] : -1;
            return;
        }
        var hidden = MainRoles(MainOf(device.Kind)).Select(r => device.Roles.TryGetValue(r, out int a) ? a : -1).ToHashSet();
        for (int a = 0; a < 8; a++)
        {
            double v = state.Axes[a];
            if (!device.HasAxis[a] || double.IsNaN(v)) continue;
            if (double.IsNaN(rest[a])) { rest[a] = v; continue; }
            if (Math.Abs(v - rest[a]) <= Jitter) continue;
            rest[a] = v;
            if (!hidden.Contains(a)) axisAt[a] = now;
        }
        bool anyWasDown = down.Take(buttons).Any(x => x);
        for (int b = 0; b < buttons; b++)
        {
            bool on = state.Buttons[b];
            if (on == down[b]) continue;
            down[b] = on;
            buttonsAt = now;
            if (!on) continue;
            // a press with nothing held starts the tile again; one while others are held joins them
            if (!anyWasDown) buttonsShown.Clear();
            anyWasDown = true;
            if (!buttonsShown.Contains(b)) buttonsShown.Add(b);
        }
        int pov = (device.Info?.Povs ?? 0) > 0 ? state.Povs[0] : -1;
        if (pov != lastPov)
        {
            // pushed or let go: the tile shows the way it was pushed last
            lastPov = pov;
            hatAt = now;
            if (pov >= 0) hatShown = pov;
        }
    }

    private long At(string id) => id switch
    {
        "buttons" => buttonsShown.Count > 0 ? buttonsAt : long.MinValue / 2,
        "hat" => hatShown >= 0 ? hatAt : long.MinValue / 2,
        _ => axisAt[id[1] - '0'],
    };

    private double Alpha(string id, long now)
    {
        long age = now - At(id);
        return age <= Hold ? 1 : Math.Max(0, 1 - (age - Hold) / (double)Fade);
    }

    /// <summary>The tiles showing now, in the order they came (a tile that went and comes back goes last).</summary>
    private List<string> Visible(long now)
    {
        var ids = Enumerable.Range(0, 8).Select(a => $"a{a}").Append("buttons").Append("hat");
        foreach (var id in ids)
        {
            bool on = Alpha(id, now) > 0;
            if (on && !order.Contains(id)) order.Add(id);
            else if (!on) order.Remove(id);
        }
        return order.ToList();
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 420 : available.Width, double.IsInfinity(available.Height) ? H : available.Height);

    protected override void OnRender(DrawingContext context)
    {
        dc = context;
        Brush B(string key) => Ui.Brush(this, key);
        text = B("TextBrush"); muted = B("TextSecondaryBrush"); control = B("ControlBrush"); edge = B("ControlBorderStrongBrush"); accent = B("AccentBrush"); success = B("SuccessBrush");
        dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // (catches right-clicks all over)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (d == null) return;
        long now = clock.ElapsedMilliseconds;
        var main = MainOf(d.Kind);
        var tiles = Visible(now);
        double width = MainWidth(main) + tiles.Sum(t => TileWidth(t) + Gap);
        double s = Math.Min(ActualHeight / H, width > 0 ? ActualWidth / width : double.MaxValue);
        if (s <= 0 || double.IsInfinity(s)) return;
        dc.PushTransform(new TranslateTransform(0, (ActualHeight - H * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        if (!connected) dc.PushOpacity(0.35);
        double x = 0;
        switch (main)
        {
            case Main.Wheel: Wheel(x); break;
            case Main.Stick: Stick(x); break;
            case Main.Lever: Lever(x, Role(K.Throttle)); break;
            case Main.StickAndLever: Stick(x); Lever(x + 100 + Gap, Role(K.Throttle)); break;
            case Main.Handbrake: Handbrake(x); break;
        }
        x += MainWidth(main);
        foreach (var id in tiles)
        {
            if (x > 0) x += Gap;
            dc.PushOpacity(Alpha(id, now));
            if (id == "buttons") Buttons(x);
            else if (id == "hat") Hat(x);
            else Axis(x, id[1] - '0');
            dc.Pop();
            x += TileWidth(id);
        }
        if (!connected) dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static double MainWidth(Main main) => main switch
    {
        Main.Wheel or Main.Stick or Main.Handbrake => 100,
        Main.Lever => 40,
        Main.StickAndLever => 100 + Gap + 40,
        _ => 0,
    };

    private double TileWidth(string id) => id == "buttons" ? Math.Max(TileW, Measure(ButtonsText, 26, true) + 16) : TileW;

    private string ButtonsText => string.Join(" ", buttonsShown.Select(b => (b + 1).ToString()));

    // ---------------------------------------------------------------- pieces

    /// <summary>The role's axis as shown (0 – 1, the way the control goes); NaN: no axis does it.</summary>
    private double Role(string id) => d != null && d.Roles.TryGetValue(id, out int a) ? Value(a) : double.NaN;

    private double Value(int axis)
    {
        if (d == null || axis < 0 || !read || double.IsNaN(state.Axes[axis])) return double.NaN;
        return d.Shown(axis, state.Axes[axis]);
    }

    private FormattedText Text(string s, double size, Brush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Ui.Typeface("Segoe UI", bold ? FontWeights.SemiBold : FontWeights.Normal), size, brush, dip);

    private double Measure(string s, double size, bool bold) => Text(s, size, text, bold).WidthIncludingTrailingWhitespace;

    private void Label(string s, double cx, double y, double size, Brush brush, bool bold = false, double max = TileW + 6)
    {
        var f = Text(s, size, brush, bold);
        f.MaxTextWidth = max;
        f.MaxLineCount = 1;
        f.Trimming = TextTrimming.CharacterEllipsis;
        f.TextAlignment = TextAlignment.Center;
        dc.DrawText(f, new Point(cx - max / 2, y));
    }

    private Pen Line(Brush brush, double thick) => new(brush, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private void Wheel(double x)
    {
        double v = Role(K.Steering), turn = double.IsNaN(v) ? 0 : (v - 0.5) * 450, cx = x + 50, cy = 46, r = 40;
        dc.PushTransform(new RotateTransform(turn, cx, cy));
        dc.DrawEllipse(null, Ui.Pen(muted, r * 0.18), new Point(cx, cy), r * 0.88, r * 0.88);
        var spoke = Line(muted, r * 0.15);
        dc.DrawLine(spoke, new Point(cx - r * 0.84, cy), new Point(cx + r * 0.84, cy));
        dc.DrawLine(spoke, new Point(cx, cy), new Point(cx, cy + r * 0.84));
        dc.DrawEllipse(control, Ui.Pen(edge, 1.5), new Point(cx, cy), r * 0.28, r * 0.28);
        dc.DrawLine(Line(accent, r * 0.14), new Point(cx, cy - r * 0.8), new Point(cx, cy - r * 0.96));
        dc.Pop();
        Label(double.IsNaN(v) ? "–" : $"{(v - 0.5) * 200:+0;-0;0}%", cx, 86, 10, muted, max: 100);
    }

    private void Stick(double x)
    {
        double cx = x + 50, cy = 50, r = 44;
        dc.DrawEllipse(control, Ui.Pen(edge, 1.5), new Point(cx, cy), r, r);
        dc.DrawLine(Ui.Pen(edge, 1), new Point(cx - r, cy), new Point(cx + r, cy));
        dc.DrawLine(Ui.Pen(edge, 1), new Point(cx, cy - r), new Point(cx, cy + r));
        double sx = Role(K.StickX), sy = Role(K.StickY);
        double px = cx + ((double.IsNaN(sx) ? 0.5 : sx) - 0.5) * 2 * (r - 10), py = cy + ((double.IsNaN(sy) ? 0.5 : sy) - 0.5) * 2 * (r - 10);
        dc.DrawLine(Line(muted, 4), new Point(cx, cy), new Point(px, py));
        dc.DrawEllipse(accent, null, new Point(px, py), 9, 9);
    }

    /// <summary>A throttle lever in its slot: forward (up) is 100%.</summary>
    private void Lever(double x, double reading)
    {
        double v = double.IsNaN(reading) ? double.NaN : 1 - reading, cx = x + 20, top = 6, track = 76;
        dc.DrawRoundedRectangle(control, Ui.Pen(edge, 1), new Rect(cx - 5, top, 10, track), 5, 5);
        bool none = double.IsNaN(v);
        double p = none ? 0 : Math.Clamp(v, 0, 1), ly = top + (1 - p) * track;
        if (!none) dc.DrawRoundedRectangle(accent, null, new Rect(cx - 5, ly, 10, top + track - ly), 5, 5);
        dc.DrawRoundedRectangle(none ? edge : text, null, new Rect(cx - 16, ly - 6, 32, 12), 4, 4);
        Label(none ? "–" : $"{v * 100:0}%", cx, 86, 10, muted, max: 44);
    }

    /// <summary>
    /// The tab's handbrake, small: the lever nearly flat at rest, coming up as it's pulled; its % and its axis (a
    /// handbrake that's a button: any button held is pulled).
    /// </summary>
    private void Handbrake(double x)
    {
        int axis = d != null && d.Roles.TryGetValue(K.Handbrake, out int a) ? a : -1;
        double v = Role(K.Handbrake);
        if (axis < 0 && read) v = down.Any(b => b) ? 1 : 0;
        double p = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1), angle = (8 + 52 * p) * Math.PI / 180;
        Point pivot = new(x + 24, 58), tip = new(pivot.X + Math.Cos(angle) * 62, pivot.Y - Math.Sin(angle) * 62);
        dc.DrawRoundedRectangle(control, Ui.Pen(edge, 1), new Rect(x + 10, 55, 40, 8), 3, 3);
        dc.DrawLine(Line(muted, 5), pivot, tip);
        dc.DrawEllipse(accent, null, tip, 6, 6);
        dc.DrawEllipse(text, null, pivot, 3, 3);
        Label(double.IsNaN(v) ? "–" : $"{v * 100:0}%", x + 50, 68, 12, text, bold: true, max: 100);
        Label(axis >= 0 ? K.AxisName(axis) : T("Button"), x + 50, 84, 10, muted, max: 100);
    }

    /// <summary>An axis that moved: its value, a bar, and which axis it is (X Axis, Z Rotation…).</summary>
    private void Axis(double x, int axis)
    {
        string? role = d?.Roles.FirstOrDefault(p => p.Value == axis).Key;
        string name = K.AxisName(axis);
        var brush = role switch
        {
            K.Throttle or K.Throttle2 => Ui.Brush(ThrottleColor),
            K.Brake or K.LeftBrake or K.RightBrake => Ui.Brush(BrakeColor),
            K.Clutch => Ui.Brush(ClutchColor),
            _ => accent,
        };
        double v = Value(axis), cx = x + TileW / 2, top = 18, track = 62, w = 18;
        Label(double.IsNaN(v) ? "–" : $"{v * 100:0}%", cx, 0, 12, text, bold: true);
        dc.DrawRoundedRectangle(control, Ui.Pen(edge, 1), new Rect(cx - w / 2, top, w, track), 5, 5);
        if (!double.IsNaN(v))
        {
            double p = Math.Clamp(v, 0, 1);
            if (Centred(role))
            {
                double mid = top + track / 2, y = top + (1 - p) * track;
                dc.DrawRectangle(brush, null, new Rect(cx - w / 2 + 2, Math.Min(mid, y), w - 4, Math.Abs(y - mid)));
                dc.DrawLine(Ui.Pen(edge, 1), new Point(cx - w / 2, mid), new Point(cx + w / 2, mid));
            }
            else if (p > 0) dc.DrawRoundedRectangle(brush, null, new Rect(cx - w / 2, top + (1 - p) * track, w, p * track), 5, 5);
        }
        Label(name, cx, 84, 10, muted);
    }

    /// <summary>The buttons pressed last (held together), by number.</summary>
    private void Buttons(double x)
    {
        double w = TileWidth("buttons");
        bool held = buttonsShown.Any(b => down[b]);
        dc.DrawRoundedRectangle(held ? success : control, Ui.Pen(edge, 1), new Rect(x, 14, w, 64), 8, 8);
        var f = Text(ButtonsText, 26, held ? Brushes.Black : text, true);
        dc.DrawText(f, new Point(x + (w - f.Width) / 2, 46 - f.Height / 2));
        Label(buttonsShown.Count > 1 ? T("Buttons") : T("Button"), x + w / 2, 84, 10, muted, max: w + 6);
    }

    /// <summary>The hat, the way it was pushed last.</summary>
    private void Hat(double x)
    {
        double cx = x + TileW / 2, cy = 46, r = 24;
        bool held = read && state.Povs[0] >= 0;
        dc.DrawEllipse(control, Ui.Pen(edge, 1), new Point(cx, cy), r, r);
        double a = hatShown / 100.0 * Math.PI / 180 - Math.PI / 2;
        dc.DrawLine(Line(held ? success : muted, 6), new Point(cx, cy), new Point(cx + Math.Cos(a) * (r - 5), cy + Math.Sin(a) * (r - 5)));
        Label(T("Hat"), cx, 84, 10, muted);
    }
}
