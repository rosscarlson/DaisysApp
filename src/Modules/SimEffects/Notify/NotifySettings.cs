using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Notify;

/// <summary>Notify's colours (as "#RRGGBB"). Saved in %APPDATA%\DaisysApp\Notify.json; the shift points are the shared
/// <see cref="Lights.LightsSettings"/>.</summary>
public sealed class NotifySettings
{
    private const string FileName = "Notify";

    public bool Enabled { get; set; } = true;
    // the gear matrix
    public string Gear { get; set; } = "#FFFFFF";
    public string Neutral { get; set; } = "#00FF00";
    public string Reverse { get; set; } = "#FF0000";
    /// <summary>The gear at the shift point.</summary>
    public string GearShift { get; set; } = "#0000FF";
    /// <summary>The gear flashing (over-rev).</summary>
    public string GearFlash { get; set; } = "#FF0000";
    // the shift lights: the first half, the next 30%, the last 20%, and all of them at the shift point
    public string Low { get; set; } = "#00FF00";
    public string Mid { get; set; } = "#FFFF00";
    public string High { get; set; } = "#FF0000";
    public string Shift { get; set; } = "#0000FF";
    /// <summary>The shift lights flash too (over-rev), not only the gear.</summary>
    public bool StripFlashes { get; set; } = true;

    public static NotifySettings Load() => JsonStore.Load<NotifySettings>(FileName);

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>The colours the pickers offer.</summary>
public static class LightColours
{
    public static readonly (string Name, string Hex)[] All =
    {
        (T("White"), "#FFFFFF"), (T("Red"), "#FF0000"), (T("Orange"), "#FF6000"), (T("Yellow"), "#FFFF00"),
        (T("Green"), "#00FF00"), (T("Cyan"), "#00FFFF"), (T("Blue"), "#0000FF"), (T("Purple"), "#8000FF"),
        (T("Magenta"), "#FF00FF"), (T("Pink"), "#FF4080"), (T("Off"), "#000000"),
    };

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out uint v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (255, 255, 255);
    }
}
