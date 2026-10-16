using System.Globalization;
using System.IO;
using DaisysApp.Applets.Gaming.Sessions;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>
/// A session log read back, resampled to an even 100 samples a second (straight lines between the logged rows; a gap
/// of more than a quarter of a second in the log is left as NaN). Values 0–1; steering 0.5 is straight ahead.
/// </summary>
internal sealed class SessionData
{
    public const int Hz = 100;
    public const double Dt = 1.0 / Hz;

    public required string Path { get; init; }
    public required DateTime Started { get; init; }
    public required double[] Steer { get; init; }
    public required double[] Throttle { get; init; }
    public required double[] Brake { get; init; }
    public required double[] Clutch { get; init; }
    public required double[] Handbrake { get; init; }

    public int Count => Throttle.Length;
    public double Seconds => Count * Dt;

    public static SessionData Load(string path)
    {
        var ms = new List<long>();
        var cols = new List<double>[5];
        for (int i = 0; i < 5; i++) cols[i] = new List<double>();
        DateTime started = SessionLog.StartedOf(new FileInfo(path));
        using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("# started,", StringComparison.Ordinal)
                        && DateTime.TryParse(line[10..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) started = t.ToLocalTime();
                    continue;
                }
                if (!char.IsDigit(line[0])) continue; // the header
                var parts = line.Split(',');
                if (parts.Length < 6 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at)) continue;
                if (ms.Count > 0 && at <= ms[^1]) continue;
                ms.Add(at);
                for (int i = 0; i < 5; i++)
                    cols[i].Add(double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? Math.Clamp(v, 0, 1) : double.NaN);
            }
        }
        int n = ms.Count == 0 ? 0 : (int)(ms[^1] / (1000 / Hz)) + 1;
        var outs = new double[5][];
        for (int c = 0; c < 5; c++)
        {
            var o = outs[c] = new double[n];
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i * 1000.0 / Hz;
                while (j + 1 < ms.Count && ms[j + 1] <= t) j++;
                if (j + 1 >= ms.Count) { o[i] = ms[j] == t ? cols[c][j] : double.NaN; continue; }
                double t0 = ms[j], t1 = ms[j + 1];
                if (t1 - t0 > 250) { o[i] = t - t0 < 15 ? cols[c][j] : double.NaN; continue; }
                double a = cols[c][j], b = cols[c][j + 1];
                o[i] = double.IsNaN(a) || double.IsNaN(b) ? (t - t0 < t1 - t ? a : b) : a + (b - a) * (t - t0) / (t1 - t0);
            }
        }
        return new SessionData
        {
            Path = path,
            Started = started,
            Steer = outs[0],
            Throttle = outs[1],
            Brake = outs[2],
            Clutch = outs[3],
            Handbrake = outs[4],
        };
    }
}
