using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Settings;
using DaisysApp.Shell;
using DaisysApp.Theming;

namespace DaisysApp;

public partial class App : Application
{
    private const string ShowEventName = @"Local\DaisysApp.Show";
    private const string ExitEventName = @"Local\DaisysApp.Exit";

    private static Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow? window = null;
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write("DispatcherUnhandledException", args.Exception);
            window?.ShowNotice("Something went wrong: " + args.Exception.Message);
            args.Handled = true;
        };

        // Single instance: a second launch just brings the running one to the front.
        singleInstance = new Mutex(true, @"Local\DaisysApp.SingleInstance", out bool first);
        bool exitRequest = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        if (!first && e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
        {
            // Settings → Restart now: wait for the previous copy to finish closing, then carry on as the only instance.
            try { first = singleInstance.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { first = true; } // it exited without releasing: the mutex is ours now
        }
        if (!first)
        {
            // "DaisysApp.exe --exit" closes the running instance cleanly (used by the uninstaller); otherwise bring it forward.
            try { using var signal = EventWaitHandle.OpenExisting(exitRequest ? ExitEventName : ShowEventName); signal.Set(); } catch { }
            Shutdown();
            return;
        }
        if (exitRequest)
        {
            // We just became the only instance, so there was nothing running to exit.
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        ThemeManager.Apply(settings.Theme);

        // Every applet found in Applets/, except the ones switched off in Settings → General.
        var applets = new List<IApplet>();
        var failed = new List<string>();
        foreach (var entry in AppletCatalog.All.Where(a => !settings.DisabledApplets.Contains(a.Meta.Id)))
        {
            try { applets.Add(entry.Create()); }
            catch (Exception ex)
            {
                ErrorLog.Write($"{entry.Meta.Id} create", ex);
                failed.Add($"{entry.Meta.Title} couldn't load: {(ex.InnerException ?? ex).Message}");
            }
        }
        window = new MainWindow(settings, applets);
        MainWindow = window;

        foreach (var applet in applets)
        {
            try { applet.Start(); }
            catch (Exception ex)
            {
                ErrorLog.Write($"{applet.Meta.Id}.Start", ex);
                failed.Add($"{applet.Meta.Title} couldn't start: {ex.Message}");
            }
        }
        if (failed.Count > 0) window.ShowNotice(string.Join(" ", failed));

        bool startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && settings.RunInTray;
        if (!startHidden) window.Show();

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        new Thread(() =>
        {
            var handles = new WaitHandle[] { showEvent, exitEvent };
            while (true)
            {
                if (WaitHandle.WaitAny(handles) == 0) Dispatcher.BeginInvoke(window.ShowFromTray);
                else Dispatcher.BeginInvoke(window.ExitApp);
            }
        }) { IsBackground = true, Name = "DaisysApp instance listener" }.Start();

        // Restart Manager (installer / auto-update) and Windows shutdown must be able to close the app.
        SessionEnding += (_, _) => window.PrepareForSessionEnd();

        if (settings.AutoCheckUpdates) _ = window.CheckForUpdatesAsync(manual: false);
    }
}
