using System.Diagnostics;
using DaisysApp.Shared.Win32;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>(since 4.20.0) Where a recording's playback is (its video's time, seconds), for any thread.</summary>
internal sealed class PlaybackClock
{
    private readonly object gate = new();
    private double at, shift;
    private long since;
    private bool running;
    private int jumps;

    public (double At, bool Running, int Jumps, double Shift) Now
    {
        get { lock (gate) return (Position(), running, jumps, shift); }
    }

    private double Position() => running ? at + (Stopwatch.GetTimestamp() - since) / (double)Stopwatch.Frequency : at;

    public void Run(bool on)
    {
        lock (gate)
        {
            at = Position();
            since = Stopwatch.GetTimestamp();
            running = on;
        }
    }

    public void Seek(double to)
    {
        lock (gate)
        {
            at = Math.Max(0, to);
            since = Stopwatch.GetTimestamp();
            jumps++;
        }
    }

    /// <summary>Where the telemetry's start is in the video.</summary>
    public void SetShift(double by)
    {
        lock (gate) { shift = by; jumps++; }
    }
}

/// <summary>
/// (since 4.20.0) A recording read as the game was: each frame when the clock comes to it. Paused, at its end and for a
/// moment at a jump, the frames say not driving (as a paused game does: the Shaker goes quiet, the rest hold).
/// </summary>
internal sealed class PlaybackSource(FrameData data, PlaybackClock clock) : ITelemetrySource
{
    private const int StillMs = 33;
    private readonly FrameData.Cursor cursor = new(data);
    private readonly IDisposable timer = HighResTimer.Hold();
    private int next, jumps = -1;
    /// <summary>The last frame given wasn't the one before: its time step isn't from it.</summary>
    private bool fresh = true;

    public string? Problem => null;

    public bool Read(TelemetryFrame frame, int timeoutMs)
    {
        long until = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var (at, running, jump, shift) = clock.Now;
            double t = at - shift;
            if (data.Count == 0) { Thread.Sleep(Math.Max(1, timeoutMs)); return false; }
            if (jump != jumps)
            {
                jumps = jump;
                next = data.At(t);
                return Still(frame);
            }
            if (!running || next >= data.Count)
            {
                Thread.Sleep(Math.Clamp(timeoutMs, 1, StillMs));
                return Still(frame);
            }
            double wait = data.Times[next] - t;
            if (wait > 0)
            {
                long left = until - Environment.TickCount64;
                if (left <= 0) return false;
                Thread.Sleep((int)Math.Clamp(wait * 1000, 1, left));
                continue;
            }
            // (held up: on to where it should be)
            int i = next;
            while (i + 1 < data.Count && data.Times[i + 1] < t - 0.05) i++;
            cursor.Fill(frame, i);
            if (fresh || i != next) frame.Dt = 0;
            fresh = false;
            next = i + 1;
            return true;
        }
    }

    private bool Still(TelemetryFrame frame)
    {
        cursor.Fill(frame, Math.Min(next, data.Count - 1));
        frame.Live = false;
        frame.Dt = 0;
        fresh = true;
        return true;
    }

    public void Dispose() => timer.Dispose();
}
