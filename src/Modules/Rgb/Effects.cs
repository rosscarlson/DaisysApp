using System.Globalization;
using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Rgb;

/// <summary>The lighting effects every device is offered (each device says which it can do, <see cref="IRgbDevice.Effects"/>).</summary>
public enum LightEffect { Off, Static, Breathing, Flashing, ColourCycle, Rainbow }

public enum Speed { Slow, Normal, Fast }

/// <summary>A colour as the lights take it: red, green, blue, 0–255 each.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb? Parse(string? hex)
    {
        string h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return null;
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public Rgb Times(double k) => new((byte)Math.Round(R * k), (byte)Math.Round(G * k), (byte)Math.Round(B * k));

    /// <summary>Full-brightness colour at <paramref name="hue"/> (0–1 round the wheel).</summary>
    public static Rgb FromHue(double hue)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        int i = (int)hue;
        byte f = (byte)Math.Round((hue - i) * 255), q = (byte)(255 - f);
        return i switch
        {
            0 => new(255, f, 0),
            1 => new(q, 255, 0),
            2 => new(0, 255, f),
            3 => new(0, q, 255),
            4 => new(f, 0, 255),
            _ => new(255, 0, q),
        };
    }
}

/// <summary>What one device is set to (saved per device in <see cref="RgbSettings"/>).</summary>
public sealed class LightSetting
{
    public LightEffect Effect { get; set; } = LightEffect.Static;
    public string Colour { get; set; } = "#FF0000";
    public Speed Speed { get; set; } = Speed.Normal;

    [JsonIgnore] public Rgb Rgb => Rgb.Parse(Colour) ?? new Rgb(255, 0, 0);

    public LightSetting Copy() => new() { Effect = Effect, Colour = Colour, Speed = Speed };

    public bool Same(LightSetting o) => Effect == o.Effect && Speed == o.Speed && string.Equals(Colour, o.Colour, StringComparison.OrdinalIgnoreCase);

    public static readonly LightSetting AllOff = new() { Effect = LightEffect.Off };

    /// <summary>Effects with a colour of their own (the others make their own colours).</summary>
    public static bool HasColour(LightEffect e) => e is LightEffect.Static or LightEffect.Breathing or LightEffect.Flashing;

    public static bool HasSpeed(LightEffect e) => e is LightEffect.Breathing or LightEffect.Flashing or LightEffect.ColourCycle or LightEffect.Rainbow;
}

/// <summary>
/// Effects drawn by Daisy's App, for devices that can only be given a colour (<see cref="IRgbDevice.Leds"/>): each
/// LED's colour at a moment. Devices that run an effect by themselves (<see cref="IRgbDevice.Effects"/>) don't use it.
/// </summary>
public static class Animation
{
    /// <summary>Seconds one round of the effect takes.</summary>
    public static double Period(LightEffect e, Speed s)
    {
        double k = s switch { Speed.Slow => 1.6, Speed.Fast => 0.5, _ => 1 };
        return k * e switch
        {
            LightEffect.Breathing => 4,
            LightEffect.Flashing => 1,
            LightEffect.ColourCycle => 8,
            LightEffect.Rainbow => 5,
            _ => 1,
        };
    }

    public static Rgb At(LightSetting s, double seconds, int led, int leds)
    {
        double phase = seconds / Period(s.Effect, s.Speed) % 1;
        return s.Effect switch
        {
            LightEffect.Off => Rgb.Black,
            LightEffect.Static => s.Rgb,
            // a smooth rise and fall (squared, so it lingers near dark as a breath does)
            LightEffect.Breathing => s.Rgb.Times(Math.Pow((1 - Math.Cos(phase * 2 * Math.PI)) / 2, 2)),
            LightEffect.Flashing => phase < 0.5 ? s.Rgb : Rgb.Black,
            LightEffect.ColourCycle => Rgb.FromHue(phase),
            LightEffect.Rainbow => Rgb.FromHue(phase + (leds > 1 ? (double)led / leds : 0)),
            _ => Rgb.Black,
        };
    }
}
