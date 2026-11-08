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
    /// <summary>(since 4.5.0) Its race's session log (a file name in Core's session folder; null: none).</summary>
    public string? Session { get; set; }
}

/// <summary>A race finished (since 3.28.1): when, where, in what, its laps and time, and the place the car started and
/// finished in (Forza's RacePosition: 1 the leader; 0 unknown) of how many.</summary>
public sealed class RaceRecord
{
    public DateTime At { get; set; }
    public string Game { get; set; } = LapStore.OldGame;
    public string Track { get; set; } = "";
    public int Car { get; set; }
    public int Class { get; set; }
    public int Pi { get; set; }
    public int Laps { get; set; }
    /// <summary>Seconds, start to finish.</summary>
    public float Time { get; set; }
    public int Start { get; set; }
    public int Finish { get; set; }
    /// <summary>The most cars the race showed (its highest position seen), or the track's.</summary>
    public int Racers { get; set; }
    /// <summary>(since 4.5.0) Its session log (a file name in Core's session folder; null: none).</summary>
    public string? Session { get; set; }
}

/// <summary>
/// Every lap recorded, a JSON line each in %APPDATA%\DaisysApp\AnalysisLaps.jsonl (added to as laps come; kept until
/// a track is deleted with its laps), and (since 3.28.1) every race finished, in AnalysisRaces.jsonl.
/// </summary>
public sealed class LapStore
{
    private static readonly string Path = System.IO.Path.Combine(AppPaths.SettingsFolder, "AnalysisLaps.jsonl");
    private static readonly string RacesPath = System.IO.Path.Combine(AppPaths.SettingsFolder, "AnalysisRaces.jsonl");
    /// <summary>The game laps and tracks from before 3.24.0 ("FH") were in: Horizon 6, the one out then.</summary>
    public const string OldGame = "FH6";
    private readonly List<LapRecord> laps = new();
    private readonly List<RaceRecord> races = new();
    private readonly DateTime started = DateTime.Now;

    public IReadOnlyList<LapRecord> All => laps;
    public IReadOnlyList<RaceRecord> Races => races;

    /// <summary>A lap or race was added, or some were deleted (on the UI thread).</summary>
    public event Action? Changed;

    public LapStore()
    {
        Read(Path, laps, l => { if (l.Game == "FH") l.Game = OldGame; });
        Read(RacesPath, races, null);
    }

    private static void Read<TRecord>(string path, List<TRecord> into, Action<TRecord>? fix)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<TRecord>(line) is not { } r) continue;
                    fix?.Invoke(r);
                    into.Add(r);
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Analysis: reading {System.IO.Path.GetFileName(path)}", ex); }
    }

    private static void Append(string path, object record)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Analysis: saving to {System.IO.Path.GetFileName(path)}", ex); }
    }

    public void Add(LapRecord lap)
    {
        laps.Add(lap);
        Append(Path, lap);
        Changed?.Invoke();
    }

    public void Add(RaceRecord race)
    {
        races.Add(race);
        Append(RacesPath, race);
        Changed?.Invoke();
    }

    /// <summary>A track's laps and races deleted.</summary>
    public void RemoveTrack(string track)
    {
        int lapsGone = laps.RemoveAll(l => l.Track == track), racesGone = races.RemoveAll(r => r.Track == track);
        if (lapsGone + racesGone == 0) return;
        try
        {
            if (lapsGone > 0) File.WriteAllLines(Path, laps.Select(l => JsonSerializer.Serialize(l)));
            if (racesGone > 0) File.WriteAllLines(RacesPath, races.Select(r => JsonSerializer.Serialize(r)));
        }
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
/// on it (where starts overlap, the nearest that fits; since 3.31.1); otherwise it's a new track, named by the user (<see cref="NewTrack"/>). Two routes from the same place (common
/// in Horizon) are told apart by their length. A sprint (point to point, since 3.24.5) ends no lap: its finish is the
/// race clock stopping while the car still sends, LastLap set to the race's time, or (since 3.24.7: Forza sends nothing
/// after a finish) the data stopping mid-race and coming back in free roam; recorded as lap 1. A place named by the user
/// (<see cref="NameHere"/>: a track with no length yet) is the track of the next race started within
/// <see cref="PendingMinutes"/> (since 3.24.7: wherever its grid is), or of a race starting within
/// <see cref="PlaceRadius"/> of it and nearer than any track that fits. Fed the newest frame on the UI thread.
/// </summary>
public sealed class LapTracker
{
    /// <summary>How far apart two starts of the same track can be, metres: the setting (since 3.30.2, 500 at first; 750
    /// in 3.27.1, which found another event's start 700 m away; 150 before: a grid, the event's marker and its start
    /// line can be hundreds of metres apart).</summary>
    private float StartRadius => settings.StartRadius;
    /// <summary>How far from this race's start the next one of a series starts (a new race straight after the finish).</summary>
    private const float SeriesRadius = 150;
    /// <summary>How much a lap's length can differ on the same track: the share, or this many metres if more.</summary>
    public const float LengthTolerance = 0.06f, LengthSlack = 250;
    /// <summary>How far from a named place (where the event's started) its race can start, metres: the nearest wins
    /// (since 3.30.2 the start radius; 750 in 3.27.1, 300 before).</summary>
    private float PlaceRadius => settings.StartRadius;
    /// <summary>How long the race clock has to stand still, while the car sends, for a sprint to have finished.</summary>
    private const long FinishMs = 1000;
    /// <summary>How long a place just named is the next race's track, wherever that race starts (the event's grid can be
    /// far from where it was started), minutes.</summary>
    public const int PendingMinutes = 15;
    /// <summary>How long the data has to stop mid-race, then come back in free roam, for a sprint to have finished.</summary>
    private const long GapMs = 1500;
    /// <summary>How much of a known sprint has to be driven for a stop to be its finish (not the race quit).</summary>
    private const float FinishShare = 0.85f;

    private readonly AnalysisSettings settings;
    private readonly LapStore store;
    private string game = LapStore.OldGame;
    private bool inRace;
    private float lastRaceTime, lapStartDistance, raceStartDistance, startX, startY, startZ;
    private int lastLap, racers, lapsEnded, startPos, lastPos;
    private bool madeHere, finished;
    private float startLastLap, lapLastLap;
    private long stillSince, lastLiveAt;
    private readonly TelemetryFrame lastRace = new();
    /// <summary>(since 3.30.3) The frame furthest into this lap: a finish is judged on it, as Forza sends a few frames
    /// after the finish (the next race loading) with the clock as it was and the distance back near 0.</summary>
    private readonly TelemetryFrame furthest = new();
    private float furthestDriven;
    /// <summary>The race clock when the last lap number went up.</summary>
    private float lapEndRaceTime;
    private Track? pending;
    private DateTime pendingAt;
    /// <summary>(since 4.10.12) Where the car last was in free roam, and when (0: not since the last race began).</summary>
    private float roamX, roamY, roamZ;
    private long roamAt;
    /// <summary>Where it was before it last jumped after a pause (put on an event's grid, waiting for the start), and when.</summary>
    private float[]? jumpedFrom;
    private long jumpedAt;
    /// <summary>Where this race's event was entered (the car in free roam just before it; null: not known), and whether
    /// its track was found by that.</summary>
    private float[]? entry;
    private bool byEntry;
    /// <summary>How long before a race the car's last place in free roam is where its event was entered (the event's
    /// menu, loading).</summary>
    private const long EntryMs = 180_000;
    /// <summary>When this race started (its session log is found by it).</summary>
    private DateTime raceAt;

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

    /// <summary>How much of a sprint is driven, 0 – 1, once its track's length is known (-1: unknown, or not in a sprint
    /// still running). Forza sends no progress: it's the distance driven over the track's.</summary>
    public float Progress => inRace && !finished && lapsEnded == 0 && Track is { Length: > 0, Laps: <= 1, Key: null } t
        ? Math.Clamp((lastRace.Distance - raceStartDistance) / t.Length, 0, 1) : -1;

    /// <summary>The track starting nearest the car (<paramref name="f"/>'s place) within <see cref="StartRadius"/>, and how
    /// far it is (null: none).</summary>
    public (Track Track, float Metres)? Nearest(TelemetryFrame? f, string game)
    {
        if (f is { HasRace: true, TrackKey.Length: > 0 }) return ByKey(f, game) is { } keyed ? (keyed, float.NaN) : null;
        if (f is not { HasRace: true } || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) return null;
        // (since 4.10.12 where its race starts or its event is entered, the nearer)
        var best = settings.Tracks.Where(t => OnMap(t, game)).Select(t => (t, Math.Min(Dist(t.X - f.PosX, t.Y - f.PosY, t.Z - f.PosZ),
                t.Entry is { Length: 3 } e ? Dist(e[0] - f.PosX, e[1] - f.PosY, e[2] - f.PosZ) : float.MaxValue)))
            .Where(x => x.Item2 <= StartRadius).OrderBy(x => x.Item2).FirstOrDefault();
        return best.t == null ? null : best;
    }

    /// <summary>The track the car is at: its race's, or (none) the one starting nearest it, and how far from its start
    /// (null: none, or no place).</summary>
    public (Track Track, float Metres)? Here(TelemetryFrame? f, string game)
    {
        if (inRace && Track is { Key: not null } k && f is { HasRace: true }) return (k, float.NaN);
        if (f is not { HasRace: true } || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) return null;
        if (inRace && Track is { } t) return (t, Dist(t.X - f.PosX, t.Y - f.PosY, t.Z - f.PosZ));
        return Nearest(f, game);
    }

    /// <summary>(since 4.5.0) A lap or result of the race started then is kept: its session log is kept too, and its
    /// file name returned for the record.</summary>
    public Func<DateTime, string?>? Claim { get; set; }

    /// <summary>Recording (the tool's on): otherwise races are followed, but no lap is kept and no track added.</summary>
    public bool Recording { get; set; }

    /// <param name="game">The game being read ("FH6"…): tracks and laps are its own.</param>
    public void Feed(TelemetryFrame f, string game)
    {
        this.game = game;
        if (f.Sessions) { FeedSessions(f); return; }
        if (!f.HasRace || !f.Live) return;
        float rt = f.RaceTime;
        long now = Environment.TickCount64;
        if (rt <= 0)
        {
            // back in free roam (or the race ended)
            if (inRace)
            {
                Logging.Log.Here.Info($"Analysis: race ended ({lastRaceTime:0.0} s, {lapsEnded} laps ended{(finished ? ", finished" : "")}, data stopped {(now - lastLiveAt) / 1000.0:0.0} s)");
                Stopped(now);
            }
            inRace = false;
            Track = null;
            lastRaceTime = 0;
            racers = 0;
            if (f.PosX != 0 || f.PosY != 0 || f.PosZ != 0)
            {
                if (roamAt > 0 && now - roamAt >= GapMs && Dist(f.PosX - roamX, f.PosY - roamY, f.PosZ - roamZ) > SeriesRadius)
                {
                    jumpedFrom = new[] { roamX, roamY, roamZ };
                    jumpedAt = now;
                }
                roamX = f.PosX; roamY = f.PosY; roamZ = f.PosZ; roamAt = now;
            }
            return;
        }
        // a race starts, or starts again (restarted: its clock back near 0, or back at all after the data stopped)
        if (!inRace || (rt < 0.5f && lastRaceTime > 2) || (rt < lastRaceTime - 1 && now - lastLiveAt >= GapMs))
        {
            // (the next race of a series, straight after this one's finish: it starts somewhere else)
            bool again = inRace && Dist(f.PosX - startX, f.PosY - startY, f.PosZ - startZ) <= SeriesRadius;
            if (inRace && !again) Stopped(now);
            // (since 4.10.12) the event entered where the car just was in free roam (a restart: where this one was)
            var seen = new List<float[]>();
            if (jumpedFrom != null && now - jumpedAt < EntryMs) seen.Add(jumpedFrom);
            if (roamAt > 0 && now - roamAt < EntryMs) seen.Add(new[] { roamX, roamY, roamZ });
            roamAt = 0;
            jumpedFrom = null;
            inRace = true;
            raceAt = DateTime.Now;
            madeHere = false;
            startX = f.PosX; startY = f.PosY; startZ = f.PosZ;
            lapStartDistance = raceStartDistance = f.Distance;
            lastLap = f.LapNumber;
            racers = 0;
            startPos = lastPos = f.RacePosition;
            lapsEnded = 0;
            finished = false;
            startLastLap = lapLastLap = f.LastLap;
            stillSince = 0;
            furthestDriven = 0;
            f.CopyTo(furthest);
            var near = Near().ToList();
            // (a place just named: this race's, wherever it starts; it's taken once)
            var named = pending is { Length: <= 0 } p && OnMap(p, game) && settings.Tracks.Contains(p)
                && DateTime.Now - pendingAt < TimeSpan.FromMinutes(PendingMinutes) ? p : null;
            pending = null;
            // (since 3.31.1, where starts overlap) a named place nearer than any track here is this race's
            var place = Place();
            // (since 4.10.12) the track whose event is entered where this one was
            Track? entered = null;
            if (seen.Count > 0 || !again)
            {
                entry = seen.FirstOrDefault(e => Entered(e) != null) ?? seen.FirstOrDefault();
                entered = named == null && entry != null ? Entered(entry) : null;
            }
            else if (byEntry && Track != null && settings.Tracks.Contains(Track)) entered = Track;
            byEntry = entered != null;
            Track = named ?? entered ?? (place != null && (near.Count == 0 || Apart(place) < Apart(near[0])) ? place : near.Count == 1 ? near[0] : null);
            // (since 4.10.0 Motorsport (2023) says which track it is)
            if (f.TrackKey.Length > 0) Track = ByKey(f, game) ?? (Recording ? AddKeyed(f) : null);
            Logging.Log.Here.Info($"Analysis: race started at {startX:0}, {startY:0}, {startZ:0} ({(Track != null ? "on " + settings.TrackName(Track.Id) : near.Count + " tracks start here")}{(entry != null ? $", entered at {entry[0]:0}, {entry[1]:0}, {entry[2]:0}{(byEntry ? "" : ", no track's")}" : "")})");
        }
        // (since 3.30.3) back after a gap, the clock as it was but the car somewhere else: the race is over (the next
        // one loading), judged on the furthest frame; these frames are no part of it
        else if (!finished && now - lastLiveAt >= GapMs && rt >= lastRaceTime - 1
            && (lastRace.Distance - f.Distance > 100 || Dist(f.PosX - lastRace.PosX, f.PosY - lastRace.PosY, f.PosZ - lastRace.PosZ) > SeriesRadius))
        {
            Logging.Log.Here.Info($"Analysis: back after {(now - lastLiveAt) / 1000.0:0.0} s somewhere else ({lastRace.Distance - f.Distance:0} m less driven): the race is over");
            Stopped(now);
            finished = true;
        }
        // a sprint's finish: no lap ends, the race clock stops (or LastLap is set to the race's time)
        lastLiveAt = now;
        f.CopyTo(lastRace);
        bool still = rt == lastRaceTime;
        if (!still) stillSince = 0;
        else if (stillSince == 0) stillSince = now;
        lastRaceTime = rt;
        if (finished) return;
        if (f.Distance - lapStartDistance >= furthestDriven)
        {
            furthestDriven = f.Distance - lapStartDistance;
            f.CopyTo(furthest);
        }
        racers = Math.Max(racers, f.RacePosition);
        // (since 3.28.1) the place it started in (its first) and is in, for the race's result
        if (f.RacePosition > 0)
        {
            lastPos = f.RacePosition;
            if (startPos == 0) startPos = f.RacePosition;
        }
        if (lapsEnded == 0 && f.LapNumber == lastLap && rt > 5 && f.Distance - lapStartDistance > 100)
        {
            if (f.LastLap > 0 && f.LastLap != startLastLap && Math.Abs(f.LastLap - rt) < 2) { Finish(f, f.LastLap, "LastLap set"); return; }
            if (stillSince != 0 && now - stillSince >= FinishMs) { Finish(f, rt, "the race clock stopped"); return; }
        }
        // (since 3.28.0) a circuit's last lap: no lap number follows it, the race clock stops or LastLap takes its time
        // (once most of a lap is driven)
        if (lapsEnded > 0 && f.LapNumber == lastLap && Track is { Length: > 0 } circuit && f.Distance - lapStartDistance >= circuit.Length * FinishShare)
        {
            if (f.LastLap > 0 && f.LastLap != lapLastLap) { LastLap(f, f.LastLap, "LastLap set"); return; }
            if (stillSince != 0 && now - stillSince >= FinishMs && f.CurrentLap > 0) { LastLap(f, f.CurrentLap, "the race clock stopped"); return; }
        }
        if (f.LapNumber < lastLap) { lastLap = f.LapNumber; return; } // rewound
        if (f.LapNumber == lastLap) return;

        // a lap ended: Forza's LastLap is its time (a sprint's, if it ends one with none: the race's)
        float length = f.Distance - lapStartDistance;
        lapStartDistance = f.Distance;
        int lapNo = lastLap + 1;
        lastLap = f.LapNumber;
        lapsEnded++;
        lapLastLap = f.LastLap;
        lapEndRaceTime = rt;
        furthestDriven = 0;
        f.CopyTo(furthest);
        float time = f.LastLap > 0 ? f.LastLap : lapNo == 1 ? rt : 0;
        Record(f, lapNo, time, length);
    }

    /// <summary>The race's data stopped and came back in free roam or another race: Forza sends nothing after the finish
    /// line, so a sprint that drove far enough (most of its length, once it's known) finished there, and a circuit's
    /// last lap (most of a lap driven) ended there, in its current lap time. Judged on the frame furthest into the lap
    /// (since 3.30.3; the last one before: frames after the finish could make it look undriven). A circuit whose lap
    /// number went up at the line (Horizon 5) finished there when hardly any of the next lap was driven.</summary>
    private void Stopped(long now)
    {
        if (finished) return;
        var f = furthest;
        float driven = furthestDriven;
        bool gap = now - lastLiveAt >= GapMs;
        string why = $"{lastRaceTime:0.0} s, {lapsEnded} laps ended, {driven:0} m into the lap{(Track is { Length: > 0 } k ? $" of {k.Length:0} m" : "")}{(gap ? ", the data stopped" : "")}";
        if (f.RaceTime <= 5)
        {
            Logging.Log.Here.Info($"Analysis: not a finish ({why})");
            return;
        }
        if (lapsEnded > 0)
        {
            // (a circuit's last lap ended even when the data didn't stop first: since 3.28.0, the third lap of three was lost
            // when the race clock went straight to 0 at the line)
            if (Track is { Length: > 0 } lapped && driven >= lapped.Length * FinishShare && f.CurrentLap > 0)
            {
                finished = true;
                Logging.Log.Here.Info($"Analysis: last lap ended ({why}) in {f.CurrentLap:0.00} s");
                Record(f, lastLap + 1, f.CurrentLap, driven);
                Result(f, lastLap + 1, f.RaceTime);
            }
            else if (Track is { Length: > 0 } after && driven < after.Length * (1 - FinishShare) && lapEndRaceTime > 0)
            {
                finished = true;
                Logging.Log.Here.Info($"Analysis: finished at the end of lap {lapsEnded} ({why})");
                Result(f, lapsEnded, lapEndRaceTime);
            }
            else Logging.Log.Here.Info($"Analysis: not a finish ({why})");
            return;
        }
        if (driven <= 100) { Logging.Log.Here.Info($"Analysis: not a finish ({why})"); return; }
        if (!gap) { Logging.Log.Here.Info($"Analysis: not a finish, the data didn't stop ({why})"); return; }
        if (Track is { Length: > 0 } known && driven < known.Length * FinishShare)
            Logging.Log.Here.Info($"Analysis: not a finish ({driven:0} m of {settings.TrackName(known.Id)}'s {known.Length:0} m)");
        else Finish(f, f.RaceTime, "the data stopped");
    }

    /// <summary>A circuit's last lap ended (the finish): recorded, nothing more until the next race.</summary>
    private void LastLap(TelemetryFrame f, float time, string why)
    {
        finished = true;
        Logging.Log.Here.Info($"Analysis: last lap ended ({why}) in {time:0.00} s");
        Record(f, lastLap + 1, time, f.Distance - lapStartDistance);
        Result(f, lastLap + 1, f.RaceTime);
    }

    /// <summary>A sprint finished: its one lap recorded, nothing more until the next race.</summary>
    private void Finish(TelemetryFrame f, float time, string why)
    {
        finished = true;
        Logging.Log.Here.Info($"Analysis: sprint finished ({why}) in {time:0.00} s");
        Record(f, 1, time, f.Distance - lapStartDistance);
        Result(f, 1, time);
    }

    /// <summary>(since 3.28.1) A race finished: its result (where the car started and finished) recorded.</summary>
    private void Result(TelemetryFrame f, int laps, float time)
    {
        if (!Recording || Track == null || !(time > 0))
        {
            Logging.Log.Here.Info($"Analysis: race not kept ({(!Recording ? "not recording" : Track == null ? "no track" : "no time")})");
            return;
        }
        var race = new RaceRecord
        {
            At = DateTime.Now, Game = game, Track = Track.Id, Car = f.CarOrdinal, Class = f.CarClass, Pi = f.CarPi,
            Laps = laps, Time = time, Start = startPos, Finish = f.RacePosition > 0 ? f.RacePosition : lastPos, Racers = Racers,
            Session = Claim?.Invoke(raceAt),
        };
        Logging.Log.Here.Info($"Analysis: race on {settings.TrackName(Track.Id)} finished in {Blocks.LapTime(time)}, started {race.Start}, finished {race.Finish} of {race.Racers}");
        store.Add(race);
    }

    private void Record(TelemetryFrame f, int lapNo, float time, float length)
    {
        if (!(time > 0) || !Recording) return;
        if (f.TrackKey.Length > 0)
        {
            Track ??= ByKey(f, game) ?? AddKeyed(f);
            if (Track.Length <= 0 && length > 100) { Track.Length = length; settings.Save(); }
        }
        else if (lapNo == 1 || Track == null) Track = Find(length) ?? Add(length);
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
            Lap = lapNo, Time = time, Length = length, Session = Claim?.Invoke(raceAt),
        };
        Logging.Log.Here.Info($"Analysis: lap {lapNo} on {settings.TrackName(Track.Id)} in {settings.CarName(game, f.CarOrdinal)}: {Blocks.LapTime(time)}");
        store.Add(lap);
    }

    /// <summary>The tracks whose races start where this one did (a named place with no race yet isn't one).</summary>
    private IEnumerable<Track> Near() => settings.Tracks.Where(t => OnMap(t, game) && t.Length > 0 && Apart(t) <= StartRadius).OrderBy(Apart);

    /// <summary>A track of <paramref name="g"/> found by where its races start, on the map it's on (since 4.10.8: Horizon's
    /// maps share their coordinates).</summary>
    private bool OnMap(Track t, string g) => t.Game == g && t.Key == null && t.Map == settings.MapOf(g);

    private float Apart(Track t)
    {
        return Dist(t.X - startX, t.Y - startY, t.Z - startZ);
    }

    private static float Dist(float dx, float dy, float dz) => MathF.Sqrt(dx * dx + dy * dy + dz * dz);

    /// <summary>The nearest named place with no race yet within <see cref="PlaceRadius"/> of this race's start.</summary>
    private Track? Place() => settings.Tracks.Where(t => OnMap(t, game) && t.Length <= 0 && Apart(t) <= PlaceRadius)
        .OrderBy(Apart).FirstOrDefault();

    /// <summary>(since 4.10.12) The track whose event is entered nearest <paramref name="e"/>, within
    /// <see cref="StartRadius"/>: its <see cref="Track.Entry"/>, or a named place with no race yet.</summary>
    private Track? Entered(float[] e) => settings.Tracks.Where(t => OnMap(t, game) && EntryOf(t) != null)
        .Select(t => (t, m: EntryOf(t)!)).Select(x => (x.t, d: Dist(x.m[0] - e[0], x.m[1] - e[1], x.m[2] - e[2])))
        .Where(x => x.d <= StartRadius).OrderBy(x => x.d).Select(x => x.t).FirstOrDefault();

    private static float[]? EntryOf(Track t) => t.Entry is { Length: 3 } e ? e : t.Length <= 0 ? new[] { t.X, t.Y, t.Z } : null;

    private static bool Fits(Track t, float length) => Math.Abs(t.Length - length) <= Math.Max(t.Length * LengthTolerance, LengthSlack);

    private Track? Find(float length)
    {
        // (since 4.10.12) the track whose event was entered: it, as long as its lap is (a place: its first race)
        if (byEntry && Track is { } entered && settings.Tracks.Contains(entered))
        {
            if (entered.Length > 0 && Fits(entered, length)) return entered;
            if (entered.Length <= 0) return FirstRace(entered, length);
        }
        // (since 3.31.1 the nearest that fits, where starts overlap, not the closest in length)
        var known = Near().FirstOrDefault(t => Fits(t, length));
        // (a place named nearer this race's start than that track: it's its own, not that track again)
        if (known != null && Track is { Length: <= 0 } nearer && settings.Tracks.Contains(nearer) && Apart(nearer) < Apart(known)) known = null;
        if (known != null)
        {
            // (this race's place was named for a track that's known already: the track takes its name, if it has none,
            // and the place goes)
            if (Track is { Length: <= 0 } tagged && settings.Tracks.Contains(tagged))
            {
                if (string.IsNullOrWhiteSpace(known.Name)) known.Name = tagged.Name;
                settings.Tracks.Remove(tagged);
                settings.Save();
                Logging.Log.Here.Info($"Analysis: the place \"{tagged.Name}\" is {settings.TrackName(known.Id)}");
            }
            return known;
        }
        var place = Track is { Length: <= 0 } named ? named : Place();
        return place == null ? null : FirstRace(place, length);
    }

    /// <summary>A named place's first race: it starts where the race did, as long as its lap; the place (since 4.10.12)
    /// is where its event is entered.</summary>
    private Track FirstRace(Track place, float length)
    {
        place.Entry ??= new[] { place.X, place.Y, place.Z };
        place.X = startX; place.Y = startY; place.Z = startZ;
        place.Length = Math.Max(1, length);
        settings.Save();
        madeHere = true;
        Logging.Log.Here.Info($"Analysis: {settings.TrackName(place.Id)}'s first race (a lap {length:0} m)");
        return place;
    }

    /// <summary>The place the car is (<paramref name="f"/>'s), saved as a track named <paramref name="name"/>: the next
    /// race, or one that starts nearest it (within <see cref="PlaceRadius"/>), is on it. Null: the game sends no place.</summary>
    public Track? NameHere(TelemetryFrame? f, string game, string name)
    {
        // (a game that names its tracks: the track, named so)
        if (f is { HasRace: true, TrackKey.Length: > 0 })
        {
            this.game = game;
            var keyed = ByKey(f, game) ?? AddKeyed(f);
            keyed.Name = name.Trim();
            settings.Save();
            return keyed;
        }
        if (f is not { HasRace: true } || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) return null;
        var t = new Track { Game = game, Map = settings.MapOf(game), Name = name.Trim(), X = f.PosX, Y = f.PosY, Z = f.PosZ };
        settings.Tracks.Add(t);
        settings.Save();
        pending = t;
        pendingAt = DateTime.Now;
        Logging.Log.Here.Info($"Analysis: a place named \"{t.Name}\" at {t.X:0}, {t.Y:0}, {t.Z:0}");
        return t;
    }

    // ---------------------------------------------------------------- the games that name their tracks (since 4.10.0)

    /// <summary>The track the game says the car is on, if it's known.</summary>
    private Track? ByKey(TelemetryFrame f, string game) => settings.Tracks.FirstOrDefault(t => t.Game == game && t.Key == f.TrackKey);

    /// <summary>The track the game says the car is on, added (its name the game's, if it gives one; else to be named).</summary>
    private Track AddKeyed(TelemetryFrame f)
    {
        var t = new Track
        {
            Game = game, Key = f.TrackKey, Name = f.TrackName, Length = Math.Max(0, f.TrackLength),
            X = float.IsNaN(f.TrackEast) ? 0 : f.TrackEast, Z = float.IsNaN(f.TrackNorth) ? 0 : f.TrackNorth,
        };
        settings.Tracks.Add(t);
        settings.Save();
        Logging.Log.Here.Info($"Analysis: a new track, {settings.TrackName(t.Id)} ({f.TrackKey}{(t.Length > 0 ? $", a lap {t.Length:0} m" : "")})");
        if (string.IsNullOrWhiteSpace(t.Name)) NewTrack?.Invoke(t);
        return t;
    }

    private string runKey = "";
    private bool lapCut, checkered;
    private float lapsTime;
    private int fullLaps;
    /// <summary>A lap ended (the game's count went up), waiting for the game's time for it: its length, the last lap's
    /// time before it, when it ended and whether it was cut.</summary>
    private sealed record Waiting(float Length, float Before, long At, bool Cut, bool FromLine);
    /// <summary>The lap being driven started at the line, in this run (not where the car came on track: out of the pits,
    /// the grid, the app started mid-lap).</summary>
    private bool fromLine;
    private Waiting? waiting;

    /// <summary>iRacing and the Assetto Corsas: they say the track, the session, when a lap ends and its time, and the
    /// chequered flag, so a run (a stint on track: <see cref="TelemetryFrame.RaceTime"/>) is followed by those. A lap
    /// is kept when it's a whole one (most of the track's length: not an out lap or the grid to the line), not cut, and
    /// timed; a race's result when a lap ends under the chequered flag.</summary>
    private void FeedSessions(TelemetryFrame f)
    {
        // (the session over: the game in its menus, a replay)
        if (!f.HasRace)
        {
            if (inRace) EndRun("no session");
            return;
        }
        long now = Environment.TickCount64;
        float rt = f.RaceTime;
        if (rt <= 0)
        {
            if (inRace) EndRun("off track");
            return;
        }
        // (paused)
        if (!f.Live) return;
        if (!inRace || f.TrackKey != runKey || rt < lastRaceTime - 1 || f.LapNumber < lastLap)
        {
            if (inRace) EndRun("another session");
            StartRun(f);
        }
        lastRaceTime = rt;
        lastLiveAt = now;
        f.CopyTo(lastRace);
        if (f.Session == SessionKind.Race)
        {
            racers = Math.Max(racers, Math.Max(f.FieldSize, f.RacePosition));
            if (f.RacePosition > 0)
            {
                lastPos = f.RacePosition;
                if (startPos == 0) startPos = f.RacePosition;
            }
            if (f.Checkered) checkered = true;
        }
        if (finished) return;
        if (f.LapNumber > lastLap)
        {
            if (waiting != null) Settle(f, timed: false);
            waiting = new Waiting(f.Distance - lapStartDistance, lapLastLap, now, lapCut, fromLine);
            lapStartDistance = f.Distance;
            lastLap = f.LapNumber;
            lapCut = false;
            fromLine = true;
        }
        else lapCut |= f.LapInvalid;
        // (the game's time for it comes with the lap, or a moment after)
        if (waiting != null && f.LastLap > 0 && f.LastLap != waiting.Before) Settle(f, timed: true);
        else if (waiting != null && now - waiting.At > 3000) Settle(f, timed: false);
    }

    private void StartRun(TelemetryFrame f)
    {
        inRace = true;
        runKey = f.TrackKey;
        raceAt = DateTime.Now;
        lastLap = f.LapNumber;
        lapLastLap = f.LastLap;
        lapStartDistance = raceStartDistance = f.Distance;
        racers = startPos = lastPos = lapsEnded = fullLaps = 0;
        finished = checkered = lapCut = fromLine = false;
        lapsTime = 0;
        waiting = null;
        Track = ByKey(f, game) ?? (Recording ? AddKeyed(f) : null);
        if (settings.Learn(game, f)) settings.Save();
        Logging.Log.Here.Info($"Analysis: on track ({f.Session}{(Track != null ? ", " + settings.TrackName(Track.Id) : "")}, lap {f.LapNumber + 1})");
    }

    private void EndRun(string why)
    {
        if (waiting != null) Settle(lastRace, timed: lastRace.LastLap > 0 && lastRace.LastLap != waiting.Before);
        Logging.Log.Here.Info($"Analysis: run ended ({why}: {lastRaceTime:0.0} s, {fullLaps} laps{(finished ? ", finished" : "")})");
        inRace = false;
        Track = null;
        lastRaceTime = 0;
        racers = 0;
    }

    /// <summary>The lap waiting: kept (a whole lap, not cut, timed) or not, and the race's result if it ended under the
    /// chequered flag.</summary>
    private void Settle(TelemetryFrame f, bool timed)
    {
        var w = waiting!;
        waiting = null;
        float time = timed ? f.LastLap : 0;
        lapLastLap = f.LastLap;
        lapsEnded++;
        float length = f.TrackLength > 0 ? f.TrackLength : Track?.Length ?? 0;
        bool whole = w.FromLine && (length > 0 ? w.Length >= length * FinishShare && w.Length <= length * 1.5f : w.Length > 500);
        if (!whole) Logging.Log.Here.Info($"Analysis: not a whole lap ({(w.FromLine ? "" : "not from the line, ")}{w.Length:0} m{(length > 0 ? $" of {length:0}" : "")}), not kept");
        else
        {
            fullLaps++;
            if (time > 0) lapsTime += time;
            if (w.Cut) Logging.Log.Here.Info($"Analysis: lap {fullLaps} cut ({Blocks.LapTime(time)}), not kept");
            else if (time <= 0) Logging.Log.Here.Info($"Analysis: lap {fullLaps} has no time from the game, not kept");
            else RecordSession(f, fullLaps, time, w.Length);
        }
        if (checkered && f.Session == SessionKind.Race && !finished)
        {
            finished = true;
            Logging.Log.Here.Info($"Analysis: race finished ({fullLaps} laps, {Blocks.LapTime(lapsTime)})");
            Result(f, fullLaps, lapsTime);
        }
    }

    private void RecordSession(TelemetryFrame f, int lapNo, float time, float length)
    {
        if (!Recording) return;
        Track ??= ByKey(f, game) ?? AddKeyed(f);
        bool learnt = settings.Learn(game, f);
        if (Track.Length <= 0) { Track.Length = length; learnt = true; }
        if (f.Session == SessionKind.Race)
        {
            if (lapNo > Track.Laps) { Track.Laps = lapNo; learnt = true; }
            if (racers > Track.Racers) { Track.Racers = racers; learnt = true; }
        }
        if (learnt) settings.Save();
        var lap = new LapRecord
        {
            At = DateTime.Now, Game = game, Track = Track.Id, Car = f.CarOrdinal, Class = f.CarClass, Pi = f.CarPi, Drive = f.Drivetrain,
            Lap = lapNo, Time = time, Length = length, Session = Claim?.Invoke(raceAt),
        };
        Logging.Log.Here.Info($"Analysis: lap {lapNo} on {settings.TrackName(Track.Id)} in {settings.CarName(game, f.CarOrdinal)}: {Blocks.LapTime(time)}");
        store.Add(lap);
    }

    private Track Add(float length)
    {
        // (since 4.10.12 where its event was entered: the next race entered there is on it)
        var t = new Track { Game = game, Map = settings.MapOf(game), X = startX, Y = startY, Z = startZ, Length = Math.Max(0, length), Entry = entry };
        settings.Tracks.Add(t);
        settings.Save();
        madeHere = true;
        Logging.Log.Here.Info($"Analysis: a new track ({settings.TrackName(t.Id)}, a lap {length:0} m)");
        NewTrack?.Invoke(t);
        return t;
    }
}
