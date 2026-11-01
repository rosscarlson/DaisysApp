using System.Windows;
using System.Windows.Interop;
using DaisysApp.Shell;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// Devices: every game controller Windows' Game Controllers page lists, with its number watched for changes, a picture
/// of what it's doing and its Properties page. A module of its own; in Daisy's App a sub-tab of Gaming (Core's
/// Shell/TabGroups.cs). Devices are read only while the tab or a widget shows; the numbers are checked at the start and
/// whenever a device comes or goes.
/// </summary>
[Applet("GameDevices", "Devices", "", Order = 59, Group = "GamingTools", GroupOrder = 45,
    Description = "Every game controller: its number, alerts when the order changes, and a live test of its input")]
public sealed class GameDevicesApplet : IApplet
{
    private readonly DevicesSettings settings = DevicesSettings.Load();
    // a hidden window of its own, which DirectInput reads controllers in the background for and Windows tells of devices
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp Devices") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly DevicesHub hub;
    private readonly DevicesView view;

    public GameDevicesApplet()
    {
        hub = new DevicesHub(settings, window);
        view = new DevicesView(hub);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    public void Start() => hub.Start();
    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hub.Dispose();
        settings.Save();
        window.Dispose();
    }
}
