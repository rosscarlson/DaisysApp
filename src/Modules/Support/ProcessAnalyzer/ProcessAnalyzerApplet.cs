using System.Windows;
using System.Windows.Controls;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// Process Analyzer, a sub-tab of the Support tab (since 4.1.0): every running program checked against a database of
/// known ones (the list it comes with, programs classified here, imported, or Daisy's Server's master database), new
/// ones classified and stored, and suggestions for the ones that are risky, likely unnecessary or heavier than they
/// should be (see README.md). While the tab isn't showing it looks every few minutes and notifies about a risky one.
/// </summary>
public sealed class ProcessAnalyzerApplet : IApplet
{
    public ProcessAnalyzerSettings Settings { get; } = ProcessAnalyzerSettings.Load();
    private ProcessAnalyzerView? view;
    private UserControl? settingsView;
    private ServerSync? sync;
    private Timer? watch;
    private Analyzer? watcher;
    private int watching;
    private readonly HashSet<string> warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tab's showing (it looks every 2 s itself then).</summary>
    internal volatile bool ViewShowing;

    internal event Action? ServerChanged;
    internal string ServerStatus => sync?.Status ?? T("Local only");

    public FrameworkElement View => view ??= new ProcessAnalyzerView(this);
    public FrameworkElement? SettingsView => settingsView ??= new ProcessAnalyzerSettingsView(this);

    public void Start()
    {
        Log.Here.Debug($"Process Analyzer: {ProcessDb.Db.Count} programs known, database version {ProcessDb.Db.Version}");
        ApplyServer();
        ApplyWatch();
    }

    /// <summary>Starts or stops the server's sync to match the setting.</summary>
    internal void ApplyServer()
    {
        if (Settings.UseServer && sync == null)
        {
            sync = new ServerSync();
            sync.Changed += () => ServerChanged?.Invoke();
        }
        else if (!Settings.UseServer && sync != null) { sync.Dispose(); sync = null; }
        ServerChanged?.Invoke();
    }

    /// <summary>Starts or stops the look every few minutes to match the setting (the first a minute after the start).</summary>
    internal void ApplyWatch()
    {
        watch?.Dispose();
        watch = null;
        if (!Settings.Watch) return;
        var every = TimeSpan.FromMinutes(Math.Clamp(Settings.WatchMinutes, 1, 120));
        watch = new Timer(_ => Watch(), null, TimeSpan.FromMinutes(1), every);
    }

    private void Watch()
    {
        if (ViewShowing || Interlocked.Exchange(ref watching, 1) == 1) return;
        try
        {
            watcher ??= new Analyzer(Settings);
            var found = watcher.Run(observe: true);
            watcher.LookUpUnknown(found);
            foreach (var a in found.Where(a => a.Threat >= Threat.High && a.Suggestion.Length > 0))
            {
                if (!warned.Add(a.Name)) continue;
                string title = a.Entry.Title.Length > 0 ? a.Entry.Title : a.Name;
                Notifications.Show(T("Process Analyzer"), F("{0} ({1}.exe): {2}", title, a.Name, a.Suggestion));
            }
        }
        catch (Exception ex) { Log.Here.Warn($"Process Analyzer: {ex.Message}"); }
        finally { watching = 0; }
    }

    public void SaveSettings() => Settings.Save();

    public void Dispose()
    {
        watch?.Dispose();
        sync?.Dispose();
        ProcessDb.Db.Flush();
        Settings.Save();
    }
}
