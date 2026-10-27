using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaisysApp.Applets.Gaming.ForzaWheel;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Settings;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>Where the pedals and wheel are read from: the game's telemetry (the profile's game), or a controller.</summary>
public enum OverlaySource { Game, Device }

public enum SpeedUnit { Mph, Kmh }

/// <summary>A trace read from a controller: its axis, and reversed (a pedal that reads full when released).</summary>
public sealed class OverlayAxis
{
    public InputRef? Input { get; set; }
    public bool Invert { get; set; }
}

/// <summary>
/// Saved in %APPDATA%\DaisysApp\InputOverlay.json: on / off and its shortcut, the profiles (the game it reads, the
/// programs that switch it on), where it reads from, where it shows, and what it draws. Started from ForzaWheel's
/// input overlay (its place, history, traces and bound axes) the first time, since it moved here in 3.17.0.
/// </summary>
public sealed class OverlaySettings : IProfileSettings<GameProfile>, IWidgetPlace
{
    private const string FileName = "InputOverlay";

    /// <summary>The traces, in the order they're listed.</summary>
    public static readonly string[] Roles = { "Steering", "Throttle", "Brake", "Clutch", "Handbrake" };

    public bool Enabled { get; set; }
    public string? OnOffKey { get; set; }
    public List<GameProfile> Profiles { get; set; } = new();
    public string? Active { get; set; }
    public bool GameSwitching { get; set; } = true;
    public bool SwitchOnWithGame { get; set; }

    public OverlaySource Source { get; set; }
    /// <summary>Each trace's axis when it's read from a controller, by <see cref="Roles"/>.</summary>
    public Dictionary<string, OverlayAxis> Axes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Its place and size on screen, in physical pixels (0 width: not placed yet).</summary>
    public int X { get; set; } = 80;
    public int Y { get; set; } = 80;
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>How many seconds of history the traces show at the normal speed (the left fifth runs faster: <see cref="OverlayWindow.Shown"/>).</summary>
    public int Seconds { get; set; } = 5;
    /// <summary>How dark the background is behind the traces, 0 (none) to 1.</summary>
    public double Background { get; set; } = 0.35;
    public bool Steering { get; set; } = true;
    public bool Throttle { get; set; } = true;
    public bool Brake { get; set; } = true;
    public bool Clutch { get; set; } = true;
    public bool Handbrake { get; set; } = true;
    /// <summary>The speeds left of the traces: now, and the highest and lowest of the last <see cref="WindowSeconds"/>.</summary>
    public bool Speed { get; set; } = true;
    public int WindowSeconds { get; set; } = 5;
    public SpeedUnit Unit { get; set; }
    /// <summary>Writes a session log of the inputs while it shows, for Forza Analysis (SessionLog.cs).</summary>
    public bool Record { get; set; } = true;

    // (always fully shown and on top of the game: its background has its own darkness)
    double IWidgetPlace.Opacity { get => 1; set { } }
    bool IWidgetPlace.Topmost { get => true; set { } }

    public OverlayAxis Axis(string role) => Axes.TryGetValue(role, out var a) ? a : Axes[role] = new OverlayAxis();

    public static OverlaySettings Load()
    {
        bool fresh = !File.Exists(JsonStore.PathFor(FileName));
        var s = JsonStore.Load<OverlaySettings>(FileName);
        if (fresh) s.FromForzaWheel();
        if (s.Profiles.Count == 0) s.Profiles.AddRange(GameProfiles.Defaults<GameProfile>());
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);

    /// <summary>ForzaWheel's overlay settings (before 3.17.0) and the axes bound there: shown there, it comes on here
    /// with the game (Forza Horizon's profile in use).</summary>
    private void FromForzaWheel()
    {
        try
        {
            var path = JsonStore.PathFor("ForzaWheel");
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject fw) return;
            if (fw["Overlay"] is JsonObject o)
            {
                int Int(string k, int d) => o[k] is JsonValue v && v.TryGetValue(out int x) ? x : d;
                bool Bool(string k, bool d) => o[k] is JsonValue v && v.TryGetValue(out bool x) ? x : d;
                X = Int("X", X); Y = Int("Y", Y); Width = Int("Width", 0); Height = Int("Height", 0);
                Seconds = Int("Seconds", Seconds);
                if (o["Background"] is JsonValue b && b.TryGetValue(out double back)) Background = back;
                Steering = Bool("Steering", true); Throttle = Bool("Throttle", true); Brake = Bool("Brake", true);
                Clutch = Bool("Clutch", true); Handbrake = Bool("Handbrake", true); Record = Bool("Record", true);
                SwitchOnWithGame = Bool("Show", false);
            }
            if (fw["Config"]?["Bindings"] is JsonArray bindings)
                foreach (var b in bindings.OfType<JsonObject>())
                {
                    string role = b["Role"]?.GetValue<string>() ?? "";
                    if (!Roles.Contains(role, StringComparer.OrdinalIgnoreCase)) continue;
                    var input = b["Inputs"]?.AsArray().FirstOrDefault()?.Deserialize<InputRef>(JsonStore.Options);
                    if (input is not { Kind: InputRef.InputKind.Axis }) continue;
                    Axes[role] = new OverlayAxis { Input = input, Invert = b["Invert"] is JsonValue v && v.TryGetValue(out bool inv) && inv };
                }
            Logging.Log.Here.Info($"Input Overlay: settings taken from ForzaWheel's overlay ({Axes.Count} axes)");
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Input Overlay: ForzaWheel's overlay settings", ex); }
    }
}
