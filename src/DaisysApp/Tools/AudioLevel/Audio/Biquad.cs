namespace DaisysApp.Tools.AudioLevel.Audio;

/// <summary>RBJ cookbook biquad, transposed direct form II. Coefficients can be retuned in place without resetting state.</summary>
internal sealed class Biquad
{
    private double b0, b1, b2, a1, a2;
    private double z1, z2;

    private Biquad() { b0 = 1; }

    public static Biquad LowPass(double fs, double f, double q)
    {
        var b = new Biquad();
        b.SetLowPass(fs, f, q);
        return b;
    }

    public static Biquad HighPass(double fs, double f, double q)
    {
        var b = new Biquad();
        b.SetHighPass(fs, f, q);
        return b;
    }

    public void SetLowPass(double fs, double f, double q)
    {
        double w0 = 2 * Math.PI * f / fs, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetHighPass(double fs, double f, double q)
    {
        double w0 = 2 * Math.PI * f / fs, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    private void Set(double nb0, double nb1, double nb2, double a0, double na1, double na2)
    {
        b0 = nb0 / a0; b1 = nb1 / a0; b2 = nb2 / a0;
        a1 = na1 / a0; a2 = na2 / a0;
    }

    public double Process(double x)
    {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
}
