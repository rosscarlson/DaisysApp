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
/// time split, the trace, Compare (since 4.6.5 a race and lap of its own, yours or imported, against another on the same
/// track, any car; with export and import) beside
/// the Track Map (since 4.6.2: the lines both took), Smoothness (the friction circle left of its chart) in two thirds of
/// the width beside the Tips (red, white and green, a box each), the brake zones lined up at the brake point and at the
/// release and the throttle applications, drawn over each other, with Shifting (since 4.6.10; four across), and Brake and Throttle Analysis (a
/// double-click opens one).
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
    private readonly OverlayChart brakeOverlay = new() { Height = 165 };
    private readonly OverlayChart releaseOverlay = new() { Height = 165 };
    private readonly OverlayChart throttleOverlay = new() { Height = 165 };
    private readonly ShiftChart shiftChart = new() { Height = 165 };
    private readonly DataGrid brakeGrid = new() { IsReadOnly = true, MaxHeight = 190 };
    private readonly DataGrid throttleGrid = new() { IsReadOnly = true, MaxHeight = 190 };
    private readonly StackPanel tips = new();
    private readonly TextBlock busy = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    // (since 4.7.0 as tall as the Tips beside them, the circle 25% larger and growing with them)
    private readonly SmoothnessChart smoothChart = new() { MinHeight = 275 };
    private readonly FrictionCircle friction = new() { Width = 300, MinHeight = 275 };
    private PedalAnalysis? analysis;
    private CancellationTokenSource? loading;
    private Border headCard = null!;
    private FrameworkElement Host => headCard;

    // compare
    private readonly CompareChart compareChart = new() { Height = 300 };
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
    private readonly ComboBox thisSession = new() { Width = 100 };
    private readonly ComboBox thisPart = new() { Width = 220 };
    private readonly ComboBox otherSession = new() { Width = 100 };
    private readonly ComboBox otherPart = new() { Width = 220 };
    private readonly TextBlock compareStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock otherNote = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly StackPanel thisChannels = new() { Orientation = Orientation.Horizontal }, otherChannels = new() { Orientation = Orientation.Horizontal };
    private readonly ToggleButton deltaChip = Ui.Chip(T("Delta"), true, T("The other's time less this one's: above the line, this one is ahead"));
    private readonly DataGrid corners = new() { IsReadOnly = true, MinHeight = 240 };
    private PedalAnalysis? other;
    /// <summary>Compare's Session 1: since 4.7.0 always the session analysed (4.6.5–4.6.10 a race of its own).</summary>
    private PedalAnalysis? mine;
    private Item? mineItem;
    private bool filling;
    private readonly Dictionary<string, PedalAnalysis> loaded = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The lap (its number in the race, 1 the first) to pick in This once the session's read.</summary>
    private int? wantLap;

    /// <summary>A session: a race's log (its track, car and laps from the records naming it), or one imported.</summary>
    private sealed class Item
    {
        public required FileInfo File { get; init; }
        public required string Key { get; init; }
        public required DateTime Started { get; init; }
        public required string Title { get; init; }
        public string? Driver { get; init; }
        public string? Track { get; init; }
        public bool Imported => Driver != null;
        /// <summary>Where it starts (read once, for a race with no track).</summary>
        public (double X, double Z)? Start { get; set; }
        public bool StartRead { get; set; }
        public override string ToString() => Title;
    }

    private sealed record Part(string Text, int From, int To, Lap? Lap, bool Best = false)
    {
        public override string ToString() => Text;
    }

    /// <summary>A row of Compare's corner table: a brake zone of this run and the other's nearest one.</summary>
    private sealed record Corner(int Number, double X, double XEnd, double? BrakePoint, string Peak, string Entry, string Slowest, double? Delta);

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

    /// <summary>The sessions again (a lap or race kept, a track renamed): the races' logs that are still there, newest
    /// first, then the imported ones.</summary>
    public void Refresh()
    {
        var names = applet.Settings;
        var mine = new List<Item>();
        var races = applet.Store.Races.Where(r => r.Session != null).GroupBy(r => r.Session!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
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
            mine.Add(new Item
            {
                File = file, Key = g.Key, Started = started, Track = track,
                Title = $"{names.TrackName(track)} · {names.CarName(game, car)} · {started:g}" + (best > 0 ? " · " + Blocks.LapTime(best) : ""),
            });
        }
        var imported = ImportedFiles().Select(f =>
        {
            var head = SessionData.Head(f.FullName);
            var started = head.Started ?? SessionLog.StartedOf(f);
            string driver = string.IsNullOrWhiteSpace(head.Driver) ? T("Imported") : head.Driver;
            return new Item { File = f, Key = KeyOf(f), Started = started, Driver = driver, Title = driver + " · " + (string.IsNullOrWhiteSpace(head.Name) ? started.ToString("g") : head.Name) };
        });
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
    }

    /// <summary>The tab shown: the session picked read, if it isn't yet.</summary>
    public void Shown()
    {
        if (sessions.SelectedItem is Item i && !Same(i, current) && !Same(i, opening)) Open(i);
    }

    /// <summary>A lap, best lap or race clicked in the tables: its race analysed, that lap picked in This (null: the
    /// best); the cards brought into view. False: it has no session log.</summary>
    public bool Open(string? session, int? lap)
    {
        if (session == null || items.FirstOrDefault(i => !i.Imported && i.Key.Equals(session, StringComparison.OrdinalIgnoreCase)) is not { } item) return false;
        headCard.BringIntoView();
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
        headCard.BringIntoView();
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
        busy.Text = T("Analysing…");
        busy.Visibility = Visibility.Visible;
        PedalAnalysis? a = null;
        string? problem = null;
        try { a = await Load(item.File); }
        catch (Exception ex) { problem = ex.Message; }
        if (cancel.IsCancellationRequested) return;
        opening = null;
        busy.Visibility = Visibility.Collapsed;
        current = item;
        if (a == null) { ShowSession(null, F("The session couldn't be read: {0}", problem)); return; }
        ShowSession(a);
    }

    /// <summary>A session analysed, kept while its file is unchanged (the compare picks it again).</summary>
    private async Task<PedalAnalysis> Load(FileInfo file)
    {
        file.Refresh();
        string key = file.FullName;
        if (loaded.TryGetValue(key, out var a) && a.Size == file.Length) return a;
        a = await Task.Run(() => new PedalAnalysis(SessionData.Load(file.FullName)) { Size = file.Length });
        if (loaded.Count > 6) loaded.Clear();
        loaded[key] = a;
        return a;
    }

    private Item? current;

    private static bool Same(Item? a, Item? b) => a != null && b != null && a.Key.Equals(b.Key, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- the session

    private void Build()
    {
        var head = new StackPanel();
        head.Children.Add(busy);
        about.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        head.Children.Add(about);
        head.Children.Add(scores);
        head.Children.Add(split);
        head.Children.Add(splitLegend);
        head.Children.Add(numbers);
        System.Windows.Automation.AutomationProperties.SetName(sessions, T("Session"));
        sessions.ToolTip = T("Session");
        sessions.SelectionChanged += (_, _) => { if (!filling && sessions.SelectedItem is Item i) { wantLap = null; Pick(i); } };
        // (since 4.6.2 an icon)
        var folder = IconButton("", T("Open folder"));
        folder.Margin = new Thickness(6, 0, 0, 0);
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(SessionLog.Folder);
            Ui.Open("explorer.exe", $"\"{SessionLog.Folder}\"");
        };
        // (since 4.7.0 here, an icon; Compare's Export… button before)
        var export = IconButton("", T("Export this lap (or session) to send to someone"));
        export.Margin = new Thickness(0, 0, 4, 0);
        export.Click += (_, _) => Export();
        headCard = card("Session", T("Session Analysis"), head, false, new UIElement[] { sessions, folder, export });
        headCard.IsVisibleChanged += (_, _) => { if (headCard.IsVisible) Shown(); };
        Cards.Add(headCard);

        // (since 4.6.2) Compare and the lines both runs took; since 4.7.0 the Trace too (Session 1 alone, Session 2 None)
        compareChart.Hovered += x =>
        {
            // (since 4.10.3 off the chart: the cursor's moment again)
            bool off = double.IsNaN(x);
            lineMap.HoverAt(off ? compareChart.PlayX : x);
            // (since 4.8.0) the race's video at that moment (since 4.10.3 not while it plays)
            if (player.Playing || lastA == null) return;
            if (!off) video.ShowSample(lastA.From + lastA.IndexAt(x));
            else if (!double.IsNaN(player.Position)) video.ShowSample(player.Position);
        };
        // (since 4.10.3) a click puts the cursor there; playing moves it, the video and the map with it
        compareChart.Clicked += x => { if (lastA != null) player.Seek(lastA.From + lastA.IndexAt(x)); };
        player.Moved += (i, rate, jumped) =>
        {
            if (lastA == null) return;
            double x = lastA.XAt(i);
            compareChart.PlayAt(x);
            if (rate != 0) compareChart.Follow(x, rate > 0);
            if (!compareChart.IsMouseOver) lineMap.HoverAt(x);
            if (!fromVideo) video.Follow(i, rate, jumped);
            if (videoRun == null) return;
            double vx = videoRun.XAt(i);
            videoChart.PlayAt(vx);
            if (rate != 0) videoChart.Follow(vx, rate > 0);
        };
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
        RaceVideos.Changed += () => { if (mine != null && mineItem != null) video.Show(mine.Data, mineItem.Imported ? null : mineItem.Key); };
        compareChart.ZoneClicked += z => Pick(z, fromChart: true);
        Cards.Add(BuildCompare());

        // Smoothness and Tips (since 4.6.2 under Compare; since 4.7.0 the chart and circle as tall as the Tips)
        ToggleButton Chip(string text, bool on, Action<bool> set)
        {
            var c = Ui.Chip(text, on);
            c.Margin = new Thickness(0, 0, 4, 0);
            Ui.OnToggle(c, v => { set(v); settings.Save(); smoothChart.InvalidateVisual(); friction.InvalidateVisual(); });
            return c;
        }
        smoothChart.ShowBrake = friction.ShowBrake = settings.SmoothBrake;
        smoothChart.ShowThrottle = friction.ShowThrottle = settings.SmoothThrottle;
        smoothChart.ShowOn = settings.SmoothOn;
        smoothChart.ShowOff = settings.SmoothOff;
        smoothChart.ToolTip = null;
        smoothChart.Picked += o => { if (o is BrakeZone z) Pick(z); else if (o is ThrottleMove m) PickThrottle(m); };
        friction.Margin = new Thickness(0, 0, 12, 0);
        // (its width follows its height, 300 by 275 at the least)
        friction.SizeChanged += (_, e) => { if (e.HeightChanged) friction.Width = Math.Max(300, e.NewSize.Height * 300 / 275); };
        DockPanel.SetDock(friction, Dock.Left);
        var smoothCard = card("Smoothness", T("Smoothness"), new DockPanel { Children = { friction, smoothChart } }, true, new UIElement[]
        {
            Chip(T("Brake"), settings.SmoothBrake, v => settings.SmoothBrake = smoothChart.ShowBrake = friction.ShowBrake = v),
            Chip(T("Throttle"), settings.SmoothThrottle, v => settings.SmoothThrottle = smoothChart.ShowThrottle = friction.ShowThrottle = v),
            Chip(T("On"), settings.SmoothOn, v => settings.SmoothOn = smoothChart.ShowOn = v),
            Chip(T("Off"), settings.SmoothOff, v => settings.SmoothOff = smoothChart.ShowOff = v),
        });
        var tipsCard = card("Tips", T("Tips"), tips, false, Array.Empty<UIElement>());
        Cards.Add(SideBySide(smoothCard, tipsCard, 2));
        row(new[] { smoothCard, tipsCard });

        // the zones lined up at the brake point and at the release (since 4.6.2 a card each; a drop-down picked one
        // before), and the throttle applications, over each other; then (since 4.6.10) the gear changes
        brakeOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        releaseOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        throttleOverlay.Picked += o => { if (o is ThrottleMove m) PickThrottle(m); };
        shiftChart.Picked += s => ShowMoment(s.At - SessionData.Hz, s.At + SessionData.Hz, null, s.At);
        var four = new Grid();
        var fourCards = new[]
        {
            card("BrakesCompared", T("Brake Point"), brakeOverlay, false, Array.Empty<UIElement>()),
            card("ReleasesCompared", T("Brake Release"), releaseOverlay, false, Array.Empty<UIElement>()),
            card("ThrottleCompared", T("Throttle"), throttleOverlay, false, Array.Empty<UIElement>()),
            card("Shifting", T("Shifting"), shiftChart, false, Array.Empty<UIElement>()),
        };
        for (int i = 0; i < fourCards.Length; i++)
        {
            if (i > 0) four.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            four.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(fourCards[i], i * 2);
            four.Children.Add(fourCards[i]);
        }
        Cards.Add(four);
        row(fourCards);

        // Brake Analysis and Throttle Analysis
        Column(brakeGrid, "#", nameof(BrakeZone.Number), null);
        Column(brakeGrid, T("Peak"), nameof(BrakeZone.PeakPct), "{0:0}%");
        Column(brakeGrid, T("Hold"), nameof(BrakeZone.HoldS), "{0:0.00} s");
        Column(brakeGrid, T("Release"), nameof(BrakeZone.ReleaseS), "{0:0.00} s");
        Column(brakeGrid, T("Smooth"), nameof(BrakeZone.ReleaseSmoothPct), "{0:0}%");
        Column(brakeGrid, T("Trail"), nameof(BrakeZone.Trail), "{0:0}%");
        Column(brakeGrid, T("AppScore"), nameof(BrakeZone.ApplyScore), "{0:0}", tip: T("Application"));
        Column(brakeGrid, T("ResScore"), nameof(BrakeZone.ReleaseScore), "{0:0}", tip: T("Release"));
        brakeGrid.SelectionChanged += (_, _) => { if (brakeGrid.SelectedItem is BrakeZone z && !picking) Pick(z); };
        OpenOnClick(brakeGrid);
        Column(throttleGrid, T("Way"), nameof(ThrottleMove.Way), null);
        Column(throttleGrid, T("From"), nameof(ThrottleMove.FromPct), "{0:0}%");
        Column(throttleGrid, T("To"), nameof(ThrottleMove.ToPct), "{0:0}%");
        Column(throttleGrid, T("Time"), nameof(ThrottleMove.Ms), "{0:0} ms");
        Column(throttleGrid, T("Smooth"), nameof(ThrottleMove.SmoothPct), "{0:0}%");
        Column(throttleGrid, T("Score"), nameof(ThrottleMove.Score), "{0:0}");
        throttleGrid.SelectionChanged += (_, _) => { if (throttleGrid.SelectedItem is ThrottleMove m && !picking) PickThrottle(m); };
        OpenOnClick(throttleGrid);
        var brakeCard = card("BrakeAnalysis", T("Brake Analysis"), brakeGrid, false, Array.Empty<UIElement>());
        var throttleCard = card("ThrottleAnalysis", T("Throttle Analysis"), throttleGrid, false, Array.Empty<UIElement>());
        Cards.Add(SideBySide(brakeCard, throttleCard));
        row(new[] { brakeCard, throttleCard });
    }

    /// <summary>An icon button, its name as its tooltip.</summary>
    private static Button IconButton(string glyph, string name)
    {
        var b = SimEffects.BassShaker.VolumeControl.IconButton(glyph);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    /// <summary>Two cards sharing the width half and half, or the left <paramref name="share"/> times the right's.</summary>
    private static Grid SideBySide(UIElement left, UIElement right, double share = 1)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    /// <summary>A double-click on a row (since 4.6.2; a click before) opens its moment (graph, numbers, notes) in a window.</summary>
    private void OpenOnClick(DataGrid grid)
    {
        grid.MouseDoubleClick += (_, e) =>
        {
            if (analysis == null || e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(grid, source) is DataGridRow row && row.Item is { } item)
                Host.Dispatcher.BeginInvoke(() => MomentWindow.Show(Host, analysis, item));
        };
    }

    private static void Column(DataGrid grid, string header, string path, string? format, string? sort = null, bool star = false, string? tip = null)
    {
        var b = new Binding(path);
        if (format != null) b.StringFormat = format;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = tip == null ? header : new TextBlock { Text = header, ToolTip = tip },
            Binding = b,
            SortMemberPath = sort ?? path,
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            MinWidth = star ? 160 : 0,
        });
    }

    private void ShowSession(PedalAnalysis? a, string? problem = null)
    {
        analysis = a;
        smoothChart.Analysis = a;
        friction.Analysis = a;
        scores.Children.Clear();
        numbers.Children.Clear();
        tips.Children.Clear();
        split.Children.Clear();
        split.ColumnDefinitions.Clear();
        splitLegend.Children.Clear();
        brakeGrid.ItemsSource = a?.Brakes;
        throttleGrid.ItemsSource = a?.Throttle;
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

    private bool picking;

    private void Pick(BrakeZone z, bool fromChart = false)
    {
        picking = true;
        brakeGrid.SelectedItem = z;
        brakeGrid.ScrollIntoView(z);
        picking = false;
        brakeOverlay.Selected = releaseOverlay.Selected = z;
        brakeOverlay.InvalidateVisual();
        releaseOverlay.InvalidateVisual();
        if (!fromChart) ShowMoment(z.Start, z.End, z);
    }

    private void PickThrottle(ThrottleMove m)
    {
        picking = true;
        throttleGrid.SelectedItem = m;
        throttleGrid.ScrollIntoView(m);
        picking = false;
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
        void Row(string label, ComboBox session, ComboBox part, StackPanel channels, params UIElement[] after)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            session.Margin = part.Margin = new Thickness(0, 0, 6, 0);
            session.ItemContainerStyle = WholeLines();
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
        otherNote.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        deltaChip.VerticalAlignment = VerticalAlignment.Center;
        deltaChip.Margin = new Thickness(14, 0, 0, 0);
        Row(T("Session 1"), thisSession, thisPart, thisChannels, compareStatus);
        Row(T("Session 2"), otherSession, otherPart, otherChannels, deltaChip, otherNote);
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
        s.Children.Add(compareChart);
        // (since 4.6.9) where the part shown is in the whole (since 4.10.3 the whole drawn, the part framed); then playback
        s.Children.Add(new CompareOverview(compareChart) { Margin = new Thickness(52, 4, 8, 0) });
        s.Children.Add(new PlaybackBar(player));
        videoChart.Hidden.Add((Channel.Clutch, 1));
        video.SetVolume(settings.VideoVolume, settings.VideoMuted);
        video.VolumeChanged += () =>
        {
            settings.VideoVolume = video.Volume;
            settings.VideoMuted = video.Muted;
            settings.SaveSoon();
        };
        foreach (var h in settings.VideoHidden)
            if (Enum.TryParse<Channel>(h, out var vc)) videoChart.Hidden.Add((vc, 1));

        corners.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(Corner.Number)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Brake point"), Binding = new Binding(nameof(Corner.BrakePoint)) { StringFormat = "{0:+0;-0;0}" }, SortMemberPath = nameof(Corner.BrakePoint) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Peak"), Binding = new Binding(nameof(Corner.Peak)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Entry"), Binding = new Binding(nameof(Corner.Entry)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Slowest"), Binding = new Binding(nameof(Corner.Slowest)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Delta"), Binding = new Binding(nameof(Corner.Delta)) { StringFormat = "{0:+0.00;-0.00;0.00} s" }, SortMemberPath = nameof(Corner.Delta), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        // (since 4.7.0 a line on the chart where it is)
        corners.SelectionChanged += (_, _) =>
        {
            if (corners.SelectedItem is not Corner c) return;
            compareChart.Mark(c.X, c.XEnd);
            lineMap.ShowRange(c.X, c.XEnd);
            if (lastA != null) player.Seek(lastA.From + lastA.IndexAt(c.X));
        };
        System.Windows.Automation.AutomationProperties.SetName(corners, T("Corners"));
        // (since 4.7.0) the corners (always there, empty with nothing to compare), the race's video (since 4.8.0; its
        // sync tools only in its own window) and the Track Map; the video and the map pop out. Since 4.10.9 the video
        // first, half the width and 450 px high (half as high again), the map an eighth (taller than wide), the corners
        // the rest; the video's window has its timeline under it
        System.Windows.Automation.AutomationProperties.SetName(lineMap, T("Track Map"));
        var under = new Grid { Margin = new Thickness(0, 10, 0, 0), Height = 450 };
        var slots = new FrameworkElement[] { PopOut(T("Video"), video, popped => video.Tools = popped, VideoTimeline(), 1000, 940), PopOut(T("Track Map"), lineMap), corners };
        var shares = new[] { 4.0, 1, 3 };
        for (int i = 0; i < slots.Length; i++)
        {
            if (i > 0) under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shares[i], GridUnitType.Star) });
            Grid.SetColumn(slots[i], i * 2);
            under.Children.Add(slots[i]);
        }
        s.Children.Add(under);
        return card("Compare", T("Compare"), s, false, new UIElement[] { import });
    }

    /// <summary>(since 4.10.9) Under the popped-out Video: Session 1's channels (a box each), its chart, the whole of it
    /// with the part shown framed, and playback, all as Compare's and moving with it.</summary>
    private FrameworkElement VideoTimeline()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        videoChannels.Margin = new Thickness(42, 0, 0, 4);
        DockPanel.SetDock(videoChannels, Dock.Top);
        panel.Children.Add(videoChannels);
        var bar = new PlaybackBar(player);
        DockPanel.SetDock(bar, Dock.Bottom);
        panel.Children.Add(bar);
        var overview = new CompareOverview(videoChart) { Margin = new Thickness(52, 4, 8, 0) };
        DockPanel.SetDock(overview, Dock.Bottom);
        panel.Children.Add(overview);
        System.Windows.Automation.AutomationProperties.SetName(videoChart, T("Timeline"));
        panel.Children.Add(videoChart);
        return panel;
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

    /// <summary>With's races: since 4.6.5 only those on This one's track (any car; yours, then other drivers').</summary>
    private void FillOtherSessions()
    {
        string? keep = (otherSession.SelectedItem as Item)?.Key;
        filling = true;
        otherSession.Items.Clear();
        otherSession.Items.Add(T("None"));
        foreach (var i in items.Where(OnTrackOfThis)) otherSession.Items.Add(i);
        otherSession.SelectedItem = otherSession.Items.OfType<Item>().FirstOrDefault(i => i.Key == keep) ?? otherSession.Items[0];
        filling = false;
    }

    /// <summary>On This one's track: the same race, a race with the same track, or (one with no track, such as someone
    /// else's) starting within the start radius of where This one or one of its laps starts.</summary>
    private bool OnTrackOfThis(Item i)
    {
        if (mineItem == null) return false;
        if (Same(i, mineItem)) return true;
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
        lineMap.Show(a, b);
        video.Show(mine?.Data, mineItem == null || mineItem.Imported ? null : mineItem.Key);
        ShowLegend(a, b);
        // (since 4.7.0 always there, empty with nothing to compare)
        corners.ItemsSource = a != null && b != null ? Corners(a, b) : null;
        otherPart.Visibility = otherSession.SelectedItem is Item ? Visibility.Visible : Visibility.Hidden;
        otherNote.Text = mineItem != null && otherSession.Items.Count < 2 ? T("No other race on this track yet") : "";

        var status = new List<string>();
        if (a != null && b != null)
        {
            var la = (thisPart.SelectedItem as Part)?.Lap;
            var lb = (otherPart.SelectedItem as Part)?.Lap;
            if (la is { Complete: true } && lb is { Complete: true })
                status.Add(F("{0} vs {1} ({2:+0.000;-0.000;0.000} s)", Lap.LapClock(la.Seconds), Lap.LapClock(lb.Seconds), lb.Seconds - la.Seconds));
            if (!compareChart.ByDistance) status.Add(T("By time (no distance in the telemetry)"));
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
        Entry(thisChannels, compareChart, lastA, 1, all, SaveChannels, ShowLegend);
        Entry(otherChannels, compareChart, lastB, 2, all, SaveChannels, ShowLegend);
        // (since 4.10.9) the Video's timeline: Session 1's, no Clutch
        Entry(videoChannels, videoChart, videoRun, 1, all[..^1], () =>
        {
            settings.VideoHidden = videoChart.Hidden.Where(h => h.Channel != Channel.Clutch).Select(h => h.Channel.ToString()).ToList();
            settings.Save();
        }, ShowLegend);
        deltaChip.Visibility = lastB != null && compareChart.ByDistance ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A run's channels on a chart: a box (shown or not) and a name (highlighted) each.</summary>
    private static void Entry(StackPanel toggles, CompareChart chart, Run? r, int which, Channel[] channels, Action save, Action again)
    {
        toggles.Children.Clear();
        toggles.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
        if (r == null) return;
        foreach (var c in channels)
        {
            var channel = c;
            string text = c switch { Channel.Speed => T("Speed"), Channel.Throttle => T("Throttle"), Channel.Brake => T("Brake"), Channel.Clutch => T("Clutch"), _ => T("Steering") };
            bool hi = chart.Highlight == (c, which);
            var box = new CheckBox { IsChecked = !chart.Hidden.Contains((c, which)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = T("Show") };
            System.Windows.Automation.AutomationProperties.SetName(box, F("Show {0}", text) + (which == 2 ? " 2" : ""));
            Ui.OnToggle(box, on =>
            {
                if (on) chart.Hidden.Remove((channel, which)); else chart.Hidden.Add((channel, which));
                save();
                chart.InvalidateVisual();
            });
            Brush colour = c == Channel.Steering ? PedalColours.Theme(toggles, "TextBrush") : PedalColours.Frozen(CompareChart.ColourOf(c));
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
            tag.MouseLeftButtonUp += (_, _) => { chart.Highlight = hi ? null : (channel, which); again(); chart.InvalidateVisual(); };
            // (a channel the run hasn't, its room kept so the rows line up)
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { box, tag } };
            if (c == Channel.Speed && !r.Data.HasSpeed) item.Visibility = Visibility.Hidden;
            toggles.Children.Add(item);
        }
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

    /// <summary>Each firm brake zone of this run with the other's nearest (within 100 m, or 3 s by time): where each
    /// braked, how hard, the speeds, and the time gained or lost through it.</summary>
    private List<Corner> Corners(Run a, Run b)
    {
        double Pos(Run r, int sample) => r.Xs[Math.Clamp(sample - r.From, 0, r.Xs.Length - 1)];
        var bz = b.Analysis.Brakes.Where(z => z.Peak >= 0.15 && z.Start >= b.From && z.Start <= b.To).ToList();
        double near = compareChart.ByDistance ? 100 : 3;
        var rows = new List<Corner>();
        int n = 0;
        foreach (var z in a.Analysis.Brakes.Where(z => z.Peak >= 0.15 && z.Start >= a.From && z.Start <= a.To))
        {
            double x = Pos(a, z.Start), xEnd = Pos(a, z.End);
            var match = bz.MinBy(o => Math.Abs(Pos(b, o.Start) - x));
            if (match != null && Math.Abs(Pos(b, match.Start) - x) > near) match = null;
            string Two(string mine, string? theirs) => theirs == null ? mine : mine + "  /  " + theirs;
            double margin = compareChart.ByDistance ? 30 : 0.5;
            double d0 = compareChart.DeltaAt(Math.Max(0, x - margin)), d1 = compareChart.DeltaAt(Math.Min(a.Length, xEnd + margin * 2));
            rows.Add(new Corner(++n, x, xEnd,
                match == null ? null : Math.Round(Pos(b, match.Start) - x, compareChart.ByDistance ? 0 : 2),
                Two($"{z.PeakPct:0}%", match == null ? null : $"{match.PeakPct:0}%"),
                Two(Speeds.Text(z.EntrySpeed), match == null ? null : Speeds.Text(match.EntrySpeed)),
                Two(Speeds.Text(z.MinSpeed), match == null ? null : Speeds.Text(match.MinSpeed)),
                double.IsNaN(d0) || double.IsNaN(d1) ? null : Math.Round(d1 - d0, 3)));
        }
        return rows;
    }

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
