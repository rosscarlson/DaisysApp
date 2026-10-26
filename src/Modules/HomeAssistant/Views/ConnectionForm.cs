using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// Where Home Assistant is and its access token, with Test connection. Used by the Settings page, the setup wizard's
/// step and the tab before it's set up. Every change is saved straight away; the connection is made again a moment
/// after the last one.
/// </summary>
internal sealed class ConnectionForm : StackPanel
{
    private readonly HaService service;
    private HaSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 300 };
    private readonly PasswordBox token = new() { MinWidth = 300 };
    private readonly CheckBox ignoreCert = new() { Content = T("Accept a certificate Windows doesn't trust") };
    private readonly Button test = new() { Content = T("Test connection"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly DispatcherTimer reconnect = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private string? tested;
    private bool loading = true;

    /// <summary>Raised after a successful test.</summary>
    public event Action? Connected;

    public ConnectionForm(HaService service)
    {
        this.service = service;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(string label, FrameworkElement field, string? tip = null)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 8) };
            field.Margin = new Thickness(0, 0, 0, 8);
            field.HorizontalAlignment = HorizontalAlignment.Left;
            if (tip != null) field.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(field, label.Length > 0 ? label : tip ?? "");
            Grid.SetRow(l, r);
            Grid.SetRow(field, r);
            Grid.SetColumn(field, 1);
            grid.Children.Add(l);
            grid.Children.Add(field);
        }
        Row(T("Address"), address, T("e.g. http://homeassistant.local:8123 or 192.168.1.20"));
        Row(T("Access token"), token, T("A long-lived access token: in Home Assistant, your profile → Security → Long-lived access tokens → Create token"));
        Row("", ignoreCert);
        Children.Add(grid);
        test.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(test);
        status.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(status);

        void Fill()
        {
            if (IsKeyboardFocusWithin) return;
            loading = true;
            if (address.Text != S.Address) address.Text = S.Address;
            if (token.Password != S.Token) token.Password = S.Token;
            ignoreCert.IsChecked = S.IgnoreCertificate;
            ShowState(null);
            loading = false;
        }
        Fill();

        address.TextChanged += (_, _) => Changed(() => S.Address = address.Text.Trim());
        token.PasswordChanged += (_, _) => Changed(() => S.Token = token.Password.Trim());
        ignoreCert.Click += (_, _) => Changed(() => S.IgnoreCertificate = ignoreCert.IsChecked == true);
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
        ShowState(null);
    }

    private void ShowState(IReadOnlyCollection<string>? ids)
    {
        if (ids != null) return;
        if (tested != null) { status.Text = tested; return; }
        status.Text = !S.HasServer ? T("Fill in the address and a token, then Test connection.")
            : service.Connected ? F("Connected to Home Assistant {0}: {1}.", service.Version, P(service.Things.Count, "{0} device", "{0} devices"))
            : service.Error is { } e ? e
            : service.Connecting ? T("Connecting…")
            : "";
    }

    /// <summary>Connects with what's filled in; on success it's marked as set up and the service connects.</summary>
    public async Task<bool> TestAsync()
    {
        test.IsEnabled = false;
        status.Text = T("Connecting…");
        try
        {
            using var c = await HaClient.ConnectAsync(S);
            var states = await c.CommandAsync("get_states");
            int n = states.ValueKind == System.Text.Json.JsonValueKind.Array ? states.GetArrayLength() : 0;
            S.Configured = true;
            S.SetupSkipped = false;
            S.Save();
            tested = F("Connected to Home Assistant {0}: {1}.", c.Version, P(n, "{0} entity", "{0} entities"));
            status.Text = tested;
            reconnect.Stop();
            service.Reconnect();
            Connected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            tested = ex is HaException ? ex.Message : F("Couldn't connect: {0}", ex.Message);
            status.Text = tested;
            return false;
        }
        finally { test.IsEnabled = true; }
    }
}
