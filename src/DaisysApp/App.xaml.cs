using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Settings;
using DaisysApp.Shell;
using DaisysApp.Theming;

namespace DaisysApp;

public partial class App : Application
{
    private bool sessionStarted;

    protected override void OnExit(ExitEventArgs e)
    {
        if (sessionStarted) ErrorLog.EndSession(); // a second copy that only signalled the first one leaves its marker alone
        base.OnExit(e);
    }

    private const string ShowEventName = @"Local\DaisysApp.Show";
    private const string ExitEventName = @"Local\DaisysApp.Exit";

    private static Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow? window = null;
        // exceptions on background threads end the process; at least say why in the log
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("Unhandled exception (Daisy's App is closing)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write("DispatcherUnhandledException", args.Exception);
            window?.ShowNotice(T("Something went wrong: ") + args.Exception.Message);
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

        // the only copy from here on: note in the log if the last run crashed, and mark this one as running
        ErrorLog.StartSession(Updates.UpdateService.Display(Updates.UpdateService.CurrentVersion));
        sessionStarted = true;

        var settings = AppSettings.Load();
        Loc.Init(settings.Language);
        ThemeManager.Apply(settings.Theme);

        // Every applet in the modules folder, except the ones switched off in Settings → General.
        var applets = new List<IApplet>();
        var failed = AppletCatalog.All.Count == 0 && AppletCatalog.Failed.Count == 0
            ? new List<string> { T("No modules found: the modules folder next to DaisysApp.exe is missing or empty. Reinstalling puts them back.") }
            : AppletCatalog.Failed.Select(f => F("The {0} module couldn't load: {1}", f.Module, f.Error)).ToList();
        foreach (var entry in AppletCatalog.All.Where(a => AppletCatalog.IsOn(a.Meta, settings)))
        {
            try { applets.Add(entry.Create()); }
            catch (Exception ex)
            {
                ErrorLog.Write($"{entry.Meta.Id} create", ex);
                failed.Add(F("{0} couldn't load: {1}", Any(entry.Meta.Title), (ex.InnerException ?? ex).Message));
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
                failed.Add(F("{0} couldn't start: {1}", Any(applet.Meta.Title), ex.Message));
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
