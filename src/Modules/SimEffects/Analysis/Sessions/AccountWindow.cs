using System.Windows;
using System.Windows.Controls;
using DaisysApp.Server;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.18.0) The Shared Session Manager's <b>Account</b>: Share Session's ForzaLeaderBoard account
/// (<see cref="SessionServer.Leaderboard"/>). A user and password from the website (Sign up opens it) to sign in with,
/// so shares are filed under the account; none, anonymous (shares are this install's, by its device id, claimable on
/// the website once signed in). The user and password, and (since 4.18.2) the Server's address, filled with the built-in
/// one (another, such as its LAN address, for testing), and (since 4.18.3) its Port beside it. Never a key: the
/// built-in one is pinned whatever the address (4.18.0's key and Default went in 4.18.1).
/// </summary>
internal sealed class AccountWindow
{
    private static AccountWindow? open;

    private readonly Window window;
    private readonly Action changed;
    private readonly TextBox server = new() { Width = 170 };
    private readonly TextBox port = new() { Width = 62, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox user = new() { Width = 240 };
    private readonly PasswordBox password = new() { Width = 240 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly Button signIn = new() { Content = T("Sign in"), Padding = new Thickness(16, 4, 16, 4) };
    private readonly Button signOut = new() { Content = T("Sign out"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0) };
    private string? problem;

    private static ServerConnection Link => SessionServer.Leaderboard;

    /// <param name="changed">Signed in or out: the sessions listed again.</param>
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
        window = Ui.Dialog(T("ForzaLeaderBoard Account"), owner, 480);
        window.ResizeMode = ResizeMode.NoResize;
        var s = Link.Settings;
        server.Text = Link.Host;
        port.Text = Link.Port.ToString();
        user.Text = s.User;
        password.Password = s.Password;

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetName(port, T("Port"));
        port.ToolTip = T("Port");
        panel.Children.Add(Row(T("Server"), server, port));
        panel.Children.Add(Row(T("User"), user));
        panel.Children.Add(Row(T("Password"), password));
        signIn.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        signIn.IsDefault = true;
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

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;

        Link.Changed += LinkChanged;
        window.Closed += (_, _) => Link.Changed -= LinkChanged;
        window.Loaded += (_, _) => (user.Text.Length == 0 ? user : (Control)password).Focus();
        ShowState();
    }

    private static StackPanel Row(string label, Control box, params Control[] more)
    {
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(box);
        foreach (var c in more) p.Children.Add(c);
        return p;
    }

    private void LinkChanged() => window.Dispatcher.BeginInvoke(ShowState);

    /// <summary>Only whether, and as whom, it's signed in (no address or key).</summary>
    private void ShowState()
    {
        var s = Link.Settings;
        signOut.IsEnabled = Link.SignedIn;
        bool bad = problem != null || !Link.Connected && Link.Problem.Length > 0;
        status.Text = problem
            ?? (!Link.Connected && Link.Problem.Length > 0 ? T("Can't reach ForzaLeaderBoard")
            : Link.SignedIn ? F("Signed in as {0}", s.User) : T("Not signed in"));
        status.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private async Task SignIn()
    {
        // (the built-in server and port back if they're emptied)
        var builtIn = Link.Default!;
        string host = server.Text.Trim();
        if (host.Length == 0) host = builtIn.Host;
        int number = builtIn.Port;
        if (port.Text.Trim().Length > 0 && (!int.TryParse(port.Text.Trim(), out number) || number is < 1 or > 65535))
        {
            Problem(T("That port isn't right"));
            return;
        }
        // (another server with no user: anonymous there)
        bool moved = !host.Equals(Link.Host, StringComparison.OrdinalIgnoreCase) || number != Link.Port;
        if (!(moved && user.Text.Trim().Length == 0 && password.Password.Length == 0)
            && (user.Text.Trim().Length == 0 || password.Password.Length == 0)) { Problem(T("Give a user and a password")); return; }
        signIn.IsEnabled = false;
        problem = null;
        status.Text = T("Signing in…");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        string? result;
        // (the built-in key, whatever the address: the LAN address is the same server)
        try { result = await Link.SetUpAsync(host, number, user.Text, password.Password, builtIn.Key); }
        finally { signIn.IsEnabled = true; }
        if (result != null)
        {
            // (a problem with the server's key isn't the user's to deal with)
            Problem(result.Contains("key", StringComparison.OrdinalIgnoreCase) ? T("Can't reach ForzaLeaderBoard") : result.TrimEnd('.'));
            return;
        }
        server.Text = Link.Host;
        port.Text = Link.Port.ToString();
        Done();
    }

    private void Done()
    {
        problem = null;
        ShowState();
        changed();
    }

    private void Problem(string text)
    {
        problem = text;
        ShowState();
    }
}
