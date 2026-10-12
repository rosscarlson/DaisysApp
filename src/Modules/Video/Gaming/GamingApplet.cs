using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Gaming, a sub-tab of the Video tab: an FPS overlay (frame rate, frame time, video memory and more, in three sizes), screen recording with
/// the graphics card's encoder, and a history of every game's performance.
/// </summary>
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

    /// <summary>The setup wizard's Gaming step, until this sign-in can count frames.</summary>
    public FrameworkElement? SetupPage => Permission.Granted ? null : new GamingSetupPage(service);
    public void Dispose() => service.Dispose();
}
