using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisysApp.Applets.AudioTools.Tests.Eq;

/// <summary>
/// A record of one EQ Wizard run, for working out what went wrong when the EQ sounds wrong: the settings, the mic,
/// where the EQ is kept (before, while checking, after), every recording (as WAV), and per speaker what was heard,
/// the response worked out from it, the filters, and the check. Kept in Logs\EqWizard in the settings folder (the
/// last <see cref="Keep"/> runs); <see cref="Export"/> zips them with the EQ settings.
/// </summary>
public sealed class EqRunLog
{
    private const int Keep = 5;
    private static readonly double[] TableHz = [20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Root => Path.Combine(AppPaths.SettingsFolder, "Logs", "EqWizard");

    private readonly string folder;
    private readonly StringBuilder text = new();
    private readonly DateTime started = DateTime.Now;
    private readonly List<object> speakers = [];
    private double[]? floorGrid;
    private int recordings;

    public EqRunLog()
    {
        folder = Path.Combine(Root, started.ToString("yyyyMMdd-HHmmss", Inv));
        Directory.CreateDirectory(folder);
        Prune();
        string version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
        Line($"Daisy's App {version}, {Environment.OSVersion}, {(Environment.Is64BitProcess ? "64" : "32")}-bit, culture {CultureInfo.CurrentCulture.Name}");
    }

    public string Folder => folder;

    /// <summary>A line in log.txt, with the time since the run started.</summary>
    public void Line(string line)
    {
        lock (text) text.Append($"[{(DateTime.Now - started).TotalSeconds,7:0.00}s] ").AppendLine(line);
    }

    /// <summary>A block of text in log.txt (and its own file).</summary>
    public void Block(string title, string body)
    {
        Line($"----- {title} -----");
        lock (text) text.AppendLine(body.TrimEnd()).AppendLine();
        TryWrite(Safe(title) + ".txt", body);
    }

    public void Options(EqOptions o, EqLimits limits, double lfeCutoff)
    {
        Line($"options: target {o.Target}, correct up to {o.UpToHz} Hz, most boost {o.MaxBoostDb} dB; LFE cutoff {lfeCutoff} Hz");
        Line($"limits: {limits}");
        if (o.Calibration is { } cal)
        {
            Line($"calibration: {cal.Name}, {cal.Points} points");
            TryWrite("calibration-as-read.txt", string.Join(Environment.NewLine, cal.All.Select(p => string.Format(Inv, "{0:0.###}\t{1:0.###}", p.Hz, p.Db))));
        }
        else Line("calibration: none");
    }

    public void Sources(double[] mains, double[] lfe)
    {
        TryWrite("source-spectra.json", JsonSerializer.Serialize(new { grid = Response.Grid, mains, lfe }, JsonOptions));
    }

    /// <summary>The room's background noise recording.</summary>
    public void Floor(float[] rec, double micRate)
    {
        WriteWav($"{++recordings:00}-background-noise.wav", rec, micRate);
        floorGrid = Spectrum.ToGrid(Spectrum.Power(rec), micRate);
        Line($"background noise: {rec.Length / micRate:0.00} s, peak {Peak(rec):0.0000}, RMS {Rms(rec):0.0} dBFS");
    }

    /// <summary>One measurement of a speaker: the recording, the source it played, and the levelled response.</summary>
    public void Measured(string speaker, int channel, bool lfe, bool check, float[] rec, double micRate, double[] source,
                         MicCalibration? cal, Response levelled, double levelDb, EqTarget target, double from, double to, IReadOnlyList<EqBand>? bands)
    {
        string pass = check ? "check" : "before";
        WriteWav($"{++recordings:00}-{Safe(speaker)}-{pass}.wav", rec, micRate);
        var heard = Spectrum.ToGrid(Spectrum.Power(rec), micRate);
        var grid = Response.Grid;
        Line($"{speaker} (channel {channel}{(lfe ? ", LFE" : "")}) {pass}: {rec.Length / micRate:0.00} s, peak {Peak(rec):0.0000}, RMS {Rms(rec):0.0} dBFS, levelled by {-levelDb:+0.0;-0.0} dB, range {from:0}–{to:0} Hz");
        if (bands != null) Line($"  filters: {string.Join("  ·  ", bands.Select(b => string.Format(Inv, "{0:0.#} Hz {1:+0.0;-0.0} dB Q {2:0.00}", b.Hz, b.GainDb, b.Q)))}");

        var sb = new StringBuilder();
        sb.AppendLine("     Hz   heard   noise  source     cal  levelled  target      eq");
        foreach (double hz in TableHz)
        {
            int i = Array.FindIndex(grid, f => f >= hz / Math.Pow(2, 1.0 / 48));
            if (i < 0) continue;
            sb.AppendLine(string.Format(Inv, "{0,7:0.#} {1,7:0.0} {2,7:0.0} {3,7:0.0} {4,7:0.0} {5,9:0.0} {6,7:0.0} {7,7:0.0}",
                hz, heard[i], floorGrid?[i] ?? double.NaN, source[i], cal?.At(grid[i]) ?? 0, levelled.Db[i],
                EqDesigner.TargetDb(target, grid[i]), bands == null ? double.NaN : EqBand.ResponseDb(bands, grid[i])));
        }
        lock (text) text.Append(sb);

        lock (speakers)
            speakers.Add(new { speaker, channel, lfe, pass, levelDb, from, to, bands, heard, levelled = levelled.Db });
    }

    /// <summary>After the check: what the EQ was meant to change against what the mic says it changed.</summary>
    public void Compare(string speaker, Response before, Response after, IReadOnlyList<EqBand> bands)
    {
        var grid = Response.Grid;
        var sb = new StringBuilder();
        sb.AppendLine($"{speaker}: designed EQ vs measured change (after − before)");
        sb.AppendLine("     Hz  designed  measured");
        foreach (double hz in TableHz)
        {
            int i = Array.FindIndex(grid, f => f >= hz / Math.Pow(2, 1.0 / 48));
            if (i < 0) continue;
            sb.AppendLine(string.Format(Inv, "{0,7:0.#} {1,9:0.0} {2,9:0.0}", hz, EqBand.ResponseDb(bands, grid[i]), after.Db[i] - before.Db[i]));
        }
        lock (text) text.Append(sb);
    }

    /// <summary>Writes log.txt and measurements.json. Never throws.</summary>
    public void Save(string outcome)
    {
        Line("outcome: " + outcome);
        lock (text) TryWrite("log.txt", text.ToString());
        lock (speakers) TryWrite("measurements.json", JsonSerializer.Serialize(new { grid = Response.Grid, floor = floorGrid, speakers }, JsonOptions));
    }

    // ---------------------------------------------------------------- export

    /// <summary>
    /// Zips every kept run, the Audio Tools and speaker EQ settings, the mic calibration files in
    /// <paramref name="calibrationFolder"/> and <paramref name="now"/> (the EQ as it is at export) into <paramref name="zipPath"/>.
    /// </summary>
    public static void Export(string zipPath, string now, string calibrationFolder)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("now.txt");
        using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(now);

        void AddFolder(string dir, string prefix)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { zip.CreateEntryFromFile(file, prefix + Path.GetRelativePath(dir, file).Replace('\\', '/'), CompressionLevel.Optimal); }
                catch { /* in use or gone */ }
        }
        void AddFile(string file, string name)
        {
            if (File.Exists(file))
                try { zip.CreateEntryFromFile(file, name); } catch { }
        }

        AddFolder(Root, "runs/");
        AddFile(Path.Combine(AppPaths.SettingsFolder, "AudioLevel.json"), "settings/AudioLevel.json");
        AddFile(Path.Combine(AppPaths.SettingsFolder, "SpeakerEq.json"), "settings/SpeakerEq.json");
        AddFolder(calibrationFolder, "calibration/");
    }

    // ---------------------------------------------------------------- helpers

    private void Prune()
    {
        try
        {
            foreach (var old in Directory.GetDirectories(Root).OrderByDescending(d => d, StringComparer.Ordinal).Skip(Keep))
                Directory.Delete(old, recursive: true);
        }
        catch { /* in use: next time */ }
    }

    private void TryWrite(string name, string content)
    {
        try { File.WriteAllText(Path.Combine(folder, name), content, new UTF8Encoding(false)); }
        catch { /* diagnostics never break a run */ }
    }

    /// <summary>A mono 32-bit float WAV.</summary>
    private void WriteWav(string name, float[] samples, double rate)
    {
        try
        {
            using var w = new BinaryWriter(File.Create(Path.Combine(folder, name)));
            int bytes = samples.Length * 4;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)1);
            w.Write((int)rate); w.Write((int)rate * 4); w.Write((short)4); w.Write((short)32);
            w.Write("data"u8); w.Write(bytes);
            foreach (float s in samples) w.Write(s);
        }
        catch { }
    }

    private static string Safe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => bad.Contains(c) || c == ' ' ? '-' : c).ToArray());
    }

    private static double Peak(float[] x) => x.Length == 0 ? 0 : x.Max(v => Math.Abs(v));

    private static double Rms(float[] x)
    {
        if (x.Length == 0) return double.NaN;
        double sum = 0;
        foreach (float v in x) sum += (double)v * v;
        return 10 * Math.Log10(Math.Max(sum / x.Length, 1e-30));
    }
}
