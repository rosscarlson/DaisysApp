using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Settings;
using DaisysApp.Theming;
using DaisysApp.Updates;

namespace DaisysApp.Shell;

/// <summary>The Settings tab: General for the app itself (including which applets are on), then a page per enabled applet that has settings.</summary>
public partial class SettingsPage : UserControl
{
    private readonly AppSettings settings;
    private readonly MainWindow window;
    private bool updating = true;

    public SettingsPage(AppSettings settings, IReadOnlyList<IApplet> applets, MainWindow window)
    {
        this.settings = settings;
        this.window = window;
        InitializeComponent();

        // General first, then a page for each applet that has settings, in the same order as the main tabs
        var subTabs = new TabStrip(SubTabButtons, SubTabPages);
        ((Panel)GeneralPanel.Parent).Children.Remove(GeneralPanel);
        subTabs.Add("general", "General", "", GeneralPanel);
        foreach (var applet in applets)
            if (applet.SettingsView is { } view) subTabs.Add(applet.Meta.Id, applet.Meta.Title, applet.Meta.Icon, view);
        subTabs.Select(null);

        // Applets: every one found in Applets/, with the ones loaded at startup ticked
        loadedDisabled = settings.DisabledApplets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        appletToggles = AppletCatalog.All
            .Select(e => new AppletToggle(e.Meta.Id, e.Meta.Title, e.Meta.Description, !loadedDisabled.Contains(e.Meta.Id)))
            .ToList();
        AppletList.ItemsSource = appletToggles;

        VersionText.Text = $"{AppPaths.DisplayName} version {UpdateService.Display(UpdateService.CurrentVersion)}";
        SettingsFolderText.Text = AppPaths.SettingsFolder;
        LogFolderText.Text = AppPaths.LogFolder;

        TrayBox.IsChecked = settings.RunInTray;
        StartupBox.IsChecked = SafeIsStartupEnabled();
        StartHiddenBox.IsChecked = settings.StartHidden;
        AutoUpdateBox.IsChecked = settings.AutoCheckUpdates;
        ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        UpdateDependentOptions();
        updating = false;

        ThemeManager.ThemeChanged += () => ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        window.UpdateBusyChanged += busy => CheckNowButton.IsEnabled = !busy;
    }

    // ---------------------------------------------------------------- applets

    /// <summary>One row of the Applets card (bound to its checkbox).</summary>
    public sealed class AppletToggle(string id, string title, string description, bool enabled)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Description { get; } = description;
        public bool Enabled { get; set; } = enabled;
    }

    private readonly List<AppletToggle> appletToggles;
    private readonly HashSet<string> loadedDisabled; // what this run started with

    private void Applet_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        settings.DisabledApplets = appletToggles.Where(t => !t.Enabled).Select(t => t.Id).ToList();
        settings.Save();
        // takes effect on the next start; offer a restart while it differs from what's loaded
        bool changed = !settings.DisabledApplets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(loadedDisabled);
        RestartRow.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e) => window.Restart();

    // ---------------------------------------------------------------- startup and tray

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupManager.IsEnabled(); }
        catch { return false; }
    }

    private void UpdateDependentOptions() =>
        StartHiddenBox.IsEnabled = TrayBox.IsChecked == true && StartupBox.IsChecked == true;

    /// <summary>Writes (or removes) the sign-in entry so it matches the three checkboxes.</summary>
    private void ApplyStartup()
    {
        StartupError.Visibility = Visibility.Collapsed;
        try
        {
            StartupManager.Set(StartupBox.IsChecked == true, hidden: settings.RunInTray && settings.StartHidden);
        }
        catch (Exception ex)
        {
            StartupError.Text = "Couldn't change the startup setting: " + ex.Message;
            StartupError.Visibility = Visibility.Visible;
        }
    }

    private void TrayBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        settings.RunInTray = TrayBox.IsChecked == true;
        settings.Save();
        window.ApplyTraySetting();
        UpdateDependentOptions();
        if (StartupBox.IsChecked == true) ApplyStartup(); // "start hidden" only works with the tray on
    }

    private void StartupBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        UpdateDependentOptions();
        ApplyStartup();
    }

    private void StartHiddenBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        settings.StartHidden = StartHiddenBox.IsChecked == true;
        settings.Save();
        if (StartupBox.IsChecked == true) ApplyStartup();
    }

    // ---------------------------------------------------------------- appearance

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        window.SetTheme((ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex)); // items: Dark, Light, System
    }

    // ---------------------------------------------------------------- updates

    private void AutoUpdateBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        settings.AutoCheckUpdates = AutoUpdateBox.IsChecked == true;
        settings.Save();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await window.CheckForUpdatesAsync(manual: true);

    private void Releases_Click(object sender, RoutedEventArgs e) => UpdateService.OpenInBrowser(UpdateService.RepoUrl + "/releases");

    // ---------------------------------------------------------------- files

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.SettingsFolder);
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogFolder);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            window.ShowNotice("Couldn't open the folder: " + ex.Message);
        }
    }
}
