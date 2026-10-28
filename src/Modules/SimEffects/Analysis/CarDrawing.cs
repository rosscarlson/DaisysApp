using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>The Tires block's car: a sports car from above, its wheels peeking out, drawn faintly in the theme's
/// colours (since 3.24.0 smaller, so the temperatures beside each wheel (<see cref="BlockView"/> puts them there) can be
/// larger), with no room left around it.</summary>
internal static class CarDrawing
{
    /// <summary>The whole: a temperature's room each side of the car.</summary>
    public const double TextWidth = 104, Width = 2 * (TextWidth + Gap) + (WheelRight - WheelLeft) * Scale, Height = (BodyBottom - BodyTop) * Scale + 8;
    /// <summary>The middle of the front and the rear wheels in the whole.</summary>
    public const double FrontY = Top + 72 * Scale, RearY = Top + 196 * Scale;
    /// <summary>The temperatures' left edges.</summary>
    public const double LeftText = 0, RightText = Left + WheelRight * Scale + Gap;

    private const double Scale = 0.62, Gap = 6;
    // (the drawing's own: the wheels' outer edges, the body's top and bottom)
    private const double WheelLeft = 61, WheelRight = 189, BodyTop = 16, BodyBottom = 246;
    private const double Left = TextWidth + Gap - WheelLeft * Scale, Top = 4 - BodyTop * Scale;

    // the body, nose at the top, wider over the rear wheels, narrower at the doors (centre x = 125)
    private const string Body =
        "M125,16 C156,16 167,23 171,40 L175,72 C177,96 171,112 169,130 C171,150 178,170 178,196 L176,230 C174,242 156,246 125,246 " +
        "C94,246 76,242 74,230 L72,196 C72,170 79,150 81,130 C79,112 73,96 75,72 L79,40 C83,23 94,16 125,16 Z";
    private const string Windscreen = "M98,100 C109,90 141,90 152,100 L146,120 C134,115 116,115 104,120 Z";
    private const string RearWindow = "M104,158 C116,162 134,162 146,158 L150,178 C138,184 112,184 100,178 Z";
    private const string Hood = "M106,40 C112,60 112,78 110,92 M144,40 C138,60 138,78 140,92";
    private const string Lights = "M84,30 C90,24 98,22 104,22 L102,28 C96,29 90,31 86,35 Z M166,30 C160,24 152,22 146,22 L148,28 C154,29 160,31 164,35 Z";
    private const string Wing = "M80,236 L170,236 L170,244 L80,244 Z";

    public static Canvas Make()
    {
        var outer = new Canvas { Width = Width, Height = Height, ClipToBounds = true };
        // washed out, so the temperatures stand out
        var c = new Canvas { Width = 250, Height = 262, Opacity = 0.32, RenderTransform = new ScaleTransform(Scale, Scale) };
        Canvas.SetLeft(c, Left);
        Canvas.SetTop(c, Top);
        outer.Children.Add(c);
        foreach (var (x, y) in new[] { (64.0, 72.0), (172.0, 72.0), (61.0, 196.0), (175.0, 196.0) })
        {
            var tyre = new Rectangle { Width = 14, Height = 38, RadiusX = 4, RadiusY = 4 };
            tyre.SetResourceReference(Shape.FillProperty, "TextBrush");
            Canvas.SetLeft(tyre, x);
            Canvas.SetTop(tyre, y - 19);
            c.Children.Add(tyre);
        }
        c.Children.Add(Draw(Body, "ControlBrush", "TextSecondaryBrush", 2));
        c.Children.Add(Draw(Hood, null, "TextSecondaryBrush", 1.5, 0.6));
        c.Children.Add(Draw(Windscreen, "TextSecondaryBrush", null, 0, 0.5));
        c.Children.Add(Draw(RearWindow, "TextSecondaryBrush", null, 0, 0.5));
        c.Children.Add(Draw(Lights, "TextSecondaryBrush", null, 0, 0.8));
        c.Children.Add(Draw(Wing, "TextSecondaryBrush", null, 0, 0.7));
        foreach (var x in new[] { 76.0, 166.0 })
        {
            var mirror = new Ellipse { Width = 8, Height = 5 };
            mirror.SetResourceReference(Shape.FillProperty, "TextSecondaryBrush");
            Canvas.SetLeft(mirror, x);
            Canvas.SetTop(mirror, 106);
            c.Children.Add(mirror);
        }
        return outer;
    }

    private static Path Draw(string data, string? fill, string? stroke, double thickness, double opacity = 1)
    {
        var p = new Path { Data = Geometry.Parse(data), StrokeThickness = thickness, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round };
        if (fill != null) p.SetResourceReference(Shape.FillProperty, fill);
        if (stroke != null) p.SetResourceReference(Shape.StrokeProperty, stroke);
        return p;
    }
}
