using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Theming;
using DaisysApp.Updates;

namespace DaisysApp.Shell;

/// <summary>
/// The wrapper's "which tools?" window: every module of its version's release, ticked if it's installed. Tick the ones
/// you want and Install: the new ones are downloaded and the unticked ones removed, at a restart (ModuleStore). Shown
/// when the wrapper has no modules yet, and from Settings → General (Add or remove tools).
/// </summary>
internal sealed class ModulePicker : Window
{
    private readonly StackPanel list = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ProgressBar bar = new() { Height = 4, Margin = new Thickness(0, 8, 0, 0), Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Button install = new() { MinWidth = 150 }, cancel = new() { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
    private readonly List<(ModuleListing Module, CheckBox Box)> rows = new();
    private readonly Dictionary<string, string> installed = ModuleStore.Installed();
    private CancellationTokenSource? working;

    /// <summary>True when changes were downloaded: restart to use them.</summary>
    public bool Changed { get; private set; }

    public ModulePicker()
    {
        Title = F("{0}: choose your tools", AppPaths.DisplayName);
        Width = 640;
        Height = 620;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var head = new StackPanel();
        var title = new TextBlock { Text = T("Choose your tools"), FontSize = 20, FontWeight = FontWeights.SemiBold };
        head.Children.Add(title);
        var about = new TextBlock { Text = F("Tick the tools you want: only those are downloaded, each a module of its own. You can come back here any time: Settings → General → Add or remove tools. ({0} restarts to use them.)", AppPaths.DisplayName), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
        about.SetResourceReference(StyleProperty, "SecondaryText");
        head.Children.Add(about);

        install.SetResourceReference(StyleProperty, "AccentButton");
        install.Content = T("Install and restart");
        install.IsEnabled = false;
        install.Click += async (_, _) => await ApplyAsync();
        cancel.Content = T("Cancel");
        cancel.Click += (_, _) => { working?.Cancel(); Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(install);
        buttons.Children.Add(cancel);
        var foot = new StackPanel();
        foot.Children.Add(bar);
        foot.Children.Add(status);
        foot.Children.Add(buttons);

        var dock = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(head);
        dock.Children.Add(foot);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        status.Text = T("Getting the list of tools…");
        try
        {
            var modules = await ModuleStore.FetchAsync();
            foreach (var m in modules.OrderBy(m => Any(m.Title), StringComparer.CurrentCultureIgnoreCase)) list.Children.Add(Row(m));
            status.Text = modules.Count == 0 ? T("This release has no tools to download.") : "";
            install.IsEnabled = modules.Count > 0;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Module list", ex);
            status.Text = F("The list of tools couldn't be fetched: {0}", ex.Message);
            status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
    }

    private Border Row(ModuleListing m)
    {
        bool has = installed.ContainsKey(m.Name);
        var icon = new TextBlock { Text = m.Icon, FontSize = 18, Width = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var text = new StackPanel();
        var name = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        name.Inlines.Add(Any(m.Title));
        var size = new System.Windows.Documents.Run("   " + DaisysApp.Shared.Sizes.Bytes(m.Size) + (has ? "   " + T("installed") : "")) { FontWeight = FontWeights.Normal };
        size.SetResourceReference(TextElementForeground, "TextSecondaryBrush");
        name.Inlines.Add(size);
        text.Children.Add(name);
        string more = m.Description;
        if (m.Tools.Count > 1) more += (more.Length > 0 ? " " : "") + F("Tools: {0}.", string.Join(", ", m.Tools.Select(Any)));
        if (m.Tab != null) more += (more.Length > 0 ? " " : "") + F("Shares the {0} tab with the others of its kind.", Any(m.Tab));
        var desc = new TextBlock { Text = more, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        desc.SetResourceReference(StyleProperty, "SecondaryText");
        text.Children.Add(desc);
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(text);
        var box = new CheckBox { Content = line, IsChecked = has, VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(box, Any(m.Title));
        rows.Add((m, box));
        var card = new Border { Child = box, Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(StyleProperty, "Card");
        return card;
    }

    private static readonly DependencyProperty TextElementForeground = System.Windows.Documents.TextElement.ForegroundProperty;

    private async Task ApplyAsync()
    {
        var wanted = rows.Where(r => r.Box.IsChecked == true).Select(r => r.Module).ToList();
        // new ones, and ones from another version
        var get = wanted.Where(m => !installed.TryGetValue(m.Name, out var v) || v != ModuleStore.AppVersion).ToList();
        var remove = installed.Keys.Where(k => rows.Any(r => r.Module.Name.Equals(k, StringComparison.OrdinalIgnoreCase) && r.Box.IsChecked != true)).ToList();
        if (get.Count == 0 && remove.Count == 0) { Close(); return; }
        install.IsEnabled = false;
        foreach (var r in rows) r.Box.IsEnabled = false;
        bar.Visibility = Visibility.Visible;
        status.ClearValue(TextBlock.ForegroundProperty);
        working = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(string Text, double Done)>(p => { status.Text = p.Text; bar.Value = p.Done; });
            await ModuleStore.StageAsync(get, remove, progress, working.Token);
            Changed = true;
            DialogResult = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Installing modules", ex);
            status.Text = F("That didn't work: {0}", ex.Message);
            status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            install.IsEnabled = true;
            foreach (var r in rows) r.Box.IsEnabled = true;
            bar.Visibility = Visibility.Collapsed;
        }
    }
}
