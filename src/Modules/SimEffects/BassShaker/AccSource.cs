using System.IO;
using System.IO.MemoryMappedFiles;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// Assetto Corsa Competizione's telemetry, from the memory it shares while it runs: "Local\acpmf_physics" (the car,
/// updated hundreds of times a second) and "Local\acpmf_graphics" (whether you're driving). Every field of the physics
/// page is 4 bytes, so a field's offset is its place × 4 (SPageFilePhysics in ACC's shared memory documentation).
/// ACC also gives its own vibration amounts (kerbs, slip, ABS), which are used as they are.
/// </summary>
internal sealed class AccSource : ITelemetrySource
{
    private MemoryMappedFile? physicsMap, graphicsMap;
    private MemoryMappedViewAccessor? physics, graphics;
    private DateTime nextOpen;
    private int lastPacket = -1;
    private DateTime lastAt;
    private readonly float[] lastTravel = new float[4];

    public string? Problem => null;

    // field places in SPageFilePhysics
    private const int Gas = 1, BrakeAt = 2, GearAt = 4, Rpms = 5, SpeedKmh = 7, AccG = 11, SuspensionTravel = 46,
        TyresOut = 61, CurrentMaxRpm = 147, AbsInAction = 169, KerbVibration = 196, SlipVibrations = 197;

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
            physicsMap = MemoryMappedFile.OpenExisting(@"Local\acpmf_physics", MemoryMappedFileRights.Read);
            physics = physicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            graphicsMap = MemoryMappedFile.OpenExisting(@"Local\acpmf_graphics", MemoryMappedFileRights.Read);
            graphics = graphicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (physics.Capacity < (SlipVibrations + 1) * 4) throw new InvalidDataException("physics page too small: " + physics.Capacity);
            Logging.Log.Here.Info("Shaker: ACC's telemetry found");
            return true;
        }
        catch (FileNotFoundException) { Close(); return false; }
        catch (Exception ex)
        {
            Logging.Log.Here.Debug(() => "Shaker: ACC's memory couldn't be read: " + ex.Message);
            Close();
            return false;
        }
    }

    private void Fill(TelemetryFrame f, float dt)
    {
        var p = physics!;
        float Float(int place) => p.ReadSingle(place * 4);
        int Int(int place) => p.ReadInt32(place * 4);
        // (a flag that's an int in some versions and a float in others)
        bool Flag(int place)
        {
            int raw = Int(place);
            return raw == 1 || BitConverter.Int32BitsToSingle(raw) > 0.5f;
        }

        // status: 0 off, 1 replay, 2 live, 3 paused
        f.Live = graphics!.ReadInt32(4) == 2;
        f.Dt = dt;
        f.Throttle = Float(Gas);
        f.Brake = Float(BrakeAt);
        f.Gear = Int(GearAt);
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
        }
        f.Kerb = Math.Clamp(Float(KerbVibration) * 2, 0, 1);
        f.OffRoad = Math.Clamp(Int(TyresOut) / 4f, 0, 1);
        f.Slip = Math.Clamp(Float(SlipVibrations) * 4, 0, 1);
        f.Abs = Flag(AbsInAction) ? 1 : 0;
        f.SameOnEveryWheel();
    }

    private void Close()
    {
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
