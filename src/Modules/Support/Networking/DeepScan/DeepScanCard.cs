using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// The Deep Scan box in Host Scan's report (to the right of Summary): a progress bar while it runs, then the verdict,
/// the candidate addresses ranked by how well they match the site, the subdomains and mail records it found, the
/// sources it used, and the scan log. <see cref="DeepScanner"/> does the work.
/// </summary>
internal sealed class DeepScanCard
{
    private readonly FrameworkElement owner;
    private readonly ProgressBar bar = Ui.Bar(4);
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly StackPanel body = new();

    public Border Root { get; }

    public DeepScanCard(FrameworkElement owner)
    {
        this.owner = owner;
        bar.Margin = new Thickness(0, 6, 0, 6);
        bar.Maximum = 1;
        bar.Visibility = Visibility.Collapsed;
        Root = Ui.Card(new StackPanel { Children = { Ui.Header(T("Deep Scan")), bar, status, body } }, new Thickness(0));
        status.Text = T("Finding the origin behind the proxy…");
    }

    public void Begin()
    {
        body.Children.Clear();
        bar.Value = 0;
        bar.Visibility = Visibility.Visible;
    }

    public void Report(string text, double frac)
    {
        status.Text = text;
        bar.Value = frac;
    }

    public void Fail(string message)
    {
        bar.Visibility = Visibility.Collapsed;
        status.Text = message;
    }

    public void Show(DeepReport r)
    {
        bar.Visibility = Visibility.Collapsed;
        status.Text = F("{0} · {1} sources", r.Domain, r.Sources.Count);
        body.Children.Clear();

        // the verdict, in its level's colour
        string color = r.VerdictLevel switch { Severity.High => "#FF5C5C", Severity.Medium => NetUi.Orange, _ => "AccentBrush" };
        var verdict = new TextBlock { Text = r.Verdict, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 10) };
        if (color.StartsWith('#')) verdict.Foreground = Ui.Brush(owner, color);
        else verdict.SetResourceReference(TextBlock.ForegroundProperty, color);
        body.Children.Add(verdict);
        if (r.FrontCdn != null) body.Children.Add(Ui.Secondary(F("Behind: {0}", r.FrontCdn)));

        // candidates, best match first
        var origins = r.Candidates.Where(c => !c.Cdn).ToList();
        if (origins.Count > 0)
        {
            body.Children.Add(Sub(T("Candidate origins")));
            foreach (var c in origins.Take(12)) body.Children.Add(Candidate(c));
        }
        int cdn = r.Candidates.Count(c => c.Cdn);
        if (cdn > 0) body.Children.Add(Ui.Secondary(F("{0} more addresses were the CDN's own (left out).", cdn)));
        if (r.Candidates.Count == 0) body.Children.Add(Ui.Secondary(T("No candidate addresses found.")));

        // what it found along the way
        if (r.Subdomains.Count > 0)
        {
            var list = new StackPanel();
            foreach (var (host, ip, isCdn) in r.Subdomains.Take(60))
                list.Children.Add(NetUi.Selectable($"{host} → {ip}" + (isCdn ? " · " + T("CDN") : " · " + T("off the CDN")), false));
            body.Children.Add(new Expander { Header = F("Subdomains ({0})", r.Subdomains.Count), Content = list, Margin = new Thickness(0, 8, 0, 0) });
        }
        if (r.Mail.Count > 0)
        {
            var f = new NetUi.Fields();
            foreach (var fact in r.Mail) f.Add(fact.Label, fact.Value);
            body.Children.Add(new Expander { Header = T("Mail records"), Content = f, Margin = new Thickness(0, 4, 0, 0) });
        }

        // the honest caveat, always
        body.Children.Add(new TextBlock
        {
            Text = T("A hardened origin (its firewall allowing only the CDN, and Authenticated Origin Pulls) leaks nothing: no public method recovers it."),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 10, 0, 0),
        });
        if (r.Sources.Count > 0) body.Children.Add(Ui.Secondary(F("Sources: {0}", string.Join(", ", r.Sources))));
        body.Children.Add(new Expander { Header = T("Deep Scan log"), Content = Cards.Raw(string.Join("\n", r.Log)), Margin = new Thickness(0, 4, 0, 0) });
    }

    private FrameworkElement Candidate(DeepCandidate c)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var top = new DockPanel();
        var ipText = NetUi.Selectable(c.HostName is { Length: > 0 } h ? $"{c.Ip}  ({h})" : c.Ip, false);
        ipText.FontWeight = FontWeights.SemiBold;
        var scoreText = new TextBlock { Text = F("{0}%", c.Score), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        if (c.Score >= 70) scoreText.Foreground = Ui.Brush(owner, "#FF5C5C");
        else if (c.Score >= 40) scoreText.Foreground = Ui.Brush(owner, NetUi.Orange);
        DockPanel.SetDock(scoreText, Dock.Right);
        top.Children.Add(scoreText);
        top.Children.Add(ipText);
        panel.Children.Add(top);
        var meter = Ui.Bar(5);
        meter.Maximum = 100;
        meter.Value = c.Score;
        meter.Margin = new Thickness(0, 3, 0, 3);
        panel.Children.Add(meter);
        panel.Children.Add(Ui.Secondary(string.Join(" · ", new[] { c.Source, c.Why, c.Status is { } s ? $"HTTP {s}" : null }.Where(x => !string.IsNullOrEmpty(x)))));
        return panel;
    }

    private static TextBlock Sub(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };
}
