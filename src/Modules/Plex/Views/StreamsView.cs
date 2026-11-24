using System.Windows.Controls;
using System.Windows;
using DaisysApp.Applets.Plex.Streams;
using DaisysApp.Shared.Charts;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Streams (since 4.21.0): each video stream's last 5 minutes (what Plex sent the player each second, red where it
/// buffered, the file's bit rate dashed; the player's ping) and every stall with its likely cause, from
/// <see cref="StreamMonitor"/>. Watch in background keeps it recording on other tabs.
/// </summary>
internal sealed class StreamsView : Border, IDisposable
{
    private readonly PlexService service;
    private readonly StreamMonitor monitor;
    private readonly TextBlock summary = Ui.Secondary();
    private readonly StackPanel streams = new();
    private readonly TextBlock note = Ui.Note("", 4);
    private readonly DataGrid stalls = Ui.Table(300);
    private readonly Dictionary<string, StreamPanel> panels = new();

    public StreamsView(PlexService service, StreamMonitor monitor)
    {
        this.service = service;
        this.monitor = monitor;
        var background = Ui.Chip(T("Watch in background"), service.Settings.WatchStreams, T("Keep recording streams on other tabs"));
        background.Margin = new Thickness(12, 0, 0, 0);
        summary.VerticalAlignment = VerticalAlignment.Center;
        Ui.OnToggle(background, on =>
        {
            service.Settings.WatchStreams = on;
            service.Settings.Save();
            monitor.Recheck();
        });
        var page = new StackPanel();
        var now = new StackPanel();
        now.Children.Add(Ui.HeaderRow(T("Streams"), summary, background));
        now.Children.Add(streams);
        now.Children.Add(note);
        page.Children.Add(Ui.Card(now, new Thickness(0, 0, 0, 12)));

        stalls.Columns.Add(Ui.Column(T("When"), nameof(Stall.When), 70));
        stalls.Columns.Add(Ui.Column(T("Title"), nameof(Stall.Title), 180));
        stalls.Columns.Add(Ui.Column(T("Player"), nameof(Stall.Player), 170));
        stalls.Columns.Add(Ui.Column(T("Length"), nameof(Stall.Length), 60, nameof(Stall.Seconds), right: true));
        stalls.Columns.Add(Ui.Column(T("Sent before / during"), nameof(Stall.Data), 130));
        stalls.Columns.Add(Ui.Column(T("Ping"), nameof(Stall.PingText), 110));
        stalls.Columns.Add(Ui.Column(T("Likely cause"), nameof(Stall.Cause), -1));
        var list = new StackPanel();
        list.Children.Add(Ui.Header(T("Stalls")));
        list.Children.Add(stalls);
        page.Children.Add(Ui.Card(list));
        Child = Ui.Page(page);

        monitor.Updated += Show;
        IsVisibleChanged += (_, _) => monitor.Visible = IsVisible;
        Show();
    }

    private void Show()
    {
        if (!IsVisible) return;
        var tracks = monitor.Tracks.OrderByDescending(t => t.Live).ThenByDescending(t => t.LastSeen).ToList();
        int i = 0;
        foreach (var t in tracks)
        {
            if (!panels.TryGetValue(t.Key, out var p)) panels[t.Key] = p = new StreamPanel();
            p.Show(t);
            if (streams.Children.IndexOf(p) != i)
            {
                streams.Children.Remove(p);
                streams.Children.Insert(i, p);
            }
            i++;
        }
        foreach (var gone in panels.Keys.Except(tracks.Select(t => t.Key)).ToList())
        {
            streams.Children.Remove(panels[gone]);
            panels.Remove(gone);
        }
        int live = tracks.Count(t => t.Live);
        summary.Text = live == 0 ? T("No video playing") : P(live, "{0} stream", "{0} streams");
        note.Text = service.Client == null ? T("Plex isn't set up: Settings → Plex.") : monitor.Error ?? "";
        stalls.ItemsSource = null;
        stalls.ItemsSource = monitor.Stalls;
    }

    public void Dispose()
    {
        monitor.Updated -= Show;
        monitor.Stop();
    }
}

/// <summary>One stream: what it is, how it's going now, and its graphs.</summary>
internal sealed class StreamPanel : Border
{
    private static readonly TimeSpan Span = TimeSpan.FromMinutes(5);
    private static readonly Unit Mbps = new("Mbps", "0.0"), Ms = new("ms", "0");

    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel tags = new() { Margin = new Thickness(0, 4, 0, 2) };
    private readonly ContentControl facts = new(), now = new();
    private readonly LineGraph sent = new() { Detailed = true, Height = 140, Margin = new Thickness(0, 6, 0, 0) };
    private readonly LineGraph ping = new() { Detailed = true, Height = 80, Margin = new Thickness(0, 4, 0, 0) };

    public StreamPanel()
    {
        Margin = new Thickness(0, 0, 0, 10);
        Padding = new Thickness(0, 0, 0, 10);
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BorderBrushProperty, "CardBorderBrush");
        Child = new StackPanel { Children = { title, tags, facts, now, sent, ping } };
    }

    public void Show(StreamTrack t)
    {
        title.Text = t.Title + (t.User.Length > 0 ? " · " + t.User : "");
        tags.Children.Clear();
        string state = t.Live ? t.State : "stopped";
        tags.Children.Add(PlexUi.Tag(state switch { "playing" => T("Playing"), "paused" => T("Paused"), "buffering" => T("Buffering"), "stopped" => T("Stopped"), _ => state },
            state == "playing" ? "SuccessBrush" : state == "buffering" ? "DangerBrush" : "#6B7280"));
        tags.Children.Add(PlexUi.Tag(t.Decision, t.Decision == T("Direct Play") ? "SuccessBrush" : t.Decision == T("Transcode") ? "DangerBrush" : PlexUi.Amber));
        if (t.StallCount > 0) tags.Children.Add(PlexUi.Tag(P(t.StallCount, "{0} stall", "{0} stalls") + $" · {t.Stalled.TotalSeconds:0} s", "DangerBrush"));

        double fileMbps = t.FileKbps / 1000.0;
        facts.Content = PlexUi.Facts(
            (T("Player"), t.Player),
            (t.Local ? T("Local") : T("Remote"), t.Address),
            (T("File"), fileMbps > 0 ? F("{0:0.0} Mbps average", fileMbps) : null),
            (T("Speed"), t.Speed > 0 ? F("{0:0.0}×", t.Speed) : null));

        var last10 = t.Sent.Where(p => p.T > DateTime.Now.AddSeconds(-12)).Select(p => p.V).ToList();
        var lastPing = t.Ping.Count > 0 ? t.Ping[^1].V : double.NaN;
        var pings = t.Ping.Where(p => p.T > DateTime.Now.AddMinutes(-1)).ToList();
        now.Content = t.Live ? PlexUi.Facts(
            (T("Sent"), last10.Count > 0 ? F("{0:0.0} Mbps (10 s)", last10.Average()) : null),
            (T("Peak"), t.PeakMbps > 0 ? F("{0:0} Mbps", t.PeakMbps) : null),
            (T("Ping"), t.Ping.Count == 0 ? null : double.IsNaN(lastPing) ? T("no answer") : F("{0:0} ms", lastPing)),
            (T("Last minute"), pings.Count == 0 ? null : F("max {0:0} ms, {1} lost", pings.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(0).Max(), pings.Count(p => double.IsNaN(p.V)))),
            (T("Stall"), t.Open is { Kind: null } s ? s.Cause : null)) : null;

        DateTime to = t.Live ? DateTime.Now : t.LastSeen, from = to - Span;
        double top = Math.Max(Math.Max(t.PeakMbps, fileMbps) * 1.1, 1);
        var buffering = t.States.Select(s => (s.T, s.V == "buffering" ? top : double.NaN)).ToList();
        sent.Levels = fileMbps > 0 ? [(fileMbps, PlexUi.Amber)] : [];
        sent.Show([
            new GraphSeries { Name = T("Sent"), Points = t.Sent, Metric = Mbps },
            new GraphSeries { Name = T("Buffering"), Points = buffering, Metric = new Mark("Mbps"), Color = "DangerBrush", Faint = false },
        ], from, to, null, TimeSpan.FromSeconds(5));

        double pingTop = t.Ping.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(10).Max();
        var lost = t.Ping.Select(p => (p.T, double.IsNaN(p.V) ? pingTop : double.NaN)).ToList();
        ping.Visibility = t.Ping.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ping.Show([
            new GraphSeries { Name = T("Ping"), Points = t.Ping, Metric = Ms, Color = PlexUi.Amber, Fill = false },
            new GraphSeries { Name = T("Lost"), Points = lost, Metric = new Mark("ms"), Color = "DangerBrush", Fill = false, Bold = true },
        ], from, to, null, TimeSpan.FromSeconds(5));
    }

    private sealed record Unit(string Name, string Format) : IGraphUnit
    {
        string IGraphUnit.Unit => Name;
        public string Text(double value) => double.IsNaN(value) ? "—" : value.ToString(Format) + " " + Name;
    }

    /// <summary>A mark (buffering, a lost ping): its readout says yes, not its height.</summary>
    private sealed record Mark(string Name) : IGraphUnit
    {
        string IGraphUnit.Unit => Name;
        public string Text(double value) => double.IsNaN(value) ? "—" : T("yes");
    }
}
