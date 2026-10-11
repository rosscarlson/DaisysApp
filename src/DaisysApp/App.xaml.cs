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
        if (sessionStarted)
        {
            Log.App.Info($"Exited cleanly (exit code {e.ApplicationExitCode}).");
            ErrorLog.EndSession(); // a second copy that only signalled the first one leaves its marker alone
        }
        Log.Close();
        base.OnExit(e);
    }

    [ThreadStatic] private static bool inFirstChance;

    /// <summary>Debug logging: every exception as it's thrown, even ones that are caught (most are harmless).</summary>
    private static void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
    {
        if (!Log.DebugEnabled || inFirstChance || args.Exception is OperationCanceledException) return;
        inFirstChance = true;
        try
        {
            var ex = args.Exception;
            Log.App.Debug($"Exception thrown (it may be caught): {ex.GetType().FullName}: {ex.Message} (in {ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name})");
        }
        catch { }
        finally { inFirstChance = false; }
    }

    private static void LogStartup(AppSettings settings, string[] args)
    {
        var log = Log.App;
        log.Info($"{AppPaths.DisplayName} {Updates.UpdateService.Display(Updates.UpdateService.CurrentVersion)} starting" +
                 $" (process {Environment.ProcessId}{(args.Length > 0 ? ", arguments " + string.Join(" ", args) : "")})");
        log.Info($"Windows {Environment.OSVersion.Version} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, " +
                 $".NET {Environment.Version}, {Environment.ProcessorCount} logical processors, " +
                 $"culture {System.Globalization.CultureInfo.CurrentCulture.Name}, language {settings.Language}, " +
                 $"logging {(settings.DebugLogging ? "Debug" : "Normal")}");
        log.Info($"Program {Environment.ProcessPath}; settings {AppPaths.SettingsFolder}; logs {Log.Folder}");
        log.Debug(() => "Settings: " + System.Text.Json.JsonSerializer.Serialize(settings));
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
        {
            ErrorLog.Write("Unhandled exception (Daisy's App is closing)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
            Log.Close();
        };
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
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

        // the only copy from here on: start the day's logs, note in them if the last run crashed, and mark this one as running
        var settings = AppSettings.Load();
        Log.DebugEnabled = settings.DebugLogging;
        Log.Prune();
        LogStartup(settings, e.Args);
        ErrorLog.StartSession(Updates.UpdateService.Display(Updates.UpdateService.CurrentVersion));
        sessionStarted = true;

        Loc.Init(settings.Language);
        ThemeManager.Apply(settings.Theme);

        // Every applet in the modules folder, except the ones switched off in Settings → General.
        var applets = new List<IApplet>();
        var failed = AppletCatalog.All.Count == 0 && AppletCatalog.Failed.Count == 0
            ? new List<string> { T("No modules found: the modules folder next to DaisysApp.exe is missing or empty. Reinstalling puts them back.") }
            : AppletCatalog.Failed.Select(f => F("The {0} module couldn't load: {1}", f.Module, f.Error)).ToList();
        var folderOf = new Dictionary<IApplet, string>();
        foreach (var entry in AppletCatalog.All)
        {
            if (!AppletCatalog.IsOn(entry.Meta, settings))
            {
                Log.App.Info($"{entry.Meta.Id} is switched off: not loaded");
                continue;
            }
            var log = Log.For(entry.Folder);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var applet = entry.Create();
                applets.Add(applet);
                folderOf[applet] = entry.Folder;
                log.Info($"{entry.Meta.Id} created in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                log.Error($"{entry.Meta.Id} couldn't be created", ex);
                ErrorLog.Write($"{entry.Meta.Id} create", ex);
                failed.Add(F("{0} couldn't load: {1}", Any(entry.Meta.Title), (ex.InnerException ?? ex).Message));
            }
        }
        var built = System.Diagnostics.Stopwatch.StartNew();
        window = new MainWindow(settings, applets);
        Log.App.Info($"Main window built in {built.ElapsedMilliseconds} ms");
        MainWindow = window;

        foreach (var applet in applets)
        {
            var log = Log.For(folderOf[applet]);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                applet.Start();
                log.Info($"{applet.Meta.Id} started in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                log.Error($"{applet.Meta.Id} couldn't start", ex);
                ErrorLog.Write($"{applet.Meta.Id}.Start", ex);
                failed.Add(F("{0} couldn't start: {1}", Any(applet.Meta.Title), ex.Message));
            }
        }
        if (failed.Count > 0)
        {
            Log.App.Warn("Shown at startup: " + string.Join(" ", failed));
            window.ShowNotice(string.Join(" ", failed));
        }

        bool startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && settings.RunInTray;
        if (!startHidden) window.Show();
        Log.App.Info(startHidden ? "Started hidden in the tray" : "Window shown");

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
