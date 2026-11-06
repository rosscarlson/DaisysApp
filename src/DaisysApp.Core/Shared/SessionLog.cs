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
/// start, then steering, throttle, brake, clutch and handbrake, 0–1, empty when not bound). Since 4.4.0 (format 2) the
/// game's telemetry follows, empty without it (<see cref="SessionTelemetry"/>), and a "# car," line each time the car
/// changes; an exported one adds "# driver," and "# name," lines.
/// </summary>
public static class SessionLog
{
    /// <summary>%LOCALAPPDATA%\DaisysApp\ForzaSessions</summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.DaisysAppName, "ForzaSessions");

    public const string Header = "ms,steering,throttle,brake,clutch,handbrake,speed,latg,long,lap,laptime,distance,x,z,susp";

    /// <summary>Sessions imported from other drivers (Forza Analysis), under <see cref="Folder"/>.</summary>
    public static string ImportedFolder => Path.Combine(Folder, "Imported");
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
/// The game's telemetry for a row of the session log (since Core 0.57): speed in m/s, lateral and longitudinal
/// acceleration in g (right, forward), the lap (0 the first; -1 unknown), the time into it and the distance driven (s,
/// m), where the car is (m), the fastest suspension movement of the four wheels (m/s), and the car's name. NaN, -1 or
/// null where the game doesn't give it.
/// </summary>
public readonly record struct SessionTelemetry(double SpeedMs, double LatG, double LonG, int Lap, double LapTime, double Distance,
    double X, double Z, double Suspension, string? Car)
{
    public static readonly SessionTelemetry None = new(double.NaN, double.NaN, double.NaN, -1, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, null);
}

/// <summary>
/// Writes the session log from the overlay's reading thread: a file is started by the first movement of a pedal or the wheel,
/// rows go in at most every 10 ms, and two minutes with nothing moving ends the session (the next movement starts a new
/// one). A session with less than 20 seconds of driving in it isn't kept. <see cref="Manual"/> (since Core 0.58,
/// SimEffects → Analysis: a session a race): only <see cref="Begin"/> starts one and <see cref="End"/> ends it, and
/// none is dropped (the caller keeps or deletes it).
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

    /// <summary>Started and ended only by <see cref="Begin"/> and <see cref="End"/>; nothing dropped.</summary>
    public bool Manual { get; init; }

    /// <summary>The session being written now, or null.</summary>
    public string? Current { get { lock (gate) return path; } }

    private static long Now => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>The inputs now (0–1, NaN unbound). Cheap when it isn't time for a row yet.</summary>
    public void Sample(double steer, double throttle, double brake, double clutch, double handbrake) =>
        Sample(steer, throttle, brake, clutch, handbrake, SessionTelemetry.None);

    private string? lastCar;

    /// <summary>The inputs now (0–1, NaN unbound) and the game's telemetry. Cheap when it isn't time for a row yet.</summary>
    public void Sample(double steer, double throttle, double brake, double clutch, double handbrake, in SessionTelemetry t)
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
                if (Manual || !moving) return;
                Open(now);
                if (writer == null) return;
            }
            if (moving)
            {
                if (now - lastActive < 1000) activeMs += now - Math.Max(last, lastActive);
                lastActive = now;
            }
            else if (!Manual && now - lastActive > IdleEndMs) { CloseLocked(); return; }
            last = now;
            line.Clear();
            line.Append(now - start);
            foreach (double v in new[] { steer, throttle, brake, clutch, handbrake }) Value(v, "0.####");
            Value(t.SpeedMs, "0.##");
            Value(t.LatG, "0.###");
            Value(t.LonG, "0.###");
            line.Append(',');
            if (t.Lap >= 0) line.Append(t.Lap.ToString(CultureInfo.InvariantCulture));
            Value(t.LapTime, "0.###");
            Value(t.Distance, "0.#");
            Value(t.X, "0.#");
            Value(t.Z, "0.#");
            Value(t.Suspension, "0.###");
            try
            {
                if (!string.IsNullOrEmpty(t.Car) && t.Car != lastCar) { writer.WriteLine("# car," + t.Car); lastCar = t.Car; }
                writer.WriteLine(line);
                rows++;
                if (now - lastFlush > FlushMs) { writer.Flush(); lastFlush = now; }
            }
            catch (Exception ex) { Logging.ErrorLog.Write("Input Overlay session log", ex); CloseLocked(); }
        }
    }

    private void Value(double v, string format)
    {
        line.Append(',');
        if (!double.IsNaN(v) && !double.IsInfinity(v)) line.Append(Math.Round(v, 4).ToString(format, CultureInfo.InvariantCulture));
    }

    private void Open(long now)
    {
        try
        {
            Directory.CreateDirectory(SessionLog.Folder);
            var started = DateTime.Now;
            string name = started.ToString(SessionLog.FileNameFormat, CultureInfo.InvariantCulture);
            path = Path.Combine(SessionLog.Folder, name + ".csv");
            for (int k = 2; File.Exists(path); k++) path = Path.Combine(SessionLog.Folder, $"{name} ({k}).csv");
            writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536);
            writer.WriteLine("# ForzaWheel session,2"); // (the format's name, from when ForzaWheel wrote it; 2: with telemetry)
            writer.WriteLine("# started," + started.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteLine(SessionLog.Header);
            start = now;
            last = long.MinValue;
            lastActive = now;
            lastFlush = now;
            activeMs = 0;
            rows = 0;
            lastCar = null;
            Logging.Log.Here.Info($"Session log started: {path}");
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Input Overlay session log", ex);
            writer = null;
            path = null;
        }
    }

    /// <summary>(<see cref="Manual"/>) Ends the session being written, if any, and starts another: its file's path
    /// (null: it couldn't be made).</summary>
    public string? Begin()
    {
        lock (gate)
        {
            CloseLocked();
            Open(Now);
            return path;
        }
    }

    /// <summary>Ends the session being written: its file's path (null: none).</summary>
    public string? End()
    {
        lock (gate)
        {
            string? was = writer != null ? path : null;
            CloseLocked();
            return was;
        }
    }

    /// <summary>Ends the session now (the overlay closed, or the app is closing).</summary>
    public void Close() { lock (gate) CloseLocked(); }

    private void CloseLocked()
    {
        if (writer == null) return;
        try { writer.Dispose(); } catch { }
        writer = null;
        if (Manual) Logging.Log.Here.Info($"Session log ended: {path} ({rows} rows)");
        else if (activeMs < MinActiveMs)
        {
            try { File.Delete(path!); } catch { }
            Logging.Log.Here.Info($"Input Overlay session log dropped: only {activeMs / 1000.0:0} s of driving");
        }
        else Logging.Log.Here.Info($"Input Overlay session log saved: {path} ({rows} rows, {activeMs / 1000.0:0} s of driving)");
        path = null;
    }

    public void Dispose() => Close();
}
