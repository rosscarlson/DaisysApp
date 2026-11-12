using System.Text.Json;
using System.Windows.Controls;
using System.Windows;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Tautulli: its statistics for the last 7, 30, 90 or 365 days (plays, most watched and most popular, most active users,
/// platforms and libraries, most streams at once) and every user (last seen, plays, time watched, last played). Its
/// recent plays are on Now Playing.
/// </summary>
internal sealed class TautulliView : Border
{
    private readonly PlexService service;
    private readonly TextBlock totals = Ui.Secondary("", wrap: true);
    private readonly WrapPanel stats = new();
    private readonly DataGrid users = Ui.Table(380);
    private readonly TextBlock note = Ui.Note("", 0);
    private bool loading;
    private DateTime loadedAt;

    public TautulliView(PlexService service)
    {
        this.service = service;
        var days = Ui.Choice(new[] { 7, 30, 90, 365 }.Select(d => (P(d, "{0} day", "{0} days"), d)), service.Settings.StatsDays, v => { service.Settings.StatsDays = v; service.Settings.Save(); _ = Load(); }, 100);
        var refresh = Ui.IconButton("\uE72C", T("Refresh"), null, () => _ = Load());
        var page = new StackPanel();
        page.Children.Add(note);
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Statistics"), new TextBlock { Text = T("Last"), VerticalAlignment = VerticalAlignment.Center }, days, refresh), totals, stats } }, new Thickness(0, 0, 0, 12)));

        users.Columns.Add(Ui.Column(T("User"), nameof(UserRow.Name), 160));
        users.Columns.Add(Ui.Column(T("Last seen"), nameof(UserRow.SeenText), 120, nameof(UserRow.Seen)));
        users.Columns.Add(Ui.Column(T("Plays"), nameof(UserRow.PlaysText), 70, nameof(UserRow.Plays), right: true));
        users.Columns.Add(Ui.Column(T("Watched"), nameof(UserRow.Watched), 90, nameof(UserRow.Seconds), right: true));
        users.Columns.Add(Ui.Column(T("Last played"), nameof(UserRow.LastPlayed), -1));
        users.Columns.Add(Ui.Column(T("Player"), nameof(UserRow.Player), 150));
        users.Columns.Add(Ui.Column(T("Address"), nameof(UserRow.Address), 120));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Users")), users } }));
        Child = Ui.Page(page);

        IsVisibleChanged += (_, e) => { if (e.NewValue is true && DateTime.Now - loadedAt > TimeSpan.FromMinutes(2)) _ = Load(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { loadedAt = default; if (IsVisible) _ = Load(); });
    }

    private async Task Load()
    {
        if (loading) return;
        if (service.Tautulli is not { } t)
        {
            note.Text = T("Tautulli isn't set up: Settings → Plex.");
            note.Margin = new Thickness(0, 0, 0, 12);
            return;
        }
        loading = true;
        loadedAt = DateTime.Now;
        int days = service.Settings.StatsDays;
        try
        {
            note.Text = "";
            note.Margin = new Thickness(0);
            var byDate = await t.CallAsync("get_plays_by_date", $"time_range={days}");
            var series = PlexClient.List(byDate, "series").ToList();
            long Sum(string name) => series.Where(s => PlexClient.Str(s, "name") == name).SelectMany(s => PlexClient.List(s, "data")).Sum(v => v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0);
            var activity = await t.CallAsync("get_activity");
            totals.Text = F("{0} plays: {1} TV, {2} movies, {3} music · {4} playing now", Sizes.Count(Sum("Total")), Sizes.Count(Sum("TV")), Sizes.Count(Sum("Movies")), Sizes.Count(Sum("Music")), PlexClient.Str(activity, "stream_count") ?? "0");

            var home = await t.CallAsync("get_home_stats", $"time_range={days}&stats_count=5");
            stats.Children.Clear();
            foreach (var s in home.ValueKind == JsonValueKind.Array ? home.EnumerateArray() : Enumerable.Empty<JsonElement>())
                if (Stat(s) is { } card) stats.Children.Add(card);

            var u = await t.CallAsync("get_users_table", "length=200&order_column=last_seen&order_dir=desc");
            users.ItemsSource = PlexClient.List(u, "data").Select(UserRow.From).ToList();
        }
        catch (Exception ex) { note.Text = ex.Message; note.Margin = new Thickness(0, 0, 0, 12); }
        finally { loading = false; }
    }

    /// <summary>One of Tautulli's home stats as a small card: its title, then a line per row.</summary>
    private static FrameworkElement? Stat(JsonElement s)
    {
        string id = PlexClient.Str(s, "stat_id") ?? "";
        var rows = PlexClient.List(s, "rows").ToList();
        if (rows.Count == 0) return null;
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = PlexClient.Str(s, "stat_title") ?? id, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var r in rows)
        {
            string name = id switch
            {
                "top_users" or "last_watched" => PlexClient.Str(r, "friendly_name") ?? "",
                "top_platforms" => PlexClient.Str(r, "platform") ?? "",
                "top_libraries" => PlexClient.Str(r, "section_name") ?? PlexClient.Str(r, "library_name") ?? "",
                _ => PlexClient.Str(r, "title") ?? "",
            };
            string value = id switch
            {
                "popular_movies" or "popular_tv" or "popular_music" => P((int)PlexClient.Num(r, "users_watched"), "{0} user", "{0} users"),
                "last_watched" => (PlexClient.Str(r, "title") ?? "") + " · " + PlexUi.Ago(PlexClient.Time(r, "last_watch")),
                "most_concurrent" => PlexClient.Num(r, "count") + " · " + (PlexClient.Time(r, "started")?.ToString("d") ?? ""),
                _ => P((int)PlexClient.Num(r, "total_plays"), "{0} play", "{0} plays"),
            };
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var v = Ui.Secondary(value);
            v.Margin = new Thickness(10, 0, 0, 0);
            if (id != "last_watched") { DockPanel.SetDock(v, Dock.Right); line.Children.Add(v); }
            line.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
            p.Children.Add(line);
            if (id == "last_watched") { v.Margin = new Thickness(0, 0, 0, 4); p.Children.Add(v); }
        }
        var card = Ui.Card(p, new Thickness(0, 0, 10, 10));
        card.Width = 280;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private sealed record UserRow(string Name, DateTime? Seen, long Plays, long Seconds, string LastPlayed, string Player, string Address)
    {
        public string SeenText => PlexUi.Ago(Seen);
        public string PlaysText => Sizes.Count(Plays);
        public string Watched => Seconds <= 0 ? "" : TimeSpan.FromSeconds(Seconds) is var t && t.TotalDays >= 1 ? F("{0:#,0} d {1} h", (int)t.TotalDays, t.Hours) : F("{0} h {1} min", t.Hours, t.Minutes);

        public static UserRow From(JsonElement e) => new(
            PlexClient.Str(e, "friendly_name") ?? PlexClient.Str(e, "username") ?? "",
            PlexClient.Time(e, "last_seen"), PlexClient.Num(e, "plays"), PlexClient.Num(e, "duration"),
            PlexClient.Str(e, "last_played") ?? "",
            ((PlexClient.Str(e, "player") ?? "") + " (" + (PlexClient.Str(e, "platform") ?? "") + ")").Replace(" ()", "").Trim(),
            PlexClient.Str(e, "ip_address") ?? "");
    }
}
