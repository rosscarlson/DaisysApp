namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>The games a profile can be for. Forza sends its telemetry over UDP (Data Out, to the profile's port);
/// iRacing and ACC share it in memory, so they need no port.</summary>
public enum Game { ForzaHorizon, ForzaMotorsport, IRacing, Acc }

public static class Games
{
    public static readonly Game[] All = { Game.ForzaHorizon, Game.ForzaMotorsport, Game.IRacing, Game.Acc };

    public static string Title(Game g) => g switch
    {
        Game.ForzaHorizon => T("Forza Horizon"),
        Game.ForzaMotorsport => T("Forza Motorsport"),
        Game.IRacing => T("iRacing"),
        _ => T("Assetto Corsa Competizione"),
    };

    /// <summary>True for the games that send UDP (so a profile for them has a port).</summary>
    public static bool UsesPort(Game g) => g is Game.ForzaHorizon or Game.ForzaMotorsport;

    public static ITelemetrySource Source(Game g, int port) => g switch
    {
        Game.ForzaHorizon or Game.ForzaMotorsport => new ForzaSource(port),
        Game.IRacing => new IRacingSource(),
        _ => new AccSource(),
    };
}

/// <summary>
/// One moment of telemetry, the same for every game: what the effects are worked out from. Accelerations are in g
/// (X right, Y up, Z forward); wheels in the order front left, front right, rear left, rear right. Amounts the games
/// give in their own ways (kerbs, off-track, slip, ABS) are 0..1.
/// </summary>
public sealed class TelemetryFrame
{
    /// <summary>Driving (not in a menu, paused or a replay).</summary>
    public bool Live;
    /// <summary>Seconds since the frame before (0 when unknown).</summary>
    public float Dt;
    public float SpeedMs, Rpm, IdleRpm, MaxRpm, Throttle, Brake;
    public int Gear;
    public float AccelX, AccelY, AccelZ;
    /// <summary>Suspension speed of each wheel, m/s (+ compressing).</summary>
    public readonly float[] SuspVel = new float[4];
    public float Kerb, OffRoad, Slip, Abs;

    /// <summary>Where the RPM is between idle (0) and the redline (1).</summary>
    public float RpmFraction => MaxRpm > IdleRpm + 100 ? Math.Clamp((Rpm - IdleRpm) / (MaxRpm - IdleRpm), 0, 1) : 0;

    public void CopyTo(TelemetryFrame o)
    {
        o.Live = Live; o.Dt = Dt; o.SpeedMs = SpeedMs; o.Rpm = Rpm; o.IdleRpm = IdleRpm; o.MaxRpm = MaxRpm;
        o.Throttle = Throttle; o.Brake = Brake; o.Gear = Gear; o.AccelX = AccelX; o.AccelY = AccelY; o.AccelZ = AccelZ;
        Array.Copy(SuspVel, o.SuspVel, 4);
        o.Kerb = Kerb; o.OffRoad = OffRoad; o.Slip = Slip; o.Abs = Abs;
    }
}

/// <summary>Where telemetry comes from: read on the engine's thread.</summary>
public interface ITelemetrySource : IDisposable
{
    /// <summary>Waits up to <paramref name="timeoutMs"/> for the next frame and fills it; false when none came.</summary>
    bool Read(TelemetryFrame frame, int timeoutMs);

    /// <summary>What's wrong, if anything (the port is taken, say), for the status line.</summary>
    string? Problem { get; }
}
