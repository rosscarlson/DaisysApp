using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.Network.Zabbix.Api;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>
/// Where the Zabbix server is and how to sign in: its address, port, HTTPS and path, an API token or a user name and
/// password (the rest is Core's <see cref="DaisysApp.Shared.ConnectionForm"/>).
/// </summary>
internal sealed class ConnectionForm : DaisysApp.Shared.ConnectionForm
{
    private readonly ZabbixService service;
    private ZabbixSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 260 }, port = new() { Width = 80 }, path = new() { Width = 160 }, user = new() { MinWidth = 200 };
    private readonly CheckBox https = new() { Content = "HTTPS", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly CheckBox ignoreCert = new() { Content = T("Accept a certificate Windows doesn't trust (a self-signed one)") };
    private readonly RadioButton byToken = new() { Content = T("API token"), Margin = new Thickness(0, 0, 18, 0) };
    private readonly RadioButton byPassword = new() { Content = T("User name and password") };
    private readonly PasswordBox token = new() { MinWidth = 340 }, password = new() { MinWidth = 200 };
    private readonly FrameworkElement tokenLabel, userLabel, passwordLabel;
    private readonly ComboBox refresh = new() { Width = 120 };

    public ConnectionForm(ZabbixService service, bool showRefresh = true)
    {
        this.service = service;
        Row(T("Server"), address, T("The IP address or name of the server with Zabbix's web interface, e.g. 192.168.1.10 or zabbix.home"));
        var portLine = new StackPanel { Orientation = Orientation.Horizontal };
        portLine.Children.Add(port);
        portLine.Children.Add(https);
        port.ToolTip = T("Leave it empty for the usual one (80, or 443 with HTTPS)");
        Row(T("Port"), portLine);
        Row(T("Path"), path, T("Where the web interface is on the server: /zabbix for most installs, / for Docker and the Zabbix appliance"));
        Row("", ignoreCert);
        var how = new StackPanel { Orientation = Orientation.Horizontal };
        how.Children.Add(byToken);
        how.Children.Add(byPassword);
        Row(T("Sign in with"), how);
        tokenLabel = Row(T("API token"), token, T("Make one in Zabbix under User settings → API tokens (Zabbix 5.4 and later). A user that can only read is enough."));
        userLabel = Row(T("User name"), user);
        passwordLabel = Row(T("Password"), password);
        foreach (int s in new[] { 10, 30, 60, 120, 300 }) refresh.Items.Add(new ComboBoxItem { Content = s < 60 ? F("{0} seconds", s) : P(s / 60, "{0} minute", "{0} minutes"), Tag = s });
        if (showRefresh) Row(T("Read every"), refresh, T("How often to ask the server for new values while the Zabbix sub-tab, a widget or a graph is showing (nothing is asked otherwise)"));
        Finish();

        address.TextChanged += (_, _) => Changed(() => S.Address = address.Text.Trim());
        port.TextChanged += (_, _) => Changed(() => S.Port = int.TryParse(port.Text.Trim(), out int p) && p is > 0 and < 65536 ? p : 0);
        https.Click += (_, _) => Changed(() => S.Https = https.IsChecked == true);
        path.TextChanged += (_, _) => Changed(() => S.Path = path.Text.Trim());
        ignoreCert.Click += (_, _) => Changed(() => S.IgnoreCertificate = ignoreCert.IsChecked == true);
        byToken.Checked += (_, _) => { ShowAuth(); Changed(() => S.UseToken = true); };
        byPassword.Checked += (_, _) => { ShowAuth(); Changed(() => S.UseToken = false); };
        token.PasswordChanged += (_, _) => Changed(() => S.Token = token.Password.Trim());
        user.TextChanged += (_, _) => Changed(() => S.User = user.Text.Trim());
        password.PasswordChanged += (_, _) => Changed(() => S.Password = password.Password);
        refresh.SelectionChanged += (_, _) =>
        {
            if (Loading || refresh.SelectedItem is not ComboBoxItem { Tag: int s }) return;
            S.RefreshSeconds = s;
            S.Save();
            service.ApplyInterval();
        };
    }

    private void ShowAuth()
    {
        bool t = byToken.IsChecked == true;
        foreach (var e in new[] { token, tokenLabel }) e.Visibility = t ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in new[] { user, userLabel, password, passwordLabel }) e.Visibility = t ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void FillFields()
    {
        if (address.Text != S.Address) address.Text = S.Address;
        string portText = S.Port > 0 ? S.Port.ToString() : "";
        if (port.Text != portText) port.Text = portText;
        https.IsChecked = S.Https;
        if (path.Text != S.Path) path.Text = S.Path;
        ignoreCert.IsChecked = S.IgnoreCertificate;
        (S.UseToken ? byToken : byPassword).IsChecked = true;
        if (token.Password != S.Token) token.Password = S.Token;
        if (user.Text != S.User) user.Text = S.User;
        if (password.Password != S.Password) password.Password = S.Password;
        refresh.SelectedItem = refresh.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == S.RefreshSeconds) ?? refresh.Items[1];
        ShowAuth();
    }

    protected override void Watch(bool on)
    {
        if (on) service.Changed += ShowState;
        else service.Changed -= ShowState;
    }

    protected override void ShowState()
    {
        Status.Text = Tested ?? (!S.HasServer ? T("Fill in the server, then Test connection.")
            : service.Connected ? F("Connected to Zabbix {0}: {1}.", service.ServerVersion, P(service.Hosts.Count, "{0} host", "{0} hosts"))
            : service.Error is { } e ? e
            : "");
    }

    protected override void Unconfirm()
    {
        S.Configured = false;
        S.Save();
    }

    /// <summary>A path found elsewhere is kept.</summary>
    protected override async Task<string> Connect()
    {
        using var c = await ZabbixClient.ConnectAsync(S);
        var hosts = await c.HostsAsync();
        await c.LogoutAsync(!S.UseToken);
        if (c.FoundPath != null)
        {
            Loading = true;
            path.Text = S.Path = c.FoundPath;
            Loading = false;
        }
        S.Configured = true;
        S.SetupSkipped = false;
        S.Save();
        return F("Connected to Zabbix {0}: {1}.", c.Version, P(hosts.Count, "{0} host", "{0} hosts"))
            + (c.FoundPath != null ? " " + F("(Its API was at {0}, so the path is now that.)", c.FoundPath) : "");
    }

    protected override void Reconnect() => service.Reconnect();

    protected override bool OwnError(Exception ex) => ex is ZabbixException;
}
