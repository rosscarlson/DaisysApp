using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared;

namespace DaisysApp.Server;

/// <summary>
/// The Daisy's Server card, on Settings → General: the server's address and port, the
/// user and password (empty: not signed in; since 4.18.0 Share Session is on ForzaLeaderBoard, not here); <b>Connect</b> tries them and keeps them if they work.
/// The server's key (its fingerprint, kept from the first connection) with Forget, for a reinstalled server. Once
/// signed in: Change password (the user's data key wrapped again there, so nothing is lost) and Forget.
/// </summary>
public sealed class ServerSettingsView : Border
{
    private readonly TextBox host = new() { Width = 240 };
    private readonly TextBox port = new() { Width = 80 };
    private readonly TextBox user = new() { Width = 200 };
    private readonly PasswordBox password = new() { Width = 200 };
    private readonly TextBox key = new() { Width = 420, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    private readonly StackPanel signedIn = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button connect;
    private readonly Button forgetKey;

    public ServerSettingsView()
    {
        SetResourceReference(StyleProperty, "Card");
        var s = ServerLink.Settings;
        host.Text = s.Host;
        port.Text = s.Host.Length > 0 ? s.Port.ToString() : "";
        user.Text = s.User;
        password.Password = s.Password;

        var panel = new StackPanel();
        var header = new TextBlock { Text = T("Daisy's Server") };
        header.SetResourceReference(StyleProperty, "CardHeader");
        panel.Children.Add(header);
        panel.Children.Add(Row(T("Address"), host,
            new TextBlock { Text = T("Port"), Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
            Placeholder(port, ServerLinkSettings.DefaultPort.ToString())));
        panel.Children.Add(Row(T("User"), user));
        panel.Children.Add(Row(T("Password"), password));
        forgetKey = ConfirmButton.Make(T("Forget"), () =>
        {
            ServerLink.ForgetKey();
            Refresh();
        });
        forgetKey.Margin = new Thickness(8, 0, 0, 0);
        forgetKey.ToolTip = T("Forget the server's key (after it's reinstalled)");
        panel.Children.Add(Row(T("Server key"), key, forgetKey));
        connect = new Button { Content = T("Connect"), Padding = new Thickness(16, 4, 16, 4) };
        connect.SetResourceReference(StyleProperty, "AccentButton");
        connect.Click += async (_, _) => await ConnectAsync();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(100, 4, 0, 0), Children = { connect, status } });

        // signed in: the password changed there, or everything forgotten here
        var current = new PasswordBox { Width = 160 };
        var next = new PasswordBox { Width = 160, Margin = new Thickness(8, 0, 0, 0) };
        var result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var change = new Button { Content = T("Change"), Margin = new Thickness(8, 0, 0, 0) };
        change.Click += async (_, _) =>
        {
            change.IsEnabled = false;
            var problem = await ServerLink.ChangePasswordAsync(current.Password, next.Password);
            change.IsEnabled = true;
            result.Text = problem ?? T("✓ Changed");
            result.SetResourceReference(TextBlock.ForegroundProperty, problem != null ? "ErrorTextBrush" : "TextSecondaryBrush");
            if (problem == null)
            {
                password.Password = next.Password;
                current.Clear();
                next.Clear();
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(current, T("Current password"));
        System.Windows.Automation.AutomationProperties.SetName(next, T("New password"));
        current.ToolTip = T("Current password");
        next.ToolTip = T("New password");
        var forget = ConfirmButton.Make(T("Forget"), () =>
        {
            ServerLink.Forget();
            host.Clear();
            port.Clear();
            user.Clear();
            password.Clear();
            Refresh();
        });
        forget.Margin = new Thickness(16, 0, 0, 0);
        var labels = new TextBlock { Text = T("Current, new"), Width = 100, VerticalAlignment = VerticalAlignment.Center };
        labels.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var changing = new StackPanel { Orientation = Orientation.Horizontal, Children = { labels, current, next, change, forget, result } };
        signedIn.Children.Add(new TextBlock { Text = T("Change password"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        signedIn.Children.Add(changing);
        panel.Children.Add(signedIn);
        Child = panel;

        ServerLink.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private static StackPanel Row(string label, params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center });
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    /// <summary>The box with <paramref name="text"/> greyed in it while it's empty.</summary>
    private static UIElement Placeholder(TextBox box, string text)
    {
        if (text.Length == 0) return box;
        var hint = new TextBlock { Text = text, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        void Show() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Show();
        Show();
        return new Grid { Children = { box, hint } };
    }

    private void Refresh()
    {
        var s = ServerLink.Settings;
        signedIn.Visibility = ServerLink.SignedIn ? Visibility.Visible : Visibility.Collapsed;
        key.Text = s.ServerKey;
        forgetKey.IsEnabled = s.ServerKey.Length > 0;
        bool bad = false;
        if (!ServerLink.Configured) status.Text = T("Not set up");
        else if (ServerLink.Connected)
            status.Text = ServerLink.SignedIn ? F("Connected to {0} as {1}", ServerLink.Name, s.User)
                : F("Connected to {0} (not signed in)", ServerLink.Name);
        else if (ServerLink.Problem.Length > 0) { status.Text = ServerLink.Problem; bad = true; }
        else status.Text = ServerLink.SignedIn ? F("Set up: {0} as {1}", s.Host, s.User) : F("Set up: {0}", s.Host);
        status.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private async Task ConnectAsync()
    {
        int p = ServerLinkSettings.DefaultPort;
        if (port.Text.Trim().Length > 0 && (!int.TryParse(port.Text.Trim(), out p) || p is < 1 or > 65535))
        {
            Show(T("A port is 1 to 65535"));
            return;
        }
        connect.IsEnabled = false;
        status.Text = T("Connecting…");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var problem = await ServerLink.SetUpAsync(host.Text, p, user.Text, password.Password);
        connect.IsEnabled = true;
        if (problem != null) { Show(problem); return; }
        Refresh();
        status.Text = T("✓ Connected and saved");
    }

    private void Show(string problem)
    {
        status.Text = problem;
        status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }
}
