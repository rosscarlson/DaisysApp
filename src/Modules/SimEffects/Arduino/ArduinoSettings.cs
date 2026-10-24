using System.Text.Json.Serialization;
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
    public List<ArduinoFeature> Features { get; set; } = new();
}

/// <summary>Saved in %APPDATA%\DaisysApp\Arduino.json.</summary>
public sealed class ArduinoSettings
{
    private const string FileName = "Arduino";

    public List<ArduinoBoard> Boards { get; set; } = new();

    public static ArduinoSettings Load() => JsonStore.Load<ArduinoSettings>(FileName);

    public void Save() => JsonStore.Save(FileName, this);
}
