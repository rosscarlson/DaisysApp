using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// iRacing's telemetry, from the memory it shares while it runs (irsdk: "Local\IRSDKMemMapFileName"): a header, the
/// list of variables (name, type, offset) and up to four buffers of their values, the newest with the highest tick.
/// 60 a second. The redline and idle RPM are in the session's YAML.
/// </summary>
internal sealed partial class IRacingSource : ITelemetrySource
{
    private const string MapName = @"Local\IRSDKMemMapFileName";
    private MemoryMappedFile? map;
    private MemoryMappedViewAccessor? view;
    private DateTime nextOpen;
    private readonly Dictionary<string, int> offsets = new();
    private int lastTick = -1, sessionUpdate = -1, tickRate = 60;
    private float redline = 8000, idle = 800;

    public string? Problem => null;

    public bool Read(TelemetryFrame f, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (Open() && Latest(out int tick, out int buf))
            {
                if (tick != lastTick)
                {
                    float dt = lastTick >= 0 && tick > lastTick ? (tick - lastTick) / (float)tickRate : 0;
                    lastTick = tick;
                    Fill(f, buf, dt > 0.25f ? 0 : dt);
                    return true;
                }
            }
            Thread.Sleep(5);
        }
        return false;
    }

    private bool Open()
    {
        if (view != null)
        {
            // iRacing closed: connected (bit 0 of status) goes off
            if ((view.ReadInt32(4) & 1) != 0) return true;
            Close();
            nextOpen = DateTime.UtcNow.AddSeconds(2);
            return false;
        }
        if (DateTime.UtcNow < nextOpen) return false;
        nextOpen = DateTime.UtcNow.AddSeconds(2);
        try
        {
            map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if ((view.ReadInt32(4) & 1) == 0) { Close(); return false; }
            tickRate = Math.Max(1, view.ReadInt32(8));
            ReadVars();
            Logging.Log.Here.Info($"Shaker: iRacing's telemetry found ({offsets.Count} variables)");
            return true;
        }
        catch (FileNotFoundException) { Close(); return false; }
        catch (Exception ex)
        {
            Logging.Log.Here.Debug(() => "Shaker: iRacing's memory couldn't be read: " + ex.Message);
            Close();
            return false;
        }
    }

    private void ReadVars()
    {
        offsets.Clear();
        int count = view!.ReadInt32(24), at = view.ReadInt32(28);
        var name = new byte[32];
        for (int i = 0; i < count; i++)
        {
            long h = at + i * 144L;
            view.ReadArray(h + 16, name, 0, 32);
            int len = Array.IndexOf(name, (byte)0);
            offsets[Encoding.ASCII.GetString(name, 0, len < 0 ? 32 : len)] = view.ReadInt32(h + 4);
        }
    }

    private bool Latest(out int tick, out int buf)
    {
        tick = -1;
        buf = 0;
        int n = Math.Clamp(view!.ReadInt32(32), 1, 4);
        for (int i = 0; i < n; i++)
        {
            int t = view.ReadInt32(48 + i * 16);
            if (t > tick) { tick = t; buf = view.ReadInt32(48 + i * 16 + 4); }
        }
        return tick >= 0;
    }

    private void Fill(TelemetryFrame f, int buf, float dt)
    {
        var v = view!;
        int update = v.ReadInt32(12);
        if (update != sessionUpdate)
        {
            sessionUpdate = update;
            ReadSession();
        }
        float Float(string name) => offsets.TryGetValue(name, out int o) ? v.ReadSingle(buf + o) : 0;
        int Int(string name) => offsets.TryGetValue(name, out int o) ? v.ReadInt32(buf + o) : 0;
        bool Bool(string name) => offsets.TryGetValue(name, out int o) && v.ReadByte(buf + o) != 0;

        const float G = 9.81f;
        f.Dt = dt;
        f.Live = Bool("IsOnTrack") && !Bool("IsReplayPlaying");
        f.SpeedMs = Float("Speed");
        f.Rpm = Float("RPM");
        f.MaxRpm = redline;
        f.IdleRpm = idle;
        f.Gear = Int("Gear");
        f.Throttle = Float("Throttle");
        f.Brake = Float("Brake");
        f.AccelX = -Float("LatAccel") / G;
        f.AccelY = Float("VertAccel") / G;
        f.AccelZ = Float("LongAccel") / G;
        f.SuspVel[0] = Float("LFshockVel");
        f.SuspVel[1] = Float("RFshockVel");
        f.SuspVel[2] = Float("LRshockVel");
        f.SuspVel[3] = Float("RRshockVel");
        // irsdk_TrkSurf: 11-14 rumble strips, 15-27 grass, dirt, sand, gravel, grasscrete, astroturf
        int surface = Int("PlayerTrackSurfaceMaterial");
        f.Kerb = surface is >= 11 and <= 14 ? 1 : 0;
        f.OffRoad = surface is >= 15 and <= 27 ? 1 : 0;
        f.Slip = 0;
        f.Abs = Bool("BrakeABSactive") ? 1 : 0;
    }

    private void ReadSession()
    {
        try
        {
            var v = view!;
            int len = v.ReadInt32(16), at = v.ReadInt32(20);
            if (len <= 0) return;
            var bytes = new byte[len];
            v.ReadArray(at, bytes, 0, len);
            string yaml = Encoding.Latin1.GetString(bytes);
            if (Redline().Match(yaml) is { Success: true } r && float.TryParse(r.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out float red) && red > 1000) redline = red;
            if (Idle().Match(yaml) is { Success: true } i && float.TryParse(i.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out float id) && id > 100) idle = id;
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => "Shaker: iRacing's session info: " + ex.Message); }
    }

    [GeneratedRegex(@"DriverCarRedLine:\s*([0-9.]+)")]
    private static partial Regex Redline();

    [GeneratedRegex(@"DriverCarIdleRPM:\s*([0-9.]+)")]
    private static partial Regex Idle();

    private void Close()
    {
        view?.Dispose();
        map?.Dispose();
        view = null;
        map = null;
        lastTick = -1;
        sessionUpdate = -1;
    }

    public void Dispose() => Close();
}
