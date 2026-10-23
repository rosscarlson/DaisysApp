using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>Settings → Network → Zabbix: switched on or off and which hosts the grid shows (side by side), then the server and the sign-in, with the grid's size beside them.</summary>
internal sealed class ZabbixSettingsView : StackPanel
{
    private readonly ZabbixService service;
    private readonly StackPanel hidden = new();

    public ZabbixSettingsView(ZabbixService service)
    {
        this.service = service;
        var use = new CheckBox { Content = T("Use Zabbix"), IsChecked = !service.Settings.Off, FontWeight = FontWeights.SemiBold };
        use.Click += (_, _) => service.SetOff(use.IsChecked != true);
        service.OffChanged += () => use.IsChecked = !service.Settings.Off;
        var useNote = Ui.Secondary(T("Off: the Zabbix sub-tab and its widgets go, and nothing is read from the server."), wrap: true);
        useNote.Margin = new Thickness(0, 4, 0, 0);
        var useCard = Ui.Card(new StackPanel { Children = { use, useNote } }, new Thickness(0, 0, 0, 12));

        var server = new StackPanel();
        server.Children.Add(Ui.Header(T("Server")));
        var note = Ui.Secondary(T("Where your Zabbix server is and how to sign in. Daisy's App only reads (hosts, items, history and problems), so a user that can only read is enough. The token and password are kept encrypted for your Windows account."), wrap: true);
        note.Margin = new Thickness(0, 0, 0, 12);
        server.Children.Add(note);
        server.Children.Add(new ConnectionForm(service));
        var serverCard = Ui.Card(server, new Thickness(0));

        var hosts = new StackPanel();
        hosts.Children.Add(Ui.Header(T("Hosts")));
        var disabled = new CheckBox { Content = T("Show hosts whose monitoring is switched off in Zabbix"), IsChecked = service.Settings.ShowDisabled, Margin = new Thickness(0, 0, 0, 10) };
        disabled.Click += (_, _) =>
        {
            service.Settings.ShowDisabled = disabled.IsChecked == true;
            service.Settings.Save();
            service.RaiseChanged();
        };
        hosts.Children.Add(disabled);
        hosts.Children.Add(hidden);
        var hostsCard = Ui.Card(hosts, new Thickness(0, 0, 0, 12));

        // Use Zabbix and Hosts side by side, the server under them
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition() } };
        Grid.SetColumn(hostsCard, 2);
        top.Children.Add(useCard);
        top.Children.Add(hostsCard);
        Children.Add(top);

        // the server, and the grid's size beside it
        var size = new StackPanel();
        size.Children.Add(Ui.Header(T("Display Grid Size")));
        size.Children.Add(Number(T("Columns"), T("0 = as many as fit"), () => service.Settings.GridColumns, v => service.Settings.GridColumns = v));
        size.Children.Add(Number(T("Rows"), T("Rows to fit the window's height; 0 = as tall as they need"), () => service.Settings.GridRows, v => service.Settings.GridRows = v));
        var sizeCard = Ui.Card(size, new Thickness(0));
        var lower = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(sizeCard, 2);
        sizeCard.VerticalAlignment = VerticalAlignment.Top;
        lower.Children.Add(serverCard);
        lower.Children.Add(sizeCard);
        Children.Add(lower);
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) ShowHidden(); };
    }

    /// <summary>A labelled whole number (0–20), saved as it's typed.</summary>
    private FrameworkElement Number(string label, string tip, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString(), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            if (!int.TryParse(box.Text.Trim().Length == 0 ? "0" : box.Text.Trim(), out int v) || v < 0 || v > 20 || v == get()) return;
            set(v);
            service.Settings.Save();
            service.RaiseChanged();
        };
        var l = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { l, box } };
    }

    /// <summary>The hosts hidden from the grid (right-click → Hide this host), each with Show.</summary>
    private void ShowHidden()
    {
        hidden.Children.Clear();
        var ids = service.Settings.HiddenHosts;
        if (ids.Count == 0) { hidden.Children.Add(Ui.Secondary(T("No hosts are hidden. Right-click a host on the Zabbix sub-tab to hide it."), wrap: true)); return; }
        hidden.Children.Add(Ui.Secondary(T("Hidden from the Zabbix sub-tab:")));
        foreach (var id in ids.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var show = new Button { Content = T("Show") };
            show.Click += (_, _) =>
            {
                service.Settings.HiddenHosts.Remove(id);
                service.Settings.Save();
                service.RaiseChanged();
                ShowHidden();
            };
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            row.Children.Add(new TextBlock { Text = service.Host(id)?.Display ?? F("Host {0}", id), VerticalAlignment = VerticalAlignment.Center });
            hidden.Children.Add(row);
        }
    }
}
