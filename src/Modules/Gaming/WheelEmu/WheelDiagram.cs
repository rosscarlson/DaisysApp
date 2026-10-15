using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// A picture of the game's controller, for testing the bindings: the wheel turns with the steering (over Forza's
/// 450°), its paddles, buttons and d-pad light up, the pedals and handbrake fill, the H-shifter shows the gear, and a
/// button box holds the other wheel buttons. What isn't bound is faint. Fed each role's value by the emulator.
/// </summary>
internal sealed class WheelDiagram : Viewbox
{
    private const double W = 780, H = 250, WheelX = 150, WheelY = 125;

    private readonly Canvas canvas = new() { Width = W, Height = H };
    private readonly Canvas wheel = new() { Width = W, Height = H };
    private readonly RotateTransform turn = new(0, WheelX, WheelY);
    private readonly TextBlock steerText = Label("", 0, 0);
    /// <summary>Lights (lit when the role is pressed), and bars (filled as far as the axis goes).</summary>
    private readonly List<(string Role, Shape Shape, FrameworkElement Fade)> lights = new();
    private readonly List<(string Role, Rectangle Fill, double Top, double Height, FrameworkElement Fade)> bars = new();

    public WheelDiagram()
    {
        Stretch = Stretch.Uniform;
        MaxHeight = 270;
        Child = canvas;

        // the paddles, behind the wheel
        Paddle("ShiftDown", "−", WheelX - 142);
        Paddle("ShiftUp", "+", WheelX + 120);

        // the wheel, which turns
        wheel.RenderTransform = turn;
        canvas.Children.Add(wheel);
        var rim = new Ellipse { Width = 220, Height = 220, StrokeThickness = 18 };
        rim.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        Place(wheel, rim, WheelX - 110, WheelY - 110);
        foreach (var (x, y, w, h) in new[] { (WheelX - 100, WheelY - 8, 70.0, 16.0), (WheelX + 30, WheelY - 8, 70.0, 16.0), (WheelX - 8, WheelY + 30, 16.0, 70.0) })
        {
            var spoke = new Rectangle { Width = w, Height = h, RadiusX = 4, RadiusY = 4 };
            spoke.SetResourceReference(Shape.FillProperty, "ControlBorderBrush");
            Place(wheel, spoke, x, y);
        }
        var hub = new Ellipse { Width = 84, Height = 84, StrokeThickness = 1 };
        hub.SetResourceReference(Shape.FillProperty, "ControlBrush");
        hub.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        Place(wheel, hub, WheelX - 42, WheelY - 42);
        var top = new Rectangle { Width = 8, Height = 18, Fill = Brushes.OrangeRed };
        Place(wheel, top, WheelX - 4, WheelY - 119);

        // on the wheel: the d-pad left, A B X Y right, View and Menu on the hub, bumpers above, stick clicks below
        Light(wheel, "DpadUp", Square(14), WheelX - 64, WheelY - 24, "");
        Light(wheel, "DpadDown", Square(14), WheelX - 64, WheelY + 10, "");
        Light(wheel, "DpadLeft", Square(14), WheelX - 81, WheelY - 7, "");
        Light(wheel, "DpadRight", Square(14), WheelX - 47, WheelY - 7, "");
        Light(wheel, "Y", Dot(16), WheelX + 49, WheelY - 27, "Y");
        Light(wheel, "X", Dot(16), WheelX + 31, WheelY - 8, "X");
        Light(wheel, "B", Dot(16), WheelX + 67, WheelY - 8, "B");
        Light(wheel, "A", Dot(16), WheelX + 49, WheelY + 11, "A");
        Light(wheel, "Back", Pill(20, 10), WheelX - 26, WheelY - 22, "");
        Light(wheel, "Start", Pill(20, 10), WheelX + 6, WheelY - 22, "");
        Light(wheel, "LB", Pill(28, 12), WheelX - 58, WheelY - 62, "LB");
        Light(wheel, "RB", Pill(28, 12), WheelX + 30, WheelY - 62, "RB");
        Light(wheel, "LS", Dot(14), WheelX - 30, WheelY + 4, "");
        Light(wheel, "RS", Dot(14), WheelX + 16, WheelY + 4, "");
        Place(canvas, steerText, WheelX - 60, H - 16);
        steerText.Width = 120;
        steerText.TextAlignment = TextAlignment.Center;

        // the button box: the other wheel buttons
        var box = new Rectangle { Width = 170, Height = 118, RadiusX = 8, RadiusY = 8, StrokeThickness = 1 };
        box.SetResourceReference(Shape.StrokeProperty, "ControlBorderBrush");
        Place(canvas, box, 296, 50);
        Place(canvas, Label(T("Wheel buttons"), 0, 0), 300, 30);
        var boxRoles = new[] { ("PageUp", T("Page up")), ("PageDown", T("Page down")), ("Horn", T("Horn")), ("LookBack", T("Look back")), ("Camera", T("Camera")), ("Rewind", T("Rewind")) };
        for (int i = 0; i < boxRoles.Length; i++)
        {
            double x = 302 + i % 3 * 54, y = 60 + i / 3 * 54;
            Light(canvas, boxRoles[i].Item1, Square(30), x + 12, y, "");
            var name = Label(boxRoles[i].Item2, 0, 0, 9);
            name.Width = 54;
            name.TextAlignment = TextAlignment.Center;
            Place(canvas, name, x, y + 32);
        }

        // the pedals and the handbrake
        Bar("Clutch", T("Clutch"), 490);
        Bar("Brake", T("Brake"), 530);
        Bar("Throttle", T("Throttle"), 570);
        Bar("Handbrake", T("Handbrake"), 616);

        // the H-shifter: 1 3 5 7 across the top, 2 4 6 R across the bottom
        const double sx = 672, sTop = 52, sBottom = 168, step = 28;
        for (int c = 0; c < 4; c++)
        {
            var gate = new Line { X1 = sx + c * step, X2 = sx + c * step, Y1 = sTop, Y2 = sBottom, StrokeThickness = 3 };
            gate.SetResourceReference(Shape.StrokeProperty, "ControlBorderBrush");
            canvas.Children.Add(gate);
        }
        var across = new Line { X1 = sx, X2 = sx + 3 * step, Y1 = (sTop + sBottom) / 2, Y2 = (sTop + sBottom) / 2, StrokeThickness = 3 };
        across.SetResourceReference(Shape.StrokeProperty, "ControlBorderBrush");
        canvas.Children.Add(across);
        string[] topRow = { "Gear1", "Gear3", "Gear5", "Gear7" }, bottomRow = { "Gear2", "Gear4", "Gear6", "GearR" };
        for (int c = 0; c < 4; c++)
        {
            Light(canvas, topRow[c], Dot(20), sx + c * step - 10, sTop - 10, topRow[c][^1].ToString());
            Light(canvas, bottomRow[c], Dot(20), sx + c * step - 10, sBottom - 10, bottomRow[c][^1].ToString());
        }
        Place(canvas, Label(T("Shifter"), 0, 0), sx - 6, 16);
    }

    /// <summary>Shows the values (axes 0–1, buttons 0 or 1); what isn't bound is faint.</summary>
    public void Show(IReadOnlyDictionary<string, double> values, IEnumerable<string> bound)
    {
        var isBound = new HashSet<string>(bound, StringComparer.OrdinalIgnoreCase);
        bool combined = isBound.Contains("Combined");
        foreach (var (role, shape, fade) in lights)
        {
            bool on = values.TryGetValue(role, out var v) && v > 0.5;
            if (on) shape.SetResourceReference(Shape.FillProperty, "AccentBrush");
            else shape.SetResourceReference(Shape.FillProperty, "ControlBrush");
            fade.Opacity = isBound.Contains(role) ? 1 : 0.35;
        }
        foreach (var (role, fill, top, height, fade) in bars)
        {
            double v = values.TryGetValue(role, out var x) ? Math.Clamp(x, 0, 1) : 0;
            fill.Height = v * height;
            Canvas.SetTop(fill, top + height - fill.Height);
            fade.Opacity = isBound.Contains(role) || (combined && role is "Throttle" or "Brake") ? 1 : 0.35;
        }
        double steer = values.TryGetValue("Steering", out var s) ? s : 0.5;
        double angle = (steer - 0.5) * 450;
        turn.Angle = angle;
        wheel.Opacity = isBound.Contains("Steering") ? 1 : 0.5;
        steerText.Text = isBound.Contains("Steering") ? F("Steering {0:+0;−0;0}°", angle) : T("Steering not bound");
    }

    // ---------------------------------------------------------------- drawing

    private void Paddle(string role, string sign, double x)
    {
        var paddle = new Rectangle { Width = 22, Height = 60, RadiusX = 6, RadiusY = 6, StrokeThickness = 1 };
        Light(canvas, role, paddle, x, WheelY - 50, sign);
    }

    private void Bar(string role, string name, double x)
    {
        const double top = 52, height = 116;
        var frame = new Canvas();
        var outline = new Rectangle { Width = 30, Height = height, RadiusX = 4, RadiusY = 4, StrokeThickness = 1 };
        outline.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        outline.SetResourceReference(Shape.FillProperty, "ControlBrush");
        Place(frame, outline, x, top);
        var fill = new Rectangle { Width = 30, Height = 0, RadiusX = 4, RadiusY = 4 };
        fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
        Place(frame, fill, x, top + height);
        var label = Label(name, 0, 0, 10);
        label.Width = 60;
        label.TextAlignment = TextAlignment.Center;
        Place(frame, label, x - 15, top + height + 6);
        canvas.Children.Add(frame);
        bars.Add((role, fill, top, height, frame));
    }

    private void Light(Canvas on, string role, Shape shape, double x, double y, string text)
    {
        shape.StrokeThickness = 1;
        shape.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        shape.SetResourceReference(Shape.FillProperty, "ControlBrush");
        var holder = new Canvas();
        Place(holder, shape, x, y);
        if (text.Length > 0)
        {
            var t = Label(text, 0, 0, 10);
            t.Width = shape.Width;
            t.TextAlignment = TextAlignment.Center;
            t.FontWeight = FontWeights.SemiBold;
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Place(holder, t, x, y + (shape.Height - 13) / 2);
        }
        on.Children.Add(holder);
        lights.Add((role, shape, holder));
    }

    private static Shape Dot(double size) => new Ellipse { Width = size, Height = size };
    private static Shape Square(double size) => new Rectangle { Width = size, Height = size, RadiusX = 3, RadiusY = 3 };
    private static Shape Pill(double w, double h) => new Rectangle { Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2 };

    private static TextBlock Label(string text, double x, double y, double size = 11)
    {
        var t = new TextBlock { Text = text, FontSize = size };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        return t;
    }

    private static void Place(Panel on, UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        on.Children.Add(e);
    }
}
