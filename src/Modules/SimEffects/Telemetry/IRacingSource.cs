using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// iRacing's telemetry, from the memory it shares while it runs (irsdk: "Local\IRSDKMemMapFileName"): a header, the
/// list of variables (name, type, offset) and up to four buffers of their values, the newest with the highest tick.
/// 60 a second. The redline and idle RPM are in the session's YAML; so (since 3.28.2) are the drivers and the
/// sessions, which with the CarIdx arrays make the field's order and gaps (<see cref="Standings"/>), 4 times a second.
/// </summary>
internal sealed partial class IRacingSource : ITelemetrySource
{
    private const string MapName = @"Local\IRSDKMemMapFileName";
    private MemoryMappedFile? map;
    private MemoryMappedViewAccessor? view;
    private DateTime nextOpen;
    private readonly Dictionary<string, int> offsets = new();
    private int lastTick = -1, sessionUpdate = -1, tickRate = 60;
    private float redline = 8000, idle = 800, first, shift, blink;
    private string carId = "", car = "";

    /// <summary>A driver in the session's YAML: car number, name, and their class's estimated lap (seconds).</summary>
    private sealed record Driver(string Number, string Name, float EstLap);
    private Dictionary<int, Driver> drivers = new();
    private Dictionary<int, string> sessionTypes = new();
    private int playerIdx = -1;
    private Standings? field;
    private int fieldTick = int.MinValue, fieldSession = -1;
    private const int Cars = 64;
    // each car's lap and place on it at the last field (to mend a lap count a moment late at the line)
    private readonly int[] lastLap = new int[Cars];
    private readonly float[] lastPct = new float[Cars];

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
            Logging.Log.Here.Info($"Telemetry: iRacing's telemetry found ({offsets.Count} variables)");
            return true;
        }
        catch (FileNotFoundException) { Close(); return false; }
        catch (Exception ex)
        {
            Logging.Log.Here.Debug(() => "Telemetry: iRacing's memory couldn't be read: " + ex.Message);
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
        f.Gear = f.GearNo = Int("Gear");
        f.CarId = carId;
        f.Car = car;
        f.FirstRpm = first;
        f.ShiftRpm = shift;
        f.BlinkRpm = blink;
        f.Throttle = Float("Throttle");
        f.Brake = Float("Brake");
        // iRacing's clutch is how engaged it is: 1 with the pedal up
        f.Clutch = offsets.ContainsKey("Clutch") ? 1 - Float("Clutch") : 0;
        f.Handbrake = Float("HandbrakeRaw");
        // the wheel's angle in radians, + to the left, of its lock (SteeringWheelAngleMax)
        float lockRad = Float("SteeringWheelAngleMax");
        f.Steer = lockRad > 0.1f ? Math.Clamp(0.5f - Float("SteeringWheelAngle") / (2 * lockRad), 0, 1) : 0.5f;
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
        f.SameOnEveryWheel();
        if ((long)lastTick - fieldTick >= tickRate / 4 || lastTick < fieldTick)
        {
            fieldTick = lastTick;
            field = Field(v, buf);
        }
        f.Field = field;
    }

    /// <summary>The field now. In a race once it's started (SessionState 4 racing, 5 checkered, 6 cool down): ordered by
    /// laps done + the place on the lap, each gap the laps between the two cars at the class's estimated lap plus the
    /// difference of CarIdxEstTime (iRacing's estimate of how long a car takes from the line to where it is). Before
    /// the start and outside a race: CarIdxPosition's order, the gaps between best laps.</summary>
    private Standings? Field(MemoryMappedViewAccessor v, int buf)
    {
        if (drivers.Count == 0 || !offsets.ContainsKey("CarIdxPosition")) return null;
        int Int(string name) => offsets.TryGetValue(name, out int o) ? v.ReadInt32(buf + o) : 0;
        int Ints(string name, int i) => offsets.TryGetValue(name, out int o) ? v.ReadInt32(buf + o + i * 4) : 0;
        float Floats(string name, int i) => offsets.TryGetValue(name, out int o) ? v.ReadSingle(buf + o + i * 4) : 0;
        bool Bools(string name, int i) => offsets.TryGetValue(name, out int o) && v.ReadByte(buf + o + i) != 0;

        int session = Int("SessionNum"), state = Int("SessionState");
        bool race = state >= 4 && sessionTypes.TryGetValue(session, out var type) && type.Contains("Race", StringComparison.OrdinalIgnoreCase);
        if (session != fieldSession)
        {
            fieldSession = session;
            Array.Fill(lastLap, int.MinValue);
        }
        int me = offsets.ContainsKey("PlayerCarIdx") ? Int("PlayerCarIdx") : playerIdx;
        var cars = new List<(int Idx, Driver D, int Pos, int Lap, float Pct, float Est, float Best, bool Pits)>();
        foreach (var (idx, d) in drivers)
        {
            if (idx is < 0 or >= Cars) continue;
            int pos = Ints("CarIdxPosition", idx), lap = Ints("CarIdxLapCompleted", idx);
            float pct = Floats("CarIdxLapDistPct", idx);
            // (not in the world, and no place: not racing, or gone)
            if (pos <= 0 && pct < 0) continue;
            if (pct >= 0)
            {
                // the line crossed but the lap count not moved on yet (or moved on before the place on the lap wrapped)
                int seen = lap;
                if (lastLap[idx] != int.MinValue)
                {
                    if (pct < 0.05f && lastPct[idx] > 0.95f && lap == lastLap[idx]) lap++;
                    else if (pct > 0.95f && lastPct[idx] < 0.05f && lap > lastLap[idx]) lap--;
                }
                lastLap[idx] = seen;
                lastPct[idx] = pct;
            }
            cars.Add((idx, d, pos, lap, pct, Floats("CarIdxEstTime", idx), Floats("CarIdxBestLapTime", idx), Bools("CarIdxOnPitRoad", idx)));
        }
        if (cars.Count == 0) return null;
        // (a car with no place, or not on track, after the rest)
        if (race) cars = cars.OrderByDescending(c => c.Pct >= 0 ? c.Lap + c.Pct : float.MinValue).ThenBy(c => c.Pos > 0 ? c.Pos : int.MaxValue).ToList();
        else cars = cars.OrderBy(c => c.Pos > 0 ? c.Pos : int.MaxValue).ThenBy(c => c.Idx).ToList();
        float myLap = drivers.TryGetValue(me, out var mine) ? mine.EstLap : 0;
        var racers = new List<Racer>(cars.Count);
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            float gap = float.NaN;
            int down = 0;
            if (i > 0)
            {
                var a = cars[i - 1];
                if (race && a.Pct >= 0 && c.Pct >= 0)
                {
                    float lapTime = c.D.EstLap > 0 ? c.D.EstLap : myLap;
                    float behind = (a.Lap + a.Pct) - (c.Lap + c.Pct);
                    down = (int)Math.Floor(behind);
                    if (a.Est > 0 || c.Est > 0) gap = Math.Max(0, (a.Lap - c.Lap) * lapTime + (a.Est - c.Est));
                    else if (lapTime > 0) gap = behind * lapTime;
                }
                else if (!race && a.Best > 0 && c.Best > 0) gap = Math.Max(0, c.Best - a.Best);
            }
            racers.Add(new Racer(i + 1, c.D.Number, c.D.Name, gap, down, c.Idx == me, c.Pits));
        }
        return new Standings(racers, race);
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
            first = Number(yaml, "DriverCarSLFirstRPM");
            shift = Number(yaml, "DriverCarSLShiftRPM");
            blink = Number(yaml, "DriverCarSLBlinkRPM");
            ReadField(yaml);
            // the player's car: its entry in Drivers, by DriverCarIdx
            if (Regex.Match(yaml, @"DriverCarIdx:\s*(\d+)") is { Success: true } idx
                && Regex.Match(yaml, @"- CarIdx:\s*" + idx.Groups[1].Value + @"\s*$", RegexOptions.Multiline) is { Success: true } entry)
            {
                // that driver's lines, up to the next one's
                int end = yaml.IndexOf("- CarIdx:", entry.Index + entry.Length, StringComparison.Ordinal);
                string block = yaml[entry.Index..(end < 0 ? yaml.Length : end)];
                string path = Regex.Match(block, @"CarPath:\s*(\S+)").Groups[1].Value;
                string name = Regex.Match(block, @"CarScreenName:\s*(.+)").Groups[1].Value.Trim();
                if (path.Length > 0)
                {
                    carId = "iRacing:" + path;
                    car = name.Length > 0 ? name : path;
                }
            }
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => "Telemetry: iRacing's session info: " + ex.Message); }
    }

    /// <summary>The sessions' types ("Practice", "Race"…) and the drivers (but the pace car and spectators), for the
    /// field.</summary>
    private void ReadField(string yaml)
    {
        var types = new Dictionary<int, string>();
        foreach (var (key, block) in Entries(yaml, "SessionNum"))
            types[key] = Regex.Match(block, @"SessionType:\s*(.+)").Groups[1].Value.Trim();
        var list = new Dictionary<int, Driver>();
        foreach (var (key, block) in Entries(yaml, "CarIdx"))
        {
            string Text(string k) => Regex.Match(block, k + @":[ \t]*(.*)") is { Success: true } m ? m.Groups[1].Value.Trim().Trim('"') : "";
            if (Text("CarIsPaceCar") == "1" || Text("IsSpectator") == "1") continue;
            float.TryParse(Text("CarClassEstLapTime"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float est);
            string name = Text("UserName");
            list[key] = new Driver(Text("CarNumber"), name.Length > 0 ? name : Text("TeamName"), est);
        }
        if (Regex.Match(yaml, @"DriverCarIdx:\s*(\d+)") is { Success: true } me) playerIdx = int.Parse(me.Groups[1].Value);
        sessionTypes = types;
        drivers = list;
        fieldTick = int.MinValue;
    }

    /// <summary>A YAML list's entries starting "- key: n": n and the entry's lines, up to the next one's.</summary>
    private static IEnumerable<(int Key, string Block)> Entries(string yaml, string key)
    {
        var starts = Regex.Matches(yaml, @"^[ \t]*- " + key + @":[ \t]*(\d+)[ \t]*$", RegexOptions.Multiline);
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1].Index : yaml.Length;
            yield return (int.Parse(starts[i].Groups[1].Value), yaml[starts[i].Index..end]);
        }
    }

    private static float Number(string yaml, string key) =>
        Regex.Match(yaml, key + @":\s*([0-9.]+)") is { Success: true } m
        && float.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 100 ? v : 0;

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
        field = null;
        drivers = new();
        fieldTick = int.MinValue;
        fieldSession = -1;
    }

    public void Dispose() => Close();
}
