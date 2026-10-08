using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Resizer;

/// <summary>
/// Resizer tab (from Resize Rabbit): saved window sizes and positions per program, applied with a click, a global
/// shortcut, the tray menu, a script / Stream Deck command, or automatically when the program starts.
/// </summary>
[Applet("Resizer", "Resizer", "", Order = 40,
    Description = "Saves window sizes and positions per program, e.g. to stretch a game across three monitors")]
public sealed class ResizerApplet : IApplet
{
    private readonly ResizerService service = new();
    private readonly ResizerView view;
    private readonly ResizerSettingsView settingsView;

    public ResizerApplet()
    {
        view = new ResizerView(service);
        settingsView = new ResizerSettingsView(service);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() => service.Start();
    public void SaveSettings() => service.Data.Save();
    public IReadOnlyList<AppletMenuItem>? TrayMenu => service.TrayMenu();
    public void Dispose() => service.Dispose();
}
