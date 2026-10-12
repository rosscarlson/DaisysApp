namespace DaisysApp.Applets.AudioTools.Tests.Audio;

/// <summary>
/// Checks that a mic recording is something a real microphone could have heard. Noise suppression, noise gates and
/// echo cancellation (in Windows or the mic's driver) give themselves away: a quiet room comes back as pure digital
/// silence, a steady test noise comes back jumping by tens of dB from moment to moment, or it fades away within a
/// few seconds as the suppression learns it. Measurements made through
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
        var b = Blocks(rec, rate, skipSeconds);
        return b.Count < 2 ? 0 : b.Max() - b.Min();
    }

    /// <summary>How many times a speaker is measured before a jumping level stops the run.</summary>
    public const int Tries = 3;

    /// <summary>
    /// How far a steady test noise may fade between the start and the end of a measurement. Noise suppression (in the
    /// mic, its driver or an app) learns a steady sound within a second or two and then turns it down, by 10 dB or
    /// more: the level starts right and sinks. Band-limited noise itself wanders by a dB or so.
    /// </summary>
    public const double MaxFadeDb = 4;

    /// <summary>
    /// The recording filtered to the band the wizard reads (the same filters as the meter): 400 Hz–2.5 kHz for a main
    /// speaker, 20 Hz to <paramref name="lfeHz"/> for the subwoofer. Then a sound outside it (rumble under the mains band,
    /// hiss or a fan above the sub's) doesn't count as the speaker's level jumping.
    /// </summary>
    public static float[] InBand(float[] rec, double rate, MicBand band, double lfeHz = 120)
    {
        const double q = 0.7071;
        Biquad[] chain = band switch
        {
            MicBand.Mains => [Biquad.HighPass(rate, 400, q), Biquad.HighPass(rate, 400, q), Biquad.LowPass(rate, 2500, q), Biquad.LowPass(rate, 2500, q)],
            MicBand.Lfe => [Biquad.HighPass(rate, 20, q), Biquad.LowPass(rate, lfeHz, q), Biquad.LowPass(rate, lfeHz, q)],
            _ => [Biquad.HighPass(rate, 20, q)],
        };
        var y = new float[rec.Length];
        for (int i = 0; i < rec.Length; i++)
        {
            double v = rec[i];
            foreach (var f in chain) v = f.Process(v);
            y[i] = (float)v;
        }
        return y;
    }

    /// <summary>The block levels (dBFS) of a recording, half a second each unless said, skipping the first <paramref name="skipSeconds"/>.</summary>
    public static List<double> Blocks(float[] rec, double rate, double skipSeconds = 0, double blockSeconds = 0.5)
    {
        int block = (int)(rate * blockSeconds), start = (int)(rate * skipSeconds);
        var levels = new List<double>();
        for (int i = start; i + block <= rec.Length; i += block) levels.Add(RmsDb(rec.AsSpan(i, block)));
        return levels;
    }

    /// <summary>
    /// How much quieter the end of a recording is than its start (the first two half-second blocks against the last
    /// two), in dB; 0 if it's too short to tell (under four blocks) or it got louder.
    /// </summary>
    public static double Fade(float[] rec, double rate, double skipSeconds = 0, double blockSeconds = 0.5)
    {
        var b = Blocks(rec, rate, skipSeconds, blockSeconds);
        if (b.Count < 4) return 0;
        return Math.Max(0, (b[0] + b[1]) / 2 - (b[^1] + b[^2]) / 2);
    }

    /// <summary>
    /// The checks for a recording of one speaker's test noise, in the band the wizard reads. A main speaker's band noise
    /// is steady to within a dB from one half second to the next (in the voice band). The subwoofer's isn't, nor is
    /// full-range noise in a room, whose bass dominates the level: its noise is mostly a few octaves
    /// at the very bottom, which a room's resonances narrow further, so even a perfectly steady sub swings by 5–9 dB
    /// between half-second slices, more the longer it's measured (what an SPL meter's one-second averaging hides). So
    /// the sub is judged on one-second slices, leaving out the loudest and the softest when there are four or more.
    /// </summary>
    public static (double Swing, double Fade, bool Ok, string Blocks) Check(float[] inBand, double rate, MicBand band)
    {
        // the sub, and full range (which a room's bass resonances dominate), on one-second slices, trimmed; the voice
        // band on half seconds
        bool sub = band != MicBand.Mains;
        double seconds = sub ? 1.0 : 0.5;
        var b = Blocks(inBand, rate, 0, seconds);
        var sorted = b.OrderBy(v => v).ToList();
        double swing = sorted.Count < 2 ? 0 : sub && sorted.Count >= 4 ? sorted[^2] - sorted[1] : sorted[^1] - sorted[0];
        double fade = Fade(inBand, rate, 0, seconds);
        bool ok = swing <= MaxSwingDb && fade <= (sub ? MaxSubFadeDb : MaxFadeDb);
        string text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0}-second levels in the {1} band (dBFS): {2}: swing {3:0.0} dB{4}, fade {5:0.0} dB",
            seconds, band, string.Join(" ", b.Select(v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))),
            swing, sub && sorted.Count >= 4 ? " (loudest and softest left out)" : "", fade);
        return (swing, fade, ok, text);
    }

    /// <summary>
    /// How much louder a steady test noise's first half second is than the rest (the median of the half seconds from
    /// 1.5 s on), when the rest is steady; 0 otherwise. Automatic gain control gives itself away like this: after a
    /// quiet moment its gain is at the top, and within a second of sound it's pulled down by 10 dB or more, and stays.
    /// </summary>
    public static double Attack(float[] inBand, double rate, double skipSeconds = 0)
    {
        var b = Blocks(inBand, rate, skipSeconds);
        if (b.Count < 5) return 0;
        var rest = b.Skip(3).OrderBy(v => v).ToList();
        if (rest[^1] - rest[0] > 6) return 0; // not steady afterwards: not AGC's shape
        double median = rest[rest.Count / 2];
        return Math.Max(0, b[0] - median);
    }

    /// <summary>The first half second this much louder than the steady rest is taken for automatic gain control.</summary>
    public const double MaxAttackDb = 8;

    public static string AgcMessage(string speaker, double dropDb, bool adapterAgcOff) => adapterAgcOff
        ? F("When {0} started, the mic heard it {1:0} dB louder for the first moment, then the level dropped and stayed down: something is turning the sound down automatically. Daisy's App switched off the mic adapter's automatic gain control, so it's probably on the speakers' side: an AV receiver's Dynamic Volume, night mode or Audyssey Dynamic Volume, or a compressor in Voicemeeter. Turn it off while leveling and try again.", speaker, dropDb)
        : F("When {0} started, the mic heard it {1:0} dB louder for the first moment, then the level dropped and stayed down: that's automatic gain control (AGC), most likely in the mic's USB adapter, which Windows doesn't let Daisy's App switch off. With AGC the room's background noise reads far too loud, the speakers seem barely above it, and their differences are squeezed, so the levels come out wrong. Look for an AGC switch in Sound settings → Recording → the mic → Properties (the Levels or Advanced tab) or the adapter's own app; otherwise use an adapter or audio interface without AGC (a measurement mic's USB version has none).", speaker, dropDb);

    /// <summary>The fade allowed on the subwoofer and in full range, whose one-second levels wander more than the voice band's.</summary>
    public const double MaxSubFadeDb = 6;

    public static string FadeMessage(string speaker, double fadeDb) =>
        F("While {0} played a steady test noise, the microphone's level faded by {1:0} dB within a few seconds, each of the 3 times it was measured, though the sound in the room doesn't change. That's noise suppression: the mic's own processing, its driver, or an app between it and Windows (NVIDIA Broadcast, Krisp, SteelSeries Sonar, Logitech G HUB's Blue VO!CE, Windows Studio Effects' Voice Focus…) learns a steady sound and turns it down, so the readings aren't what the room hears. Turn it off for this mic, or use one without it (a measurement mic is best), and try again. A shorter measuring time only hides it.", speaker, fadeDb);

    /// <summary>The half-second block levels of a recording, for the log.</summary>
    public static string DescribeBlocks(float[] rec, double rate, double skipSeconds = 0)
    {
        var levels = Blocks(rec, rate, skipSeconds).Select(v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        return "  half-second levels (dBFS): " + string.Join(" ", levels);
    }

    public static string SilentMessage =>
        T("The microphone sends pure digital silence when the room is quiet, which a real mic never does: Windows or the mic's driver is processing its signal (noise suppression or a noise gate), so it can't be measured with. Turn off the mic's Audio enhancements (Settings → System → Sound → the mic) and any noise suppression in its own app, then try again.");

    public static string SwingMessage(string speaker, double swingDb) =>
        F("The microphone's level jumped by {0:0} dB while {1} played a steady test noise, each of the 3 times it was measured. Either something noisy happened in the room, or Windows or the mic's driver is processing its signal (noise suppression or echo cancellation). Keep the room quiet, turn off the mic's Audio enhancements (Settings → System → Sound → the mic), and try again.", swingDb, speaker);
}
