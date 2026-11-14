namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>The games a profile can be for. Forza sends its telemetry over UDP (Data Out, to the profile's port);
/// iRacing and the Assetto Corsas share it in memory, so they need no port (Assetto Corsa and AC Evo since 4.10.0).</summary>
public enum Game { ForzaHorizon, ForzaMotorsport, IRacing, Acc, AssettoCorsa, AcEvo }

/// <summary>The kind of session a game is in (where it says: iRacing, the Assetto Corsas).</summary>
public enum SessionKind { None, Practice, Qualifying, Race }

public static class Games
{
    public static readonly Game[] All = { Game.ForzaHorizon, Game.ForzaMotorsport, Game.IRacing, Game.Acc, Game.AssettoCorsa, Game.AcEvo };

    public static string Title(Game g) => g switch
    {
        Game.ForzaHorizon => T("Forza Horizon"),
        Game.ForzaMotorsport => T("Forza Motorsport"),
        Game.IRacing => T("iRacing"),
        Game.AssettoCorsa => T("Assetto Corsa"),
        Game.AcEvo => T("Assetto Corsa EVO"),
        _ => T("Assetto Corsa Competizione"),
    };

    /// <summary>True for the games that send UDP (so a profile for them has a port).</summary>
    public static bool UsesPort(Game g) => g is Game.ForzaHorizon or Game.ForzaMotorsport;

    /// <summary>The game's own reader. Use <see cref="TelemetryHub.Open"/> instead, so tools listening to the same game
    /// (the Shaker, Notify, Wheel) share one: only one program can listen on a UDP port.</summary>
    internal static ITelemetrySource Source(Game g, int port) => g switch
    {
        Game.ForzaHorizon => new ForzaSource(port, "FH"),
        Game.ForzaMotorsport => new ForzaSource(port, "FM"),
        Game.IRacing => new IRacingSource(),
        Game.AssettoCorsa => new AccSource(AccSource.Kind.Ac),
        Game.AcEvo => new AccSource(AccSource.Kind.Evo),
        _ => new AccSource(AccSource.Kind.Acc),
    };

    /// <summary>The programs each game runs as (one running picks its profile, or the game, with Game Switching).</summary>
    public static List<string> Programs(Game g) => g switch
    {
        Game.ForzaHorizon => new() { "ForzaHorizon4.exe", "ForzaHorizon5.exe", "ForzaHorizon6.exe" },
        Game.ForzaMotorsport => new() { "forza_steamworks_release_final.exe", "forza_gaming.desktop.x64_release_final.exe", "ForzaMotorsport7.exe" },
        Game.IRacing => new() { "iRacingSim64DX11.exe" },
        Game.AssettoCorsa => new() { "acs.exe" },
        Game.AcEvo => new() { "AssettoCorsaEVO.exe" },
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
    /// <summary>The car and race data (since 3.23.1, for Analysis): Forza's dash, and since 4.10.0 iRacing's and the
    /// Assetto Corsas' (their cars numbered by <see cref="CarNames.Number"/>); false while there's none.</summary>
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
    /// <summary>Every car in the race, in order (since 3.28.2; iRacing's: Forza sends only your place), or null.</summary>
    public Standings? Field;
    /// <summary>(since 4.10.0) The track, where the game names it (iRacing, the Assetto Corsas, Forza Motorsport (2023)'s
    /// number): its id within the game ("" in Horizon, which doesn't say), its name and a lap's length in metres (0:
    /// not known).</summary>
    public string TrackKey = "", TrackName = "";
    public float TrackLength;
    /// <summary>Where the track is on Earth, metres east and north (iRacing's latitude and longitude, for the Map); NaN:
    /// not known.</summary>
    public float TrackEast = float.NaN, TrackNorth = float.NaN;
    /// <summary>The game runs sessions and counts the laps itself (iRacing, the Assetto Corsas): Analysis follows those
    /// (<see cref="RaceTime"/> is then the time since the car went on track in this session, 0 off it), not Forza's race
    /// clock; <see cref="LapNumber"/> is the laps done in the session.</summary>
    public bool Sessions;
    public SessionKind Session;
    /// <summary>The chequered flag is out; the lap being driven doesn't count (cut; where the game says).</summary>
    public bool Checkered, LapInvalid;
    /// <summary>The session's laps (0: not set, or timed) and how many cars are in it (0: not known).</summary>
    public int SessionLaps, FieldSize;
    /// <summary>The car's class in games that have one (iRacing's, ACC's GT3…), "" in the others.</summary>
    public string CarClassName = "";
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
        o.LapNumber = LapNumber; o.RacePosition = RacePosition; o.Field = Field;
        o.TrackKey = TrackKey; o.TrackName = TrackName; o.TrackLength = TrackLength; o.TrackEast = TrackEast; o.TrackNorth = TrackNorth;
        o.Sessions = Sessions; o.Session = Session; o.Checkered = Checkered; o.LapInvalid = LapInvalid;
        o.SessionLaps = SessionLaps; o.FieldSize = FieldSize; o.CarClassName = CarClassName;
    }
}

/// <summary>One car of <see cref="Standings"/>: its place, number, driver, and how far it is behind the car ahead of it
/// (seconds; NaN: the leader, or not known; <see cref="LapsDown"/> laps when it's a lap or more), yours or not, in the
/// pits or not.</summary>
public sealed record Racer(int Place, string Number, string Name, float Interval, int LapsDown, bool Me, bool InPits);

/// <summary>The field in order, made new each time (so a frame can keep it as it is): in a race by where each car is
/// on track, the gaps the time between one car and the next; outside one (practice, qualifying) by the game's places,
/// the gaps between best laps.</summary>
public sealed record Standings(IReadOnlyList<Racer> Racers, bool Race);

/// <summary>Where telemetry comes from: read on the engine's thread.</summary>
public interface ITelemetrySource : IDisposable
{
    /// <summary>Waits up to <paramref name="timeoutMs"/> for the next frame and fills it; false when none came.</summary>
    bool Read(TelemetryFrame frame, int timeoutMs);

    /// <summary>What's wrong, if anything (the port is taken, say), for the status line.</summary>
    string? Problem { get; }
}
