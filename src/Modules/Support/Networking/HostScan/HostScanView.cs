using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Host Scan: one device looked at every way there is (<see cref="HostScanner"/>), and shown as what
/// it is (kind, system, model, how sure), its findings worst first, its open ports as tiles (a click looks closer), what
/// it says about itself (names, mDNS, UPnP, SNMP, NetBIOS), nmap's answers when nmap is installed, its public
/// details when it's on the internet, and the scan's log.
/// </summary>
internal sealed class HostScanView : UserControl, IDisposable
{
    private readonly NetworkingSettings settings;
    private readonly ComboBox hostBox = new() { IsEditable = true, Width = 240, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button run, copy;
    private readonly ProgressBar bar = Ui.Bar(4);
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly StackPanel results = new();
    private readonly string? nmap = Nmap.Find();
    private CancellationTokenSource? cts;
    private HostReport? last;

    public HostScanView(NetworkingSettings settings)
    {
        this.settings = settings;
        hostBox.Text = settings.HostScanHost.Length > 0 ? settings.HostScanHost : NetInfo.Gateway() ?? "";
        hostBox.ToolTip = T("Host name or IP address");
        hostBox.DropDownOpened += (_, _) => FillHosts();
        hostBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = ScanAsync(); } };
        FillHosts();
        var ports = Ui.Choice(new[] { (T("Common"), "Common"), (T("1–1024"), "WellKnown"), (T("All"), "All") }, settings.HostPorts, v => { settings.HostPorts = v; settings.Save(); }, 100);
        ports.ToolTip = F("Common: {0} ports devices use", HostScanner.PortsFor("Common").Count);
        var logins = new CheckBox { Content = T("Try without a password"), IsChecked = settings.HostLogins, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Anonymous FTP, SNMP's default communities, Redis, MQTT") };
        Ui.OnToggle(logins, on => { settings.HostLogins = on; settings.Save(); });
        var useNmap = new CheckBox { Content = "nmap", IsChecked = settings.HostNmap && nmap != null, IsEnabled = nmap != null, VerticalAlignment = VerticalAlignment.Center, ToolTip = nmap ?? T("Not installed (nmap.org)") };
        Ui.OnToggle(useNmap, on => { settings.HostNmap = on; settings.Save(); });
        run = Ui.Button(T("Scan"), () => { if (cts != null) cts.Cancel(); else _ = ScanAsync(); }, accent: true);
        copy = Ui.IconButton("", T("Copy Report"), T("The whole report as text"), () => { if (last != null) Ui.Copy(Report(last)); });
        copy.IsEnabled = false;

        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var e in new FrameworkElement[] { hostBox, ports, logins, useNmap, run })
        {
            e.Margin = new Thickness(0, 0, 12, 0);
            controls.Children.Add(e);
        }
        if (nmap == null)
        {
            var get = Ui.Link(T("Get nmap"), "https://nmap.org/download.html");
            get.VerticalAlignment = VerticalAlignment.Center;
            get.Margin = new Thickness(0);
            controls.Children.Add(get);
        }
        bar.Margin = new Thickness(0, 0, 0, 6);
        bar.Visibility = Visibility.Collapsed;
        bar.Maximum = 1;
        status.Text = T("Only scan devices you own or may test.");
        var top = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Host Scan")), copy), controls, bar, status } }, new Thickness(0));
        Content = Ui.Page(new StackPanel { Children = { top, results } });
    }

    private void FillHosts()
    {
        string text = hostBox.Text;
        hostBox.Items.Clear();
        var seen = new HashSet<string>();
        void Add(string h) { if (seen.Add(h)) hostBox.Items.Add(h); }
        foreach (var h in settings.HostScanRecent) Add(h);
        if (NetInfo.Gateway() is { } gw) Add(gw);
        foreach (var (ip, _) in Discovery.Neighbours()) Add(ip);
        hostBox.Text = text;
    }

    private async Task ScanAsync()
    {
        if (cts != null) return;
        string host = hostBox.Text.Trim();
        if (host.Length == 0) return;
        settings.HostScanHost = host;
        settings.HostScanRecent.Remove(host);
        settings.HostScanRecent.Insert(0, host);
        if (settings.HostScanRecent.Count > 12) settings.HostScanRecent.RemoveRange(12, settings.HostScanRecent.Count - 12);
        settings.Save();

        cts = new CancellationTokenSource();
        run.Content = T("Stop");
        bar.Visibility = Visibility.Visible;
        bar.Value = 0;
        results.Children.Clear();
        try
        {
            status.Text = F("Looking up {0}…", host);
            if (await NetInfo.ResolveAsync(host, cts.Token) is not { } ip) { status.Text = F("Couldn't find {0}.", host); return; }
            var report = await HostScanner.ScanAsync(host, ip, settings, (text, f) => Dispatcher.BeginInvoke(() => { status.Text = text; bar.Value = f; }), cts.Token);
            last = report;
            copy.IsEnabled = true;
            status.Text = F("{0} ({1})", host, ip) + " · " + F("{0} open of {1} ports · {2} findings · {3}", report.Open.Count, report.Scanned, report.Findings.Count(f => f.Level > Severity.Info), Ui.Duration(report.Took));
            Show(report);
        }
        catch (OperationCanceledException) { status.Text = T("Stopped"); }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Host scan failed", ex);
            status.Text = ex.Message;
        }
        finally
        {
            cts?.Dispose();
            cts = null;
            run.Content = T("Scan");
            bar.Visibility = Visibility.Collapsed;
        }
    }

    // ---------------------------------------------------------------- the report

    private void Show(HostReport r)
    {
        results.Children.Clear();
        results.Children.Add(Columns(DeviceCard(r), SummaryCard(r), 1.6));
        results.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Findings")), Cards.Findings(r.Findings) } }, new Thickness(0, 12, 0, 0)));
        results.Children.Add(PortsCard(r));
        results.Children.Add(Columns(IdentityCard(r), AnnouncesCard(r), 1));
        if (r.Nmap is { } n) results.Children.Add(NmapCard(n));
        if (!r.IsPrivate) results.Children.Add(new IpDetailsView(r.Ip, r.DnsNames.FirstOrDefault()));
        var log = new Expander { Header = T("Scan log"), Content = Cards.Raw(string.Join("\n", r.Log)), Margin = new Thickness(0, 4, 0, 0) };
        results.Children.Add(Ui.Card(log, new Thickness(0, 12, 0, 0)));
    }

    private static Grid Columns(FrameworkElement left, FrameworkElement right, double leftWeight)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(left);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    private Border DeviceCard(HostReport r)
    {
        var (name, icon) = HostScanner.DeviceKinds[r.Kind];
        var glyph = Ui.Icon(icon, 52);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        glyph.Margin = new Thickness(4, 4, 20, 0);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        var title = new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var sure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        if (r.Kind != "unknown")
        {
            var meter = Ui.Bar(6);
            meter.Width = 140;
            meter.Maximum = 100;
            meter.Value = r.Confidence;
            meter.VerticalAlignment = VerticalAlignment.Center;
            sure.Children.Add(meter);
            var pct = Ui.Secondary(F("{0}% sure", r.Confidence));
            pct.Margin = new Thickness(8, 0, 0, 0);
            sure.Children.Add(pct);
        }
        var fields = new Ui.Fields();
        fields.Add(T("Name"), r.Name);
        fields.Add(T("Model"), r.Model);
        fields.Add(T("System"), r.Os, r.OsWhy.Length > 0 ? F("From {0}", r.OsWhy) : null);
        fields.Add(T("Address"), r.Host == r.Ip ? r.Ip : $"{r.Ip} ({r.Host})");
        fields.Add(T("Maker"), r.Vendor);
        var why = Ui.Secondary(string.Join("\n", r.KindWhy.Take(8).Select(w => "· " + w)), wrap: true);
        why.Margin = new Thickness(0, 6, 0, 0);
        if (r.KindWhy.Count == 0) why.Text = T("Nothing it said gives it away.");
        var text = new StackPanel { Children = { title, sure, fields, Ui.Header(T("Why")), why } };
        var dock = new DockPanel();
        dock.Children.Add(glyph);
        dock.Children.Add(text);
        var card = Ui.Card(dock, new Thickness(0));
        return card;
    }

    private Border SummaryCard(HostReport r)
    {
        var tiles = new UniformGrid2(2);
        tiles.Add(Tile(r.Open.Count.ToString(), T("open ports"), null));
        tiles.Add(Tile(r.Ping is { } p ? PingText(p.Ms) : "—", T("ping"), null));
        foreach (var level in new[] { Severity.High, Severity.Medium, Severity.Low, Severity.Info })
        {
            int n = r.Findings.Count(f => f.Level == level);
            var sample = new Finding(level, "", "");
            tiles.Add(Tile(n.ToString(), sample.LevelText, n > 0 && level != Severity.Info ? Ui.BrushOf(this, sample.Color) : null));
        }
        var fields = new Ui.Fields { Margin = new Thickness(0, 10, 0, 0) };
        fields.Add(T("Scanned"), F("{0} ports in {1}", r.Scanned, Ui.Duration(r.Took)));
        fields.Add(T("Closed"), F("{0} closed, {1} no answer", r.Closed, r.Filtered), T("No answer: a firewall drops them"));
        fields.Add(T("When"), Ui.When(r.When));
        return Ui.Card(new StackPanel { Children = { Ui.Header(T("Summary")), tiles, fields } }, new Thickness(0));
    }

    private static FrameworkElement Tile(string value, string label, Brush? color)
    {
        var v = new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold };
        if (color != null) v.Foreground = color;
        return new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children = { v, Ui.Secondary(label) } };
    }

    /// <summary>A grid of equal columns, filled row by row.</summary>
    private sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid2(int columns) => Columns = columns;
        public void Add(UIElement e) => Children.Add(e);
    }

    private Border PortsCard(HostReport r)
    {
        var tiles = new WrapPanel();
        var ip = IPAddress.Parse(r.Ip);
        foreach (var (port, info) in r.Open)
        {
            var known = Ports.Of(port.Port);
            var worst = info.Findings.Where(f => f.Level > Severity.Info).OrderByDescending(f => f.Level).FirstOrDefault();
            var number = new TextBlock { Text = port.Port.ToString(), FontSize = 20, FontWeight = FontWeights.SemiBold };
            string speaks = info.Protocol.Length > 0 ? info.Protocol : known?.Service ?? "";
            var panel = new StackPanel { Children = { number, new TextBlock { Text = speaks, TextTrimming = TextTrimming.CharacterEllipsis } } };
            if (info.ProductText.Length > 0 && info.ProductText != speaks) panel.Children.Add(Ui.Secondary(info.ProductText));
            if (worst != null)
            {
                var w = new TextBlock { Text = worst.Title.Split(" · ")[0], FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.BrushOf(this, worst.Color), Margin = new Thickness(0, 2, 0, 0) };
                panel.Children.Add(w);
            }
            var border = new Border
            {
                Width = 170, Height = 96, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(worst != null ? 2 : 1), Child = panel,
            };
            border.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
            if (worst != null) border.BorderBrush = Ui.BrushOf(this, worst.Color);
            else border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            // a button that looks like the tile, so the keyboard and UI Automation open it too
            var tile = new Button
            {
                Content = border, Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
                ToolTip = string.Join("\n", new[] { known != null ? $"{known.Service}: {known.About}" : "", port.Banner }.Where(s => s.Length > 0).DefaultIfEmpty(T("Click to look closer"))),
            };
            System.Windows.Automation.AutomationProperties.SetName(tile, F("Port {0}", port.Port));
            tile.Click += (_, _) => PortWindow.Open(ip, r.Host, port, info, settings.HostLogins, this);
            tiles.Children.Add(tile);
        }
        if (r.Open.Count == 0) tiles.Children.Add(Ui.Secondary(T("No open ports")));
        return Ui.Card(new StackPanel { Children = { Ui.Header(F("Open Ports ({0})", r.Open.Count)), tiles } }, new Thickness(0, 12, 0, 0));
    }

    private static Border IdentityCard(HostReport r)
    {
        var f = new Ui.Fields();
        f.Add(T("DNS name"), string.Join(", ", r.DnsNames));
        f.Add(T("mDNS name"), r.Mdns?.HostName);
        f.Add("NetBIOS", string.Join("\n", r.NetBios.Select(n => $"{n.Name} ({n.Kind})")));
        f.Add(T("MAC"), r.Mac is { } mac ? r.RandomMac ? F("{0} (random or virtual)", mac) : mac : null, r.RandomMac ? T("Chosen at random by the device, so it names no maker") : null);
        f.Add(T("Maker"), r.Vendor);
        if (r.Ping is { } p)
        {
            var (start, system) = Discovery.FromTtl(p.Ttl);
            f.Add(T("Ping"), F("{0} · TTL {1}", PingText(p.Ms), p.Ttl));
            if (start > 0) f.Add(T("TTL says"), F("{0} ({1} hops away)", system, start - p.Ttl));
        }
        else f.Add(T("Ping"), T("no answer (blocked?)"));
        if (r.IsGateway) f.Add(T("Role"), T("your router (the gateway)"));
        if (r.IsThisPc) f.Add(T("Role"), T("this PC"));
        return Ui.Card(new StackPanel { Children = { Ui.Header(T("Identity")), f } }, new Thickness(0));
    }

    private static Border AnnouncesCard(HostReport r)
    {
        var panel = new StackPanel { Children = { Ui.Header(T("What It Announces")) } };
        if (r.Mdns is { Services.Count: > 0 } m)
        {
            panel.Children.Add(Sub(T("mDNS services")));
            var f = new Ui.Fields();
            foreach (var (type, instance, port, texts) in m.Services)
            {
                string label = type.Replace(".local", "");
                string value = instance.Replace("." + type, "") + (port > 0 ? $" · {port}" : "") + (texts.Count > 0 ? "\n" + string.Join("  ", texts.Take(12)) : "");
                f.Add(label, value);
            }
            panel.Children.Add(f);
        }
        foreach (var d in r.Upnp)
        {
            panel.Children.Add(Sub(d.DeviceTypes.Count > 0 ? $"UPnP · {string.Join(", ", d.DeviceTypes.Distinct())}" : "UPnP"));
            var f = new Ui.Fields();
            IpDetailsView.Fill(f, d.Facts);
            f.Add(T("Server"), d.Server);
            f.Add(T("Services"), string.Join(", ", d.Services.Distinct()));
            var loc = f.Add(T("Description"), d.Location);
            if (loc != null) IpDetailsView.Linked(loc, d.Location);
            panel.Children.Add(f);
        }
        if (r.Snmp is { } s)
        {
            panel.Children.Add(Sub(F("SNMP · community \"{0}\"", s.Community)));
            var f = new Ui.Fields();
            IpDetailsView.Fill(f, s.Facts);
            panel.Children.Add(f);
        }
        if (panel.Children.Count == 1) panel.Children.Add(Ui.Secondary(T("Nothing: no mDNS, UPnP or SNMP answers")));
        return Ui.Card(panel, new Thickness(0));
    }

    /// <summary>Windows times pings in whole milliseconds: under one is "under 1 ms", not 0.</summary>
    private static string PingText(double ms) => ms < 1 ? "<1 ms" : Ui.Ms(ms);

    private static TextBlock Sub(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };

    private static Border NmapCard(Nmap.Result n)
    {
        var panel = new StackPanel { Children = { Ui.Header("nmap") } };
        if (n.Error != null) panel.Children.Add(Ui.Secondary(n.Error.Split('\n')[0], wrap: true));
        foreach (var (name, accuracy) in n.Os.Take(4))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var meter = Ui.Bar(6);
            meter.Width = 100;
            meter.Maximum = 100;
            meter.Value = accuracy;
            meter.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(meter);
            row.Children.Add(Ui.Selectable($"{name} ({accuracy}%)", false));
            panel.Children.Add(row);
        }
        if (n.Ports.Count > 0)
        {
            var f = new Ui.Fields { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var p in n.Ports) f.Add($"{p.Port}/{p.Protocol}", string.Join(" ", new[] { p.Service, p.Product, p.Version, p.Extra.Length > 0 ? $"({p.Extra})" : "" }.Where(s => s.Length > 0)));
            panel.Children.Add(f);
        }
        if (n.Scripts.Count > 0)
        {
            panel.Children.Add(Sub(T("Scripts")));
            panel.Children.Add(Cards.Raw(string.Join("\n\n", n.Scripts.Select(s => $"{(s.Port > 0 ? s.Port + " " : "")}{s.Script}:\n{s.Output}"))));
        }
        var cmd = Ui.Secondary(n.Command, wrap: true);
        cmd.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(cmd);
        return Ui.Card(panel, new Thickness(0, 12, 0, 0));
    }

    /// <summary>The report as text, for pasting somewhere.</summary>
    private static string Report(HostReport r)
    {
        var b = new StringBuilder();
        b.AppendLine($"Host Scan: {r.Host} ({r.Ip}) · {r.When:g}");
        b.AppendLine($"{HostScanner.DeviceKinds[r.Kind].Name} ({r.Confidence}%) · {r.Os} · {r.Model} · {r.Name}".Replace(" ·  ·", " ·"));
        if (r.Mac != null) b.AppendLine($"MAC {r.Mac}{(r.Vendor != null ? $" ({r.Vendor})" : "")}");
        foreach (var w in r.KindWhy) b.AppendLine("  · " + w);
        b.AppendLine().AppendLine("Findings:");
        foreach (var f in r.Findings.OrderByDescending(f => f.Level)) b.AppendLine($"  [{f.LevelText}] {f.Title}: {f.Detail}{(f.Url != null ? " " + f.Url : "")}");
        b.AppendLine().AppendLine("Open ports:");
        foreach (var (port, info) in r.Open)
        {
            b.AppendLine($"  {port.Port}/tcp {info.Protocol} {info.ProductText} {port.Banner}".TrimEnd());
            foreach (var fact in info.Facts) b.AppendLine($"      {fact.Label}: {fact.Value.Replace("\n", " ")}");
        }
        if (r.Mdns is { Services.Count: > 0 } m)
        {
            b.AppendLine().AppendLine("mDNS:");
            foreach (var s in m.Services) b.AppendLine($"  {s.Type} {s.Instance} {s.Port} {string.Join(" ", s.Texts)}");
        }
        foreach (var d in r.Upnp) b.AppendLine().AppendLine("UPnP: " + string.Join(" · ", d.Facts.Select(f => $"{f.Label}: {f.Value}")));
        if (r.Snmp is { } s2) b.AppendLine().AppendLine($"SNMP ({s2.Community}): " + string.Join(" · ", s2.Facts.Select(f => $"{f.Label}: {f.Value}")));
        if (r.NetBios.Count > 0) b.AppendLine().AppendLine("NetBIOS: " + string.Join(", ", r.NetBios.Select(n => $"{n.Name} ({n.Kind})")));
        if (r.Nmap is { } n) b.AppendLine().AppendLine("nmap: " + string.Join("; ", n.Os.Take(3).Select(o => $"{o.Name} {o.Accuracy}%")));
        b.AppendLine().AppendLine("Log:");
        foreach (var l in r.Log) b.AppendLine("  " + l);
        return b.ToString();
    }

    public void Dispose() => cts?.Cancel();
}
