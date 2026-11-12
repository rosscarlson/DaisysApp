using System.Net.Http;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Now Playing: every stream on the server (who, what, on which player, where from, how it plays: Direct Play, Direct
/// Stream or Transcode and why, the bandwidth, how far in), each with Stop; what the server's busy with (scans,
/// optimising…); and Tautulli's recent plays when it's set up. Asked every 5 seconds while it shows.
/// </summary>
internal sealed class ActivityView : Border
{
    private readonly PlexService service;
    private readonly TextBlock summary = Ui.Secondary();
    private readonly StackPanel sessions = new();
    private readonly TextBlock busy = Ui.Note("", 4);
    private readonly StackPanel historyCard = new();
    private readonly DataGrid history = Ui.Table(360);
    private readonly Dictionary<string, SessionCard> cards = new();
    private DateTime historyAt;
    private bool asking;

    public ActivityView(PlexService service)
    {
        this.service = service;
        var page = new StackPanel();
        var now = new StackPanel();
        now.Children.Add(Ui.HeaderRow(T("Now Playing"), summary));
        now.Children.Add(sessions);
        now.Children.Add(busy);
        page.Children.Add(Ui.Card(now, new Thickness(0, 0, 0, 12)));

        history.Columns.Add(Ui.Column(T("When"), nameof(Play.When), 150, nameof(Play.Started)));
        history.Columns.Add(Ui.Column(T("User"), nameof(Play.User), 120));
        history.Columns.Add(Ui.Column(T("Title"), nameof(Play.Title), -1));
        history.Columns.Add(Ui.Column(T("Player"), nameof(Play.Player), 150));
        history.Columns.Add(Ui.Column(T("Watched"), nameof(Play.Watched), 80, nameof(Play.Seconds), right: true));
        history.Columns.Add(Ui.Column(T("Played"), nameof(Play.Decision), 110));
        history.Columns.Add(Ui.Column(T("Address"), nameof(Play.Address), 120));
        historyCard.Children.Add(Ui.Header(T("Recent Plays (Tautulli)")));
        historyCard.Children.Add(history);
        page.Children.Add(Ui.Card(historyCard));
        Child = Ui.Page(page);

        Ui.Every(this, 5000, () => _ = Refresh());
        service.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) { historyAt = default; _ = Refresh(); } });
    }

    private async Task Refresh()
    {
        if (asking) return;
        asking = true;
        try
        {
            historyCard.Visibility = service.Settings.HasTautulli ? Visibility.Visible : Visibility.Collapsed;
            if (service.Client is not { } c)
            {
                summary.Text = "";
                Show([]);
                busy.Text = T("Plex isn't set up: Settings → Plex.");
                return;
            }
            try
            {
                var mc = await c.GetAsync("/status/sessions");
                var list = PlexClient.List(mc, "Metadata").ToList();
                Show(list);
                long kbps = list.Sum(m => PlexClient.Num(PlexClient.Obj(m, "Session"), "bandwidth"));
                int transcodes = list.Count(m => PlexClient.Obj(m, "TranscodeSession") is { ValueKind: JsonValueKind.Object } ts && PlexClient.Str(ts, "videoDecision") == "transcode");
                summary.Text = list.Count == 0 ? T("Nothing playing")
                    : P(list.Count, "{0} stream", "{0} streams") + (transcodes > 0 ? " · " + P(transcodes, "{0} transcoding", "{0} transcoding") : "") + " · " + Kbps(kbps);
                busy.Text = await Activities(c);
            }
            catch (Exception ex)
            {
                summary.Text = "";
                busy.Text = ex is PlexException ? ex.Message : ex.Message;
            }
            if (service.Tautulli is { } t && DateTime.Now - historyAt > TimeSpan.FromSeconds(60))
            {
                historyAt = DateTime.Now;
                try
                {
                    var data = await t.CallAsync("get_history", "length=50&order_column=date&order_dir=desc");
                    history.ItemsSource = PlexClient.List(data, "data").Select(Play.From).ToList();
                }
                catch (Exception ex) { Log.Here.Debug(() => $"Tautulli history: {ex.Message}"); }
            }
        }
        finally { asking = false; }
    }

    /// <summary>What the server is busy with (Plex's /activities: scans, optimising, analysing…).</summary>
    private static async Task<string> Activities(PlexClient c)
    {
        var mc = await c.GetAsync("/activities");
        var lines = PlexClient.List(mc, "Activity").Select(a =>
        {
            long pct = PlexClient.Num(a, "progress");
            string sub = PlexClient.Str(a, "subtitle") ?? "";
            return (PlexClient.Str(a, "title") ?? "") + (sub.Length > 0 ? ": " + sub : "") + (pct > 0 ? $" ({pct}%)" : "");
        }).Where(s => s.Length > 0).ToList();
        return lines.Count == 0 ? "" : T("Server busy: ") + string.Join(" · ", lines);
    }

    private void Show(List<JsonElement> list)
    {
        var keys = new HashSet<string>();
        int i = 0;
        foreach (var m in list)
        {
            string key = PlexClient.Str(m, "sessionKey") ?? i.ToString();
            keys.Add(key);
            if (!cards.TryGetValue(key, out var card))
            {
                card = new SessionCard(service);
                cards[key] = card;
            }
            card.Show(m);
            if (sessions.Children.IndexOf(card) != i)
            {
                sessions.Children.Remove(card);
                sessions.Children.Insert(i, card);
            }
            i++;
        }
        foreach (var gone in cards.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            sessions.Children.Remove(cards[gone]);
            cards.Remove(gone);
        }
    }

    internal static string Kbps(long kbps) => kbps >= 1000 ? F("{0:0.0} Mbps", kbps / 1000.0) : F("{0} kbps", kbps);

    /// <summary>A play in Tautulli's history.</summary>
    private sealed record Play(DateTime Started, string User, string Title, string Player, long Seconds, string Decision, string Address)
    {
        public string When => Started.ToString("g");
        public string Watched => PlexUi.Clock(Seconds * 1000);

        public static Play From(JsonElement e) => new(
            PlexClient.Time(e, "date") ?? PlexClient.Time(e, "started") ?? DateTime.MinValue,
            PlexClient.Str(e, "friendly_name") is { Length: > 0 } f ? f : PlexClient.Str(e, "user") ?? "",
            PlexClient.Str(e, "full_title") ?? PlexClient.Str(e, "title") ?? "",
            ((PlexClient.Str(e, "player") ?? "") + " (" + (PlexClient.Str(e, "platform") ?? "") + ")").Replace(" ()", ""),
            PlexClient.Num(e, "play_duration") is > 0 and var d ? d : PlexClient.Num(e, "duration"),
            PlexClient.Str(e, "transcode_decision") switch { "direct play" => T("Direct Play"), "copy" => T("Direct Stream"), "transcode" => T("Transcode"), var x => x ?? "" },
            PlexClient.Str(e, "ip_address") ?? "");
    }
}

/// <summary>One stream: its poster, what it is, who and where, how it plays, how far in, and Stop.</summary>
internal sealed class SessionCard : Border
{
    private readonly PlexService service;
    private readonly Image poster;
    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock sub = Ui.Secondary();
    private readonly ContentControl who = new();
    private readonly ContentControl how = new();
    private readonly WrapPanel tags = new() { Margin = new Thickness(0, 6, 0, 4) };
    private readonly ProgressBar bar = Ui.Bar(5);
    private readonly TextBlock time = Ui.Secondary();
    private readonly Button stop;
    private string sessionId = "";

    public SessionCard(PlexService service)
    {
        this.service = service;
        Margin = new Thickness(0, 0, 0, 10);
        Padding = new Thickness(0, 0, 0, 10);
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BorderBrushProperty, "CardBorderBrush");
        poster = PlexUi.Poster(null, null, 66, 99);
        poster.Margin = new Thickness(0, 0, 14, 0);
        stop = ConfirmButton.Make(T("Stop"), Stop, T("Stop this stream (the viewer sees a message)"));
        stop.VerticalAlignment = VerticalAlignment.Top;
        stop.MinWidth = 70;
        bar.Margin = new Thickness(0, 4, 10, 0);
        var timeRow = new DockPanel();
        DockPanel.SetDock(time, Dock.Right);
        timeRow.Children.Add(time);
        timeRow.Children.Add(bar);
        var text = new StackPanel { Children = { title, sub, tags, who, how, timeRow } };
        var row = new DockPanel();
        DockPanel.SetDock(poster, Dock.Left);
        DockPanel.SetDock(stop, Dock.Right);
        row.Children.Add(poster);
        row.Children.Add(stop);
        row.Children.Add(text);
        Child = row;
    }

    public void Show(JsonElement m)
    {
        var s = (Func<string, string?>)(n => PlexClient.Str(m, n));
        string type = s("type") ?? "";
        PlexUi.SetPoster(poster, service.Client, type == "episode" ? s("grandparentThumb") ?? s("thumb") : type == "track" ? s("parentThumb") ?? s("thumb") : s("thumb"));
        title.Text = type switch
        {
            "episode" => s("grandparentTitle") ?? "",
            "track" => s("grandparentTitle") + " — " + s("title"),
            _ => s("title") + (s("year") is { } y ? $" ({y})" : ""),
        };
        sub.Text = type switch
        {
            "episode" => $"S{PlexClient.Num(m, "parentIndex"):00}E{PlexClient.Num(m, "index"):00} · {s("title")}",
            "track" => s("parentTitle") ?? "",
            _ => s("librarySectionTitle") ?? "",
        };

        var user = PlexClient.Obj(m, "User");
        var player = PlexClient.Obj(m, "Player");
        var session = PlexClient.Obj(m, "Session");
        var ts = PlexClient.Obj(m, "TranscodeSession");
        var media = PlexClient.List(m, "Media").FirstOrDefault(x => PlexClient.Flag(x, "selected"));
        if (media.ValueKind != JsonValueKind.Object) media = PlexClient.List(m, "Media").FirstOrDefault();
        sessionId = PlexClient.Str(session, "id") ?? "";
        bool local = PlexClient.Flag(player, "local");
        string state = PlexClient.Str(player, "state") ?? "";

        who.Content = PlexUi.Facts(
            (T("User"), PlexClient.Str(user, "title")),
            (T("Player"), $"{PlexClient.Str(player, "title")} ({PlexClient.Str(player, "product")})"),
            (local ? T("Local") : T("Remote"), PlexClient.Str(player, "address")),
            (T("Bandwidth"), PlexClient.Num(session, "bandwidth") is > 0 and var bw ? ActivityView.Kbps(bw) : null));

        string source = Quality(PlexClient.Str(media, "videoResolution"), PlexClient.Str(media, "videoCodec"), PlexClient.Str(media, "audioCodec"), PlexClient.Num(media, "bitrate"));
        string? target = null;
        tags.Children.Clear();
        tags.Children.Add(PlexUi.Tag(state switch { "playing" => T("Playing"), "paused" => T("Paused"), "buffering" => T("Buffering"), _ => state }, state == "playing" ? "SuccessBrush" : state == "buffering" ? "DangerBrush" : "#6B7280"));
        if (ts.ValueKind != JsonValueKind.Object)
            tags.Children.Add(PlexUi.Tag(T("Direct Play"), "SuccessBrush"));
        else
        {
            string v = PlexClient.Str(ts, "videoDecision") ?? "", a = PlexClient.Str(ts, "audioDecision") ?? "";
            bool hw = PlexClient.Flag(ts, "transcodeHwRequested") && (PlexClient.Flag(ts, "transcodeHwFullPipeline") || PlexClient.Flag(ts, "transcodeHwEncoding") || PlexClient.Str(ts, "transcodeHwDecoding") != null);
            if (v == "transcode") tags.Children.Add(PlexUi.Tag(hw ? T("Transcode (hw)") : T("Transcode"), "DangerBrush"));
            else tags.Children.Add(PlexUi.Tag(T("Direct Stream"), PlexUi.Amber));
            if (a == "transcode") tags.Children.Add(PlexUi.Tag(T("Audio transcode"), PlexUi.Amber));
            if (PlexClient.Num(ts, "width") is > 0 and var w && v == "transcode") target = Quality(Res(w, PlexClient.Num(ts, "height")), PlexClient.Str(ts, "videoCodec"), PlexClient.Str(ts, "audioCodec"), 0);
            else target = PlexClient.Str(ts, "container")?.ToUpperInvariant();
            if (PlexClient.Flag(ts, "throttled")) tags.Children.Add(PlexUi.Tag(T("Throttled"), "#6B7280"));
        }
        if (PlexClient.Flag(player, "relayed")) tags.Children.Add(PlexUi.Tag(T("Relayed"), "DangerBrush"));
        if (!PlexClient.Flag(player, "secure")) tags.Children.Add(PlexUi.Tag(T("Not secure"), PlexUi.Amber));

        how.Content = PlexUi.Facts((T("Source"), source), (T("Stream"), target),
            (T("Subtitles"), PlexClient.List(PlexClient.List(media, "Part").FirstOrDefault(), "Stream").FirstOrDefault(x => PlexClient.Num(x, "streamType") == 3 && PlexClient.Flag(x, "selected")) is { ValueKind: JsonValueKind.Object } sub2
                ? (PlexClient.Str(sub2, "displayTitle") ?? "") + (PlexClient.Str(sub2, "decision") == "burn" ? T(" (burned in)") : "") : null));

        long offset = PlexClient.Num(m, "viewOffset"), duration = PlexClient.Num(m, "duration");
        bar.Maximum = Math.Max(1, duration);
        bar.Value = offset;
        time.Text = $"{PlexUi.Clock(offset)} / {PlexUi.Clock(duration)}";
        stop.Visibility = sessionId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? Res(long w, long h) => w >= 3000 ? "4K" : h >= 1000 || w >= 1900 ? "1080p" : h >= 700 || w >= 1200 ? "720p" : h > 0 ? $"{h}p" : null;

    private static string Quality(string? res, string? video, string? audio, long kbps)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(res)) parts.Add(res.ToLowerInvariant() switch { "4k" => "4K", "sd" => "SD", var r when r.All(char.IsDigit) => r + "p", var r => r });
        if (!string.IsNullOrEmpty(video)) parts.Add(video.ToUpperInvariant());
        if (!string.IsNullOrEmpty(audio)) parts.Add(audio.ToUpperInvariant());
        if (kbps > 0) parts.Add(ActivityView.Kbps(kbps));
        return string.Join(" · ", parts);
    }

    private async void Stop()
    {
        if (service.Client is not { } c || sessionId.Length == 0) return;
        try
        {
            string reason = Uri.EscapeDataString(T("The server's admin stopped this stream."));
            await c.SendAsync(HttpMethod.Get, $"/status/sessions/terminate?sessionId={Uri.EscapeDataString(sessionId)}&reason={reason}");
            Log.Here.Info($"Stopped the stream of \"{title.Text}\"");
        }
        catch (Exception ex)
        {
            Log.Here.Warn($"Couldn't stop the stream: {ex.Message}");
            time.Text = F("Couldn't stop it: {0}", ex.Message);
        }
    }
}
