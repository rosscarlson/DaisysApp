namespace DaisysApp.Applets.AudioTools.Tests.Eq;

/// <summary>
/// What the mic hears right now, as a real-time analyser shows it: the last third of a second's spectrum on the log
/// grid, tilted so pink noise reads flat, smoothed over time, and placed so the loudest recent sound sits around 0 dB
/// (a quiet room then shows well below). The placing never goes below <see cref="FloorDb"/> (about −50 dBFS of noise),
/// so a quiet room isn't turned up until its background hiss (or the mic input's own noise, often a hump near the top
/// of the range) fills the graph. Just something to watch; nothing is measured from it.
/// </summary>
public sealed class LiveSpectrum
{
    /// <summary>How many of the mic's latest samples each update reads.</summary>
    public const int Samples = 16384;

    private const int FftSize = 8192;

    /// <summary>
    /// The lowest the 0 dB line goes: where −50 dBFS of broadband noise reads (a Hann-windowed bin of white noise at
    /// RMS a has power a² · 3/8 · N). Room noise through a mic at a normal level is well below it; the test signal is above.
    /// </summary>
    private static readonly double FloorDb = 10 * Math.Log10(Math.Pow(10, -50 / 10.0) * 0.375 * FftSize);
    private double[]? smoothed;
    private double reference = double.NaN;

    public void Reset()
    {
        smoothed = null;
        reference = double.NaN;
    }

    /// <summary>The next frame, in dB on <see cref="Response.Grid"/>; null until there's enough sound to read.</summary>
    public double[]? Update(float[] samples, double rate)
    {
        if (samples.Length < FftSize) return null;
        var db = Spectrum.ToGrid(Spectrum.Power(samples, FftSize), rate);
        var grid = Response.Grid;
        for (int i = 0; i < grid.Length; i++)
            if (!double.IsNaN(db[i])) db[i] += 10 * Math.Log10(grid[i] / 1000); // +3 dB an octave: pink noise reads flat

        if (smoothed == null) smoothed = db;
        else
            for (int i = 0; i < db.Length; i++)
                smoothed[i] = double.IsNaN(smoothed[i]) || double.IsNaN(db[i]) ? db[i] : smoothed[i] * 0.6 + db[i] * 0.4;

        double sum = 0;
        int n = 0;
        for (int i = 0; i < grid.Length; i++)
            if (grid[i] >= 100 && grid[i] <= 5000 && !double.IsNaN(smoothed[i])) { sum += smoothed[i]; n++; }
        if (n == 0) return null;
        double mean = sum / n;
        // follows a louder sound at once, a quieter one slowly (0.5 dB a second), so silence shows as silence
        reference = double.IsNaN(reference) || mean > reference ? mean : reference - 0.05;
        reference = Math.Max(reference, FloorDb); // (quiet: not turned up past this)
        return smoothed.Select(v => v - reference).ToArray();
    }
}
