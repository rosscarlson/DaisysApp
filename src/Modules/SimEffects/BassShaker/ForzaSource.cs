using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// Forza's Data Out (Horizon 4/5 and Motorsport 7/2023): a UDP packet a frame, to the port set in the game's HUD
/// settings. The first 232 bytes ("sled") are the same in all; the "dash" part (speed, pedals, gear) starts at 232,
/// or 244 in Horizon, which puts 12 bytes in between (packets of 324 bytes; Motorsport's are 311 or 331).
/// </summary>
internal sealed class ForzaSource : ITelemetrySource
{
    private readonly Socket? socket;
    private readonly byte[] buffer = new byte[2048];
    private readonly float[] lastTravel = new float[4];
    private uint lastTime;
    private bool hasLast;

    public string? Problem { get; }

    public ForzaSource(int port)
    {
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
            Logging.Log.Here.Warn($"Shaker: can't listen on UDP {port}", ex);
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
            if (S32(p, 116 + w * 4) != 0) onKerb++;
            surface += F32(p, 148 + w * 4);
            slip = Math.Max(slip, F32(p, 180 + w * 4));
            if (w < 2) frontRatio = Math.Min(frontRatio, F32(p, 84 + w * 4));
        }
        lastTime = time;
        hasLast = true;
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
            f.Gear = p[d + 75];
        }
        else
        {
            f.SpeedMs = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            f.Throttle = f.Brake = 0;
            f.Gear = 0;
        }
        // Forza has no ABS flag: a front wheel locking under braking stands in for it
        f.Abs = f.Brake > 0.2f && f.SpeedMs > 3 && frontRatio < -0.12f ? 1 : 0;
        return true;
    }

    private static float F32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadSingleLittleEndian(p[at..]);
    private static int S32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadInt32LittleEndian(p[at..]);

    public void Dispose() => socket?.Dispose();
}
