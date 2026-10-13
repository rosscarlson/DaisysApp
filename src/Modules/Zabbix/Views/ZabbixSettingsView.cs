using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Zabbix.Views;

/// <summary>Settings → Zabbix: the server and the sign-in, and which hosts the grid shows.</summary>
internal sealed class ZabbixSettingsView : StackPanel
{
    private readonly ZabbixService service;
    private readonly StackPanel hidden = new();

    public ZabbixSettingsView(ZabbixService service)
    {
        this.service = service;
        var server = new StackPanel();
        server.Children.Add(Ui.Header(T("Server")));
        var note = Ui.Secondary(T("Where your Zabbix server is and how to sign in. Daisy's App only reads (hosts, items, history and problems), so a user that can only read is enough. The token and password are kept encrypted for your Windows account."), wrap: true);
        note.Margin = new Thickness(0, 0, 0, 12);
        server.Children.Add(note);
        server.Children.Add(new ConnectionForm(service));
        Children.Add(Ui.Card(server, new Thickness(0, 0, 0, 12)));

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
        Children.Add(Ui.Card(hosts, new Thickness(0)));
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) ShowHidden(); };
    }

    /// <summary>The hosts hidden from the grid (right-click → Hide this host), each with Show.</summary>
    private void ShowHidden()
    {
        hidden.Children.Clear();
        var ids = service.Settings.HiddenHosts;
        if (ids.Count == 0) { hidden.Children.Add(Ui.Secondary(T("No hosts are hidden. Right-click a host on the Zabbix tab to hide it."), wrap: true)); return; }
        hidden.Children.Add(Ui.Secondary(T("Hidden from the Zabbix tab:")));
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
