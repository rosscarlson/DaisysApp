using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// Forza's Data Out (Horizon 4/5 and Motorsport 7/2023): a UDP packet a frame, to the port set in the game's HUD
/// settings. The first 232 bytes ("sled") are the same in all; the "dash" part (speed, pedals, gear) starts at 232,
/// or 244 in Horizon, which puts 12 bytes in between (packets of 324 bytes; Motorsport's are 311 or 331). Horizon 6's
/// documentation names them: CarGroup (U32), SmashableVelDiff and SmashableMass.
/// </summary>
internal sealed class ForzaSource : ITelemetrySource
{
    private readonly Socket? socket;
    private readonly byte[] buffer = new byte[2048];
    private readonly float[] lastTravel = new float[4];
    private uint lastTime;
    private bool hasLast;

    private readonly string game;

    public string? Problem { get; }

    /// <param name="game">"FH" or "FM", before the car's number in its id (the two games number cars differently).</param>
    public ForzaSource(int port, string game = "FH")
    {
        this.game = game;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            socket = null;
            Problem = F("Port {0} is in use by another program", port);
            Logging.Log.Here.Warn($"Telemetry: can't listen on UDP {port}", ex);
        }
    }

    public bool Read(TelemetryFrame f, int timeoutMs)
    {
        if (socket == null) { Thread.Sleep(timeoutMs); return false; }
        if (!socket.Poll(timeoutMs * 1000, SelectMode.SelectRead)) return false;
        int n;
        try { n = socket.Receive(buffer); }
        catch (SocketException) { return false; }
        // the newest only: frames that queued up meanwhile are old
        while (socket.Available > 0)
        {
            try { n = socket.Receive(buffer); } catch (SocketException) { break; }
        }
        if (n < 232) return false;
        return Parse(buffer.AsSpan(0, n), f);
    }

    private bool Parse(ReadOnlySpan<byte> p, TelemetryFrame f)
    {
        f.Live = S32(p, 0) != 0;
        uint time = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
        f.MaxRpm = F32(p, 8);
        f.IdleRpm = F32(p, 12);
        f.Rpm = F32(p, 16);
        const float G = 9.81f;
        f.AccelX = F32(p, 20) / G;
        f.AccelY = F32(p, 24) / G;
        f.AccelZ = F32(p, 28) / G;
        float vx = F32(p, 32), vy = F32(p, 36), vz = F32(p, 40);

        float dt = hasLast && time > lastTime ? (time - lastTime) / 1000f : 0;
        if (dt > 0.25f) dt = 0;
        f.Dt = dt;
        int onKerb = 0;
        float surface = 0, slip = 0, frontRatio = 0;
        for (int w = 0; w < 4; w++)
        {
            float travel = F32(p, 196 + w * 4);
            f.SuspVel[w] = dt > 0 ? (travel - lastTravel[w]) / dt : 0;
            lastTravel[w] = travel;
            bool kerb = S32(p, 116 + w * 4) != 0;
            if (kerb) onKerb++;
            f.KerbAt[w] = kerb ? 1 : 0;
            float rumble = F32(p, 148 + w * 4), combined = F32(p, 180 + w * 4);
            surface += rumble;
            f.OffRoadAt[w] = Math.Clamp(rumble, 0, 1);
            f.SlipAt[w] = Math.Clamp((combined - 1) / 1.5f, 0, 1);
            slip = Math.Max(slip, combined);
            if (w < 2) frontRatio = Math.Min(frontRatio, F32(p, 84 + w * 4));
        }
        lastTime = time;
        hasLast = true;
        int car = S32(p, 212), carClass = S32(p, 216), pi = S32(p, 220);
        f.CarOrdinal = car; f.CarClass = carClass; f.CarPi = pi;
        f.Drivetrain = S32(p, 224); f.Cylinders = S32(p, 228);
        if (car != 0)
        {
            f.CarId = $"{game}:{car}";
            // (its name from the list of the game's cars, for the version being read)
            string? edition = CarNames.Edition is { } e && e.StartsWith(game) ? e : CarNames.Key(game == "FM" ? Game.ForzaMotorsport : Game.ForzaHorizon, null);
            f.Car = $"{CarNames.Name(edition, car) ?? F("Car {0}", car)} ({ClassName(carClass, edition)} {pi})";
        }
        else f.CarId = f.Car = "";
        f.Kerb = Math.Min(1, onKerb / 2f);
        f.OffRoad = Math.Clamp(surface / 4, 0, 1);
        // combined slip is about 1 at the limit of grip
        f.Slip = Math.Clamp((slip - 1) / 1.5f, 0, 1);

        if (p.Length >= 311)
        {
            int d = p.Length >= 324 && p.Length != 331 ? 244 : 232;
            f.SpeedMs = F32(p, d + 12);
            f.Throttle = p[d + 71] / 255f;
            f.Brake = p[d + 72] / 255f;
            f.Clutch = p[d + 73] / 255f;
            f.Handbrake = p[d + 74] / 255f;
            f.Steer = ((sbyte)p[d + 76] + 127) / 254f;
            f.HasRace = true;
            f.CarGroup = d == 244 ? BinaryPrimitives.ReadUInt32LittleEndian(p[232..]) : 0;
            f.PosX = F32(p, d); f.PosY = F32(p, d + 4); f.PosZ = F32(p, d + 8);
            f.Power = F32(p, d + 16); f.Torque = F32(p, d + 20);
            for (int w = 0; w < 4; w++) f.TireTemp[w] = F32(p, d + 24 + w * 4);
            f.Boost = F32(p, d + 40); f.Fuel = F32(p, d + 44); f.Distance = F32(p, d + 48);
            f.BestLap = F32(p, d + 52); f.LastLap = F32(p, d + 56); f.CurrentLap = F32(p, d + 60); f.RaceTime = F32(p, d + 64);
            f.LapNumber = BinaryPrimitives.ReadUInt16LittleEndian(p[(d + 68)..]);
            f.RacePosition = p[d + 70];
            f.Gear = p[d + 75];
            // 0 is reverse; past the tenth, neutral
            f.GearNo = f.Gear == 0 ? -1 : f.Gear > 10 ? 0 : f.Gear;
        }
        else
        {
            f.SpeedMs = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            f.Throttle = f.Brake = f.Clutch = f.Handbrake = 0;
            f.Steer = 0.5f;
            f.Gear = f.GearNo = 0;
            f.HasRace = false;
        }
        // Forza has no ABS flag: a front wheel locking under braking stands in for it
        f.Abs = f.Brake > 0.2f && f.SpeedMs > 3 && frontRatio < -0.12f ? 1 : 0;
        return true;
    }

    /// <summary>Forza's class letters by CarClass.</summary>
    /// <summary>A class's letter. Horizon 6 added R between S2 and X (since 3.24.2 read as 6 = R, 7 = X there; a guess
    /// until a real FH6 says otherwise).</summary>
    internal static string ClassName(int c, string? edition = null) => c switch
    {
        0 => "D", 1 => "C", 2 => "B", 3 => "A", 4 => "S1", 5 => "S2",
        6 => edition == "FH6" ? "R" : "X", 7 when edition == "FH6" => "X", _ => "",
    };

    /// <summary>A game's classes, the fastest first (Analysis' Tracks).</summary>
    internal static int[] Classes(string? edition) => edition == "FH6" ? new[] { 7, 6, 5, 4, 3, 2, 1, 0 } : new[] { 6, 5, 4, 3, 2, 1, 0 };

    private static float F32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadSingleLittleEndian(p[at..]);
    private static int S32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadInt32LittleEndian(p[at..]);

    public void Dispose() => socket?.Dispose();
}
