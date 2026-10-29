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

    /// <summary>The fastest lap on a track (0: none yet), in a car and / or of a class (null: any);
    /// <paramref name="session"/>: since the app started.</summary>
    public float Best(string? track, int? car, int? carClass, bool session = false) =>
        track == null ? 0 : laps.Where(l => l.Track == track && (car == null || l.Car == car) && (carClass == null || l.Class == carClass) && (!session || l.At >= started))
            .Select(l => l.Time).DefaultIfEmpty(0).Min();
}

/// <summary>
/// Follows Forza's race data and records each lap. Forza doesn't say which track a race is on, so a track is known by
/// where its races start (the car's place when the race clock starts) and how long a lap is (the distance driven over
/// the first): a race starting within <see cref="StartRadius"/> of a known track's start, with a first lap as long, is
/// on it; otherwise it's a new track, named by the user (<see cref="NewTrack"/>). Two routes from the same place (common
/// in Horizon) are told apart by their length. A sprint (point to point, since 3.24.5) ends no lap: its finish is the
/// race clock stopping while the car still sends (or LastLap set to the race's time), recorded as lap 1. A place named
/// by the user (<see cref="NameHere"/>: a track with no length yet) is the track of a race starting within
/// <see cref="PlaceRadius"/> of it. Fed the newest frame on the UI thread.
/// </summary>
public sealed class LapTracker
{
    /// <summary>How far apart two starts of the same track can be (cars on a grid start metres apart), metres.</summary>
    public const float StartRadius = 150;
    /// <summary>How much a lap's length can differ on the same track: the share, or this many metres if more.</summary>
    public const float LengthTolerance = 0.06f, LengthSlack = 250;
    /// <summary>How far from a named place (where the event's started) its race can start, metres: the nearest wins.</summary>
    public const float PlaceRadius = 300;
    /// <summary>How long the race clock has to stand still, while the car sends, for a sprint to have finished.</summary>
    private const long FinishMs = 1000;

    private readonly AnalysisSettings settings;
    private readonly LapStore store;
    private string game = LapStore.OldGame;
    private bool inRace;
    private float lastRaceTime, lapStartDistance, startX, startY, startZ;
    private int lastLap, racers, lapsEnded;
    private bool madeHere, finished;
    private float startLastLap;
    private long stillSince;

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
            if (inRace) Logging.Log.Here.Info($"Analysis: race ended ({lastRaceTime:0.0} s, {lapsEnded} laps ended{(finished ? ", finished" : "")})");
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
            lapsEnded = 0;
            finished = false;
            startLastLap = f.LastLap;
            stillSince = 0;
            var near = Near().ToList();
            Track = near.Count == 1 ? near[0] : near.Count == 0 ? Place() : null;
            Logging.Log.Here.Info($"Analysis: race started at {startX:0}, {startY:0}, {startZ:0} ({(Track != null ? "on " + settings.TrackName(Track.Id) : near.Count + " tracks start here")})");
        }
        // a sprint's finish: no lap ends, the race clock stops (or LastLap is set to the race's time)
        long now = Environment.TickCount64;
        bool still = rt == lastRaceTime;
        if (!still) stillSince = 0;
        else if (stillSince == 0) stillSince = now;
        lastRaceTime = rt;
        if (finished) return;
        racers = Math.Max(racers, f.RacePosition);
        if (lapsEnded == 0 && f.LapNumber == lastLap && rt > 5 && f.Distance - lapStartDistance > 100)
        {
            if (f.LastLap > 0 && f.LastLap != startLastLap && Math.Abs(f.LastLap - rt) < 2) { Finish(f, f.LastLap, "LastLap set"); return; }
            if (stillSince != 0 && now - stillSince >= FinishMs) { Finish(f, rt, "the race clock stopped"); return; }
        }
        if (f.LapNumber < lastLap) { lastLap = f.LapNumber; return; } // rewound
        if (f.LapNumber == lastLap) return;

        // a lap ended: Forza's LastLap is its time (a sprint's, if it ends one with none: the race's)
        float length = f.Distance - lapStartDistance;
        lapStartDistance = f.Distance;
        int lapNo = lastLap + 1;
        lastLap = f.LapNumber;
        lapsEnded++;
        float time = f.LastLap > 0 ? f.LastLap : lapNo == 1 ? rt : 0;
        Record(f, lapNo, time, length);
    }

    /// <summary>A sprint finished: its one lap recorded, nothing more until the next race.</summary>
    private void Finish(TelemetryFrame f, float time, string why)
    {
        finished = true;
        Logging.Log.Here.Info($"Analysis: sprint finished ({why}) in {time:0.00} s");
        Record(f, 1, time, f.Distance - lapStartDistance);
    }

    private void Record(TelemetryFrame f, int lapNo, float time, float length)
    {
        if (!(time > 0) || !Recording) return;
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
            Lap = lapNo, Time = time, Length = length,
        };
        Logging.Log.Here.Info($"Analysis: lap {lapNo} on {settings.TrackName(Track.Id)} in {settings.CarName(game, f.CarOrdinal)}: {Blocks.LapTime(time)}");
        store.Add(lap);
    }

    /// <summary>The tracks whose races start where this one did (a named place with no race yet isn't one).</summary>
    private IEnumerable<Track> Near() => settings.Tracks.Where(t => t.Game == game && t.Length > 0 && Apart(t) <= StartRadius);

    private float Apart(Track t)
    {
        float dx = t.X - startX, dy = t.Y - startY, dz = t.Z - startZ;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The nearest named place with no race yet within <see cref="PlaceRadius"/> of this race's start.</summary>
    private Track? Place() => settings.Tracks.Where(t => t.Game == game && t.Length <= 0 && Apart(t) <= PlaceRadius)
        .OrderBy(Apart).FirstOrDefault();

    private Track? Find(float length)
    {
        var known = Near().Where(t => Math.Abs(t.Length - length) <= Math.Max(t.Length * LengthTolerance, LengthSlack))
            .OrderBy(t => Math.Abs(t.Length - length)).FirstOrDefault();
        if (known != null || Place() is not { } place) return known;
        // (a named place's first race: it starts where the race did, as long as its lap)
        place.X = startX; place.Y = startY; place.Z = startZ;
        place.Length = Math.Max(1, length);
        settings.Save();
        madeHere = true;
        Logging.Log.Here.Info($"Analysis: {settings.TrackName(place.Id)}'s first race (a lap {length:0} m)");
        return place;
    }

    /// <summary>The place the car is (<paramref name="f"/>'s), named as a track: the race that starts nearest it (within
    /// <see cref="PlaceRadius"/>) is on it. Null: the game sends no place.</summary>
    public Track? NameHere(TelemetryFrame? f, string game)
    {
        if (f is not { HasRace: true } || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) return null;
        var t = new Track { Game = game, X = f.PosX, Y = f.PosY, Z = f.PosZ };
        settings.Tracks.Add(t);
        settings.Save();
        Logging.Log.Here.Info($"Analysis: a place named at {t.X:0}, {t.Y:0}, {t.Z:0}");
        return t;
    }

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
