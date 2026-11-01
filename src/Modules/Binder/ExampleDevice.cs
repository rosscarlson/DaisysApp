using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Binder;

/// <summary>What one part of the example device shows: the action it's for, its binding, and how far it's moved or held.</summary>
internal sealed record Live(string Label, string Bind, double Value, bool Held, bool Bound);

/// <summary>
/// A pretend device for the title's kind, drawn from the roles its actions have, showing what's bound and moving with it
/// (so bindings can be tried without the game): a wheel, pedals, handbrake, shifter and buttons for a racing game; a
/// stick, yaw, throttle, fire buttons and buttons for flight and space.
/// </summary>
internal sealed class ExampleDevice : FrameworkElement
{
    private TitleKind kind;
    private Func<string, Live?> read = _ => null;
    private List<Live> caps = new();

    public void Show(TitleKind kind, Func<string, Live?> read, List<Live> caps)
    {
        this.kind = kind;
        this.read = read;
        this.caps = caps;
        InvalidateVisual();
    }

    /// <summary>The roles it draws itself (the rest of the bound buttons are caps).</summary>
    public static readonly HashSet<string> Drawn = new(StringComparer.OrdinalIgnoreCase)
    {
        "steer", "steer.left", "steer.right", "gas", "brake", "clutch", "handbrake", "gear.up", "gear.down", "gear.1", "gear.2", "gear.3", "gear.4", "gear.5",
        "gear.6", "gear.7", "gear.r", "pitch", "pitch.up", "pitch.down", "roll", "roll.left", "roll.right", "yaw", "yaw.left", "yaw.right", "throttle",
        "throttle.up", "throttle.down", "fire.primary", "fire.secondary", "strafe.x", "strafe.y",
    };

    /// <summary>Buttons and keys that stand in for an axis that isn't bound (pitch from Pitch Up and Pitch Down).</summary>
    private static readonly Dictionary<string, (string Neg, string Pos)> Pairs = new()
    {
        ["steer"] = ("steer.left", "steer.right"), ["pitch"] = ("pitch.down", "pitch.up"), ["roll"] = ("roll.left", "roll.right"),
        ["yaw"] = ("yaw.left", "yaw.right"), ["throttle"] = ("throttle.down", "throttle.up"),
    };

    protected override Size MeasureOverride(Size available) => new(0, 250);

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private Brush track = Brushes.Gray, fill = Brushes.DodgerBlue, text = Brushes.White, dim = Brushes.Gray, edge = Brushes.Gray, lit = Brushes.LimeGreen;

    protected override void OnRender(DrawingContext dc)
    {
        track = Res("ControlBrush", Brushes.DimGray);
        fill = Res("AccentBrush", Brushes.DodgerBlue);
        text = Res("TextBrush", Brushes.White);
        dim = Res("TextSecondaryBrush", Brushes.Gray);
        edge = Res("ControlBorderStrongBrush", Brushes.Gray);
        lit = Res("SuccessBrush", Brushes.LimeGreen);
        double w = ActualWidth;
        if (w <= 0) return;
        double x = kind == TitleKind.Racing ? Racing(dc) : Flight(dc);
        Caps(dc, x + 24, w);
    }

    /// <summary>An axis' value, from its own binding or the buttons that stand in for it (-1 to 1).</summary>
    private Live Axis(string role, string name)
    {
        var a = read(role);
        if (a is { Bound: true }) return a;
        if (Pairs.TryGetValue(role, out var p) && (read(p.Neg) is { Bound: true } || read(p.Pos) is { Bound: true }))
        {
            var neg = read(p.Neg);
            var pos = read(p.Pos);
            double v = (pos?.Held == true ? 1 : 0) - (neg?.Held == true ? 1 : 0);
            return new Live(a?.Label ?? name, string.Join(" / ", new[] { neg?.Bind, pos?.Bind }.Where(s => !string.IsNullOrEmpty(s))), v, v != 0, true);
        }
        return a ?? new Live(name, "", 0, false, false);
    }

    // ---------------------------------------------------------------- racing

    private double Racing(DrawingContext dc)
    {
        var steer = Axis("steer", T("Steering"));
        var c = new Point(95, 100);
        dc.DrawEllipse(null, new Pen(steer.Bound ? edge : track, 10), c, 72, 72);
        dc.PushTransform(new RotateTransform(steer.Value * 135, c.X, c.Y));
        dc.DrawRectangle(fill, null, new Rect(c.X - 4, c.Y - 82, 8, 20));
        dc.DrawLine(new Pen(edge, 6), new Point(c.X - 66, c.Y), new Point(c.X + 66, c.Y));
        dc.DrawLine(new Pen(edge, 6), c, new Point(c.X, c.Y + 66));
        dc.Pop();
        Label(dc, steer, new Rect(10, 184, 170, 50));

        double x = 200;
        foreach (var (role, name) in new[] { ("clutch", T("Clutch")), ("brake", T("Brake")), ("gas", T("Gas")), ("handbrake", T("Handbrake")) })
        {
            var p = Axis(role, name);
            double v = p.Held && p.Value == 0 ? 1 : (p.Value + 1) / 2;
            Bar(dc, new Rect(x + 12, 20, 28, 150), p.Bound ? v : 0, p.Bound);
            Label(dc, p, new Rect(x - 4, 184, 64, 50), centre: true);
            x += 68;
        }

        // the H-shifter's gears, and the paddles
        x += 10;
        string[] gears = ["1", "2", "3", "4", "5", "6", "7", "R"];
        for (int i = 0; i < gears.Length; i++)
        {
            var g = read("gear." + gears[i].ToLowerInvariant());
            var r = new Rect(x + i / 2 * 34, i % 2 == 0 ? 30 : 120, 28, 28);
            Cap(dc, r, gears[i], g);
        }
        dc.DrawLine(new Pen(track, 3), new Point(x + 14, 72), new Point(x + 14 + 3 * 34, 72));
        for (int i = 0; i < 4; i++) dc.DrawLine(new Pen(track, 3), new Point(x + 14 + i * 34, 58), new Point(x + 14 + i * 34, 120));
        var down = read("gear.down");
        var up = read("gear.up");
        Cap(dc, new Rect(x, 168, 62, 26), "−", down);
        Cap(dc, new Rect(x + 74, 168, 62, 26), "+", up);
        Small(dc, T("Shift down / up"), new Rect(x, 200, 140, 16), dim);
        return x + 140;
    }

    // ---------------------------------------------------------------- flight and space

    private double Flight(DrawingContext dc)
    {
        var pitch = Axis("pitch", T("Pitch"));
        var roll = Axis("roll", T("Roll"));
        var box = new Rect(10, 10, 160, 160);
        dc.DrawRoundedRectangle(null, new Pen(pitch.Bound || roll.Bound ? edge : track, 2), box, 8, 8);
        dc.DrawLine(new Pen(track, 1), new Point(box.Left, box.Top + 80), new Point(box.Right, box.Top + 80));
        dc.DrawLine(new Pen(track, 1), new Point(box.Left + 80, box.Top), new Point(box.Left + 80, box.Bottom));
        var dot = new Point(box.Left + 80 + roll.Value * 70, box.Top + 80 + pitch.Value * 70);
        dc.DrawEllipse(fill, null, dot, 9, 9);
        Label(dc, new Live(T("Pitch / Roll"), string.Join(" · ", new[] { pitch.Bind, roll.Bind }.Where(s => s.Length > 0)), 0, false, pitch.Bound || roll.Bound), new Rect(10, 176, 160, 30));

        var yaw = Axis("yaw", T("Yaw"));
        var yawBar = new Rect(10, 214, 160, 10);
        dc.DrawRoundedRectangle(track, null, yawBar, 5, 5);
        double mid = yawBar.Left + 80, len = Math.Abs(yaw.Value) * 80;
        if (len > 0.5) dc.DrawRoundedRectangle(fill, null, yaw.Value >= 0 ? new Rect(mid, yawBar.Top, len, 10) : new Rect(mid - len, yawBar.Top, len, 10), 5, 5);
        Small(dc, yaw.Label + (yaw.Bind.Length > 0 ? " · " + yaw.Bind : ""), new Rect(10, 228, 200, 16), yaw.Bound ? text : dim);

        var throttle = Axis("throttle", T("Throttle"));
        Bar(dc, new Rect(200, 20, 34, 150), throttle.Bound ? (throttle.Value + 1) / 2 : 0, throttle.Bound);
        Label(dc, throttle, new Rect(180, 184, 74, 50), centre: true);

        var sx = Axis("strafe.x", T("Strafe"));
        var sy = Axis("strafe.y", T("Strafe"));
        var sbox = new Rect(276, 20, 70, 70);
        dc.DrawRoundedRectangle(null, new Pen(sx.Bound || sy.Bound ? edge : track, 2), sbox, 6, 6);
        dc.DrawEllipse(fill, null, new Point(sbox.Left + 35 + sx.Value * 28, sbox.Top + 35 + sy.Value * 28), 6, 6);
        Small(dc, T("Strafe"), new Rect(276, 94, 70, 16), sx.Bound || sy.Bound ? text : dim, centre: true);

        Cap(dc, new Rect(276, 124, 70, 40), T("Fire"), read("fire.primary"));
        Cap(dc, new Rect(276, 172, 70, 40), T("Fire 2"), read("fire.secondary"));
        return 346;
    }

    // ---------------------------------------------------------------- the rest of the buttons

    private void Caps(DrawingContext dc, double x, double right)
    {
        if (caps.Count == 0 || right - x < 120) return;
        const double cw = 118, ch = 36, gap = 6;
        int cols = Math.Max(1, (int)((right - x + gap) / (cw + gap)));
        int rows = 6;
        for (int i = 0; i < Math.Min(caps.Count, cols * rows); i++)
        {
            var r = new Rect(x + i % cols * (cw + gap), 4 + i / cols * (ch + gap), cw, ch);
            Cap(dc, r, caps[i].Label, caps[i], small: true);
        }
    }

    // ---------------------------------------------------------------- drawing

    private void Bar(DrawingContext dc, Rect r, double v, bool bound)
    {
        dc.DrawRoundedRectangle(track, bound ? new Pen(edge, 1) : null, r, 5, 5);
        v = Math.Clamp(v, 0, 1);
        if (v * r.Height > 1) dc.DrawRoundedRectangle(fill, null, new Rect(r.Left, r.Bottom - v * r.Height, r.Width, v * r.Height), 5, 5);
    }

    private void Cap(DrawingContext dc, Rect r, string name, Live? l, bool small = false)
    {
        bool bound = l?.Bound == true;
        dc.DrawRoundedRectangle(l?.Held == true ? lit : track, new Pen(bound ? edge : track, 1), r, 5, 5);
        if (small)
        {
            Small(dc, name, new Rect(r.Left + 6, r.Top + 2, r.Width - 12, 16), l?.Held == true ? Brushes.Black : text);
            Small(dc, l?.Bind ?? "", new Rect(r.Left + 6, r.Top + 18, r.Width - 12, 16), l?.Held == true ? Brushes.Black : dim);
        }
        else Small(dc, name, new Rect(r.Left, r.Top + (r.Height - 16) / 2, r.Width, 16), bound ? (l?.Held == true ? Brushes.Black : text) : dim, centre: true);
    }

    private void Label(DrawingContext dc, Live l, Rect r, bool centre = false)
    {
        Small(dc, l.Label, new Rect(r.Left, r.Top, r.Width, 16), l.Bound ? text : dim, centre, bold: true);
        Small(dc, l.Bound ? l.Bind : T("Not bound"), new Rect(r.Left, r.Top + 16, r.Width, Math.Max(16, r.Height - 16)), dim, centre, wrap: r.Height > 34);
    }

    private void Small(DrawingContext dc, string s, Rect r, Brush brush, bool centre = false, bool bold = false, bool wrap = false)
    {
        if (string.IsNullOrEmpty(s) || r.Width <= 4) return;
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            11.5, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = r.Width, MaxTextHeight = r.Height, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left };
        if (!wrap) ft.MaxLineCount = 1;
        dc.DrawText(ft, r.TopLeft);
    }
}
