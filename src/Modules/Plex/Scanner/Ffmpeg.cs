using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DaisysApp.Logging;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.Plex.Scanner;

/// <summary>
/// FFmpeg, which Scanner's transcodes run: the one picked in the settings, the one downloaded to
/// %LOCALAPPDATA%\DaisysApp\ffmpeg, or one on the PATH. The download is gyan.dev's essentials build of FFmpeg 7.1:
/// later builds need NVIDIA's driver 570 or newer for NVENC, which GTX 10-series cards (Pascal) never got.
/// </summary>
public static class Ffmpeg
{
    public const string DownloadUrl = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "ffmpeg");

    /// <summary>ffmpeg.exe, or null if there's none.</summary>
    public static string? Find(PlexSettings s)
    {
        if (s.FfmpegPath.Trim() is { Length: > 0 } p && File.Exists(p)) return p;
        string mine = Path.Combine(Folder, "ffmpeg.exe");
        if (File.Exists(mine)) return mine;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { string f = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(f)) return f; } catch { }
        }
        return null;
    }

    /// <summary>ffprobe.exe beside it.</summary>
    public static string? Probe(string ffmpeg) => Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe") is var p && File.Exists(p) ? p : null;

    /// <summary>"7.1-essentials_build-www.gyan.dev".</summary>
    public static async Task<string> VersionAsync(string ffmpeg)
    {
        var r = await RunAsync(ffmpeg, ["-hide_banner", "-version"], CancellationToken.None);
        string first = r.Output.Split('\n').FirstOrDefault() ?? "";
        return first.StartsWith("ffmpeg version ") ? first["ffmpeg version ".Length..].Split(' ')[0] : first.Trim();
    }

    /// <summary>Downloads and unpacks FFmpeg to <see cref="Folder"/>; <paramref name="progress"/> 0–1.</summary>
    public static async Task DownloadAsync(Action<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        string zip = Path.Combine(Folder, "ffmpeg.zip");
        using (var http = Http.Client(TimeSpan.FromMinutes(30)))
        using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(cancel);
            await using var dst = File.Create(zip);
            var buf = new byte[1 << 16];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buf, cancel)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), cancel);
                got += n;
                if (total > 0) progress(got / (double)total);
            }
        }
        await Task.Run(() =>
        {
            using var a = ZipFile.OpenRead(zip);
            foreach (var e in a.Entries.Where(e => e.FullName.Contains("/bin/") && e.Name is "ffmpeg.exe" or "ffprobe.exe"))
                e.ExtractToFile(Path.Combine(Folder, e.Name), true);
            var license = a.Entries.FirstOrDefault(e => e.Name == "LICENSE");
            license?.ExtractToFile(Path.Combine(Folder, "LICENSE.txt"), true);
        }, cancel);
        File.Delete(zip);
        encoders.Clear();
        Log.Here.Info($"Plex Scanner: FFmpeg downloaded to {Folder}");
    }

    private static readonly Dictionary<string, Encoder?> encoders = new();

    /// <summary>The encoder for a profile's codec: the graphics card's (NVIDIA, Intel, AMD: the first that works here),
    /// else the processor's (slow; none for AV1). Tried once with a moment of black.</summary>
    public static async Task<Encoder?> EncoderAsync(string ffmpeg, string codec)
    {
        lock (encoders) if (encoders.TryGetValue(codec, out var known)) return known;
        string c = codec switch { "H264" => "h264", "AV1" => "av1", _ => "hevc" };
        Encoder? found = null;
        foreach (var (name, family) in new[] { ($"{c}_nvenc", "nvenc"), ($"{c}_qsv", "qsv"), ($"{c}_amf", "amf") })
        {
            var r = await RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=1280x720:r=30:d=0.2",
                "-c:v", name, "-f", "null", "-"], CancellationToken.None);
            if (r.Code == 0) { found = new Encoder(name, family); break; }
            Log.Here.Debug($"Plex Scanner: {name} not usable: {r.Output.Trim().Split('\n').FirstOrDefault()}");
        }
        if (found == null && c != "av1") found = new Encoder(c == "h264" ? "libx264" : "libx265", "cpu");
        lock (encoders) encoders[codec] = found;
        Log.Here.Info($"Plex Scanner: {codec} encoder: {found?.Name ?? "none"}");
        return found;
    }

    public sealed record Encoder(string Name, string Family)
    {
        public bool Gpu => Family != "cpu";
        public string Label => Family switch { "nvenc" => "NVIDIA NVENC", "qsv" => "Intel Quick Sync", "amf" => "AMD AMF", _ => T("the processor (slow)") };
    }

    /// <summary>What ffprobe says of a file.</summary>
    public static async Task<ProbeResult> ProbeAsync(string ffprobe, string file, CancellationToken cancel)
    {
        var r = await RunAsync(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file], cancel);
        if (r.Code != 0) throw new InvalidOperationException(F("FFprobe couldn't read it: {0}", r.Output.Trim().Split('\n').LastOrDefault() ?? ""));
        using var doc = JsonDocument.Parse(r.Output);
        var root = doc.RootElement;
        var p = new ProbeResult();
        if (root.TryGetProperty("format", out var f))
        {
            p.Duration = double.TryParse(PlexClient.Str(f, "duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
            p.BitrateKbps = long.TryParse(PlexClient.Str(f, "bit_rate"), out var b) ? b / 1000 : 0;
        }
        if (root.TryGetProperty("streams", out var streams))
            foreach (var s in streams.EnumerateArray())
            {
                var st = new ProbeStream
                {
                    Index = (int)PlexClient.Num(s, "index"),
                    Type = PlexClient.Str(s, "codec_type") ?? "",
                    Codec = PlexClient.Str(s, "codec_name") ?? "",
                    Channels = (int)PlexClient.Num(s, "channels"),
                    Width = (int)PlexClient.Num(s, "width"),
                    Height = (int)PlexClient.Num(s, "height"),
                    PixFmt = PlexClient.Str(s, "pix_fmt") ?? "",
                    Transfer = PlexClient.Str(s, "color_transfer") ?? "",
                    Primaries = PlexClient.Str(s, "color_primaries") ?? "",
                    Space = PlexClient.Str(s, "color_space") ?? "",
                    AttachedPic = s.TryGetProperty("disposition", out var disp) && PlexClient.Num(disp, "attached_pic") == 1,
                };
                string rate = PlexClient.Str(s, "avg_frame_rate") ?? "";
                if (rate.Split('/') is [var a, var bb] && double.TryParse(a, out var na) && double.TryParse(bb, out var nb) && nb > 0) st.Fps = na / nb;
                foreach (var sd in PlexClient.List(s, "side_data_list"))
                    if (PlexClient.Num(sd, "dv_profile") is > 0 and var dv) st.DolbyVision = (int)dv;
                p.Streams.Add(st);
            }
        return p;
    }

    public sealed record Result(int Code, string Output);

    /// <summary>Runs a program hidden, its output and errors read together.</summary>
    public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(exe);
        var o = p.StandardOutput.ReadToEndAsync(cancel);
        var e = p.StandardError.ReadToEndAsync(cancel);
        try { await p.WaitForExitAsync(cancel); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        return new Result(p.ExitCode, await o + await e);
    }
}

public sealed class ProbeResult
{
    public double Duration { get; set; }
    public long BitrateKbps { get; set; }
    public List<ProbeStream> Streams { get; } = [];
    public ProbeStream? Video => Streams.FirstOrDefault(s => s.Type == "video" && !s.AttachedPic);
}

public sealed class ProbeStream
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public string Codec { get; init; } = "";
    public int Channels { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string PixFmt { get; init; } = "";
    public string Transfer { get; init; } = "";
    public string Primaries { get; init; } = "";
    public string Space { get; init; } = "";
    public bool AttachedPic { get; init; }
    public double Fps { get; set; }
    public int DolbyVision { get; set; }

    public bool Hdr => Transfer is "smpte2084" or "arib-std-b67";
    public bool TenBit => PixFmt.Contains("10") || PixFmt.Contains("12");
}
