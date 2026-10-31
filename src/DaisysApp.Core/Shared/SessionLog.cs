using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DaisysApp.Shared;

/// <summary>
/// A driving session's input log: what SimEffects → Input Overlay writes while it shows (ForzaWheel's input overlay
/// before 3.17.0), and Forza Analysis reads. One
/// CSV file per session in <see cref="Folder"/> (shared by Daisy's App and the wrapper, so either can read the
/// other's), named after when it started: a few # lines, a header, then a row about every 10 ms (milliseconds since the
/// start, then steering, throttle, brake, clutch and handbrake, 0–1, empty when not bound).
/// </summary>
public static class SessionLog
{
    /// <summary>%LOCALAPPDATA%\DaisysApp\ForzaSessions</summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.DaisysAppName, "ForzaSessions");

    public const string Header = "ms,steering,throttle,brake,clutch,handbrake";
    public const string FileNameFormat = "yyyy-MM-dd HH.mm.ss";

    /// <summary>The session files, newest first.</summary>
    public static List<FileInfo> List()
    {
        try
        {
            var dir = new DirectoryInfo(Folder);
            return dir.Exists ? dir.GetFiles("*.csv").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList() : new();
        }
        catch { return new(); }
    }

    /// <summary>When a session file's session started (from its name, or the file's time).</summary>
    public static DateTime StartedOf(FileInfo file) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), FileNameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? t : file.CreationTime;
}

/// <summary>
/// Writes the session log from the overlay's reading thread: a file is started by the first movement of a pedal or the wheel,
/// rows go in at most every 10 ms, and two minutes with nothing moving ends the session (the next movement starts a new
/// one). A session with less than 20 seconds of driving in it isn't kept.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    public const int IntervalMs = 10;
    private const long IdleEndMs = 120_000, MinActiveMs = 20_000, FlushMs = 2000;

    private readonly object gate = new();
    private StreamWriter? writer;
    private string? path;
    private long start, last = long.MinValue, lastActive, lastFlush, activeMs, rows;
    private double lastSteer = double.NaN;
    private readonly StringBuilder line = new(64);

    /// <summary>The session being written now, or null.</summary>
    public string? Current { get { lock (gate) return path; } }

    private static long Now => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>The inputs now (0–1, NaN unbound). Cheap when it isn't time for a row yet.</summary>
    public void Sample(double steer, double throttle, double brake, double clutch, double handbrake)
    {
        long now = Now;
        lock (gate)
        {
            // (last starts at long.MinValue: now - last overflowed, so before 3.17.0 no row, nor any session, was written)
            if (last != long.MinValue && now - last < IntervalMs) return;
            bool moving = throttle > 0.02 || brake > 0.02 || clutch > 0.05 || handbrake > 0.05
                || (!double.IsNaN(steer) && !double.IsNaN(lastSteer) && Math.Abs(steer - lastSteer) > 0.002);
            if (!double.IsNaN(steer)) lastSteer = steer;
            if (writer == null)
            {
                if (!moving) return;
                Open(now);
                if (writer == null) return;
            }
            if (moving)
            {
                if (now - lastActive < 1000) activeMs += now - Math.Max(last, lastActive);
                lastActive = now;
            }
            else if (now - lastActive > IdleEndMs) { CloseLocked(); return; }
            last = now;
            line.Clear();
            line.Append(now - start);
            foreach (double v in new[] { steer, throttle, brake, clutch, handbrake })
            {
                line.Append(',');
                if (!double.IsNaN(v)) line.Append(Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture));
            }
            try
            {
                writer.WriteLine(line);
                rows++;
                if (now - lastFlush > FlushMs) { writer.Flush(); lastFlush = now; }
            }
            catch (Exception ex) { Logging.ErrorLog.Write("Input Overlay session log", ex); CloseLocked(); }
        }
    }

    private void Open(long now)
    {
        try
        {
            Directory.CreateDirectory(SessionLog.Folder);
            var started = DateTime.Now;
            path = Path.Combine(SessionLog.Folder, started.ToString(SessionLog.FileNameFormat, CultureInfo.InvariantCulture) + ".csv");
            writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536);
            writer.WriteLine("# ForzaWheel session,1"); // (the format's name, from when ForzaWheel wrote it)
            writer.WriteLine("# started," + started.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteLine(SessionLog.Header);
            start = now;
            last = long.MinValue;
            lastActive = now;
            lastFlush = now;
            activeMs = 0;
            rows = 0;
            Logging.Log.Here.Info($"Input Overlay session log started: {path}");
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Input Overlay session log", ex);
            writer = null;
            path = null;
        }
    }

    /// <summary>Ends the session now (the overlay closed, or the app is closing).</summary>
    public void Close() { lock (gate) CloseLocked(); }

    private void CloseLocked()
    {
        if (writer == null) return;
        try { writer.Dispose(); } catch { }
        writer = null;
        if (activeMs < MinActiveMs)
        {
            try { File.Delete(path!); } catch { }
            Logging.Log.Here.Info($"Input Overlay session log dropped: only {activeMs / 1000.0:0} s of driving");
        }
        else Logging.Log.Here.Info($"Input Overlay session log saved: {path} ({rows} rows, {activeMs / 1000.0:0} s of driving)");
        path = null;
    }

    public void Dispose() => Close();
}
