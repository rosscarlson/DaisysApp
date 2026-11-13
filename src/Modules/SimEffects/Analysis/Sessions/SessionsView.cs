using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// The session analysis on the Analysis tab ("Race Analysis" until 4.6.2; Forza Analysis, a module of its own until
/// 4.5.0): for the race picked (the Session box, or a lap, best lap or race clicked in the tables below), its scores and
/// time split, Compare (since 4.6.5 a race and lap of its own, yours or imported, against another on the same track, any
/// car; with export and import) with the race's Video, the Track Map and (since 4.13.0, the corners table before) the
/// friction circle under it; the brake zones lined up at the brake point and at the release and the throttle
/// applications, drawn over each other, with Shifting, four across beside the Tips (red, white and green, a box each;
/// since 4.13.0 a row together). Brake and Throttle Analysis' tables went in 4.13.0, Smoothness in 4.13.1.
/// Its sessions are the races' logs (<see cref="RaceLog"/>), found from the laps and races that name them.
/// </summary>
internal sealed class SessionsView
{
    private readonly AnalysisApplet applet;
    private readonly SessionSettings settings = SessionSettings.Load();
    private readonly Func<string, string, UIElement, bool, UIElement[], Border> card;
    private readonly Action<Border[]> row;
    private readonly ComboBox sessions = new() { MinWidth = 360, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock about = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel scores = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Grid split = new() { Height = 16, Margin = new Thickness(0, 10, 0, 4) };
    private readonly WrapPanel splitLegend = new();
    private readonly WrapPanel numbers = new() { Margin = new Thickness(0, 8, 0, 0) };
    // (since 4.13.0 as tall as the Tips beside them)
    private readonly OverlayChart brakeOverlay = new() { MinHeight = 165 };
    private readonly OverlayChart releaseOverlay = new() { MinHeight = 165 };
    private readonly OverlayChart throttleOverlay = new() { MinHeight = 165 };
    private readonly ShiftChart shiftChart = new() { MinHeight = 165 };
    private readonly StackPanel tips = new();
    // (since 4.13.0 under Compare's chart)
    private readonly FrictionCircle friction = new();
    private PedalAnalysis? analysis;
    private CancellationTokenSource? loading;
    private Border headCard = null!;
    /// <summary>(since 4.13.1) The title row's Current Location link was clicked.</summary>
    internal Action<FrameworkElement>? LocationAsked { get; init; }
    private FrameworkElement Host => headCard;

    // compare
    private readonly CompareChart compareChart = new() { Height = 330 };
    private readonly LineMap lineMap = new();
    private readonly RaceVideoView video = new();
    // (since 4.10.3) under the chart: the whole of it with the part shown framed (the scroll bar before), and playback
    private readonly Playback player = new();
    // (since 4.10.9) the popped-out Video's timeline: Session 1's run alone (its own copy, laid out by itself), its
    // channels shown or hidden (no Clutch), the same playback as Compare's
    private readonly CompareChart videoChart = new() { MinHeight = 140 };
    private readonly StackPanel videoChannels = new() { Orientation = Orientation.Horizontal };
    private Run? videoRun;
    /// <summary>The cursor being moved by the video's own play (popped out): the video isn't sent back to it.</summary>
    private bool fromVideo;
    // (since 4.7.0 Session 1 and Session 2: the sessions' boxes narrow, their lines whole when open; the parts' a width,
    // so the channels' boxes after them line up)
    private readonly ComboBox thisSession = new() { Width = 280 };
    private readonly ComboBox thisPart = new() { Width = 110 };
    private readonly ComboBox otherSession = new() { Width = 280 };
    private readonly ComboBox otherPart = new() { Width = 110 };
    private readonly TextBlock compareStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel thisChannels = new() { Orientation = Orientation.Horizontal }, otherChannels = new() { Orientation = Orientation.Horizontal };
    private readonly ToggleButton deltaChip = Ui.Chip(T("Delta"), true, T("The other's time less this one's: above the line, this one is ahead"));
    /// <summary>(since 4.13.0) One timeline per session instead of a lane per channel.</summary>
    private readonly ToggleButton combineChip = Ui.Chip(T("Combine"), false, T("One timeline per session, every channel on it"));
    private PedalAnalysis? other;
    /// <summary>Compare's Session 1: since 4.7.0 always the session analysed (4.6.5–4.6.10 a race of its own).</summary>
    private PedalAnalysis? mine;
    private Item? mineItem;
    private bool filling;
    private readonly Dictionary<string, PedalAnalysis> loaded = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The lap (its number in the race, 1 the first) to pick in This once the session's read.</summary>
    private int? wantLap;

    /// <summary>A session: a race's log (its track, car and laps from the records naming it), or one imported.</summary>
    internal sealed class Item
    {
        public required FileInfo File { get; init; }
        public required string Key { get; init; }
        public required DateTime Started { get; init; }
        public required string Title { get; init; }
        public string? Driver { get; init; }
        public string? Track { get; init; }
        public bool Imported => Driver != null;
        // (since 4.12.0, for the Session Manager)
        public string Game { get; init; } = "";
        public string TrackName { get; init; } = "";
        public string CarName { get; init; } = "";
        public int Laps { get; init; }
        /// <summary>The best lap's (or the race's) time, seconds; 0: none.</summary>
        public float Best { get; init; }
        public bool Incomplete { get; init; }
        /// <summary>(since 4.13.0) Its race video is there (an icon after its title).</summary>
        public bool HasVideo { get; init; }
        /// <summary>Where it starts (read once, for a race with no track).</summary>
        public (double X, double Z)? Start { get; set; }
        public bool StartRead { get; set; }
        public override string ToString() => Title;
    }

    /// <summary>(since 4.13.0) Session 2's line for no session: None, or no session to pick. As an <see cref="Item"/> to the boxes' template.</summary>
    private sealed record Blank(string Title)
    {
        public bool HasVideo => false;
        public override string ToString() => Title;
    }

    private sealed record Part(string Text, int From, int To, Lap? Lap, bool Best = false)
    {
        public override string ToString() => Text;
    }

    /// <param name="card">The tab's card maker (id, title, body, whether the body fills the card, buttons top right): each
    /// folds, kept.</param>
    /// <param name="row">(since 4.7.0) Cards side by side, the last one's blue chevron folding or opening them all.</param>
    public SessionsView(AnalysisApplet applet, Func<string, string, UIElement, bool, UIElement[], Border> card, Action<Border[]> row)
    {
        this.applet = applet;
        this.card = card;
        this.row = row;
        Build();
        ShowSession(null);
    }

    /// <summary>The cards, top to bottom.</summary>
    public List<UIElement> Cards { get; } = new();

    // ---------------------------------------------------------------- the sessions

    private List<Item> items = new();

    private static string KeyOf(FileInfo f) =>
        f.DirectoryName != null && f.DirectoryName.TrimEnd('\\').Equals(SessionLog.ImportedFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? "Imported\\" + f.Name : f.Name;

    private static List<FileInfo> ImportedFiles()
    {
        try
        {
            var dir = new DirectoryInfo(SessionLog.ImportedFolder);
            return dir.Exists ? dir.GetFiles("*.csv").ToList() : new();
        }
        catch { return new(); }
    }

    /// <summary>(since 4.13.0) A session's moment in its title and the picker: the month and day (no year) and the time.</summary>
    internal static string When(DateTime t) => t.ToString(WhenFormat, CultureInfo.CurrentCulture);

    private static readonly string WhenFormat = System.Text.RegularExpressions.Regex.Replace(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern, "[^Md]*y+[^Md]*", "")
        + " " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;

    private static bool VideoThere(string key) => RaceVideos.Of(key)?.Watchable == true;

    /// <summary>The sessions again (a lap or race kept, a track renamed): the races' logs that are still there, newest
    /// first, then the imported ones.</summary>
    public void Refresh()
    {
        var names = applet.Settings;
        var mine = new List<Item>();
        var races = applet.Store.Races.Where(r => r.Session != null).GroupBy(r => r.Session!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // (since 4.10.14) the races left before their finish and kept: their sessions marked incomplete
        var unfinished = applet.Store.Unfinished.Where(r => r.Session != null && !races.ContainsKey(r.Session)).GroupBy(r => r.Session!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var (k, r) in unfinished) races.TryAdd(k, r);
        foreach (var g in applet.Store.All.Where(l => l.Session != null).GroupBy(l => l.Session!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Laps: g.ToList())).Concat(races.Keys.Where(k => applet.Store.All.All(l => !k.Equals(l.Session, StringComparison.OrdinalIgnoreCase))).Select(k => (Key: k, Laps: new List<LapRecord>()))))
        {
            var file = new FileInfo(Path.Combine(SessionLog.Folder, g.Key));
            if (!file.Exists) continue;
            races.TryGetValue(g.Key, out var race);
            string game = race?.Game ?? g.Laps[0].Game, track = race?.Track ?? g.Laps[0].Track;
            int car = race?.Car ?? g.Laps[0].Car;
            var started = SessionLog.StartedOf(file);
            var best = g.Laps.Count > 0 ? g.Laps.Min(l => l.Time) : race?.Time ?? 0;
            // (an incomplete race's time is how long it ran, not a result)
            if (unfinished.ContainsKey(g.Key) && g.Laps.Count == 0) best = 0;
            mine.Add(new Item
            {
                File = file, Key = g.Key, Started = started, Track = track,
                Game = game, TrackName = names.TrackName(track), CarName = names.CarName(game, car),
                Laps = Math.Max(g.Laps.Count, race?.Laps ?? 0), Best = best, Incomplete = unfinished.ContainsKey(g.Key),
                HasVideo = VideoThere(g.Key),
                Title = $"{names.TrackName(track)} · {names.CarName(game, car)} · {When(started)}" + (best > 0 ? " · " + Blocks.LapTime(best) : "")
                    + (unfinished.ContainsKey(g.Key) ? " · " + T("INCOMPLETE") : ""),
            });
        }
        bool linked = false;
        var imported = ImportedFiles().Select(f =>
        {
            var head = SessionData.Head(f.FullName);
            var started = head.Started ?? SessionLog.StartedOf(f);
            string driver = string.IsNullOrWhiteSpace(head.Driver) ? T("Imported") : head.Driver;
            string key = KeyOf(f);
            // (since 4.15.0) its video on YouTube, as exported: linked as a race's (its sync kept)
            if (head.Video is { } v && RaceVideos.Of(key) == null)
            {
                RaceVideos.All.Videos[key] = new RaceVideoLink { File = "", YouTube = v.Url, Started = started, Offset = v.At };
                linked = true;
            }
            return new Item { File = f, Key = key, Started = started, Driver = driver, Title = driver + " · " + (string.IsNullOrWhiteSpace(head.Name) ? When(started) : head.Name),
                TrackName = head.Name ?? "", HasVideo = RaceVideos.Of(key)?.Watchable == true };
        }).ToList();
        if (linked) RaceVideos.All.Save();
        string? keep = (sessions.SelectedItem as Item)?.Key ?? settings.Selected;
        items = mine.OrderByDescending(i => i.Started).Concat(imported.OrderByDescending(i => i.Started)).ToList();
        filling = true;
        // (since 4.7.0 every race, other drivers' too, as Session 1: the same list)
        sessions.ItemsSource = items;
        var pick = items.FirstOrDefault(i => i.Key.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault(i => !i.Imported);
        sessions.SelectedItem = pick;
        thisSession.ItemsSource = items;
        thisSession.SelectedItem = items.FirstOrDefault(i => Same(i, mineItem)) ?? pick;
        if (thisSession.SelectedItem is Item kept && Same(kept, mineItem)) mineItem = kept;
        filling = false;
        FillOtherSessions();
        if (pick == null) { current = null; ShowSession(null); }
        else if (Same(pick, current)) current = pick;
        else if (Host.IsVisible) Open(pick);
        Listed?.Invoke();
    }

    /// <summary>(since 4.12.0) The sessions listed: the races' logs, newest first, then the imported ones.</summary>
    public IReadOnlyList<Item> Items => items;

    /// <summary>(since 4.12.0) The sessions listed again (Refresh).</summary>
    public event Action? Listed;

    /// <summary>(since 4.12.0) The session analysed (or being read).</summary>
    public Item? Analysed => opening ?? current;

    /// <summary>(since 4.13.2) A session's still being read.</summary>
    public bool Reading => opening != null;

    /// <summary>(since 4.12.0) A session from the Session Manager analysed, as if picked in the Session box.</summary>
    public void Load(Item item)
    {
        wantLap = null;
        Pick(item);
    }

    /// <summary>The tab shown: the session picked read, if it isn't yet.</summary>
    public void Shown()
    {
        if (sessions.SelectedItem is Item i && !Same(i, current) && !Same(i, opening)) Open(i);
    }

    /// <summary>A lap, best lap or race clicked: its race analysed, that lap picked in This (null: the best); the cards
    /// brought into view unless <paramref name="stay"/> (since 4.13.0, the tables: the page stays where it is). False: it
    /// has no session log.</summary>
    public bool Open(string? session, int? lap, bool stay = false)
    {
        if (session == null || items.FirstOrDefault(i => !i.Imported && i.Key.Equals(session, StringComparison.OrdinalIgnoreCase)) is not { } item) return false;
        if (!stay) headCard.BringIntoView();
        if (Same(item, current) && analysis != null)
        {
            SelectLap(thisPart, lap);
            return true;
        }
        wantLap = lap;
        if (Same(item, opening)) return true;
        Pick(item);
        return true;
    }

    /// <summary>A session put in both boxes (the title's and Session 1) and analysed.</summary>
    private void Pick(Item item)
    {
        filling = true;
        sessions.SelectedItem = item;
        thisSession.SelectedItem = item;
        filling = false;
        Open(item);
    }

    /// <summary>A lap or race to compare with: put in With (this one stays). False: it has no session log.</summary>
    public bool CompareWith(string? session, int? lap)
    {
        if (session == null || items.FirstOrDefault(i => i.Key.Equals(session, StringComparison.OrdinalIgnoreCase)) is not { } item) return false;
        _ = PickOther(item, lap);
        return true;
    }

    /// <summary>Picks a lap (its number in the race, 1 the first) in a part box; none, or not there: the best.</summary>
    private void SelectLap(ComboBox box, int? lap)
    {
        var parts = box.Items.OfType<Part>().ToList();
        if (parts.Count == 0) return;
        box.SelectedItem = (lap is { } n ? parts.FirstOrDefault(p => p.Lap?.Number == n - 1) : null) ?? DefaultPart(parts);
    }

    /// <summary>The session being read.</summary>
    private Item? opening;

    private async void Open(Item item)
    {
        opening = item;
        settings.Selected = item.Key;
        settings.Save();
        loading?.Cancel();
        var cancel = loading = new CancellationTokenSource();
        // (in the line that's there: the page under it mustn't move)
        about.Text = T("Analysing…");
        PedalAnalysis? a = null;
        string? problem = null;
        try { a = await Load(item.File); }
        catch (Exception ex) { problem = ex.Message; }
        if (cancel.IsCancellationRequested) return;
        opening = null;
        current = item;
        if (a == null) { ShowSession(null, F("The session couldn't be read: {0}", problem)); return; }
        ShowSession(a);
    }

    /// <summary>A session analysed, kept while its file is unchanged (the compare picks it again).</summary>
    private async Task<PedalAnalysis> Load(FileInfo file)
    {
        file.Refresh();
        string key = file.FullName;
        bool leftFoot = applet.Settings.LeftFootBraking;
        if (loaded.TryGetValue(key, out var a) && a.Size == file.Length && a.LeftFoot == leftFoot) return a;
        a = await Task.Run(() => new PedalAnalysis(SessionData.Load(file.FullName), leftFoot) { Size = file.Length });
        if (loaded.Count > 6) loaded.Clear();
        loaded[key] = a;
        return a;
    }

    private Item? current;

    /// <summary>(since 4.13.3) Left Foot Braking switched: the session shown (and Session 2) analysed again.</summary>
    public void Reanalyse()
    {
        loaded.Clear();
        if (current is { } c && opening == null) { current = null; Pick(c); }
    }

    private static bool Same(Item? a, Item? b) => a != null && b != null && a.Key.Equals(b.Key, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- the session

    private void Build()
    {
        var head = new StackPanel();
        about.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        head.Children.Add(about);
        head.Children.Add(scores);
        head.Children.Add(split);
        head.Children.Add(splitLegend);
        head.Children.Add(numbers);
        System.Windows.Automation.AutomationProperties.SetName(sessions, T("Session"));
        sessions.ToolTip = T("Session");
        sessions.ItemTemplate = SessionTemplate();
        sessions.SelectionChanged += (_, _) => { if (!filling && sessions.SelectedItem is Item i) { wantLap = null; Pick(i); } };
        // (since 4.10.12) the session picked deleted, with its race video
        var delete = IconButton("", T("Delete this session and its video"));
        delete.Margin = new Thickness(6, 0, 4, 0);
        ConfirmButton.ArmIcon(delete, () => { if (sessions.SelectedItem is Item i) Delete(i); });
        // (since 4.13.1) Current Location's window (since 4.13.2 a compass; its words before)
        var place = IconButton("", T("Current Location"));
        place.Content = Compass();
        place.Margin = new Thickness(0, 0, 4, 0);
        place.Click += (_, _) => LocationAsked?.Invoke(Host);
        // (since 4.7.0 here, an icon; Compare's Export… button before)
        var export = IconButton("", T("Export this lap (or session) to send to someone"));
        export.Margin = new Thickness(0, 0, 4, 0);
        export.Click += (_, _) => Export();
        // (since 4.12.0) the Session Manager (since 4.13.3 the Analysis Manager): every session, its disk use, loaded from there
        var manage = IconButton("", T("Analysis Manager"));
        manage.Click += (_, _) => SessionManager.Show(Host, this);
        headCard = card("Session", T("Session"), head, false, new UIElement[] { sessions, delete, place, export, manage });
        headCard.IsVisibleChanged += (_, _) => { if (headCard.IsVisible) Shown(); };
        Cards.Add(headCard);

        // (since 4.6.2) Compare and the lines both runs took; since 4.7.0 the Trace too (Session 1 alone, Session 2 None)
        compareChart.Hovered += x =>
        {
            // (since 4.10.3 off the chart: the cursor's moment again)
            bool off = double.IsNaN(x);
            lineMap.HoverAt(off ? compareChart.PlayX : x);
            // (since 4.14.1) the friction circle's dot at that moment too
            if (lastA != null) FrictionAt(off ? player.Position : lastA.From + lastA.IndexAt(x));
            // (since 4.8.0) the race's video at that moment (since 4.10.3 not while it plays)
            if (player.Playing || lastA == null) return;
            if (!off) video.ShowSample(lastA.From + lastA.IndexAt(x));
            else if (!double.IsNaN(player.Position)) video.ShowSample(player.Position);
            // (since 4.15.0) and Session 2's at the same place
            if (bothVideos) video2.ShowSample(off ? SampleOfB(player.Position) : BAt(x));
        };
        // (since 4.10.3) a click puts the cursor there; playing moves it, the video and the map with it (since 4.15.3
        // waiting while a YouTube video buffers)
        player.Waiting = () => video.Buffering || (bothVideos && video2.Buffering);
        compareChart.Clicked += x => { if (lastA != null) player.Seek(lastA.From + lastA.IndexAt(x)); };
        player.Moved += (i, rate, jumped) =>
        {
            if (lastA == null) return;
            double x = lastA.XAt(i);
            compareChart.PlayAt(x);
            if (rate != 0) compareChart.Follow(x, rate > 0);
            if (!compareChart.IsMouseOver) { lineMap.HoverAt(x); FrictionAt(i); }
            if (!fromVideo) video.Follow(i, rate, jumped);
            if (bothVideos) video2.Follow(SampleOfB(i) is var j && !double.IsNaN(j) ? j : lastB?.To ?? double.NaN, double.IsNaN(SampleOfB(i)) ? 0 : rate, jumped);
            if (videoRun == null) return;
            double vx = videoRun.XAt(i);
            videoChart.PlayAt(vx);
            if (rate != 0) videoChart.Follow(vx, rate > 0);
        };
        // (since 4.10.15) the section to loop, set from the charts' menus, shown on them
        player.Changed += () =>
        {
            double X(Run? r, double i) => r == null || double.IsNaN(i) ? double.NaN : r.XAt(i);
            compareChart.SetLoop(X(lastA, player.LoopFrom), X(lastA, player.LoopTo));
            videoChart.SetLoop(X(videoRun, player.LoopFrom), X(videoRun, player.LoopTo));
        };
        // (since 4.14.1) a pause hidden or shown on either chart: both laid out again, the cursor kept at its moment
        void Relaid()
        {
            compareChart.Relayout();
            videoChart.Relayout();
            player.Relaid();
        }
        compareChart.PausesChanged += Relaid;
        videoChart.PausesChanged += Relaid;
        LoopMenu(compareChart, () => compareChart.MenuX, () => lastA);
        LoopMenu(videoChart, () => videoChart.MenuX, () => videoRun);
        // (since 4.10.9) the Video's timeline as Compare's chart: a click puts the cursor there, the mouse shows the
        // video and the map at its moment
        videoChart.Clicked += x => { if (videoRun != null) player.Seek(videoRun.From + videoRun.IndexAt(x)); };
        videoChart.Hovered += x =>
        {
            if (lastA == null || videoRun == null) return;
            bool off = double.IsNaN(x);
            double i = off ? player.Position : videoRun.From + videoRun.IndexAt(x);
            lineMap.HoverAt(off ? compareChart.PlayX : double.IsNaN(i) ? double.NaN : lastA.XAt(i));
            if (!player.Playing && !double.IsNaN(i)) video.ShowSample(i);
            if (!player.Playing && bothVideos) video2.ShowSample(SampleOfB(i));
        };
        // (since 4.8.0) the video playing, or moved in its window: where it is on the chart and the map (since 4.10.3
        // the cursor, Compare's playback stopped)
        video.Shown += i =>
        {
            if (double.IsNaN(i) || lastA == null || i < lastA.From || i > lastA.To) return;
            fromVideo = true;
            player.Play(0);
            player.Seek(i);
            fromVideo = false;
        };
        // (since 4.15.4) a click on either video plays or pauses Compare (YouTube's own play, under it, played a moment
        // and was paused again to keep in step)
        void PlayOrPause() { if (lastA != null) player.Play(player.Playing ? 0 : 1); }
        video.Clicked += PlayOrPause;
        video2.Clicked += PlayOrPause;
        RaceVideos.Changed += ShowVideos;
        // (since 4.15.0) a race video's name and description on YouTube, from its session
        RaceUploads.Describe = key => items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } i ? null
            : (string.Join(" · ", new[] { i.TrackName, i.CarName, i.Best > 0 ? Blocks.LapTime(i.Best) : "", i.Game }.Where(t => t.Length > 0)),
               string.Join("\n", new[]
               {
                   i.Game, F("Track: {0}", i.TrackName), F("Car: {0}", i.CarName), i.Laps > 0 ? F("Laps: {0}", i.Laps) : "",
                   i.Best > 0 ? F("Best: {0}", Blocks.LapTime(i.Best)) : "", i.Started.ToString("f", CultureInfo.CurrentCulture),
               }.Where(t => t.Length > 0)));
        compareChart.ZoneClicked += z => Pick(z, fromChart: true);
        Cards.Add(BuildCompare());

        // the zones lined up at the brake point and at the release (since 4.6.2 a card each; a drop-down picked one
        // before), and the throttle applications, over each other; then (since 4.6.10) the gear changes; since 4.13.0 a
        // row with the Tips
        brakeOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        releaseOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        throttleOverlay.Picked += o => { if (o is ThrottleMove m) PickThrottle(m); };
        shiftChart.Picked += s => ShowMoment(s.At - SessionData.Hz, s.At + SessionData.Hz, null, s.At);
        var five = new Grid();
        var fiveCards = new[]
        {
            card("BrakesCompared", T("Brake Point"), brakeOverlay, true, Array.Empty<UIElement>()),
            card("ReleasesCompared", T("Brake Release"), releaseOverlay, true, Array.Empty<UIElement>()),
            card("ThrottleCompared", T("Throttle"), throttleOverlay, true, Array.Empty<UIElement>()),
            card("Shifting", T("Shifting"), shiftChart, true, Array.Empty<UIElement>()),
            card("Tips", T("Tips"), tips, false, Array.Empty<UIElement>()),
        };
        for (int i = 0; i < fiveCards.Length; i++)
        {
            if (i > 0) five.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            five.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 4 ? 1.5 : 1, GridUnitType.Star) });
            Grid.SetColumn(fiveCards[i], i * 2);
            five.Children.Add(fiveCards[i]);
        }
        Cards.Add(five);
        row(fiveCards);
    }

    /// <summary>A chip for what a chart shows, kept.</summary>
    private ToggleButton Chip(string text, bool on, Action<bool> set)
    {
        var c = Ui.Chip(text, on);
        c.Margin = new Thickness(0, 0, 4, 0);
        Ui.OnToggle(c, v => { set(v); settings.Save(); friction.InvalidateVisual(); });
        return c;
    }

    /// <summary>(since 4.10.12) A session deleted: its log, and its race video (the file and its link). The laps and
    /// race it had stay in the tables.</summary>
    internal void Delete(Item i) => Delete(new[] { i });

    /// <summary>(since 4.13.1) Several at once (the Session Manager's), listed again once at the end; those that can't be
    /// deleted are said together.</summary>
    internal void Delete(IReadOnlyList<Item> list)
    {
        var failed = new List<string>();
        foreach (var i in list)
        {
            try { i.File.Delete(); }
            catch (Exception ex)
            {
                failed.Add(ex.Message);
                continue;
            }
            loaded.Remove(i.File.FullName);
            string? video = null;
            if (!i.Imported && RaceVideos.All.Videos.Remove(i.Key, out var link))
            {
                // (since 4.15.0 none when only on YouTube: that copy stays)
                video = link.File.Length > 0 ? link.File : null;
                // (the video shown lets go of it)
                RaceVideos.All.Save();
            }
            Logging.Log.Here.Info($"Analysis: session deleted: {i.Key}{(video != null ? ", and its video " + Path.GetFileName(video) : "")}");
            if (video != null)
                Task.Run(async () =>
                {
                    for (int k = 0; k < 20 && File.Exists(video); k++)
                    {
                        try { File.Delete(video); return; }
                        catch { await Task.Delay(1000); }
                    }
                    if (File.Exists(video)) Logging.Log.Here.Warn($"Analysis: the session's video couldn't be deleted: {video}");
                });
        }
        if (failed.Count > 0) MessageBox.Show(Window.GetWindow(Host), F("It couldn't be deleted: {0}", string.Join("\n", failed.Distinct())), T("Analysis"));
        Refresh();
    }

    /// <summary>(since 4.13.2) A compass, in the colour of the text around it (the icon font has none).</summary>
    private static UIElement Compass()
    {
        var colour = new Binding { Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
        var ring = new System.Windows.Shapes.Ellipse { StrokeThickness = 1.3 };
        ring.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, colour);
        var needle = new System.Windows.Shapes.Path { Data = Geometry.Parse("M10.8,3.2 L8.2,8.2 L3.2,10.8 L5.8,5.8 Z") };
        needle.SetBinding(System.Windows.Shapes.Shape.FillProperty, colour);
        return new Grid { Width = 14, Height = 14, Children = { ring, needle } };
    }

    /// <summary>An icon button, its name as its tooltip.</summary>
    private static Button IconButton(string glyph, string name)
    {
        var b = SimEffects.BassShaker.VolumeControl.IconButton(glyph);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    private void ShowSession(PedalAnalysis? a, string? problem = null)
    {
        analysis = a;
        friction.Analysis = a;
        scores.Children.Clear();
        numbers.Children.Clear();
        tips.Children.Clear();
        split.Children.Clear();
        split.ColumnDefinitions.Clear();
        splitLegend.Children.Clear();
        ShowOverlays();
        UseThis(current, a, wantLap);
        wantLap = null;
        if (a == null)
        {
            about.Text = problem ?? (items.Count == 0 ? T("No races yet") : "");
            return;
        }
        int ups = a.Throttle.Count(m => m.Up);
        var parts = new List<string> { a.Data.Started.ToString("f") };
        if (current?.Imported == true) parts.Insert(0, current.Driver!);
        if (!string.IsNullOrEmpty(a.Data.Car)) parts.Add(a.Data.Car);
        parts.Add(F("{0} long, {1} driving", SessionChart.Clock(a.Data.Seconds), SessionChart.Clock(a.ActiveS)));
        int laps = a.Data.Laps.Count(l => l.Complete);
        if (laps > 0) parts.Add(F("{0}, best {1}", P(laps, "{0} lap", "{0} laps"), Lap.LapClock(a.Data.BestLap!.Seconds)));
        parts.Add(P(a.Brakes.Count, "{0} brake zone", "{0} brake zones"));
        parts.Add(P(ups, "{0} throttle application", "{0} throttle applications"));
        about.Text = string.Join(" · ", parts);

        Tile(T("Overall"), a.OverallScore, T("The scores below together, the brake release counting most"));
        Tile(T("Brake application"), a.BrakeApplyScore, T("Firm and in one movement"));
        Tile(T("Brake release"), a.BrakeReleaseScore, T("Gradual and in one movement"));
        Tile(T("Throttle application"), a.ThrottleApplyScore, T("Progressive and in one movement"));
        Tile(T("Throttle release"), a.ThrottleReleaseScore, T("Clean and in one movement"));
        Tile(T("Steering"), a.SteeringScore, T("Fewer small corrections score higher"));
        Tile(T("Consistency"), a.ConsistencyScore, T("How alike the brake zones are"));

        void Part(double pct, Color colour, string name)
        {
            if (pct <= 0) return;
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            var r = new Border { Background = PedalColours.Frozen(colour), ToolTip = F("{0}: {1:0.0}%", name, pct) };
            Grid.SetColumn(r, split.ColumnDefinitions.Count - 1);
            split.Children.Add(r);
            var dot = new Border { Width = 10, Height = 10, Background = PedalColours.Frozen(colour), Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(2) };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(dot);
            item.Children.Add(new TextBlock { Text = F("{0} {1:0}%", name, pct), FontSize = 12 });
            splitLegend.Children.Add(item);
        }
        Part(a.FullThrottlePct, PedalColours.Throttle, T("Full throttle"));
        Part(a.PartThrottlePct, Color.FromRgb(0x2A, 0x8A, 0x4E), T("Part throttle"));
        Part(a.BrakingPct, PedalColours.Brake, T("Braking"));
        Part(a.OverlapPct, PedalColours.Warning, T("Both"));
        Part(a.CoastPct, Color.FromRgb(0x80, 0x80, 0x80), T("Coasting"));

        var firm = a.Brakes.Where(z => z.Peak >= 0.15).ToList();
        if (firm.Count > 0)
        {
            Number(T("Brake peak"), F("{0:0}%", firm.Average(z => z.Peak) * 100), T("The average of the zones' highest pressure"));
            Number(T("Brake apply"), F("{0:0} ms", firm.Average(z => z.ApplyMs)), T("On average, from 10% to 90% of the peak"));
            Number(T("Brake release"), F("{0:0.00} s", firm.Average(z => z.ReleaseS)), T("On average, from the level held to letting go"));
            var trail = firm.Where(z => z.Trail != null).ToList();
            if (trail.Count > 0) Number(T("Trail braking"), F("{0:0}%", trail.Average(z => z.TrailPct)), T("On average, how much of the release was with the wheel turned"));
            var coast = firm.Where(z => z.Coast != null).ToList();
            if (coast.Count > 0) Number(T("Coast to throttle"), F("{0:0.00} s", coast.Average(z => z.CoastS)), T("On average, from letting go of the brake to the throttle"));
        }
        if (!double.IsNaN(a.ThrottleToBrakeS)) Number(T("Throttle to brake"), F("{0:0.00} s", a.ThrottleToBrakeS), T("On average, from lifting off the throttle to the brake going on"));
        var apps = a.Throttle.Where(m => m.Up).ToList();
        if (apps.Count > 0) Number(T("Throttle apply"), F("{0:0} ms", apps.Average(m => m.Ms)), T("On average, from 10% to 90% of the way"));
        Number(T("Throttle stabs"), a.Stabs.ToString(), T("On and straight off again"));
        if (a.HasSteer) Number(T("Steering corrections"), F("{0:0} a minute", a.SteerCorrectionsPerMin), T("Small turns back of the wheel, inside a second"));
        if (a.Data.HasG || a.Data.HasSpeed)
        {
            int crashes = a.Incidents.Count(x => x.Crash);
            Number(T("Impacts"), (a.Incidents.Count - crashes).ToString(), T("Hitting a car or wall"));
            Number(T("Crashes"), crashes.ToString(), F("Hits of {0:0.0} g or more", Incident.CrashG));
        }

        ShowTips(a);
    }

    /// <summary>The tips (since 4.6.2) in a box per colour, no titles: the red ones first, then (since 4.7.0) the orange,
    /// the white, then the green.</summary>
    private void ShowTips(PedalAnalysis a)
    {
        foreach (var group in new[] { a.Tips.Where(t => t.Bad), a.Tips.Where(t => t.Warn && !t.Bad), a.Tips.Where(t => !t.Bad && !t.Warn && !t.Good), a.Tips.Where(t => t.Good && !t.Bad && !t.Warn) })
        {
            var list = group.ToList();
            if (list.Count == 0) continue;
            bool bad = list[0].Bad, warn = !bad && list[0].Warn, good = !bad && !warn && list[0].Good;
            var box = new StackPanel();
            var frame = new Border
            {
                Child = box, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 10, 8), Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = bad ? PedalColours.Frozen(PedalColours.Brake, 0x70) : warn ? PedalColours.Frozen(PedalColours.Warning, 0x70) : good ? PedalColours.Frozen(PedalColours.Throttle, 0x70) : PedalColours.Frozen(Colors.Gray, 0x60),
                Background = bad ? PedalColours.Frozen(PedalColours.Brake, 0x14) : warn ? PedalColours.Frozen(PedalColours.Warning, 0x14) : good ? PedalColours.Frozen(PedalColours.Throttle, 0x14) : Brushes.Transparent,
            };
            foreach (var tip in list) box.Children.Add(TipRow(a, tip));
            tips.Children.Add(frame);
        }
        if (a.Tips.Count == 0) tips.Children.Add(Ui.Note(T("Nothing stands out: keep it up."), 4));
    }

    private static DockPanel TipRow(PedalAnalysis a, Tip tip)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(bullet, Dock.Left);
        row.Children.Add(bullet);
        if (!double.IsNaN(tip.Seconds) && tip.Seconds >= 0.005)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = PedalColours.Frozen(tip.Bad ? PedalColours.Brake : PedalColours.Warning, 0x30),
                ToolTip = a.PerLap ? T("About how much time this could save a lap") : T("About how much time this could have saved this session"),
                Child = new TextBlock
                {
                    Text = a.PerLap ? F("{0:0.0#} s/lap", tip.Seconds) : F("{0:0.0#} s", tip.Seconds),
                    FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                    Foreground = PedalColours.Frozen(tip.Bad ? PedalColours.Brake : PedalColours.Warning),
                },
            };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
        }
        var text = new TextBlock { Text = tip.Text, TextWrapping = TextWrapping.Wrap };
        if (tip.Bad) { text.Foreground = PedalColours.Frozen(PedalColours.Brake); text.FontWeight = FontWeights.SemiBold; bullet.Foreground = text.Foreground; }
        else if (tip.Warn) { text.Foreground = PedalColours.Frozen(PedalColours.Warning); bullet.Foreground = text.Foreground; }
        else if (tip.Good) { text.Foreground = PedalColours.Frozen(PedalColours.Throttle); bullet.Foreground = text.Foreground; }
        row.Children.Add(text);
        return row;
    }

    private void Tile(string name, double score, string tip)
    {
        var b = new Border { Width = 128, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ToolTip = tip };
        b.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var s = new StackPanel();
        var value = new TextBlock { Text = double.IsNaN(score) ? "–" : score.ToString("0"), FontSize = 26, FontWeight = FontWeights.SemiBold };
        if (!double.IsNaN(score)) value.Foreground = PedalColours.Frozen(score >= 85 ? PedalColours.Throttle : score >= 65 ? PedalColours.Warning : PedalColours.Brake);
        s.Children.Add(value);
        var label = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(label);
        b.Child = s;
        scores.Children.Add(b);
    }

    private void Number(string name, string value, string tip)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 22, 6), ToolTip = tip };
        var label = new TextBlock { Text = name, FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(label);
        s.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold });
        numbers.Children.Add(s);
    }

    private void ShowOverlays()
    {
        var a = analysis;
        if (a == null)
        {
            brakeOverlay.Show(null, Array.Empty<OverlayChart.Curve>(), PedalColours.Brake, 0.5, 2.5);
            releaseOverlay.Show(null, Array.Empty<OverlayChart.Curve>(), PedalColours.Brake, 0.5, 2.5);
            throttleOverlay.Show(null, Array.Empty<OverlayChart.Curve>(), PedalColours.Throttle, 0.3, 2);
            shiftChart.Show(null);
            return;
        }
        // (since 4.7.0 no share beside its title: the chart says it)
        shiftChart.Show(a.Shifting);
        var zones = a.Brakes.Where(z => z.Peak >= 0.15).ToList();
        brakeOverlay.Show(a.SmoothBrake, zones.Select(z => new OverlayChart.Curve(z.Start, z.End, z)).ToList(), PedalColours.Brake, 0.3, 3);
        releaseOverlay.Show(a.SmoothBrake, zones.Select(z => new OverlayChart.Curve(z.ReleaseFrom, z.End, z)).ToList(), PedalColours.Brake, 1, 2);
        throttleOverlay.Show(a.SmoothThrottle, a.Throttle.Where(m => m.Up).Select(m => new OverlayChart.Curve(m.Start, m.End, m)).ToList(), PedalColours.Throttle, 0.3, 2);
    }

    private void Pick(BrakeZone z, bool fromChart = false)
    {
        brakeOverlay.Selected = releaseOverlay.Selected = z;
        brakeOverlay.InvalidateVisual();
        releaseOverlay.InvalidateVisual();
        if (!fromChart) ShowMoment(z.Start, z.End, z);
    }

    private void PickThrottle(ThrottleMove m)
    {
        if (m.Up) { throttleOverlay.Selected = m; throttleOverlay.InvalidateVisual(); }
        ShowMoment(m.Start, m.End);
    }

    /// <summary>(since 4.7.0, the Trace's in Compare's chart) A moment of the session analysed shown there, a line at it:
    /// Session 1's part switched to the lap it's in (or the whole session) if it's outside the one picked.</summary>
    private void ShowMoment(int from, int to, BrakeZone? zone = null, int? at = null)
    {
        if (mine == null || !ReferenceEquals(mine, analysis)) return;
        if (thisPart.SelectedItem is not Part p || from < p.From || to > p.To)
        {
            var parts = thisPart.Items.OfType<Part>().ToList();
            var fit = parts.FirstOrDefault(q => q.Lap != null && q.From <= from && q.To >= to) ?? parts.FirstOrDefault();
            if (fit == null) return;
            filling = true;
            thisPart.SelectedItem = fit;
            filling = false;
            ShowCompare();
        }
        compareChart.ShowSamples(from, to, zone, at);
        player.Seek(at ?? from);
        if (compareChart.Total > 0 && lastA != null) lineMap.ShowRange(lastA.XOf(from), lastA.XOf(to));
    }

    // ---------------------------------------------------------------- compare

    private Border BuildCompare()
    {
        var s = new StackPanel();
        // (since 4.7.0 an icon; the Driver box went: an export carries the name kept before, else Windows' user name)
        var import = IconButton("", T("Import someone else's lap or session"));
        import.Margin = new Thickness(0, 0, 4, 0);
        import.Click += (_, _) => Import();

        // (since 4.7.0) Session 1 (the session analysed) and Session 2 (a race on the same track, any car, anyone's, or
        // None: the Trace), each a row: its race, its lap, then its channels, so the rows line up (since 4.9.1 the
        // channels at the right)
        void Row(string label, int slot, ComboBox session, ComboBox part, StackPanel channels, params UIElement[] after)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            // (since 4.13.0 a click on it opens a window to find a session in)
            row.Children.Add(new Border { Width = 44, VerticalAlignment = VerticalAlignment.Center, Child = PickerLink(label, slot) });
            session.Margin = part.Margin = new Thickness(0, 0, 6, 0);
            session.ItemContainerStyle = WholeLines();
            session.ItemTemplate = SessionTemplate();
            session.SetBinding(FrameworkElement.ToolTipProperty, new Binding("SelectedItem.Title") { RelativeSource = RelativeSource.Self });
            row.Children.Add(session);
            row.Children.Add(part);
            channels.VerticalAlignment = VerticalAlignment.Center;
            channels.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(channels, Dock.Right);
            line.Children.Add(channels);
            foreach (var x in after) row.Children.Add(x);
            line.Children.Add(row);
            s.Children.Add(line);
        }
        System.Windows.Automation.AutomationProperties.SetName(thisSession, T("Session 1"));
        System.Windows.Automation.AutomationProperties.SetName(thisPart, T("Session 1 lap"));
        System.Windows.Automation.AutomationProperties.SetName(otherSession, T("Session 2"));
        System.Windows.Automation.AutomationProperties.SetName(otherPart, T("Session 2 lap"));
        thisPart.ItemTemplate = otherPart.ItemTemplate = PartTemplate();
        compareStatus.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        deltaChip.VerticalAlignment = VerticalAlignment.Center;
        deltaChip.Margin = new Thickness(14, 0, 0, 0);
        Row(T("S1"), 1, thisSession, thisPart, thisChannels, compareStatus);
        Row(T("S2"), 2, otherSession, otherPart, otherChannels, deltaChip);
        thisSession.SelectionChanged += (_, _) => { if (!filling && thisSession.SelectedItem is Item i) { wantLap = null; Pick(i); } };
        thisPart.SelectionChanged += (_, _) => { if (!filling) ShowCompare(); };
        otherSession.SelectionChanged += async (_, _) => { if (!filling) await PickOther(); };
        otherPart.SelectionChanged += (_, _) => { if (!filling) ShowCompare(); };

        compareChart.Margin = new Thickness(0, 4, 0, 0);
        // (a channel left out of CompareChannels, from before 4.6.2: hidden in both runs; Clutch, new in 4.7.0, hidden at first)
        compareChart.ShowDelta = settings.CompareChannels.Contains(nameof(Channel.Delta));
        foreach (var c in Enum.GetValues<Channel>())
            if (c != Channel.Delta && !settings.CompareChannels.Contains(c.ToString())) { compareChart.Hidden.Add((c, 1)); compareChart.Hidden.Add((c, 2)); }
        foreach (var h in settings.CompareHidden)
            if (h.Split('.') is [var name, var run] && Enum.TryParse<Channel>(name, out var c) && int.TryParse(run, out int r)) compareChart.Hidden.Add((c, r));
        deltaChip.IsChecked = compareChart.ShowDelta;
        Ui.OnToggle(deltaChip, on => { compareChart.ShowDelta = on; SaveChannels(); compareChart.InvalidateVisual(); });
        compareChart.Combined = settings.CompareCombined;
        combineChip.IsChecked = compareChart.Combined;
        combineChip.Margin = new Thickness(0, 0, 4, 0);
        combineChip.VerticalAlignment = VerticalAlignment.Center;
        Ui.OnToggle(combineChip, on =>
        {
            compareChart.Combined = settings.CompareCombined = on;
            settings.Save();
            compareChart.InvalidateVisual();
        });
        // (since 4.13.3) Previous and Next Event and the marks' filter at its top right, in the marks' row
        foreach (var h in settings.HiddenMarkers)
            if (Enum.TryParse<MarkerKind>(h, out var mk)) compareChart.HiddenMarkers.Add(mk);
        videoChart.HiddenMarkers = compareChart.HiddenMarkers;
        var events = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 8, 0) };
        var previous = MiniButton("", T("Previous Event"));
        previous.Click += (_, _) => JumpEvent(-1);
        var next = MiniButton("", T("Next Event"));
        next.Click += (_, _) => JumpEvent(1);
        events.Children.Add(MarkFilter());
        events.Children.Add(previous);
        events.Children.Add(next);
        s.Children.Add(new Grid { Children = { compareChart, events } });
        // (since 4.6.9) where the part shown is in the whole (since 4.10.3 the whole drawn, the part framed); then playback
        var compareOverview = new CompareOverview(compareChart) { Margin = new Thickness(52, 4, 8, 0) };
        LoopMenu(compareOverview, () => compareOverview.MenuX, () => lastA);
        s.Children.Add(compareOverview);
        s.Children.Add(new PlaybackBar(player));
        videoChart.Hidden.Add((Channel.Clutch, 1));
        videoChart.Hidden.Add((Channel.Rpm, 1));
        videoChart.Hidden.Add((Channel.Gear, 1));
        video.SetVolume(settings.VideoVolume, settings.VideoMuted);
        video.VolumeChanged += () =>
        {
            settings.VideoVolume = video.Volume;
            settings.VideoMuted = video.Muted;
            settings.SaveSoon();
        };
        foreach (var h in settings.VideoHidden)
            if (Enum.TryParse<Channel>(h, out var vc)) videoChart.Hidden.Add((vc, 1));

        // (since 4.7.0) the race's video (since 4.8.0; its sync tools only in its own window), the Track Map and (since
        // 4.13.0, the corners table before) the friction circle; the video and the map pop out. Since 4.10.9 the video
        // first, half the width and 450 px high (half as high again), the map an eighth (taller than wide); the video's
        // window has its timeline under it
        System.Windows.Automation.AutomationProperties.SetName(lineMap, T("Track Map"));
        under = new Grid { Margin = new Thickness(0, 10, 0, 0), Height = 450 };
        // (since 4.15.0 the Video slot has Session 2's beside Session 1's when both have one: the map and the friction
        // circle make way, here and popped out)
        var slots = new FrameworkElement[]
        {
            PopOut(T("Video"), VideoPair(), popped => { video.Tools = popped; video2.Tools = popped; }, VideoTimeline(), 1000, 940),
            PopOut(T("Track Map"), lineMap), FrictionSlot(),
        };
        // (since 4.13.3 the gaps between them dragged to resize them, kept)
        var shares = settings.UnderWidths is { Count: 3 } kept && kept.All(w => w > 0) ? kept.ToArray() : new[] { 4.0, 1, 3 };
        for (int i = 0; i < slots.Length; i++)
        {
            if (i > 0)
            {
                under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                var splitter = Splitter();
                Grid.SetColumn(splitter, i * 2 - 1);
                splitter.DragCompleted += (_, _) =>
                {
                    settings.UnderWidths = Enumerable.Range(0, slots.Length).Select(k => Math.Round(under.ColumnDefinitions[k * 2].ActualWidth, 1)).ToList();
                    settings.Save();
                };
                under.Children.Add(splitter);
            }
            under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shares[i], GridUnitType.Star), MinWidth = 120 });
            Grid.SetColumn(slots[i], i * 2);
            under.Children.Add(slots[i]);
        }
        s.Children.Add(under);
        return card("Compare", T("Analysis"), s, false, new UIElement[] { import });
    }

    private Grid under = null!;
    private readonly RaceVideoView video2 = new();
    private readonly TextBlock videoLabel1 = VideoLabel(), videoLabel2 = VideoLabel();
    private readonly Grid videoPair = new();
    private bool bothVideos;
    private GridLength[]? underKept;

    private static TextBlock VideoLabel()
    {
        var t = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 30, 4), Visibility = Visibility.Collapsed };
        return t;
    }

    /// <summary>(since 4.15.0) The Video slot's content: Session 1's video, and Session 2's beside it when both have
    /// one, each with its name over it and its own volume and mute.</summary>
    private FrameworkElement VideoPair()
    {
        videoPair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        videoPair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        videoPair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        var one = new DockPanel { Children = { videoLabel1, video } };
        DockPanel.SetDock(videoLabel1, Dock.Top);
        var two = new DockPanel { Children = { videoLabel2, video2 }, Visibility = Visibility.Collapsed };
        DockPanel.SetDock(videoLabel2, Dock.Top);
        Grid.SetColumn(two, 2);
        videoPair.Children.Add(one);
        videoPair.Children.Add(two);
        System.Windows.Automation.AutomationProperties.SetName(video2, T("Session 2 video"));
        video2.SetVolume(settings.Video2Volume, settings.Video2Muted);
        video2.VolumeChanged += () =>
        {
            settings.Video2Volume = video2.Volume;
            settings.Video2Muted = video2.Muted;
            settings.SaveSoon();
        };
        return videoPair;
    }

    /// <summary>(since 4.15.0) Session 2's video beside Session 1's (the map and the friction circle hidden), or not.</summary>
    private void BothVideos(bool both, string label1 = "", string label2 = "")
    {
        videoLabel1.Text = "S1 · " + label1;
        videoLabel2.Text = "S2 · " + label2;
        videoLabel1.ToolTip = label1;
        videoLabel2.ToolTip = label2;
        if (both == bothVideos) return;
        bothVideos = both;
        videoLabel1.Visibility = videoLabel2.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
        // (since 4.15.4 the two the same: the names over them clear the pop-out button)
        video.Room = video2.Room = both ? 10 : 32;
        videoPair.ColumnDefinitions[1].Width = new GridLength(both ? 10 : 0);
        videoPair.ColumnDefinitions[2].Width = both ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        videoPair.Children[1].Visibility = both ? Visibility.Visible : Visibility.Collapsed;
        // (the map's and the friction circle's columns, and the splitters before them, to nothing and back)
        var cols = under.ColumnDefinitions;
        if (both)
        {
            underKept = cols.Select(c => c.Width).ToArray();
            for (int k = 1; k < cols.Count; k++) { cols[k].MinWidth = 0; cols[k].Width = new GridLength(0); }
        }
        else if (underKept != null)
        {
            for (int k = 1; k < cols.Count; k++) { cols[k].Width = underKept[k]; if (k % 2 == 0) cols[k].MinWidth = 120; }
        }
        foreach (UIElement child in under.Children)
            if (Grid.GetColumn(child) > 0) child.Visibility = both ? Visibility.Collapsed : Visibility.Visible;
    }

    private (string One, string Two) videoLabels = ("", "");

    /// <summary>Session 1's race video, and (since 4.15.0) Session 2's beside it when both have one.</summary>
    private void ShowVideos()
    {
        video.Show(mine?.Data, mineItem?.Key);
        var otherItem = lastB != null ? otherSession.SelectedItem as Item : null;
        bool both = lastA != null && otherItem != null && RaceVideos.Of(mineItem?.Key)?.Watchable == true && RaceVideos.Of(otherItem.Key)?.Watchable == true;
        video2.Show(both ? other?.Data : null, both ? otherItem!.Key : null);
        BothVideos(both, videoLabels.One, videoLabels.Two);
    }

    /// <summary>(since 4.15.0) Session 2's moment at Session 1's (a sample of each; the same distance, or time, along
    /// their runs), NaN past Session 2's ends.</summary>
    private double SampleOfB(double sampleA) =>
        lastA == null || lastB == null || double.IsNaN(sampleA) ? double.NaN : BAt(lastA.XAt(sampleA));

    /// <summary>(since 4.15.0) Session 2's moment at a place along the chart.</summary>
    private double BAt(double x) => lastB == null || double.IsNaN(x) || double.IsNaN(lastB.IndexAt(x)) ? double.NaN : lastB.From + lastB.IndexAt(x);

    /// <summary>(since 4.13.3) Previous Event's and Next Event's shortcuts.</summary>
    internal IReadOnlyList<Shared.Hotkeys.Hotkey> EventKeys => applet.EventKeys;

    /// <summary>(since 4.13.3) Next (1) or Previous (-1) Event: the cursor (else the run's start or end) moved to the
    /// next mark shown above Compare's chart, the video and map with it, its tooltip shown.</summary>
    internal void JumpEvent(int dir)
    {
        if (lastA == null) return;
        double at = !double.IsNaN(player.Position) ? player.Position : dir > 0 ? lastA.From - 1 : lastA.To + 1;
        if (compareChart.MarkFrom(at, dir) is not { } m) return;
        player.Seek(m.At);
        compareChart.Pin(m);
    }

    /// <summary>(since 4.13.3) A button as small as the marks' row (14 px high): an icon in an outline, and a small
    /// chevron after it for a drop-down.</summary>
    private static Button MiniButton(string glyph, string name, bool dropDown = false)
    {
        TextBlock Icon(string g, double size)
        {
            var t = new TextBlock { Text = g, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            return t;
        }
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { Icon(glyph, 9) } };
        if (dropDown) icons.Children.Add(new Border { Margin = new Thickness(3, 1, 0, 0), Child = Icon("", 7) });
        var outline = new Border { Height = 14, MinWidth = 20, Padding = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Child = icons };
        outline.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        outline.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        var b = new Button
        {
            Content = outline, Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
            Margin = new Thickness(3, 0, 0, 0), Height = 14, MinHeight = 0, MinWidth = 0, Cursor = Cursors.Hand, ToolTip = name, Focusable = false,
        };
        b.MouseEnter += (_, _) => outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        b.MouseLeave += (_, _) => outline.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    /// <summary>(since 4.13.3) The marks' filter: a drop-down of their kinds, each ticked to show it on Compare's chart
    /// (and the Video's), kept; its icon blue while some are hidden.</summary>
    private Button MarkFilter()
    {
        var b = MiniButton("", T("Events shown"), dropDown: true);
        var icon = (TextBlock)((StackPanel)((Border)b.Content).Child).Children[0];
        void Tint() => icon.SetResourceReference(TextBlock.ForegroundProperty, compareChart.HiddenMarkers.Count > 0 ? "AccentBrush" : "TextBrush");
        var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
        foreach (var kind in Enum.GetValues<MarkerKind>())
        {
            if (kind == MarkerKind.Impact) menu.Items.Add(new Separator());
            var item = new MenuItem
            {
                Header = new Marker(0, kind, "").Title, IsCheckable = true, StaysOpenOnClick = true, IsChecked = !compareChart.HiddenMarkers.Contains(kind),
                Icon = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = CompareChart.MarkerBrush(kind) },
            };
            void Changed()
            {
                if (item.IsChecked) compareChart.HiddenMarkers.Remove(kind); else compareChart.HiddenMarkers.Add(kind);
                settings.HiddenMarkers = compareChart.HiddenMarkers.Select(k => k.ToString()).OrderBy(k => k).ToList();
                settings.Save();
                Tint();
                compareChart.InvalidateVisual();
                videoChart.InvalidateVisual();
            }
            item.Checked += (_, _) => Changed();
            item.Unchecked += (_, _) => Changed();
            menu.Items.Add(item);
        }
        b.Click += (_, _) => menu.IsOpen = true;
        Tint();
        return b;
    }

    /// <summary>(since 4.13.3) A gap under Compare's chart dragged to resize the slots either side: a short grip in its
    /// middle.</summary>
    private static GridSplitter Splitter()
    {
        var grip = new FrameworkElementFactory(typeof(Border));
        grip.SetValue(FrameworkElement.WidthProperty, 3.0);
        grip.SetValue(FrameworkElement.HeightProperty, 36.0);
        grip.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
        grip.SetValue(UIElement.OpacityProperty, 0.45);
        grip.SetResourceReference(Border.BackgroundProperty, "TextSecondaryBrush");
        var back = new FrameworkElementFactory(typeof(Border));
        back.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        back.AppendChild(grip);
        var splitter = new GridSplitter
        {
            Width = 12, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = GridResizeDirection.Columns, ShowsPreview = false, Focusable = false, Cursor = Cursors.SizeWE, ToolTip = T("Drag to resize"),
            Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = back },
        };
        System.Windows.Automation.AutomationProperties.SetName(splitter, T("Drag to resize"));
        return splitter;
    }

    /// <summary>(since 4.13.0) The friction circle under Compare's chart, its own Brake and Throttle chips over it.</summary>
    private FrameworkElement FrictionSlot()
    {
        friction.ShowBrake = settings.FrictionBrake;
        friction.ShowThrottle = settings.FrictionThrottle;
        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                Chip(T("Brake"), settings.FrictionBrake, v => settings.FrictionBrake = friction.ShowBrake = v),
                Chip(T("Throttle"), settings.FrictionThrottle, v => settings.FrictionThrottle = friction.ShowThrottle = v),
            },
        };
        // (since 4.15.4) the g averaged over a moment, so a bumpy corner doesn't scatter it
        friction.Smooth = settings.FrictionSmooth;
        var smooth = new ComboBox { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Smoothing") };
        System.Windows.Automation.AutomationProperties.SetName(smooth, T("Smoothing"));
        foreach (double s in new[] { 0, 0.1, 0.25, 0.5, 1 })
            smooth.Items.Add(new ComboBoxItem { Content = s == 0 ? T("Smooth: off") : F("Smooth: {0} s", s), Tag = s });
        smooth.SelectedItem = smooth.Items.OfType<ComboBoxItem>().MinBy(i => Math.Abs((double)i.Tag - settings.FrictionSmooth));
        smooth.SelectionChanged += (_, _) =>
        {
            if (smooth.SelectedItem is not ComboBoxItem { Tag: double s }) return;
            friction.Smooth = settings.FrictionSmooth = s;
            settings.Save();
        };
        chips.Children.Add(smooth);
        DockPanel.SetDock(chips, Dock.Top);
        System.Windows.Automation.AutomationProperties.SetName(friction, T("Friction Circle"));
        return new DockPanel { Children = { chips, friction } };
    }

    /// <summary>(since 4.13.0) S1 or S2 ("Session 1" or "Session 2" until 4.13.2), outlined as the channels' but blue: a
    /// click opens the <see cref="SessionPicker"/>; since 4.15.4 a click highlights the session on Compare's chart
    /// (every line of it white, the other's faded; again: not) and a right-click opens the picker.</summary>
    private Button PickerLink(string label, int slot)
    {
        var text = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var outline = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), BorderThickness = new Thickness(1), Background = Brushes.Transparent, Child = text };
        outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        pickerOutlines[slot - 1] = outline;
        var link = new Button
        {
            Content = outline, Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("Click: highlight · Right-click: find a session"),
        };
        System.Windows.Automation.AutomationProperties.SetName(link, F("Highlight {0}", label));
        link.Click += (_, _) =>
        {
            compareChart.HighlightRun = compareChart.HighlightRun == slot ? null : slot;
            compareChart.Highlight = null;
            ShowLegend();
            compareChart.InvalidateVisual();
        };
        link.MouseRightButtonUp += (_, e) => { e.Handled = true; SessionPicker.Show(Host, this, slot); };
        return link;
    }

    /// <summary>(since 4.15.4) S1's and S2's outlines: filled while their session's highlighted.</summary>
    private readonly Border?[] pickerOutlines = new Border?[2];

    private void ShowPickerLinks()
    {
        for (int k = 0; k < 2; k++)
            if (pickerOutlines[k] is { } o)
                o.Background = compareChart.HighlightRun == k + 1 ? PedalColours.Frozen(Colors.White, 0x26) : Brushes.Transparent;
    }

    /// <summary>(since 4.13.0) The sessions a slot of Compare can have: every one for Session 1, Session 2's list for 2.</summary>
    internal IReadOnlyList<Item> ChoicesFor(int slot) => slot == 1 ? items : otherSession.Items.OfType<Item>().ToList();

    /// <summary>(since 4.13.0) The session in a slot of Compare.</summary>
    internal Item? ChosenFor(int slot) => (slot == 1 ? thisSession.SelectedItem : otherSession.SelectedItem) as Item;

    /// <summary>(since 4.13.0) A session put in a slot of Compare, from the picker.</summary>
    internal void Choose(int slot, Item item)
    {
        if (Same(item, ChosenFor(slot))) return;
        if (slot == 1) { Load(item); return; }
        _ = PickOther(otherSession.Items.OfType<Item>().FirstOrDefault(i => Same(i, item)) ?? item);
    }

    /// <summary>(since 4.13.0) Session 2's list changed (Session 1 did, or the sessions).</summary>
    public event Action? OthersListed;

    /// <summary>(since 4.10.9) Under the popped-out Video: Session 1's channels (a box each), its chart, the whole of it
    /// with the part shown framed, and playback, all as Compare's and moving with it.</summary>
    private FrameworkElement VideoTimeline()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        videoChannels.Margin = new Thickness(0, 0, 0, 4);
        videoChannels.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(videoChannels, Dock.Top);
        panel.Children.Add(videoChannels);
        var bar = new PlaybackBar(player);
        DockPanel.SetDock(bar, Dock.Bottom);
        panel.Children.Add(bar);
        var overview = new CompareOverview(videoChart) { Margin = new Thickness(52, 4, 8, 0) };
        LoopMenu(overview, () => overview.MenuX, () => videoRun);
        DockPanel.SetDock(overview, Dock.Bottom);
        panel.Children.Add(overview);
        System.Windows.Automation.AutomationProperties.SetName(videoChart, T("Timeline"));
        panel.Children.Add(videoChart);
        return panel;
    }

    /// <summary>(since 4.10.15) A chart's (or its whole's) right-click menu: the loop's start or end where it was
    /// clicked, or the loop cleared.</summary>
    private void LoopMenu(FrameworkElement on, Func<double> x, Func<Run?> run)
    {
        double At() => run() is { } r && !double.IsNaN(x()) ? r.From + r.IndexAt(x()) : double.NaN;
        var start = new MenuItem { Header = T("Set Start") };
        var end = new MenuItem { Header = T("Set End") };
        var clear = new MenuItem { Header = T("Clear Loop") };
        start.Click += (_, _) => { if (At() is var i && !double.IsNaN(i)) player.SetLoop(i, true); };
        end.Click += (_, _) => { if (At() is var i && !double.IsNaN(i)) player.SetLoop(i, false); };
        clear.Click += (_, _) => player.SetLoop(double.NaN, true);
        var menu = new ContextMenu { Items = { start, end, new Separator(), clear } };
        menu.Opened += (_, _) =>
        {
            start.IsEnabled = end.IsEnabled = !double.IsNaN(At());
            clear.IsEnabled = !double.IsNaN(player.LoopFrom) || !double.IsNaN(player.LoopTo);
        };
        on.ContextMenu = menu;
    }

    /// <summary>(since 4.7.0) A session box's lines whole when it's open, however narrow the box; the title as its tooltip.</summary>
    private static Style WholeLines()
    {
        var style = new Style(typeof(ComboBoxItem), Application.Current?.TryFindResource(typeof(ComboBoxItem)) as Style);
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(Item.Title))));
        return style;
    }

    /// <summary>(since 4.7.0) A slot under Compare's chart with a pop-out button at its top right: the content goes into
    /// a window of its own (moved and sized anywhere), and back when that closes (or the button's clicked again). Since
    /// 4.10.9 the window can have something under it, <paramref name="below"/> (the Video's timeline), each growing with it.</summary>
    private static int open;

    private static FrameworkElement PopOut(string title, FrameworkElement content, Action<bool>? popped = null, FrameworkElement? below = null, double width = 720, double height = 480)
    {
        var slot = new Grid { MinHeight = 240 };
        var place = new Border { Child = content };
        var note = Ui.Note(T("In its own window"));
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Center;
        note.Visibility = Visibility.Collapsed;
        var button = IconButton("", F("Pop out {0}", title));
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 2, 2, 0);
        slot.Children.Add(place);
        slot.Children.Add(note);
        slot.Children.Add(button);
        Window? window = null;
        button.Click += (_, _) =>
        {
            if (window != null) { window.Close(); return; }
            place.Child = null;
            var w = window = Ui.Dialog(title, slot, width, Math.Min(height, SystemParameters.WorkArea.Height - 20));
            w.ResizeMode = ResizeMode.CanResize;
            Grid? split = null;
            if (below != null)
            {
                split = new Grid();
                split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 160 });
                split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 260 });
                Grid.SetRow(below, 1);
                split.Children.Add(content);
                split.Children.Add(below);
            }
            var frame = new Border { Padding = new Thickness(8), Child = split ?? content };
            w.Content = frame;
            w.Closed += (_, _) =>
            {
                frame.Child = null;
                split?.Children.Clear();
                place.Child = content;
                note.Visibility = Visibility.Collapsed;
                ((TextBlock)button.Content).Text = "";
                button.ToolTip = F("Pop out {0}", title);
                System.Windows.Automation.AutomationProperties.SetName(button, F("Pop out {0}", title));
                window = null;
                popped?.Invoke(false);
            };
            note.Visibility = Visibility.Visible;
            ((TextBlock)button.Content).Text = "";
            button.ToolTip = T("Put back");
            System.Windows.Automation.AutomationProperties.SetName(button, T("Put back"));
            // (a second one a little lower right, not over the first)
            w.Loaded += (_, _) => { if (open > 0) { w.Left += 32 * open; w.Top += 32 * open; } open++; };
            w.Closed += (_, _) => open--;
            w.Show();
            popped?.Invoke(true);
        };
        return slot;
    }

    private static List<Part> PartsOf(PedalAnalysis a)
    {
        var parts = new List<Part> { new(T("Whole session"), 0, Math.Max(0, a.Data.Count - 1), null) };
        var best = a.Data.BestLap;
        foreach (var lap in a.Data.Laps)
            parts.Add(new Part(lap.Text, lap.Start, lap.End, lap, lap == best));
        return parts;
    }

    private static Part DefaultPart(List<Part> parts) => parts.FirstOrDefault(p => p.Best) ?? parts[0];

    /// <summary>(since 4.13.0) A session's line in its box: its title, then an icon when it has a race video (whole even
    /// when the box is narrow: the title's cut first).</summary>
    private static DataTemplate SessionTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Title)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var icon = new FrameworkElementFactory(typeof(TextBlock), "video");
        icon.SetValue(TextBlock.TextProperty, "\uE714");
        icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(DockPanel.DockProperty, Dock.Right);
        icon.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        icon.SetValue(FrameworkElement.ToolTipProperty, T("Has a video"));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.AppendChild(icon);
        row.AppendChild(text);
        var template = new DataTemplate { VisualTree = row };
        template.Triggers.Add(new DataTrigger { Binding = new Binding(nameof(Item.HasVideo)), Value = true, Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Visible, "video") } });
        return template;
    }

    /// <summary>(since 4.6.5) A part's line in its box: the lap and its time, the fastest in bold with a green star.</summary>
    private static DataTemplate PartTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock), "text");
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Part.Text)));
        var star = new FrameworkElementFactory(typeof(TextBlock), "star");
        star.SetValue(TextBlock.TextProperty, "  ★ " + T("fastest"));
        star.SetValue(TextBlock.ForegroundProperty, PedalColours.Frozen(PedalColours.Throttle));
        star.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        star.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.AppendChild(text);
        row.AppendChild(star);
        var template = new DataTemplate { VisualTree = row };
        template.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(Part.Best)), Value = true,
            Setters = { new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold, "text"), new Setter(UIElement.VisibilityProperty, Visibility.Visible, "star") },
        });
        return template;
    }

    /// <summary>Session 1 set to the race analysed and its lap (null: the fastest); Session 2 refilled with that track's races.</summary>
    private void UseThis(Item? item, PedalAnalysis? a, int? lap)
    {
        mineItem = item;
        mine = a;
        filling = true;
        thisSession.SelectedItem = item == null ? null : items.FirstOrDefault(i => Same(i, item));
        thisPart.Items.Clear();
        if (a != null)
        {
            foreach (var p in PartsOf(a)) thisPart.Items.Add(p);
            SelectLap(thisPart, lap);
        }
        filling = false;
        FillOtherSessions();
        ShowCompare();
    }

    /// <summary>With's races: since 4.6.5 only those on This one's track (any car; yours, then other drivers'); since
    /// 4.13.0 not This one itself, "-- No Matches --" when that leaves none; since 4.15.0 This one too when it has 2 laps
    /// or more (another of its laps to compare with), even with no other session on its track.</summary>
    private void FillOtherSessions()
    {
        string? keep = (otherSession.SelectedItem as Item)?.Key;
        filling = true;
        otherSession.Items.Clear();
        var matches = items.Where(i => Same(i, mineItem) ? Math.Max(i.Laps, mine?.Data.Laps.Count ?? 0) >= 2 : OnTrackOfThis(i)).ToList();
        otherSession.Items.Add(new Blank(matches.Count > 0 || mineItem == null ? T("None") : T("-- No Matches --")));
        foreach (var i in matches) otherSession.Items.Add(i);
        otherSession.SelectedItem = otherSession.Items.OfType<Item>().FirstOrDefault(i => i.Key == keep) ?? otherSession.Items[0];
        filling = false;
        OthersListed?.Invoke();
    }

    /// <summary>On This one's track: a race with the same track, or (one with no track, such as someone
    /// else's) starting within the start radius of where This one or one of its laps starts.</summary>
    private bool OnTrackOfThis(Item i)
    {
        if (mineItem == null) return false;
        if (mineItem.Track != null && i.Track != null) return i.Track == mineItem.Track;
        if (mine == null || StartOf(i) is not { } at) return false;
        double radius = applet.Settings.StartRadius;
        return StartsOf(mine).Any(p => Math.Sqrt((p.X - at.X) * (p.X - at.X) + (p.Z - at.Z) * (p.Z - at.Z)) <= radius);
    }

    private static bool Placed(double x, double z) => !double.IsNaN(x) && !double.IsNaN(z) && (x != 0 || z != 0);

    /// <summary>Where a session and each of its laps start.</summary>
    private static IEnumerable<(double X, double Z)> StartsOf(PedalAnalysis a)
    {
        var d = a.Data;
        int first = Enumerable.Range(0, Math.Min(d.Count, d.X.Length)).FirstOrDefault(k => Placed(d.X[k], d.Z[k]), -1);
        if (first >= 0) yield return (d.X[first], d.Z[first]);
        foreach (var lap in d.Laps)
            if (lap.Start >= 0 && lap.Start < d.X.Length && Placed(d.X[lap.Start], d.Z[lap.Start])) yield return (d.X[lap.Start], d.Z[lap.Start]);
    }

    /// <summary>Where a session file starts: its first row with a place (read once).</summary>
    private static (double X, double Z)? StartOf(Item i)
    {
        if (i.StartRead) return i.Start;
        i.StartRead = true;
        try
        {
            int xi = -1, zi = -1;
            foreach (var line in File.ReadLines(i.File.FullName).Take(20000))
            {
                if (line.StartsWith("ms,", StringComparison.Ordinal))
                {
                    var head = line.Split(',');
                    xi = Array.IndexOf(head, "x");
                    zi = Array.IndexOf(head, "z");
                    continue;
                }
                if (xi < 0 || zi < 0 || line.Length == 0 || !char.IsDigit(line[0])) continue;
                var c = line.Split(',');
                if (c.Length > Math.Max(xi, zi) && double.TryParse(c[xi], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(c[zi], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) && Placed(x, z))
                    return i.Start = (x, z);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private async Task PickOther(Item? pick = null, int? lap = null)
    {
        if (pick != null)
        {
            filling = true;
            // (asked for by name, as Compare With: there even if it's elsewhere)
            if (!otherSession.Items.Contains(pick)) otherSession.Items.Add(pick);
            otherSession.SelectedItem = pick;
            filling = false;
        }
        other = null;
        filling = true;
        otherPart.Items.Clear();
        filling = false;
        if (otherSession.SelectedItem is Item item)
        {
            try { other = await Load(item.File); }
            catch (Exception ex) { compareStatus.Text = F("The session couldn't be read: {0}", ex.Message); }
            if (other != null && otherSession.SelectedItem == item)
            {
                filling = true;
                var parts = PartsOf(other);
                foreach (var p in parts) otherPart.Items.Add(p);
                SelectLap(otherPart, lap);
                // the same session: another lap than this one's
                if (lap == null && other == mine && thisPart.SelectedItem is Part ours && otherPart.SelectedItem is Part theirs && ours.From == theirs.From)
                    otherPart.SelectedItem = parts.FirstOrDefault(p => p.Lap is { Complete: true } && p.From != ours.From) ?? theirs;
                filling = false;
            }
        }
        ShowCompare();
    }

    /// <summary>(since 4.14.1) The friction circle's dot at a moment of Session 1 (a sample; NaN: none).</summary>
    private void FrictionAt(double sample) =>
        friction.At = lastA != null && friction.Analysis == lastA.Analysis ? sample : double.NaN;

    private static Run? RunOf(PedalAnalysis? a, object? part, string label) =>
        a != null && part is Part p && p.To > p.From ? new Run { Analysis = a, From = p.From, To = p.To, Label = label } : null;

    private void ShowCompare()
    {
        string thisLabel = (mineItem == null ? "" : mineItem.Imported ? mineItem.Driver + " · " : Same(mineItem, current) ? "" : mineItem.Title + " · ") + (thisPart.SelectedItem as Part)?.Text;
        string otherLabel = otherSession.SelectedItem is Item i ? (i.Imported ? i.Driver + " · " : Same(i, mineItem) ? "" : i.Title + " · ") + (otherPart.SelectedItem as Part)?.Text : "";
        var a = RunOf(mine, thisPart.SelectedItem, thisLabel);
        var b = otherSession.SelectedItem is Item ? RunOf(other, otherPart.SelectedItem, otherLabel) : null;
        compareChart.Highlight = null;
        compareChart.Show(a, b);
        videoRun = a == null ? null : new Run { Analysis = a.Analysis, From = a.From, To = a.To, Label = a.Label };
        videoChart.Highlight = null;
        videoChart.Show(videoRun, null);
        player.Show(a);
        friction.At = double.NaN;
        lineMap.Show(a, b);
        ShowLegend(a, b);
        videoLabels = (thisLabel.Length > 0 ? thisLabel : mineItem?.Title ?? "", otherLabel);
        ShowVideos();
        otherPart.Visibility = otherSession.SelectedItem is Item ? Visibility.Visible : Visibility.Hidden;

        var status = new List<string>();
        if (a != null && b != null)
        {
            var la = (thisPart.SelectedItem as Part)?.Lap;
            var lb = (otherPart.SelectedItem as Part)?.Lap;
            if (la is { Complete: true } && lb is { Complete: true })
                status.Add(F("{0} vs {1} ({2:+0.000;-0.000;0.000} s)", Lap.LapClock(la.Seconds), Lap.LapClock(lb.Seconds), lb.Seconds - la.Seconds));
            if (!SameTrack(a, b, la, lb)) status.Add(T("Not the same track?"));
        }
        compareStatus.Text = string.Join(" · ", status);
        compareStatus.Foreground = status.Contains(T("Not the same track?")) ? PedalColours.Frozen(PedalColours.Warning) : null;
        if (compareStatus.Foreground == null) compareStatus.ClearValue(TextBlock.ForegroundProperty);
        compareStatus.Visibility = status.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Where both say where the car was: their starts within 500 m, and laps within 6% of each other's length.</summary>
    private static bool SameTrack(Run a, Run b, Lap? la, Lap? lb)
    {
        double ax = a.Data.X[a.From], az = a.Data.Z[a.From], bx = b.Data.X[b.From], bz = b.Data.Z[b.From];
        if (la != null && lb != null && !double.IsNaN(ax) && !double.IsNaN(bx) && Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)) > 500) return false;
        if (la is { Complete: true } && lb is { Complete: true } && !double.IsNaN(la.Length) && !double.IsNaN(lb.Length)
            && Math.Abs(la.Length - lb.Length) > 0.06 * Math.Max(la.Length, lb.Length)) return false;
        return true;
    }

    /// <summary>The channels (since 4.6.2; since 4.7.0 on the Session rows, without the runs' names): a chip per channel
    /// in its colour, Session 2's dotted as its lines: its box shows or hides that line, its name highlights it (the
    /// others faded). Session 2's, and Delta, only while there's one to compare with.</summary>
    private void ShowLegend(Run? a, Run? b)
    {
        lastA = a;
        lastB = b;
        ShowLegend();
    }

    private void ShowLegend()
    {
        var all = new[] { Channel.Speed, Channel.Throttle, Channel.Brake, Channel.Steering, Channel.Clutch };
        // (since 4.15.4 the rpm and the gear in a drop-down after them)
        var more = new[] { Channel.Rpm, Channel.Gear };
        Entry(thisChannels, compareChart, lastA, 1, all, SaveChannels, ShowLegend, combineChip, more);
        Entry(otherChannels, compareChart, lastB, 2, all, SaveChannels, ShowLegend, more: more);
        // (since 4.10.9) the Video's timeline: Session 1's, no Clutch
        Entry(videoChannels, videoChart, videoRun, 1, all[..^1], () =>
        {
            settings.VideoHidden = videoChart.Hidden.Where(h => h.Channel is not (Channel.Clutch or Channel.Rpm or Channel.Gear)).Select(h => h.Channel.ToString()).ToList();
            settings.Save();
        }, ShowLegend);
        deltaChip.Visibility = lastB != null && compareChart.ByDistance ? Visibility.Visible : Visibility.Collapsed;
        // (since 4.15.4 S2 highlighted with no Session 2: not)
        if (compareChart.HighlightRun == 2 && lastB == null) compareChart.HighlightRun = null;
        ShowPickerLinks();
    }

    /// <summary>A run's channels on a chart: a box (shown or not) and a name (highlighted) each, after <paramref name="first"/>;
    /// (since 4.15.4) then <paramref name="more"/> in a drop-down.</summary>
    private static void Entry(StackPanel toggles, CompareChart chart, Run? r, int which, Channel[] channels, Action save, Action again, UIElement? first = null, Channel[]? more = null)
    {
        toggles.Children.Clear();
        toggles.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
        if (r == null) return;
        if (first != null) toggles.Children.Add(first);
        foreach (var c in channels)
        {
            var item = ChannelItem(toggles, chart, which, c, save, again);
            item.Margin = new Thickness(10, 0, 0, 0);
            // (a channel the run hasn't, its room kept so the rows line up)
            if (!CompareChart.Logged(r.Data, c)) item.Visibility = Visibility.Hidden;
            toggles.Children.Add(item);
        }
        if (more != null) toggles.Children.Add(MoreChannels(toggles, chart, r, which, more, save, again));
    }

    /// <summary>(since 4.15.4) A drop-down of a run's other channels, each with its box, colour and name as the rows';
    /// on the button a dot in the colour of each shown.</summary>
    private static FrameworkElement MoreChannels(StackPanel toggles, CompareChart chart, Run r, int which, Channel[] channels, Action save, Action again)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Dots()
        {
            dots.Children.Clear();
            foreach (var c in channels.Where(c => !chart.Hidden.Contains((c, which)) && CompareChart.Logged(r.Data, c)))
                dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 4, 0), Fill = PedalColours.Frozen(CompareChart.ColourOf(c)) });
        }
        Dots();
        var chevron = new TextBlock { Text = "", FontSize = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) };
        chevron.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var button = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = T("More channels"),
            BorderThickness = new Thickness(1), BorderBrush = PedalColours.Frozen(Colors.Gray, 0x60), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dots, new TextBlock { Text = T("More"), VerticalAlignment = VerticalAlignment.Center }, chevron } },
        };
        System.Windows.Automation.AutomationProperties.SetName(button, T("More channels") + (which == 2 ? " 2" : ""));
        var list = new StackPanel();
        var frame = new Border { Child = list, Padding = new Thickness(8, 6, 10, 2), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        frame.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, Child = frame, VerticalOffset = 2 };
        foreach (var c in channels)
        {
            var item = ChannelItem(toggles, chart, which, c, () => { save(); Dots(); }, () => { popup.IsOpen = false; again(); });
            item.Margin = new Thickness(0, 0, 0, 4);
            if (!CompareChart.Logged(r.Data, c)) item.IsEnabled = false;
            list.Children.Add(item);
        }
        button.MouseLeftButtonUp += (_, _) => popup.IsOpen = !popup.IsOpen;
        // (neither in this run's log: its room kept so the rows line up)
        if (!channels.Any(c => CompareChart.Logged(r.Data, c))) button.Visibility = Visibility.Hidden;
        return button;
    }

    /// <summary>A run's channel on a chart: a box (shown or not) and a name in its colour (highlighted).</summary>
    private static StackPanel ChannelItem(FrameworkElement owner, CompareChart chart, int which, Channel c, Action save, Action again)
    {
        var channel = c;
        string text = CompareChart.NameOf(c);
        bool hi = chart.Highlight == (c, which);
        var box = new CheckBox { IsChecked = !chart.Hidden.Contains((c, which)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = T("Show") };
        System.Windows.Automation.AutomationProperties.SetName(box, F("Show {0}", text) + (which == 2 ? " 2" : ""));
        Ui.OnToggle(box, on =>
        {
            if (on) chart.Hidden.Remove((channel, which)); else chart.Hidden.Add((channel, which));
            save();
            chart.InvalidateVisual();
        });
        Brush colour = c == Channel.Steering ? PedalColours.Theme(owner, "TextBrush") : PedalColours.Frozen(CompareChart.ColourOf(c));
        var swatch = new Border { Width = 16, Height = 3, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Background = colour };
        // (Session 2's dotted, as its lines)
        if (which == 2)
            swatch.Background = new VisualBrush(new Border { Width = 3, Height = 3, Background = colour })
            {
                TileMode = TileMode.Tile, Stretch = Stretch.None, Viewport = new Rect(0, 0, 6, 3), ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 6, 3), ViewboxUnits = BrushMappingMode.Absolute,
            };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = hi ? FontWeights.SemiBold : FontWeights.Normal };
        var tag = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 10, 3), Cursor = Cursors.Hand, ToolTip = T("Highlight"),
            BorderThickness = new Thickness(1), BorderBrush = hi ? colour : PedalColours.Frozen(Colors.Gray, 0x60),
            Background = hi ? PedalColours.Frozen(Colors.White, 0x26) : Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, label } },
        };
        tag.MouseLeftButtonUp += (_, _) => { chart.Highlight = hi ? null : (channel, which); chart.HighlightRun = null; again(); chart.InvalidateVisual(); };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { box, tag } };
    }

    /// <summary>Compare's lines kept: Delta in CompareChannels (with every channel, so none is hidden as one left out
    /// before 4.6.2 was), each run's hidden ones in CompareHidden.</summary>
    private void SaveChannels()
    {
        settings.CompareChannels = Enum.GetValues<Channel>().Where(c => c != Channel.Delta || compareChart.ShowDelta).Select(c => c.ToString()).ToList();
        settings.CompareHidden = compareChart.Hidden.Select(h => $"{h.Channel}.{h.Run}").ToList();
        settings.Save();
    }

    private Run? lastA, lastB;

    // ---------------------------------------------------------------- export and import

    /// <summary>Saves this lap (or the session) as a session file of its own, with the driver's name and the session's,
    /// to send to someone: their Analysis imports it.</summary>
    private void Export()
    {
        if (mine == null || mineItem == null || thisPart.SelectedItem is not Part part) return;
        string driver = string.IsNullOrWhiteSpace(settings.Driver) ? Environment.UserName : settings.Driver;
        string name = mineItem.Title;
        string suggested = string.Join("_", $"{driver} - {name}{(part.Lap != null ? " - " + F("Lap {0}", part.Lap.Number + 1) : "")}".Split(Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = suggested + ".csv", Filter = T("Session") + " (*.csv)|*.csv", DefaultExt = ".csv" };
        if (dialog.ShowDialog(Window.GetWindow(Host)) != true) return;
        try
        {
            long from = (long)part.From * 1000 / SessionData.Hz, to = (long)part.To * 1000 / SessionData.Hz;
            var output = new StringBuilder();
            output.AppendLine("# ForzaWheel session,2");
            output.AppendLine("# started," + mine.Data.Started.AddMilliseconds(from).ToString("o", CultureInfo.InvariantCulture));
            output.AppendLine("# driver," + driver.Replace('\n', ' '));
            output.AppendLine("# name," + (name + (part.Lap != null ? " · " + part.Text : "")).Replace('\n', ' '));
            // (since 4.15.0) its video on YouTube, and where in it this starts, so theirs plays it in step
            if (RaceVideos.Of(mineItem.Key) is { YouTube: { } url } link)
                output.AppendLine("# video," + url + "," + link.VideoAt(mine.Data.Started, from / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
            string? car = null;
            var rows = new StringBuilder();
            foreach (var line in File.ReadLines(mineItem.File.FullName))
            {
                if (line.StartsWith("# car,", StringComparison.Ordinal)) { car = line; continue; }
                if (line.Length == 0 || !char.IsDigit(line[0])) continue;
                int comma = line.IndexOf(',');
                if (comma < 0 || !long.TryParse(line.AsSpan(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms)) continue;
                if (ms < from || ms > to) continue;
                rows.Append((ms - from).ToString(CultureInfo.InvariantCulture)).Append(line.AsSpan(comma)).AppendLine();
            }
            if (car != null) output.AppendLine(car);
            output.AppendLine(SessionLog.Header);
            output.Append(rows);
            File.WriteAllText(dialog.FileName, output.ToString(), new UTF8Encoding(false));
            Logging.Log.Here.Info($"Analysis: exported {dialog.FileName}");
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(Host), F("It couldn't be saved: {0}", ex.Message), T("Analysis")); }
    }

    /// <summary>Adds session files (someone else's exported laps) to the list, and compares with the first.</summary>
    private async void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Session") + " (*.csv)|*.csv", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(Host)) != true) return;
        string? first = null;
        var problems = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                if (!File.ReadLines(path).Take(12).Any(l => l.StartsWith("ms,steering", StringComparison.Ordinal))) { problems.Add(F("{0}: not a session", Path.GetFileName(path))); continue; }
                Directory.CreateDirectory(SessionLog.ImportedFolder);
                string target = Path.Combine(SessionLog.ImportedFolder, Path.GetFileName(path));
                for (int k = 2; File.Exists(target); k++) target = Path.Combine(SessionLog.ImportedFolder, $"{Path.GetFileNameWithoutExtension(path)} ({k}).csv");
                File.Copy(path, target);
                first ??= "Imported\\" + Path.GetFileName(target);
                Logging.Log.Here.Info($"Analysis: imported {path}");
            }
            catch (Exception ex) { problems.Add(F("{0}: {1}", Path.GetFileName(path), ex.Message)); }
        }
        if (problems.Count > 0) MessageBox.Show(Window.GetWindow(Host), string.Join("\n", problems), T("Analysis"));
        if (first == null) return;
        Refresh();
        if (mine != null && items.FirstOrDefault(i => i.Key == first) is { } imported) await PickOther(imported);
    }

    // ---------------------------------------------------------------- helpers
}
