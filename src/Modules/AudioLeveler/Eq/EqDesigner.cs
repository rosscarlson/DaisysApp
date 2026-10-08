namespace DaisysApp.Applets.AudioLevel.Eq;

/// <summary>What the EQ should aim for.</summary>
public enum EqTarget { Flat, RoomCurve }

/// <summary>The limits of where the EQ is kept (Voicemeeter's cells, Equalizer APO's filters).</summary>
public sealed record EqLimits(int MaxBands, double MinQ, double MaxQ, double MinHz, double MaxHz, double MaxCutDb, double MaxBoostDb);

/// <summary>
/// Fits peaking filters that bring a measured response close to a target. Greedy: each new filter goes on the worst
/// remaining deviation (cuts preferred over boosts, since boosting a room's dips mostly wastes power), then every
/// filter so far is fine-tuned together. Boosts are kept modest and broad; above 1 kHz only gentle, wide filters are
/// used, as a single mic position can't tell much about the treble.
/// </summary>
public static class EqDesigner
{
    /// <summary>The target curve, in dB: flat, or a gentle room curve (a little more bass, slightly less treble).</summary>
    public static double TargetDb(EqTarget target, double f) => target switch
    {
        EqTarget.RoomCurve => 4 / Math.Sqrt(1 + Math.Pow(f / 70, 4)) - (f > 2000 ? 0.6 * Math.Log2(f / 2000) : 0),
        _ => 0,
    };

    /// <summary>
    /// The lowest frequency worth correcting: where the speaker has rolled off 10 dB below its level (for a third of an
    /// octave), searching down from <paramref name="startHz"/>. Boosting below that only strains the speaker.
    /// </summary>
    public static double LowLimit(Response r, double floorHz, double startHz = 200)
    {
        var grid = Response.Grid;
        int run = 0;
        for (int i = Array.FindLastIndex(grid, f => f <= startHz); i >= 0 && grid[i] >= floorHz; i--)
        {
            double v = r.Db[i];
            run = double.IsNaN(v) || v < -10 ? run + 1 : 0;
            if (run >= 8) return Math.Max(floorHz, grid[Math.Min(grid.Length - 1, i + 8)] * 1.12); // a third of an octave of it
        }
        return floorHz;
    }

    /// <summary>
    /// Designs the filters. <paramref name="measured"/> is already normalised (0 dB = the speaker's level); only
    /// points between <paramref name="from"/> and <paramref name="to"/> that aren't NaN count.
    /// </summary>
    public static List<EqBand> Design(Response measured, EqTarget target, double from, double to, EqLimits limits)
    {
        var grid = Response.Grid;
        var idx = Enumerable.Range(0, grid.Length).Where(i => grid[i] >= from && grid[i] <= to && !double.IsNaN(measured.Db[i])).ToArray();
        var bands = new List<EqBand>();
        if (idx.Length < 8 || limits.MaxBands <= 0) return bands;

        // what the EQ should add at each point; deep dips are only partly filled
        var f = idx.Select(i => grid[i]).ToArray();
        var want = idx.Select(i => Math.Clamp(TargetDb(target, grid[i]) - measured.Db[i], -limits.MaxCutDb, MaxBoost(limits, grid[i], from))).ToArray();

        double Eq(IReadOnlyList<EqBand> bs, int j) { double s = 0; foreach (var b in bs) s += b.ResponseDb(f[j]); return s; }

        for (int k = 0; k < limits.MaxBands; k++)
        {
            var resid = new double[f.Length];
            for (int j = 0; j < f.Length; j++) resid[j] = want[j] - Eq(bands, j);

            int best = -1;
            double bestScore = 0;
            for (int j = 0; j < f.Length; j++)
            {
                double score = Math.Abs(resid[j]) * (resid[j] > 0 ? 0.6 : 1);
                if (score > bestScore) { bestScore = score; best = j; }
            }
            if (best < 0 || Math.Abs(resid[best]) < 1) break;

            // width: out to where the deviation falls to half
            double g = resid[best];
            int lo = best, hi = best;
            while (lo > 0 && Math.Sign(resid[lo - 1]) == Math.Sign(g) && Math.Abs(resid[lo - 1]) > Math.Abs(g) / 2) lo--;
            while (hi < f.Length - 1 && Math.Sign(resid[hi + 1]) == Math.Sign(g) && Math.Abs(resid[hi + 1]) > Math.Abs(g) / 2) hi++;
            double bw = Math.Max(Math.Log2(f[hi] / f[lo]), 1.0 / 6);
            double q = Math.Sqrt(Math.Pow(2, bw)) / (Math.Pow(2, bw) - 1);

            bands.Add(Constrain(new EqBand(f[best], g, q), limits, from, to));
            Refine(bands, f, want, limits, from, to);
        }

        bands.RemoveAll(b => Math.Abs(b.GainDb) < 0.5);
        return bands
            .Select(b => new EqBand(Math.Round(b.Hz, b.Hz < 100 ? 1 : 0), Math.Round(b.GainDb, 1), Math.Round(b.Q, 2)))
            .OrderBy(b => b.Hz)
            .ToList();
    }

    /// <summary>No boost at all at the very bottom of the range, where the speaker is already rolling off; at most 3 dB above 500 Hz.</summary>
    private static double MaxBoost(EqLimits limits, double hz, double from) =>
        hz < from * 1.26 ? 0 : hz > 500 ? Math.Min(limits.MaxBoostDb, 3) : limits.MaxBoostDb;

    private static double MaxQ(EqLimits limits, double hz, double gain) =>
        Math.Min(limits.MaxQ, (hz < 300 ? 10 : hz < 1000 ? 4 : 2) * (gain > 0 ? 0.5 : 1));

    private static EqBand Constrain(EqBand b, EqLimits limits, double from, double to)
    {
        double hz = Math.Clamp(b.Hz, Math.Max(limits.MinHz, from * 0.8), Math.Min(limits.MaxHz, to));
        double gain = Math.Clamp(b.GainDb, -limits.MaxCutDb, MaxBoost(limits, hz, from));
        double maxQ = Math.Max(limits.MinQ, MaxQ(limits, hz, gain));
        return new EqBand(hz, gain, Math.Clamp(b.Q, limits.MinQ, maxQ));
    }

    /// <summary>Coordinate descent over every filter's frequency, gain and Q.</summary>
    private static void Refine(List<EqBand> bands, double[] f, double[] want, EqLimits limits, double from, double to)
    {
        var cache = new double[bands.Count][];
        for (int b = 0; b < bands.Count; b++) cache[b] = Curve(bands[b], f);

        double Cost(int skip, double[] replacement)
        {
            double sum = 0;
            for (int j = 0; j < f.Length; j++)
            {
                double eq = 0;
                for (int b = 0; b < bands.Count; b++) eq += b == skip ? replacement[j] : cache[b][j];
                double e = want[j] - eq;
                sum += e * e;
                if (eq > limits.MaxBoostDb + 0.5) sum += 4 * (eq - limits.MaxBoostDb) * (eq - limits.MaxBoostDb); // filters stacking up into a big boost
            }
            return sum;
        }

        double cost = Cost(-1, []);
        double fStep = 1.0 / 12, gStep = 1, qStep = 1.25;
        for (int round = 0; round < 60; round++)
        {
            bool improved = false;
            for (int b = 0; b < bands.Count; b++)
            {
                var cur = bands[b];
                EqBand[] tries =
                [
                    cur with { Hz = cur.Hz * Math.Pow(2, fStep) }, cur with { Hz = cur.Hz / Math.Pow(2, fStep) },
                    cur with { GainDb = cur.GainDb + gStep }, cur with { GainDb = cur.GainDb - gStep },
                    cur with { Q = cur.Q * qStep }, cur with { Q = cur.Q / qStep },
                ];
                foreach (var t in tries)
                {
                    var c = Constrain(t, limits, from, to);
                    if (c == cur) continue;
                    var curve = Curve(c, f);
                    double nc = Cost(b, curve);
                    if (nc < cost - 1e-9)
                    {
                        cost = nc;
                        bands[b] = cur = c;
                        cache[b] = curve;
                        improved = true;
                    }
                }
            }
            if (!improved)
            {
                fStep /= 2; gStep /= 2; qStep = Math.Sqrt(qStep);
                if (gStep < 0.05) break;
            }
        }
    }

    private static double[] Curve(EqBand b, double[] f) => f.Select(b.ResponseDb).ToArray();
}
