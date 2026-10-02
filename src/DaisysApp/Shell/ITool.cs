using System.Windows;
using System.Windows.Input;

namespace DaisysApp.Shell;

/// <summary>
/// One tool = one tab. To add a tool: put it in its own folder under Tools/, implement this interface, and add one
/// line to <see cref="ToolRegistry"/>. The shell supplies the tab, the Settings sub-tab, the tray, startup and updates.
/// A tool keeps its own settings with <see cref="Settings.JsonStore"/> (one file per tool, named after <see cref="Id"/>).
/// </summary>
public interface ITool : IDisposable
{
    /// <summary>Stable key: settings file name and remembered-tab id. Never change it once released.</summary>
    string Id { get; }

    /// <summary>Tab caption, also used for the Settings sub-tab.</summary>
    string Title { get; }

    /// <summary>Segoe Fluent Icons glyph shown next to the caption, e.g. "".</summary>
    string Icon { get; }

    /// <summary>The tab's content. Created once and kept alive while the app runs.</summary>
    FrameworkElement View { get; }

    /// <summary>Content of this tool's sub-tab under Settings, or null if it has no settings.</summary>
    FrameworkElement? SettingsView { get; }

    /// <summary>Called once at launch, also when the app starts hidden in the tray. Start background work here.</summary>
    void Start();

    /// <summary>Keyboard shortcuts: called for key presses in the main window while this tool's tab is open.</summary>
    void OnPreviewKeyDown(KeyEventArgs e) { }

    /// <summary>The window was hidden to the tray: stop anything that only makes sense while it's visible.</summary>
    void OnWindowHidden() { }

    /// <summary>Persist settings. Called when the window is hidden and before <see cref="IDisposable.Dispose"/> on exit.</summary>
    void SaveSettings();
}
