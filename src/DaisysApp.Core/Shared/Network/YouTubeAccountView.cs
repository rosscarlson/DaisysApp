using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Shared.Network;

/// <summary>
/// (since Core 0.62, 4.15.0) The YouTube account's rows for any module's settings: the OAuth client's ID and secret
/// (a link to where one's made), Sign In (in the browser) or Sign Out, and who's signed in. One account for the app
/// (<see cref="YouTube"/>).
/// </summary>
public sealed class YouTubeAccountView : StackPanel
{
    private readonly TextBox id = new() { Margin = new Thickness(0, 2, 0, 8) };
    private readonly PasswordBox secret = new() { Margin = new Thickness(0, 2, 0, 8) };
    private readonly Button sign = new() { MinWidth = 120, Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = Ui.Note("", new Thickness(10, 0, 0, 0));
    private CancellationTokenSource? signing;

    public YouTubeAccountView()
    {
        Children.Add(new TextBlock { Text = T("OAuth client ID") });
        AutomationProperties.SetName(id, T("OAuth client ID"));
        id.Text = YouTube.Account.ClientId;
        Children.Add(id);
        Children.Add(new TextBlock { Text = T("Client secret") });
        AutomationProperties.SetName(secret, T("Client secret"));
        secret.Password = YouTube.ClientSecret;
        Children.Add(secret);
        var how = Ui.Link(T("Make a Desktop app client (YouTube Data API v3 on)"), YouTube.ConsoleUrl);
        how.Margin = new Thickness(0, 0, 0, 10);
        Children.Add(how);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        var row = new DockPanel();
        DockPanel.SetDock(sign, Dock.Left);
        row.Children.Add(sign);
        row.Children.Add(status);
        Children.Add(row);

        id.LostFocus += (_, _) => YouTube.SetClient(id.Text, secret.Password);
        secret.LostFocus += (_, _) => YouTube.SetClient(id.Text, secret.Password);
        sign.Click += async (_, _) =>
        {
            if (signing != null) { signing.Cancel(); return; }
            if (YouTube.SignedIn) { YouTube.SignOut(); Show(); return; }
            YouTube.SetClient(id.Text, secret.Password);
            signing = new CancellationTokenSource();
            Show(T("Signing in, in the browser…"));
            var (ok, message) = await YouTube.SignIn(signing.Token);
            signing.Dispose();
            signing = null;
            Show(ok ? null : message);
        };
        YouTube.Changed += Changed;
        Unloaded += (_, _) => YouTube.Changed -= Changed;
        Loaded += (_, _) => { YouTube.Changed -= Changed; YouTube.Changed += Changed; };
        Show();
    }

    private void Changed()
    {
        if (Dispatcher.CheckAccess()) { if (signing == null) Show(); }
        else Dispatcher.BeginInvoke(Changed);
    }

    private void Show(string? note = null)
    {
        bool busy = signing != null;
        sign.Content = busy ? T("Cancel") : YouTube.SignedIn ? T("Sign Out") : T("Sign In");
        AutomationProperties.SetName(sign, (string)sign.Content);
        id.IsEnabled = secret.IsEnabled = !busy && !YouTube.SignedIn;
        status.Text = note ?? (YouTube.SignedIn ? F("Signed in: {0}", YouTube.Account.Channel) : T("Not signed in"));
    }
}
