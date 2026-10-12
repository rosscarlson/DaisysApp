namespace DaisysApp.Applets.Video.Gaming;

/// <summary>The shape of the recorded (and virtual webcam's) picture: as captured, or a centred part of it in a set shape.</summary>
public enum OutputAspect { Captured, Wide32x9, Wide21x9, Wide16x9, Standard4x3, Portrait9x16 }

internal static class OutputAspects
{
    public static readonly OutputAspect[] All = Enum.GetValues<OutputAspect>();

    public static (int W, int H)? Ratio(OutputAspect a) => a switch
    {
        OutputAspect.Wide32x9 => (32, 9),
        OutputAspect.Wide21x9 => (21, 9),
        OutputAspect.Wide16x9 => (16, 9),
        OutputAspect.Standard4x3 => (4, 3),
        OutputAspect.Portrait9x16 => (9, 16),
        _ => null,
    };

    public static string Label(OutputAspect a) => a switch
    {
        OutputAspect.Wide32x9 => T("32:9 (super ultrawide)"),
        OutputAspect.Wide21x9 => T("21:9 (ultrawide)"),
        OutputAspect.Wide16x9 => T("16:9 (widescreen)"),
        OutputAspect.Standard4x3 => T("4:3"),
        OutputAspect.Portrait9x16 => T("9:16 (phone, portrait)"),
        _ => T("As captured"),
    };

    /// <summary>
    /// The largest part of <paramref name="area"/> in that shape, in the middle of it, in even sizes (what the encoder
    /// takes): 5120×1440 at 16:9 is 2560×1440 from x = 1280.
    /// </summary>
    public static PixelRect Fit(PixelRect area, OutputAspect a)
    {
        if (Ratio(a) is not var (rw, rh) || area.Width <= 0 || area.Height <= 0) return area;
        double target = (double)rw / rh;
        int w = area.Width, h = area.Height;
        if ((double)w / h > target) w = (int)Math.Round(h * target);
        else h = (int)Math.Round(w / target);
        w = Math.Max(2, w & ~1);
        h = Math.Max(2, h & ~1);
        return new PixelRect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }
}
