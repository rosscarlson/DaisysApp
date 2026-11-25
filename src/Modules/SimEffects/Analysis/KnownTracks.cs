using System.Text.Json;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The tracks that come with the app (since 3.34.0; <c>Tracks.json</c>, embedded): every FH4, FH5 and FH6 race the
/// developer mapped, as Export writes them. Since 4.18.9 they're the only tracks in those games, the same on every PC:
/// on each start a game's tracks are made the file's (name, map, start, entry, type), each found by its
/// <see cref="Track.Found"/>, then its name, then its start (its id kept, so its laps and races stay on it); one the
/// file hasn't got is taken off (its laps then on "Unknown track"). Nothing in the app adds, renames or deletes them.
/// </summary>
internal static class KnownTracks
{
    private sealed class TrackFile
    {
        public List<FileTrack> Tracks { get; set; } = new();
    }

    private sealed class FileTrack
    {
        public string Game { get; set; } = "";
        public string? Map { get; set; }
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Length { get; set; }
        public int Laps { get; set; }
        public int Racers { get; set; }
        public DateTime Found { get; set; }
        public float[]? Entry { get; set; }
    }

    /// <summary>The games the file has tracks for: no others are added in them.</summary>
    public static IReadOnlySet<string> Games => games.Value;

    private static readonly Lazy<List<FileTrack>> file = new(Read);
    private static readonly Lazy<HashSet<string>> games = new(() => file.Value.Select(e => e.Game).ToHashSet());

    private static List<FileTrack> Read()
    {
        try
        {
            using var stream = typeof(KnownTracks).Assembly.GetManifestResourceStream("DaisysApp.SimEffects.Tracks.json");
            if (stream == null) return new();
            var read = JsonSerializer.Deserialize<TrackFile>(stream, JsonStore.Options) ?? new();
            return read.Tracks.Where(e => e.Game.Length > 0 && e.Name.Length > 0).ToList();
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Analysis: the app's tracks", ex);
            return new();
        }
    }

    /// <summary>The file's games' tracks made the file's; true when anything changed (the settings need saving).</summary>
    public static bool Merge(AnalysisSettings s)
    {
        var tracks = file.Value;
        if (tracks.Count == 0) return false;
        string before = JsonSerializer.Serialize(s.Tracks);
        var mine = s.Tracks.Where(t => Games.Contains(t.Game) && t.Key == null).ToList();
        var claimed = new Dictionary<FileTrack, Track>();
        void Claim(Func<FileTrack, Track, bool> same)
        {
            foreach (var e in tracks.Where(e => !claimed.ContainsKey(e)))
                if (mine.FirstOrDefault(t => !claimed.ContainsValue(t) && t.Game == e.Game && same(e, t)) is { } t) claimed[e] = t;
        }
        Claim((e, t) => e.Found != default && t.Found.ToUniversalTime() == e.Found.ToUniversalTime());
        Claim((e, t) => string.Equals(t.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase) && t.Map == e.Map);
        Claim((e, t) => Math.Abs(t.X - e.X) < 1 && Math.Abs(t.Y - e.Y) < 1 && Math.Abs(t.Z - e.Z) < 1);
        // (one mapped on this PC where the file's is: its laps go on the file's)
        Claim((e, t) => t.Map == e.Map && Math.Abs(t.X - e.X) < 50 && Math.Abs(t.Z - e.Z) < 50
            && (e.Length <= 0 || t.Length <= 0 || Math.Abs(t.Length - e.Length) <= e.Length * LapTracker.LengthTolerance));
        int added = 0, removed = 0;
        foreach (var e in tracks)
        {
            if (!claimed.TryGetValue(e, out var t))
            {
                t = new Track { Game = e.Game };
                s.Tracks.Add(t);
                added++;
            }
            t.Name = e.Name; t.Map = e.Map; t.Type = e.Type; t.Found = e.Found == default ? t.Found : e.Found;
            // (since 4.21.3) a lap learnt here short of a whole one (a restart or quit taken for a finish) isn't kept
            if (e.Length <= 0 && t.Length > 0 && t.Length < LapTracker.HorizonWhole)
            {
                Logging.Log.Here.Info($"Analysis: {e.Name}'s lap of {t.Length:0} m is short of a whole one: learnt again");
                t.Length = 0;
            }
            // (since 4.21.7) a start learnt here where another of the file's tracks starts, its lap known: that track's race
            // taken for this place's first (Edo Circuit's for The Gauntlet's), learnt again
            if (e.Length <= 0 && t.Length > 0 && tracks.Any(o => o != e && o.Game == e.Game && o.Map == e.Map && o.Length > 0
                && Math.Abs(o.X - t.X) < 50 && Math.Abs(o.Z - t.Z) < 50) && !(Math.Abs(e.X - t.X) < 50 && Math.Abs(e.Z - t.Z) < 50))
            {
                Logging.Log.Here.Info($"Analysis: {e.Name}'s start is another track's: learnt again");
                t.Length = 0;
            }
            // (a place in the file, with no race yet, whose first race was driven here: its start and lap learnt here kept)
            if (e.Length > 0 || t.Length <= 0)
            {
                t.X = e.X; t.Y = e.Y; t.Z = e.Z; t.Entry = e.Entry;
                if (e.Length > 0) t.Length = e.Length;
            }
            else t.Entry = e.Entry ?? new[] { e.X, e.Y, e.Z };
            t.Laps = Math.Max(t.Laps, e.Laps); t.Racers = Math.Max(t.Racers, e.Racers);
        }
        foreach (var t in mine.Where(t => !claimed.ContainsValue(t)))
        {
            s.Tracks.Remove(t);
            foreach (var a in s.Maps.Values.SelectMany(m => m.Anchors).Where(a => a.Track == t.Id).ToList())
                s.Maps.Values.First(m => m.Anchors.Contains(a)).Anchors.Remove(a);
            removed++;
        }
        if (JsonSerializer.Serialize(s.Tracks) == before) return false;
        Logging.Log.Here.Info($"Analysis: the app's {tracks.Count} tracks: {added} added, {removed} not in it taken off");
        return true;
    }
}
