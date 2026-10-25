using System.Windows;
using System.Windows.Interop;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// Calibration: each controller axis's min, centre and max as Windows keeps them (DirectInput's calibration, as
/// joy.cpl and Heusinkveld's DIView set it), so every game gets the calibrated axis. A module of its own; in Daisy's
/// App a sub-tab of Gaming (../Joy2Key/GamingTab.cs). Nothing runs while the tab isn't showing.
/// </summary>
[Applet("Calibration", "Calibration", "", Order = 59, Group = "GamingTools", GroupOrder = 50,
    Description = "Min, centre and max of your pedals', wheels' and sticks' axes, kept by Windows for every game")]
public sealed class CalibrationApplet : IApplet
{
    private readonly CalibrationSettings settings = CalibrationSettings.Load();
    // a hidden window of its own, which DirectInput reads controllers in the background for (as ForzaWheel's)
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp Calibration") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly CalibrationView view;

    public CalibrationApplet() => view = new CalibrationView(settings, window.Handle);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    public void Start() { }
    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        view.Dispose();
        settings.Save();
        window.Dispose();
    }
}
