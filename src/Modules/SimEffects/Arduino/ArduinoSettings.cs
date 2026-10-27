using System.Text.Json.Serialization;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>One thing on a board (a slot): its kind (<see cref="IArduinoFeature.Id"/>), name, and settings.</summary>
public sealed class ArduinoFeature
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Its <see cref="FeatureField"/>s' values by key; a field not here has its default.</summary>
    public Dictionary<string, int> Values { get; set; } = new();

    public int Get(FeatureField field) => Values.TryGetValue(field.Key, out int v) ? v : field.Default;

    [JsonIgnore] public IArduinoFeature? Info => ArduinoFeatures.Of(Kind);
}

/// <summary>An Arduino: its COM port and what's on it.</summary>
public sealed class ArduinoBoard
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>"COM5".</summary>
    public string Port { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>The sketch found on it last (<see cref="ArduinoSketch"/>; "" before it's been connected): tried first,
    /// and the window shows only that sketch's settings.</summary>
    public string Sketch { get; set; } = "";
    public List<ArduinoFeature> Features { get; set; } = new();
}

/// <summary>The sketches a board can have.</summary>
public static class ArduinoSketch
{
    /// <summary>SimHub's own (its Arduino setup's): what's on which pin is set in the sketch.</summary>
    public const string SimHub = "SimHub";
    /// <summary>Daisy's App's own: told what's on which pin when it connects.</summary>
    public const string DaisysSim = "DaisysSim";
}

/// <summary>Saved in %APPDATA%\DaisysApp\Arduino.json.</summary>
public sealed class ArduinoSettings : IProfileSettings<GameProfile>
{
    private const string FileName = "Arduino";

    /// <summary>The boards are connected (since 3.17.0; on to start with, as before).</summary>
    public bool Enabled { get; set; } = true;
    public string? OnOffKey { get; set; }
    public bool TitleBadge { get; set; } = true;
    public List<ArduinoBoard> Boards { get; set; } = new();
    /// <summary>The games whose programs switch it on (<see cref="GameSwitcher{T}"/>).</summary>
    public List<GameProfile> Profiles { get; set; } = new();
    public string? Active { get; set; }
    public bool GameSwitching { get; set; } = true;
    public bool SwitchOnWithGame { get; set; }

    public static ArduinoSettings Load()
    {
        var s = JsonStore.Load<ArduinoSettings>(FileName);
        if (s.Profiles.Count == 0) s.Profiles.AddRange(GameProfiles.Defaults<GameProfile>());
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}
