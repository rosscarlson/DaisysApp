using DaisysApp.Controllers;
using DaisysApp.Settings;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// Saved in %APPDATA%\DaisysApp\Calibration.json (the Calibration module's file until 4.6.7, kept as it was). The
/// calibrations themselves are Windows' (see <see cref="Calibrator"/>); this only keeps what the block needs. Axes are
/// named "VID:PID/offset", as Windows files calibration by VID and PID.
/// </summary>
public sealed class CalibrationSettings
{
    private const string FileName = "Calibration";

    /// <summary>Axes Find Ranges leaves out (the Calibration module's wizard's Ignore until 4.6.7).</summary>
    public HashSet<string> Ignored { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each axis's calibration before Daisy's App first changed it, for Original.</summary>
    public Dictionary<string, Cal> Original { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string KeyOf(ControllerInfo c) => $"{c.Vid:X4}:{c.Pid:X4}";

    public static string KeyOf(ControllerInfo c, Axis a) => $"{KeyOf(c)}/{a.Offset}";

    public static CalibrationSettings Load() => JsonStore.Load<CalibrationSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
