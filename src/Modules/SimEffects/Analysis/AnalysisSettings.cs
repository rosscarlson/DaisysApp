using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Settings;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.Analysis;

public enum AnalysisUnits { Imperial, Metric }

public enum TemperatureUnit { Fahrenheit, Celsius }

/// <summary>
/// Saved in %APPDATA%\DaisysApp\Analysis.json: on / off (kept per SimEffects profile: on in Forza Horizon's at first),
/// the units, each block's widget, the tracks found and the names given to cars. The laps are in
/// <see cref="LapStore"/>'s file.
/// </summary>
public sealed class AnalysisSettings
{
    private const string FileName = "Analysis";

    /// <summary>Recording laps (the blocks and widgets show the game's data either way).</summary>
    public bool Enabled { get; set; }

    /// <summary>On, and the SimEffects profile is playing: what records.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool On => Enabled && SimProfiles.Playing;

    /// <summary>What each SimEffects profile keeps of these.</summary>
    public static readonly string[] Scoped = { nameof(Enabled) };

    /// <summary>(since 4.8.0) Each race recorded with Video → Recording (<see cref="RaceVideo"/>), the same in every profile.</summary>
    public bool RecordVideo { get; set; }

    /// <summary>The on / off shortcut (since 3.24.3): a key combination or a controller button.</summary>
    public string? OnOffKey { get; set; }

    public AnalysisUnits Units { get; set; }

    /// <summary>Tyre temperatures in °F or °C (since 3.24.0); null: as <see cref="Units"/>.</summary>
    public TemperatureUnit? Temperature { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Celsius => Temperature == TemperatureUnit.Celsius || (Temperature == null && Units == AnalysisUnits.Metric);

    /// <summary>The Tires block's colours (since 3.24.2), °F: green from <see cref="TyreGreen"/> (warm enough to grip),
    /// orange from <see cref="TyreOrange"/> (getting hot), red from <see cref="TyreRed"/> (overheating); below green, none.</summary>
    public float TyreGreen { get; set; } = 140;
    public float TyreOrange { get; set; } = 210;
    public float TyreRed { get; set; } = 240;

    /// <summary>A tyre's band at <paramref name="f"/> °F: 0 cold, 1 green, 2 orange, 3 red.</summary>
    public int TyreBand(float f) => f >= TyreRed ? 3 : f >= TyreOrange ? 2 : f >= TyreGreen ? 1 : 0;

    /// <summary>How many cars the Leaderboard shows (since 3.28.2), yours in the middle.</summary>
    public int BoardRows { get; set; } = 10;

    /// <summary>How far from a track's start (or a named place) a race can start and be on it, metres (since 3.30.2;
    /// 750 before): <see cref="LapTracker"/>'s start radius.</summary>
    public int StartRadius { get; set; } = DefaultStartRadius;
    public const int DefaultStartRadius = 500, MinStartRadius = 50, MaxStartRadius = 2000;

    /// <summary>Each block's widget, by the block's id.</summary>
    public Dictionary<string, BlockWidget> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tab's cards folded (since 3.28.0), by their ids ("Block.Laps", "BestTimes"…).</summary>
    public HashSet<string> Folded { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tracks found, in the order they were.</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>The version of the app's tracks last added (<see cref="Analysis.KnownTracks"/>, since 3.34.0).</summary>
    public string? KnownTracks { get; set; }

    /// <summary>Each game's map (since 3.31.0): its image and the tracks placed on it, by the game ("FH6"…).</summary>
    public Dictionary<string, GameMap> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>(since 4.10.8) The games with more than one map, by the game: their maps, the main one first. Horizon sends
    /// the same kind of place on each (Mexico's and Hot Wheels Park's tracks can start at the same coordinates), so a
    /// track is on one (<see cref="Track.Map"/>) and only found on it.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Worlds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["FH4"] = ["Britain", "Fortune Island", "LEGO Valley"],
        ["FH5"] = ["Mexico", "Sierra Nueva", "Hot Wheels Park"],
    };

    /// <summary>(since 4.10.8) The map each game is on (picked, or found by <see cref="Terrain"/>), by the game; the main
    /// one isn't kept.</summary>
    public Dictionary<string, string> World { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The map <paramref name="game"/> is on: null for its main one (or a game with one).</summary>
    public string? MapOf(string? game) => game != null && World.TryGetValue(game, out var m) && Worlds.TryGetValue(game, out var all)
        && all.Skip(1).Contains(m) ? m : null;

    public void SetMap(string game, string? map)
    {
        if (map == null || !Worlds.TryGetValue(game, out var all) || all[0] == map) World.Remove(game); else World[game] = map;
    }

    /// <summary>A game's map as one key ("FH5", "FH5|Hot Wheels Park"): the Map card's maps are kept by it.</summary>
    public static string WorldKey(string game, string? map) => map == null ? game : $"{game}|{map}";
    public static string WorldKey(Track t) => WorldKey(t.Game, t.Map);

    /// <summary>A map's key as a name ("FH5 · Hot Wheels Park").</summary>
    public static string WorldName(string key) => key.Replace("|", " · ");

    /// <summary>The names given to cars, by "FH6:ordinal" ("FH:ordinal" before 3.24.0): over the list's
    /// (<see cref="CarNames"/>).</summary>
    public Dictionary<string, string> Cars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>(since 4.10.0) The names of the cars and classes the games name themselves (iRacing, the Assetto Corsas),
    /// as seen, by "game:number" (<see cref="CarNames.Number"/> of the game's id for it).</summary>
    public Dictionary<string, string> GameCars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> GameClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The settings loaded (the class names, for <see cref="Blocks.ClassName"/>).</summary>
    internal static AnalysisSettings? Loaded { get; private set; }

    /// <summary>The frame's car and class names kept, where the game names them; true when one was new (to be saved).</summary>
    public bool Learn(string game, TelemetryFrame f)
    {
        if (CarNames.IsForza(game)) return false;
        bool added = false;
        if (f.CarOrdinal > 0 && f.Car.Length > 0 && !GameCars.ContainsKey($"{game}:{f.CarOrdinal}")) { GameCars[$"{game}:{f.CarOrdinal}"] = f.Car; added = true; }
        if (f.CarClass > 0 && f.CarClassName.Length > 0 && !GameClasses.ContainsKey($"{game}:{f.CarClass}")) { GameClasses[$"{game}:{f.CarClass}"] = f.CarClassName; added = true; }
        return added;
    }

    public BlockWidget Widget(string block) => Widgets.TryGetValue(block, out var w) ? w : Widgets[block] = new BlockWidget();

    public Track? TrackById(string? id) => id == null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>A track's name, or "Track n" while it has none.</summary>
    public string TrackName(string? id) => TrackById(id) is { } t
        ? (string.IsNullOrWhiteSpace(t.Name) ? F("Track {0}", Tracks.IndexOf(t) + 1) : t.Name)
        : T("Unknown track");

    /// <summary>A car's name: the one given to it, the game's list's, or "Car n".</summary>
    public string CarName(string? game, int ordinal) =>
        Given(game, ordinal) ?? CarNames.Name(game, ordinal)
        ?? (GameCars.TryGetValue($"{game}:{ordinal}", out var seen) ? seen : null) ?? F("Car {0}", ordinal);

    /// <summary>The name given to a car (in this game, or before 3.24.0 in any Horizon).</summary>
    public string? Given(string? game, int ordinal) =>
        game != null && Cars.TryGetValue($"{game}:{ordinal}", out var n) && !string.IsNullOrWhiteSpace(n) ? n
        : game?.StartsWith("FH") != false && Cars.TryGetValue($"FH:{ordinal}", out n) && !string.IsNullOrWhiteSpace(n) ? n : null;

    public static AnalysisSettings Load()
    {
        var s = JsonStore.Load<AnalysisSettings>(FileName);
        // (before 3.24.0 every track was "FH": Horizon 6's, the one out)
        foreach (var t in s.Tracks.Where(t => t.Game == "FH")) t.Game = LapStore.OldGame;
        s.StartRadius = Math.Clamp(s.StartRadius, MinStartRadius, MaxStartRadius);
        // (the Vehicle and Laps blocks, and their widgets, went in 3.30.2)
        s.Widgets.Remove("Vehicle");
        s.Widgets.Remove("Laps");
        if (Analysis.KnownTracks.Merge(s) | s.AdoptPlaces()) JsonStore.Save(FileName, s);
        Loaded = s;
        return s;
    }

    /// <summary>(since 4.10.12) A place the user named (no race yet) whose name a track with races has, on the same
    /// map: where that track's event is entered (<see cref="Track.Entry"/>, if it has none), the place gone. True: one was.</summary>
    public bool AdoptPlaces()
    {
        bool any = false;
        foreach (var p in Tracks.Where(p => p.Length <= 0 && p.Key == null && p.Name.Trim().Length > 0).ToList())
        {
            var t = Tracks.Where(t => t.Length > 0 && t.Key == null && t.Entry == null && t.Game == p.Game && t.Map == p.Map
                    && string.Equals(t.Name.Trim(), p.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .MinBy(t => (t.X - p.X) * (t.X - p.X) + (t.Z - p.Z) * (t.Z - p.Z));
            if (t == null) continue;
            t.Entry = new[] { p.X, p.Y, p.Z };
            Tracks.Remove(p);
            foreach (var a in Maps.Values.SelectMany(m => m.Anchors).Where(a => a.Track == p.Id)) a.Track = t.Id;
            Logging.Log.Here.Info($"Analysis: the place \"{p.Name}\" is where {t.Name}'s event is entered");
            any = true;
        }
        return any;
    }

    public void Save()
    {
        JsonStore.Save(FileName, this);
        SimProfiles.Saved(this);
    }
}

/// <summary>A block's widget: open or not, where (Core's widget keeps it), its background and title (since 3.24.2),
/// and the rows it leaves out.</summary>
public sealed class BlockWidget : IOverlayPlace
{
    public double Background { get; set; } = 1;
    public bool? Title { get; set; }
    public bool Show { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 0.9;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    /// <summary>Its on / off shortcut (since 3.27.1): a key combination or a controller button.</summary>
    public string? Key { get; set; }
    /// <summary>The rows hidden (unticked while it's being edited), by their keys.</summary>
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A track, as Analysis knows one (Forza doesn't say which it is): where its races start (the car's place when the race
/// clock starts) and how long a lap is (the distance driven over the first lap). A race starting within
/// <see cref="StartRadius"/> of one with a lap as long (within <see cref="LapTracker.LengthTolerance"/>) is on it.
/// </summary>
public sealed class Track
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Which game ("FH6"…, <see cref="CarNames.Edition"/>): a track is only found again in its own.</summary>
    public string Game { get; set; } = "FH6";
    /// <summary>(since 4.10.0) The game's own id for it, where the game names its tracks (iRacing, the Assetto Corsas,
    /// Forza Motorsport (2023)): it's found by that, not by where its races start; null in Horizon.</summary>
    public string? Key { get; set; }
    /// <summary>(since 4.10.8) Which of the game's maps (<see cref="AnalysisSettings.Worlds"/>) it's on: null, the main one.</summary>
    public string? Map { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>A lap, metres.</summary>
    public float Length { get; set; }
    /// <summary>The most laps and cars seen in a race on it (Forza doesn't say): Lap's and Position's "of".</summary>
    public int Laps { get; set; }
    public int Racers { get; set; }
    public DateTime Found { get; set; } = DateTime.Now;
    /// <summary>Its kind (since 3.31.0: <see cref="MapView.Kinds"/>, "Road"…); null: guessed from its name.</summary>
    public string? Type { get; set; }
    /// <summary>(since 4.10.12) Where its event is entered (X, Y, Z: the place the user named there; Horizon then puts
    /// the car on the grid, often far off): a race entered there is on it. Null: not known.</summary>
    public float[]? Entry { get; set; }
}

/// <summary>A game's map (since 3.31.0): its image (a file in AnalysisMaps) and the tracks placed on it.</summary>
public sealed class GameMap
{
    public string? Image { get; set; }
    public List<MapAnchor> Anchors { get; set; } = new();
}

/// <summary>A track placed on a map: where it starts in the game (X, Z) and on the image (U, V: 0–1 across and down).</summary>
public sealed class MapAnchor
{
    public string Track { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public double U { get; set; }
    public double V { get; set; }
}
