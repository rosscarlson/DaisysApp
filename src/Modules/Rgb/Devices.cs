namespace DaisysApp.Applets.Rgb;

public enum DeviceKind { Motherboard, Header, Ram, Gpu }

/// <summary>
/// One set of lights that takes one effect: a motherboard's own lights, one of its fan / strip headers, the RAM, a
/// graphics card. Made by the finders (<see cref="AuraUsb"/>, <see cref="NvidiaGpus"/>, <see cref="SmbusDevices"/>).
/// Its methods are called on <see cref="RgbHub"/>'s thread only.
/// </summary>
public interface IRgbDevice : IDisposable
{
    /// <summary>Stable across starts: what its setting is saved under.</summary>
    string Id { get; }
    string Name { get; }
    DeviceKind Kind { get; }
    /// <summary>The controller and its version, for the tooltip and the log.</summary>
    string About { get; }

    /// <summary>The effects the device runs by itself (kept when Daisy's App closes).</summary>
    IReadOnlyCollection<LightEffect> Effects { get; }
    /// <summary>Whether its own effects have a speed.</summary>
    bool HasSpeed { get; }

    /// <summary>LEDs <see cref="Show"/> takes (0 when it can't be given colours, so Daisy's App can't draw other effects on it).</summary>
    int Leds { get; }

    /// <summary>Changes need administrator rights (the SMBus): applied with Apply, through Windows' question.</summary>
    bool NeedsAdmin { get; }

    /// <summary>Several devices on one controller share it: <see cref="Save"/> is called once per owner.</summary>
    object Owner { get; }

    /// <summary>Starts one of <see cref="Effects"/>.</summary>
    void Apply(LightSetting s);

    /// <summary>Keeps what was applied on the device itself (through a restart), where it can.</summary>
    void Save();

    /// <summary>Sets each LED (for the effects Daisy's App draws).</summary>
    void Show(Rgb[] colours);
}

public static class Devices
{
    public static readonly LightEffect[] All = Enum.GetValues<LightEffect>();

    /// <summary>What the device can be set to: its own effects, and every one when Daisy's App can draw them.</summary>
    public static IEnumerable<LightEffect> Offered(IRgbDevice d) => d.Leds > 0 ? All : All.Where(d.Effects.Contains);

    /// <summary>Whether Daisy's App draws <paramref name="e"/> on the device (it doesn't run it itself).</summary>
    public static bool Drawn(IRgbDevice d, LightEffect e) => !d.Effects.Contains(e) && d.Leds > 0;
}
