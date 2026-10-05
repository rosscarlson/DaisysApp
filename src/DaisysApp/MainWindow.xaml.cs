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
    private const string SettingsTabId = "settings";

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
        InitializeComponent();
        var v = UpdateService.CurrentVersion;
        Title = $"{AppPaths.DisplayName} v{v.Major}.{v.Minor}" + (v.Build > 0 ? $".{v.Build}" : ""); // e.g. "Daisy's App v0.4"
        RestorePlacement();

        tabs = new TabStrip(TabButtons, TabPages);
        foreach (var applet in applets) tabs.Add(applet.Meta.Id, applet.Meta.Title, applet.Meta.Icon, applet.View);
        tabs.Add(SettingsTabId, "Settings", "", new SettingsPage(settings, applets, this));
        tabs.Selected += id =>
        {
            activeApplet = applets.FirstOrDefault(t => t.Meta.Id == id);
            settings.LastTab = id;
        };
        tabs.Select(settings.LastTab);

        IsVisibleChanged += (_, e) => { if (e.NewValue is true) wasShown = true; };
        ApplyTraySetting();
    }

    // ---------------------------------------------------------------- window

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => activeApplet?.OnPreviewKeyDown(e);

    /// <summary>Applies and saves the theme; every theme picker follows via <see cref="ThemeManager.ThemeChanged"/>.</summary>
    public void SetTheme(ThemeChoice choice)
    {
        if (choice == settings.Theme && choice == ThemeManager.Choice) return;
        settings.Theme = choice;
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
        foreach (var applet in applets)
        {
            try
            {
                applet.SaveSettings();
                applet.Dispose();
            }
            catch (Exception ex) { ErrorLog.Write($"{applet.Meta.Id} shutdown", ex); }
        }
        settings.Save();
        tray?.Dispose();
        tray = null;
    }

    // ---------------------------------------------------------------- banner

    /// <summary>Shows an app-wide message in the banner (no buttons other than Hide).</summary>
    public void ShowNotice(string text)
    {
        BannerText.Text = text;
        BannerIcon.Visibility = Visibility.Collapsed;
        UpdateInstallButton.Visibility = UpdateNotesButton.Visibility = Visibility.Collapsed;
        Banner.Visibility = Visibility.Visible;
    }

    private void BannerDismiss_Click(object sender, RoutedEventArgs e) => Banner.Visibility = Visibility.Collapsed;

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
            if (update != null)
            {
                ShowUpdateAvailable(update);
                if (!IsVisible)
                    tray?.ShowBalloon($"{AppPaths.DisplayName} {UpdateService.Display(update.Version)} is available",
                        "Click here to open the app and install the update.");
            }
            else if (manual) ShowNotice($"You're up to date (version {UpdateService.Display(UpdateService.CurrentVersion)}).");
        }
        catch (Exception ex)
        {
            if (manual) ShowNotice("Couldn't check for updates: " + ex.Message);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private void ShowUpdateAvailable(UpdateInfo update, string? note = null)
    {
        pendingUpdate = update;
        BannerText.Text = note ?? $"Version {UpdateService.Display(update.Version)} is available. You have {UpdateService.Display(UpdateService.CurrentVersion)}.";
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
            BannerText.Text = "Downloading update…";
            var progress = new Progress<double>(f => BannerText.Text = $"Downloading update… {f:P0}");
            string installer = await UpdateService.DownloadAsync(update, progress);

            BannerText.Text = "Installing. The app will restart when it's done.";
            UpdateService.LaunchInstaller(installer); // Windows asks for admin approval here
            ExitApp();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user declined the admin prompt
        {
            ShowUpdateAvailable(update, "Update cancelled. Click Install update to try again.");
        }
        catch (Exception ex)
        {
            ShowUpdateAvailable(update, "Update failed: " + ex.Message);
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
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        SavePlacement();
        Hide();
        foreach (var applet in applets)
        {
            try
            {
                applet.OnWindowHidden();
                applet.SaveSettings();
            }
            catch (Exception ex) { ErrorLog.Write($"{applet.Meta.Id} hide", ex); }
        }
        if (!settings.TrayHintShown && tray != null)
        {
            tray.ShowBalloon($"{AppPaths.DisplayName} is still running",
                "Its applets keep working in the system tray. Right-click the tray icon to exit, or change this under Settings → General.");
            settings.TrayHintShown = true;
        }
        settings.Save();
    }

    /// <summary>Really exit (tray menu, updater, --exit) instead of hiding to the tray.</summary>
    public void ExitApp()
    {
        exiting = true;
        Close();
        ShutDownApplets(); // in case the window was never shown and Closing didn't run
        Application.Current.Shutdown();
    }

    /// <summary>Closes and starts again (e.g. after switching applets on or off). The new copy waits for this one to exit.</summary>
    public void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ShowNotice("Couldn't restart: " + ex.Message);
            return;
        }
        ExitApp();
    }

    public void PrepareForSessionEnd() => exiting = true;
}
