using System.Globalization;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>Speeds as the region shows them: mph where it isn't metric, km/h elsewhere.</summary>
internal static class Speeds
{
    public static readonly bool Mph = !RegionInfo.CurrentRegion.IsMetric;
    public static string Unit => Mph ? T("mph") : T("km/h");
    public static double Of(double ms) => ms * (Mph ? 2.23694 : 3.6);
    public static string Text(double ms) => double.IsNaN(ms) ? "–" : $"{Of(ms):0} {Unit}";
}

/// <summary>
/// Contact with a wall or another car, found in the telemetry as the shaker's Crashes and Impacts feel it: a
/// <see cref="Crash"/> is the car's horizontal acceleration at <see cref="CrashG"/> or more; an impact a jolt, the
/// horizontal g changing by <see cref="JoltG"/> or more within 50 ms (a car or wall, not braking or a kerb, which build
/// slower), or a hard suspension hit with a jolt of <see cref="KnockJoltG"/>. What it cost: the speed it took off (more
/// than the trend before it), and the time that takes to get back (accelerating at about 0.4 g), plus any time stopped.
/// </summary>
internal sealed record Incident(int At, bool Crash, double G, double SpeedBefore, double SpeedLost, double SecondsLost)
{
    public const double CrashG = 3.5, JoltG = 1.3, KnockJoltG = 0.8, KnockSusp = 1.5;
    private const int Hz = SessionData.Hz;
    private const double Recover = 4.0; // m/s², getting the speed back

    public string Describe() => (Crash ? T("Crash") : T("Impact")) + ": " + string.Join(", ", new[]
    {
        F("{0:0.0} g", G),
        double.IsNaN(SpeedLost) ? null : F("{0} lost", Speeds.Text(SpeedLost)),
        double.IsNaN(SecondsLost) ? null : F("about {0:0.0} s", SecondsLost),
    }.Where(x => x != null));

    public static List<Incident> Find(SessionData d)
    {
        var found = new List<Incident>();
        int n = d.Count;
        double H(int i) => double.IsNaN(d.LatG[i]) || double.IsNaN(d.LonG[i]) ? double.NaN : Math.Sqrt(d.LatG[i] * d.LatG[i] + d.LonG[i] * d.LonG[i]);
        for (int i = 5; i < n; i++)
        {
            double h = H(i);
            if (double.IsNaN(h) || double.IsNaN(d.LatG[i - 5]) || double.IsNaN(d.LonG[i - 5])) continue;
            if (d.HasSpeed && !(d.Speed[i] > 5 || d.Speed[i - 5] > 5)) continue;
            double jolt = Math.Sqrt(Math.Pow(d.LatG[i] - d.LatG[i - 5], 2) + Math.Pow(d.LonG[i] - d.LonG[i - 5], 2));
            bool crash = h >= CrashG;
            bool knock = !double.IsNaN(d.Suspension[i]) && d.Suspension[i] >= KnockSusp && jolt >= KnockJoltG;
            if (!crash && jolt < JoltG && !knock) continue;
            // the hardest part of it
            double peak = h;
            for (int k = i; k < Math.Min(n, i + 15); k++) { double x = H(k); if (x > peak) peak = x; }
            crash |= peak >= CrashG;
            found.Add(Cost(d, i, crash, peak));
            i += crash ? Hz * 3 / 2 : Hz; // one hit, not each frame of it
        }
        return found;
    }

    private static Incident Cost(SessionData d, int i, bool crash, double g)
    {
        if (!d.HasSpeed) return new Incident(i, crash, g, double.NaN, double.NaN, double.NaN);
        int n = d.Count;
        int b = Math.Max(0, i - Hz / 5), b0 = Math.Max(0, i - Hz / 2);
        double v0 = d.Speed[b], vEarly = d.Speed[b0];
        if (double.IsNaN(v0)) return new Incident(i, crash, g, double.NaN, double.NaN, double.NaN);
        double slope = double.IsNaN(vEarly) || b == b0 ? 0 : (v0 - vEarly) / (b - b0);
        slope = Math.Min(0, slope); // braking before it carries on; speeding up doesn't count as lost
        int end = Math.Min(n - 1, i + (crash ? Hz * 3 / 2 : Hz * 6 / 10));
        double lost = 0;
        for (int k = i; k <= end; k++)
        {
            if (double.IsNaN(d.Speed[k])) continue;
            double expected = Math.Max(0, v0 + slope * (k - b));
            lost = Math.Max(lost, expected - d.Speed[k]);
        }
        double seconds = v0 > 5 ? lost * lost / (2 * Recover * v0) : 0;
        if (crash)
        {
            // time stopped (or nearly) after it
            int stopped = 0;
            for (int k = i; k < Math.Min(n, i + 10 * Hz); k++) if (d.Speed[k] < 3) stopped++;
            seconds += stopped * SessionData.Dt;
        }
        return new Incident(i, crash, g, v0, lost, Math.Min(20, seconds));
    }
}
