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

    /// <summary>Always on top: an item in every window's title bar menu that puts it on top (or not).</summary>
    public bool TopMost { get; set; }

    /// <summary>The shortcut that puts the window in front on top, or not.</summary>
    public string? TopMostKey { get; set; }

    /// <summary>The shortcut that turns every monitor black, or back.</summary>
    public string? BlackoutKey { get; set; }

    public static ScreenSettings Load() => JsonStore.Load<ScreenSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
