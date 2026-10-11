using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Logs;

/// <summary>
/// The Logs tab: a row of tabs, one per open log (each with a close button), and Add, which opens another: Daisy's
/// App's logs, Windows' event logs, or a WSL distribution's journal. The open tabs are remembered.
/// </summary>
internal sealed class LogsView : UserControl, IDisposable
{
    private readonly LogsSettings settings;
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    private readonly Grid host = new();
    private readonly TextBlock emptyText;
    private readonly List<(LogPage Page, RadioButton Button)> tabs = new();
    private readonly Task<List<string>> wslDistros = Task.Run(WslSource.Distros);

    public LogsView(LogsSettings settings)
    {
        this.settings = settings;

        var add = new Button { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Open another log in a tab of its own") };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal };
        var plus = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        plus.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        addContent.Children.Add(plus);
        addContent.Children.Add(new TextBlock { Text = T("Add") });
        add.Content = addContent;
        AutomationProperties.SetName(add, T("Add"));
        add.Click += (_, _) => ShowAddMenu(add);

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = strip };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(scroller);
        row.Children.Add(add);
        top.Children.Add(row);

        emptyText = new TextBlock { Text = T("No logs are open. Press Add to open one."), Margin = new Thickness(4, 20, 0, 0) };
        emptyText.SetResourceReference(StyleProperty, "SecondaryText");
        host.Children.Add(emptyText);

        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(host);
        Content = layout;

        foreach (var key in settings.Open.Distinct().ToList())
            if (LogSources.Create(key) is { } source) AddTab(source, select: false);
        settings.Open = tabs.Select(t => t.Page.Source.Key).ToList();
        Select(settings.Selected ?? settings.Open.FirstOrDefault());
    }

    // ---------------------------------------------------------------- tabs

    private void AddTab(LogSource source, bool select)
    {
        if (tabs.FirstOrDefault(t => t.Page.Source.Key == source.Key) is { Page: not null } existing)
        {
            if (select) existing.Button.IsChecked = true;
            return;
        }
        var page = new LogPage(source, settings) { Visibility = Visibility.Collapsed };
        host.Children.Add(page);

        var title = new TextBlock { Text = source.Title, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "", FontSize = 10, Padding = new Thickness(4), Margin = new Thickness(8, 0, -6, 0), MinWidth = 0, MinHeight = 0,
            Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("Close this log"),
        };
        close.SetResourceReference(FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(close, F("Close {0}", source.Title));
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        header.Children.Add(close);
        var button = new RadioButton { Content = header };
        button.SetResourceReference(StyleProperty, "TabButton");
        AutomationProperties.SetName(button, source.Title);
        button.Checked += (_, _) => Show(page);
        close.Click += (_, e) => { e.Handled = true; Close(page); };
        strip.Children.Add(button);
        tabs.Add((page, button));
        emptyText.Visibility = Visibility.Collapsed;
        if (!settings.Open.Contains(source.Key)) settings.Open.Add(source.Key);
        if (select) Log.Here.Info($"Log tab opened: {source.Key}");
        else Log.Here.Debug($"Log tab restored: {source.Key}");
        if (select) button.IsChecked = true;
    }

    private void Select(string? key)
    {
        var tab = tabs.FirstOrDefault(t => t.Page.Source.Key == key);
        if (tab.Button == null && tabs.Count > 0) tab = tabs[0];
        if (tab.Button != null) tab.Button.IsChecked = true;
    }

    private void Show(LogPage page)
    {
        settings.Selected = page.Source.Key;
        foreach (var t in tabs) t.Page.Visibility = t.Page == page ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Close(LogPage page)
    {
        int i = tabs.FindIndex(t => t.Page == page);
        if (i < 0) return;
        bool wasShowing = tabs[i].Button.IsChecked == true;
        strip.Children.Remove(tabs[i].Button);
        host.Children.Remove(page);
        page.Dispose();
        tabs.RemoveAt(i);
        settings.Open.Remove(page.Source.Key);
        Log.Here.Info($"Log tab closed: {page.Source.Key}");
        if (tabs.Count == 0)
        {
            emptyText.Visibility = Visibility.Visible;
            settings.Selected = null;
        }
        else if (wasShowing) tabs[Math.Min(i, tabs.Count - 1)].Button.IsChecked = true;
        settings.Save();
    }

    // ---------------------------------------------------------------- add

    private void ShowAddMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        MenuItem Item(string text, Func<LogSource?> make, string? tip = null)
        {
            var item = new MenuItem { Header = text, ToolTip = tip };
            item.Click += (_, _) => { if (make() is { } s) { AddTab(s, select: true); settings.Save(); } };
            return item;
        }

        menu.Items.Add(Item(T("Daisy's App (every applet)"), () => new AppLogSource(null), T("The app's log and every applet's, merged")));
        var one = new MenuItem { Header = T("Daisy's App: one applet") };
        var names = LogSources.AppLogNames();
        foreach (var name in names) one.Items.Add(Item(name, () => new AppLogSource(name)));
        if (names.Count == 0) one.Items.Add(new MenuItem { Header = T("(no logs yet)"), IsEnabled = false });
        menu.Items.Add(one);
        menu.Items.Add(Item(T("Daisy's App crashes (Windows)"), WindowsLogSource.AppCrashes, T("What Windows recorded when DaisysApp.exe crashed or stopped responding")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T("Windows: Application"), () => new WindowsLogSource("Application"), T("Programs' events, including crashes of any program")));
        menu.Items.Add(Item(T("Windows: System"), () => new WindowsLogSource("System"), T("Windows' own events: drivers, devices, services, power, shutdowns and restarts")));
        menu.Items.Add(Item(T("Windows: Setup"), () => new WindowsLogSource("Setup"), T("Windows updates and feature installs")));
        menu.Items.Add(Item(T("Windows: other log…"), PickWindowsLog, T("Any other event log on this PC, e.g. a driver's or an app's own")));
        menu.Items.Add(new Separator());
        if (!wslDistros.IsCompleted) menu.Items.Add(new MenuItem { Header = T("WSL: looking for distributions…"), IsEnabled = false });
        else if (wslDistros.Result.Count == 0) menu.Items.Add(new MenuItem { Header = T("WSL: not installed"), IsEnabled = false });
        else foreach (var d in wslDistros.Result) menu.Items.Add(Item(F("WSL: {0}", d), () => new WslSource(d), T("Its system journal since it last started; opening it starts WSL if it isn't running")));
        menu.IsOpen = true;
    }

    private LogSource? PickWindowsLog()
    {
        var picker = new WindowsLogPicker { Owner = Window.GetWindow(this) };
        return picker.ShowDialog() == true && picker.Chosen is { } name ? new WindowsLogSource(name) : null;
    }

    public void Dispose()
    {
        foreach (var t in tabs) t.Page.Dispose();
    }
}
