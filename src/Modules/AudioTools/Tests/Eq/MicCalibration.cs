using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.AudioTools.Tests.Eq;

/// <summary>
/// A measurement mic's calibration file (e.g. the one Dayton Audio supplies for each iMM-6 by serial number): lines of
/// "frequency  dB [phase]", plus optional header lines in quotes or starting with *. The dB values are the mic's
/// deviation from flat, so they're subtracted from what it measures.
/// </summary>
public sealed partial class MicCalibration
{
    private readonly double[] hz, db;

    public string Name { get; }

    private MicCalibration(string name, double[] hz, double[] db)
    {
        Name = name;
        this.hz = hz;
        this.db = db;
    }

    public int Points => hz.Length;

    /// <summary>The file's points as read (for the EQ Wizard's diagnostics).</summary>
    public IEnumerable<(double Hz, double Db)> All => hz.Zip(db);

    /// <summary>Reads a calibration file. Throws with a readable message if it isn't one.</summary>
    public static MicCalibration Load(string path)
    {
        var points = new SortedDictionary<double, double>();
        foreach (var raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '"' or '*' or '#' or ';') continue;
            var parts = Separator().Split(line);
            if (parts.Length < 2) continue;
            if (!TryNumber(parts[0], out double f) || !TryNumber(parts[1], out double d)) continue;
            if (f < 1 || f > 100000 || Math.Abs(d) > 60) continue;
            points[f] = d;
        }
        if (points.Count < 10)
            throw new InvalidDataException(T("That doesn't look like a microphone calibration file (it needs lines of frequency and dB)."));
        return new MicCalibration(Path.GetFileName(path), points.Keys.ToArray(), points.Values.ToArray());
    }

    private static bool TryNumber(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ||
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex Separator();

    /// <summary>The mic's deviation at <paramref name="f"/> (log-frequency interpolation; the end values beyond the file's range).</summary>
    public double At(double f)
    {
        if (f <= hz[0]) return db[0];
        if (f >= hz[^1]) return db[^1];
        int i = Array.BinarySearch(hz, f);
        if (i >= 0) return db[i];
        i = ~i;
        double t = Math.Log(f / hz[i - 1]) / Math.Log(hz[i] / hz[i - 1]);
        return db[i - 1] + (db[i] - db[i - 1]) * t;
    }
}
