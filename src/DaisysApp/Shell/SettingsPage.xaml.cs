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
    private readonly SavedIndicator saved;
    private bool updating = true;

    public SettingsPage(AppSettings settings, IReadOnlyList<IApplet> applets, MainWindow window)
    {
        this.settings = settings;
        this.window = window;
        InitializeComponent();

        // General first, then a page for each applet that has settings, in the same order as the main tabs
        var subTabs = new TabStrip(SubTabButtons, SubTabPages);
        ((Panel)GeneralPanel.Parent).Children.Remove(GeneralPanel);
        subTabs.Add("general", T("General"), "", GeneralPanel, renamable: false);
        foreach (var applet in applets)
            if (applet.SettingsView is { } view) subTabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, view);
        subTabs.Select(null);
        saved = new SavedIndicator(SubTabPages); // every page saves as you change it: say so

        // Applets: every one in the modules folder, with the ones loaded at startup ticked
        appletToggles = AppletCatalog.All
            .Select(e => new AppletToggle(e.Meta, TabNames.For(e.Meta.Id, Any(e.Meta.Title)), Any(e.Meta.Description), AppletCatalog.IsOn(e.Meta, settings)))
            .ToList();
        loadedOn = appletToggles.Where(t => t.Enabled).Select(t => t.Meta.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppletList.ItemsSource = appletToggles;
        BuildTabNames(applets);

        VersionText.Text = F("{0} version {1}", AppPaths.DisplayName, UpdateService.Display(UpdateService.CurrentVersion));
        SettingsFolderText.Text = AppPaths.SettingsFolder;
        LogFolderText.Text = AppPaths.LogFolder;

        TrayBox.IsChecked = settings.RunInTray;
        StartupBox.IsChecked = SafeIsStartupEnabled();
        StartHiddenBox.IsChecked = settings.StartHidden;
        AutoUpdateBox.IsChecked = settings.AutoCheckUpdates;
        ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        foreach (var (code, name) in Loc.Available()) LanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Language)
            ?? LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Loc.English);
        UpdateDependentOptions();
        updating = false;

        ThemeManager.ThemeChanged += () => ThemeBox.SelectedIndex = (int)ThemeManager.Choice;
        window.UpdateBusyChanged += busy => CheckNowButton.IsEnabled = !busy;
    }

    // ---------------------------------------------------------------- applets

    /// <summary>One row of the Applets card (bound to its checkbox).</summary>
    public sealed class AppletToggle(AppletAttribute meta, string title, string description, bool enabled)
    {
        public AppletAttribute Meta { get; } = meta;
        public string Title { get; } = title;
        public string Description { get; } = description;
        public bool Enabled { get; set; } = enabled;
    }

    private readonly List<AppletToggle> appletToggles;
    private readonly HashSet<string> loadedOn; // what this run started with

    private void Applet_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        // applets that are on by default are remembered when they're off, and the others when they're on; ids of
        // modules that aren't installed now are kept, for when they're back
        var shown = appletToggles.Select(t => t.Meta.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.DisabledApplets = settings.DisabledApplets.Where(id => !shown.Contains(id))
            .Concat(appletToggles.Where(t => t.Meta.OnByDefault && !t.Enabled).Select(t => t.Meta.Id)).ToList();
        settings.EnabledApplets = settings.EnabledApplets.Where(id => !shown.Contains(id))
            .Concat(appletToggles.Where(t => !t.Meta.OnByDefault && t.Enabled).Select(t => t.Meta.Id)).ToList();
        settings.Save();
        // takes effect on the next start; offer a restart while it differs from what's loaded
        bool changed = !appletToggles.Where(t => t.Enabled).Select(t => t.Meta.Id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(loadedOn);
        RestartRow.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e) => window.Restart();

    // ---------------------------------------------------------------- tab names

    /// <summary>A row per tab (the loaded applets, then Settings): its own name, a box for the user's, and Reset.</summary>
    private void BuildTabNames(IReadOnlyList<IApplet> applets)
    {
        var rows = applets.Select(a => (a.Meta.Id, Default: Any(a.Meta.Title), a.Meta.Icon))
            .Append((Id: MainWindow.SettingsTabId, Default: T("Settings"), Icon: "\uE713")).ToList();
        foreach (var (id, def, icon) in rows)
        {
            int r = TabNameGrid.RowDefinitions.Count;
            TabNameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var glyph = new TextBlock { Text = icon, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
            var name = new TextBlock { Text = def, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 8), MinWidth = 120 };
            name.SetResourceReference(StyleProperty, "SecondaryText");
            var box = new TextBox { Text = TabNames.For(id, def), Margin = new Thickness(0, 0, 8, 8), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, F("Name of the {0} tab", def));
            var reset = new Button { Content = T("Reset"), Margin = new Thickness(0, 0, 0, 8), IsEnabled = TabNames.IsRenamed(id) };
            void Save() => TabNames.Set(id, box.Text, def);
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Save(); };
            box.LostKeyboardFocus += (_, _) => Save();
            reset.Click += (_, _) => TabNames.Set(id, null, def);
            TabNames.Changed += changed =>
            {
                if (changed != id) return;
                if (!box.IsKeyboardFocusWithin) box.Text = TabNames.For(id, def);
                reset.IsEnabled = TabNames.IsRenamed(id);
            };
            foreach (var (element, column) in new (UIElement, int)[] { (glyph, 0), (name, 1), (box, 2), (reset, 3) })
            {
                Grid.SetRow(element, r);
                Grid.SetColumn(element, column);
                TabNameGrid.Children.Add(element);
            }
        }
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
            StartupError.Text = T("Couldn't change the startup setting: ") + ex.Message;
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

    // ---------------------------------------------------------------- files

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.SettingsFolder);
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogFolder);

    private void ViewErrorLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(Logging.ErrorLog.LogPath))
            {
                System.IO.Directory.CreateDirectory(AppPaths.LogFolder);
                System.IO.File.WriteAllText(Logging.ErrorLog.LogPath, "");
            }
            System.Diagnostics.Process.Start("notepad.exe", $"\"{Logging.ErrorLog.LogPath}\"");
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
