using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DaisysApp.Server;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.18.0) The Shared Session Manager's <b>Account</b>: Share Session's connection to ForzaLeaderBoard
/// (<see cref="SessionServer.Leaderboard"/>). A user and password from the website (Sign up opens it) to sign in with,
/// so shares are filed under the account; none, anonymous (shares are this install's, by its device id, claimable on
/// the website once signed in). <b>Server</b> (folded while it's the built-in one): another address, port and key (for
/// testing, or a copy of one's own), trusted on first use with Forget, and Default back to ForzaLeaderBoard.
/// </summary>
internal sealed class AccountWindow
{
    private static AccountWindow? open;

    private readonly Window window;
    private readonly Action changed;
    private readonly TextBox user = new() { Width = 240 };
    private readonly PasswordBox password = new() { Width = 240 };
    private readonly TextBox host = new() { Width = 240 };
    private readonly TextBox port = new() { Width = 80 };
    private readonly TextBox key = new() { Width = 330, FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly Button signIn = new() { Content = T("Sign in"), Padding = new Thickness(16, 4, 16, 4) };
    private readonly Button signOut = new() { Content = T("Sign out"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button connect = new() { Content = T("Connect"), Padding = new Thickness(16, 4, 16, 4) };
    private readonly Button builtIn = new() { Content = T("Default"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button forgetKey;
    private readonly Expander server = new() { Header = T("Server"), Margin = new Thickness(0, 14, 0, 0) };
    private string? problem;

    private static ServerConnection Link => SessionServer.Leaderboard;

    /// <param name="changed">Signed in or out, or another server: the sessions listed again.</param>
    public static void Show(FrameworkElement owner, Action changed)
    {
        if (open is { window.IsLoaded: true } was)
        {
            was.window.Activate();
            return;
        }
        var w = open = new AccountWindow(owner, changed);
        w.window.Closed += (_, _) => open = null;
        w.window.Show();
    }

    private AccountWindow(FrameworkElement owner, Action changed)
    {
        this.changed = changed;
        window = Ui.Dialog(T("ForzaLeaderBoard Account"), owner, 600);
        window.ResizeMode = ResizeMode.NoResize;
        var s = Link.Settings;
        user.Text = s.User;
        password.Password = s.Password;
        var d = Link.Default!;

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(status);
        panel.Children.Add(Row(T("User"), user));
        panel.Children.Add(Row(T("Password"), password));
        signIn.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        signIn.Click += async (_, _) => await SignIn();
        signOut.Click += (_, _) =>
        {
            Link.SignOut();
            password.Clear();
            Done();
        };
        var signUp = Ui.Link(T("Sign up"), SessionServer.Website + "/signup");
        signUp.VerticalAlignment = VerticalAlignment.Center;
        signUp.Margin = new Thickness(16, 0, 0, 0);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(100, 4, 0, 0), Children = { signIn, signOut, signUp } });

        // another server (testing, or one's own copy)
        var servers = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        servers.Children.Add(Row(T("Address"), Hint(host, d.Host),
            new TextBlock { Text = T("Port"), Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, Hint(port, d.Port.ToString())));
        forgetKey = ConfirmButton.Make(T("Forget"), () =>
        {
            Link.ForgetKey();
            Done();
        });
        forgetKey.Margin = new Thickness(8, 0, 0, 0);
        forgetKey.ToolTip = T("Forget the server's key (after it's reinstalled)");
        key.ToolTip = T("Empty: the key it has the first time");
        servers.Children.Add(Row(T("Server key"), Hint(key, d.Key), forgetKey));
        connect.Click += async (_, _) => await Connect();
        builtIn.ToolTip = d.Host;
        builtIn.Click += async (_, _) => await Apply(Link.SetUpAsync("", d.Port, Link.Settings.User, Link.Settings.Password));
        servers.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(100, 4, 0, 0), Children = { connect, builtIn } });
        // (the expander's own text colour is for its header)
        servers.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        server.Content = servers;
        server.IsExpanded = !Link.UsesBuiltIn;
        panel.Children.Add(server);

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;

        Link.Changed += LinkChanged;
        window.Closed += (_, _) => Link.Changed -= LinkChanged;
        window.Loaded += (_, _) => (user.Text.Length == 0 ? user : (Control)password).Focus();
        Fill();
    }

    private static StackPanel Row(string label, params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center });
        foreach (var i in items) p.Children.Add(i);
        if (items[0] is Grid { Children: [FrameworkElement box, ..] }) System.Windows.Automation.AutomationProperties.SetName(box, label);
        else System.Windows.Automation.AutomationProperties.SetName(items[0], label);
        return p;
    }

    /// <summary>The box with <paramref name="text"/> greyed in it while it's empty.</summary>
    private static UIElement Hint(TextBox box, string text)
    {
        var hint = new TextBlock { Text = text, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontFamily = box.FontFamily };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        void Show() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Show();
        Show();
        return new Grid { Children = { box, hint } };
    }

    private void LinkChanged() => window.Dispatcher.BeginInvoke(ShowState);

    /// <summary>The boxes as set up, and the state.</summary>
    private void Fill()
    {
        var s = Link.Settings;
        host.Text = Link.UsesBuiltIn ? "" : s.Host;
        port.Text = Link.UsesBuiltIn ? "" : s.Port.ToString();
        key.Text = Link.UsesBuiltIn ? "" : s.ServerKey;
        ShowState();
    }

    private void ShowState()
    {
        var s = Link.Settings;
        signOut.IsEnabled = Link.SignedIn;
        forgetKey.IsEnabled = !Link.UsesBuiltIn && s.ServerKey.Length > 0;
        builtIn.IsEnabled = !Link.UsesBuiltIn;
        string where = Link.UsesBuiltIn ? Link.Name : $"{Link.Name} ({Link.Host}:{Link.Port})";
        bool bad = problem != null || !Link.Connected && Link.Problem.Length > 0;
        status.Text = problem ?? (Link.Connected
            ? Link.SignedIn ? F("Signed in as {0} on {1}", s.User, where) : F("Connected to {0}, not signed in", where)
            : Link.Problem.Length > 0 ? Link.Problem
            : Link.SignedIn ? F("{0} on {1}", s.User, where) : F("Not signed in: {0}", where));
        status.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private async Task SignIn()
    {
        if (user.Text.Trim().Length == 0 || password.Password.Length == 0) { Problem(T("Give a user and a password")); return; }
        var s = Link.Settings;
        await Apply(Link.SetUpAsync(s.Host, s.Port, user.Text, password.Password));
    }

    private async Task Connect()
    {
        int p = Link.Default!.Port;
        if (port.Text.Trim().Length > 0 && (!int.TryParse(port.Text.Trim(), out p) || p is < 1 or > 65535)) { Problem(T("A port is 1 to 65535")); return; }
        string k = key.Text.Trim();
        if (k.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(k, "^[A-Za-z0-9_-]{43}$")) { Problem(T("A server key is 43 letters, digits, - and _")); return; }
        var s = Link.Settings;
        await Apply(Link.SetUpAsync(host.Text, p, s.User, s.Password, k));
    }

    /// <summary>A setup tried: its problem shown, or the boxes filled again and the sessions listed again.</summary>
    private async Task Apply(Task<string?> setUp)
    {
        signIn.IsEnabled = connect.IsEnabled = builtIn.IsEnabled = false;
        problem = null;
        status.Text = T("Connecting…");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        string? result;
        try { result = await setUp; }
        finally { signIn.IsEnabled = connect.IsEnabled = true; }
        if (result != null) { Problem(result.TrimEnd('.')); return; }
        password.Password = Link.Settings.Password;
        Done();
    }

    private void Done()
    {
        problem = null;
        Fill();
        changed();
    }

    private void Problem(string text)
    {
        problem = text;
        ShowState();
    }
}
