using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The tracks that come with the app (since 3.34.0; <c>Tracks.json</c>, embedded): every FH5 and FH6 (since 4.15.5 FH4's too)
/// race the user mapped, as Export writes them. Added to <see cref="AnalysisSettings.Tracks"/> once per version of the file
/// (<see cref="AnalysisSettings.KnownTracks"/>), so one deleted stays gone until the file changes; one already there
/// (same game, and the same name or start) isn't added again; one the file added before is brought up to date. Two of
/// the file's with one name (on two maps, or two places) are both added.
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

    /// <summary>Adds the ones missing; true when anything changed (the settings need saving).</summary>
    public static bool Merge(AnalysisSettings s)
    {
        try
        {
            using var stream = typeof(KnownTracks).Assembly.GetManifestResourceStream("DaisysApp.SimEffects.Tracks.json");
            if (stream == null) return false;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            string version = Convert.ToHexString(SHA256.HashData(copy.ToArray()))[..12];
            if (s.KnownTracks == version) return false;
            copy.Position = 0;
            var file = JsonSerializer.Deserialize<TrackFile>(copy, JsonStore.Options) ?? new();
            int added = 0, updated = 0;
            var before = s.Tracks.ToList();
            foreach (var e in file.Tracks.Where(e => e.Game.Length > 0 && e.Name.Length > 0))
            {
                // (since 4.10.11) One an earlier file added (its Found, which nothing changes) takes this one's name,
                // map and start, its id (and the races on it) kept.
                if (e.Found != default && before.FirstOrDefault(t => t.Game == e.Game && t.Found.ToUniversalTime() == e.Found.ToUniversalTime()) is { } mine)
                {
                    if (mine.Name != e.Name || mine.Map != e.Map || mine.X != e.X || mine.Y != e.Y || mine.Z != e.Z) updated++;
                    mine.Name = e.Name; mine.Map = e.Map; mine.X = e.X; mine.Y = e.Y; mine.Z = e.Z;
                    if (e.Type != null) mine.Type = e.Type;
                    if (e.Entry != null) mine.Entry = e.Entry;
                    mine.Length = Math.Max(mine.Length, e.Length); mine.Laps = Math.Max(mine.Laps, e.Laps); mine.Racers = Math.Max(mine.Racers, e.Racers);
                    continue;
                }
                bool known = before.Any(t => t.Game == e.Game && (string.Equals(t.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || Math.Abs(t.X - e.X) < 1 && Math.Abs(t.Y - e.Y) < 1 && Math.Abs(t.Z - e.Z) < 1));
                if (known) continue;
                s.Tracks.Add(new Track
                {
                    Game = e.Game, Map = e.Map, Name = e.Name, Type = e.Type, X = e.X, Y = e.Y, Z = e.Z, Length = e.Length, Laps = e.Laps, Racers = e.Racers, Entry = e.Entry,
                    Found = e.Found == default ? DateTime.Now : e.Found,
                });
                added++;
            }
            s.KnownTracks = version;
            Logging.Log.Here.Info($"Analysis: {added} of the app's {file.Tracks.Count} tracks added, {updated} updated");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Analysis: the app's tracks", ex);
            return false;
        }
    }
}
