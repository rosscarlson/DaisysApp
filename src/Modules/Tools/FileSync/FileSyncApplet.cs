using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>
/// File Sync, a sub-tab of the Tools tab: two folders kept the same, like FreeFileSync. Mirror (the destination exactly
/// like the source) or Update (new and changed files only); Compare shows what would happen; each sync runs in its
/// own window, so the app stays free (or in the background, on the tab). Saved syncs can also be run from the tray icon's menu.
/// </summary>
public sealed class FileSyncApplet : IApplet
{
    private readonly FileSyncSettings settings = FileSyncSettings.Load();
    private readonly FileSyncView view;
    private readonly List<SyncWindow> windows = new();

    public FileSyncApplet()
    {
        view = new FileSyncView(settings, (job, plan) => Open(job, plan));
        SyncWindow.OpenChanged += (w, open) => { if (open) windows.Add(w); else windows.Remove(w); };
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    /// <summary>A sync's own window: what's about to happen, then its progress.</summary>
    private void Open(SyncJob job, SyncPlan? compared = null)
    {
        // the same two folders already open: that window, not another sync over the same files
        var same = windows.FirstOrDefault(w => !w.Finished && w.Job.Source.Equals(job.Source, StringComparison.OrdinalIgnoreCase) && w.Job.Destination.Equals(job.Destination, StringComparison.OrdinalIgnoreCase));
        if (same != null)
        {
            if (same.InBackground) { same.PopOut(); return; }
            if (same.WindowState == WindowState.Minimized) same.WindowState = WindowState.Normal;
            same.Activate();
            return;
        }
        var w = new SyncWindow(job, settings, compared);
        w.Ran += _ => view.SyncRan();
        w.Backgrounded += view.ShowInBackground;
        w.Show();
    }

    /// <summary>The saved syncs, to run from the tray.</summary>
    public IReadOnlyList<AppletMenuItem>? TrayMenu =>
        settings.Saved.Count == 0 ? null
            : settings.Saved.Select(j => new AppletMenuItem(F("Sync \"{0}\"…", j.Name), () =>
            {
                if (FileSyncView.Problem(j) is { } p) { Log.Here.Warn($"Saved sync {j.Name}: {p}"); MessageBox.Show(p, AppPaths.DisplayName); return; }
                Open(j.Clone());
            })).ToList();

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        foreach (var w in windows.ToList())
        {
            if (w.IsRunning) Log.Here.Info($"Sync stopped as the app closes: {w.Job.Source} → {w.Job.Destination}");
            w.Stop();
        }
        view.Shutdown();
    }
}
