using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// A port double-clicked (Port Scan, Host Scan): what the port is usually for, what it said, and a closer look at what's
/// really speaking on it (<see cref="ServiceProbe"/>: product and version, certificate, settings, findings), with links.
/// </summary>
internal static class PortWindow
{
    /// <param name="known">What Host Scan already found on it (null: looked at now).</param>
    public static void Open(IPAddress ip, string host, PortResult port, ServiceInfo? known, bool logins, FrameworkElement owner)
    {
        var usual = Ports.Of(port.Port);
        var w = Ui.Dialog(usual != null ? F("Port {0} · {1} · {2}", port.Port, usual.Service, host) : F("Port {0} · {1}", port.Port, host), owner, 820, 720);
        w.ResizeMode = ResizeMode.CanResize;
        var fields = new Ui.Fields();
        fields.Add(T("Host"), host == ip.ToString() ? host : $"{host} ({ip})");
        fields.Add(T("Port"), $"{port.Port} (TCP)");
        fields.Add(T("State"), port.State switch { PortState.Open => T("open"), PortState.Closed => T("closed"), _ => T("no answer") });
        if (usual != null) fields.Add(T("Usually"), $"{usual.Service}: {usual.About}");
        fields.Add(T("Time to connect"), Ui.Ms(port.Ms));
        var links = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        links.Children.Add(Ui.Link("SpeedGuide", Ports.InfoUrl(port.Port)));
        links.Children.Add(Ui.Link("IANA", $"https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xhtml?search={port.Port}"));
        var top = Ui.Card(new StackPanel { Children = { Ui.Header(T("Port")), fields, links } }, new Thickness(0));

        var closer = new StackPanel();
        var status = Ui.Secondary(T("Looking closer…"));
        closer.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("What's on it")), status } }, new Thickness(0, 12, 0, 0)));
        w.Content = Ui.Page(new StackPanel { Margin = new Thickness(16), Children = { top, closer } });
        var cts = new CancellationTokenSource();
        w.Closed += (_, _) => cts.Cancel();
        w.Show();
        if (port.State != PortState.Open) { status.Text = T("Only an open port can be looked at."); return; }

        _ = Fill();
        async Task Fill()
        {
            ServiceInfo info;
            try { info = known ?? await ServiceProbe.ProbeAsync(ip, host, port, logins, cts.Token); }
            catch (OperationCanceledException) { return; }
            closer.Children.Clear();
            var what = new Ui.Fields();
            what.Add(T("Speaks"), info.Protocol.Length > 0 ? info.Protocol : T("not recognised"));
            what.Add(T("Software"), info.ProductText);
            IpDetailsView.Fill(what, info.Facts);
            var whatPanel = new StackPanel { Children = { Ui.Header(T("What's on it")), what } };
            if (info.Url != null)
            {
                var open = Ui.Button(T("Open in Browser"), () => Ui.OpenUrl(info.Url));
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 6, 0, 0);
                whatPanel.Children.Add(open);
            }
            if (info.Product.Length > 0 && info.Version.Length > 0) links.Children.Add(Ui.Link(F("NVD: {0}", info.ProductText), ServiceInfo.NvdUrl(info.ProductText)));
            closer.Children.Add(Ui.Card(whatPanel, new Thickness(0, 12, 0, 0)));
            closer.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Findings")), Cards.Findings(info.Findings) } }, new Thickness(0, 12, 0, 0)));
            string raw = info.Raw.Length > 0 ? info.Raw : port.RawBanner;
            if (raw.Length > 0) closer.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("What it said")), Cards.Raw(raw) } }, new Thickness(0, 12, 0, 0)));
        }
    }
}
