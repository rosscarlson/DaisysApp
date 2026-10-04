using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Settings;
using DaisysApp.Theming;
using DaisysApp.Updates;

namespace DaisysApp.Shell;

/// <summary>The Settings tab: General for the app itself, then a sub-tab per tool that has settings (each tool supplies its own view).</summary>
public partial class SettingsPage : UserControl
{
    private readonly AppSettings settings;
    private readonly MainWindow window;
    private bool updating = true;

    public SettingsPage(AppSettings settings, IReadOnlyList<ITool> tools, MainWindow window)
    {
        this.settings = settings;
        this.window = window;
        InitializeComponent();

        // General first, then a sub-tab for each tool that has settings, in the same order as the main tabs
        var subTabs = new TabStrip(SubTabButtons, SubTabPages);
        ((Panel)GeneralPanel.Parent).Children.Remove(GeneralPanel);
        subTabs.Add("general", "General", "", GeneralPanel);
        foreach (var tool in tools)
            if (tool.SettingsView is { } view) subTabs.Add(tool.Id, tool.Title, tool.Icon, view);
        subTabs.Select(null);

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
