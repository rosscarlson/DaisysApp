using DaisysApp.Settings;

namespace DaisysApp.Applets.Rgb;

/// <summary>Saved in %APPDATA%\DaisysApp\Rgb.json.</summary>
public sealed class RgbSettings
{
    private const string FileName = "Rgb";

    /// <summary>Each device's effect, by its <see cref="IRgbDevice.Id"/>. A device without one is left as it was.</summary>
    public Dictionary<string, LightSetting> Lights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All lights off (each device's own setting kept for when they're on again).</summary>
    public bool Off { get; set; }

    /// <summary>What the All Devices row last set.</summary>
    public LightSetting All { get; set; } = new() { Effect = LightEffect.Rainbow };

    /// <summary>The SMBus devices the last search found (it needs administrator rights, so it isn't done at every start).</summary>
    public List<SmbusDevices.Seen> Smbus { get; set; } = new();

    public static RgbSettings Load() => JsonStore.Load<RgbSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
