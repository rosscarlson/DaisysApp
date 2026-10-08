namespace DaisysApp.Applets.AudioLevel.Eq;

/// <summary>One peaking (bell) filter: centre frequency, gain and Q.</summary>
public sealed record EqBand(double Hz, double GainDb, double Q)
{
    /// <summary>The sample rate the response is worked out at; the filters run at the device's rate, which is close enough.</summary>
    private const double Fs = 48000;

    /// <summary>The filter's gain in dB at <paramref name="f"/> (RBJ cookbook peaking EQ).</summary>
    public double ResponseDb(double f)
    {
        if (Math.Abs(GainDb) < 1e-6 || f <= 0 || f >= Fs / 2) return 0;
        double a = Math.Pow(10, GainDb / 40);
        double w0 = 2 * Math.PI * Math.Min(Hz, Fs * 0.49) / Fs, alpha = Math.Sin(w0) / (2 * Q), cos = Math.Cos(w0);
        double b0 = 1 + alpha * a, b1 = -2 * cos, b2 = 1 - alpha * a;
        double a0 = 1 + alpha / a, a1 = -2 * cos, a2 = 1 - alpha / a;

        double w = 2 * Math.PI * f / Fs;
        double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2);
        double dr = a0 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }

    public static double ResponseDb(IEnumerable<EqBand> bands, double f) => bands.Sum(b => b.ResponseDb(f));

    public override string ToString() =>
        $"{FormatHz(Hz)} {GainDb.ToString("+0.0;−0.0", System.Globalization.CultureInfo.CurrentCulture)} dB Q {Q:0.0#}";

    public static string FormatHz(double hz) => hz >= 1000 ? $"{hz / 1000:0.##} kHz" : $"{hz:0} Hz";
}
