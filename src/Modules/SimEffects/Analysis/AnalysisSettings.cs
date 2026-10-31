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

    /// <summary>Each block's widget, by the block's id.</summary>
    public Dictionary<string, BlockWidget> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tab's cards folded (since 3.28.0), by their ids ("Block.Laps", "BestTimes"…).</summary>
    public HashSet<string> Folded { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tracks found, in the order they were.</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>The names given to cars, by "FH6:ordinal" ("FH:ordinal" before 3.24.0): over the list's
    /// (<see cref="CarNames"/>).</summary>
    public Dictionary<string, string> Cars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public BlockWidget Widget(string block) => Widgets.TryGetValue(block, out var w) ? w : Widgets[block] = new BlockWidget();

    public Track? TrackById(string? id) => id == null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>A track's name, or "Track n" while it has none.</summary>
    public string TrackName(string? id) => TrackById(id) is { } t
        ? (string.IsNullOrWhiteSpace(t.Name) ? F("Track {0}", Tracks.IndexOf(t) + 1) : t.Name)
        : T("Unknown track");

    /// <summary>A car's name: the one given to it, the game's list's, or "Car n".</summary>
    public string CarName(string? game, int ordinal) =>
        Given(game, ordinal) ?? CarNames.Name(game, ordinal) ?? F("Car {0}", ordinal);

    /// <summary>The name given to a car (in this game, or before 3.24.0 in any Horizon).</summary>
    public string? Given(string? game, int ordinal) =>
        game != null && Cars.TryGetValue($"{game}:{ordinal}", out var n) && !string.IsNullOrWhiteSpace(n) ? n
        : game?.StartsWith("FH") != false && Cars.TryGetValue($"FH:{ordinal}", out n) && !string.IsNullOrWhiteSpace(n) ? n : null;

    public static AnalysisSettings Load()
    {
        var s = JsonStore.Load<AnalysisSettings>(FileName);
        // (before 3.24.0 every track was "FH": Horizon 6's, the one out)
        foreach (var t in s.Tracks.Where(t => t.Game == "FH")) t.Game = LapStore.OldGame;
        return s;
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
    /// <summary>Its on / off shortcut (since 3.27.1): a key combination or a controller button.</summary>
    public string? Key { get; set; }
    /// <summary>The rows hidden (unticked while it's being edited), by their keys.</summary>
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A track, as Analysis knows one (Forza doesn't say which it is): where its races start (the car's place when the race
/// clock starts) and how long a lap is (the distance driven over the first lap). A race starting within
/// <see cref="LapTracker.StartRadius"/> of one with a lap as long (within <see cref="LapTracker.LengthTolerance"/>) is on it.
/// </summary>
public sealed class Track
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Which game ("FH6"…, <see cref="CarNames.Edition"/>): a track is only found again in its own.</summary>
    public string Game { get; set; } = "FH6";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>A lap, metres.</summary>
    public float Length { get; set; }
    /// <summary>The most laps and cars seen in a race on it (Forza doesn't say): Lap's and Position's "of".</summary>
    public int Laps { get; set; }
    public int Racers { get; set; }
    public DateTime Found { get; set; } = DateTime.Now;
}
