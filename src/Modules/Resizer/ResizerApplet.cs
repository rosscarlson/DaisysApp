using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video.Resizer;

/// <summary>
/// Resizer (from Resize Rabbit): saved window sizes and positions per program, applied with a click, a global shortcut,
/// the tray menu, a script / Stream Deck command, or automatically when the program starts. A module of its own (and an
/// app of its own, Resizer.exe); in Daisy's App the first sub-tab of the Video tab.
/// </summary>
[Applet("Resizer", "Resizer", "", Order = 40, Group = "Video", GroupOrder = 0,
    Description = "Saved window sizes and positions per program, applied with a click, a shortcut or when the program starts")]
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
