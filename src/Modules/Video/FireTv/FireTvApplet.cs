using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// Fire TV, a sub-tab of the Video tab: an Amazon Fire TV (Fire OS) on the network, debloated over ADB: its ads,
/// tracking and Amazon's apps removed for its user (and put back), the apps you keep left alone, and a few tweaks.
/// </summary>
public sealed class FireTvApplet : IApplet
{
    private readonly FireTvSettings settings = FireTvSettings.Load();
    private readonly FireTvView view;
    private readonly KeepOff keepOff;

    public FireTvApplet()
    {
        view = new FireTvView(settings);
        keepOff = new KeepOff(settings, () => view.Busy);
        keepOff.Said += view.FromKeepOff;
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => keepOff.Start();
    public void SaveSettings() => settings.Save();
    public void Dispose()
    {
        keepOff.Dispose();
        view.Shutdown();
    }
}
