using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>
/// Forza Analysis: the sessions ForzaWheel logs while its input overlay shows (Core's Shared/SessionLog.cs), each
/// analysed for how smoothly the brake and throttle go on and come off (PedalAnalysis), with the trace, the zones
/// compared, tables and tips. A module of its own; in Daisy's App a sub-tab of Gaming (Core's Shell/TabGroups.cs).
/// </summary>
[Applet("ForzaAnalysis", "Forza Analysis", "\uE9D2", Order = 57, Group = "GamingTools", GroupOrder = 30,
    Description = "Your driving sessions from ForzaWheel, analysed for smooth braking and throttle")]
public sealed class ForzaAnalysisApplet : IApplet
{
    private readonly ForzaAnalysisSettings settings = ForzaAnalysisSettings.Load();
    private readonly ForzaAnalysisView view;

    public ForzaAnalysisApplet() => view = new ForzaAnalysisView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    public void Start() { }
    public void SaveSettings() => settings.Save();
    public void Dispose() => settings.Save();
}
