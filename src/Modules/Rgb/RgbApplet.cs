using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// RGB: the lights on the motherboard (and the fans and strips on its headers), the RAM and the graphics card, each set
/// to an effect (Static, Breathing, Flashing, Colour Cycle, Rainbow, Off), and all off at once, without the makers'
/// programs. A module of its own; in Daisy's App a sub-tab of Gaming (../Joy2Key/GamingTab.cs). At the start the
/// devices are found and set to what they were set to here (those never set here are left alone).
/// </summary>
[Applet("Rgb", "RGB", "", Order = 60, Group = "GamingTools", GroupOrder = 60,
    Description = "Your motherboard's, fans', RAM's and graphics card's lights: an effect each, or all off")]
public sealed class RgbApplet : IApplet
{
    private readonly RgbSettings settings = RgbSettings.Load();
    private readonly RgbHub hub;
    private readonly RgbView view;

    public RgbApplet()
    {
        hub = new RgbHub(settings);
        view = new RgbView(settings, hub);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    public void Start() => hub.Find();
    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hub.Dispose();
        settings.Save();
    }
}
