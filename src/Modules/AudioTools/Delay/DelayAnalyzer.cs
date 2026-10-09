using System.Numerics;

namespace DaisysApp.Applets.AudioTools.Delay;

/// <summary>When one beep arrived at the mic, relative to when it was played, and how clearly it was heard.</summary>
public sealed record Arrival(double Seconds, double Match, double SnrDb, bool Clipped);

/// <summary>
/// Finds each chirp in a mic recording by normalized cross-correlation with the chirp itself. The earliest strong match
/// is taken, so the direct sound wins over later room reflections.
/// </summary>
public static class DelayAnalyzer
{
    /// <param name="recording">Mic samples, starting just before the playback stream started.</param>
    /// <param name="burstTimes">When each chirp was played, in seconds from the start of the playback stream.</param>
    /// <param name="windowSeconds">How long after each chirp to look for it (must be less than the spacing).</param>
    /// <returns>One result per chirp; null where nothing chirp-like was found.</returns>
    public static Arrival?[] FindArrivals(float[] recording, int sampleRate, IReadOnlyList<double> burstTimes, double windowSeconds)
    {
        var template = Chirp.Make(sampleRate);
        int n = template.Length;
        double templateNorm = Math.Sqrt(template.Sum(v => (double)v * v));
        var result = new Arrival?[burstTimes.Count];

        for (int b = 0; b < burstTimes.Count; b++)
        {
            int from = (int)(burstTimes[b] * sampleRate);
            int to = Math.Min(recording.Length - n, from + (int)(windowSeconds * sampleRate));
            if (from < 0 || to <= from) continue;

            int count = to - from;
            var ncc = new float[count];
            var energy = new double[count];

            // running energy of the recording segment under the template
            double e = 0;
            for (int i = from; i < from + n; i++) e += recording[i] * recording[i];
            for (int k = 0; k < count; k++)
            {
                int i = from + k;
                if (k > 0)
                {
                    e += recording[i + n - 1] * recording[i + n - 1] - recording[i - 1] * recording[i - 1];
                    if (e < 0) e = 0;
                }
                energy[k] = e;
                double dot = Dot(recording.AsSpan(i, n), template);
                ncc[k] = e > 1e-12 ? (float)Math.Abs(dot / (Math.Sqrt(e) * templateNorm)) : 0;
            }

            int best = 0;
            for (int k = 1; k < count; k++) if (ncc[k] > ncc[best]) best = k;
            if (ncc[best] < 0.2) continue; // nothing that looks like the chirp

            // prefer the earliest peak that is nearly as strong (direct sound before reflections), within 30 ms
            int earliest = best;
            int back = Math.Max(0, best - (int)(0.030 * sampleRate));
            for (int k = back; k < best; k++)
            {
                if (ncc[k] >= 0.8 * ncc[best] && IsLocalPeak(ncc, k)) { earliest = k; break; }
            }

            // signal-to-noise: energy at the chirp vs the median energy across the window
            var sorted = energy.ToArray();
            Array.Sort(sorted);
            double median = Math.Max(sorted[sorted.Length / 2], 1e-12);
            double snr = 10 * Math.Log10(Math.Max(energy[earliest], 1e-12) / median);

            bool clipped = false;
            for (int i = from + earliest; i < from + earliest + n; i++)
                if (Math.Abs(recording[i]) > 0.98f) { clipped = true; break; }

            result[b] = new Arrival((double)earliest / sampleRate, ncc[earliest], snr, clipped);
        }
        return result;
    }

    private static bool IsLocalPeak(float[] v, int k) =>
        (k == 0 || v[k] >= v[k - 1]) && (k == v.Length - 1 || v[k] >= v[k + 1]);

    private static double Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int i = 0;
        var acc = Vector<float>.Zero;
        int w = Vector<float>.Count;
        for (; i <= a.Length - w; i += w)
            acc += new Vector<float>(a.Slice(i, w)) * new Vector<float>(b.Slice(i, w));
        double sum = Vector.Dot(acc, Vector<float>.One);
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}
