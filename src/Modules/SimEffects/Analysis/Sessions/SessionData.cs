using System.Globalization;
using System.IO;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>One lap of a session (samples <see cref="Start"/> to <see cref="End"/>): its number (0 the first), time,
/// length in metres (NaN: unknown), and whether it was driven from line to line.</summary>
internal sealed record Lap(int Number, int Start, int End, double Seconds, double Length, bool Complete)
{
    public string Text => Complete ? F("Lap {0}  {1}", Number + 1, LapClock(Seconds)) : F("Lap {0} (part)", Number + 1);

    /// <summary>"1:42.315".</summary>
    public static string LapClock(double s)
    {
        if (double.IsNaN(s)) return "–";
        long ms = (long)Math.Round(s * 1000);
        return $"{ms / 60000}:{ms % 60000 / 1000:00}.{ms % 1000:000}";
    }
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
    /// <summary>(since 4.15.4) Has the engine's rpm.</summary>
    public bool HasRpm { get; private set; }
    /// <summary>(since 4.21.2) Each lap's last row as logged (by its number): the time into it, and how far that clock
    /// moved a frame (the middle of the last 5 steps since 4.21.10, their average before). The frame that crosses the finish line isn't sent, so a lap the log stops in
    /// ends a frame after its last row (this matched the game's own times to the millisecond).</summary>
    private readonly Dictionary<int, (double Time, double Step)> ends = new();
    /// <summary>The frames looked at for <see cref="ends"/>' step.</summary>
    private const int StepFrames = 5;

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

    /// <param name="recorded">(since 4.17.11) The laps the game recorded for this race (its number, 1 the first, and time):
    /// the last one whole though the log stops at the line.</param>
    public static SessionData Load(string path, IReadOnlyDictionary<int, float>? recorded = null)
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
        var ends = new Dictionary<int, (double, double)>();
        for (int k = 0; k < ms.Count; k++)
        {
            double lapNo = cols[8][k], at = cols[9][k];
            if (double.IsNaN(lapNo) || double.IsNaN(at) || (k + 1 < ms.Count && cols[8][k + 1] == lapNo && !double.IsNaN(cols[9][k + 1]))) continue;
            int b = k - StepFrames;
            double step = 0;
            if (b >= 0 && cols[8][b] == lapNo && at - cols[9][b] is > 0 and < 0.05 * StepFrames)
            {
                // (since 4.21.10) the middle step: a row not logged made the average a fifth of a frame longer
                var steps = new double[StepFrames];
                for (int j = 0; j < StepFrames; j++) steps[j] = cols[9][b + j + 1] - cols[9][b + j];
                Array.Sort(steps);
                step = steps[StepFrames / 2] is > 0 and < 0.05 and var m ? m : 0;
            }
            ends[(int)lapNo] = (at, step);
        }
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
        data.HasRpm = data.Rpm.Any(v => v > 0);
        foreach (var (no, end) in ends) data.ends[no] = end;
        data.Pauses.AddRange(pauses);
        data.FindLaps(recorded);
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
    /// one follows it and it started at the line (the time into it near 0). Since 4.17.11 the last one too when the log
    /// stops in it (a race's finish: the log ends as the line's crossed) and the game recorded it (else, someone
    /// else's, it's as long as a whole one, within 2%). Since 4.21.2 a lap ends at its last row (not across the gap
    /// after a finish, which made it the gap's length longer), one the data stopped in with its clock still running ends
    /// a frame after it (<see cref="FrameStep"/>), and a lap the game recorded has the game's time.</summary>
    private void FindLaps(IReadOnlyDictionary<int, float>? recorded)
    {
        int i = 0, n = Count;
        bool lastFromLine = false;
        while (i < n)
        {
            if (double.IsNaN(LapNo[i])) { i++; continue; }
            int s = i;
            double no = LapNo[i];
            while (i + 1 < n && (LapNo[i + 1] == no || double.IsNaN(LapNo[i + 1]))) i++;
            int e = i;
            i++;
            // (since 4.21.2) its last row with a time: not the gap after it and what comes then (the finish, then the
            // next race loading, with no time into the lap, the lap's number or the next's)
            int end = e;
            while (e > s && double.IsNaN(LapNo[e])) e--;
            int timed = e;
            while (timed > s && double.IsNaN(LapTime[timed])) timed--;
            if (!double.IsNaN(LapTime[timed])) e = timed;
            bool stopped = e < end || i >= n;
            if (e - s < 5 * Hz) continue;
            bool next = i < n && LapNo[i] == no + 1;
            double startT = FirstKnown(LapTime, s, s + Hz);
            bool fromLine = !double.IsNaN(startT) ? startT < 1.0 : s > 0 && !double.IsNaN(LapNo[s - 1]);
            double time = LastKnown(LapTime, e - Hz, e);
            if (double.IsNaN(time)) time = (e - s) * Dt;
            // (since 4.21.2) the frame that crossed the line has no time into the lap (the next lap's starts at 0), or
            // wasn't sent (the finish: the data stops): a frame after its last row, the clock still running
            else if ((next || stopped) && ends.TryGetValue((int)no, out var row) && row.Step > 0 && Math.Abs(row.Time - time) < 0.05
                && LastKnown(LapTime, e - Hz, e - Hz / 5) is var before && time > before)
                time = Math.Round(row.Time + row.Step, 3);
            // the line was crossed between the two samples: add what was left of the lap
            else if (next && !double.IsNaN(LapTime[e]) && !double.IsNaN(LapTime[i]) && LapTime[i] < Dt) time = LapTime[e] + Dt - LapTime[i];
            if (recorded != null && recorded.TryGetValue((int)no + 1, out float kept) && kept > 0 && Math.Abs(kept - time) < 1) time = kept;
            double d0 = FirstKnown(Distance, s, s + Hz), d1 = LastKnown(Distance, e - Hz, e);
            Laps.Add(new Lap((int)no, s, e, time, double.IsNaN(d0) || double.IsNaN(d1) ? double.NaN : d1 - d0, next && fromLine));
            lastFromLine = fromLine;
        }
        if (Laps.Count == 0 || Laps[^1] is not { Complete: false } last || !lastFromLine || last.End < n - 1 - Hz) return;
        float? game = recorded != null && recorded.TryGetValue(last.Number + 1, out float rt) && rt > 0 ? rt : null;
        bool alike = !double.IsNaN(last.Length) && Laps.Any(l => l.Complete && !double.IsNaN(l.Length) && Math.Abs(l.Length - last.Length) <= 0.02 * l.Length);
        if (game != null || alike) Laps[^1] = last with { Complete = true, Seconds = game ?? last.Seconds };
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
