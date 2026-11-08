using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The tracks that come with the app (since 3.34.0; <c>Tracks.json</c>, embedded): every FH5 and FH6 race the user
/// mapped, as Export writes them. Added to <see cref="AnalysisSettings.Tracks"/> once per version of the file
/// (<see cref="AnalysisSettings.KnownTracks"/>), so one deleted stays gone until the file changes; one already there
/// (same game, and the same name or start) isn't added again.
/// </summary>
internal static class KnownTracks
{
    private sealed class TrackFile
    {
        public List<Entry> Tracks { get; set; } = new();
    }

    private sealed class Entry
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
            int added = 0;
            foreach (var e in file.Tracks.Where(e => e.Game.Length > 0 && e.Name.Length > 0))
            {
                bool known = s.Tracks.Any(t => t.Game == e.Game && (string.Equals(t.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || Math.Abs(t.X - e.X) < 1 && Math.Abs(t.Y - e.Y) < 1 && Math.Abs(t.Z - e.Z) < 1));
                if (known) continue;
                s.Tracks.Add(new Track
                {
                    Game = e.Game, Map = e.Map, Name = e.Name, Type = e.Type, X = e.X, Y = e.Y, Z = e.Z, Length = e.Length, Laps = e.Laps, Racers = e.Racers,
                    Found = e.Found == default ? DateTime.Now : e.Found,
                });
                added++;
            }
            s.KnownTracks = version;
            Logging.Log.Here.Info($"Analysis: {added} of the app's {file.Tracks.Count} tracks added");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Analysis: the app's tracks", ex);
            return false;
        }
    }
}
