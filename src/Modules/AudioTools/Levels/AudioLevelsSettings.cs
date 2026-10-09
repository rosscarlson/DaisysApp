using DaisysApp.Settings;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>A device's or app's three shortcuts (keyboard combinations or controller buttons).</summary>
public sealed class VolumeHotkeys
{
    /// <summary>What it's for, kept so a device that isn't connected can still be named.</summary>
    public string Name { get; set; } = "";
    public string? Up { get; set; }
    public string? Down { get; set; }
    public string? Mute { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Any => Up != null || Down != null || Mute != null;
}

/// <summary>Saved in %APPDATA%\DaisysApp\AudioLevels.json.</summary>
public sealed class AudioLevelsSettings
{
    private const string FileName = "AudioLevels";

    /// <summary>How much one press of a volume shortcut changes the volume (percentage points).</summary>
    public int Step { get; set; } = 10;

    /// <summary>
    /// Shortcuts by target: "out:&lt;device id&gt;" (playback), "in:&lt;device id&gt;" (recording) or
    /// "app:&lt;program name&gt;" (an app's own volume, e.g. "app:spotify").
    /// </summary>
    public Dictionary<string, VolumeHotkeys> Hotkeys { get; set; } = new();

    public static AudioLevelsSettings Load()
    {
        var s = JsonStore.Load<AudioLevelsSettings>(FileName);
        s.Hotkeys ??= new();
        s.Step = Math.Clamp(s.Step, 1, 100);
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}
