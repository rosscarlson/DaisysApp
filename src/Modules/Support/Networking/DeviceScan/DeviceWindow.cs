using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Network Scan → a device clicked: what the scan found about it (kind, names, maker, ports, why), its open ports
/// looked at closer on a click (<see cref="PortWindow"/>), and Scan Host to open it in Host Scan. Beside it, a column for
/// each module that knows the device (Zabbix, Home Assistant: Core's <see cref="DeviceLookups"/>), filled as they answer.
/// </summary>
internal static class DeviceWindow
{
    /// <param name="scanHost">Opens the address in Host Scan and scans it (null when Host Scan is switched off).</param>
    public static void Open(FoundDevice d, Action<string>? scanHost, bool logins, FrameworkElement owner)
    {
        var lookups = DeviceLookups.Usable;
        var w = Ui.Dialog(d.Name.Length > 0 ? $"{d.Name} · {d.Ip}" : d.Ip, owner, 560);
        var kind = HostScanner.DeviceKinds[d.Kind];

        var icon = Ui.Icon(kind.Icon, 26);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { icon, Ui.Header(d.Title, new Thickness(0)) } };

        var fields = new NetUi.Fields();
        fields.Add(T("Kind"), kind.Name + (d.Confidence > 0 ? " · " + F("{0}% sure", d.Confidence) : ""));
        string role = d.IsThisPc ? T("this PC") : d.IsGateway ? T("router") : "";
        fields.Add(T("Address"), d.Ip + (role.Length > 0 ? " · " + role : ""));
        fields.Add(T("Name"), d.Name);
        fields.Add(T("Also called"), string.Join(", ", d.Names));
        fields.Add(T("Model"), d.Model);
        fields.Add(T("System"), d.Os);
        fields.Add(T("MAC"), d.Mac != null ? d.Mac + (d.Vendor != null ? $" ({d.Vendor})" : d.RandomMac ? $" ({T("random MAC")})" : "") : null);
        fields.Add(T("Ping"), d.PingMs is { } ms ? (ms < 1 ? "<1 ms" : NetUi.Ms(ms)) : null);
        if (d.Asleep) fields.Add(T("State"), T("Not answering: asleep or away (named by the network's DNS)"));
        fields.Add(T("Services"), string.Join(", ", d.Services));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (scanHost != null)
            buttons.Children.Add(Ui.Button(T("Scan Host"), () => { w.Close(); scanHost(d.Ip); }, accent: true));
        string? web = d.Ports.Contains(443) ? $"https://{d.Ip}" : d.Ports.Contains(80) ? $"http://{d.Ip}" : d.Ports.Contains(8080) ? $"http://{d.Ip}:8080" : null;
        if (web != null) buttons.Children.Add(Ui.Button(T("Open in Browser"), () => Ui.Open(web)));
        foreach (FrameworkElement b in buttons.Children) b.Margin = new Thickness(0, 0, 8, 0);
        var page = new StackPanel { Children = { Ui.Card(new StackPanel { Children = { header, fields, buttons } }, new Thickness(0)) } };

        if (d.Ports.Count > 0 && IPAddress.TryParse(d.Ip, out var ip))
        {
            var ports = new StackPanel();
            foreach (int p in d.Ports.Order())
            {
                var usual = Ports.Of(p);
                var link = new Hyperlink(new Run(p.ToString())) { ToolTip = T("Look closer") };
                link.Click += (_, _) => PortWindow.Open(ip, d.Title, new PortResult(p, PortState.Open, double.NaN, "", ""), null, logins, owner);
                var line = new TextBlock { Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
                line.Inlines.Add(link);
                if (usual != null) line.Inlines.Add(new Run($"  {usual.Service}: {usual.About}"));
                ports.Children.Add(line);
            }
            page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Open ports")), ports } }, new Thickness(0, 12, 0, 0)));
        }
        if (d.Why.Count > 0)
            page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Why")), new TextBlock { Text = string.Join("\n", d.Why.Select(x => "· " + x)), TextWrapping = TextWrapping.Wrap } } }, new Thickness(0, 12, 0, 0)));

        if (lookups.Count == 0)
        {
            page.Margin = new Thickness(16);
            w.Content = Ui.Page(page);
            w.Show();
            return;
        }

        // the scan's column, then one per module that can look (removed when it doesn't know the device)
        var columns = new Grid { Margin = new Thickness(16) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(page, 0);
        columns.Children.Add(page);
        var notFound = Ui.Secondary("", wrap: true);
        notFound.Margin = new Thickness(0, 12, 0, 0);
        page.Children.Add(notFound);
        var missing = new List<string>();
        var stop = new CancellationTokenSource();
        w.Closed += (_, _) => stop.Cancel();
        var query = new DeviceLookups.Query(d.Ip, d.Mac, d.Names.Prepend(d.Name).Where(n => n.Length > 0).Distinct().ToList());
        w.Width = 560;
        foreach (var source in lookups)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            w.Width += 392;
            var body = new StackPanel { Children = { SourceHeader(source), Ui.Secondary(F("Looking in {0}…", source.Name)) } };
            var card = Ui.Card(body, new Thickness(12, 0, 0, 0));
            card.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(card, columns.ColumnDefinitions.Count - 1);
            columns.Children.Add(card);
            _ = FillAsync(source, body, card);
        }
        w.Content = Ui.Page(columns);
        w.Show();

        async Task FillAsync(DeviceLookups.Source source, StackPanel body, Border card)
        {
            DeviceLookups.Match? match;
            try { match = await source.Find(query, stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (stop.IsCancellationRequested) return;
                body.Children.RemoveAt(1);
                body.Children.Add(new TextBlock { Text = F("Couldn't look: {0}", ex.Message), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("#E45959") });
                return;
            }
            if (stop.IsCancellationRequested) return;
            if (match == null)
            {
                // not known there: its column goes, and the scan's column says so
                int col = Grid.GetColumn(card);
                columns.Children.Remove(card);
                foreach (UIElement c in columns.Children) if (Grid.GetColumn(c) > col) Grid.SetColumn(c, Grid.GetColumn(c) - 1);
                columns.ColumnDefinitions.RemoveAt(columns.ColumnDefinitions.Count - 1);
                w.Width -= 392;
                missing.Add(source.Name);
                notFound.Text = F("Not found in {0}", string.Join(", ", missing));
                return;
            }
            body.Children.RemoveAt(1);
            body.Children.Add(new TextBlock { Text = match.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var by = Ui.Secondary(F("Matched by {0}", match.MatchedBy));
            by.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(by);
            var lines = new NetUi.Fields();
            foreach (var l in match.Lines)
                lines.Add(l.Label, l.Value, color: l.Color != null ? Ui.Brush(card, l.Color) : null);
            body.Children.Add(lines);
            if (match.Open is { } open)
            {
                var b = Ui.Button(F("Open in {0}", source.Name), () => { w.Close(); open(); });
                b.HorizontalAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(b);
            }
        }
    }

    private static StackPanel SourceHeader(DeviceLookups.Source source)
    {
        var icon = Ui.Icon(source.Glyph, 20);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { icon, Ui.Header(source.Name, new Thickness(0)) } };
    }
}
