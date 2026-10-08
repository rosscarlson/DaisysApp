using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// Mini Mirror tab (formerly a SimHub plugin): draw a rectangle or circle around any part of the screen and show it
/// live in its own always-on-top window, with its own size, zoom, opacity, frame rate and show / hide shortcut.
/// </summary>
[Applet("MiniMirror", "Mini Mirror", "", Order = 50,
    Description = "Shows part of the screen live in its own always-on-top window, e.g. a track map on another monitor")]
public sealed class MiniMirrorApplet : IApplet
{
    private readonly MiniMirrorService service = new();
    private readonly MiniMirrorView view;
    private readonly MiniMirrorSettingsView settingsView;

    public MiniMirrorApplet()
    {
        view = new MiniMirrorView(service);
        settingsView = new MiniMirrorSettingsView(service);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() => service.Start();
    public void SaveSettings() => service.SaveNow();
    public IReadOnlyList<AppletMenuItem>? TrayMenu => service.TrayMenu();
    public void Dispose() => service.Dispose();
}
