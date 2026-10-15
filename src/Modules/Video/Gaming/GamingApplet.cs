using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Overlay, a sub-tab of the Video tab: what's running now (frame rate, 1% low, frame time) and the FPS overlay over
/// games. It owns the Gaming service, which <see cref="RecordingApplet"/> (the Recording sub-tab) shares.
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

    internal GamingService Service => service;
    internal GamingView Views => view;
    internal GamingSettingsView SettingsViews => settingsView;

    public FrameworkElement View => view.OverlayPage;
    public FrameworkElement? SettingsView => settingsView.OverlaySettings;

    public void Start() => service.Start();
    public void SaveSettings() => service.SaveNow();
    public IReadOnlyList<AppletMenuItem>? TrayMenu => service.OverlayTrayMenu();

    /// <summary>The setup wizard's Gaming step, until this sign-in can count frames.</summary>
    // optional (frame counting for the FPS overlay): in the full wizard, never a reason to open it
    public FrameworkElement? SetupPage => new GamingSetupPage(service);
    public void Dispose() => service.Dispose();
}

/// <summary>
/// Recording, a sub-tab of the Video tab: the PC's GPU, CPU and memory load, screen recording with the graphics card's
/// encoder, benchmarks, and each app's performance history. The Overlay sub-tab's <see cref="GamingApplet"/> owns the
/// service (starting, saving and stopping it); this is its second page.
/// </summary>
public sealed class RecordingApplet(GamingApplet overlay) : IApplet
{
    public FrameworkElement View => overlay.Views.RecordingPage;
    public FrameworkElement? SettingsView => overlay.SettingsViews.RecordingSettings;

    public void Start() { }
    public void SaveSettings() { }
    public IReadOnlyList<AppletMenuItem>? TrayMenu => overlay.Service.RecordingTrayMenu();
    public void Dispose() { }
}
