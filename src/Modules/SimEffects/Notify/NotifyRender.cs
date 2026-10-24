using DaisysApp.Applets.SimEffects.Arduino;
using DaisysApp.Applets.SimEffects.Lights;

namespace DaisysApp.Applets.SimEffects.Notify;

/// <summary>What Notify's lights show for a moment: the gear matrix's picture and colour, the shift lights' colours.
/// Used for the LEDs and for the tab's previews.</summary>
internal static class NotifyRender
{
    public static readonly (byte, byte, byte) Black = (0, 0, 0);

    /// <summary>The gear's picture and colour: reverse, neutral and gears each in theirs; the shift colour at the shift
    /// point; flashing in the flash colour over-rev. Nothing while not driving.</summary>
    public static (byte[] Rows, (byte R, byte G, byte B) On) Matrix(RevState s, NotifySettings c)
    {
        if (!s.Live) return (new byte[8], Black);
        string colour = s.Gear < 0 ? c.Reverse : s.Gear == 0 ? c.Neutral : c.Gear;
        if (s.OverRev)
        {
            if (!s.BlinkOn) return (new byte[8], Black);
            colour = c.GearFlash;
        }
        else if (s.AtShift) colour = c.GearShift;
        return (Glyphs.Of(s.GearText), LightColours.Parse(colour));
    }

    /// <summary>The <see cref="Arduino.ArduinoProtocol.Bitmap"/> data for it.</summary>
    public static byte[] MatrixData(RevState s, NotifySettings c)
    {
        var (rows, on) = Matrix(s, c);
        var d = new byte[14];
        rows.CopyTo(d, 0);
        d[8] = on.R; d[9] = on.G; d[10] = on.B;
        return d;
    }

    /// <summary>
    /// Each LED's colour, first to last as wired: lit from <see cref="RevState.Start"/> to the shift point, the first
    /// half <see cref="NotifySettings.Low"/>, the next 30% <see cref="NotifySettings.Mid"/>, the rest
    /// <see cref="NotifySettings.High"/>; all <see cref="NotifySettings.Shift"/> at the shift point (flashing over-rev).
    /// <paramref name="bothEnds"/> fills from both ends to the middle.
    /// </summary>
    public static (byte R, byte G, byte B)[] Strip(RevState s, NotifySettings c, int count, bool bothEnds, bool reverse)
    {
        var leds = new (byte, byte, byte)[count];
        if (!s.Live) return leds;
        if (s.AtShift)
        {
            var all = s.OverRev && c.StripFlashes && !s.BlinkOn ? Black : LightColours.Parse(c.Shift);
            Array.Fill(leds, all);
            return leds;
        }
        var low = LightColours.Parse(c.Low);
        var mid = LightColours.Parse(c.Mid);
        var high = LightColours.Parse(c.High);
        (byte, byte, byte) Zone(double at) => at < 0.5 ? low : at < 0.8 ? mid : high;
        if (bothEnds)
        {
            int side = (count + 1) / 2;
            int lit = Lit(s.Fraction, side);
            for (int i = 0; i < lit; i++)
            {
                var col = Zone((i + 0.5) / side);
                leds[i] = col;
                leds[count - 1 - i] = col;
            }
        }
        else
        {
            int lit = Lit(s.Fraction, count);
            for (int i = 0; i < lit; i++) leds[i] = Zone((i + 0.5) / count);
        }
        if (reverse) Array.Reverse(leds);
        return leds;
    }

    // (the first one comes on as soon as the RPM passes the start)
    private static int Lit(float fraction, int count) => fraction <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(fraction * count), 1, count);

    /// <summary>The <see cref="Arduino.ArduinoProtocol.Pixels"/> data for it: from the first LED.</summary>
    public static byte[] StripData((byte R, byte G, byte B)[] leds)
    {
        var d = new byte[1 + leds.Length * 3];
        for (int i = 0; i < leds.Length; i++)
        {
            d[1 + i * 3] = leds[i].R; d[2 + i * 3] = leds[i].G; d[3 + i * 3] = leds[i].B;
        }
        return d;
    }
}
