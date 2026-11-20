using System.Windows;
using System.Windows.Controls;
using DaisysApp.Server;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.18.0) The Shared Session Manager's <b>Account</b>: Share Session's ForzaLeaderBoard account
/// (<see cref="SessionServer.Leaderboard"/>), in a window of its own (<see cref="AccountPanel"/>).
/// </summary>
internal sealed class AccountWindow
{
    private static Window? open;

    /// <param name="changed">Signed in or out: the sessions listed again.</param>
    public static void Show(FrameworkElement owner, Action changed)
    {
        if (open is { IsLoaded: true } was)
        {
            was.Activate();
            return;
        }
        var window = open = Ui.Dialog(T("ForzaLeaderBoard Account"), owner, 480);
        window.ResizeMode = ResizeMode.NoResize;
        var account = new AccountPanel(changed);
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18), Children = { account } };
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;
        window.Loaded += (_, _) => account.FocusFirst();
        window.Closed += (_, _) => open = null;
        window.Show();
    }
}

/// <summary>
/// The ForzaLeaderBoard account's rows: a user and password from the website (Sign up opens it) to sign in with, so
/// shares are filed under the account; none, anonymous (shares are this install's, by its device id, claimable on the
/// website once signed in). The user and password, and (since 4.18.2) the Server's address, filled with the built-in
/// one (another, such as its LAN address, for testing), and (since 4.18.3) its Port beside it (since SimEffects
/// Analysis 0.3.0 not in a product that hides "SimEffects/Analysis/SharePort": the one set is used). Never a key: the
/// built-in one is pinned whatever the address (4.18.0's key and Default went in 4.18.1). In <see cref="AccountWindow"/>,
/// and (since SimEffects Analysis 0.3.0) a card on a product's Settings ("SimEffects/Analysis/Account").
/// </summary>
internal sealed class AccountPanel : StackPanel
{
    private readonly Action changed;
    private readonly TextBox server = new();
    private readonly TextBox port = new() { Width = 62, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox user = new();
    private readonly PasswordBox password = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly Button signIn = new() { Content = T("Sign in"), Padding = new Thickness(16, 4, 16, 4) };
    private readonly Button signOut = new() { Content = T("Sign out"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0) };
    private string? problem;

    private static ServerConnection Link => SessionServer.Leaderboard;

    /// <param name="changed">Signed in or out.</param>
    public AccountPanel(Action changed)
    {
        this.changed = changed;
        var s = Link.Settings;
        server.Text = Link.Host;
        port.Text = Link.Port.ToString();
        user.Text = s.User;
        password.Password = s.Password;

        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetName(port, T("Port"));
        port.ToolTip = T("Port");
        var rows = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { MaxWidth = 300 }, new ColumnDefinition { Width = new GridLength(0.01, GridUnitType.Star) } } };
        Row(rows, T("Server"), server, AppPaths.Product?.Hides("SimEffects/Analysis/SharePort") == true ? null : port);
        Row(rows, T("User"), user);
        Row(rows, T("Password"), password);
        Children.Add(rows);
        signIn.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        signIn.Click += async (_, _) => await SignIn();
        // (Enter in the password: not the button as the default, which on Settings would be the whole window's)
        password.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await SignIn(); } };
        signOut.Click += (_, _) =>
        {
            Link.SignOut();
            password.Clear();
            Done();
        };
        var signUp = Ui.Link(T("Sign up"), SessionServer.Website + "/signup");
        signUp.VerticalAlignment = VerticalAlignment.Center;
        signUp.Margin = new Thickness(16, 0, 0, 0);
        Children.Add(new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Children = { signIn, signOut, signUp } });

        Link.Changed += LinkChanged;
        Unloaded += (_, _) => Link.Changed -= LinkChanged;
        Loaded += (_, _) => { Link.Changed -= LinkChanged; Link.Changed += LinkChanged; ShowState(); };
        ShowState();
    }

    /// <summary>The user box, or the password's if there's a user.</summary>
    public void FocusFirst() => (user.Text.Length == 0 ? user : (Control)password).Focus();

    private static void Row(Grid rows, string label, Control box, Control? more = null)
    {
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        int r = rows.RowDefinitions.Count;
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, MinWidth = 70, Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, r);
        rows.Children.Add(text);
        FrameworkElement field = box;
        if (more != null)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(more, Dock.Right);
            dock.Children.Add(more);
            dock.Children.Add(box);
            field = dock;
        }
        field.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(field, r);
        Grid.SetColumn(field, 1);
        rows.Children.Add(field);
    }

    private void LinkChanged() => Dispatcher.BeginInvoke(ShowState);

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
