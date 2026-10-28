using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Connection: the adapter the internet goes through (type, link speed, addresses, gateway, DNS,
/// DHCP), its Wi-Fi link when it's Wi-Fi (network, access point, signal, channel, rates, security), its error and
/// discard counters (a flaky cable or driver shows here), your public address and provider, and every adapter.
/// Refreshed every 2 seconds while it's showing.
/// </summary>
internal sealed class ConnectionView : UserControl, IDisposable
{
    private sealed record AdapterRow(string Name, string Type, string Status, string Speed, string Address, string Mac, string Description);

    private readonly Ui.Fields main = new(), wifi = new(), counters = new(), publicFields = new();
    private readonly Border wifiCard;
    private readonly DataGrid adapters = Ui.Table();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock publicStatus = Ui.Secondary();
    private (long Errors, long Discards, DateTime At)? baseline;
    private string adapterId = "";
    private bool publicAsked;

    public ConnectionView()
    {
        var left = new StackPanel();
        left.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("This Connection")), main } }, new Thickness(0)));
        wifiCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Wi-Fi")), wifi } }, new Thickness(0, 12, 0, 0));
        left.Children.Add(wifiCard);

        var right = new StackPanel();
        var refresh = Ui.IconButton("", "", T("Look up again"), () => _ = LookUpPublic());
        right.Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Public Address")), refresh), publicFields, publicStatus } }, new Thickness(0)));
        right.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Errors")), counters } }, new Thickness(0, 12, 0, 0)));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(left);
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        adapters.Columns.Add(Ui.Column(T("Name"), nameof(AdapterRow.Name), -1));
        adapters.Columns.Add(Ui.Column(T("Type"), nameof(AdapterRow.Type), 110));
        adapters.Columns.Add(Ui.Column(T("Status"), nameof(AdapterRow.Status), 90));
        adapters.Columns.Add(Ui.Column(T("Speed"), nameof(AdapterRow.Speed), 110));
        adapters.Columns.Add(Ui.Column(T("Address"), nameof(AdapterRow.Address), 140));
        adapters.Columns.Add(Ui.Column(T("MAC"), nameof(AdapterRow.Mac), 150));
        adapters.Columns.Add(Ui.Column(T("Device"), nameof(AdapterRow.Description), -1.4));
        var all = Ui.Card(new StackPanel { Children = { Ui.Header(T("Adapters")), adapters } }, new Thickness(0, 12, 0, 0));

        Content = Ui.Page(new StackPanel { Children = { columns, all } });
        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); timer.Start(); if (!publicAsked) _ = LookUpPublic(); }
            else timer.Stop();
        };
    }

    private async Task LookUpPublic()
    {
        publicAsked = true;
        publicStatus.Text = T("Looking up…");
        var me = await Geo.MineAsync();
        publicFields.Clear();
        if (me == null) { publicStatus.Text = T("Couldn't look it up (no internet?)"); return; }
        publicStatus.Text = "";
        publicFields.Add(T("Address"), me.Ip);
        publicFields.Add(T("Provider"), me.Isp);
        publicFields.Add(T("Network"), me.As);
        publicFields.Add(T("Location"), me.Place, T("Roughly: where your provider says the address is"));
        if (await Names.ReverseAsync(me.Ip) is { } name) publicFields.Add(T("Name"), name);
    }

    private void Refresh()
    {
        try
        {
            var primary = NetInfo.Primary();
            ShowMain(primary);
            ShowCounters(primary);
            ShowWifi(primary);
            adapters.ItemsSource = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(n => n.OperationalStatus == OperationalStatus.Up ? 0 : 1).ThenBy(n => n.Name)
                .Select(n => new AdapterRow(n.Name, NetInfo.TypeName(n.NetworkInterfaceType), Status(n.OperationalStatus),
                    n.OperationalStatus == OperationalStatus.Up ? Ui.LinkSpeed(n.Speed) : "",
                    string.Join(", ", n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString())),
                    NetInfo.Mac(n), n.Description)).ToList();
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"Connection: {ex.Message}"); }
    }

    private static string Status(OperationalStatus s) => s switch
    {
        OperationalStatus.Up => T("Up"),
        OperationalStatus.Down => T("Down"),
        OperationalStatus.NotPresent => T("Not present"),
        OperationalStatus.Dormant => T("Dormant"),
        _ => s.ToString(),
    };

    private void ShowMain(NetworkInterface? n)
    {
        main.Clear();
        if (n == null) { main.Add(T("Status"), T("Not connected")); return; }
        var ip = n.GetIPProperties();
        main.Add(T("Adapter"), n.Name, n.Description);
        main.Add(T("Type"), NetInfo.TypeName(n.NetworkInterfaceType));
        main.Add(T("Link speed"), Ui.LinkSpeed(n.Speed));
        main.Add(T("IPv4"), string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => $"{a.Address}/{a.PrefixLength}")));
        main.Add(T("IPv6"), string.Join("\n", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
            .Select(a => a.Address.ToString())));
        main.Add(T("Gateway"), string.Join(", ", ip.GatewayAddresses.Select(g => g.Address.ToString())));
        main.Add(T("DNS servers"), string.Join(", ", ip.DnsAddresses.Select(a => a.ToString())));
        try
        {
            var v4 = ip.GetIPv4Properties();
            main.Add(T("DHCP"), v4.IsDhcpEnabled ? string.Join(", ", ip.DhcpServerAddresses.Select(a => a.ToString())) is { Length: > 0 } s ? F("On ({0})", s) : T("On") : T("Off (set by hand)"));
            main.Add(T("MTU"), v4.Mtu.ToString());
        }
        catch { }
        main.Add(T("DNS suffix"), ip.DnsSuffix);
        main.Add(T("MAC"), NetInfo.Mac(n));
    }

    private void ShowCounters(NetworkInterface? n)
    {
        counters.Clear();
        if (n == null) return;
        var s = n.GetIPStatistics();
        long errors = s.IncomingPacketsWithErrors + s.OutgoingPacketsWithErrors;
        long discards = s.IncomingPacketsDiscarded + s.OutgoingPacketsDiscarded;
        if (baseline == null || adapterId != n.Id) { baseline = (errors, discards, DateTime.Now); adapterId = n.Id; }
        var danger = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
        counters.Add(T("Received"), F("{0:#,0} packets · {1}", s.UnicastPacketsReceived + s.NonUnicastPacketsReceived, Bytes(s.BytesReceived)));
        counters.Add(T("Sent"), F("{0:#,0} packets · {1}", s.UnicastPacketsSent + s.NonUnicastPacketsSent, Bytes(s.BytesSent)));
        counters.Add(T("Errors"), F("{0:#,0} in, {1:#,0} out", s.IncomingPacketsWithErrors, s.OutgoingPacketsWithErrors), T("Since the adapter started"),
            errors > 0 ? danger : null);
        counters.Add(T("Discarded"), F("{0:#,0} in, {1:#,0} out", s.IncomingPacketsDiscarded, s.OutgoingPacketsDiscarded), T("Since the adapter started"));
        long newErrors = errors - baseline.Value.Errors, newDiscards = discards - baseline.Value.Discards;
        counters.Add(T("While watching"), F("{0:#,0} errors, {1:#,0} discarded since {2:t}", newErrors, newDiscards, baseline.Value.At), null,
            newErrors > 0 ? danger : null);
    }

    private static string Bytes(long n) => n >= 1L << 30 ? $"{n / (double)(1L << 30):0.0} GB" : $"{n / (double)(1 << 20):0.0} MB";

    private void ShowWifi(NetworkInterface? n)
    {
        wifi.Clear();
        WlanLink? link = null;
        Guid adapter = default;
        if (n?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && Guid.TryParse(n.Id, out adapter)) link = Wlan.Current(adapter);
        wifiCard.Visibility = link == null ? Visibility.Collapsed : Visibility.Visible;
        if (link == null) return;
        var bss = Wlan.Networks(adapter).List.FirstOrDefault(b => b.Bssid == link.Bssid);
        wifi.Add(T("Network"), link.Ssid);
        wifi.Add(T("Access point"), link.Bssid);
        wifi.Add(T("Signal"), bss != null ? F("{0}% ({1} dBm)", link.Quality, bss.Rssi) : $"{link.Quality}%", null,
            bss != null && bss.Rssi < -75 ? (System.Windows.Media.Brush)FindResource("ErrorTextBrush") : null);
        if (bss != null) wifi.Add(T("Channel"), F("{0} ({1} GHz, {2} MHz wide)", bss.Channel, bss.Band, bss.WidthMHz));
        wifi.Add(T("Standard"), link.Phy);
        wifi.Add(T("Rates"), F("{0:0} Mbit/s receive, {1:0} Mbit/s send", link.RxMbps, link.TxMbps));
        wifi.Add(T("Security"), link.Cipher.Length > 0 ? $"{link.Auth} · {link.Cipher}" : link.Auth);
        if (link.Profile != link.Ssid) wifi.Add(T("Profile"), link.Profile);
    }

    public void Dispose() => timer.Stop();
}
