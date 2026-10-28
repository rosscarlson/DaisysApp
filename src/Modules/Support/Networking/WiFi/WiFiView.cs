using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Wi-Fi: the access points the picked Wi-Fi adapter hears, on a graph of one band's channels by
/// signal (as Android's Wi-Fi analysers draw them), each one's signal over the last few minutes, the least crowded
/// channels, and a table of them all. Scans every 10 seconds while it's showing (Auto), or with Scan.
/// </summary>
internal sealed class WiFiView : UserControl, IDisposable
{
    private sealed record BssRow(string Name, string Bssid, int Rssi, string Signal, int Quality, int Channel, string Band, string Width, string Phy, string Security, string Mine);

    private const int HistoryMinutes = 5;
    private readonly NetworkingSettings settings;
    private readonly ComboBox adapterBox = new() { Width = 260, VerticalAlignment = VerticalAlignment.Center };
    private readonly ChannelGraph channels = new() { Height = 300 };
    private readonly LineGraph history = new() { Height = 160, Detailed = true, TimeSpans = 3 };
    private readonly DataGrid table = Ui.Table(360);
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly TextBlock advice = Ui.Secondary(wrap: true);
    private readonly StackPanel bands = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button locationButton;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, List<(DateTime T, double V)>> seen = new();
    private readonly Dictionary<string, string> colors = new();
    private List<WlanBss> last = new();
    private string? highlight;
    private bool scanning;

    public WiFiView(NetworkingSettings settings)
    {
        this.settings = settings;
        adapterBox.ToolTip = T("The Wi-Fi adapter that scans");
        adapterBox.SelectionChanged += (_, _) =>
        {
            if (adapterBox.SelectedItem is ComboBoxItem { Tag: Guid id } && id.ToString() != settings.WifiAdapter)
            {
                settings.WifiAdapter = id.ToString();
                settings.Save();
                seen.Clear();
                _ = ScanAsync();
            }
        };
        var auto = new CheckBox { Content = T("Auto"), IsChecked = settings.WifiAuto, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Scan every 10 seconds while this shows") };
        Ui.OnToggle(auto, on => { settings.WifiAuto = on; settings.Save(); if (on && IsVisible) timer.Start(); else timer.Stop(); });
        var scan = Ui.IconButton("", T("Scan"), null, () => _ = ScanAsync(), accent: true);
        foreach (var (text, b) in new[] { ("2.4 GHz", "2.4"), ("5 GHz", "5"), ("6 GHz", "6") })
        {
            var chip = Ui.Chip(text, settings.WifiBand == b);
            Ui.OnToggle(chip, on => { if (on && settings.WifiBand != b) { settings.WifiBand = b; settings.Save(); Draw(); } else if (!on && settings.WifiBand == b) chip.IsChecked = true; });
            bands.Children.Add(chip);
        }
        var header = Ui.HeaderRow(Ui.Header(T("Wi-Fi Networks")), bands, adapterBox, auto, scan);
        locationButton = Ui.Button(T("Location Settings"), () => Ui.OpenUrl("ms-settings:privacy-location"));
        locationButton.Visibility = Visibility.Collapsed;
        locationButton.HorizontalAlignment = HorizontalAlignment.Left;
        locationButton.Margin = new Thickness(0, 6, 0, 0);
        advice.Margin = new Thickness(0, 8, 0, 0);
        var graphCard = Ui.Card(new StackPanel { Children = { header, status, locationButton, channels, advice } }, new Thickness(0));

        var historyCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Signal")), history } }, new Thickness(0, 12, 0, 0));

        table.Columns.Add(Ui.CopyColumn(T("Network"), nameof(BssRow.Name), -1.2));
        table.Columns.Add(Ui.Column("", nameof(BssRow.Mine), 26));
        table.Columns.Add(Ui.CopyColumn(T("Access point"), nameof(BssRow.Bssid), 150));
        table.Columns.Add(Ui.Column(T("Signal"), nameof(BssRow.Signal), 110, nameof(BssRow.Rssi), right: true));
        table.Columns.Add(Ui.Column(T("Channel"), nameof(BssRow.Channel), 70, right: true));
        table.Columns.Add(Ui.Column(T("Band"), nameof(BssRow.Band), 70));
        table.Columns.Add(Ui.Column(T("Width"), nameof(BssRow.Width), 80));
        table.Columns.Add(Ui.Column(T("Standard"), nameof(BssRow.Phy), 150));
        table.Columns.Add(Ui.Column(T("Security"), nameof(BssRow.Security), 120));
        table.SelectionChanged += (_, _) =>
        {
            highlight = (table.SelectedItem as BssRow)?.Bssid;
            Draw(table: false);
        };
        var tableCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Access Points")), table } }, new Thickness(0, 12, 0, 0));

        Content = Ui.Page(new StackPanel { Children = { graphCard, historyCard, tableCard } });
        history.Show(Array.Empty<GraphSeries>(), DateTime.Now.AddMinutes(-HistoryMinutes), DateTime.Now, 100, TimeSpan.FromSeconds(35));
        timer.Tick += (_, _) => _ = ScanAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                FillAdapters();
                _ = ScanAsync();
                if (settings.WifiAuto) timer.Start();
            }
            else timer.Stop();
        };
    }

    private Guid? Adapter => adapterBox.SelectedItem is ComboBoxItem { Tag: Guid id } ? id : null;

    private void FillAdapters()
    {
        var list = Wlan.Adapters();
        var keep = settings.WifiAdapter;
        adapterBox.Items.Clear();
        foreach (var a in list) adapterBox.Items.Add(new ComboBoxItem { Content = a.Name, Tag = a.Id });
        adapterBox.SelectedItem = adapterBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag.ToString() == keep) ?? (adapterBox.Items.Count > 0 ? adapterBox.Items[0] : null);
        settings.WifiAdapter = keep ?? Adapter?.ToString();
        adapterBox.IsEnabled = list.Count > 1;
        if (list.Count == 0) status.Text = T("No Wi-Fi adapter.");
    }

    private async Task ScanAsync()
    {
        if (scanning || Adapter is not Guid id) return;
        scanning = true;
        try
        {
            if (last.Count == 0) status.Text = T("Scanning…");
            Wlan.Scan(id);
            await Task.Delay(3500); // the adapter takes a few seconds to go through the channels
            var (list, error) = await Task.Run(() => Wlan.Networks(id));
            locationButton.Visibility = error == Wlan.AccessDenied ? Visibility.Visible : Visibility.Collapsed;
            if (error != 0)
            {
                status.Text = error == Wlan.AccessDenied
                    ? T("Windows needs location access for Wi-Fi scans: Settings → Privacy → Location → Let desktop apps access your location.")
                    : F("Windows couldn't scan ({0}).", new System.ComponentModel.Win32Exception(error).Message);
                return;
            }
            last = list;
            var now = DateTime.Now;
            foreach (var b in list)
            {
                if (!seen.TryGetValue(b.Bssid, out var points)) seen[b.Bssid] = points = new();
                points.Add((now, b.Quality));
                if (!colors.ContainsKey(b.Bssid)) colors[b.Bssid] = Ui.Colors[colors.Count % Ui.Colors.Length];
            }
            var cutoff = now.AddMinutes(-HistoryMinutes);
            foreach (var key in seen.Keys.ToList())
            {
                seen[key].RemoveAll(p => p.T < cutoff);
                if (seen[key].Count == 0) seen.Remove(key);
            }
            status.Text = P(list.Count, "{0} access point", "{0} access points") + " · " + F("scanned {0:T}", now);
            Draw();
        }
        finally { scanning = false; }
    }

    private void Draw(bool table = true)
    {
        foreach (var chip in bands.Children.OfType<ToggleButton>())
            chip.IsChecked = (string)chip.Content == settings.WifiBand + " GHz";
        string? mine = Adapter is Guid id ? Wlan.Current(id)?.Bssid : null;
        var inBand = last.Where(b => b.Band == settings.WifiBand).ToList();
        channels.Show(inBand.Select(b => (b, colors.GetValueOrDefault(b.Bssid, "AccentBrush"))).ToList(), settings.WifiBand, mine, highlight);
        advice.Text = Advice(inBand);

        var now = DateTime.Now;
        var strongest = inBand.OrderByDescending(b => b.Rssi).Take(10).Select(b => b.Bssid).ToHashSet();
        if (highlight != null) strongest.Add(highlight);
        var series = last.Where(b => strongest.Contains(b.Bssid) && seen.ContainsKey(b.Bssid))
            .Select(b => new GraphSeries { Name = b.Name, Metric = PercentUnit.Instance, Points = seen[b.Bssid], Color = colors.GetValueOrDefault(b.Bssid, "AccentBrush"), Fill = false, Bold = b.Bssid == mine })
            .ToList();
        history.Highlight = highlight != null ? last.FirstOrDefault(b => b.Bssid == highlight)?.Name : null;
        history.Show(series, now.AddMinutes(-HistoryMinutes), now, 100, TimeSpan.FromSeconds(35));

        if (!table) return;
        var selected = highlight;
        this.table.ItemsSource = last.OrderByDescending(b => b.Rssi).Select(b => new BssRow(b.Name, b.Bssid, b.Rssi, F("{0} dBm · {1}%", b.Rssi, b.Quality), b.Quality,
            b.Channel, b.Band + " GHz", b.WidthMHz + " MHz", b.Phy, b.Security, b.Bssid == mine ? "✓" : "")).ToList();
        if (selected != null) this.table.SelectedItem = this.table.Items.Cast<BssRow>().FirstOrDefault(r => r.Bssid == selected);
    }

    /// <summary>The least crowded channels of the band: each access point counts by its strength over the channels it covers.</summary>
    private static string Advice(List<WlanBss> inBand)
    {
        int[] candidates = inBand.FirstOrDefault()?.Band switch
        {
            "2.4" => [1, 6, 11],
            "5" => [36, 40, 44, 48, 149, 153, 157, 161],
            _ => [],
        };
        if (candidates.Length == 0 || inBand.Count == 0) return "";
        double Load(int ch) => inBand.Where(b => Math.Abs(b.CenterChannel - ch) < b.WidthMHz / 10.0 + 2)
            .Sum(b => Math.Pow(10, (b.Rssi + 100) / 20.0));
        var best = candidates.OrderBy(Load).Take(3).ToList();
        return F("Least crowded: {0}", string.Join(", ", best));
    }

    public void Dispose() => timer.Stop();
}
