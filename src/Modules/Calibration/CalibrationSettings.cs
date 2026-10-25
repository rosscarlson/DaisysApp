using DaisysApp.Settings;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// Saved in %APPDATA%\DaisysApp\Calibration.json. The calibrations themselves are Windows' (see <see cref="Controllers"/>);
/// this only keeps what the tab needs. Axes are named "VID:PID/offset", as Windows files calibration by VID and PID.
/// </summary>
public sealed class CalibrationSettings
{
    private const string FileName = "Calibration";

    /// <summary>The controller last picked (its DirectInput instance).</summary>
    public Guid? Selected { get; set; }

    /// <summary>Axes the wizard leaves out (Ignore).</summary>
    public HashSet<string> Ignored { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each axis's calibration before Daisy's App first changed it, for Original.</summary>
    public Dictionary<string, Cal> Original { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string KeyOf(ControllerInfo c, Axis a) => $"{c.Key}/{a.Offset}";

    public static CalibrationSettings Load() => JsonStore.Load<CalibrationSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
