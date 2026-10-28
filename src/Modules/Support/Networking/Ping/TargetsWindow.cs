using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Ping → gear: the targets, each a ping, a DNS lookup or a TCP connection, with its red level.</summary>
internal sealed class TargetsWindow : Window
{
    private sealed class Row
    {
        public TextBox Name = new() { ToolTip = T("What to call it (optional)") };
        public ComboBox Kind = new() { ToolTip = T("Ping, ask a DNS server for a name, or connect to a TCP port") };
        public TextBox Address = new() { ToolTip = T("IP address or hostname (DNS: the server)") };
        public TextBox Extra = new() { ToolTip = T("DNS: the name to look up · TCP: the port") };
        public TextBox Alert = new() { ToolTip = T("Slower than this (ms) shows red"), HorizontalContentAlignment = HorizontalAlignment.Right };
        public Grid Grid = new() { Margin = new Thickness(0, 0, 0, 6) };
        public string query = "www.google.com", port = "443";
    }

    private static readonly double[] Widths = [140, 80, 0, 150, 70, 40];
    private readonly NetworkingSettings settings;
    private readonly StackPanel rowsPanel = new();
    private readonly List<Row> rows = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };

    public static bool Edit(NetworkingSettings settings, Window? owner) =>
        new TargetsWindow(settings) { Owner = owner }.ShowDialog() == true;

    private TargetsWindow(NetworkingSettings settings)
    {
        this.settings = settings;
        Title = T("Ping targets");
        Width = 760;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");

        var head = MakeGrid();
        string[] titles = [T("Name"), T("Kind"), T("Address"), T("Name / port"), T("Red (ms)")];
        for (int i = 0; i < titles.Length; i++)
        {
            var t = Ui.Secondary(titles[i]);
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 6);
            Grid.SetColumn(t, i);
            head.Children.Add(t);
        }
        foreach (var t in settings.Targets) AddRow(t);

        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var add = Ui.IconButton("", T("Add"), null, () => AddRow(new NetTarget()));
        var router = Ui.Button(T("Add Router"), () => { if (NetInfo.Gateway() is { } gw) AddRow(new NetTarget { Name = T("Router"), Address = gw, AlertMs = 20 }); });
        router.IsEnabled = NetInfo.Gateway() != null;
        var dns = Ui.Button(T("Add DNS Servers"), () =>
        {
            foreach (var d in NetInfo.DnsServers()) AddRow(new NetTarget { Name = T("DNS server"), Address = d });
        });
        var defaults = Ui.Button(T("Defaults"), () =>
        {
            foreach (var r in rows.ToList()) rowsPanel.Children.Remove(r.Grid);
            rows.Clear();
            foreach (var t in NetTarget.Defaults()) AddRow(t);
        });
        foreach (var b in new[] { add, router, dns, defaults }) { b.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(b); }

        var save = Ui.Button(T("Save"), Save, accent: true);
        save.MinWidth = 90;
        save.IsDefault = true;
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { save, cancel } };

        Content = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 16),
            Children = { Ui.Card(new StackPanel { Children = { head, rowsPanel, buttons } }, new Thickness(0)), error, bottom },
        };
    }

    private static Grid MakeGrid()
    {
        var g = new Grid();
        foreach (var w in Widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
        return g;
    }

    private void AddRow(NetTarget t)
    {
        var r = new Row { Grid = MakeGrid() };
        r.Grid.Margin = new Thickness(0, 0, 0, 6);
        r.Name.Text = t.Name;
        foreach (var k in new[] { NetTarget.PingKind, NetTarget.DnsKind, NetTarget.TcpKind }) r.Kind.Items.Add(k);
        r.Kind.SelectedItem = t.Kind is NetTarget.DnsKind or NetTarget.TcpKind ? t.Kind : NetTarget.PingKind;
        r.Address.Text = t.Address;
        r.query = t.Query;
        r.port = t.Port.ToString(CultureInfo.CurrentCulture);
        r.Alert.Text = t.AlertMs.ToString(CultureInfo.CurrentCulture);
        void ShowExtra()
        {
            string kind = (string)r.Kind.SelectedItem;
            r.Extra.IsEnabled = kind != NetTarget.PingKind;
            r.Extra.Text = kind == NetTarget.DnsKind ? r.query : kind == NetTarget.TcpKind ? r.port : "";
        }
        ShowExtra();
        r.Extra.TextChanged += (_, _) =>
        {
            if ((string)r.Kind.SelectedItem == NetTarget.DnsKind) r.query = r.Extra.Text;
            else if ((string)r.Kind.SelectedItem == NetTarget.TcpKind) r.port = r.Extra.Text;
        };
        r.Kind.SelectionChanged += (_, _) => ShowExtra();
        var remove = Ui.IconButton("", "", T("Remove"), () => { rows.Remove(r); rowsPanel.Children.Remove(r.Grid); });
        var cells = new FrameworkElement[] { r.Name, r.Kind, r.Address, r.Extra, r.Alert, remove };
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
            Grid.SetColumn(cells[i], i);
            r.Grid.Children.Add(cells[i]);
        }
        rows.Add(r);
        rowsPanel.Children.Add(r.Grid);
    }

    private void Save()
    {
        var list = new List<NetTarget>();
        foreach (var r in rows)
        {
            string name = r.Name.Text.Trim(), address = r.Address.Text.Trim(), kind = (string)r.Kind.SelectedItem;
            if (name.Length == 0 && address.Length == 0) continue;
            string shown = name.Length > 0 ? name : address;
            if (address.Length == 0 || address.Contains(' ') || address.Contains("://")) { Fail(F("{0}: an IP address or hostname, without http://.", shown)); return; }
            if (!int.TryParse(r.Alert.Text.Trim(), out int alert) || alert < 1) { Fail(F("{0}: Red is a number of milliseconds.", shown)); return; }
            var t = new NetTarget { Name = name, Address = address, Kind = kind, AlertMs = alert, Query = r.query.Trim() };
            if (kind == NetTarget.DnsKind && t.Query.Length == 0) { Fail(F("{0}: the name to look up.", shown)); return; }
            if (kind == NetTarget.TcpKind)
            {
                if (!int.TryParse(r.port.Trim(), out int port) || port < 1 || port > 65535) { Fail(F("{0}: a port from 1 to 65535.", shown)); return; }
                t.Port = port;
            }
            list.Add(t);
        }
        settings.Targets = list;
        settings.Save();
        DialogResult = true;
    }

    private void Fail(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }
}
