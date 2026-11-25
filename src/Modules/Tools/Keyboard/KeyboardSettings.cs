using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Keyboards;

/// <summary>Saved in %APPDATA%\DaisysApp\Keyboard.json.</summary>
public sealed class KeyboardSettings
{
    private const string FileName = "Keyboard";

    /// <summary>The remaps work (off: every key is as it was).</summary>
    public bool On { get; set; } = true;

    /// <summary>Each key remapped, and what it sends instead.</summary>
    public List<Remap> Remaps { get; set; } = [];

    public static KeyboardSettings Load() => JsonStore.Load<KeyboardSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
