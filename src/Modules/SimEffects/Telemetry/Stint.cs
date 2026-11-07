namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// (since 4.10.0) For the games that run sessions (iRacing, the Assetto Corsas: <see cref="TelemetryFrame.Sessions"/>):
/// Analysis' race clock and distance, which they don't send as Forza does. The clock counts while the car is on track
/// and the game live, and starts again at 0 in each session and each time the car goes back on track (out of the
/// garage), so a run is one race log; the distance is the speed over time. Laps are counted from the car's place on
/// the lap where the game doesn't count them.
/// </summary>
internal sealed class Stint
{
    private string session = "";
    private float time, distance, lastPct = -1;
    private int laps;

    /// <summary>The frame's clock and distance, for the session <paramref name="key"/>.</summary>
    public void Step(TelemetryFrame f, string key, bool onTrack)
    {
        if (key != session)
        {
            session = key;
            time = 0;
            laps = 0;
            lastPct = -1;
        }
        if (!onTrack) time = 0;
        else if (f.Live && f.Dt > 0)
        {
            time += f.Dt;
            distance += f.SpeedMs * f.Dt;
        }
        f.RaceTime = time;
        f.Distance = distance;
    }

    /// <summary>The laps done, counted at each crossing of the line (the place on the lap going from near 1 to near 0).</summary>
    public int Laps(float pct)
    {
        if (pct is >= 0 and <= 1)
        {
            if (lastPct > 0.9f && pct < 0.1f) laps++;
            else if (lastPct is >= 0 and < 0.1f && pct > 0.9f) laps = Math.Max(0, laps - 1);
            lastPct = pct;
        }
        return laps;
    }
}
