using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Settings;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.Analysis;

public enum AnalysisUnits { Imperial, Metric }

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

    public AnalysisUnits Units { get; set; }

    /// <summary>Each block's widget, by the block's id.</summary>
    public Dictionary<string, BlockWidget> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tracks found, in the order they were.</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>The names given to cars, by "FH:ordinal" (the game doesn't send them).</summary>
    public Dictionary<string, string> Cars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public BlockWidget Widget(string block) => Widgets.TryGetValue(block, out var w) ? w : Widgets[block] = new BlockWidget();

    public Track? TrackById(string? id) => id == null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>A track's name, or "Track n" while it has none.</summary>
    public string TrackName(string? id) => TrackById(id) is { } t
        ? (string.IsNullOrWhiteSpace(t.Name) ? F("Track {0}", Tracks.IndexOf(t) + 1) : t.Name)
        : T("Unknown track");

    /// <summary>A car's name, or "Car n" while it has none.</summary>
    public string CarName(string game, int ordinal) =>
        Cars.TryGetValue($"{game}:{ordinal}", out var n) && !string.IsNullOrWhiteSpace(n) ? n : F("Car {0}", ordinal);

    public static AnalysisSettings Load() => JsonStore.Load<AnalysisSettings>(FileName);

    public void Save()
    {
        JsonStore.Save(FileName, this);
        SimProfiles.Saved(this);
    }
}

/// <summary>A block's widget: open or not, where (Core's widget keeps it), and the rows it leaves out.</summary>
public sealed class BlockWidget : IWidgetPlace
{
    public bool Show { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 0.9;
    public bool Topmost { get; set; } = true;
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
    public string Game { get; set; } = "FH";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>A lap, metres.</summary>
    public float Length { get; set; }
    public DateTime Found { get; set; } = DateTime.Now;
}
