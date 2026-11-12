using System.Windows.Controls;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Plex Media Server's address and token (the rest is Core's <see cref="DaisysApp.Shared.ConnectionForm"/>), with
/// <b>This PC's Plex</b> (the token Plex keeps in this account's registry) and <b>Sign in with Plex…</b> (plex.tv in the
/// browser, then a server of the account).
/// </summary>
internal sealed class PlexForm : DaisysApp.Shared.ConnectionForm
{
    private readonly PlexService service;
    private PlexSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 320 };
    private readonly PasswordBox token = new() { MinWidth = 320 };
    private readonly CheckBox ignoreCert = new() { Content = T("Accept a certificate Windows doesn't trust") };
    private readonly ComboBox servers = new() { MinWidth = 320, Visibility = Visibility.Collapsed };
    private readonly Button signIn = new() { Content = T("Sign in with Plex…"), Padding = new Thickness(12, 4, 12, 4) };
    private CancellationTokenSource? signing;

    public PlexForm(PlexService service)
    {
        this.service = service;
        var quick = new StackPanel { Orientation = Orientation.Horizontal };
        if (PlexLocal.Installed && PlexLocal.Token != null)
        {
            var local = new Button { Content = T("This PC's Plex"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), ToolTip = T("The server on this PC, with the token it keeps") };
            local.SetResourceReference(StyleProperty, "AccentButton");
            local.Click += async (_, _) =>
            {
                Fill("http://127.0.0.1:32400", PlexLocal.Token ?? "");
                await TestAsync();
            };
            quick.Children.Add(local);
        }
        signIn.ToolTip = T("Sign in at plex.tv in your browser, then pick a server");
        signIn.Click += async (_, _) => await SignIn();
        quick.Children.Add(signIn);
        Row("", quick);
        var serverLabel = Row(T("Server"), servers);
        serverLabel.Visibility = Visibility.Collapsed;
        servers.IsVisibleChanged += (_, _) => serverLabel.Visibility = servers.Visibility;
        Row(T("Address"), address, T("e.g. http://192.168.1.20:32400 (32400 if no port)"));
        Row(T("Token"), token, T("Your X-Plex-Token (Sign in with Plex fills it in)"));
        Row("", ignoreCert);
        Finish();

        address.TextChanged += (_, _) => Changed(() => S.Address = address.Text.Trim());
        token.PasswordChanged += (_, _) => Changed(() => S.Token = token.Password.Trim());
        ignoreCert.Click += (_, _) => Changed(() => S.IgnoreCertificate = ignoreCert.IsChecked == true);
        servers.SelectionChanged += async (_, _) => { if (servers.SelectedItem is PlexServerChoice c) await Pick(c); };
        Unloaded += (_, _) => signing?.Cancel();
    }

    private void Fill(string a, string t)
    {
        address.Text = a;
        token.Password = t;
    }

    private async Task SignIn()
    {
        signing?.Cancel();
        signing = new CancellationTokenSource();
        var cancel = signing.Token;
        signIn.IsEnabled = false;
        Status.Text = T("Approve the sign-in in your browser…");
        try
        {
            string accountToken = await PlexClient.SignInAsync(S.ClientId, cancel);
            Status.Text = T("Signed in: finding your servers…");
            var list = await PlexClient.ServersAsync(S.ClientId, accountToken, cancel);
            Log.Here.Info($"Signed in to plex.tv: {list.Count} server(s)");
            if (list.Count == 0) { Status.Text = T("Signed in, but the account has no servers."); return; }
            servers.ItemsSource = list.OrderByDescending(s => s.Owned).ToList();
            servers.Visibility = Visibility.Visible;
            if (list.Count == 1) servers.SelectedIndex = 0;
            else Status.Text = T("Pick the server.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = ex is PlexException ? ex.Message : F("Couldn't sign in: {0}", ex.Message); }
        finally { signIn.IsEnabled = true; }
    }

    /// <summary>A server of the account: its first address that answers (local ones first).</summary>
    private async Task Pick(PlexServerChoice c)
    {
        Status.Text = F("Trying {0}…", c.Name);
        foreach (var uri in c.Uris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) continue;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await new PlexClient(u, c.Token, S.ClientId, false).GetAsync("/identity", cts.Token);
                Fill(uri, c.Token);
                await TestAsync();
                return;
            }
            catch (Exception ex) { Log.Here.Debug(() => $"{uri}: {ex.Message}"); }
        }
        Status.Text = F("None of {0}'s addresses answered from here.", c.Name);
    }

    protected override void FillFields()
    {
        if (address.Text != S.Address) address.Text = S.Address;
        if (token.Password != S.Token) token.Password = S.Token;
        ignoreCert.IsChecked = S.IgnoreCertificate;
    }

    protected override void Watch(bool on)
    {
        if (on) service.Changed += Show;
        else service.Changed -= Show;
    }

    private void Show() => Dispatcher.BeginInvoke(ShowState);

    protected override void ShowState()
    {
        Status.Text = Tested ?? (!S.HasServer ? T("Pick a way to connect, or fill in the address and token, then Test connection.")
            : service.Connected ? F("Connected to {0}, Plex Media Server {1}{2}.", service.ServerName, service.Version, service.IsLocal ? T(" (on this PC)") : "")
            : service.Error is { } e ? e
            : service.Connecting ? T("Connecting…")
            : "");
    }

    protected override void Unconfirm()
    {
        S.Configured = false;
        S.Save();
    }

    protected override async Task<string> Connect()
    {
        var c = PlexClient.For(S) ?? throw new PlexException(T("Fill in the address and token."));
        var root = await c.GetAsync("/");
        if (PlexClient.Str(root, "machineIdentifier") == null) throw new PlexException(T("That doesn't look like a Plex Media Server."));
        S.Configured = true;
        S.SetupSkipped = false;
        S.Save();
        return F("Connected to {0}, Plex Media Server {1}.", PlexClient.Str(root, "friendlyName") ?? "Plex", PlexClient.Str(root, "version") ?? "");
    }

    protected override void Reconnect() => service.Reconnect();

    protected override bool OwnError(Exception ex) => ex is PlexException;
}

/// <summary>Tautulli's address and API key, with <b>This PC's Tautulli</b> when its config.ini is here.</summary>
internal sealed class TautulliForm : DaisysApp.Shared.ConnectionForm
{
    private readonly PlexService service;
    private PlexSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 320 };
    private readonly PasswordBox key = new() { MinWidth = 320 };

    public TautulliForm(PlexService service)
    {
        this.service = service;
        if (PlexLocal.Tautulli() is { } found)
        {
            var local = new Button { Content = T("This PC's Tautulli"), Padding = new Thickness(12, 4, 12, 4), ToolTip = T("The address and API key from Tautulli's settings on this PC") };
            local.Click += async (_, _) =>
            {
                address.Text = found.Address;
                key.Password = found.Key;
                await TestAsync();
            };
            Row("", local);
        }
        Row(T("Address"), address, T("e.g. http://192.168.1.20:8181 (8181 if no port)"));
        Row(T("API key"), key, T("Tautulli → Settings → Web Interface → API key"));
        Finish();
        address.TextChanged += (_, _) => Changed(() => S.TautulliAddress = address.Text.Trim());
        key.PasswordChanged += (_, _) => Changed(() => S.TautulliKey = key.Password.Trim());
    }

    protected override void FillFields()
    {
        if (address.Text != S.TautulliAddress) address.Text = S.TautulliAddress;
        if (key.Password != S.TautulliKey) key.Password = S.TautulliKey;
    }

    protected override void Watch(bool on)
    {
        if (on) service.Changed += Show;
        else service.Changed -= Show;
    }

    private void Show() => Dispatcher.BeginInvoke(ShowState);

    protected override void ShowState()
    {
        Status.Text = Tested ?? (!S.HasTautulli ? T("Optional: fill in Tautulli's address and API key, then Test connection.")
            : service.TautulliConnected ? F("Connected to Tautulli {0}.", service.TautulliVersion)
            : service.TautulliError ?? "");
    }

    protected override void Unconfirm()
    {
        S.TautulliConfigured = false;
        S.Save();
    }

    protected override async Task<string> Connect()
    {
        var c = TautulliClient.For(S) ?? throw new PlexException(T("Fill in the address and API key."));
        var info = await c.CallAsync("get_tautulli_info");
        S.TautulliConfigured = true;
        S.Save();
        return F("Connected to Tautulli {0}.", PlexClient.Str(info, "tautulli_version") ?? "");
    }

    protected override void Reconnect() => service.Reconnect();

    protected override bool OwnError(Exception ex) => ex is PlexException;
}
