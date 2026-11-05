using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Network Tools → Network Scan: every device on a subnet (<see cref="DeviceScanner"/>), grouped by what it is
/// (network, computers, phones and tablets, TV and media, smart home…), with a search box to find one. A device
/// clicked shows its details (<see cref="DeviceWindow"/>), whose Scan Host opens it in Host Scan.
/// </summary>
internal sealed class DeviceScanView : UserControl, IDisposable
{
    private readonly NetworkingSettings settings;
    private readonly Action<string>? fullScan;
    private readonly DeviceScanResults saved = DeviceScanResults.Load();
    private readonly Dictionary<string, FoundDevice> devices = new();
    private readonly ComboBox subnetBox = new() { IsEditable = true, Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox search = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button run;
    private readonly ProgressBar bar = Ui.Bar(4);
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly StackPanel results = new();
    private readonly DispatcherTimer redraw;
    private CancellationTokenSource? cts;
    private bool dirty;

    /// <param name="fullScan">Opens an address in Host Scan and scans it (null when Host Scan is switched off).</param>
    public DeviceScanView(NetworkingSettings settings, Action<string>? fullScan)
    {
        this.settings = settings;
        this.fullScan = fullScan;
        var local = DeviceScanner.LocalSubnets();
        foreach (var s in local) subnetBox.Items.Add(s);
        subnetBox.Text = settings.DeviceScanSubnet.Length > 0 ? settings.DeviceScanSubnet : local.FirstOrDefault() ?? "192.168.1.0/24";
        subnetBox.ToolTip = T("Subnet (192.168.1.0/24)");
        subnetBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = ScanAsync(); } };
        run = Ui.Button(T("Scan"), () => { if (cts != null) cts.Cancel(); else _ = ScanAsync(); }, accent: true);
        search.ToolTip = T("Find a device: name, address, MAC, maker, model");
        search.TextChanged += (_, _) => Render();
        var find = Ui.Icon("", 14);
        find.VerticalAlignment = VerticalAlignment.Center;
        find.Margin = new Thickness(0, 0, 6, 0);

        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var e in new FrameworkElement[] { subnetBox, run })
        {
            e.Margin = new Thickness(0, 0, 12, 0);
            controls.Children.Add(e);
        }
        find.Margin = new Thickness(12, 0, 6, 0);
        controls.Children.Add(find);
        controls.Children.Add(search);
        bar.Margin = new Thickness(0, 0, 0, 6);
        bar.Visibility = Visibility.Collapsed;
        bar.Maximum = 1;
        var top = Ui.Card(new StackPanel { Children = { Ui.Header(T("Network Scan")), controls, bar, status } }, new Thickness(0));
        var actions = new List<(string, Action<object>)> { (T("Details"), o => Details((FoundDevice)o)) };
        if (fullScan != null) actions.Add((T("Scan Host"), o => fullScan(((FoundDevice)o).Ip)));
        NetUi.ItemMenu(results, o => o is FoundDevice d ? new string?[] { d.Ip, d.Name } : Array.Empty<string?>(), actions.ToArray());
        Content = Ui.Page(new StackPanel { Children = { top, results } });

        redraw = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => { if (dirty) Render(); }, Dispatcher);
        redraw.Stop();
        foreach (var d in saved.Devices) devices[d.Ip] = d;
        status.Text = saved.Devices.Count > 0
            ? F("{0} devices on {1} · {2}", saved.Devices.Count, saved.Subnet, NetUi.When(saved.When))
            : T("Only scan networks you own or may test.");
        Render();
    }

    private async Task ScanAsync()
    {
        if (cts != null) return;
        string text = subnetBox.Text.Trim();
        if (!DeviceScanner.TryParse(text, out uint network, out int prefix)) { status.Text = T("Type a subnet, such as 192.168.1.0/24."); return; }
        if (prefix < DeviceScanner.MinPrefix) { status.Text = F("Too big: /{0} at most ({1} addresses).", DeviceScanner.MinPrefix, (1 << (32 - DeviceScanner.MinPrefix)) - 2); return; }
        string subnet = DeviceScanner.Text(network, prefix);
        subnetBox.Text = subnet;
        settings.DeviceScanSubnet = subnet;
        settings.Save();

        cts = new CancellationTokenSource();
        run.Content = T("Stop");
        bar.Visibility = Visibility.Visible;
        bar.Value = 0;
        devices.Clear();
        Render();
        redraw.Start();
        var started = DateTime.Now;
        try
        {
            var list = await DeviceScanner.ScanAsync(network, prefix, saved,
                d => Dispatcher.BeginInvoke(() => { if (cts != null) { devices[d.Ip] = d; dirty = true; } }),
                (t, f) => Dispatcher.BeginInvoke(() => { if (cts != null) { status.Text = t; bar.Value = f; } }), cts.Token);
            devices.Clear();
            foreach (var d in list) devices[d.Ip] = d;
            saved.Subnet = subnet;
            saved.When = started;
            saved.Devices = list;
            saved.Save();
            status.Text = F("{0} devices on {1} · {2}", list.Count, subnet, NetUi.Duration(DateTime.Now - started));
        }
        catch (OperationCanceledException) { status.Text = T("Stopped"); }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Device scan failed", ex);
            status.Text = ex.Message;
        }
        finally
        {
            cts?.Dispose();
            cts = null;
            redraw.Stop();
            run.Content = T("Scan");
            bar.Visibility = Visibility.Collapsed;
            Render();
        }
    }

    // ---------------------------------------------------------------- the groups

    private void Render()
    {
        dirty = false;
        results.Children.Clear();
        string q = search.Text.Trim();
        var shown = devices.Values.Where(d => q.Length == 0 || d.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => IPAddress.TryParse(d.Ip, out var a) ? BitConverter.ToUInt32(a.GetAddressBytes().Reverse().ToArray()) : 0).ToList();
        if (devices.Count > 0 && shown.Count == 0) results.Children.Add(Ui.Card(Ui.Secondary(F("Nothing matches \"{0}\".", q)), new Thickness(0, 12, 0, 0)));
        foreach (var (id, name, kinds) in DeviceScanner.Groups)
        {
            var mine = shown.Where(d => d.Group == id).ToList();
            if (mine.Count == 0) continue;
            var icon = Ui.Icon(HostScanner.DeviceKinds[kinds[0]].Icon, 18);
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, Ui.Header($"{name} ({mine.Count})", new Thickness(0)) } };
            header.Margin = new Thickness(0, 0, 0, 10);
            var tiles = new WrapPanel();
            foreach (var d in mine) tiles.Children.Add(Tile(d));
            results.Children.Add(Ui.Card(new StackPanel { Children = { header, tiles } }, new Thickness(0, 12, 0, 0)));
        }
    }

    private FrameworkElement Tile(FoundDevice d)
    {
        var icon = Ui.Icon(HostScanner.DeviceKinds[d.Kind].Icon, 26);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        icon.Margin = new Thickness(0, 2, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var title = new TextBlock { Text = d.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { Children = { title } };
        string role = d.IsThisPc ? T("this PC") : d.IsGateway ? T("router") : d.Asleep ? T("not answering") : "";
        text.Children.Add(Ui.Secondary(d.Name.Length > 0 ? d.Ip + (role.Length > 0 ? " · " + role : "") : role));
        string about = string.Join(" · ", new[] { d.Model, d.Vendor ?? (d.RandomMac ? T("random MAC") : "") }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
        if (about.Length > 0) text.Children.Add(new TextBlock { Text = about, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var t in text.Children.OfType<TextBlock>()) t.TextTrimming = TextTrimming.CharacterEllipsis;
        var dock = new DockPanel();
        dock.Children.Add(icon);
        dock.Children.Add(text);
        var border = new Border { Width = 230, Height = 76, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = dock };
        border.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var tip = new List<string> { $"{HostScanner.DeviceKinds[d.Kind].Name}{(d.Confidence > 0 ? " · " + F("{0}% sure", d.Confidence) : "")}" };
        if (d.Asleep) tip.Add(T("Not answering: asleep or away (named by the network's DNS)"));
        if (d.Names.Count > 0) tip.Add(F("Also called: {0}", string.Join(", ", d.Names)));
        if (d.Os.Length > 0) tip.Add(F("System: {0}", d.Os));
        if (d.Mac != null) tip.Add(F("MAC: {0}", d.Mac) + (d.Vendor != null ? $" ({d.Vendor})" : ""));
        if (d.PingMs is { } ms) tip.Add(F("Ping: {0}", ms < 1 ? "<1 ms" : NetUi.Ms(ms)));
        if (d.Ports.Count > 0) tip.Add(F("Open: {0}", string.Join(", ", d.Ports)));
        tip.AddRange(d.Why.Take(6).Select(w => "· " + w));
        tip.Add(T("Click for details"));
        // a button that looks like the tile, so the keyboard and UI Automation open it too
        var tile = new Button
        {
            Content = border, Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, DataContext = d,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
            Opacity = d.Asleep ? 0.55 : 1,
            ToolTip = string.Join("\n", tip),
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"{d.Title} {d.Ip}");
        tile.Click += (_, _) => Details(d);
        return tile;
    }

    private void Details(FoundDevice d) => DeviceWindow.Open(d, fullScan, settings.HostLogins, this);

    public void Dispose()
    {
        cts?.Cancel();
        redraw.Stop();
    }
}
