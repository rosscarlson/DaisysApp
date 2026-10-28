using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Port Scan: which TCP ports a host answers on, as a basic nmap does (a connection to each port:
/// open, refused (closed) or no answer (filtered)), with each open port's service, what it is, what it said first (its
/// banner), and a link to more about the port. A double-click on a port looks closer at it (<see cref="PortWindow"/>).
/// </summary>
internal sealed class PortScanView : UserControl, IDisposable
{
    public sealed record PortRow(int Port, string State, string Service, string About, string Banner, double Ms, string Time, IPAddress Ip, string Host, PortResult Result);

    private readonly NetworkingSettings settings;
    private readonly ComboBox hostBox = new() { IsEditable = true, Width = 240, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox customBox = new() { Width = 160, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button run;
    private readonly ProgressBar bar = Ui.Bar(4);
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly DataGrid table = Ui.Table(480);
    private readonly ObservableCollection<PortRow> rows = new();
    private readonly List<PortRow> all = new();
    private CancellationTokenSource? cts;

    public PortScanView(NetworkingSettings settings)
    {
        this.settings = settings;
        hostBox.Text = settings.ScanHost.Length > 0 ? settings.ScanHost : NetInfo.Gateway() ?? "127.0.0.1";
        hostBox.ToolTip = T("Host name or IP address");
        hostBox.Items.Add("127.0.0.1");
        if (NetInfo.Gateway() is { } gw) hostBox.Items.Add(gw);
        hostBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = ScanAsync(); } };
        var ports = Ui.Choice(new[] { (T("Common"), "Common"), (T("1–1024"), "WellKnown"), (T("All"), "All"), (T("Custom"), "Custom") }, settings.ScanPorts,
            v => { settings.ScanPorts = v; settings.Save(); customBox.Visibility = v == "Custom" ? Visibility.Visible : Visibility.Collapsed; }, 100);
        ports.ToolTip = F("Common: {0} well-known ports", Ports.Common.Count);
        customBox.Text = settings.ScanCustom;
        customBox.ToolTip = T("e.g. 22, 80, 8000-8100");
        customBox.Visibility = settings.ScanPorts == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        var showAll = new CheckBox { Content = T("Show Closed"), IsChecked = settings.ScanShowAll, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("List closed and unanswered ports too") };
        Ui.OnToggle(showAll, on => { settings.ScanShowAll = on; settings.Save(); Fill(); });
        run = Ui.Button(T("Scan"), () => { if (cts != null) cts.Cancel(); else _ = ScanAsync(); }, accent: true);

        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var e in new FrameworkElement[] { hostBox, ports, customBox, showAll, run })
        {
            e.Margin = new Thickness(0, 0, 10, 0);
            controls.Children.Add(e);
        }
        bar.Margin = new Thickness(0, 0, 0, 6);
        bar.Visibility = Visibility.Collapsed;
        status.Margin = new Thickness(0, 0, 0, 8);

        table.Columns.Add(Ui.Column(T("Port"), nameof(PortRow.Port), 70, right: true));
        table.Columns.Add(Ui.Column(T("State"), nameof(PortRow.State), 90));
        table.Columns.Add(Ui.Column(T("Service"), nameof(PortRow.Service), 140));
        table.Columns.Add(Ui.Column(T("What it is"), nameof(PortRow.About), -1.4));
        table.Columns.Add(Ui.CopyColumn(T("Banner"), nameof(PortRow.Banner), -1));
        table.Columns.Add(Ui.Column(T("Time"), nameof(PortRow.Time), 70, nameof(PortRow.Ms), right: true));
        table.Columns.Add(LinkColumn());
        table.ItemsSource = rows;
        Ui.ItemMenu(table, o => o is PortRow r ? [r.Host, r.Ip.ToString(), r.Banner] : [], (T("Details…"), o => Details(o as PortRow)));
        table.MouseDoubleClick += (_, e) => Details(Ui.ItemOf(e, table) as PortRow);

        Content = Ui.Page(Ui.Card(new StackPanel { Children = { Ui.Header(T("Port Scan")), controls, bar, status, table } }, new Thickness(0)));
    }

    /// <summary>"Details": the port's page at SpeedGuide.</summary>
    private static DataGridTemplateColumn LinkColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        var link = new FrameworkElementFactory(typeof(Hyperlink));
        link.AddHandler(Hyperlink.ClickEvent, new RoutedEventHandler((s, _) =>
        {
            if ((s as FrameworkContentElement)?.DataContext is PortRow r) Ui.OpenUrl(Ports.InfoUrl(r.Port));
        }));
        var run = new FrameworkElementFactory(typeof(Run));
        run.SetValue(Run.TextProperty, T("Details"));
        link.AppendChild(run);
        text.AppendChild(link);
        text.SetValue(TextBlock.ToolTipProperty, T("More about this port (speedguide.net)"));
        return new DataGridTemplateColumn { Header = "", Width = 70, CellTemplate = new DataTemplate { VisualTree = text } };
    }

    private void Details(PortRow? r)
    {
        if (r != null) PortWindow.Open(r.Ip, r.Host, r.Result, null, settings.HostLogins, this);
    }

    private void Fill()
    {
        rows.Clear();
        foreach (var r in all.OrderBy(r => r.Port).Where(r => settings.ScanShowAll || r.State == T("open"))) rows.Add(r);
    }

    private async Task ScanAsync()
    {
        if (cts != null) return;
        string host = hostBox.Text.Trim();
        if (host.Length == 0) return;
        List<int>? ports = settings.ScanPorts switch
        {
            "WellKnown" => Enumerable.Range(1, 1024).ToList(),
            "All" => Enumerable.Range(1, 65535).ToList(),
            "Custom" => Ports.Parse(customBox.Text),
            _ => Ports.Common.ToList(),
        };
        if (ports == null) { status.Text = T("Ports: numbers and ranges, e.g. 22, 80, 8000-8100."); return; }
        settings.ScanHost = host;
        settings.ScanCustom = customBox.Text.Trim();
        settings.Save();

        cts = new CancellationTokenSource();
        var ct = cts.Token;
        run.Content = T("Stop");
        all.Clear();
        rows.Clear();
        bar.Visibility = Visibility.Visible;
        bar.Maximum = ports.Count;
        bar.Value = 0;
        var sw = Stopwatch.StartNew();
        int done = 0, open = 0, closed = 0, filtered = 0;
        try
        {
            status.Text = F("Looking up {0}…", host);
            if (await NetInfo.ResolveAsync(host, ct) is not { } ip) { status.Text = F("Couldn't find {0}.", host); return; }
            Logging.Log.Here.Info($"Port scan of {host} ({ip}): {ports.Count} ports");
            int timeout = Math.Clamp(settings.ScanTimeoutMs, 100, 10000);
            using var gate = new SemaphoreSlim(Math.Clamp(settings.ScanParallel, 1, 1000));
            var progress = Stopwatch.StartNew();
            var tasks = ports.Select(async port =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var r = Row(host, ip, await PortProbe.ProbeAsync(ip, port, timeout, ct));
                    await Dispatcher.InvokeAsync(() =>
                    {
                        done++;
                        if (r.State == T("open")) open++;
                        else if (r.State == T("closed")) closed++;
                        else filtered++;
                        all.Add(r);
                        if (r.State == T("open") || settings.ScanShowAll)
                        {
                            int at = 0;
                            while (at < rows.Count && rows[at].Port < r.Port) at++;
                            rows.Insert(at, r);
                        }
                        if (progress.ElapsedMilliseconds > 100 || done == ports.Count)
                        {
                            progress.Restart();
                            bar.Value = done;
                            status.Text = F("{0} of {1} · {2} open", done, ports.Count, open);
                        }
                    });
                }
                finally { gate.Release(); }
            }).ToList();
            await Task.WhenAll(tasks);
            status.Text = (host == ip.ToString() ? host + ": " : F("{0} ({1}): ", host, ip)) + F("{0} open, {1} closed, {2} no answer · {3:0.0} s", open, closed, filtered, sw.Elapsed.TotalSeconds);
            Logging.Log.Here.Info($"Port scan of {host}: {open} open ({string.Join(", ", all.Where(r => r.State == T("open")).Select(r => r.Port).OrderBy(p => p))})");
        }
        catch (OperationCanceledException) { status.Text = F("Stopped: {0} of {1} · {2} open", done, ports.Count, open); }
        finally
        {
            cts?.Dispose();
            cts = null;
            run.Content = T("Scan");
            bar.Visibility = Visibility.Collapsed;
        }
    }

    private static PortRow Row(string host, IPAddress ip, PortResult r)
    {
        var known = Ports.Of(r.Port);
        string state = r.State switch { PortState.Open => T("open"), PortState.Closed => T("closed"), _ => T("no answer") };
        return new(r.Port, state, known?.Service ?? "", known?.About ?? "", r.Banner, double.IsNaN(r.Ms) ? double.MaxValue : r.Ms, Ui.Ms(r.Ms), ip, host, r);
    }

    public void Dispose() => cts?.Cancel();
}
