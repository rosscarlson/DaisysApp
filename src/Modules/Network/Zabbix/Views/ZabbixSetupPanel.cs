using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>
/// Zabbix's step in the app's setup wizard (and its own first-run window), while no server has been set up: the
/// connection form, with Skip for now and Switch off Zabbix (its sub-tab hidden) for anyone who doesn't have a Zabbix server.
/// </summary>
internal sealed class ZabbixSetupPanel : StackPanel
{
    private readonly ZabbixService service;
    private readonly TextBlock note = Ui.Secondary("", wrap: true);

    /// <summary>Raised when the user chose Skip, Switch off, or connected.</summary>
    public event Action? Finished;

    public ZabbixSetupPanel(ZabbixService service)
    {
        this.service = service;
        Children.Add(new TextBlock { Text = T("Zabbix: your server's hosts at a glance"), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        Children.Add(Text(T("Zabbix, on the Network tab, shows each host on your Zabbix server, with its values, graphs and problems, and widgets for any of them. It only reads, and changes nothing on the server. It needs to know where the server is and how to sign in: an API token is best (make one in Zabbix under User settings → API tokens), or a user name and password.")));
        var form = new ConnectionForm(service, showRefresh: false) { Margin = new Thickness(0, 4, 0, 12) };
        form.Connected += () => { note.Text = T("Done: Zabbix is ready, on the Network tab."); Finished?.Invoke(); };
        Children.Add(form);

        Children.Add(Text(T("No Zabbix server? Skip this for now (it's in Settings → Network → Zabbix whenever you want it), or switch Zabbix off so its sub-tab goes and it reads nothing.")));
        var skip = new Button { Content = T("Skip for now"), MinWidth = 120 };
        skip.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            note.Text = T("Skipped. Set it up any time in Settings → Network → Zabbix.");
            Finished?.Invoke();
        };
        var off = new Button { Content = T("Switch off Zabbix"), MinWidth = 140, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Hide the Zabbix sub-tab and read nothing from any server (Settings → Network → Zabbix switches it back on)") };
        off.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            service.SetOff(true);
            note.Text = T("Zabbix is switched off. Settings → Network → Zabbix switches it back on.");
            Finished?.Invoke();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { skip, off } };
        Children.Add(buttons);
        note.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(note);
    }

    private static TextBlock Text(string s) => new() { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13.5 };

    /// <summary>The panel in its own window, for a first run outside the setup wizard (Zabbix was switched on later).</summary>
    public static void ShowWindow(ZabbixService service, Window owner)
    {
        var panel = new ZabbixSetupPanel(service);
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsCancel = true };
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var w = new Window
        {
            Title = T("Set up Zabbix"), Width = 640, Height = 640, Owner = owner, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);
        close.Click += (_, _) => w.Close();
        panel.Finished += () => w.Dispatcher.BeginInvoke(() => { if (w.IsVisible) w.Close(); }, System.Windows.Threading.DispatcherPriority.Background);
        w.Closed += (_, _) =>
        {
            // closed without choosing: don't ask again by itself (Settings → Network → Zabbix has it)
            if (!service.Settings.Configured && !service.Settings.SetupSkipped) { service.Settings.SetupSkipped = true; service.Settings.Save(); }
        };
        w.ShowDialog();
    }
}
