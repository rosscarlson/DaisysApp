using System.IO;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since 4.5.0) A session log a race, for the race analysis: written from the reading thread while the race clock
/// runs (a restart or the next race of a series starts another), ended by free roam or the data stopping for 5 s. Only
/// a race the tracker keeps a lap or the result of is kept (<see cref="Claim"/>, which names its file on the
/// records); the others (driving around, a race quit) are deleted once the next frames have come and gone without a
/// claim. Core's <see cref="SessionRecorder"/> writes it (Manual), in Core's session folder.
/// </summary>
internal sealed class RaceLog : IDisposable
{
    /// <summary>How long after the data comes back a race's file waits for the tracker to keep it.</summary>
    private const long SettleMs = 15_000;
    private const long StopMs = 5_000, GapMs = 1_500;

    private readonly SessionRecorder recorder = new() { Manual = true };
    private readonly object gate = new();
    private sealed class Entry
    {
        public required string Path { get; init; }
        public required DateTime Started { get; init; }
        public bool Ended, Kept;
        public long SettleAt = long.MaxValue;
    }
    private readonly List<Entry> entries = new();
    private Entry? current;
    private float lastRaceTime;
    private long lastAt;

    /// <summary>Recording (Analysis is on): off, nothing's written.</summary>
    public volatile bool On;

    /// <summary>A frame from the game (the reading thread).</summary>
    public void Feed(TelemetryFrame f)
    {
        long now = Environment.TickCount64;
        lock (gate)
        {
            bool racing = On && f.Live && f.HasRace && f.RaceTime > 0;
            if (racing)
            {
                float rt = f.RaceTime;
                // restarted (the clock back near 0, or back at all after the data stopped): another race, as the tracker sees it
                if (current != null && ((rt < 0.5f && lastRaceTime > 2) || (rt < lastRaceTime - 1 && now - lastAt >= GapMs))) EndLocked();
                if (current == null && recorder.Begin() is { } path)
                {
                    current = new Entry { Path = path, Started = DateTime.Now };
                    entries.Add(current);
                }
                recorder.Sample(f.Steer, f.Throttle, f.Brake, f.Clutch, f.Handbrake, Telemetry(f));
                lastRaceTime = rt;
                lastAt = now;
            }
            // free roam, or switched off
            else if (current != null && (!On || (f.Live && f.HasRace && f.RaceTime <= 0))) EndLocked();
            // frames after a race ended: the tracker has seen them too, so its claim is due
            if (f.Live) foreach (var e in entries) if (e.Ended && e.SettleAt == long.MaxValue) e.SettleAt = now + SettleMs;
            SettleLocked(now);
        }
    }

    /// <summary>No frame lately (the reading thread): the data stopping ends the race's file.</summary>
    public void Idle()
    {
        long now = Environment.TickCount64;
        lock (gate)
        {
            if (current != null && (!On || now - lastAt > StopMs)) EndLocked();
            SettleLocked(now);
        }
    }

    /// <summary>The race the tracker started at <paramref name="raceAt"/> has a lap or result kept: its file is kept, and
    /// its name returned for the records (null: no file for it).</summary>
    public string? Claim(DateTime raceAt)
    {
        lock (gate)
        {
            var e = entries.Where(x => Math.Abs((x.Started - raceAt).TotalSeconds) <= 5).MaxBy(x => x.Started);
            if (e == null) return null;
            if (!e.Kept) Logging.Log.Here.Info($"Analysis: race log kept: {Path.GetFileName(e.Path)}");
            e.Kept = true;
            return Path.GetFileName(e.Path);
        }
    }

    private void EndLocked()
    {
        recorder.End();
        if (current != null) current.Ended = true;
        current = null;
        lastRaceTime = 0;
    }

    /// <summary>Ended files: kept ones forgotten, the rest deleted once their time to be claimed is up.</summary>
    private void SettleLocked(long now)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (!e.Ended || now < e.SettleAt) continue;
            entries.RemoveAt(i);
            if (!e.Kept) Delete(e.Path);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
            Logging.Log.Here.Info($"Analysis: race log dropped (no lap or race kept): {Path.GetFileName(path)}");
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Analysis: deleting {Path.GetFileName(path)}", ex); }
    }

    /// <summary>What the log keeps of the game's telemetry: laps and the car's place only where the game gives them
    /// (Forza); since 4.6.10 the rpm, gear and power (the dash's) for Shifting.</summary>
    private static SessionTelemetry Telemetry(TelemetryFrame f)
    {
        float susp = 0;
        for (int w = 0; w < 4; w++) susp = Math.Max(susp, Math.Abs(f.SuspVel[w]));
        return new SessionTelemetry(f.SpeedMs, f.AccelX, f.AccelZ, f.LapNumber, f.CurrentLap > 0 ? f.CurrentLap : double.NaN,
            f.Distance, f.PosX, f.PosZ, susp, string.IsNullOrEmpty(f.Car) ? null : f.Car,
            f.MaxRpm > 0 ? f.Rpm : double.NaN, f.MaxRpm > 0 ? f.MaxRpm : double.NaN, f.MaxRpm > 0 ? f.GearNo : double.NaN,
            f.HasRace ? f.Power / 1000 : double.NaN);
    }

    /// <summary>The app closing: the race being written ends, and files no race claimed are deleted.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            EndLocked();
            foreach (var e in entries) if (!e.Kept) Delete(e.Path);
            entries.Clear();
        }
    }
}
