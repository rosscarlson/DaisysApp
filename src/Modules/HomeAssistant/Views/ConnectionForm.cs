using System.Windows.Controls;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>Where Home Assistant is and its access token (the rest is Core's <see cref="DaisysApp.Shared.ConnectionForm"/>).</summary>
internal sealed class ConnectionForm : DaisysApp.Shared.ConnectionForm
{
    private readonly HaService service;
    private HaSettings S => service.Settings;
    private readonly TextBox address = new() { MinWidth = 300 };
    private readonly PasswordBox token = new() { MinWidth = 300 };
    private readonly CheckBox ignoreCert = new() { Content = T("Accept a certificate Windows doesn't trust") };

    public ConnectionForm(HaService service)
    {
        this.service = service;
        Row(T("Address"), address, T("e.g. http://homeassistant.local:8123 or 192.168.1.20"));
        Row(T("Access token"), token, T("A long-lived access token: in Home Assistant, your profile → Security → Long-lived access tokens → Create token"));
        Row("", ignoreCert);
        Finish();

        address.TextChanged += (_, _) => Changed(() => S.Address = address.Text.Trim());
        token.PasswordChanged += (_, _) => Changed(() => S.Token = token.Password.Trim());
        ignoreCert.Click += (_, _) => Changed(() => S.IgnoreCertificate = ignoreCert.IsChecked == true);
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

    private void Show(IReadOnlyCollection<string>? ids)
    {
        if (ids == null) ShowState();
    }

    protected override void ShowState()
    {
        Status.Text = Tested ?? (!S.HasServer ? T("Fill in the address and a token, then Test connection.")
            : service.Connected ? F("Connected to Home Assistant {0}: {1}.", service.Version, P(service.Things.Count, "{0} device", "{0} devices"))
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
        using var c = await HaClient.ConnectAsync(S);
        var states = await c.CommandAsync("get_states");
        int n = states.ValueKind == System.Text.Json.JsonValueKind.Array ? states.GetArrayLength() : 0;
        S.Configured = true;
        S.SetupSkipped = false;
        S.Save();
        return F("Connected to Home Assistant {0}: {1}.", c.Version, P(n, "{0} entity", "{0} entities"));
    }

    protected override void Reconnect() => service.Reconnect();

    protected override bool OwnError(Exception ex) => ex is HaException;
}
