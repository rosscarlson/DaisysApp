using System.Globalization;
using System.IO;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>One lap of a session (samples <see cref="Start"/> to <see cref="End"/>): its number (0 the first), time,
/// length in metres (NaN: unknown), and whether it was driven from line to line.</summary>
internal sealed record Lap(int Number, int Start, int End, double Seconds, double Length, bool Complete)
{
    public string Text => Complete ? F("Lap {0}  {1}", Number + 1, LapClock(Seconds)) : F("Lap {0} (part)", Number + 1);

    /// <summary>"1:42.315".</summary>
    public static string LapClock(double s) => double.IsNaN(s) ? "–" : $"{(int)(s / 60)}:{s % 60:00.000}";
}

/// <summary>(since 4.14.1) A pause in a session's log: samples <see cref="From"/> to <see cref="End"/> (not included),
/// no rows logged. Hidden (at first) it's cut out of Compare's time, playback and the video.</summary>
internal sealed class Pause(int from, int end)
{
    public int From { get; } = from;
    public int End { get; } = end;
    public bool Hidden { get; set; } = true;
    public double Seconds => (End - From) * SessionData.Dt;
}

/// <summary>
/// A session log read back, resampled to an even 100 samples a second (straight lines between the logged rows; a gap
/// of more than a quarter of a second in the log is left as NaN). Pedals 0–1; steering 0.5 is straight ahead. Since
/// 4.4.0 (format 2) the game's telemetry too, NaN where it wasn't sent: speed (m/s), lateral and longitudinal g, the lap
/// and time into it, distance, place, and suspension speed (since 4.6.10 the rpm, its maximum, the gear and the power);
/// and from the # lines the car, driver and name.
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
    public required double[] Speed { get; init; }
    public required double[] LatG { get; init; }
    public required double[] LonG { get; init; }
    public required double[] LapNo { get; init; }
    public required double[] LapTime { get; init; }
    public required double[] Distance { get; init; }
    public required double[] X { get; init; }
    public required double[] Z { get; init; }
    public required double[] Suspension { get; init; }
    public required double[] Rpm { get; init; }
    public required double[] MaxRpm { get; init; }
    /// <summary>The gear: -1 reverse, 0 neutral, NaN unknown (a log from before 4.6.10).</summary>
    public required double[] Gear { get; init; }
    /// <summary>The engine's power, kW.</summary>
    public required double[] Power { get; init; }
    public string? Car { get; init; }
    /// <summary>(since 4.14.1) The pauses in the log (the game paused: no rows for a second or more), in order.</summary>
    public List<Pause> Pauses { get; } = new();
    public string? Driver { get; init; }
    public string? Name { get; init; }
    public List<Lap> Laps { get; } = new();

    public int Count => Throttle.Length;
    public double Seconds => Count * Dt;
    public bool HasSpeed { get; private set; }
    public bool HasG { get; private set; }
    public bool HasDistance { get; private set; }
    public bool HasGear { get; private set; }

    public Lap? BestLap => Laps.Where(l => l.Complete).MinBy(l => l.Seconds);

    private const int Columns = 18;
    /// <summary>(since 4.14.1) A gap in the log this long is a pause (the game paused, or rewound).</summary>
    private const long PauseMs = 1000;

    /// <summary>(since 4.14.1) The hidden pause a moment (a sample, fractional) is in, or null.</summary>
    public Pause? HiddenAt(double sample)
    {
        foreach (var p in Pauses) if (p.Hidden && sample >= p.From && sample < p.End) return p;
        return null;
    }
    // columns kept as they were between rows rather than drawn between them: the lap and the gear
    private static readonly bool[] Step = { false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, true, false };

    /// <summary>The # lines of a session file: when it started, and its driver and name if it was exported; since 4.15.0
    /// its video on YouTube, if it has one ("# video,link,seconds": where in the video the session starts).</summary>
    public static (DateTime? Started, string? Driver, string? Name, (string Url, double At)? Video) Head(string path)
    {
        DateTime? started = null;
        string? driver = null, name = null;
        (string, double)? video = null;
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            string? line;
            for (int i = 0; i < 12 && (line = reader.ReadLine()) != null; i++)
            {
                if (!line.StartsWith('#')) continue;
                if (line.StartsWith("# started,", StringComparison.Ordinal)
                    && DateTime.TryParse(line[10..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) started = t.ToLocalTime();
                else if (line.StartsWith("# driver,", StringComparison.Ordinal)) driver = line[9..].Trim();
                else if (line.StartsWith("# name,", StringComparison.Ordinal)) name = line[7..].Trim();
                else if (line.StartsWith("# video,", StringComparison.Ordinal) && line[8..].Split(',') is { Length: >= 2 } v
                    && double.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double at) && v[0].Trim().Length > 0) video = (v[0].Trim(), at);
            }
        }
        catch { }
        return (started, driver, name, video);
    }

    public static SessionData Load(string path)
    {
        var ms = new List<long>();
        var cols = new List<double>[Columns];
        for (int i = 0; i < Columns; i++) cols[i] = new List<double>();
        DateTime started = SessionLog.StartedOf(new FileInfo(path));
        string? car = null, driver = null, name = null;
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
                    else if (line.StartsWith("# car,", StringComparison.Ordinal)) car = line[6..].Trim();
                    else if (line.StartsWith("# driver,", StringComparison.Ordinal)) driver = line[9..].Trim();
                    else if (line.StartsWith("# name,", StringComparison.Ordinal)) name = line[7..].Trim();
                    continue;
                }
                if (!char.IsDigit(line[0])) continue; // the header
                var parts = line.Split(',');
                if (parts.Length < 6 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at)) continue;
                if (ms.Count > 0 && at <= ms[^1]) continue;
                ms.Add(at);
                for (int i = 0; i < Columns; i++)
                {
                    double v = i + 1 < parts.Length && double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : double.NaN;
                    cols[i].Add(i < 5 && !double.IsNaN(v) ? Math.Clamp(v, 0, 1) : v);
                }
            }
        }
        Rewinds(ms, cols);
        var pauses = new List<Pause>();
        for (int k = 1; k < ms.Count; k++)
        {
            if (ms[k] - ms[k - 1] < PauseMs) continue;
            int from = (int)(ms[k - 1] / (1000 / Hz)) + 1, end = (int)Math.Ceiling(ms[k] / (1000.0 / Hz));
            if (end > from) pauses.Add(new Pause(from, end));
        }
        int n = ms.Count == 0 ? 0 : (int)(ms[^1] / (1000 / Hz)) + 1;
        var outs = new double[Columns][];
        for (int c = 0; c < Columns; c++)
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
                // the lap and anything counting from its start (time, and the distance where the game resets it) don't
                // draw a line across the line
                bool across = !Step[c] && (c == 9 || c == 10) && !double.IsNaN(a) && !double.IsNaN(b) && b < a;
                o[i] = Step[c] || across ? a
                    : double.IsNaN(a) || double.IsNaN(b) ? (t - t0 < t1 - t ? a : b) : a + (b - a) * (t - t0) / (t1 - t0);
            }
        }
        var data = new SessionData
        {
            Path = path,
            Started = started,
            Steer = outs[0],
            Throttle = outs[1],
            Brake = outs[2],
            Clutch = outs[3],
            Handbrake = outs[4],
            Speed = outs[5],
            LatG = outs[6],
            LonG = outs[7],
            LapNo = outs[8],
            LapTime = outs[9],
            Distance = outs[10],
            X = outs[11],
            Z = outs[12],
            Suspension = outs[13],
            Rpm = outs[14],
            MaxRpm = outs[15],
            Gear = outs[16],
            Power = outs[17],
            Car = car,
            Driver = driver,
            Name = name,
        };
        data.HasSpeed = data.Speed.Any(v => !double.IsNaN(v));
        data.HasG = data.LatG.Any(v => !double.IsNaN(v));
        data.HasDistance = data.Distance.Any(v => !double.IsNaN(v));
        data.HasGear = data.Gear.Any(v => v >= 1) && data.Rpm.Any(v => v > 0);
        data.Pauses.AddRange(pauses);
        data.FindLaps();
        return data;
    }

    /// <summary>(since 4.10.14) A rewind (Forza): the log back after a pause of a second or more, less far driven on the same
    /// lap or the one before (its lap time back too: after a finish Horizon sends a few frames with the distance back near
    /// 0 and the clock as it was), at most a minute back. What was driven again is dropped (the part rewound over), a gap
    /// left where it was, so the race reads as driven and its video still lines up by the clock.</summary>
    private static void Rewinds(List<long> ms, List<double>[] cols)
    {
        List<double> lap = cols[8], time = cols[9], distance = cols[10];
        for (int k = 1; k < ms.Count; k++)
        {
            double d = distance[k];
            if (ms[k] - ms[k - 1] < 1000 || double.IsNaN(d) || !(distance[k - 1] > d + 1) || !(lap[k] <= lap[k - 1]) || !(lap[k] >= lap[k - 1] - 1)) continue;
            if (lap[k] == lap[k - 1] && !(time[k] < time[k - 1] - 0.5)) continue;
            int s = k - 1;
            while (s > 0 && distance[s - 1] > d) s--;
            if (s == 0 || ms[k - 1] - ms[s] > 60_000) continue;
            ms.RemoveRange(s, k - s);
            foreach (var c in cols) c.RemoveRange(s, k - s);
            k = s;
        }
    }

    /// <summary>The laps, from the lap number the game sends: a run of the same number is a lap, complete when the next
    /// one follows it and it started at the line (the time into it near 0).</summary>
    private void FindLaps()
    {
        int i = 0, n = Count;
        while (i < n)
        {
            if (double.IsNaN(LapNo[i])) { i++; continue; }
            int s = i;
            double no = LapNo[i];
            while (i + 1 < n && (LapNo[i + 1] == no || double.IsNaN(LapNo[i + 1]))) i++;
            int e = i;
            i++;
            if (e - s < 5 * Hz) continue;
            bool next = i < n && LapNo[i] == no + 1;
            double startT = FirstKnown(LapTime, s, s + Hz);
            bool fromLine = !double.IsNaN(startT) ? startT < 1.0 : s > 0 && !double.IsNaN(LapNo[s - 1]);
            double time = LastKnown(LapTime, e - Hz, e);
            if (double.IsNaN(time)) time = (e - s) * Dt;
            // the line was crossed between the two samples: add what was left of the lap
            else if (next && !double.IsNaN(LapTime[e]) && !double.IsNaN(LapTime[i]) && LapTime[i] < Dt) time = LapTime[e] + Dt - LapTime[i];
            double d0 = FirstKnown(Distance, s, s + Hz), d1 = LastKnown(Distance, e - Hz, e);
            Laps.Add(new Lap((int)no, s, e, time, double.IsNaN(d0) || double.IsNaN(d1) ? double.NaN : d1 - d0, next && fromLine));
        }
    }

    private static double FirstKnown(double[] x, int a, int b)
    {
        for (int k = Math.Max(0, a); k <= Math.Min(x.Length - 1, b); k++) if (!double.IsNaN(x[k])) return x[k];
        return double.NaN;
    }

    private static double LastKnown(double[] x, int a, int b)
    {
        for (int k = Math.Min(x.Length - 1, b); k >= Math.Max(0, a); k--) if (!double.IsNaN(x[k])) return x[k];
        return double.NaN;
    }
}
