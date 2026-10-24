using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Lights;

/// <summary>LEDs on screen, as the lights show them: a row (shift lights) or an 8×8 grid (the gear matrix). Off LEDs
/// are dark grey dots.</summary>
internal sealed class LedView : FrameworkElement
{
    private (byte R, byte G, byte B)[] leds;
    private readonly int columns;
    private static readonly Brush offBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)));
    private readonly Dictionary<int, Brush> brushes = new();

    /// <param name="columns">LEDs a row (8 for a matrix of 64; the count for a strip).</param>
    public LedView(int count, int columns, double size = 14)
    {
        leds = new (byte, byte, byte)[count];
        this.columns = Math.Max(1, columns);
        int rows = (count + this.columns - 1) / this.columns;
        Width = this.columns * size;
        Height = rows * size;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>New colours (drawn only if they changed).</summary>
    public void Show((byte R, byte G, byte B)[] colours)
    {
        if (colours.Length == leds.Length && colours.AsSpan().SequenceEqual(leds)) return;
        leds = colours.Length == leds.Length ? (((byte, byte, byte)[])colours.Clone()) : colours.Take(leds.Length).Concat(new (byte, byte, byte)[Math.Max(0, leds.Length - colours.Length)]).ToArray();
        InvalidateVisual();
    }

    /// <summary>An 8×8 picture (rows, bit 7 left) in one colour.</summary>
    public void ShowPicture(byte[] rows, (byte R, byte G, byte B) on)
    {
        var c = new (byte, byte, byte)[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if ((rows[y] & (0x80 >> x)) != 0) c[y * 8 + x] = on;
        Show(c);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Width / columns;
        double r = size * 0.38;
        for (int i = 0; i < leds.Length; i++)
        {
            var (R, G, B) = leds[i];
            var brush = R == 0 && G == 0 && B == 0 ? offBrush : BrushOf(R, G, B);
            dc.DrawEllipse(brush, null, new Point((i % columns + 0.5) * size, (i / columns + 0.5) * size), r, r);
        }
    }

    private Brush BrushOf(byte r, byte g, byte b)
    {
        int key = r << 16 | g << 8 | b;
        if (!brushes.TryGetValue(key, out var br)) brushes[key] = br = Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
        return br;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
