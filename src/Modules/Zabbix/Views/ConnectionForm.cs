using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Applets.Zabbix.Api;

namespace DaisysApp.Applets.Zabbix.Views;

/// <summary>
/// Where the Zabbix server is and how to sign in: its address, port, HTTPS and path, an API token or a user name and
/// password, and Test connection. Used by the Settings page, the setup wizard's step and the tab before it's set up.
/// Every change is saved straight away; the connection is made again a moment after the last one.
/// </summary>
internal sealed class ConnectionForm : StackPanel
{
    private readonly ZabbixService service;
    private ZabbixSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 260 }, port = new() { Width = 80 }, path = new() { Width = 160 }, user = new() { MinWidth = 200 };
    private readonly CheckBox https = new() { Content = "HTTPS", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly CheckBox ignoreCert = new() { Content = T("Accept a certificate Windows doesn't trust (a self-signed one)") };
    private readonly RadioButton byToken = new() { Content = T("API token"), Margin = new Thickness(0, 0, 18, 0) };
    private readonly RadioButton byPassword = new() { Content = T("User name and password") };
    private readonly PasswordBox token = new() { MinWidth = 340 }, password = new() { MinWidth = 200 };
    private readonly FrameworkElement tokenRow, userRow, passwordRow;
    private readonly ComboBox refresh = new() { Width = 120 };
    private readonly Button test = new() { Content = T("Test connection"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly DispatcherTimer reconnect = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool loading = true;

    /// <summary>Raised after a successful test.</summary>
    public event Action? Connected;

    public ConnectionForm(ZabbixService service, bool showRefresh = true)
    {
        this.service = service;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(string label, UIElement field, string? tip = null)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 8) };
            if (field is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 0, 8); if (tip != null) fe.ToolTip = tip; fe.HorizontalAlignment = HorizontalAlignment.Left; }
            Grid.SetRow(l, r);
            Grid.SetRow(field, r);
            Grid.SetColumn(field, 1);
            grid.Children.Add(l);
            grid.Children.Add(field);
            if (label.Length == 0) l.Visibility = Visibility.Collapsed;
        }
        FrameworkElement LastRow() => (FrameworkElement)grid.Children[^1];

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
        Row(T("API token"), token, T("Make one in Zabbix under User settings → API tokens (Zabbix 5.4 and later). A user that can only read is enough."));
        tokenRow = LastRow();
        var tokenLabel = (FrameworkElement)grid.Children[^2];
        Row(T("User name"), user);
        userRow = LastRow();
        var userLabel = (FrameworkElement)grid.Children[^2];
        Row(T("Password"), password);
        passwordRow = LastRow();
        var passwordLabel = (FrameworkElement)grid.Children[^2];
        foreach (int s in new[] { 10, 30, 60, 120, 300 }) refresh.Items.Add(new ComboBoxItem { Content = s < 60 ? F("{0} seconds", s) : P(s / 60, "{0} minute", "{0} minutes"), Tag = s });
        if (showRefresh) Row(T("Read every"), refresh, T("How often to ask the server for new values while the Zabbix tab, a widget or a graph is showing (nothing is asked otherwise)"));
        Children.Add(grid);

        var testLine = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        testLine.Children.Add(test);
        status.Margin = new Thickness(0, 8, 0, 0);
        testLine.Children.Add(status);
        Children.Add(testLine);

        void ShowAuth()
        {
            bool t = byToken.IsChecked == true;
            foreach (var e in new[] { tokenRow, tokenLabel }) e.Visibility = t ? Visibility.Visible : Visibility.Collapsed;
            foreach (var e in new[] { userRow, userLabel, passwordRow, passwordLabel }) e.Visibility = t ? Visibility.Collapsed : Visibility.Visible;
        }

        // the settings as they are, each time it shows (another copy of this form, e.g. the setup wizard's, may have changed them)
        void Fill()
        {
            if (IsKeyboardFocusWithin) return;
            loading = true;
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
            ShowState();
            loading = false;
        }
        Fill();

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
            if (loading || refresh.SelectedItem is not ComboBoxItem { Tag: int s }) return;
            S.RefreshSeconds = s;
            S.Save();
            service.ApplyInterval();
        };
        reconnect.Tick += (_, _) => { reconnect.Stop(); service.Reconnect(); };
        test.Click += async (_, _) => await TestAsync();
        Loaded += (_, _) => { service.Changed -= ShowState; service.Changed += ShowState; Fill(); };
        Unloaded += (_, _) =>
        {
            service.Changed -= ShowState;
            if (reconnect.IsEnabled) { reconnect.Stop(); service.Reconnect(); }
        };
    }

    private void Changed(Action apply)
    {
        if (loading) return;
        apply();
        S.Configured = false;
        S.Save();
        tested = null;
        reconnect.Stop();
        reconnect.Start();
        ShowState();
    }

    private string? tested; // the result of the last test, until something changes

    private void ShowState()
    {
        if (tested != null) { status.Text = tested; return; }
        status.Text = !S.HasServer ? T("Fill in the server, then Test connection.")
            : service.Connected ? F("Connected to Zabbix {0}: {1}.", service.ServerVersion, P(service.Hosts.Count, "{0} host", "{0} hosts"))
            : service.Error is { } e ? e
            : "";
    }

    /// <summary>Connects with what's filled in; on success it's marked as set up (and a path found elsewhere is kept).</summary>
    public async Task<bool> TestAsync()
    {
        test.IsEnabled = false;
        status.Text = T("Connecting…");
        try
        {
            using var c = await ZabbixClient.ConnectAsync(S);
            var hosts = await c.HostsAsync();
            await c.LogoutAsync(!S.UseToken);
            if (c.FoundPath != null)
            {
                loading = true;
                path.Text = S.Path = c.FoundPath;
                loading = false;
            }
            S.Configured = true;
            S.SetupSkipped = false;
            S.Save();
            tested = F("Connected to Zabbix {0}: {1}.", c.Version, P(hosts.Count, "{0} host", "{0} hosts"))
                + (c.FoundPath != null ? " " + F("(Its API was at {0}, so the path is now that.)", c.FoundPath) : "");
            status.Text = tested;
            reconnect.Stop();
            service.Reconnect();
            Connected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            tested = ex is ZabbixException ? ex.Message : F("Couldn't connect: {0}", ex.Message);
            status.Text = tested;
            return false;
        }
        finally { test.IsEnabled = true; }
    }
}
