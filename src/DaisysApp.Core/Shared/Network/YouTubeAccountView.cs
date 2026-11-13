using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Shared.Network;

/// <summary>
/// (since Core 0.62, 4.15.0) The YouTube account's rows for any module's settings: Sign In (in the browser) or Sign Out,
/// and who's signed in; the OAuth client's ID and secret (a link to where one's made) above them when the app has no
/// client of its own, else folded away under "Your own OAuth client" (since 4.15.1). One account for the app
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
        var own = new StackPanel();
        own.Children.Add(new TextBlock { Text = T("OAuth client ID") });
        AutomationProperties.SetName(id, T("OAuth client ID"));
        id.Text = YouTube.Account.ClientId;
        own.Children.Add(id);
        own.Children.Add(new TextBlock { Text = T("Client secret") });
        AutomationProperties.SetName(secret, T("Client secret"));
        secret.Password = YouTube.ClientSecret;
        own.Children.Add(secret);
        var how = Ui.Link(T("Make a Desktop app client (YouTube Data API v3 on)"), YouTube.ConsoleUrl);
        how.Margin = new Thickness(0, 0, 0, 10);
        own.Children.Add(how);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        var row = new DockPanel();
        DockPanel.SetDock(sign, Dock.Left);
        row.Children.Add(sign);
        row.Children.Add(status);
        if (YouTube.AppClient)
        {
            Children.Add(row);
            own.Margin = new Thickness(0, 6, 0, 0);
            Children.Add(new Expander { Header = T("Your own OAuth client"), Content = own, Margin = new Thickness(0, 10, 0, 0), ToolTip = T("Blank: the app's") });
        }
        else
        {
            Children.Add(own);
            Children.Add(row);
        }

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
