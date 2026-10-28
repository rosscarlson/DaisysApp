using System.IO;
using System.Text.Json;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>A lap driven: when, where (the track's id), in what (the car, its class, PI and drive) and how fast.</summary>
public sealed class LapRecord
{
    public DateTime At { get; set; }
    /// <summary>Which game: "FH6"… ("FH" before 3.24.0, read as <see cref="LapStore.OldGame"/>).</summary>
    public string Game { get; set; } = LapStore.OldGame;
    public string Track { get; set; } = "";
    public int Car { get; set; }
    public int Class { get; set; }
    public int Pi { get; set; }
    public int Drive { get; set; }
    /// <summary>The lap's number in its race, 1 the first (from a standing start).</summary>
    public int Lap { get; set; }
    /// <summary>Seconds.</summary>
    public float Time { get; set; }
    /// <summary>Metres driven over it.</summary>
    public float Length { get; set; }
}

/// <summary>
/// Every lap recorded, a JSON line each in %APPDATA%\DaisysApp\AnalysisLaps.jsonl (added to as laps come; kept until
/// a track is deleted with its laps).
/// </summary>
public sealed class LapStore
{
    private static readonly string Path = System.IO.Path.Combine(AppPaths.SettingsFolder, "AnalysisLaps.jsonl");
    /// <summary>The game laps and tracks from before 3.24.0 ("FH") were in: Horizon 6, the one out then.</summary>
    public const string OldGame = "FH6";
    private readonly List<LapRecord> laps = new();
    private readonly DateTime started = DateTime.Now;

    public IReadOnlyList<LapRecord> All => laps;

    /// <summary>A lap was added, or some were deleted (on the UI thread).</summary>
    public event Action? Changed;

    public LapStore()
    {
        try
        {
            if (!File.Exists(Path)) return;
            foreach (var line in File.ReadLines(Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<LapRecord>(line) is not { } lap) continue;
                    if (lap.Game == "FH") lap.Game = OldGame;
                    laps.Add(lap);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Analysis: reading the laps", ex); }
    }

    public void Add(LapRecord lap)
    {
        laps.Add(lap);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, JsonSerializer.Serialize(lap) + Environment.NewLine);
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Analysis: saving a lap", ex); }
        Changed?.Invoke();
    }

    /// <summary>A track's laps deleted.</summary>
    public void RemoveTrack(string track)
    {
        if (laps.RemoveAll(l => l.Track == track) == 0) return;
        try { File.WriteAllLines(Path, laps.Select(l => JsonSerializer.Serialize(l))); }
        catch (Exception ex) { Logging.Log.Here.Warn("Analysis: saving the laps", ex); }
        Changed?.Invoke();
    }

    /// <summary>The fastest lap on a track in a car of a class (0: none yet); <paramref name="session"/>: since the app started.</summary>
    public float Best(string? track, int car, int carClass, bool session = false) =>
        track == null ? 0 : laps.Where(l => l.Track == track && l.Car == car && l.Class == carClass && (!session || l.At >= started))
            .Select(l => l.Time).DefaultIfEmpty(0).Min();
}

/// <summary>
/// Follows Forza's race data and records each lap. Forza doesn't say which track a race is on, so a track is known by
/// where its races start (the car's place when the race clock starts) and how long a lap is (the distance driven over
/// the first): a race starting within <see cref="StartRadius"/> of a known track's start, with a first lap as long, is
/// on it; otherwise it's a new track, named by the user (<see cref="NewTrack"/>). Two routes from the same place (common
/// in Horizon) are told apart by their length. Fed the newest frame on the UI thread.
/// </summary>
public sealed class LapTracker
{
    /// <summary>How far apart two starts of the same track can be (cars on a grid start metres apart), metres.</summary>
    public const float StartRadius = 150;
    /// <summary>How much a lap's length can differ on the same track: the share, or this many metres if more.</summary>
    public const float LengthTolerance = 0.06f, LengthSlack = 250;

    private readonly AnalysisSettings settings;
    private readonly LapStore store;
    private string game = LapStore.OldGame;
    private bool inRace;
    private float lastRaceTime, lapStartDistance, startX, startY, startZ;
    private int lastLap, racers;
    private bool madeHere;

    public LapTracker(AnalysisSettings settings, LapStore store)
    {
        this.settings = settings;
        this.store = store;
    }

    /// <summary>The track of the race being driven (null: none, or not known before its first lap ends).</summary>
    public Track? Track { get; private set; }

    /// <summary>A race on a track not seen before finished its first lap: the track was added, to be named.</summary>
    public event Action<Track>? NewTrack;

    /// <summary>The most cars this race has shown (its highest position), or the track's: Position's "of" (0: unknown).</summary>
    public int Racers => Math.Max(racers, Track?.Racers ?? 0);

    /// <summary>The track's laps (the most a race on it had; 0: unknown).</summary>
    public int TotalLaps => Track?.Laps ?? 0;

    /// <summary>Recording (the tool's on): otherwise races are followed, but no lap is kept and no track added.</summary>
    public bool Recording { get; set; }

    /// <param name="game">The game being read ("FH6"…): tracks and laps are its own.</param>
    public void Feed(TelemetryFrame f, string game)
    {
        this.game = game;
        if (!f.HasRace || !f.Live) return;
        float rt = f.RaceTime;
        if (rt <= 0)
        {
            // back in free roam (or the race ended)
            if (inRace) Logging.Log.Here.Info("Analysis: race ended");
            inRace = false;
            Track = null;
            lastRaceTime = 0;
            racers = 0;
            return;
        }
        // a race starts, or starts again (restarted: its clock back near 0)
        if (!inRace || (rt < 0.5f && lastRaceTime > 2))
        {
            inRace = true;
            madeHere = false;
            startX = f.PosX; startY = f.PosY; startZ = f.PosZ;
            lapStartDistance = f.Distance;
            lastLap = f.LapNumber;
            racers = 0;
            var near = Near().ToList();
            Track = near.Count == 1 ? near[0] : null;
            Logging.Log.Here.Info($"Analysis: race started at {startX:0}, {startY:0}, {startZ:0} ({(Track != null ? "on " + settings.TrackName(Track.Id) : near.Count + " tracks start here")})");
        }
        lastRaceTime = rt;
        racers = Math.Max(racers, f.RacePosition);
        if (f.LapNumber < lastLap) { lastLap = f.LapNumber; return; } // rewound
        if (f.LapNumber == lastLap) return;

        // a lap ended: Forza's LastLap is its time
        float length = f.Distance - lapStartDistance;
        lapStartDistance = f.Distance;
        int lapNo = lastLap + 1;
        lastLap = f.LapNumber;
        if (!(f.LastLap > 0) || !Recording) return;
        if (lapNo == 1 || Track == null) Track = Find(length) ?? Add(length);
        else if (madeHere && lapNo == 2)
        {
            Track.Length = length; // (a flying lap measures it better than the first)
            settings.Save();
        }
        bool learnt = false;
        if (lapNo > Track.Laps) { Track.Laps = lapNo; learnt = true; }
        if (racers > Track.Racers) { Track.Racers = racers; learnt = true; }
        if (learnt) settings.Save();
        var lap = new LapRecord
        {
            At = DateTime.Now, Game = game, Track = Track.Id, Car = f.CarOrdinal, Class = f.CarClass, Pi = f.CarPi, Drive = f.Drivetrain,
            Lap = lapNo, Time = f.LastLap, Length = length,
        };
        Logging.Log.Here.Info($"Analysis: lap {lapNo} on {settings.TrackName(Track.Id)} in {settings.CarName(game, f.CarOrdinal)}: {Blocks.LapTime(f.LastLap)}");
        store.Add(lap);
    }

    /// <summary>The tracks whose races start where this one did.</summary>
    private IEnumerable<Track> Near() => settings.Tracks.Where(t =>
    {
        if (t.Game != game) return false;
        float dx = t.X - startX, dy = t.Y - startY, dz = t.Z - startZ;
        return dx * dx + dy * dy + dz * dz <= StartRadius * StartRadius;
    });

    private Track? Find(float length) => Near()
        .Where(t => Math.Abs(t.Length - length) <= Math.Max(t.Length * LengthTolerance, LengthSlack))
        .OrderBy(t => Math.Abs(t.Length - length)).FirstOrDefault();

    private Track Add(float length)
    {
        var t = new Track { Game = game, X = startX, Y = startY, Z = startZ, Length = Math.Max(0, length) };
        settings.Tracks.Add(t);
        settings.Save();
        madeHere = true;
        Logging.Log.Here.Info($"Analysis: a new track ({settings.TrackName(t.Id)}, a lap {length:0} m)");
        NewTrack?.Invoke(t);
        return t;
    }
}
