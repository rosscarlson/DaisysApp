using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Terminal;

/// <summary>
/// Terminal, a sub-tab of the Tools tab: CMD, PowerShell and WSL sessions in tabs, each shell on a Windows pseudo
/// console in Windows Terminal's own control, looking as Windows' console does. Settings → Tools → Terminal: the kind
/// + opens.
/// </summary>
public sealed class TerminalApplet : IApplet
{
    private readonly TerminalSettings settings = TerminalSettings.Load();
    private readonly TerminalView view;
    private readonly FrameworkElement settingsView;

    public TerminalApplet()
    {
        view = new TerminalView(settings);
        var kind = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(kind, T("New sessions"));
        foreach (var k in Shells.All) kind.Items.Add(new ComboBoxItem { Content = Shells.Name(k), Tag = k });
        kind.SelectedItem = kind.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (ShellKind)i.Tag == settings.Default);
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not ComboBoxItem { Tag: ShellKind k }) return;
            settings.Default = k;
            settings.Save();
            view.ShowAddTip();
        };
        var card = new StackPanel();
        card.Children.Add(Ui.Header(T("Terminal")));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        line.Children.Add(new TextBlock { Text = T("New sessions"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        line.Children.Add(kind);
        card.Children.Add(line);
        var box = Ui.Card(card);
        box.VerticalAlignment = VerticalAlignment.Top;
        settingsView = box;
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() { }

    public void SaveSettings() => settings.Save();

    public void Dispose() => view.Shutdown();
}
