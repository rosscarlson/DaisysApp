using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Template;

/// <summary>
/// A hello-world module: copy this folder to start your own (see README.md). The [Applet] attribute is what makes the app
/// show it: a stable Id (also the name of its settings file), the tab's title and icon (a Segoe Fluent Icons glyph),
/// where the tab goes, and the line in Settings → General → Applets. It's off by default, so it only appears once it's
/// ticked there.
/// </summary>
[Applet("Template", "Template", "", Order = 900, OnByDefault = false,
    Description = "A hello-world example module, for building your own (see its README)")]
public sealed class TemplateApplet : IApplet
{
    private readonly TemplateSettings settings = TemplateSettings.Load();
    private readonly TemplateView view;
    private readonly TemplateSettingsView settingsView;

    public TemplateApplet()
    {
        view = new TemplateView(settings);
        settingsView = new TemplateSettingsView(settings, view);
    }

    /// <summary>The tab's content.</summary>
    public FrameworkElement View => view;

    /// <summary>The applet's page under Settings (or null for none).</summary>
    public FrameworkElement? SettingsView => settingsView;

    /// <summary>Called once at launch, even when the app starts hidden in the tray: start background work here.</summary>
    public void Start() => view.Refresh();

    /// <summary>Called when the window is hidden and before Dispose on exit.</summary>
    public void SaveSettings() => settings.Save();

    public void Dispose() { }
}
