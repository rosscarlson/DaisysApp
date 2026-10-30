using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>Saved in %APPDATA%\DaisysApp\Screen.json.</summary>
public sealed class ScreenSettings
{
    private const string FileName = "Screen";

    /// <summary>Spacebar Drag: hold the spacebar and drag any window from anywhere on it.</summary>
    public bool SpaceDrag { get; set; }

    /// <summary>Spacebar Drag's on / off shortcut (a key combination or a controller button, as Core's Hotkeys keeps it).</summary>
    public string? SpaceDragKey { get; set; }

    public static ScreenSettings Load() => JsonStore.Load<ScreenSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
