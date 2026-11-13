using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DaisysApp.Logging;
using DaisysApp.Shared.Video;

namespace DaisysApp.Applets.Plex.Scanner;

public enum JobState { Waiting, Running, Done, Skipped, Failed, Cancelled }

/// <summary>A file in the transcode queue.</summary>
public sealed class TranscodeJob(MediaFile file) : INotifyPropertyChanged
{
    public MediaFile File { get; } = file;
    public JobState State { get; private set; }
    public double Progress { get; private set; }
    public string Status { get; private set; } = T("Waiting");
    /// <summary>Bytes saved (when done).</summary>
    public long Saved { get; private set; }
    /// <summary>FFmpeg's last lines, when it failed.</summary>
    public string? Detail { get; set; }

    public string Name => File.Name;
    public override string ToString() => Name;
    public string Source => $"{File.ResolutionText} {File.CodecText} · {File.MbpsText} · {File.SizeText}";
    public string SavedText => Saved > 0 ? Sizes.Bytes(Saved) : "";
    public bool Finished => State is JobState.Done or JobState.Skipped or JobState.Failed or JobState.Cancelled;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Set(JobState state, string status, double progress = 0, long saved = 0)
    {
        State = state;
        Status = status;
        Progress = progress;
        if (saved != 0) Saved = saved;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>
/// Plex → Scanner's transcode queue: one file at a time with FFmpeg on the graphics card's encoder, to a profile of Core's
/// <see cref="VideoProfiles"/> (its codec, height, bit rates, quality and keyframes; the source's frame rate kept). Every
/// audio track, subtitle and attachment is kept (audio copied, or AAC at the profile's rate). Written beside the original as
/// "name.mkv.part" (Plex ignores it), checked (its length the source's, smaller than it), then put in place of the
/// original (or beside it, as a version), and the folder rescanned in Plex. Runs below normal priority. UI thread.
/// </summary>
public sealed class Transcoder(PlexService service, MediaFiles files)
{
    private PlexSettings S => service.Settings;
    public ObservableCollection<TranscodeJob> Jobs { get; } = [];
    public bool Running { get; private set; }
    private bool stopAfter;
    private CancellationTokenSource? cancel;
    /// <summary>FFmpeg running, and the file it writes (for <see cref="Shutdown"/>).</summary>
    private Process? process;
    private string? writing;

    /// <summary>Running started or stopped.</summary>
    public event Action? Changed;

    /// <summary>The profile picked changed (in Space or Transcode).</summary>
    public event Action? ProfileChanged;

    public void RaiseProfile() => ProfileChanged?.Invoke();

    public RecordingProfile Profile => VideoProfiles.Find(S.TranscodeProfile) ?? VideoProfiles.Find("720p") ?? VideoProfiles.All[0];

    /// <summary>Adds files not queued already (waiting or running); how many were added.</summary>
    public int Add(IEnumerable<MediaFile> list)
    {
        int n = 0;
        foreach (var f in list)
        {
            if (Jobs.Any(j => j.File.Path == f.Path && !j.Finished)) continue;
            Jobs.Add(new TranscodeJob(f));
            n++;
        }
        return n;
    }

    public void Start()
    {
        if (Running) { stopAfter = false; return; }
        _ = RunAsync();
    }

    /// <summary>Stops after the file in hand.</summary>
    public void StopAfter() => stopAfter = true;

    /// <summary>Stops now (the file in hand is left as it was).</summary>
    public void Cancel()
    {
        stopAfter = true;
        cancel?.Cancel();
    }

    /// <summary>The app is closing: stops now and deletes the half-written file.</summary>
    public void Shutdown()
    {
        Cancel();
        try { process?.WaitForExit(3000); } catch { }
        if (writing != null) Delete(writing);
    }

    private async Task RunAsync()
    {
        Running = true;
        stopAfter = false;
        Changed?.Invoke();
        try
        {
            while (!stopAfter && Jobs.FirstOrDefault(j => j.State == JobState.Waiting) is { } job)
            {
                cancel = new CancellationTokenSource();
                try { await RunOne(job, cancel.Token); }
                catch (OperationCanceledException) { job.Set(JobState.Cancelled, T("Stopped")); }
                catch (Exception ex)
                {
                    job.Set(JobState.Failed, ex.Message);
                    Log.Here.Warn($"Plex Scanner: transcode of {job.File.Path} failed: {ex.Message}");
                }
            }
        }
        finally
        {
            Running = false;
            cancel = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Why a file wouldn't be transcoded with the profile, from what Plex says of it (null: it would).</summary>
    public static string? WouldSkip(MediaFile f, RecordingProfile p)
    {
        int target = p.Height;
        bool smaller = target <= 0 || f.Height <= target * 1.05;
        if (smaller && f.BitrateKbps > 0 && f.BitrateKbps <= p.MaxBitrateMbps * 1000 * 1.1) return T("Already this small");
        return null;
    }

    /// <summary>Roughly how big the file would be afterwards (its size if it'd be skipped).</summary>
    public static long Estimate(MediaFile f, RecordingProfile p, bool aac)
    {
        if (WouldSkip(f, p) != null || f.DurationMs <= 0) return f.Size;
        double audio = aac ? p.AudioKbps * Math.Max(1, f.AudioChannels / 2.0) : AudioGuessKbps(f.AudioCodec, f.AudioChannels);
        double kbps = p.BitrateMbps * 1000 + audio;
        return Math.Min(f.Size, (long)(kbps * 1000 / 8 * f.DurationMs / 1000));
    }

    /// <summary>A guess at an audio track's rate (Plex's lists don't say).</summary>
    private static double AudioGuessKbps(string codec, int channels) => codec switch
    {
        "truehd" or "flac" or "pcm" or "dca" when channels > 2 => 3000,
        "dca" => 1500,
        "eac3" => channels > 2 ? 768 : 224,
        "ac3" => channels > 2 ? 640 : 192,
        _ => channels > 2 ? 384 : 160,
    };

    private async Task RunOne(TranscodeJob job, CancellationToken cancel)
    {
        var f = job.File;
        var p = Profile;
        string? ffmpeg = Ffmpeg.Find(S);
        if (ffmpeg == null) { job.Set(JobState.Failed, T("FFmpeg isn't installed")); stopAfter = true; return; }
        string? ffprobe = Ffmpeg.Probe(ffmpeg);
        if (ffprobe == null) { job.Set(JobState.Failed, T("FFprobe isn't beside FFmpeg")); stopAfter = true; return; }
        string src = S.LocalPath(f.Path);
        if (!System.IO.File.Exists(src)) { job.Set(JobState.Failed, T("Not found on this PC")); return; }

        job.Set(JobState.Running, T("Reading…"));
        var probe = await Ffmpeg.ProbeAsync(ffprobe, src, cancel);
        var v = probe.Video;
        if (v == null) { job.Set(JobState.Skipped, T("No video")); return; }
        if (v.DolbyVision == 5) { job.Set(JobState.Skipped, T("Dolby Vision profile 5 (no HDR10 under it)")); return; }
        if (v.Hdr && p.Codec == "H264") { job.Set(JobState.Skipped, T("HDR: pick an HEVC or AV1 profile")); return; }
        bool scale = p.Height > 0 && v.Height > p.Height * 1.05;
        long kbps = probe.BitrateKbps > 0 ? probe.BitrateKbps : f.BitrateKbps;
        if (!scale && kbps > 0 && kbps <= p.MaxBitrateMbps * 1000 * 1.1) { job.Set(JobState.Skipped, T("Already this small")); return; }

        var enc = await Ffmpeg.EncoderAsync(ffmpeg, p.Codec);
        if (enc == null) { job.Set(JobState.Failed, F("No {0} encoder here", p.Codec)); stopAfter = true; return; }

        long size = new FileInfo(src).Length;
        string dir = Path.GetDirectoryName(src)!;
        if (new DriveInfo(Path.GetPathRoot(src)!).AvailableFreeSpace < Math.Min(size, Estimate(f, p, S.TranscodeAac) * 2) + (1L << 30))
        { job.Set(JobState.Failed, T("Not enough free space on its drive")); return; }

        string part = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + ".mkv.part");
        var args = Arguments(src, part, probe, v, p, enc, S.TranscodeAac, scale);
        Log.Here.Info($"Plex Scanner: transcoding {src} ({v.Width}x{v.Height} {v.Codec} {kbps} kbit/s) with {p.Name} ({enc.Name}): ffmpeg {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");
        var sw = Stopwatch.StartNew();
        writing = part;
        try
        {
            string error = await Encode(ffmpeg, args, probe.Duration, job, cancel);
            if (error.Length > 0) { job.Detail = error; job.Set(JobState.Failed, error.Split('\n').Last(l => l.Trim().Length > 0).Trim()); Delete(part); return; }

            var check = await Ffmpeg.ProbeAsync(ffprobe, part, CancellationToken.None);
            long newSize = new FileInfo(part).Length;
            if (Math.Abs(check.Duration - probe.Duration) > Math.Max(2, probe.Duration * 0.005))
            { Delete(part); job.Set(JobState.Failed, F("The new file is {0:0} s long, not {1:0}", check.Duration, probe.Duration)); return; }
            if (newSize >= size) { Delete(part); job.Set(JobState.Skipped, T("Not smaller: kept the original")); return; }

            string target;
            if (S.TranscodeKeepBoth)
            {
                string label = p.Height > 0 ? p.Height + "p" : p.Name;
                target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(src)} - {label}.mkv");
                for (int i = 2; System.IO.File.Exists(target); i++) target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(src)} - {label} {i}.mkv");
                System.IO.File.Move(part, target);
            }
            else
            {
                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + ".mkv");
                try { System.IO.File.Delete(src); }
                catch (Exception ex) { Delete(part); job.Set(JobState.Failed, F("The original couldn't be deleted: {0}", ex.Message)); return; }
                if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
                System.IO.File.Move(part, target);
                files.Removed(f);
            }
            long saved = S.TranscodeKeepBoth ? 0 : size - newSize;
            job.Set(JobState.Done, F("{0} → {1} in {2}", Sizes.Bytes(size), Sizes.Bytes(newSize), Views.PlexUi.Clock((long)sw.Elapsed.TotalMilliseconds)), 1, saved);
            Log.Here.Info($"Plex Scanner: {src} → {target}: {size} → {newSize} bytes in {sw.Elapsed.TotalSeconds:0} s");
            _ = files.RescanFolderAsync(f.LibraryKey, f.Folder);
        }
        catch
        {
            Delete(part);
            throw;
        }
        finally { writing = null; }
    }

    private static void Delete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
    }

    /// <summary>FFmpeg's arguments for one file.</summary>
    public static List<string> Arguments(string src, string dst, ProbeResult probe, ProbeStream v, RecordingProfile p, Ffmpeg.Encoder enc, bool aac, bool scale)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error" };
        if (enc.Gpu) a.AddRange(["-hwaccel", "auto"]);
        a.AddRange(["-i", src]);
        // the main picture, every audio track, the subtitles a Matroska file can hold, and attachments (fonts)
        a.AddRange(["-map", $"0:{v.Index}"]);
        var audio = probe.Streams.Where(s => s.Type == "audio").ToList();
        foreach (var s in audio) a.AddRange(["-map", $"0:{s.Index}"]);
        var subs = probe.Streams.Where(s => s.Type == "subtitle" && s.Codec is not ("eia_608" or "bin_data" or "")).ToList();
        foreach (var s in subs) a.AddRange(["-map", $"0:{s.Index}"]);
        foreach (var s in probe.Streams.Where(s => s.Type == "attachment")) a.AddRange(["-map", $"0:{s.Index}"]);
        a.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c", "copy"]);
        for (int i = 0; i < subs.Count; i++)
            if (subs[i].Codec == "mov_text") a.AddRange([$"-c:s:{i}", "srt"]);
        if (aac)
            for (int i = 0; i < audio.Count; i++)
                a.AddRange([$"-c:a:{i}", "aac", $"-b:a:{i}", $"{(int)(p.AudioKbps * Math.Max(1, audio[i].Channels / 2.0))}k"]);

        bool tenBit = (v.TenBit || v.Hdr) && p.Codec != "H264";
        a.AddRange(["-c:v", enc.Name]);
        if (scale) a.AddRange(["-vf", $"scale=-2:{p.Height}:flags=lanczos"]);
        a.AddRange(["-pix_fmt", tenBit ? (enc.Family == "cpu" ? "yuv420p10le" : "p010le") : (enc.Family == "qsv" ? "nv12" : "yuv420p")]);
        if (tenBit && p.Codec == "HEVC" && enc.Family != "cpu") a.AddRange(["-profile:v", "main10"]);
        string b = Mbit(p.BitrateMbps), max = Mbit(p.RateControl == RateControl.Cbr ? p.BitrateMbps : Math.Max(p.MaxBitrateMbps, p.BitrateMbps)), buf = Mbit(2 * Math.Max(p.MaxBitrateMbps, p.BitrateMbps));
        int q = Math.Clamp(p.Quality, 0, 100);
        switch (enc.Family)
        {
            case "nvenc":
                a.AddRange(["-preset", $"p{1 + q * 6 / 100}", "-rc", p.RateControl == RateControl.Cbr ? "cbr" : "vbr"]);
                break;
            case "qsv":
                a.AddRange(["-preset", new[] { "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" }[q * 6 / 100]]);
                break;
            case "amf":
                a.AddRange(["-quality", q < 34 ? "speed" : q < 67 ? "balanced" : "quality", "-rc", p.RateControl == RateControl.Cbr ? "cbr" : "vbr_peak"]);
                break;
            default:
                a.AddRange(["-preset", new[] { "ultrafast", "veryfast", "faster", "fast", "medium", "slow", "slower" }[q * 6 / 100]]);
                break;
        }
        a.AddRange(["-b:v", b, "-maxrate", max, "-bufsize", buf]);
        double fps = v.Fps is > 1 and < 300 ? v.Fps : 24;
        a.AddRange(["-g", ((int)Math.Round(fps * Math.Clamp(p.KeyframeSeconds, 1, 10))).ToString(CultureInfo.InvariantCulture)]);
        if (v.Primaries.Length > 0 && v.Primaries != "unknown") a.AddRange(["-color_primaries", v.Primaries]);
        if (v.Transfer.Length > 0 && v.Transfer != "unknown") a.AddRange(["-color_trc", v.Transfer]);
        if (v.Space.Length > 0 && v.Space != "unknown") a.AddRange(["-colorspace", v.Space]);
        a.AddRange(["-max_muxing_queue_size", "9999", "-progress", "pipe:1", "-nostats", "-f", "matroska", dst]);
        return a;
    }

    private static string Mbit(double mbps) => ((long)(mbps * 1_000_000)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Runs FFmpeg, its progress to the job; its error text, or "" if it finished.</summary>
    private async Task<string> Encode(string ffmpeg, List<string> args, double duration, TranscodeJob job, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var x in args) psi.ArgumentList.Add(x);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg");
        process = proc;
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var errors = proc.StandardError.ReadToEndAsync(CancellationToken.None);
        var sw = Stopwatch.StartNew();
        using var reg = cancel.Register(() => { try { proc.Kill(true); } catch { } });
        string? line;
        double at = 0, speed = 0;
        while ((line = await proc.StandardOutput.ReadLineAsync(CancellationToken.None)) != null)
        {
            if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out long us)) at = us / 1e6;
            else if (line.StartsWith("speed=") && double.TryParse(line[6..].TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var sp)) speed = sp;
            else if (line.StartsWith("progress=") && duration > 0)
            {
                double share = Math.Clamp(at / duration, 0, 1);
                string left = speed > 0 ? Views.PlexUi.Clock((long)((duration - at) / speed * 1000)) : "…";
                string status = F("{0:0}% · {1:0.0}× · {2} left", share * 100, speed, left);
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (job.State == JobState.Running) job.Set(JobState.Running, status, share); });
            }
        }
        await proc.WaitForExitAsync(CancellationToken.None);
        process = null;
        cancel.ThrowIfCancellationRequested();
        string err = (await errors).Trim();
        return proc.ExitCode == 0 ? "" : err.Length > 0 ? err : F("FFmpeg stopped ({0})", proc.ExitCode);
    }
}
