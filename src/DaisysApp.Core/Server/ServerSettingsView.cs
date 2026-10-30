using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared;

namespace DaisysApp.Server;

/// <summary>
/// The Daisy's Server card, on Settings → General: the server's address and port,
/// the token it gave, the user and password; <b>Connect</b> tries them and keeps them if they work. Once set up:
/// Change password (the user's data key wrapped again there, so nothing is lost) and Forget.
/// </summary>
public sealed class ServerSettingsView : Border
{
    private readonly TextBox host = new() { Width = 240 };
    private readonly TextBox port = new() { Width = 80 };
    private readonly TextBox token = new() { Width = 420, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private readonly TextBox user = new() { Width = 200 };
    private readonly PasswordBox password = new() { Width = 200 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    private readonly StackPanel signedIn = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button connect;

    public ServerSettingsView()
    {
        SetResourceReference(StyleProperty, "Card");
        var s = ServerLink.Settings;
        host.Text = s.Host;
        port.Text = s.Port.ToString();
        token.Text = s.Token;
        user.Text = s.User;
        password.Password = s.Password;

        var panel = new StackPanel();
        var header = new TextBlock { Text = T("Daisy's Server") };
        header.SetResourceReference(StyleProperty, "CardHeader");
        panel.Children.Add(header);
        panel.Children.Add(Row(T("Address"), host, new TextBlock { Text = T("Port"), Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, port));
        panel.Children.Add(Row(T("Token"), token));
        panel.Children.Add(Row(T("User"), user));
        panel.Children.Add(Row(T("Password"), password));
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
            token.Clear();
            user.Clear();
            password.Clear();
            port.Text = ServerLinkSettings.DefaultPort.ToString();
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

    private void Refresh()
    {
        signedIn.Visibility = ServerLink.Configured ? Visibility.Visible : Visibility.Collapsed;
        bool bad = false;
        if (!ServerLink.Configured) status.Text = T("Not set up");
        else if (ServerLink.Connected) status.Text = F("Connected to {0} as {1}", ServerLink.Name, ServerLink.Settings.User);
        else if (ServerLink.Problem.Length > 0) { status.Text = ServerLink.Problem; bad = true; }
        else status.Text = F("Set up: {0} as {1}", ServerLink.Settings.Host, ServerLink.Settings.User);
        status.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(port.Text.Trim(), out int p) || p is < 1 or > 65535)
        {
            Show(T("A port is 1 to 65535"));
            return;
        }
        connect.IsEnabled = false;
        status.Text = T("Connecting…");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var problem = await ServerLink.SetUpAsync(host.Text, p, token.Text, user.Text, password.Password);
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
