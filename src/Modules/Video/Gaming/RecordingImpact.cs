using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>A recording as the overlay shows it (read once a second while one runs).</summary>
internal sealed record RecordingStats
{
    public long FileBytes { get; init; }
    /// <summary>The file's average bit rate so far, Mbit/s.</summary>
    public double Mbps { get; init; } = double.NaN;
    public string Description { get; init; } = "";
    public int TargetFps { get; init; }
    /// <summary>Frames written over the last second.</summary>
    public double WrittenFps { get; init; } = double.NaN;
    public int Dropped { get; init; }
    public double DroppedPercent { get; init; }
    /// <summary>The graphics card's video encoder, busy (all programs), %; NaN where Windows doesn't say.</summary>
    public double EncoderPercent { get; init; } = double.NaN;
    /// <summary>Daisy's App's own share of the graphics card's 3D engine (the picture's conversion and cameras), %.</summary>
    public double ConvertGpuPercent { get; init; } = double.NaN;
    /// <summary>Daisy's App's CPU, as a share of the whole CPU, %.</summary>
    public double AppCpuPercent { get; init; } = double.NaN;
}

/// <summary>
/// What a recording costs the PC, for the overlay's Advanced mode: the video encoder's load and Daisy's App's share
/// of the GPU (Windows' GPU engine counters, as Task Manager shows them), Daisy's App's CPU, frames written and dropped,
/// and the file's size and bit rate.
/// </summary>
internal sealed partial class RecordingImpact : IDisposable
{
    private const string Engines = @"\GPU Engine(*)\Utilization Percentage";
    private PdhQuery? pdh;
    private bool pdhTried;
    private readonly int ownPid = Environment.ProcessId;
    private long lastWritten = -1, lastTicks;
    private TimeSpan lastCpu;

    public void Reset() => lastWritten = -1;

    public RecordingStats Read(Recorder r, bool advanced)
    {
        long bytes = 0;
        try { if (r.FilePath != null) bytes = new FileInfo(r.FilePath).Length; } catch { }
        double seconds = (DateTime.Now - r.StartedAt).TotalSeconds;
        int written = r.FramesWritten, dropped = r.FramesDropped;
        long now = Stopwatch.GetTimestamp();
        double fps = double.NaN, appCpu = double.NaN;
        TimeSpan cpu = TimeSpan.Zero;
        try { cpu = Process.GetCurrentProcess().TotalProcessorTime; } catch { }
        if (lastWritten >= 0 && now > lastTicks)
        {
            double dt = (now - lastTicks) / (double)Stopwatch.Frequency;
            fps = (written - lastWritten) / dt;
            appCpu = Math.Clamp(100 * (cpu - lastCpu).TotalSeconds / (dt * Environment.ProcessorCount), 0, 100);
        }
        lastWritten = written;
        lastTicks = now;
        lastCpu = cpu;

        double encoder = double.NaN, convert = double.NaN;
        if (advanced)
        {
            if (!pdhTried)
            {
                pdhTried = true;
                pdh = new PdhQuery();
                if (!pdh.Add(Engines)) { pdh.Dispose(); pdh = null; }
            }
            if (pdh != null)
            {
                pdh.Collect();
                double enc = 0, mine = 0;
                bool any = false;
                foreach (var (instance, value) in pdh.Values(Engines))
                {
                    var m = EngineInstance().Match(instance);
                    if (!m.Success) continue;
                    any = true;
                    string type = m.Groups[2].Value;
                    if (type.StartsWith("VideoEncode", StringComparison.OrdinalIgnoreCase)) enc += value;
                    else if (int.TryParse(m.Groups[1].Value, out int pid) && pid == ownPid && type.Equals("3D", StringComparison.OrdinalIgnoreCase)) mine += value;
                }
                if (any)
                {
                    encoder = Math.Min(100, enc);
                    convert = Math.Min(100, mine);
                }
            }
        }

        int total = written + dropped;
        return new RecordingStats
        {
            FileBytes = bytes,
            Mbps = seconds > 2 ? bytes * 8 / 1e6 / seconds : double.NaN,
            Description = r.Description,
            TargetFps = r.Fps,
            WrittenFps = fps,
            Dropped = dropped,
            DroppedPercent = total > 0 ? 100.0 * dropped / total : 0,
            EncoderPercent = encoder,
            ConvertGpuPercent = convert,
            AppCpuPercent = appCpu,
        };
    }

    public void Dispose() => pdh?.Dispose();

    [GeneratedRegex(@"pid_(\d+)_.*engtype_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EngineInstance();
}
