using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Controllers;
using K = DaisysApp.Applets.GameDevices.DeviceKinds;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// The picture of a device doing what it's doing: pedals for pedals, a wheel for a wheel. Drawn in a space of
/// <see cref="W"/> × <see cref="H"/> scaled to fit, so the tab and a widget are the same picture at any size, and only
/// when something changed. Grey while the device isn't plugged in.
/// </summary>
internal sealed class DeviceArt : FrameworkElement
{
    private const double W = 480, H = 280;
    private const string ThrottleColor = "#3FB950", BrakeColor = "#F85149", ClutchColor = "#58A6FF";

    private readonly ControllerState state = new();
    private Dictionary<string, int> roles = new();
    private HashSet<int> inverted = new();
    private DeviceKind kind;
    private bool connected, read;
    private int buttons, hats, firstGear, shownHash;
    private bool[] hasAxis = new bool[8];

    // set for the time of a draw
    private DrawingContext dc = null!;
    private Brush text = null!, muted = null!, control = null!, edge = null!, accent = null!, success = null!;
    private double dip = 1;

    /// <summary>Shows the device's reading now (<paramref name="d"/> null: no device).</summary>
    public void Show(Device? d, DevicesHub hub)
    {
        bool has = d != null && hub.Read(d, state);
        var hash = new HashCode();
        hash.Add(d?.Kind);
        hash.Add(d?.Connected);
        hash.Add(has);
        hash.Add(d?.FirstGear);
        if (d != null)
        {
            foreach (var (id, axis) in d.Roles) hash.Add(HashCode.Combine(id, axis));
            foreach (int i in d.Known.Inverted) hash.Add(i);
        }
        if (has)
        {
            foreach (double a in state.Axes) hash.Add(double.IsNaN(a) ? -1 : (int)(a * 500));
            for (int i = 0; i < (d!.Info?.Buttons ?? 0); i++) if (state.Buttons[i]) hash.Add(i);
            foreach (int p in state.Povs) hash.Add(p);
        }
        int value = hash.ToHashCode();
        if (value == shownHash) return;
        shownHash = value;
        read = has;
        connected = d?.Connected == true;
        kind = d?.Kind ?? DeviceKind.Other;
        roles = d?.Roles ?? new();
        inverted = d?.Known.Inverted ?? new();
        buttons = d?.Info?.Buttons ?? 0;
        hats = d?.Info?.Povs ?? 0;
        firstGear = d?.FirstGear ?? 0;
        hasAxis = d?.HasAxis ?? new bool[8];
        InvalidateVisual();
    }

    private double Value(int axis)
    {
        if (axis < 0 || !read || double.IsNaN(state.Axes[axis])) return double.NaN;
        return inverted.Contains(axis) ? 1 - state.Axes[axis] : state.Axes[axis];
    }

    /// <summary>The role's axis, 0 – 1 (NaN: no axis does it).</summary>
    private double Role(string id) => roles.TryGetValue(id, out int a) ? Value(a) : double.NaN;

    /// <summary>As <see cref="Role"/>, centred where it has no axis (a stick, a wheel).</summary>
    private double Centred(string id) { double v = Role(id); return double.IsNaN(v) ? 0.5 : v; }

    private bool Down(int button) => read && button >= 0 && button < buttons && state.Buttons[button];

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? W : available.Width, double.IsInfinity(available.Height) ? H : available.Height);

    protected override void OnRender(DrawingContext context)
    {
        dc = context;
        Brush B(string key) => Ui.Brush(this, key);
        text = B("TextBrush"); muted = B("TextSecondaryBrush"); control = B("ControlBrush"); edge = B("ControlBorderStrongBrush"); accent = B("AccentBrush"); success = B("SuccessBrush");
        dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // (catches right-clicks all over)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double s = Math.Min(ActualWidth / W, ActualHeight / H);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - W * s) / 2, (ActualHeight - H * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        if (!connected) dc.PushOpacity(0.35);
        switch (kind)
        {
            case DeviceKind.RacingPedals: RacingPedals(40, 20, 400, 220); break;
            case DeviceKind.FlightPedals: FlightPedals(); break;
            case DeviceKind.Throttle: Levers(150, 20, 180, 220); break;
            case DeviceKind.Joystick:
                Stick(240, 125, 100);
                if (!double.IsNaN(Role(K.Throttle))) Lever(400, 25, 34, 200, Role(K.Throttle), T("Throttle"));
                break;
            case DeviceKind.Hotas:
                Stick(145, 125, 100);
                Levers(290, 20, 170, 220);
                break;
            case DeviceKind.Yoke: Yoke(); break;
            case DeviceKind.WheelBase: Wheel(240, 125, 105); break;
            case DeviceKind.AllInOne:
                Wheel(115, 125, 95);
                RacingPedals(225, 20, 150, 220);
                Gate(385, 50, 85, 130);
                break;
            case DeviceKind.HShifter: Gate(130, 25, 220, 210); break;
            case DeviceKind.SeqShifter: Sequential(); break;
            case DeviceKind.Handbrake: Handbrake(); break;
            case DeviceKind.Xbox: Pad(true); break;
            case DeviceKind.Gamepad: Pad(false); break;
            case DeviceKind.ButtonBox: ButtonBox(); break;
            default: Generic(); break;
        }
        if (kind is not (DeviceKind.Xbox or DeviceKind.Gamepad or DeviceKind.ButtonBox or DeviceKind.HShifter or DeviceKind.SeqShifter)) Pressed();
        if (!connected) dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    // ---------------------------------------------------------------- pieces

    private void Label(string s, double x, double y, double size = 13, Brush? brush = null, TextAlignment align = TextAlignment.Center, bool bold = false)
    {
        var f = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush ?? text, dip) { TextAlignment = align };
        dc.DrawText(f, new Point(x, y));
    }

    private static string Percent(double v) => double.IsNaN(v) ? "–" : $"{v * 100:0}%";

    private Pen Line(Brush brush, double thick = 2) => new(brush, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    /// <summary>The buttons down now, under the picture.</summary>
    private void Pressed()
    {
        if (!read || buttons == 0) return;
        var down = Enumerable.Range(0, buttons).Where(i => state.Buttons[i]).Select(i => (i + 1).ToString()).Take(16).ToList();
        if (down.Count > 0) Label(F("Buttons: {0}", string.Join(" ", down)), W / 2, 256, 14, success);
    }

    /// <summary>A pedal, as a level: its face rises from the bottom of its track as it's pressed, the track filling under it.</summary>
    private void Pedal(double x, double y, double w, double h, double v, string name, string color)
    {
        var brush = Ui.Brush(color);
        bool none = double.IsNaN(v);
        double p = none ? 0 : Math.Clamp(v, 0, 1);
        Label(Percent(v), x + w / 2, y, 15, text, bold: true);
        double top = y + 24, track = h - 50;
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(x, top, w, track), 8, 8);
        if (!none && p > 0)
        {
            var fill = brush.Clone();
            fill.Opacity = 0.35;
            dc.DrawRoundedRectangle(fill, null, new Rect(x, top + (1 - p) * track, w, p * track), 8, 8);
        }
        double face = Math.Min(44, track / 3);
        var plate = new Rect(x + 5, top + (1 - p) * (track - face), w - 10, face);
        dc.DrawRoundedRectangle(none ? edge : brush, null, plate, 6, 6);
        for (int i = 1; i <= 3; i++)
        {
            double ly = plate.Top + plate.Height * i / 4;
            dc.DrawLine(new Pen(Brushes.Black, 2) { Brush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) }, new Point(plate.Left + 6, ly), new Point(plate.Right - 6, ly));
        }
        Label(name, x + w / 2, y + h - 22, 13, muted);
    }

    private void RacingPedals(double x, double y, double w, double h)
    {
        double gap = w * 0.08, each = (w - 2 * gap) / 3;
        Pedal(x, y, each, h, Role(K.Clutch), T("Clutch"), ClutchColor);
        Pedal(x + each + gap, y, each, h, Role(K.Brake), T("Brake"), BrakeColor);
        Pedal(x + 2 * (each + gap), y, each, h, Role(K.Throttle), T("Throttle"), ThrottleColor);
    }

    private void FlightPedals()
    {
        // the bar swings about its middle with the rudder, each pedal forward or back on it; its brake fills it
        double rudder = Role(K.Rudder), r = double.IsNaN(rudder) ? 0 : rudder - 0.5;
        double angle = r * 56, cx = 240, cy = 120, reach = 125;
        double rad = angle * Math.PI / 180, dx = Math.Cos(rad) * reach, dy = Math.Sin(rad) * reach;
        dc.DrawLine(Line(edge, 10), new Point(cx - dx, cy + dy), new Point(cx + dx, cy - dy));
        dc.DrawEllipse(accent, null, new Point(cx, cy), 9, 9);
        Plate(cx - dx, cy + dy, Role(K.LeftBrake), T("Left brake"));
        Plate(cx + dx, cy - dy, Role(K.RightBrake), T("Right brake"));
        Label(T("Rudder") + " " + Percent(double.IsNaN(rudder) ? double.NaN : rudder), 240, 18, 14, text, bold: true);
        double bar = 240;
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(cx - bar / 2, 246, bar, 10), 5, 5);
        if (!double.IsNaN(rudder)) dc.DrawEllipse(accent, null, new Point(cx - bar / 2 + rudder * bar, 251), 8, 8);
    }

    private void Plate(double x, double y, double brake, string name)
    {
        var r = new Rect(x - 42, y - 50, 84, 100);
        dc.DrawRoundedRectangle(control, new Pen(edge, 2), r, 12, 12);
        if (!double.IsNaN(brake))
        {
            double h = Math.Clamp(brake, 0, 1) * (r.Height - 8);
            dc.DrawRoundedRectangle(Ui.Brush(BrakeColor), null, new Rect(r.Left + 4, r.Bottom - 4 - h, r.Width - 8, h), 9, 9);
        }
        Label(Percent(brake), x, y - 22, 15, text, bold: true);
        Label(name, x, y + 2, 12, text);
    }

    /// <summary>A throttle lever in a slot: forward (up) is 100%. Devices send a throttle as they do a stick's Y, lowest
    /// forward, so the reading is turned over here; one that does the opposite takes Invert.</summary>
    private void Lever(double x, double y, double w, double h, double reading, string name)
    {
        double v = 1 - reading;
        Label(Percent(v), x + w / 2, y, 14, text, bold: true);
        double top = y + 24, track = h - 48;
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(x + w / 2 - 6, top, 12, track), 6, 6);
        bool none = double.IsNaN(v);
        double p = none ? 0 : Math.Clamp(v, 0, 1), ly = top + (1 - p) * track;
        if (!none) dc.DrawRoundedRectangle(accent, null, new Rect(x + w / 2 - 6, ly, 12, top + track - ly), 6, 6);
        dc.DrawRoundedRectangle(none ? edge : text, null, new Rect(x, ly - 9, w, 18), 5, 5);
        Label(name, x + w / 2, y + h - 20, 12, muted);
    }

    private void Levers(double x, double y, double w, double h)
    {
        var list = new List<(double V, string Name)>();
        if (!double.IsNaN(Role(K.Throttle))) list.Add((Role(K.Throttle), roles.ContainsKey(K.Throttle2) && !double.IsNaN(Role(K.Throttle2)) ? T("Left") : T("Throttle")));
        if (!double.IsNaN(Role(K.Throttle2))) list.Add((Role(K.Throttle2), T("Right")));
        if (!double.IsNaN(Role(K.Slider))) list.Add((Role(K.Slider), T("Slider")));
        if (list.Count == 0) list.Add((double.NaN, T("Throttle")));
        double each = Math.Min(70, w / list.Count), lever = each * 0.75, start = x + (w - each * list.Count) / 2;
        for (int i = 0; i < list.Count; i++) Lever(start + i * each + (each - lever) / 2, y, lever, h, list[i].V, list[i].Name);
    }

    private void Stick(double cx, double cy, double r)
    {
        double twist = Role(K.Twist);
        var pen = new Pen(edge, 2);
        dc.DrawEllipse(control, pen, new Point(cx, cy), r, r);
        dc.DrawLine(new Pen(edge, 1), new Point(cx - r, cy), new Point(cx + r, cy));
        dc.DrawLine(new Pen(edge, 1), new Point(cx, cy - r), new Point(cx, cy + r));
        double px = cx + (Centred(K.StickX) - 0.5) * 2 * (r - 14), py = cy + (Centred(K.StickY) - 0.5) * 2 * (r - 14);
        dc.DrawLine(Line(muted, 6), new Point(cx, cy), new Point(px, py));
        if (!double.IsNaN(twist))
        {
            // twist: a tick round the knob
            double a = (twist - 0.5) * Math.PI * 1.5 - Math.PI / 2, knob = 25;
            dc.DrawEllipse(null, new Pen(edge, 2), new Point(px, py), knob, knob);
            dc.DrawLine(Line(accent, 4), new Point(px + Math.Cos(a) * (knob - 7), py + Math.Sin(a) * (knob - 7)), new Point(px + Math.Cos(a) * (knob + 4), py + Math.Sin(a) * (knob + 4)));
        }
        dc.DrawEllipse(accent, null, new Point(px, py), 15, 15);
        if (hats > 0) Hat(cx + r - 2, cy - r + 2, 20, 0);
    }

    /// <summary>A hat: its eight ways round, the one it's pushed lit.</summary>
    private void Hat(double cx, double cy, double r, int hat)
    {
        dc.DrawEllipse(control, new Pen(edge, 1), new Point(cx, cy), r, r);
        int p = read ? state.Povs[hat] : -1;
        if (p < 0) { dc.DrawEllipse(edge, null, new Point(cx, cy), 3, 3); return; }
        double a = p / 100.0 * Math.PI / 180 - Math.PI / 2;
        dc.DrawLine(Line(success, 5), new Point(cx, cy), new Point(cx + Math.Cos(a) * (r - 4), cy + Math.Sin(a) * (r - 4)));
    }

    private void Wheel(double cx, double cy, double r)
    {
        double v = Role(K.Steering), turn = double.IsNaN(v) ? 0 : (v - 0.5) * 450;
        Label(double.IsNaN(v) ? "–" : $"{(v - 0.5) * 200:+0;-0;0}%", cx, 2, 15, text, bold: true);
        dc.PushTransform(new RotateTransform(turn, cx, cy));
        dc.DrawEllipse(null, new Pen(muted, r * 0.16), new Point(cx, cy), r * 0.88, r * 0.88);
        var spoke = Line(muted, r * 0.14);
        dc.DrawLine(spoke, new Point(cx - r * 0.84, cy), new Point(cx + r * 0.84, cy));
        dc.DrawLine(spoke, new Point(cx, cy), new Point(cx, cy + r * 0.84));
        dc.DrawEllipse(control, new Pen(edge, 2), new Point(cx, cy), r * 0.27, r * 0.27);
        dc.DrawLine(Line(accent, r * 0.13), new Point(cx, cy - r * 0.81), new Point(cx, cy - r * 0.95));
        dc.Pop();
        if (hats > 0) Hat(cx + r - 8, cy + r - 8, 18, 0);
    }

    private void Gate(double x, double y, double w, double h)
    {
        // 1 3 5 7 over 2 4 6 R, the lever where the gear is, the middle in neutral
        double[] columns = { x, x + w / 3, x + 2 * w / 3, x + w };
        double top = y + 14, bottom = y + h - 14, mid = (top + bottom) / 2;
        var pen = Line(edge, Math.Max(5, w * 0.035));
        dc.DrawLine(pen, new Point(columns[0], mid), new Point(columns[3], mid));
        foreach (double c in columns) dc.DrawLine(pen, new Point(c, top), new Point(c, bottom));
        (string Name, int Column, bool Top)[] gears = { ("1", 0, true), ("2", 0, false), ("3", 1, true), ("4", 1, false), ("5", 2, true), ("6", 2, false), ("R", 3, false), ("7", 3, true) };
        Point lever = new((columns[1] + columns[2]) / 2, mid);
        double size = Math.Max(13, w * 0.075);
        for (int i = 0; i < gears.Length; i++)
        {
            var g = gears[i];
            double gx = columns[g.Column], gy = g.Top ? top : bottom;
            bool on = Down(firstGear + i);
            if (on) lever = new Point(gx, gy);
            Label(g.Name, gx, g.Top ? gy - size * 1.5 : gy + size * 0.2, size, on ? success : muted, bold: on);
        }
        dc.DrawEllipse(accent, null, lever, size * 0.8, size * 0.8);
    }

    private void Sequential()
    {
        bool up = Down(firstGear), down = Down(firstGear + 1);
        double cx = 240, slot = 70;
        dc.DrawRoundedRectangle(control, new Pen(edge, 2), new Rect(cx - 30, 30, 60, 210), 20, 20);
        double y = 135 + (up ? -slot : down ? slot : 0);
        dc.DrawLine(Line(muted, 12), new Point(cx, 135), new Point(cx, y));
        dc.DrawEllipse(up || down ? success : accent, null, new Point(cx, y), 22, 22);
        Label(T("Up"), cx + 60, 52, 16, up ? success : muted, bold: up);
        Label(T("Down"), cx + 60, 200, 16, down ? success : muted, bold: down);
    }

    private void Handbrake()
    {
        double v = Role(K.Handbrake);
        if (double.IsNaN(v) && read) v = Enumerable.Range(0, buttons).Any(i => state.Buttons[i]) ? 1 : 0;
        // the lever lies nearly flat at rest, and comes up as it's pulled
        double p = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1), a = (8 + 52 * p) * Math.PI / 180;
        Point pivot = new(90, 215), tip = new(pivot.X + Math.Cos(a) * 215, pivot.Y - Math.Sin(a) * 215);
        dc.DrawRoundedRectangle(control, new Pen(edge, 2), new Rect(50, 205, 130, 26), 8, 8);
        dc.DrawLine(Line(muted, 14), pivot, tip);
        dc.DrawEllipse(accent, null, tip, 18, 18);
        dc.DrawEllipse(text, null, pivot, 8, 8);
        Label(Percent(v), 400, 110, 26, text, bold: true);
        Label(T("Handbrake"), 400, 146, 13, muted);
    }

    private void Yoke()
    {
        double v = Role(K.StickX), roll = double.IsNaN(v) ? 0 : (v - 0.5) * 90, pitch = (Centred(K.StickY) - 0.5) * 2;
        double cx = double.IsNaN(Role(K.Throttle)) ? 240 : 195, cy = 105 + pitch * 26, r = 1 + pitch * 0.12;
        dc.DrawLine(Line(edge, 16), new Point(cx, cy), new Point(cx, 240));
        dc.PushTransform(new RotateTransform(roll, cx, cy));
        dc.PushTransform(new ScaleTransform(r, r, cx, cy));
        var horns = Geometry.Parse($"M {cx - 110},{cy - 45} L {cx - 75},{cy - 45} L {cx - 60},{cy + 12} L {cx + 60},{cy + 12} L {cx + 75},{cy - 45} L {cx + 110},{cy - 45} L {cx + 100},{cy + 45} L {cx - 100},{cy + 45} Z");
        dc.DrawGeometry(control, new Pen(muted, 3), horns);
        dc.DrawEllipse(accent, null, new Point(cx, cy + 26), 14, 14);
        dc.Pop();
        dc.Pop();
        if (!double.IsNaN(Role(K.Throttle))) Lever(365, 25, 40, 205, Role(K.Throttle), T("Throttle"));
        if (hats > 0) Hat(330, 40, 18, 0);
    }

    private void Cap(double x, double y, double r, string name, bool on, Brush? color = null)
    {
        dc.DrawEllipse(on ? success : color ?? control, new Pen(edge, 1), new Point(x, y), r, r);
        Label(name, x, y - 9, 12, on ? Brushes.Black : text, bold: true);
    }

    private void ThumbStick(double x, double y, double h, double v, int button)
    {
        double px = x + (h - 0.5) * 2 * 14, py = y + (v - 0.5) * 2 * 14;
        dc.DrawEllipse(control, new Pen(edge, 2), new Point(x, y), 26, 26);
        dc.DrawEllipse(Down(button) ? success : accent, null, new Point(px, py), 15, 15);
    }

    /// <summary>A controller as DirectInput sees an Xbox pad: both triggers on one axis, 50% at rest.</summary>
    private void Pad(bool xbox)
    {
        var body = Geometry.Parse("M 150,62 C 105,62 85,92 68,150 C 48,226 56,262 92,262 C 128,262 136,214 170,202 L 310,202 C 344,214 352,262 388,262 C 424,262 432,226 412,150 C 395,92 375,62 330,62 Z");
        double trig = Role(K.Triggers), left = double.IsNaN(trig) ? 0 : Math.Max(0, (trig - 0.5) * 2), right = double.IsNaN(trig) ? 0 : Math.Max(0, (0.5 - trig) * 2);
        Trigger(100, 8, 90, left, xbox ? "LT" : "L2");
        Trigger(290, 8, 90, right, xbox ? "RT" : "R2");
        dc.DrawRoundedRectangle(Down(4) ? success : control, new Pen(edge, 1), new Rect(112, 42, 76, 16), 7, 7);
        dc.DrawRoundedRectangle(Down(5) ? success : control, new Pen(edge, 1), new Rect(292, 42, 76, 16), 7, 7);
        dc.DrawGeometry(null, new Pen(muted, 3), body);
        ThumbStick(132, 118, Centred(K.LeftX), Centred(K.LeftY), 8);
        ThumbStick(285, 172, Centred(K.RightX), Centred(K.RightY), 9);
        // the d-pad
        int p = read ? state.Povs[0] : -1;
        double dx = 190, dy = 172;
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(dx - 10, dy - 28, 20, 56), 4, 4);
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(dx - 28, dy - 10, 56, 20), 4, 4);
        if (p >= 0)
        {
            double a = p / 100.0 * Math.PI / 180 - Math.PI / 2;
            dc.DrawLine(Line(success, 9), new Point(dx, dy), new Point(dx + Math.Cos(a) * 22, dy + Math.Sin(a) * 22));
        }
        Cap(348, 150, 14, xbox ? "A" : "1", Down(0), xbox ? Ui.Brush("#2E7D32") : null);
        Cap(374, 124, 14, xbox ? "B" : "2", Down(1), xbox ? Ui.Brush("#C62828") : null);
        Cap(322, 124, 14, xbox ? "X" : "3", Down(2), xbox ? Ui.Brush("#1565C0") : null);
        Cap(348, 98, 14, xbox ? "Y" : "4", Down(3), xbox ? Ui.Brush("#F9A825") : null);
        dc.DrawEllipse(Down(6) ? success : control, new Pen(edge, 1), new Point(212, 112), 9, 9);
        dc.DrawEllipse(Down(7) ? success : control, new Pen(edge, 1), new Point(268, 112), 9, 9);
    }

    private void Trigger(double x, double y, double w, double v, string name)
    {
        dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(x, y, w, 26), 6, 6);
        if (v > 0) dc.DrawRoundedRectangle(accent, null, new Rect(x, y, w * v, 26), 6, 6);
        Label(name, x + w / 2, y + 4, 13, text, bold: true);
    }

    private void ButtonBox()
    {
        int rows = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(buttons, 1) / 2.0)));
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Max(buttons, 1) / (double)rows));
        double left = hats > 0 ? 90 : 20, step = Math.Min((W - left - 20) / columns, 240.0 / rows), r = Math.Min(step * 0.4, 26);
        for (int i = 0; i < hats; i++) Hat(44, 60 + i * 70, 28, i);
        for (int i = 0; i < buttons; i++)
        {
            double x = left + step * (i % columns) + step / 2, y = 20 + step * (i / columns) + step / 2;
            dc.DrawEllipse(Down(i) ? success : control, new Pen(edge, 1), new Point(x, y), r, r);
            Label((i + 1).ToString(), x, y - r * 0.5, r * 0.8, Down(i) ? Brushes.Black : muted);
        }
    }

    /// <summary>Anything else: a bar per axis, its hats.</summary>
    private void Generic()
    {
        var axes = Enumerable.Range(0, 8).Where(a => hasAxis[a]).ToList();
        if (axes.Count == 0 && hats == 0) { Label(T("No axes"), W / 2, 110, 16, muted); return; }
        double step = Math.Min(30, 220.0 / Math.Max(1, axes.Count)), right = hats > 0 ? 370 : 450;
        for (int i = 0; i < axes.Count; i++)
        {
            double y = 18 + i * step, v = Value(axes[i]);
            Label(K.AxisShort(axes[i]), 30, y - 2, 13, muted, TextAlignment.Left);
            dc.DrawRoundedRectangle(control, new Pen(edge, 1), new Rect(70, y, right - 70, 14), 5, 5);
            if (!double.IsNaN(v)) dc.DrawRoundedRectangle(accent, null, new Rect(70, y, (right - 70) * Math.Clamp(v, 0, 1), 14), 5, 5);
        }
        for (int i = 0; i < hats; i++) Hat(425, 50 + i * 62, 26, i);
    }
}
