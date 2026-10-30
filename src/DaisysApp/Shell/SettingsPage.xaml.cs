using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Settings;
using DaisysApp.Theming;
using DaisysApp.Updates;

namespace DaisysApp.Shell;

/// <summary>The Settings tab: General for the app itself (including which modules and sub-modules are on), Shortcuts (every module's), then a page per enabled module that has settings.</summary>
public partial class SettingsPage : UserControl
{
    private readonly AppSettings settings;
    private readonly MainWindow window;
    private readonly SavedIndicator saved;
    private readonly TabStrip subTabs;
    private readonly IReadOnlyList<IApplet> applets;
    private bool updating = true;

    public SettingsPage(AppSettings settings, IReadOnlyList<IApplet> applets, MainWindow window)
    {
        this.settings = settings;
        this.window = window;
        this.applets = applets;
        InitializeComponent();

        // General first, then a page for each applet that has settings, in the same order as the main tabs
        subTabs = new TabStrip(SubTabButtons, SubTabPages);
        subTabs.Add("general", T("General"), "", Scrolling(GeneralPanel), renamable: false);
        // every module's shortcuts in one list (Core's Hotkeys)
        subTabs.Add("shortcuts", T("Shortcuts"), "", Scrolling(new ShortcutsPage()), renamable: false);
        foreach (var applet in applets)
            if (applet.SettingsView is { } view) subTabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, Scrolling(view));
        // then the applets shown here rather than as tabs (none since Logs went to the Network tab in 0.39), last, filling the page
        foreach (var applet in applets.Where(a => a.Meta.InSettings))
            subTabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, applet.View);
        subTabs.Select(null);
        saved = new SavedIndicator(SubTabPages); // every page saves as you change it: say so
        foreach (var applet in applets.Where(a => a.Meta.InSettings)) saved.Skip.Add(applet.View); // (not a settings page)

        // Modules: every one in the modules folder, with their sub-modules, the ones loaded at startup ticked
        BuildModules();
        // one module: nothing to switch on or off, nor a setup to run unless the tool has a step
        if (AppletCatalog.All.Count <= 1 && !applets.Any(HasSetupPage) && !ModuleList.Children.OfType<FrameworkElement>().Any(r => r.Tag is "subs")) ModulesCard.Visibility = Visibility.Collapsed;
        // the wrapper: its tools are downloaded, and more can be
        if (!AppPaths.IsDaisysApp)
        {
            ModulesCard.Visibility = Visibility.Visible;
            ToolsButton.Visibility = Visibility.Visible;
        }

        VersionText.Text = F("{0} version {1}", AppPaths.DisplayName, UpdateService.Display(UpdateService.CurrentVersion));
        StorageCard.Child = new StoragePanel(settings);
        // Daisy's Server: Core's card, for every module that uses the server
        GeneralPanel.Children.Insert(GeneralPanel.Children.IndexOf(StorageCard), new Server.ServerSettingsView());
        SettingsFolderText.Text = AppPaths.SettingsFolder;
        LogFolderText.Text = AppPaths.LogFolder;
        LogLevelBox.SelectedIndex = settings.DebugLogging ? 1 : 0;

        SoftwareBox.IsChecked = settings.SoftwareRendering;
        TrayBox.IsChecked = settings.RunInTray;
        StartupBox.IsChecked = SafeIsStartupEnabled();
        StartHiddenBox.IsChecked = settings.StartHidden;
        AutoUpdateBox.IsChecked = settings.AutoCheckUpdates;
        ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        foreach (int sec in NotificationChoices)
            NotificationBox.Items.Add(new ComboBoxItem { Content = sec == 0 ? T("Never (close it myself)") : P(sec, "{0} second", "{0} seconds"), Tag = sec });
        NotificationBox.SelectedItem = NotificationBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.NotificationSeconds)
            ?? NotificationBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == 8);
        foreach (var (code, name) in Loc.Available()) LanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Language)
            ?? LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Loc.English);
        UpdateDependentOptions();
        updating = false;

        ThemeManager.ThemeChanged += () => ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        window.UpdateBusyChanged += busy => CheckNowButton.IsEnabled = !busy;
    }

    /// <summary>A settings page in its own scroll area, the whole width (a gap on the right for the scroll bar).</summary>
    private static ScrollViewer Scrolling(FrameworkElement page)
    {
        if (page.Parent is Panel old) old.Children.Remove(page);
        page.Margin = new Thickness(0, 0, 10, 0);
        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static bool HasSetupPage(IApplet a)
    {
        try { return a.SetupPage != null; }
        catch { return false; }
    }

    /// <summary>
    /// Shows an applet's page, scrolled to the element with that x:Name on it (AppNavigation.OpenSettings). A tool on a
    /// group's sub-tab (the Resizer, on the Video tab) can be named too: its group's page opens at its sub-tab.
    /// </summary>
    public void ShowPage(string appletId, string? elementName)
    {
        if (!applets.Any(a => a.Meta.Id.Equals(appletId, StringComparison.OrdinalIgnoreCase))
            && applets.OfType<AppletGroup>().FirstOrDefault(g => g.ToolFor(appletId) != null) is { } group)
        {
            (group.SettingsView as SubTabs)?.Select(group.Tools.First(t => t.Id.Equals(appletId, StringComparison.OrdinalIgnoreCase)).Id);
            appletId = group.Meta.Id;
        }
        subTabs.Select(appletId);
        if (elementName == null || applets.FirstOrDefault(a => a.Meta.Id == appletId)?.SettingsView is not { } view) return;
        // a group's page (Video, Hardware): the element is on one of its sub-tabs, which is shown first
        var target = view.FindName(elementName) as FrameworkElement ?? (view as SubTabs)?.Reveal(elementName);
        if (target == null) return;
        // after the page has been laid out, or there's nowhere to scroll to yet
        Dispatcher.BeginInvoke(() =>
        {
            // to the top of the page's scroll area, not just into view
            DependencyObject? p = target;
            while (p != null && p is not ScrollViewer) p = System.Windows.Media.VisualTreeHelper.GetParent(p);
            if (p is ScrollViewer scroller && scroller.Content is UIElement content)
                scroller.ScrollToVerticalOffset(target.TransformToAncestor(content).Transform(new Point(0, 0)).Y - 4);
            else target.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- modules (SettingsPage.Modules.cs)

    private void RestartNow_Click(object sender, RoutedEventArgs e) => window.Restart();

    private void RunSetup_Click(object sender, RoutedEventArgs e) => window.RunSetup();

    private void Tools_Click(object sender, RoutedEventArgs e) => window.ChooseModules();

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
            StartupError.Text = T("Couldn't change the startup setting: ") + ex.Message;
            StartupError.Visibility = Visibility.Visible;
        }
    }

    private void SoftwareBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        bool on = SoftwareBox.IsChecked == true;
        settings.SoftwareRendering = on;
        settings.Save();
        Logging.Log.App.Info($"Draw without the graphics card {(on ? "on" : "off")} (from the next start)");
        // takes effect on the next start: this run draws as it started
        bool software = System.Windows.Media.RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly;
        SoftwareRestartRow.Visibility = on != software ? Visibility.Visible : Visibility.Collapsed;
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

    private static readonly int[] NotificationChoices = [3, 5, 8, 15, 30, 60, 0];

    private void NotificationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || NotificationBox.SelectedItem is not ComboBoxItem { Tag: int sec } || sec == settings.NotificationSeconds) return;
        settings.NotificationSeconds = sec;
        settings.Save();
        Logging.Log.App.Info($"Notification bar closes after {(sec == 0 ? "never" : sec + " s")}");
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        window.SetTheme((ThemeChoice)Math.Max(0, ThemeBox.SelectedIndex)); // items: Dark, Light, System
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        settings.Language = code;
        settings.Save();
        // takes effect on the next start, like the applets
        LanguageRestartRow.Visibility = code != Loc.Language ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenLanguageFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Loc.Root);

    // ---------------------------------------------------------------- updates

    private void AutoUpdateBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        settings.AutoCheckUpdates = AutoUpdateBox.IsChecked == true;
        settings.Save();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await window.CheckForUpdatesAsync(manual: true);

    private void Releases_Click(object sender, RoutedEventArgs e) => UpdateService.OpenInBrowser(UpdateService.RepoUrl + "/releases");

    // ---------------------------------------------------------------- settings: saved to a file, loaded back

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SettingsBackup.DefaultFileName,
            DefaultExt = ".json",
            Filter = T("Settings files") + " (*.json)|*.json",
            InitialDirectory = AppPaths.DocumentsFolder,
        };
        try { Directory.CreateDirectory(AppPaths.DocumentsFolder); } catch { }
        if (dialog.ShowDialog(window) != true) return;
        try
        {
            int count = SettingsBackup.Save(dialog.FileName);
            window.ShowNotice(F("Settings saved: {0} ({1}).", Path.GetFileName(dialog.FileName), P(count, "{0} file", "{0} files")), MainWindow.NoticeKind.Info);
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Saving settings", ex);
            window.ShowNotice(T("Couldn't save the settings: ") + ex.Message);
        }
    }

    private void LoadSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = T("Settings files") + " (*.json)|*.json",
            InitialDirectory = Directory.Exists(AppPaths.DocumentsFolder) ? AppPaths.DocumentsFolder : "",
        };
        if (dialog.ShowDialog(window) != true) return;
        try { SettingsBackup.Stage(dialog.FileName); }
        catch (Exception ex)
        {
            Logging.Log.App.Warn($"Settings file {dialog.FileName} couldn't be loaded", ex);
            window.ShowNotice(T("Couldn't load the settings: ") + ex.Message);
            return;
        }
        // (put in place at the next start, before anything reads them)
        window.Restart();
    }

    // ---------------------------------------------------------------- files

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.SettingsFolder);
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogFolder);

    private void LogLevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        bool debug = LogLevelBox.SelectedIndex == 1;
        if (debug == settings.DebugLogging) return;
        settings.DebugLogging = debug;
        settings.Save();
        Logging.Log.App.Info($"Logging set to {(debug ? "Debug" : "Normal")}");
        Logging.Log.DebugEnabled = debug;
    }

    private void ViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (AppNavigation.OpenTab("Logs")) return;
        try
        {
            string path = Logging.ErrorLog.LogPath;
            if (!File.Exists(path)) Logging.Log.App.Info("Log opened in Notepad");
            Process.Start("notepad.exe", $"\"{path}\"");
        }
        catch { OpenFolder(AppPaths.LogFolder); }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            window.ShowNotice(T("Couldn't open the folder: ") + ex.Message);
        }
    }
}
