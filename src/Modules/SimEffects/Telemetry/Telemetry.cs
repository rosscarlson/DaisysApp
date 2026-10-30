namespace DaisysApp.Applets.SimEffects.Telemetry;

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

    /// <summary>The game's own reader. Use <see cref="TelemetryHub.Open"/> instead, so tools listening to the same game
    /// (the Shaker, Notify, Fanatec) share one: only one program can listen on a UDP port.</summary>
    internal static ITelemetrySource Source(Game g, int port) => g switch
    {
        Game.ForzaHorizon => new ForzaSource(port, "FH"),
        Game.ForzaMotorsport => new ForzaSource(port, "FM"),
        Game.IRacing => new IRacingSource(),
        _ => new AccSource(),
    };

    /// <summary>The programs each game runs as (one running picks its profile, or the game, with Game Switching).</summary>
    public static List<string> Programs(Game g) => g switch
    {
        Game.ForzaHorizon => new() { "ForzaHorizon4.exe", "ForzaHorizon5.exe", "ForzaHorizon6.exe" },
        Game.ForzaMotorsport => new() { "forza_steamworks_release_final.exe", "forza_gaming.desktop.x64_release_final.exe", "ForzaMotorsport7.exe" },
        Game.IRacing => new() { "iRacingSim64DX11.exe" },
        _ => new() { "AC2-Win64-Shipping.exe" },
    };

    /// <summary>A program's name without its folder and ".exe", as the process list has it.</summary>
    public static string Bare(string s) => DaisysApp.Shared.RunningPrograms.Bare(s);
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
    /// <summary>The clutch and handbrake pressed, 0..1 (where the game gives them).</summary>
    public float Clutch, Handbrake;
    /// <summary>The steering, 0 full left, 0.5 straight, 1 full right.</summary>
    public float Steer = 0.5f;
    /// <summary>The game's own gear number (each counts differently; for noticing a shift).</summary>
    public int Gear;
    /// <summary>The gear the same in every game: -1 reverse, 0 neutral, 1 first…</summary>
    public int GearNo;
    /// <summary>The car: an id unique within the game (its redline is learned under it) and a name to show.</summary>
    public string CarId = "", Car = "";
    /// <summary>The game's own shift lights, where it gives them (iRacing), RPM: the first light, the shift point, and
    /// where they flash. 0 when it doesn't.</summary>
    public float FirstRpm, ShiftRpm, BlinkRpm;
    public float AccelX, AccelY, AccelZ;
    /// <summary>Suspension speed of each wheel, m/s (+ compressing).</summary>
    public readonly float[] SuspVel = new float[4];
    public float Kerb, OffRoad, Slip, Abs;
    /// <summary>Forza's car and race data (since 3.23.1, for Analysis); <see cref="HasRace"/> false in other games.</summary>
    public bool HasRace;
    /// <summary>The car: its number in the game (CarOrdinal), class (0 D … 7 X), PI, drivetrain (0 FWD, 1 RWD, 2 AWD),
    /// cylinders, and Horizon's CarGroup (0 elsewhere).</summary>
    public int CarOrdinal, CarClass, CarPi, Drivetrain, Cylinders;
    public uint CarGroup;
    /// <summary>Power in watts, torque in N·m, boost in psi, fuel 0..1, the distance driven this session in metres.</summary>
    public float Power, Torque, Boost, Fuel, Distance;
    /// <summary>Where the car is in the world, metres.</summary>
    public float PosX, PosY, PosZ;
    /// <summary>Each tyre's temperature, °F (front left, front right, rear left, rear right).</summary>
    public readonly float[] TireTemp = new float[4];
    /// <summary>The race's times in seconds (0: none yet), the lap (0 the first), the place (0: not racing).</summary>
    public float BestLap, LastLap, CurrentLap, RaceTime;
    public int LapNumber, RacePosition;
    /// <summary>The same per wheel, where the game tells them apart (Forza); otherwise each is the whole car's.</summary>
    public readonly float[] KerbAt = new float[4], OffRoadAt = new float[4], SlipAt = new float[4];

    /// <summary>A game that only gives the whole car's amounts: each wheel gets them.</summary>
    public void SameOnEveryWheel()
    {
        Array.Fill(KerbAt, Kerb);
        Array.Fill(OffRoadAt, OffRoad);
        Array.Fill(SlipAt, Slip);
    }

    /// <summary>Where the RPM is between idle (0) and the redline (1).</summary>
    public float RpmFraction => MaxRpm > IdleRpm + 100 ? Math.Clamp((Rpm - IdleRpm) / (MaxRpm - IdleRpm), 0, 1) : 0;

    public void CopyTo(TelemetryFrame o)
    {
        o.Live = Live; o.Dt = Dt; o.SpeedMs = SpeedMs; o.Rpm = Rpm; o.IdleRpm = IdleRpm; o.MaxRpm = MaxRpm;
        o.Throttle = Throttle; o.Brake = Brake; o.Clutch = Clutch; o.Handbrake = Handbrake; o.Steer = Steer; o.Gear = Gear; o.GearNo = GearNo;
        o.CarId = CarId; o.Car = Car; o.FirstRpm = FirstRpm; o.ShiftRpm = ShiftRpm; o.BlinkRpm = BlinkRpm; o.AccelX = AccelX; o.AccelY = AccelY; o.AccelZ = AccelZ;
        Array.Copy(SuspVel, o.SuspVel, 4);
        o.Kerb = Kerb; o.OffRoad = OffRoad; o.Slip = Slip; o.Abs = Abs;
        Array.Copy(KerbAt, o.KerbAt, 4); Array.Copy(OffRoadAt, o.OffRoadAt, 4); Array.Copy(SlipAt, o.SlipAt, 4);
        o.HasRace = HasRace; o.CarOrdinal = CarOrdinal; o.CarClass = CarClass; o.CarPi = CarPi; o.Drivetrain = Drivetrain;
        o.Cylinders = Cylinders; o.CarGroup = CarGroup; o.Power = Power; o.Torque = Torque; o.Boost = Boost; o.Fuel = Fuel;
        o.Distance = Distance; o.PosX = PosX; o.PosY = PosY; o.PosZ = PosZ; Array.Copy(TireTemp, o.TireTemp, 4);
        o.BestLap = BestLap; o.LastLap = LastLap; o.CurrentLap = CurrentLap; o.RaceTime = RaceTime;
        o.LapNumber = LapNumber; o.RacePosition = RacePosition;
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
