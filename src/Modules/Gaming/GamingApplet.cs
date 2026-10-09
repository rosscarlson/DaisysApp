using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Gaming tab: an FPS overlay (frame rate, frame time, video memory and more, in three sizes), screen recording with
/// the graphics card's encoder, and a history of every game's performance.
/// </summary>
[Applet("Gaming", "Gaming", "", Order = 60,
    Description = "FPS overlay, screen recording with the graphics card's encoder, and each game's performance history")]
public sealed class GamingApplet : IApplet
{
    private readonly GamingService service = new();
    private readonly GamingView view;
    private readonly GamingSettingsView settingsView;

    public GamingApplet()
    {
        view = new GamingView(service);
        settingsView = new GamingSettingsView(service, view);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() => service.Start();
    public void SaveSettings() => service.SaveNow();
    public IReadOnlyList<AppletMenuItem>? TrayMenu => service.TrayMenu();
    public void Dispose() => service.Dispose();
}
