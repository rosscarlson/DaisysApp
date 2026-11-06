using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Network Tools → Network Scan: every device on a subnet (<see cref="DeviceScanner"/>), grouped by what it is
/// (network, computers, phones and tablets, TV and media, smart home…), with a search box to find one. The groups pack
/// into one grid as HA's do (<see cref="GroupGrid"/>: dragged by their titles into the order wanted, the boxes
/// stretching to fill each row). A device clicked shows its details (<see cref="DeviceWindow"/>), whose Scan Host opens it in Host Scan.
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
    private readonly Grid layout = new() { Margin = new Thickness(-6, 0, -6, 0) };
    private readonly TextBlock empty = Ui.Secondary("", wrap: true);
    private readonly ScrollViewer scroller;
    private readonly GroupGrid grid;
    private readonly Dictionary<string, GroupSection> sections = new();
    private List<GroupSection> ordered = new();
    private const string DragFormat = "DaisysApp.NetworkScanGroup";
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
        var top = Ui.Card(new StackPanel { Children = { Ui.Header(T("Network Scan")), controls, bar, status } }, new Thickness(0, 0, 6, 12));
        var actions = new List<(string, Action<object>)> { (T("Details"), o => Details((FoundDevice)o)) };
        if (fullScan != null) actions.Add((T("Scan Host"), o => fullScan(((FoundDevice)o).Ip)));
        NetUi.ItemMenu(layout, o => o is FoundDevice d ? new string?[] { d.Ip, d.Name } : Array.Empty<string?>(), actions.ToArray());
        empty.Margin = new Thickness(0, 0, 0, 12);
        empty.Visibility = Visibility.Collapsed;
        scroller = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { empty, layout } },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        grid = new GroupGrid(layout, scroller, DragFormat, () => settings.DeviceScanColumns, () => settings.DeviceScanRows, () => ordered.Select(g => g.Key), keys =>
        {
            settings.DeviceScanOrder = keys;
            settings.Save();
            Dispatcher.BeginInvoke(Render); // after the drag has finished with the piece it started from
        });
        scroller.SizeChanged += (_, e) =>
        {
            if (grid.ColumnsChanged) Render();
            else if (e.HeightChanged) grid.FitRows();
        };
        // the grid's size may have changed in Settings
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Render(); };
        DockPanel.SetDock(top, Dock.Top);
        Content = new DockPanel { Children = { top, scroller } };

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
        string q = search.Text.Trim();
        var shown = devices.Values.Where(d => q.Length == 0 || d.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => IPAddress.TryParse(d.Ip, out var a) ? BitConverter.ToUInt32(a.GetAddressBytes().Reverse().ToArray()) : 0).ToList();
        empty.Text = devices.Count > 0 && shown.Count == 0 ? F("Nothing matches \"{0}\".", q) : "";
        empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the groups in the order dragged, the rest in their own order
        int Place(string id) { int i = settings.DeviceScanOrder.IndexOf(id); return i < 0 ? int.MaxValue : i; }
        ordered = DeviceScanner.Groups.Select((g, i) => (g, i)).OrderBy(x => Place(x.g.Id)).ThenBy(x => x.i).Select(x =>
        {
            if (!sections.TryGetValue(x.g.Id, out var sec)) sections[x.g.Id] = sec = new GroupSection(x.g.Id);
            sec.Title = x.g.Name;
            return sec;
        }).ToList();
        var placed = new List<(GroupSection Sec, List<UIElement> Boxes)>();
        foreach (var sec in ordered)
        {
            var mine = shown.Where(d => d.Group == sec.Key).ToList();
            if (mine.Count == 0) continue;
            int awake = mine.Count(d => !d.Asleep), asleep = mine.Count - awake;
            var counts = new List<((string, string), int)>();
            if (awake > 0) counts.Add((("SuccessBrush", T("answering")), awake));
            if (asleep > 0) counts.Add((("TextSecondaryBrush", T("not answering")), asleep));
            sec.Counts = counts;
            placed.Add((sec, mine.Select(Tile).ToList<UIElement>()));
        }
        grid.Lay(sections.Values, placed);
    }

    private UIElement Tile(FoundDevice d)
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
        var border = new Border { Child = dock };
        border.SetResourceReference(StyleProperty, "Card");
        border.Margin = new Thickness(0);
        border.Padding = new Thickness(12, 10, 12, 10);

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
            Content = border, Margin = new Thickness(6, 0, 6, 12), Cursor = Cursors.Hand, DataContext = d,
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
