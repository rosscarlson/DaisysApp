namespace DaisysApp.Applets.AudioTools.Tests.Audio;

/// <summary>
/// Checks that a mic recording is something a real microphone could have heard. Noise suppression, noise gates and
/// echo cancellation (in Windows or the mic's driver) give themselves away: a quiet room comes back as pure digital
/// silence, and a steady test noise comes back jumping by tens of dB from moment to moment. Measurements made through
/// them are meaningless, so the wizards stop instead of setting levels or an EQ from them.
/// </summary>
public static class MicHealth
{
    /// <summary>Quieter than any real mic's own hiss: below this the signal has been gated or suppressed.</summary>
    public const double SilentDb = -130;

    /// <summary>How far a steady test noise may wander between half-second blocks before the recording is rejected.</summary>
    public const double MaxSwingDb = 10;

    /// <summary>RMS level in dBFS.</summary>
    public static double RmsDb(ReadOnlySpan<float> x)
    {
        if (x.Length == 0) return double.NaN;
        double sum = 0;
        foreach (float v in x) sum += (double)v * v;
        return 10 * Math.Log10(Math.Max(sum / x.Length, 1e-30));
    }

    /// <summary>Whether a recording of the quiet room is digital silence (so the mic's signal is being processed).</summary>
    public static bool IsSilent(float[] floor) => floor.Length > 0 && RmsDb(floor) < SilentDb;

    /// <summary>
    /// The spread (dB, loudest minus softest) of the half-second blocks of a recording of a steady test noise,
    /// skipping the first <paramref name="skipSeconds"/>; 0 if it's too short to tell.
    /// </summary>
    public static double Swing(float[] rec, double rate, double skipSeconds = 0)
    {
        int block = (int)(rate / 2), start = (int)(rate * skipSeconds);
        double lo = double.MaxValue, hi = double.MinValue;
        int n = 0;
        for (int i = start; i + block <= rec.Length; i += block, n++)
        {
            double db = RmsDb(rec.AsSpan(i, block));
            lo = Math.Min(lo, db);
            hi = Math.Max(hi, db);
        }
        return n < 2 ? 0 : hi - lo;
    }

    /// <summary>How many times a speaker is measured before a jumping level stops the run.</summary>
    public const int Tries = 3;

    /// <summary>The half-second block levels of a recording, for the log.</summary>
    public static string DescribeBlocks(float[] rec, double rate, double skipSeconds = 0)
    {
        int block = (int)(rate / 2), start = (int)(rate * skipSeconds);
        var levels = new List<string>();
        for (int i = start; i + block <= rec.Length; i += block)
            levels.Add(RmsDb(rec.AsSpan(i, block)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        return "  half-second levels (dBFS): " + string.Join(" ", levels);
    }

    public static string SilentMessage =>
        T("The microphone sends pure digital silence when the room is quiet, which a real mic never does: Windows or the mic's driver is processing its signal (noise suppression or a noise gate), so it can't be measured with. Turn off the mic's Audio enhancements (Settings → System → Sound → the mic) and any noise suppression in its own app, then try again.");

    public static string SwingMessage(string speaker, double swingDb) =>
        F("The microphone's level jumped by {0:0} dB while {1} played a steady test noise, each of the 3 times it was measured. Either something noisy happened in the room, or Windows or the mic's driver is processing its signal (noise suppression or echo cancellation). Keep the room quiet, turn off the mic's Audio enhancements (Settings → System → Sound → the mic), and try again.", swingDb, speaker);
}
