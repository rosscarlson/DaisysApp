using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Logging;
using DaisysApp.Settings;
using DaisysApp.Shell;
using DaisysApp.Theming;
using DaisysApp.Updates;

namespace DaisysApp;

/// <summary>The shell: update banner, one tab per enabled applet plus Settings, tray icon and auto-update.</summary>
public partial class MainWindow : Window
{
    public const string SettingsTabId = "settings";

    private readonly AppSettings settings;
    private readonly IReadOnlyList<IApplet> applets;
    private readonly TabStrip tabs;
    private IApplet? activeApplet;
    private TrayIcon? tray;
    private bool exiting;
    private bool shutDown;
    private bool wasShown;

    public MainWindow(AppSettings settings, IReadOnlyList<IApplet> applets)
    {
        this.settings = settings;
        this.applets = applets;
        var sw = Stopwatch.StartNew();
        void Step(string what) { Log.App.Debug($"Main window: {what} at {sw.ElapsedMilliseconds} ms"); }
        TabNames.Init(settings);
        InitializeComponent();
        noticeTimer.Tick += (_, _) => FadeNotice();
        Step("built");
        var v = UpdateService.CurrentVersion;
        Title = $"{AppPaths.DisplayName} v{v.Major}.{v.Minor}" + (v.Build > 0 ? $".{v.Build}" : ""); // e.g. "Daisy's App v0.4"
        // modules' status bubbles after the title (ForzaWheel running, a Joy2Key profile in use)
        _ = new TitleBadgeStrip(this);
        // modules' notifications: from the tray icon, or a notice in the window without one
        Notifications.Requested += (title, text) => Dispatcher.BeginInvoke(() =>
        {
            if (tray != null) tray.ShowBalloon(title, text);
            else ShowNotice(title + ": " + text, NoticeKind.Info);
        });
        RestorePlacement();

        tabs = new TabStrip(TabButtons, TabPages);
        // in the order the user dragged them into (an applet that's new since then goes after them)
        var ordered = applets.OrderBy(a => settings.TabOrder.FindIndex(id => id.Equals(a.Meta.Id, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : int.MaxValue).ToList();
        loadedOrder = ordered.Select(a => a.Meta.Id).ToList();
        // (an applet shown in Settings is a sub-tab there instead)
        foreach (var applet in ordered.Where(a => !a.Meta.InSettings)) tabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, applet.View, movable: true);
        tabs.Reordered += order =>
        {
            // keep switched-off applets' places too
            settings.TabOrder = order.Concat(settings.TabOrder.Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
            loadedOrder = order;
            settings.Save();
        };
        Step("applet tabs added");
        var settingsPage = new SettingsPage(settings, applets, this);
        Step("Settings page built");
        tabs.Add(SettingsTabId, T("Settings"), "", settingsPage);
        UpdateBusyChanged += busy => UpdateButton.IsEnabled = !busy;
        AppNavigation.SettingsRequested += (id, element) =>
        {
            tabs.Select(SettingsTabId);
            settingsPage.ShowPage(id, element);
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        };
        AppNavigation.TabOpener = id =>
        {
            if (applets.FirstOrDefault(a => a.Meta.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is not { } target)
            {
                // a tool on a group's sub-tab (e.g. Logs, on the Network tab)
                if (applets.OfType<AppletGroup>().FirstOrDefault(g => g.ToolFor(id) != null) is not { } group) return false;
                tabs.Select(((IApplet)group).Meta.Id);
                group.Select(group.Tools.First(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Id);
                ShowFromTray();
                return true;
            }
            if (target.Meta.InSettings)
            {
                tabs.Select(SettingsTabId);
                settingsPage.ShowPage(target.Meta.Id, null);
            }
            else tabs.Select(target.Meta.Id);
            ShowFromTray();
            return true;
        };
        // an applet's own setup can switch it off: saved now, and offered a restart
        AppNavigation.AppletSwitcher = id =>
        {
            if (AppletCatalog.All.FirstOrDefault(e => e.Meta.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is not { } entry) return;
            AppletCatalog.SaveOnOff(settings, new[] { (entry.Meta, false) });
            string name = TabNames.For(entry.Meta.Id, Any(entry.Meta.Title));
            Log.App.Info($"{entry.Meta.Id} switched off from its own setup");
            if (MessageBox.Show(this, F("{0} is switched off. Restart {1} now to remove its tab? (Settings → General → Modules switches it back on.)", name, AppPaths.DisplayName),
                    AppPaths.DisplayName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Dispatcher.BeginInvoke(Restart);
        };
        tabs.Selected += id =>
        {
            activeApplet = applets.FirstOrDefault(t => t.Meta.Id == id);
            settings.LastTab = id;
            Log.App.Debug($"Tab opened: {id}");
        };
        tabs.Select(settings.LastTab);
        Step("last tab selected");

        IsVisibleChanged += (_, e) => { if (e.NewValue is true) wasShown = true; };
        // the first time the window shows (at launch, or later from the tray): the setup wizard if it's due (in full
        // after installing; only what's required when an applet is missing a setting it can't do without; not after
        // every update), then any applet's own first-run setup
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            AfterQuickUpdate();
            // the wrapper: which tools to download (none yet), or its tools fetched again for its new version
            if (!AppPaths.IsDaisysApp && await WrapperModulesAsync()) return;
            var due = SetupWindow.WhenDue(settings, applets);
            if (due != SetupWindow.Due.None) RunSetup(full: due == SetupWindow.Due.Full);
            else RunOnboarding();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ApplyTraySetting();
        Step("tray icon ready");
    }

    private List<string> loadedOrder = new();
    private bool setupOpen;

    /// <summary>
    /// The wrapper at start: with no modules yet, asks which to download; with modules from another version (it was
    /// updated), fetches this version's and restarts. True when it took over (the setup wizard waits until next time).
    /// </summary>
    private async Task<bool> WrapperModulesAsync()
    {
        if (AppletCatalog.All.Count == 0 && AppletCatalog.Failed.Count == 0)
        {
            ChooseModules();
            return true;
        }
        var old = ModuleStore.OutOfDate();
        if (old.Count == 0) return false;
        Log.App.Info($"Modules from another version ({string.Join(", ", old)}): fetching {ModuleStore.AppVersion}'s");
        ShowNotice(F("Updating your tools to version {0}…", ModuleStore.AppVersion), NoticeKind.Info);
        try
        {
            var listed = await ModuleStore.FetchAsync();
            var get = listed.Where(m => old.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            // (one no longer listed stays as it is)
            if (get.Count == 0) return false;
            await ModuleStore.StageAsync(get, Array.Empty<string>(), null);
            Restart();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Updating the modules", ex);
            ShowNotice(F("Your tools couldn't be updated to version {0}: {1}", ModuleStore.AppVersion, ex.Message));
            return false;
        }
    }

    /// <summary>The wrapper's "choose your tools" window; restarts if anything changed.</summary>
    public void ChooseModules()
    {
        if (!IsVisible) Show();
        var picker = new ModulePicker { Owner = this };
        if (picker.ShowDialog() == true && picker.Changed) Restart();
    }

    /// <summary>
    /// The setup wizard: in full (after installing, and from Settings → General, every step whatever is set), or only
    /// the pages of applets missing a required setting. Restarts the app if the choices need it.
    /// </summary>
    public void RunSetup(bool full = true)
    {
        if (setupOpen) return;
        setupOpen = true;
        try
        {
            Log.App.Info(full ? "Setup wizard opened" : "Setup wizard opened for what's required");
            var wizard = new SetupWindow(settings, loadedOrder, applets, full) { Owner = this };
            bool finished = wizard.ShowDialog() == true;
            if (!finished)
            {
                // closed without finishing: it was shown, so it doesn't come back by itself (Settings has it)
                SetupWindow.MarkDone(settings);
                settings.Save();
            }
            if (finished && wizard.NeedsRestart) { Restart(); return; }
        }
        catch (Exception ex) { ErrorLog.Write("Setup wizard", ex); }
        finally { setupOpen = false; }
        RunOnboarding();
    }

    /// <summary>Each loaded applet's own first-run setup, for the ones that say they need it.</summary>
    private void RunOnboarding()
    {
        foreach (var applet in applets)
        {
            try
            {
                if (!applet.NeedsOnboarding) continue;
                LogOf(applet).Info($"{applet.Meta.Id}: running its first-run setup");
                applet.RunOnboarding(this);
            }
            catch (Exception ex) { ErrorLog.Write($"{applet.Meta.Id} onboarding", ex); }
        }
    }

    // ---------------------------------------------------------------- window

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => activeApplet?.OnPreviewKeyDown(e);

    /// <summary>Applies and saves the theme; every theme picker follows via <see cref="ThemeManager.ThemeChanged"/>.</summary>
    public void SetTheme(ThemeChoice choice)
    {
        if (choice == settings.Theme && choice == ThemeManager.Choice) return;
        settings.Theme = choice;
        Log.App.Info($"Theme set to {choice}");
        ThemeManager.Apply(choice);
        settings.Save();
    }

    private void RestorePlacement()
    {
        if (settings.WindowWidth <= 0 || settings.WindowHeight <= 0)
        {
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
            return;
        }

        Width = Math.Max(settings.WindowWidth, MinWidth);
        Height = Math.Max(settings.WindowHeight, MinHeight);
        // Only restore the position if the title bar would still be on a connected screen.
        bool onScreen = settings.WindowX + Width > SystemParameters.VirtualScreenLeft + 100
                     && settings.WindowX < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                     && settings.WindowY >= SystemParameters.VirtualScreenTop
                     && settings.WindowY < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.WindowX;
            Top = settings.WindowY;
        }
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        // A tray-only session that never showed the window has nothing real to report.
        if (!wasShown) return;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowX = (int)Left;
            settings.WindowY = (int)Top;
            settings.WindowWidth = (int)Width;
            settings.WindowHeight = (int)Height;
            settings.WindowMaximized = false;
        }
        else
        {
            settings.WindowMaximized = WindowState == WindowState.Maximized;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exiting && settings.RunInTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        ShutDownApplets();
    }

    /// <summary>Saves everything and releases the applets and the tray icon. Safe to call more than once.</summary>
    private void ShutDownApplets()
    {
        if (shutDown) return;
        shutDown = true;
        SavePlacement();
        Log.App.Info("Shutting down the applets");
        foreach (var applet in applets)
        {
            var log = LogOf(applet);
            try
            {
                var sw = Stopwatch.StartNew();
                applet.SaveSettings();
                applet.Dispose();
                log.Info($"{applet.Meta.Id} saved and stopped in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                log.Error($"{applet.Meta.Id} couldn't shut down cleanly", ex);
                ErrorLog.Write($"{applet.Meta.Id} shutdown", ex);
            }
        }
        settings.Save();
        tray?.Dispose();
        tray = null;
    }

    // ---------------------------------------------------------------- banner

    private static Logger LogOf(IApplet applet) => Log.For(Log.NameOf(applet.GetType().Assembly));

    /// <summary>Shows an app-wide message in the banner (no buttons other than Hide).</summary>
    /// <summary>What a message in the notification bar is: information fades away (after <see cref="AppSettings.NotificationSeconds"/>);
    /// something in progress, or a problem, stays until it's replaced or closed.</summary>
    public enum NoticeKind { Info, Progress, Problem }

    private readonly System.Windows.Threading.DispatcherTimer noticeTimer = new();

    /// <summary>Shows a message in the notification bar (the bar at the top of the window).</summary>
    public void ShowNotice(string text, NoticeKind kind = NoticeKind.Problem)
    {
        StopFade();
        BannerText.Text = text;
        BannerIcon.Visibility = Visibility.Collapsed;
        UpdateInstallButton.Visibility = UpdateNotesButton.Visibility = Visibility.Collapsed;
        Banner.Visibility = Visibility.Visible;
        if (kind == NoticeKind.Info && settings.NotificationSeconds > 0)
        {
            noticeTimer.Interval = TimeSpan.FromSeconds(settings.NotificationSeconds);
            noticeTimer.Start();
        }
    }

    /// <summary>The bar fading away (half a second), then closed.</summary>
    private void FadeNotice()
    {
        noticeTimer.Stop();
        if (Banner.Visibility != Visibility.Visible) return;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
        fade.Completed += (_, _) =>
        {
            if (Banner.Opacity > 0.01) return; // (a new message came meanwhile)
            Banner.Visibility = Visibility.Collapsed;
            StopFade();
        };
        Banner.BeginAnimation(OpacityProperty, fade);
    }

    private void StopFade()
    {
        noticeTimer.Stop();
        Banner.BeginAnimation(OpacityProperty, null);
        Banner.Opacity = 1;
    }

    private void BannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        StopFade();
        Banner.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- updates

    private UpdateInfo? pendingUpdate;
    private bool updateBusy;

    /// <summary>Raised when a check or install starts or ends, so other "Check for updates" buttons can follow.</summary>
    public event Action<bool>? UpdateBusyChanged;

    private void SetUpdateBusy(bool busy)
    {
        updateBusy = busy;
        UpdateBusyChanged?.Invoke(busy);
    }

    /// <summary>Automatic checks stay silent unless an update exists; manual checks always report.</summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (updateBusy) return;
        SetUpdateBusy(true);
        try
        {
            var update = await UpdateService.CheckAsync();
            Log.App.Info(update != null ? $"Update check: {UpdateService.Display(update.Version)} is available ({update.AssetName})" : $"Update check ({(manual ? "manual" : "automatic")}): up to date");
            if (update != null)
            {
                ShowUpdateAvailable(update);
                if (!IsVisible)
                    tray?.ShowBalloon(F("{0} {1} is available", AppPaths.DisplayName, UpdateService.Display(update.Version)),
                        T("Click here to open the app and install the update."));
            }
            else if (manual) ShowNotice(F("You're up to date (version {0}).", UpdateService.Display(UpdateService.CurrentVersion)), NoticeKind.Info);
        }
        catch (Exception ex)
        {
            Log.App.Warn("Update check failed", ex);
            if (manual) ShowNotice(T("Couldn't check for updates: ") + ex.Message);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    // ---- the quick update: the Update button

    private void Update_Click(object sender, RoutedEventArgs e) => _ = QuickUpdateAsync();

    /// <summary>
    /// The Update button (right of the tabs): checks for a newer version and, if there is one, installs it with as little as
    /// possible: the Update task downloads and installs it with no window and no admin question, the app closes and
    /// starts again by itself, and the setup wizard is skipped. Where the task isn't there (installed before 0.44),
    /// Windows asks for admin approval once.
    /// </summary>
    public async Task QuickUpdateAsync()
    {
        if (updateBusy) return;
        SetUpdateBusy(true);
        ShowNotice(T("Checking for updates…"), NoticeKind.Progress);
        UpdateInfo? update;
        try { update = await UpdateService.CheckAsync(); }
        catch (Exception ex)
        {
            Log.App.Warn("Quick update: the check failed", ex);
            ShowNotice(T("Couldn't check for updates: ") + ex.Message);
            SetUpdateBusy(false);
            return;
        }
        if (update == null)
        {
            Log.App.Info("Quick update: up to date");
            ShowNotice(F("You're up to date (version {0}).", UpdateService.Display(UpdateService.CurrentVersion)), NoticeKind.Info);
            SetUpdateBusy(false);
            return;
        }
        string version = UpdateService.Display(update.Version);
        Log.App.Info($"Quick update to {version}");
        settings.QuickUpdateTo = version;
        settings.Save();
        try
        {
            if (await UpdateWithTaskAsync(version)) return; // installing: the app closes
            await UpdateWithPromptAsync(update);
        }
        catch (Exception ex)
        {
            Log.App.Error("Quick update failed", ex);
            settings.QuickUpdateTo = null;
            settings.Save();
            ShowUpdateAvailable(update, T("Update failed: ") + ex.Message);
        }
        finally { SetUpdateBusy(false); }
    }

    /// <summary>The Update task (no admin question), its progress shown; true once it's installing (the app has closed).</summary>
    private async Task<bool> UpdateWithTaskAsync(string version)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        if (!UpdateService.StartUpdateTask())
        {
            Log.App.Info("Quick update: no Update task (installed before 0.44?): Windows will ask");
            return false;
        }
        var lastSeen = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(300);
            var s = UpdateService.ReadStatus();
            if (s == null || s.At < started)
            {
                if ((DateTime.UtcNow - lastSeen).TotalSeconds > 20) { Log.App.Warn("Quick update: the Update task didn't start: Windows will ask"); return false; }
                continue;
            }
            if ((DateTime.UtcNow - s.At).TotalSeconds < 60) lastSeen = DateTime.UtcNow;
            else if ((DateTime.UtcNow - lastSeen).TotalSeconds > 60) throw new TimeoutException(T("The update stopped answering."));
            switch (s.Stage)
            {
                case "checking": ShowNotice(F("Getting version {0}…", version), NoticeKind.Progress); break;
                case "downloading": ShowNotice(F("Downloading version {0}… {1:P0}", version, s.Progress), NoticeKind.Progress); break;
                case "uptodate":
                    ShowNotice(F("You're up to date (version {0}).", UpdateService.Display(UpdateService.CurrentVersion)), NoticeKind.Info);
                    settings.QuickUpdateTo = null;
                    settings.Save();
                    return true;
                case "failed": throw new InvalidOperationException(s.Error ?? T("The update failed."));
                case "installing":
                    ShowNotice(F("Installing version {0}: {1} starts again by itself in a moment.", version, AppPaths.DisplayName), NoticeKind.Progress);
                    Log.App.Info($"Quick update: {version} installing; closing for it");
                    await Task.Delay(400);
                    ExitApp();
                    return true;
            }
        }
    }

    /// <summary>Without the task: downloaded here, then the installer with no window (Windows asks for admin approval once).</summary>
    private async Task UpdateWithPromptAsync(UpdateInfo update)
    {
        string version = UpdateService.Display(update.Version);
        var progress = new Progress<double>(f => ShowNotice(F("Downloading version {0}… {1:P0}", version, f), NoticeKind.Progress));
        string installer = await UpdateService.DownloadAsync(update, progress);
        ShowNotice(F("Installing version {0}: {1} starts again by itself in a moment.", version, AppPaths.DisplayName), NoticeKind.Progress);
        try { UpdateService.LaunchInstaller(installer, quiet: true); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // admin approval declined
        {
            Log.App.Info("Quick update cancelled at the admin prompt");
            settings.QuickUpdateTo = null;
            settings.Save();
            ShowUpdateAvailable(update, T("Update cancelled. Click Install update to try again."));
            return;
        }
        Log.App.Info($"Quick update: installer for {version} started (with the admin prompt)");
        ExitApp();
    }

    /// <summary>
    /// After a quick update: the setup wizard is skipped (marked done for this version) unless it has something new
    /// to show; the tools' own first-run setup still runs for anything missing. False when this start isn't one.
    /// </summary>
    private bool AfterQuickUpdate()
    {
        if (settings.QuickUpdateTo is not { } to) return false;
        settings.QuickUpdateTo = null;
        string now = UpdateService.Display(UpdateService.CurrentVersion);
        if (to != now)
        {
            Log.App.Warn($"Quick update to {to} didn't happen (this is {now})");
            settings.Save();
            return false;
        }
        settings.Save();
        Log.App.Info($"Updated to {now} (quick update)");
        ShowNotice(F("Updated to version {0}.", now), NoticeKind.Info);
        return true;
    }

    private void ShowUpdateAvailable(UpdateInfo update, string? note = null)
    {
        StopFade();
        pendingUpdate = update;
        BannerText.Text = note ?? F("Version {0} is available. Running version {1}.", UpdateService.Display(update.Version), UpdateService.Display(UpdateService.CurrentVersion));
        BannerIcon.Visibility = Visibility.Visible;
        UpdateInstallButton.Visibility = UpdateNotesButton.Visibility = Visibility.Visible;
        UpdateInstallButton.IsEnabled = UpdateNotesButton.IsEnabled = true;
        Banner.Visibility = Visibility.Visible;
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate is not { } update || updateBusy) return;
        SetUpdateBusy(true);
        UpdateInstallButton.IsEnabled = UpdateNotesButton.IsEnabled = false;
        try
        {
            BannerText.Text = T("Downloading update…");
            var progress = new Progress<double>(f => BannerText.Text = F("Downloading update… {0:P0}", f));
            string installer = await UpdateService.DownloadAsync(update, progress);

            Log.App.Info($"Update downloaded to {installer}: starting the installer");
            BannerText.Text = T("Installing. The app will restart when it's done.");
            UpdateService.LaunchInstaller(installer); // Windows asks for admin approval here
            ExitApp();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user declined the admin prompt
        {
            Log.App.Info("Update cancelled at the admin prompt");
            ShowUpdateAvailable(update, T("Update cancelled. Click Install update to try again."));
        }
        catch (Exception ex)
        {
            Log.App.Error("Update failed", ex);
            ShowUpdateAvailable(update, T("Update failed: ") + ex.Message);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private void UpdateNotes_Click(object sender, RoutedEventArgs e) =>
        UpdateService.OpenInBrowser(pendingUpdate?.ReleaseUrl ?? UpdateService.RepoUrl + "/releases");

    // ---------------------------------------------------------------- tray / exit

    /// <summary>Creates or removes the tray icon to match <see cref="AppSettings.RunInTray"/>.</summary>
    public void ApplyTraySetting()
    {
        if (settings.RunInTray && tray == null)
        {
            tray = new TrayIcon(applets);
            tray.OpenRequested += () => Dispatcher.BeginInvoke(ShowFromTray);
            tray.CheckUpdatesRequested += () => Dispatcher.BeginInvoke(() =>
            {
                ShowFromTray();
                _ = CheckForUpdatesAsync(manual: true);
            });
            tray.ExitRequested += () => Dispatcher.BeginInvoke(ExitApp);
        }
        else if (!settings.RunInTray && tray != null)
        {
            tray.Dispose();
            tray = null;
        }
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Log.App.Info("Window shown");
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        SavePlacement();
        Hide();
        Log.App.Info("Window hidden to the tray");
        foreach (var applet in applets)
        {
            try
            {
                applet.OnWindowHidden();
                applet.SaveSettings();
                LogOf(applet).Debug($"{applet.Meta.Id}: window hidden, settings saved");
            }
            catch (Exception ex)
            {
                LogOf(applet).Error($"{applet.Meta.Id} failed when the window was hidden", ex);
                ErrorLog.Write($"{applet.Meta.Id} hide", ex);
            }
        }
        if (!settings.TrayHintShown && tray != null)
        {
            tray.ShowBalloon(F("{0} is still running", AppPaths.DisplayName),
                T("Its modules keep working in the system tray. Right-click the tray icon to exit, or change this under Settings → General."));
            settings.TrayHintShown = true;
        }
        settings.Save();
    }

    /// <summary>Really exit (tray menu, updater, --exit) instead of hiding to the tray.</summary>
    public void ExitApp()
    {
        if (!exiting) Log.App.Info("Exiting");
        exiting = true;
        Close();
        ShutDownApplets(); // in case the window was never shown and Closing didn't run
        Application.Current.Shutdown();
    }

    /// <summary>Closes and starts again (e.g. after switching applets on or off), with <paramref name="more"/> arguments
    /// if any. The new copy waits for this one to exit.</summary>
    public void Restart(string? more = null)
    {
        try
        {
            Log.App.Info("Restarting");
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, more == null ? "--restart" : "--restart " + more) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.App.Error("Couldn't restart", ex);
            ShowNotice(T("Couldn't restart: ") + ex.Message);
            return;
        }
        ExitApp();
    }

    public void PrepareForSessionEnd()
    {
        Log.App.Info("Windows is signing out or shutting down");
        exiting = true;
    }
}
