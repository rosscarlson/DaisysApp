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

    // (named after the program: Daisy's App and the wrapper, e.g. Wrapper.exe, each run once, side by side)
    private static string ShowEventName => $@"Local\{AppPaths.ShortName}.Show";
    private static string ExitEventName => $@"Local\{AppPaths.ShortName}.Exit";

    private static Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // build.ps1: the list of modules for a release ("--write-catalog <modules> <zips> <version> <out.json> [names…]")
        if (e.Args.Length >= 5 && e.Args[0] == "--write-catalog")
        {
            int code = 0;
            try { Updates.ModuleStore.WriteCatalog(e.Args[1], e.Args[2], e.Args[3], e.Args[4], e.Args.Skip(5).ToList()); }
            catch (Exception ex) { System.IO.File.WriteAllText(e.Args[4] + ".error.txt", ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        // a copy started to run a module's job (often as administrator): just that, then exit (no window, no single-instance check)
        if (e.Args.Length > 0 && e.Args[0] == ModuleTasks.Argument)
        {
            Shutdown(ModuleTasks.RunHere(e.Args));
            return;
        }
        // the Update task (elevated): the newest release downloaded and installed with no window (a quick update)
        if (e.Args.Length > 0 && e.Args[0] == "--install-update")
        {
            Shutdown(Task.Run(Updates.UpdateService.RunElevatedUpdate).GetAwaiter().GetResult());
            return;
        }

        MainWindow? window = null;
        // exceptions on background threads end the process; at least say why in the log
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            ErrorLog.Write($"Unhandled exception ({AppPaths.DisplayName} is closing)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
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
        singleInstance = new Mutex(true, $@"Local\{AppPaths.ShortName}.SingleInstance", out bool first);
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
        // drawn without the graphics card: Settings → General → Appearance, or "--software" for this run (BlankWindowCheck)
        bool softwareThisTime = e.Args.Contains("--software", StringComparer.OrdinalIgnoreCase);
        if (settings.SoftwareRendering || softwareThisTime)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Log.App.Info("Drawing in software, without the graphics card");
        }
        // (Daisy's App's settings from before its tools became sub-tabs; the wrapper never had them)
        if (AppPaths.IsDaisysApp)
        {
            try { ModuleMoves.Apply(settings); }
            catch (Exception ex) { ErrorLog.Write("Moving settings to the Video and Hardware tabs", ex); }
        }

        // the wrapper: modules downloaded (or taken off) since the last run go in place before anything loads
        if (!AppPaths.IsDaisysApp)
        {
            try { Updates.ModuleStore.ApplyPending(); }
            catch (Exception ex) { ErrorLog.Write("Putting downloaded modules in place", ex); }
        }

        Loc.Init(settings.Language);
        // Settings, General, Storage: the daily clean-up, if it's on
        Shell.Storage.StartAutoClean(settings);
        ThemeManager.Apply(settings.Theme);

        // Every applet in the modules folder, except the ones switched off in Settings → General.
        var applets = new List<IApplet>();
        // (the wrapper with none yet asks which to download, once its window shows)
        var failed = AppletCatalog.All.Count == 0 && AppletCatalog.Failed.Count == 0 && AppPaths.IsDaisysApp
            ? new List<string> { F("No modules found: the modules folder next to {0}.exe is missing or empty. Reinstalling puts them back.", AppPaths.ShortName) }
            : AppletCatalog.Failed.Select(f => F("The {0} module couldn't load: {1}", f.Module, f.Error)).ToList();
        var folderOf = new Dictionary<IApplet, string>();
        // (a module's own sub-tabs switched off in Settings → General → Modules: the modules don't make them)
        ToolSwitches.Off = new HashSet<string>(settings.DisabledTools, StringComparer.OrdinalIgnoreCase);
        bool IsOn(AppletAttribute meta) => AppletCatalog.IsOn(meta, settings);
        foreach (var entry in AppletCatalog.All)
        {
            // (a tab of other modules' applets, Gaming, with every one of them switched off: off too)
            if (!IsOn(entry.Meta) || (entry.Type == null && entry.Tools.Count > 0 && !entry.Tools.Any(t => IsOn(t.Meta))))
            {
                Log.App.Info($"{entry.Meta.Id} is switched off: not loaded");
                continue;
            }
            var log = Log.For(entry.Folder);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var applet = entry.Create(IsOn);
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
        // what each module's sub-tabs are, for Settings → General → Modules to list (kept for when a module is off)
        ModuleTools.Remember(settings, ToolSwitches.Declared);
        var built = System.Diagnostics.Stopwatch.StartNew();
        window = new MainWindow(settings, applets);
        Log.App.Info($"Main window built in {built.ElapsedMilliseconds} ms");
        MainWindow = window;
        HangWatch.Start(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle());

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
        if (softwareThisTime && !settings.SoftwareRendering)
            window.ShowNotice(T("The graphics card left the window blank, so it's drawn without it this time. Resetting the graphics driver (Win+Ctrl+Shift+B) or restarting Windows usually fixes that; if it keeps happening, switch on Draw without the graphics card in Settings → General → Appearance."));
        else if (!settings.SoftwareRendering) BlankWindowCheck.Watch(window);

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
