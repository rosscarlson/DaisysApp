namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// A kind of thing an Arduino can power (a plugin): LED lights now; motor controllers, fans and others later. It says
/// what the board needs to know (<see cref="Fields"/>, sent by <see cref="Config"/> when the board connects), and which
/// sub-tab drives it (<see cref="Tab"/>: that sub-tab shows while some board has one). The sub-tab sends its data with
/// <see cref="ArduinoHub.Send"/>. To add one: a class here, a line in <see cref="ArduinoFeatures.All"/>, a type in the
/// sketch (its README, "Adding a feature"), and the sub-tab that drives it.
/// </summary>
public interface IArduinoFeature
{
    /// <summary>Kept in the settings ("matrix"); never changed once released.</summary>
    string Id { get; }
    string Title { get; }
    /// <summary>The sketch's number for it (the first byte of its <see cref="Config"/>).</summary>
    byte Type { get; }
    /// <summary>The sub-tab that drives it (<see cref="ArduinoFeatures.NotifyTab"/>…).</summary>
    string Tab { get; }
    /// <summary>Its settings, shown in the Arduinos window.</summary>
    IReadOnlyList<FeatureField> Fields { get; }
    /// <summary>The <see cref="ArduinoProtocol.Config"/> data: <see cref="Type"/>, then its own bytes.</summary>
    byte[] Config(ArduinoFeature f);
    /// <summary>A test pattern <paramref name="seconds"/> into a test (for its Test button): the frames to send.</summary>
    IEnumerable<(byte Command, byte[] Data)> Test(ArduinoFeature f, double seconds);
}

/// <summary>
/// One setting of a feature: a number (<see cref="Min"/>..<see cref="Max"/>; a slider when it has a <see cref="Unit"/>,
/// a drop-down of the numbers when <see cref="Menu"/>), a choice (its index), or on/off. <see cref="Only"/>: shown
/// only for boards with that sketch (<see cref="ArduinoSketch"/>; SimHub's sets pins and wiring in its own sketch).
/// </summary>
public sealed record FeatureField(string Key, string Title, int Default, int Min = 0, int Max = 1, string[]? Choices = null, bool Toggle = false, string? Tip = null, string Unit = "",
    bool Menu = false, string? Only = null);

public static class ArduinoFeatures
{
    public const string NotifyTab = "Notify";

    /// <summary>Every kind there is, in the Add Feature menu's order.</summary>
    public static readonly List<IArduinoFeature> All = new() { new LedMatrixFeature(), new LedStripFeature() };

    public static IArduinoFeature? Of(string id) => All.FirstOrDefault(f => f.Id == id);
}

/// <summary>WS2812 ("NeoPixel") LEDs on a pin: what the two light features share.</summary>
public abstract class LedFeature : IArduinoFeature
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract byte Type { get; }
    public string Tab => ArduinoFeatures.NotifyTab;
    public abstract IReadOnlyList<FeatureField> Fields { get; }

    protected static FeatureField Pin(int def) => new("pin", T("Data Pin"), def, 2, 13, Tip: T("The Arduino pin the LEDs' data wire is on"), Menu: true, Only: ArduinoSketch.DaisysSim);
    public static readonly FeatureField Brightness = new("brightness", T("Brightness"), 40, 1, 100, Tip: T("WS2812s are bright: 40% is plenty"), Unit: "%");
    protected static readonly FeatureField Order = new("rgb", T("Colour Order"), 0, Choices: new[] { "GRB (WS2812B)", "RGB" }, Only: ArduinoSketch.DaisysSim);

    /// <summary>The LEDs it has.</summary>
    public abstract int Count(ArduinoFeature f);

    /// <summary>Its option bits (the sketch's <c>flags</c>): bit 4 = RGB order; the rest are the feature's own.</summary>
    protected virtual byte Flags(ArduinoFeature f) => (byte)(f.Get(Order) == 1 ? 0x10 : 0);

    public byte[] Config(ArduinoFeature f) => new[]
    {
        Type, (byte)f.Get(Fields[0]), (byte)Count(f), (byte)Math.Round(f.Get(Brightness) * 2.55), Flags(f),
    };

    public abstract IEnumerable<(byte Command, byte[] Data)> Test(ArduinoFeature f, double seconds);

    /// <summary>A rainbow running along <paramref name="count"/> LEDs.</summary>
    protected static byte[] Rainbow(int count, double seconds)
    {
        var data = new byte[1 + count * 3];
        for (int i = 0; i < count; i++)
        {
            var (r, g, b) = Hue((i / (double)count + seconds * 0.5) % 1);
            data[1 + i * 3] = r; data[2 + i * 3] = g; data[3 + i * 3] = b;
        }
        return data;
    }

    private static (byte, byte, byte) Hue(double h)
    {
        double x = h * 6;
        int s = (int)x;
        byte up = (byte)((x - s) * 255), down = (byte)(255 - up);
        return s switch { 0 => (255, up, 0), 1 => (down, 255, 0), 2 => (0, 255, up), 3 => (0, down, 255), 4 => (up, 0, 255), _ => (255, 0, down) };
    }
}

/// <summary>An 8×8 RGB matrix (Notify's gear light): what's shown is sent as a picture (<see cref="ArduinoProtocol.Bitmap"/>).</summary>
public sealed class LedMatrixFeature : LedFeature
{
    public override string Id => "matrix";
    public override string Title => T("Notify (8×8 matrix)");
    public override byte Type => 2;

    public static readonly FeatureField Rotation = new("rotate", T("Rotation"), 0, Choices: new[] { "0°", "90°", "180°", "270°" });
    public static readonly FeatureField Serpentine = new("serpentine", T("Zigzag Wiring"), 1, Toggle: true, Tip: T("Every other row runs backwards (most 8×8 boards)"), Only: ArduinoSketch.DaisysSim);
    public static readonly FeatureField Mirror = new("mirror", T("Mirror"), 0, Toggle: true, Only: ArduinoSketch.DaisysSim);

    public override IReadOnlyList<FeatureField> Fields { get; } = new[] { Pin(6), Brightness, Rotation, Serpentine, Mirror, Order };

    public override int Count(ArduinoFeature f) => 64;

    protected override byte Flags(ArduinoFeature f) => (byte)(base.Flags(f) | (f.Get(Serpentine) != 0 ? 1 : 0) | (f.Get(Rotation) & 3) << 1 | (f.Get(Mirror) != 0 ? 8 : 0));

    public override IEnumerable<(byte, byte[])> Test(ArduinoFeature f, double seconds)
    {
        // the gears one after another, each a colour of the rainbow
        int step = (int)(seconds * 2.5) % 8;
        var glyph = Glyphs.Of(step == 0 ? "N" : step.ToString());
        var colour = new byte[][] { new byte[] { 255, 255, 255 }, new byte[] { 255, 0, 0 }, new byte[] { 255, 128, 0 }, new byte[] { 255, 255, 0 },
            new byte[] { 0, 255, 0 }, new byte[] { 0, 255, 255 }, new byte[] { 0, 0, 255 }, new byte[] { 255, 0, 255 } }[step];
        yield return (ArduinoProtocol.Bitmap, glyph.Concat(colour).Concat(new byte[3]).ToArray());
    }
}

/// <summary>A row of 8 or 16 RGB LEDs (Notify's shift lights): colours sent LED by LED (<see cref="ArduinoProtocol.Pixels"/>).</summary>
public sealed class LedStripFeature : LedFeature
{
    public override string Id => "strip";
    public override string Title => T("Shift (LED strip)");
    public override byte Type => 1;

    public static readonly FeatureField FirstLed = new("first", T("First LED"), 1, 1, 150, Tip: T("Where they start on the board's LEDs, counted from 1"), Only: ArduinoSketch.SimHub);
    public static readonly FeatureField Leds = new("leds", T("LEDs"), 8, 1, 32, Tip: T("8 or 16 for a stick; up to 32"));
    public static readonly FeatureField Reverse = new("reverse", T("Right to Left"), 0, Toggle: true, Tip: T("The first LED is on the right"));
    public static readonly FeatureField Style = new("style", T("Fill"), 0, Choices: new[] { T("Left to right"), T("Both ends to the middle") });

    public override IReadOnlyList<FeatureField> Fields { get; } = new[] { Pin(7), FirstLed, Leds, Brightness, Style, Reverse, Order };

    public override int Count(ArduinoFeature f) => Math.Clamp(f.Get(Leds), 1, 32);

    /// <summary>Its first LED in a SimHub sketch's chain, from 0.</summary>
    public int First(ArduinoFeature f) => Math.Clamp(f.Get(FirstLed), 1, 150) - 1;

    public override IEnumerable<(byte, byte[])> Test(ArduinoFeature f, double seconds)
    {
        yield return (ArduinoProtocol.Pixels, Rainbow(Count(f), seconds));
    }
}
