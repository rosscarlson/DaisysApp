using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// Disk Usage, a sub-tab of the Tools tab: what takes the space on a drive or in a folder, like WizTree and
/// WinDirStat: the folder tree largest first, the file types and a treemap. NTFS drives are read straight from their
/// file table (seconds for a whole drive); anything else folder by folder, several at once.
/// </summary>
public sealed class DiskUsageApplet : IApplet
{
    private readonly DiskUsageSettings settings = DiskUsageSettings.Load();
    private readonly DiskUsageView view;

    public DiskUsageApplet() => view = new DiskUsageView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() => settings.Save();

    public void Dispose() => view.Shutdown();
}
