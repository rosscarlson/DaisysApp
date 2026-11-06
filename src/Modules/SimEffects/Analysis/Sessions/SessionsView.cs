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
/// The race analysis on the Analysis tab (Forza Analysis, a module of its own until 4.5.0): for the race picked (the
/// Session box, or a lap, best lap or race clicked in the tables below), its scores and time split, the trace, Compare
/// (a lap or the race against another, yours or imported, with export and import), the brake zones and throttle
/// applications drawn over each other, Brake and Throttle Analysis (a click opens one), and Smoothness beside the Tips.
/// Its sessions are the races' logs (<see cref="RaceLog"/>), found from the laps and races that name them.
/// </summary>
internal sealed class SessionsView
{
    private readonly AnalysisApplet applet;
    private readonly SessionSettings settings = SessionSettings.Load();
    private readonly Func<string, string, UIElement, UIElement[], Border> card;
    private readonly ComboBox sessions = new() { MinWidth = 360, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock about = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel scores = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Grid split = new() { Height = 16, Margin = new Thickness(0, 10, 0, 4) };
    private readonly WrapPanel splitLegend = new();
    private readonly WrapPanel numbers = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly SessionChart chart = new() { Height = 320 };
    private readonly OverlayChart brakeOverlay = new() { Height = 220 };
    private readonly OverlayChart throttleOverlay = new() { Height = 220 };
    private readonly DataGrid brakeGrid = new() { IsReadOnly = true, MaxHeight = 380 };
    private readonly DataGrid throttleGrid = new() { IsReadOnly = true, MaxHeight = 380 };
    private readonly StackPanel tips = new();
    private readonly TextBlock busy = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly SmoothnessChart smoothChart = new() { Height = 190 };
    private readonly FrictionCircle friction = new() { Height = 300 };
    private PedalAnalysis? analysis;
    private CancellationTokenSource? loading;
    private Border headCard = null!;
    private FrameworkElement Host => headCard;

    // compare
    private readonly CompareChart compareChart = new() { Height = 520 };
    private readonly ComboBox thisPart = new() { MinWidth = 170 };
    private readonly ComboBox otherSession = new() { MinWidth = 240 };
    private readonly ComboBox otherPart = new() { MinWidth = 170 };
    private readonly TextBlock compareStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel legend = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGrid corners = new() { IsReadOnly = true, MaxHeight = 320, Margin = new Thickness(0, 10, 0, 0) };
    private PedalAnalysis? other;
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
        public override string ToString() => Title;
    }

    private sealed record Part(string Text, int From, int To, Lap? Lap)
    {
        public override string ToString() => Text;
    }

    /// <summary>A row of Compare's corner table: a brake zone of this run and the other's nearest one.</summary>
    private sealed record Corner(int Number, double X, double XEnd, double? BrakePoint, string Peak, string Entry, string Slowest, double? Delta);

    /// <param name="card">The tab's card maker (id, title, body, buttons top right): each folds, kept.</param>
    public SessionsView(AnalysisApplet applet, Func<string, string, UIElement, UIElement[], Border> card)
    {
        this.applet = applet;
        this.card = card;
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
        sessions.ItemsSource = items.Where(i => !i.Imported).ToList();
        var pick = items.FirstOrDefault(i => !i.Imported && i.Key.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault(i => !i.Imported);
        sessions.SelectedItem = pick;
        filling = false;
        FillOtherSessions();
        if (pick == null) { current = null; ShowSession(null); }
        else if (Same(pick, current)) current = pick;
        else if (Host.IsVisible) Open(pick);
    }

    /// <summary>The tab shown: the session picked read, if it isn't yet.</summary>
    public void Shown()
    {
        if (current == null && sessions.SelectedItem is Item i) Open(i);
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
        filling = true;
        sessions.SelectedItem = item;
        filling = false;
        Open(item);
        return true;
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
        sessions.SelectionChanged += (_, _) => { if (!filling && sessions.SelectedItem is Item i) { wantLap = null; Open(i); } };
        var folder = new Button { Content = T("Open folder"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 4, 0) };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(SessionLog.Folder);
            Ui.Open("explorer.exe", $"\"{SessionLog.Folder}\"");
        };
        headCard = card("Session", T("Race Analysis"), head, new UIElement[] { sessions, folder });
        headCard.IsVisibleChanged += (_, _) => { if (headCard.IsVisible) Shown(); };
        Cards.Add(headCard);

        // the trace
        CheckBox Option(string text, bool on, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => { set(box.IsChecked == true); settings.Save(); chart.InvalidateVisual(); };
            return box;
        }
        chart.ShowSteering = settings.ShowSteering;
        chart.ShowClutch = settings.ShowClutch;
        chart.ShowSpeed = settings.ShowSpeed;
        chart.ZoneClicked += z => Pick(z, fromChart: true);
        Cards.Add(card("Trace", T("Trace"), chart, new UIElement[]
        {
            Option(T("Speed"), settings.ShowSpeed, on => settings.ShowSpeed = chart.ShowSpeed = on),
            Option(T("Steering"), settings.ShowSteering, on => settings.ShowSteering = chart.ShowSteering = on),
            Option(T("Clutch"), settings.ShowClutch, on => settings.ShowClutch = chart.ShowClutch = on),
        }));

        Cards.Add(BuildCompare());

        // the zones and applications over each other
        var align = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        align.Items.Add(new ComboBoxItem { Content = T("At the brake point"), Tag = false });
        align.Items.Add(new ComboBoxItem { Content = T("At the release"), Tag = true });
        align.SelectedIndex = settings.AlignRelease ? 1 : 0;
        align.ToolTip = T("Line the zones up where the brake goes on, or where the release starts");
        align.SelectionChanged += (_, _) => { settings.AlignRelease = align.SelectedIndex == 1; settings.Save(); ShowOverlays(); };
        brakeOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        throttleOverlay.Picked += o => { if (o is ThrottleMove m) PickThrottle(m); };
        Cards.Add(SideBySide(card("BrakesCompared", T("Brake zones compared"), brakeOverlay, new UIElement[] { align }),
            card("ThrottleCompared", T("Throttle applications compared"), throttleOverlay, Array.Empty<UIElement>())));

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
        Cards.Add(SideBySide(card("BrakeAnalysis", T("Brake Analysis"), brakeGrid, Array.Empty<UIElement>()),
            card("ThrottleAnalysis", T("Throttle Analysis"), throttleGrid, Array.Empty<UIElement>())));

        // Smoothness and Tips
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
        friction.Margin = new Thickness(0, 12, 0, 0);
        Cards.Add(SideBySide(card("Smoothness", T("Smoothness"), new StackPanel { Children = { smoothChart, friction } }, new UIElement[]
            {
                Chip(T("Brake"), settings.SmoothBrake, v => settings.SmoothBrake = smoothChart.ShowBrake = friction.ShowBrake = v),
                Chip(T("Throttle"), settings.SmoothThrottle, v => settings.SmoothThrottle = smoothChart.ShowThrottle = friction.ShowThrottle = v),
                Chip(T("On"), settings.SmoothOn, v => settings.SmoothOn = smoothChart.ShowOn = v),
                Chip(T("Off"), settings.SmoothOff, v => settings.SmoothOff = smoothChart.ShowOff = v),
            }),
            card("Tips", T("Tips"), tips, Array.Empty<UIElement>())));
    }

    /// <summary>Two cards sharing the width half and half.</summary>
    private static Grid SideBySide(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    /// <summary>A click on a row opens its moment (graph, numbers, notes) in a window.</summary>
    private void OpenOnClick(DataGrid grid)
    {
        grid.PreviewMouseLeftButtonUp += (_, e) =>
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
        chart.Analysis = a;
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
        FillThisParts();
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

    private void ShowTips(PedalAnalysis a)
    {
        foreach (var tip in a.Tips)
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
            else if (tip.Good) { text.Foreground = PedalColours.Frozen(PedalColours.Throttle); bullet.Foreground = text.Foreground; }
            row.Children.Add(text);
            tips.Children.Add(row);
        }
        if (a.Tips.Count == 0) tips.Children.Add(Ui.Note(T("Nothing stands out: keep it up."), 4));
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
            throttleOverlay.Show(null, Array.Empty<OverlayChart.Curve>(), PedalColours.Throttle, 0.3, 2);
            return;
        }
        var zones = a.Brakes.Where(z => z.Peak >= 0.15).ToList();
        if (settings.AlignRelease)
            brakeOverlay.Show(a.SmoothBrake, zones.Select(z => new OverlayChart.Curve(z.ReleaseFrom, z.End, z)).ToList(), PedalColours.Brake, 1, 2);
        else
            brakeOverlay.Show(a.SmoothBrake, zones.Select(z => new OverlayChart.Curve(z.Start, z.End, z)).ToList(), PedalColours.Brake, 0.3, 3);
        throttleOverlay.Show(a.SmoothThrottle, a.Throttle.Where(m => m.Up).Select(m => new OverlayChart.Curve(m.Start, m.End, m)).ToList(), PedalColours.Throttle, 0.3, 2);
    }

    private bool picking;

    private void Pick(BrakeZone z, bool fromChart = false)
    {
        picking = true;
        brakeGrid.SelectedItem = z;
        brakeGrid.ScrollIntoView(z);
        picking = false;
        brakeOverlay.Selected = z;
        brakeOverlay.InvalidateVisual();
        if (!fromChart) chart.ShowRange(z.Start, z.End, z);
    }

    private void PickThrottle(ThrottleMove m)
    {
        picking = true;
        throttleGrid.SelectedItem = m;
        throttleGrid.ScrollIntoView(m);
        picking = false;
        if (m.Up) { throttleOverlay.Selected = m; throttleOverlay.InvalidateVisual(); }
        chart.ShowRange(m.Start, m.End);
    }

    // ---------------------------------------------------------------- compare

    private Border BuildCompare()
    {
        var s = new StackPanel();
        var driver = new TextBox { Width = 130, Text = settings.Driver, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Your name, in what you export") };
        driver.LostFocus += (_, _) => { settings.Driver = driver.Text.Trim(); settings.Save(); };
        var driverRow = new StackPanel { Orientation = Orientation.Horizontal };
        driverRow.Children.Add(new TextBlock { Text = T("Driver"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        driverRow.Children.Add(driver);
        var export = new Button { Content = T("Export…"), Padding = new Thickness(8, 3, 8, 3), ToolTip = T("Save this lap (or session) to send to someone") };
        export.Click += (_, _) => Export();
        var import = new Button { Content = T("Import…"), Padding = new Thickness(8, 3, 8, 3), ToolTip = T("Add someone else's lap or session") };
        import.Click += (_, _) => Import();

        var picks = new WrapPanel();
        void Labelled(string label, params FrameworkElement[] e)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 6) };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            foreach (var x in e) { x.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(x); }
            picks.Children.Add(row);
        }
        Labelled(T("This"), thisPart);
        Labelled(T("With"), otherSession, otherPart);
        s.Children.Add(picks);
        thisPart.SelectionChanged += (_, _) => { if (!filling) ShowCompare(); };
        otherSession.SelectionChanged += async (_, _) => { if (!filling) await PickOther(); };
        otherPart.SelectionChanged += (_, _) => { if (!filling) ShowCompare(); };

        s.Children.Add(legend);
        compareStatus.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        s.Children.Add(compareStatus);
        compareChart.Margin = new Thickness(0, 8, 0, 0);
        foreach (var name in settings.CompareChannels) if (Enum.TryParse<Channel>(name, out var c)) compareChart.Channels.Add(c);
        foreach (var c in Enum.GetValues<Channel>()) if (!settings.CompareChannels.Contains(c.ToString())) compareChart.Channels.Remove(c);
        compareChart.RunClicked += which => { compareChart.Highlight = compareChart.Highlight == which ? 0 : which; ShowLegend(); compareChart.InvalidateVisual(); };
        s.Children.Add(compareChart);

        corners.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(Corner.Number)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Brake point"), Binding = new Binding(nameof(Corner.BrakePoint)) { StringFormat = "{0:+0;-0;0}" }, SortMemberPath = nameof(Corner.BrakePoint) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Peak"), Binding = new Binding(nameof(Corner.Peak)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Entry"), Binding = new Binding(nameof(Corner.Entry)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Slowest"), Binding = new Binding(nameof(Corner.Slowest)) });
        corners.Columns.Add(new DataGridTextColumn { Header = T("Delta"), Binding = new Binding(nameof(Corner.Delta)) { StringFormat = "{0:+0.00;-0.00;0.00} s" }, SortMemberPath = nameof(Corner.Delta), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        corners.SelectionChanged += (_, _) => { if (corners.SelectedItem is Corner c) compareChart.ShowRange(c.X, c.XEnd); };
        s.Children.Add(corners);
        driverRow.Margin = import.Margin = export.Margin = new Thickness(0, 0, 6, 0);
        return card("Compare", T("Compare"), s, new UIElement[] { driverRow, import, export });
    }

    private static List<Part> PartsOf(PedalAnalysis a)
    {
        var parts = new List<Part> { new(T("Whole session"), 0, Math.Max(0, a.Data.Count - 1), null) };
        var best = a.Data.BestLap;
        foreach (var lap in a.Data.Laps)
            parts.Add(new Part(lap == best ? lap.Text + "  ★" : lap.Text, lap.Start, lap.End, lap));
        return parts;
    }

    private static Part DefaultPart(List<Part> parts) => parts.FirstOrDefault(p => p.Text.EndsWith('★')) ?? parts[0];

    private void FillThisParts()
    {
        filling = true;
        thisPart.Items.Clear();
        if (analysis != null)
        {
            var parts = PartsOf(analysis);
            foreach (var p in parts) thisPart.Items.Add(p);
            SelectLap(thisPart, wantLap);
            wantLap = null;
        }
        filling = false;
        ShowCompare();
    }

    private void FillOtherSessions()
    {
        string? keep = (otherSession.SelectedItem as Item)?.Key;
        filling = true;
        otherSession.Items.Clear();
        otherSession.Items.Add(T("None"));
        // (the same track's first)
        foreach (var i in items.OrderBy(i => i.Track != null && i.Track == current?.Track ? 0 : i.Imported ? 1 : 2)) otherSession.Items.Add(i);
        otherSession.SelectedItem = items.FirstOrDefault(i => i.Key == keep) ?? (object)otherSession.Items[0];
        filling = false;
    }

    private async Task PickOther(Item? pick = null, int? lap = null)
    {
        if (pick != null)
        {
            filling = true;
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
                if (lap == null && other == analysis && thisPart.SelectedItem is Part mine && otherPart.SelectedItem is Part theirs && mine.From == theirs.From)
                    otherPart.SelectedItem = parts.FirstOrDefault(p => p.Lap is { Complete: true } && p.From != mine.From) ?? theirs;
                filling = false;
            }
        }
        ShowCompare();
    }

    private Run? RunOf(PedalAnalysis? a, object? part, string label) =>
        a != null && part is Part p && p.To > p.From ? new Run { Analysis = a, From = p.From, To = p.To, Label = label } : null;

    private void ShowCompare()
    {
        string thisLabel = (current?.Imported == true ? current.Driver + " · " : "") + (thisPart.SelectedItem as Part)?.Text?.TrimEnd('★', ' ');
        string otherLabel = otherSession.SelectedItem is Item i ? (i.Imported ? i.Driver + " · " : Same(i, current) ? "" : i.Title + " · ") + (otherPart.SelectedItem as Part)?.Text?.TrimEnd('★', ' ') : "";
        var a = RunOf(analysis, thisPart.SelectedItem, thisLabel);
        var b = otherSession.SelectedItem is Item ? RunOf(other, otherPart.SelectedItem, otherLabel) : null;
        compareChart.Highlight = 0;
        compareChart.Show(a, b);
        ShowLegend(a, b);
        corners.ItemsSource = a != null && b != null ? Corners(a, b) : null;
        corners.Visibility = b != null ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>The legend: each run's colour and name (a click highlights it white) and a box to show or hide it;
    /// then what the chart shows.</summary>
    private void ShowLegend(Run? a = null, Run? b = null)
    {
        a ??= lastA;
        b ??= lastB;
        lastA = a;
        lastB = b;
        legend.Children.Clear();
        void Entry(Run r, int which, Color colour)
        {
            bool shown = which == 1 ? compareChart.ShowA : compareChart.ShowB;
            var box = new CheckBox { IsChecked = shown, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = T("Show") };
            box.Click += (_, _) =>
            {
                if (which == 1) compareChart.ShowA = box.IsChecked == true; else compareChart.ShowB = box.IsChecked == true;
                compareChart.InvalidateVisual();
            };
            bool hi = compareChart.Highlight == which;
            var swatch = new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(2), Background = hi ? Brushes.White : PedalColours.Frozen(colour), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = r.Label, VerticalAlignment = VerticalAlignment.Center };
            var tag = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 10, 3), Cursor = Cursors.Hand, ToolTip = T("Highlight"),
                BorderThickness = new Thickness(1), BorderBrush = PedalColours.Frozen(colour, 0x90),
                Background = hi ? PedalColours.Frozen(Colors.White, 0x30) : Brushes.Transparent,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, name } },
            };
            if (hi) name.FontWeight = FontWeights.SemiBold;
            tag.MouseLeftButtonUp += (_, _) => { compareChart.Highlight = hi ? 0 : which; ShowLegend(); compareChart.InvalidateVisual(); };
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };
            entry.Children.Add(box);
            entry.Children.Add(tag);
            legend.Children.Add(entry);
        }
        if (a != null) Entry(a, 1, PedalColours.RunA);
        if (b != null) Entry(b, 2, PedalColours.RunB);
        if (a == null) return;
        var spacer = new Border { Width = 10 };
        legend.Children.Add(spacer);
        foreach (var c in Enum.GetValues<Channel>())
        {
            if (c == Channel.Delta && b == null) continue;
            string text = c switch { Channel.Delta => T("Delta"), Channel.Speed => T("Speed"), Channel.Throttle => T("Throttle"), Channel.Brake => T("Brake"), _ => T("Steering") };
            var chip = Ui.Chip(text, compareChart.Channels.Contains(c), c == Channel.Delta ? T("The other's time less this one's: above the line, this one is ahead") : null);
            chip.Margin = new Thickness(0, 0, 6, 4);
            Ui.OnToggle(chip, on =>
            {
                if (on) compareChart.Channels.Add(c); else compareChart.Channels.Remove(c);
                settings.CompareChannels = compareChart.Channels.Select(x => x.ToString()).ToList();
                compareChart.InvalidateVisual();
            });
            legend.Children.Add(chip);
        }
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
        if (analysis == null || current == null || thisPart.SelectedItem is not Part part) return;
        string driver = string.IsNullOrWhiteSpace(settings.Driver) ? Environment.UserName : settings.Driver;
        string name = current.Title;
        string suggested = string.Join("_", $"{driver} - {name}{(part.Lap != null ? " - " + F("Lap {0}", part.Lap.Number + 1) : "")}".Split(Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = suggested + ".csv", Filter = T("Session") + " (*.csv)|*.csv", DefaultExt = ".csv" };
        if (dialog.ShowDialog(Window.GetWindow(Host)) != true) return;
        try
        {
            long from = (long)part.From * 1000 / SessionData.Hz, to = (long)part.To * 1000 / SessionData.Hz;
            var output = new StringBuilder();
            output.AppendLine("# ForzaWheel session,2");
            output.AppendLine("# started," + analysis.Data.Started.AddMilliseconds(from).ToString("o", CultureInfo.InvariantCulture));
            output.AppendLine("# driver," + driver.Replace('\n', ' '));
            output.AppendLine("# name," + (name + (part.Lap != null ? " · " + part.Text.TrimEnd('★', ' ') : "")).Replace('\n', ' '));
            string? car = null;
            var rows = new StringBuilder();
            foreach (var line in File.ReadLines(current.File.FullName))
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
        if (analysis != null && items.FirstOrDefault(i => i.Key == first) is { } imported) await PickOther(imported);
    }

    // ---------------------------------------------------------------- helpers
}
