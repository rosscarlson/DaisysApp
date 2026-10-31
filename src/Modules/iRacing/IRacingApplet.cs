using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// iRacing: every race of yours from iRacing's Data API (RaceData, through IRacingApi), kept on this PC and summed up
/// in tiles, graphs and tables (IRacingView, RaceStats, Charts); signing in under Settings (IRacingSettingsView). A
/// module of its own; in Daisy's App a sub-tab of Gaming (Core's Shell/TabGroups.cs).
/// </summary>
[Applet("iRacing", "iRacing", "", Order = 58, Group = "GamingTools", GroupOrder = 40,
    Description = "Your iRacing races from iRacing's Data API: wins, laps at each track, ratings over time and much more, in graphs and tables")]
public sealed class IRacingApplet : IApplet
{
    private readonly IRacingSettings settings = IRacingSettings.Load();
    private readonly IRacingView view;
    private readonly IRacingSettingsView settingsView;

    public IRacingApplet()
    {
        view = new IRacingView(settings);
        settingsView = new IRacingSettingsView(settings, view);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;
    public void Start() { }
    public void SaveSettings() => settings.Save();
    public void Dispose() => settings.Save();
}
