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
        if (AppPaths.Product?.Settings is { } columns)
        {
            // (since SimEffects Analysis 0.3.0) a product's Settings on one page, no sub-tabs
            SubTabRow.Visibility = Visibility.Collapsed;
            OnePage(columns);
        }
        else
        {
            // every module's shortcuts in one list (Core's Hotkeys)
            subTabs.Add("shortcuts", T("Shortcuts"), "", Scrolling(new ShortcutsPage()), renamable: false);
            foreach (var applet in applets)
                if (applet.SettingsView is { } view) subTabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, Scrolling(view));
            // then the applets shown here rather than as tabs (none since Logs went to the Network tab in 0.39), last, filling the page
            foreach (var applet in applets.Where(a => a.Meta.InSettings))
                subTabs.Add(applet.Meta.Id, Any(applet.Meta.Title), applet.Meta.Icon, applet.View);
        }
        subTabs.Select(null);
        saved = new SavedIndicator(SubTabPages); // every page saves as you change it: say so
        foreach (var applet in applets.Where(a => a.Meta.InSettings)) saved.Skip.Add(applet.View); // (not a settings page)

        // Modules: every one in the modules folder, with their sub-modules, the ones loaded at startup ticked
        BuildModules();
        // one module: nothing to switch on or off, nor a setup to run unless the tool has a step
        if (AppletCatalog.All.Count <= 1 && !applets.Any(HasSetupPage) && !ModuleList.Children.OfType<FrameworkElement>().Any(r => r.Tag is "subs")) ModulesCard.Visibility = Visibility.Collapsed;
        // the wrapper: its tools are downloaded, and more can be
        if (AppPaths.IsWrapper)
        {
            ModulesCard.Visibility = Visibility.Visible;
            ToolsButton.Visibility = Visibility.Visible;
        }

        VersionText.Text = F("{0} version {1}", AppPaths.DisplayName, UpdateService.Display(UpdateService.CurrentVersion));
        if (AppPaths.Product != null)
        {
            // (since SimEffects Analysis 0.2.1) a product's General: no Save / Load, Modules, Daisy's Server, Audio or Storage
            BackupCard.Visibility = ModulesCard.Visibility = StorageCard.Visibility = Visibility.Collapsed;
            if (AppPaths.Product.Settings == null) UpdatesCard.Margin = new Thickness(6, 0, 0, 12);
        }
        else
        {
            StorageCard.Child = new StoragePanel(settings);
            // Daisy's Server, and the app's audio devices beside it: Core's cards, for every module
            var audio = new Shared.Audio.AudioSettingsView { Margin = new Thickness(12, 0, 0, 12), VerticalAlignment = VerticalAlignment.Top };
            var serverRow = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() } };
            serverRow.Children.Add(new Server.ServerSettingsView { VerticalAlignment = VerticalAlignment.Top });
            Grid.SetColumn(audio, 1);
            serverRow.Children.Add(audio);
            GeneralPanel.Children.Insert(GeneralPanel.Children.IndexOf(StorageCard), serverRow);
        }
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

    /// <summary>The sub-tabs' settings pages on General (<see cref="OnePage"/>).</summary>
    private readonly List<FrameworkElement> onePage = new();

    /// <summary>
    /// A product's Settings on one page (<see cref="AppPaths.ProductInfo.Settings"/>): its rows, each of columns side by
    /// side (one card as tall as the row), each column's cards stacked: General's own, sub-tabs' settings (taken out of
    /// their module's), cards modules offer (<see cref="SettingsCards"/>) or the YouTube account. General's others
    /// (Save / Load, Modules, Storage, Daisy's Server, Audio) aren't on it.
    /// </summary>
    private void OnePage(List<List<List<string>>> rows)
    {
        var own = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Startup"] = StartupCard, ["Updates"] = UpdatesCard, ["Appearance"] = AppearanceCard,
            ["Notifications"] = NotificationsCard, ["Language"] = LanguageCard, ["Files"] = FilesCard,
        };
        foreach (var card in own.Values)
        {
            (card.Parent as Panel)?.Children.Remove(card);
            card.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetColumn(card, 0);
        }
        GeneralPanel.Children.Clear();
        foreach (var columns in rows)
        {
            var row = new Grid();
            for (int c = 0; c < columns.Count; c++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement? One(string name) => own.TryGetValue(name, out var mine) ? mine : Part(name);
                var parts = columns[c].Select(name => name.Split('=', 2) is [var title, var joined]
                    ? Joined(title.Trim(), joined.Split('+').Select(n => One(n.Trim())).OfType<FrameworkElement>().ToList())
                    : One(name)).OfType<FrameworkElement>().ToList();
                var cell = new Border { Padding = new Thickness(c == 0 ? 0 : 6, 0, c == columns.Count - 1 ? 0 : 6, 0) };
                if (parts.Count == 1) cell.Child = parts[0];
                else
                {
                    var stack = new StackPanel();
                    foreach (var part in parts) stack.Children.Add(part);
                    cell.Child = stack;
                }
                Grid.SetColumn(cell, c);
                row.Children.Add(cell);
            }
            GeneralPanel.Children.Add(row);
        }
    }

    /// <summary>(since SimEffects Analysis 0.3.3) "Title=A+B": the cards as one titled <paramref name="title"/>, each's
    /// own title over its part (a line between them).</summary>
    private static FrameworkElement? Joined(string title, List<FrameworkElement> cards)
    {
        if (cards.Count == 0) return null;
        var body = new StackPanel();
        body.Children.Add(Shared.Ui.Header(title));
        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0)
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 12) };
                line.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
                body.Children.Add(line);
            }
            var inner = cards[i] is Border { Child: Panel p } ? p : null;
            if (inner == null) { (cards[i].Parent as Panel)?.Children.Remove(cards[i]); body.Children.Add(cards[i]); continue; }
            var children = inner.Children.Cast<UIElement>().ToList();
            inner.Children.Clear();
            foreach (var child in children)
            {
                // its title: smaller, as a part's
                if (child == children[0] && child is TextBlock head)
                {
                    head.Style = null;
                    head.FontWeight = FontWeights.SemiBold;
                    head.Margin = new Thickness(0, 0, 0, 6);
                }
                body.Children.Add(child);
            }
        }
        var card = new Border { Child = body, Margin = new Thickness(0, 0, 0, 12) };
        card.SetResourceReference(StyleProperty, "Card");
        return card;
    }

    private FrameworkElement? Part(string name)
    {
        if (name.Equals("Shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            // (since SimEffects Analysis 0.3.3) a card like the others
            var shortcuts = new StackPanel();
            var header = new TextBlock { Text = T("Shortcuts") };
            header.SetResourceReference(StyleProperty, "CardHeader");
            shortcuts.Children.Add(header);
            shortcuts.Children.Add(new ShortcutsPage(inCard: true));
            var box = new Border { Child = shortcuts, Margin = new Thickness(0, 0, 0, 12) };
            box.SetResourceReference(StyleProperty, "Card");
            return box;
        }
        if (SettingsCards.Make(name) is { } offered) return offered;
        if (name.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            var body = new StackPanel();
            var header = new TextBlock { Text = "YouTube" };
            header.SetResourceReference(StyleProperty, "CardHeader");
            body.Children.Add(header);
            body.Children.Add(new Shared.Network.YouTubeAccountView());
            var card = new Border { Child = body };
            card.SetResourceReference(StyleProperty, "Card");
            return card;
        }
        if (name.Split('/') is not [var groupId, var toolId]
            || applets.OfType<AppletGroup>().FirstOrDefault(g => ((IApplet)g).Meta.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)) is not { } group
            || group.TakeSettings(toolId) is not { } page)
        {
            Logging.Log.App.Warn($"Settings: {name} isn't a sub-tab with settings");
            return null;
        }
        // one that scrolls by itself (Analysis'): its content, the page scrolling
        if (page is UserControl { Content: ScrollViewer { Content: FrameworkElement inner } scroll } own)
        {
            scroll.Content = null;
            own.Content = inner;
            inner.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (inner is Border { Child: FrameworkElement body }) body.HorizontalAlignment = HorizontalAlignment.Left;
        }
        onePage.Add(page);
        return page;
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
        if (AppPaths.Product?.Settings != null)
        {
            subTabs.Select("general");
            if (elementName != null && onePage.Select(p => p.FindName(elementName) as FrameworkElement is { } e && (e == p || p.IsAncestorOf(e)) ? e : null)
                    .FirstOrDefault(e => e != null) is { } found)
                ScrollTo(found);
            return;
        }
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
        if (target != null) ScrollTo(target);
    }

    private void ScrollTo(FrameworkElement target)
    {
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

    private void Releases_Click(object sender, RoutedEventArgs e) => UpdateService.OpenInBrowser(UpdateService.ReleasesUrl);

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
            DaisysApp.Shared.Ui.Open(path);
        }
        catch (Exception ex)
        {
            window.ShowNotice(T("Couldn't open the folder: ") + ex.Message);
        }
    }
}
