using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// The Assetto Corsas' telemetry, from the memory they share while they run: Assetto Corsa Competizione's and (since
/// 4.10.0) Assetto Corsa's "Local\acpmf_physics" (the car, updated hundreds of times a second), "Local\acpmf_graphics"
/// (whether you're driving, the laps, the place) and "Local\acpmf_static" (the car and track), and AC EVO's
/// "Local\acevo_pmf_…" pages. Every field of the physics page is 4 bytes, so a field's offset is its place × 4
/// (SPageFilePhysics in their shared memory documentation; EVO keeps the same physics page and has a graphics and
/// static page of its own, SPageFileGraphicEvo and SPageFileStaticEvo, offsets worked out from Kunos' header at
/// pack(4)). ACC also gives its own vibration amounts (kerbs, slip, ABS), which are used as they are.
/// </summary>
internal sealed class AccSource : ITelemetrySource
{
    /// <summary>Which of them: their pages' names and graphics and static pages differ.</summary>
    public enum Kind { Acc, Ac, Evo }

    private readonly Kind kind;
    private MemoryMappedFile? physicsMap, graphicsMap, staticMap;
    private MemoryMappedViewAccessor? physics, graphics, statics;
    private string car = "", trackKey = "", trackName = "";
    private float trackLength, maxFuel;
    private int fieldSize;
    private DateTime nextOpen, nextStatic;
    private int lastPacket = -1;
    private DateTime lastAt;
    private readonly float[] lastTravel = new float[4];
    private readonly Stint stint = new();
    // a new session each time the laps done go back (AC and ACC have no session number)
    private int sessionNo, lastLaps;

    public AccSource(Kind kind = Kind.Acc) => this.kind = kind;

    public string? Problem => null;

    // field places in SPageFilePhysics (the same in all three)
    private const int Gas = 1, BrakeAt = 2, FuelAt = 3, GearAt = 4, Rpms = 5, SteerAngle = 6, SpeedKmh = 7, AccG = 11, TyreCoreTemp = 38,
        SuspensionTravel = 46, TyresOut = 61, TurboBoost = 69, ClutchAt = 91, CurrentMaxRpm = 147, AbsInAction = 169,
        KerbVibration = 196, SlipVibrations = 197;

    private string Prefix => kind == Kind.Evo ? @"Local\acevo_pmf_" : @"Local\acpmf_";
    private string Name => kind switch { Kind.Evo => "AC EVO", Kind.Ac => "Assetto Corsa", _ => "ACC" };
    private string IdPrefix => kind switch { Kind.Evo => "ACEvo:", Kind.Ac => "AC:", _ => "ACC:" };

    public bool Read(TelemetryFrame f, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (Open())
            {
                int packet = physics!.ReadInt32(0);
                if (packet != lastPacket)
                {
                    var now = DateTime.UtcNow;
                    float dt = lastPacket >= 0 ? (float)(now - lastAt).TotalSeconds : 0;
                    lastPacket = packet;
                    lastAt = now;
                    Fill(f, dt > 0.25f ? 0 : dt);
                    return true;
                }
            }
            Thread.Sleep(5);
        }
        return false;
    }

    private bool Open()
    {
        if (physics != null) return true;
        if (DateTime.UtcNow < nextOpen) return false;
        nextOpen = DateTime.UtcNow.AddSeconds(2);
        try
        {
            physicsMap = MemoryMappedFile.OpenExisting(Prefix + "physics", MemoryMappedFileRights.Read);
            physics = physicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            graphicsMap = MemoryMappedFile.OpenExisting(Prefix + "graphics", MemoryMappedFileRights.Read);
            graphics = graphicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (physics.Capacity < (SlipVibrations + 1) * 4) throw new InvalidDataException("physics page too small: " + physics.Capacity);
            try
            {
                staticMap = MemoryMappedFile.OpenExisting(Prefix + "static", MemoryMappedFileRights.Read);
                statics = staticMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }
            catch (FileNotFoundException) { }
            Logging.Log.Here.Info($"Telemetry: {Name}'s telemetry found");
            return true;
        }
        catch (FileNotFoundException) { Close(); return false; }
        catch (Exception ex)
        {
            Logging.Log.Here.Debug(() => $"Telemetry: {Name}'s memory couldn't be read: " + ex.Message);
            Close();
            return false;
        }
    }

    private void Fill(TelemetryFrame f, float dt)
    {
        var p = physics!;
        var g = graphics!;
        float Float(int place) => p.ReadSingle(place * 4);
        int Int(int place) => p.ReadInt32(place * 4);
        // (a flag that's an int in some versions and a float in others)
        bool Flag(int place)
        {
            int raw = Int(place);
            return raw == 1 || BitConverter.Int32BitsToSingle(raw) > 0.5f;
        }

        // status: 0 off, 1 replay, 2 live, 3 paused
        int status = g.ReadInt32(4);
        f.Live = status == 2;
        f.Dt = dt;
        f.Throttle = Float(Gas);
        f.Brake = Float(BrakeAt);
        f.Clutch = Math.Clamp(Float(ClutchAt), 0, 1);
        f.Handbrake = 0;
        // -1 full left to 1 full right
        f.Steer = Math.Clamp((Float(SteerAngle) + 1) / 2, 0, 1);
        f.Gear = Int(GearAt);
        // 0 reverse, 1 neutral, 2 first
        f.GearNo = f.Gear - 1;
        if (DateTime.UtcNow >= nextStatic) ReadStatic();
        f.Rpm = Int(Rpms);
        f.MaxRpm = Math.Max(Int(CurrentMaxRpm), 2000);
        f.IdleRpm = f.MaxRpm * 0.12f;
        f.SpeedMs = Float(SpeedKmh) / 3.6f;
        f.AccelX = Float(AccG);
        f.AccelY = Float(AccG + 1);
        f.AccelZ = Float(AccG + 2);
        for (int w = 0; w < 4; w++)
        {
            float travel = Float(SuspensionTravel + w);
            f.SuspVel[w] = dt > 0 ? (travel - lastTravel[w]) / dt : 0;
            lastTravel[w] = travel;
            // (°C; the frame's are °F)
            f.TireTemp[w] = Float(TyreCoreTemp + w) * 1.8f + 32;
        }
        // (Assetto Corsa's page ends before the vibration amounts)
        f.Kerb = kind == Kind.Ac ? 0 : Math.Clamp(Float(KerbVibration) * 2, 0, 1);
        f.OffRoad = Math.Clamp(Int(TyresOut) / 4f, 0, 1);
        f.Slip = kind == Kind.Ac ? 0 : Math.Clamp(Float(SlipVibrations) * 4, 0, 1);
        f.Abs = Flag(AbsInAction) ? 1 : 0;
        f.SameOnEveryWheel();
        // (bar)
        f.Boost = Float(TurboBoost) * 14.5038f;
        if (kind == Kind.Evo) RaceEvo(f, g, status); else RaceAc(f, g, status);
    }

    /// <summary>Assetto Corsa's and ACC's race data: SPageFileGraphic's laps, times, place and flag, and the car's place
    /// in the world (AC: the player's; ACC: every car's, the player's found by its id).</summary>
    private void RaceAc(TelemetryFrame f, MemoryMappedViewAccessor g, int status)
    {
        f.CarId = car.Length > 0 ? IdPrefix + car : "";
        f.Car = car.Length > 0 ? CarNames.Pretty(car) : "";
        int session = g.ReadInt32(8), laps = g.ReadInt32(132);
        // (AC: 0 practice, 1 qualify, 2 race, 3 hotlap, 4 time attack, 5 drift, 6 drag; ACC adds 7 hotstint, 8 superpole)
        f.Session = session switch { 2 => SessionKind.Race, 1 or 8 => SessionKind.Qualifying, >= 0 => SessionKind.Practice, _ => SessionKind.None };
        if (status == 0 || laps < lastLaps) sessionNo++;
        lastLaps = laps;
        f.LapNumber = laps;
        f.RacePosition = g.ReadInt32(136);
        f.CurrentLap = Seconds(g.ReadInt32(140));
        f.LastLap = Seconds(g.ReadInt32(144));
        f.BestLap = Seconds(g.ReadInt32(148));
        f.SessionLaps = Math.Max(0, g.ReadInt32(172));
        bool inPit = g.ReadInt32(160) != 0;
        int flag;
        if (kind == Kind.Ac)
        {
            f.PosX = g.ReadSingle(252); f.PosY = g.ReadSingle(256); f.PosZ = g.ReadSingle(260);
            flag = g.ReadInt32(268);
            f.FieldSize = fieldSize;
            f.LapInvalid = false;
        }
        else
        {
            int active = Math.Clamp(g.ReadInt32(252), 0, 60), me = g.ReadInt32(1216), at = 0;
            for (int i = 0; i < active; i++) if (g.ReadInt32(976 + i * 4) == me) { at = i; break; }
            f.PosX = g.ReadSingle(256 + at * 12); f.PosY = g.ReadSingle(260 + at * 12); f.PosZ = g.ReadSingle(264 + at * 12);
            flag = g.ReadInt32(1224);
            f.FieldSize = active;
            f.LapInvalid = g.ReadInt32(1408) == 0;
            // (and its session's number)
            session = session * 100 + g.ReadInt32(1320);
        }
        // (5: the chequered flag)
        f.Checkered = flag == 5;
        f.Fuel = maxFuel > 0 ? Math.Clamp(physics!.ReadSingle(FuelAt * 4) / maxFuel, 0, 1) : 0;
        f.Power = f.Torque = 0;
        Common(f, $"{session}|{sessionNo}", status, inPit);
    }

    /// <summary>AC EVO's race data, from its own graphics page (SPageFileGraphicEvo).</summary>
    private void RaceEvo(TelemetryFrame f, MemoryMappedViewAccessor g, int status)
    {
        // (static: -1 unknown, 0 time attack, 1 race, 2 hot stint, 3 cruise)
        int session = statics?.ReadInt32(32) ?? -1;
        f.Session = session switch { 1 => SessionKind.Race, >= 0 => SessionKind.Practice, _ => SessionKind.None };
        string model = Text(g, 3086, 33, wide: false);
        if (model.Length > 0) car = model;
        f.CarId = car.Length > 0 ? IdPrefix + car : "";
        f.Car = car.Length > 0 ? CarNames.Pretty(car) : "";
        // (the lap, 1 the first, in its session state; else counted at the line from npos)
        int lap = g.ReadInt32(2476 + 72), counted = stint.Laps(g.ReadSingle(1244));
        f.LapNumber = lap > 0 ? lap - 1 : counted;
        f.SessionLaps = Math.Max(0, g.ReadInt32(2476 + 68));
        f.RacePosition = (int)Math.Min(g.ReadUInt32(2388), 99);
        f.FieldSize = (int)Math.Min(g.ReadUInt32(2392), 99);
        f.CurrentLap = Seconds(g.ReadInt32(188));
        f.LastLap = Seconds(g.ReadInt32(2396));
        f.BestLap = Seconds(g.ReadInt32(2400));
        // (8: the chequered flag; the timing's is_invalid)
        f.Checkered = g.ReadInt32(2404) == 8 || g.ReadInt32(2408) == 8;
        f.LapInvalid = g.ReadByte(2732 + 117) != 0;
        // the player's car among car_ids (two 64-bit halves each), for car_coordinates
        ulong a = g.ReadUInt64(24), b = g.ReadUInt64(32);
        int at = 0;
        if (a != 0 || b != 0)
            for (int i = 0; i < 60; i++)
                if (g.ReadUInt64(3940 + i * 16) == a && g.ReadUInt64(3948 + i * 16) == b) { at = i; break; }
        f.PosX = g.ReadSingle(3124 + at * 12); f.PosY = g.ReadSingle(3128 + at * 12); f.PosZ = g.ReadSingle(3132 + at * 12);
        float fuel = g.ReadSingle(200);
        f.Fuel = Math.Clamp(fuel > 1 ? fuel / 100 : fuel, 0, 1);
        f.Torque = g.ReadSingle(212);
        f.Power = g.ReadInt32(216) * 745.7f;
        bool inPit = g.ReadByte(3119) != 0;
        Common(f, $"{session}|{statics?.ReadByte(69)}|{statics?.ReadByte(70)}", status, inPit);
    }

    /// <summary>What all of them fill the same: the track, the car's number and class, and the run's clock.</summary>
    private void Common(TelemetryFrame f, string session, int status, bool inPit)
    {
        f.HasRace = status is 2 or 3 && trackKey.Length > 0;
        f.Sessions = true;
        f.TrackKey = trackKey;
        f.TrackName = trackName;
        f.TrackLength = trackLength;
        f.TrackEast = f.TrackNorth = float.NaN;
        f.CarClassName = kind == Kind.Acc ? AccClass(car) : "";
        f.CarOrdinal = CarNames.Number(f.CarId);
        f.CarClass = CarNames.Number(f.CarClassName);
        f.CarPi = f.Drivetrain = f.Cylinders = 0;
        f.CarGroup = 0;
        f.Field = null;
        f.Nearby = null;
        // (on track while live or paused; back in the pits outside a race starts another run)
        bool onTrack = status is 2 or 3 && !(inPit && f.Session != SessionKind.Race);
        stint.Step(f, $"{session}|{trackKey}|{car}", onTrack);
    }

    /// <summary>The static page: the car (AC, ACC), the track and its layout, its length, the most cars and the fuel
    /// tank. It's written as a session starts, so it's read again every second.</summary>
    private void ReadStatic()
    {
        nextStatic = DateTime.UtcNow.AddSeconds(1);
        if (statics == null) return;
        string track, layout;
        if (kind == Kind.Evo)
        {
            track = Text(statics, 136, 33, wide: false);
            layout = Text(statics, 169, 33, wide: false);
            trackLength = statics.ReadSingle(204);
        }
        else
        {
            // carModel (wchar[33]) at 68, track at 134, numCars at 64, maxFuel at 416; AC: trackSPlineLength and
            // trackConfiguration at 520 and 524 (none in ACC)
            car = Text(statics, 68, 33, wide: true);
            track = Text(statics, 134, 33, wide: true);
            fieldSize = statics.ReadInt32(64);
            maxFuel = statics.ReadSingle(416);
            layout = kind == Kind.Ac ? Text(statics, 524, 33, wide: true) : "";
            trackLength = kind == Kind.Ac ? statics.ReadSingle(520) : 0;
        }
        if (!(trackLength is > 100 and < 100_000)) trackLength = 0;
        trackKey = track.Length == 0 ? "" : layout.Length > 0 ? $"{track}|{layout}" : track;
        trackName = track.Length == 0 ? "" : CarNames.Pretty(track) + (layout.Length > 0 ? $" ({CarNames.Pretty(layout)})" : "");
    }

    /// <summary>A string of a page: wide (UTF-16: AC and ACC) or bytes (EVO), up to its first 0.</summary>
    private static string Text(MemoryMappedViewAccessor v, long at, int length, bool wide)
    {
        if (wide)
        {
            var chars = new char[length];
            v.ReadArray(at, chars, 0, length);
            int end = Array.IndexOf(chars, (char)0);
            return new string(chars, 0, end < 0 ? length : end).Trim();
        }
        var bytes = new byte[length];
        v.ReadArray(at, bytes, 0, length);
        int stop = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, stop < 0 ? length : stop).Trim();
    }

    private static float Seconds(int ms) => ms > 0 && ms < int.MaxValue ? ms / 1000f : 0;

    /// <summary>ACC's class of a car, from its model's name ("bmw_m4_gt4": GT4).</summary>
    private static string AccClass(string model)
    {
        string m = model.ToLowerInvariant();
        if (m.Length == 0) return "";
        if (m.Contains("gt4")) return "GT4";
        if (m.Contains("gt2")) return "GT2";
        if (m.Contains("cup")) return "Cup";
        if (m.Contains("challenge")) return "CHL";
        if (m.Contains("huracan_st")) return "ST";
        if (m.Contains("m2_cs")) return "TCX";
        return "GT3";
    }

    private void Close()
    {
        statics?.Dispose();
        staticMap?.Dispose();
        statics = null;
        staticMap = null;
        car = trackKey = trackName = "";
        physics?.Dispose();
        graphics?.Dispose();
        physicsMap?.Dispose();
        graphicsMap?.Dispose();
        physics = graphics = null;
        physicsMap = graphicsMap = null;
        lastPacket = -1;
    }

    public void Dispose() => Close();
}
