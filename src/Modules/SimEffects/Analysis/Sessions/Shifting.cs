namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>What a gear change was, as Shifting judges it.</summary>
internal enum ShiftKind { Good, Early, Limiter, PartThrottle, Down, OverRev }

/// <summary>A gear change: where (the last sample in the old gear), from and to, the rpm it was made at (an upshift:
/// the highest in the quarter second before; a downshift: the highest in the 0.6 s after), as a share of the engine's
/// maximum, and the best point to upshift from that gear (NaN: not known).</summary>
internal sealed class Shift
{
    public int At { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public double Rpm { get; init; }
    public double Pct { get; init; }
    public double BestPct { get; init; } = double.NaN;
    public ShiftKind Kind { get; init; }
    public bool Up => To > From;
    public string Gears => $"{From}→{To}";
}

/// <summary>
/// (since 4.6.10) How the gears are used in a race, from the rpm, gear and power its log has (none before 4.6.10):
/// each upshift against the best point to make it, the time on the rev limiter, and downshifts that over-rev. The best
/// upshift point comes from the car itself: its power at full throttle by rpm, and each gear's ratio (rpm over speed),
/// so it's where the power in the next gear, at the rpm it drops to, matches the power now; if the power still climbs
/// to the limiter, just short of it.
/// </summary>
internal sealed class ShiftAnalysis
{
    private const int Hz = SessionData.Hz;
    /// <summary>On the limiter at this share of the engine's maximum rpm or more.</summary>
    public const double LimiterPct = 98;
    /// <summary>An upshift this far (points of the maximum rpm) under the best point is early.</summary>
    public const double EarlyBy = 5;

    public bool HasData { get; }
    /// <summary>(since 4.7.0) The engine's maximum rpm (the median the game sent), for the chart's rpm.</summary>
    public double MaxRpm { get; }
    public List<Shift> Shifts { get; } = new();
    /// <summary>Seconds on the limiter with the throttle down, in all and in the highest gear used.</summary>
    public double LimiterS { get; private set; }
    public double TopGearLimiterS { get; private set; }
    public int TopGear { get; private set; }
    /// <summary>The best upshift point from each gear (share of the maximum rpm), where it's known.</summary>
    public Dictionary<int, double> Best { get; } = new();
    /// <summary>The limiter's stretches (start and end samples), for the trace.</summary>
    public List<(int From, int To)> OnLimiter { get; } = new();

    private readonly SessionData d;

    public ShiftAnalysis(SessionData data)
    {
        d = data;
        HasData = data.HasGear;
        if (!HasData) return;
        TopGear = (int)data.Gear.Where(g => g >= 1).Max();
        var maxes = data.MaxRpm.Where(m => m > 0).OrderBy(m => m).ToList();
        MaxRpm = maxes.Count > 0 ? maxes[maxes.Count / 2] : double.NaN;
        var ratios = Ratios();
        var power = PowerCurve();
        if (power != null)
            foreach (var (g, k) in ratios)
                if (ratios.TryGetValue(g + 1, out double k2)) Best[g] = BestUpshift(power, k2 / k);
        FindShifts();
        FindLimiter();
    }

    private bool Clutched(int i) => !double.IsNaN(d.Clutch[i]) && d.Clutch[i] > 0.1;

    /// <summary>rpm over speed in each gear (the median of steady driving in it).</summary>
    private Dictionary<int, double> Ratios()
    {
        var by = new Dictionary<int, List<double>>();
        for (int i = Hz / 2; i < d.Count; i++)
        {
            double g = d.Gear[i], v = d.Speed[i], r = d.Rpm[i];
            if (!(g >= 1) || !(v > 8) || !(r > 0) || Clutched(i) || d.Gear[i - Hz / 2] != g || !(d.Throttle[i] > 0.2)) continue;
            if (!by.TryGetValue((int)g, out var l)) by[(int)g] = l = new();
            l.Add(r / v);
        }
        var o = new Dictionary<int, double>();
        foreach (var (g, l) in by)
            if (l.Count >= Hz) { l.Sort(); o[g] = l[l.Count / 2]; }
        return o;
    }

    /// <summary>The power at full throttle by rpm, in steps of 1% of the maximum (NaN where too little was seen).</summary>
    private double[]? PowerCurve()
    {
        var bins = new List<double>[101];
        for (int b = 0; b <= 100; b++) bins[b] = new();
        int seen = 0;
        for (int i = 0; i < d.Count; i++)
        {
            double r = d.Rpm[i], max = d.MaxRpm[i], p = d.Power[i];
            if (!(r > 0) || !(max > 0) || double.IsNaN(p) || !(d.Throttle[i] >= 0.95) || !(d.Gear[i] >= 1) || Clutched(i)) continue;
            int b = (int)Math.Round(r / max * 100);
            if (b < 0 || b > 100) continue;
            bins[b].Add(p);
            seen++;
        }
        if (seen < 3 * Hz) return null;
        var curve = new double[101];
        int known = 0;
        for (int b = 0; b <= 100; b++)
        {
            var l = bins[b];
            if (l.Count < 5) { curve[b] = double.NaN; continue; }
            l.Sort();
            curve[b] = l[(int)(l.Count * 0.9)]; // near the top: the engine's power, not a moment of it
            known++;
        }
        return known >= 10 ? curve : null;
    }

    private static double At(double[] curve, double pct)
    {
        if (pct < 0 || pct > 100) return double.NaN;
        int a = (int)Math.Floor(pct), b = Math.Min(100, a + 1);
        double pa = curve[a], pb = curve[b];
        if (double.IsNaN(pa)) return pb;
        if (double.IsNaN(pb)) return pa;
        return pa + (pb - pa) * (pct - a);
    }

    /// <summary>The best rpm (share of the maximum) to upshift at, the next gear's rpm being <paramref name="drop"/>
    /// times this one's: the first point where the next gear's power is at least this one's; none: short of the limiter.</summary>
    private static double BestUpshift(double[] curve, double drop)
    {
        for (double pct = 50; pct < LimiterPct; pct += 0.5)
        {
            double now = At(curve, pct), next = At(curve, pct * drop);
            if (double.IsNaN(now) || double.IsNaN(next)) continue;
            if (next >= now) return pct;
        }
        return LimiterPct - 1;
    }

    private double MaxPct(int a, int b)
    {
        double m = double.NaN;
        for (int k = Math.Max(0, a); k <= Math.Min(d.Count - 1, b); k++)
        {
            double r = d.Rpm[k], max = d.MaxRpm[k];
            if (r > 0 && max > 0 && !(r / max * 100 <= m)) m = r / max * 100;
        }
        return m;
    }

    private double MaxRpmNear(int a, int b)
    {
        double m = double.NaN;
        for (int k = Math.Max(0, a); k <= Math.Min(d.Count - 1, b); k++) if (d.Rpm[k] > 0 && !(d.Rpm[k] <= m)) m = d.Rpm[k];
        return m;
    }

    private double MeanThrottle(int a, int b)
    {
        double sum = 0;
        int n = 0;
        for (int k = Math.Max(0, a); k <= Math.Min(d.Count - 1, b); k++) if (!double.IsNaN(d.Throttle[k])) { sum += d.Throttle[k]; n++; }
        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>The gear changes between forward gears (through neutral too, within a second), above walking pace.</summary>
    private void FindShifts()
    {
        int last = -1, lastAt = -1;
        for (int i = 0; i < d.Count; i++)
        {
            double gd = d.Gear[i];
            if (!(gd >= 1)) continue;
            int g = (int)gd;
            if (last >= 1 && g != last && i - lastAt <= Hz && d.Speed[i] > 3)
            {
                bool up = g > last;
                Shift s;
                if (up)
                {
                    double pct = MaxPct(lastAt - Hz / 4, lastAt);
                    double best = Best.TryGetValue(last, out double b) ? b : double.NaN;
                    // the throttle before the lift for the change: a shift cruising isn't judged
                    bool full = MeanThrottle(lastAt - Hz / 2, lastAt - Hz / 10) >= 0.9;
                    // held on the limiter (0.15 s of the half second before), not just reaching it as it shifts
                    int held = 0;
                    for (int k = Math.Max(0, lastAt - Hz / 2); k <= lastAt; k++)
                        if (d.Rpm[k] > 0 && d.MaxRpm[k] > 0 && d.Rpm[k] / d.MaxRpm[k] * 100 >= LimiterPct) held++;
                    var kind = !full ? ShiftKind.PartThrottle
                        : held >= Hz * 15 / 100 ? ShiftKind.Limiter
                        : !double.IsNaN(best) ? (pct < best - EarlyBy ? ShiftKind.Early : ShiftKind.Good)
                        : pct < 70 ? ShiftKind.Early : ShiftKind.Good;
                    s = new Shift { At = lastAt, From = last, To = g, Pct = pct, Rpm = MaxRpmNear(lastAt - Hz / 4, lastAt), BestPct = best, Kind = kind };
                }
                else
                {
                    double pct = MaxPct(i, i + Hz * 6 / 10);
                    s = new Shift { At = lastAt, From = last, To = g, Pct = pct, Rpm = MaxRpmNear(i, i + Hz * 6 / 10), Kind = pct >= 99 ? ShiftKind.OverRev : ShiftKind.Down };
                }
                if (!double.IsNaN(s.Pct)) Shifts.Add(s);
            }
            last = g;
            lastAt = i;
        }
    }

    /// <summary>The time on the limiter with the throttle down (over a tenth of a second each time: a touch on the way to
    /// a shift is fine).</summary>
    private void FindLimiter()
    {
        int start = -1;
        for (int i = 0; i <= d.Count; i++)
        {
            bool on = i < d.Count && d.Rpm[i] > 0 && d.MaxRpm[i] > 0 && d.Rpm[i] / d.MaxRpm[i] * 100 >= LimiterPct
                && d.Throttle[i] >= 0.8 && d.Gear[i] >= 1 && !Clutched(i);
            if (on && start < 0) start = i;
            else if (!on && start >= 0)
            {
                double s = (i - start) * SessionData.Dt;
                if (s > 0.1)
                {
                    OnLimiter.Add((start, i - 1));
                    LimiterS += s - 0.1;
                    if (d.Gear[start] == TopGear) TopGearLimiterS += s - 0.1;
                }
                start = -1;
            }
        }
    }

    /// <summary>The tips (text, the session's seconds, bad, good), the pedals' rules of thumb's kind: a second on the
    /// limiter ≈ 0.4 s, an upshift early ≈ 0.01 s a point under the best (at most 0.15 s), an over-rev ≈ 0.05 s.</summary>
    public IEnumerable<(string Text, double Seconds, bool Bad, bool Good)> Tips()
    {
        if (!HasData) yield break;
        var ups = Shifts.Where(s => s.Up && s.Kind != ShiftKind.PartThrottle).ToList();
        double belowTop = LimiterS - TopGearLimiterS;
        if (belowTop >= 0.5)
        {
            int times = OnLimiter.Count(l => d.Gear[l.From] != TopGear);
            yield return (F("You sat on the rev limiter for {0:0.0} s ({1} times) before shifting up. Shift as the shift light comes on: on the limiter the car stops pulling.", belowTop, times), belowTop * 0.4, belowTop >= 3, false);
        }
        if (TopGearLimiterS >= 1)
            yield return (F("On the limiter in gear {0}, the highest used, for {1:0.0} s: if there's no gear above it, lengthen the final drive for this track.", TopGear, TopGearLimiterS), TopGearLimiterS * 0.4, false, false);
        var early = ups.Where(s => s.Kind == ShiftKind.Early).ToList();
        if (early.Count >= 2 && early.Count * 5 >= ups.Count)
        {
            double cost = early.Sum(s => Math.Min(0.15, ((double.IsNaN(s.BestPct) ? 85 : s.BestPct) - s.Pct) * 0.01));
            // (since 4.7.0 in rpm, a share of the maximum before)
            string text = Best.Count > 0 && MaxRpm > 0
                ? F("{0} of {1} full-throttle upshifts came early: at {2:N0} rpm on average, where this car's best is about {3:N0} rpm. Hold each gear longer.", early.Count, ups.Count, early.Average(s => s.Rpm), early.Where(s => !double.IsNaN(s.BestPct)).Select(s => s.BestPct).DefaultIfEmpty(85).Average() * MaxRpm / 100)
                : F("{0} of {1} full-throttle upshifts came early. Hold each gear longer: engines make their power high up.", early.Count, ups.Count);
            yield return (text, cost, false, false);
        }
        var over = Shifts.Count(s => s.Kind == ShiftKind.OverRev);
        if (over >= 2)
            yield return (F("{0} downshifts sent the revs to the limiter. Wait for the speed to come down before each one: it upsets the car and can lock the rear wheels.", over), over * 0.05, false, false);
        int good = ups.Count(s => s.Kind == ShiftKind.Good);
        if (ups.Count >= 5 && good * 10 >= ups.Count * 8 && belowTop < 0.5)
            yield return (F("{0} of {1} full-throttle upshifts at the right rpm: nice.", good, ups.Count), double.NaN, false, true);
    }
}
