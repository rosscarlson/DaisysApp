namespace DaisysApp.Applets.AudioLevel.Eq;

/// <summary>
/// A frequency response on a fixed log-spaced grid (1/24 octave, 20 Hz–20 kHz), in dB. NaN where there's nothing to
/// say (e.g. below the room's background noise).
/// </summary>
public sealed class Response
{
    public static readonly double[] Grid = BuildGrid();

    public double[] Db { get; }

    public Response(double[] db) => Db = db;

    private static double[] BuildGrid()
    {
        var list = new List<double>();
        for (double f = 20; f <= 20000 * 1.0001; f *= Math.Pow(2, 1.0 / 24)) list.Add(f);
        return list.ToArray();
    }

    /// <summary>Mean of the points between the two frequencies (ignoring NaN), or NaN.</summary>
    public double Mean(double from, double to)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < Grid.Length; i++)
            if (Grid[i] >= from && Grid[i] <= to && !double.IsNaN(Db[i])) { sum += Db[i]; n++; }
        return n == 0 ? double.NaN : sum / n;
    }

    public Response Shift(double db) => new(Db.Select(v => v + db).ToArray());

    /// <summary>
    /// A speaker's response from a recording of it playing <paramref name="source"/> (the signal's own spectrum, on the
    /// grid): what the mic heard minus what went in, less the mic's own calibration. Points that weren't at least
    /// <paramref name="minSnrDb"/> above the room's background noise (<paramref name="floor"/>, recorded at the same
    /// mic level) are NaN.
    /// </summary>
    public static Response Measure(float[] recording, float[] floor, double micRate, double[] source, MicCalibration? calibration, double minSnrDb = 10)
    {
        var heard = Spectrum.ToGrid(Spectrum.Power(recording), micRate);
        var noise = floor.Length >= Spectrum.FftSize ? Spectrum.ToGrid(Spectrum.Power(floor), micRate) : null;
        var db = new double[Grid.Length];
        for (int i = 0; i < Grid.Length; i++)
        {
            bool clear = noise == null || heard[i] - noise[i] >= minSnrDb;
            db[i] = clear && !double.IsNaN(source[i]) ? heard[i] - source[i] - (calibration?.At(Grid[i]) ?? 0) : double.NaN;
        }
        return new Response(db);
    }

    /// <summary>The response with an EQ added (as it'll be measured once the EQ is in place).</summary>
    public Response With(IReadOnlyList<EqBand> bands) => new(Db.Select((v, i) => v + EqBand.ResponseDb(bands, Grid[i])).ToArray());

    /// <summary>How far the response strays from <paramref name="target"/> between the two frequencies: RMS of the difference.</summary>
    public double Deviation(Func<double, double> target, double from, double to)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < Grid.Length; i++)
        {
            if (Grid[i] < from || Grid[i] > to || double.IsNaN(Db[i])) continue;
            double d = Db[i] - target(Grid[i]);
            sum += d * d;
            n++;
        }
        return n == 0 ? double.NaN : Math.Sqrt(sum / n);
    }
}

/// <summary>Power spectra (Welch's method) and turning them into a smoothed response on the log grid.</summary>
public static class Spectrum
{
    public const int FftSize = 32768;

    /// <summary>Averaged power spectrum of <paramref name="x"/>: bins 0..N/2, Hann window, 50 % overlap.</summary>
    public static double[] Power(ReadOnlySpan<float> x, int n = FftSize)
    {
        var window = new double[n];
        for (int i = 0; i < n; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
        var sum = new double[n / 2 + 1];
        var re = new double[n];
        var im = new double[n];
        int segments = 0;
        for (int start = 0; start + n <= x.Length; start += n / 2)
        {
            for (int i = 0; i < n; i++) { re[i] = x[start + i] * window[i]; im[i] = 0; }
            Fft(re, im);
            for (int k = 0; k <= n / 2; k++) sum[k] += re[k] * re[k] + im[k] * im[k];
            segments++;
        }
        if (segments > 0)
            for (int k = 0; k < sum.Length; k++) sum[k] /= segments;
        return sum;
    }

    /// <summary>
    /// Power spectrum → dB on the log grid, smoothed over 1/<paramref name="fraction"/> of an octave (the power in the
    /// bins within half that width either side of each point, averaged).
    /// </summary>
    public static double[] ToGrid(double[] power, double sampleRate, int fraction = 6)
    {
        int n = (power.Length - 1) * 2;
        double binHz = sampleRate / n;
        double half = Math.Pow(2, 0.5 / fraction);
        var grid = Response.Grid;
        var db = new double[grid.Length];
        for (int i = 0; i < grid.Length; i++)
        {
            double lo = grid[i] / half, hi = grid[i] * half;
            int k0 = Math.Max(1, (int)Math.Ceiling(lo / binHz)), k1 = Math.Min(power.Length - 1, (int)Math.Floor(hi / binHz));
            double p;
            if (k1 >= k0)
            {
                p = 0;
                for (int k = k0; k <= k1; k++) p += power[k];
                p /= k1 - k0 + 1;
            }
            else
            {
                // narrower than a bin (low frequencies): interpolate the two nearest bins
                double kf = grid[i] / binHz;
                int k = Math.Clamp((int)kf, 1, power.Length - 2);
                double t = Math.Clamp(kf - k, 0, 1);
                p = power[k] * (1 - t) + power[k + 1] * t;
            }
            db[i] = grid[i] >= sampleRate / 2 * 0.95 ? double.NaN : 10 * Math.Log10(Math.Max(p, 1e-30));
        }
        return db;
    }

    /// <summary>In-place radix-2 FFT (n must be a power of two).</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double nr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = nr;
                }
            }
        }
    }
}
