using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Wi-Fi, three cards side by side: the access points the picked Wi-Fi adapter hears (a table, and
/// the picked one's details), them on a graph of one band's channels by signal (as Android's Wi-Fi analysers draw them)
/// with the least crowded channels, and each one's signal over the last few minutes. The one picked in the table is
/// highlighted on both graphs. Scans every 10 seconds while it's showing (Auto), or with Scan.
/// </summary>
internal sealed class WiFiView : UserControl, IDisposable
{
    private sealed record BssRow(string Name, string Bssid, int Rssi, string Signal, int Quality, int Channel, string Band, string Width, string Phy, string Security, string Mine);

    private const int HistoryMinutes = 5;
    private readonly NetworkingSettings settings;
    private readonly ComboBox adapterBox = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ChannelGraph channels = new();
    private readonly LineGraph history = new() { Detailed = true, TimeSpans = 3 };
    private readonly DataGrid table = Ui.Table();
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly TextBlock advice = Ui.Secondary(wrap: true);
    private readonly Grid details = new() { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
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
        var scan = NetUi.IconButton("", T("Scan"), null, () => _ = ScanAsync(), accent: true);
        foreach (var (text, b) in new[] { ("2.4 GHz", "2.4"), ("5 GHz", "5"), ("6 GHz", "6") })
        {
            var chip = Ui.Chip(text, settings.WifiBand == b);
            Ui.OnToggle(chip, on => { if (on && settings.WifiBand != b) { settings.WifiBand = b; settings.Save(); Draw(); } else if (!on && settings.WifiBand == b) chip.IsChecked = true; });
            bands.Children.Add(chip);
        }
        locationButton = Ui.Button(T("Location Settings"), () => Ui.Open("ms-settings:privacy-location"));
        locationButton.Visibility = Visibility.Collapsed;
        locationButton.HorizontalAlignment = HorizontalAlignment.Left;
        locationButton.Margin = new Thickness(0, 6, 0, 0);
        advice.Margin = new Thickness(0, 8, 0, 0);

        table.Columns.Add(NetUi.CopyColumn(T("Network"), nameof(BssRow.Name), -1));
        table.Columns.Add(Ui.Column("", nameof(BssRow.Mine), 24));
        table.Columns.Add(Ui.Column(T("Signal"), nameof(BssRow.Signal), 72, nameof(BssRow.Rssi), right: true));
        table.Columns.Add(Ui.Column(T("Channel"), nameof(BssRow.Channel), 64, right: true));
        table.Columns.Add(Ui.Column(T("Band"), nameof(BssRow.Band), 64));
        table.SelectionChanged += (_, _) =>
        {
            var row = table.SelectedItem as BssRow;
            highlight = row?.Bssid;
            ShowDetails(row);
            // the graph shows the picked one's band
            if (row != null && row.Band != settings.WifiBand + " GHz") { settings.WifiBand = row.Band.Replace(" GHz", ""); settings.Save(); }
            Draw(table: false);
        };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition());

        var apTop = new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Access Points")), auto, scan), adapterBox, status, locationButton } };
        var apCard = Ui.Card(Fill(apTop, table, details), new Thickness(0, 0, 12, 0));
        var graphCard = Ui.Card(Fill(Ui.HeaderRow(Ui.Header(T("Networks")), bands), channels, advice), new Thickness(0, 0, 12, 0));
        var historyCard = Ui.Card(Fill(Ui.HeaderRow(Ui.Header(T("Signal"))), history, null), new Thickness(0));

        var columns = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var card in new[] { apCard, graphCard, historyCard })
        {
            Grid.SetColumn(card, columns.ColumnDefinitions.Count);
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
            columns.Children.Add(card);
        }
        Content = columns;
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

    /// <summary>A card's content: <paramref name="top"/>, <paramref name="bottom"/>, and <paramref name="middle"/> filling the rest.</summary>
    private static DockPanel Fill(UIElement top, UIElement middle, UIElement? bottom)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        if (bottom != null) { DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom); }
        dock.Children.Add(middle);
        return dock;
    }

    /// <summary>The picked access point's details, under the table.</summary>
    private void ShowDetails(BssRow? row)
    {
        details.Children.Clear();
        details.RowDefinitions.Clear();
        details.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null) return;
        foreach (var (name, value) in new[]
        {
            (T("Network"), row.Name), (T("Access point"), row.Bssid), (T("Signal"), F("{0} dBm · {1}%", row.Rssi, row.Quality)),
            (T("Channel"), F("{0} · {1} · {2}", row.Channel, row.Band, row.Width)), (T("Standard"), row.Phy), (T("Security"), row.Security),
        })
        {
            int r = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Ui.Secondary(name);
            label.Margin = new Thickness(0, 1, 12, 1);
            var text = NetUi.Selectable(value);
            Grid.SetRow(label, r);
            Grid.SetRow(text, r);
            Grid.SetColumn(text, 1);
            details.Children.Add(label);
            details.Children.Add(text);
        }
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
                if (!colors.ContainsKey(b.Bssid)) colors[b.Bssid] = NetUi.Colors[colors.Count % NetUi.Colors.Length];
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
        this.table.ItemsSource = last.OrderByDescending(b => b.Rssi).Select(b => new BssRow(b.Name, b.Bssid, b.Rssi, F("{0} dBm", b.Rssi), b.Quality,
            b.Channel, b.Band + " GHz", b.WidthMHz + " MHz", b.Phy, b.Security, b.Bssid == mine ? "✓" : "")).ToList();
        if (selected != null) this.table.SelectedItem = this.table.Items.Cast<BssRow>().FirstOrDefault(r => r.Bssid == selected);
        ShowDetails(this.table.SelectedItem as BssRow);
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
