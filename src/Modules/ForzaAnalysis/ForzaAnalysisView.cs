using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>
/// The Forza Analysis tab. Left: the sessions (newest first, with their length and score), delete and the folder.
/// Right, for the session picked: its scores and time split, the trace (zoom and move along it), the brake zones and
/// throttle applications drawn over each other to compare, tables of each with their numbers and notes, and tips.
/// </summary>
internal sealed class ForzaAnalysisView : UserControl
{
    private const int LeftWidth = 300;

    private readonly ForzaAnalysisSettings settings;
    private readonly DataGrid sessions = new() { IsReadOnly = true, MinHeight = 200, MaxHeight = 600 };
    private readonly TextBlock listStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel right = new();
    private readonly TextBox nameBox = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
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
    private readonly TextBlock busy = new() { Margin = new Thickness(0, 0, 0, 8) };
    private PedalAnalysis? analysis;
    private CancellationTokenSource? loading;

    /// <summary>A row of the session list.</summary>
    private sealed class Item : INotifyPropertyChanged
    {
        public required FileInfo File { get; init; }
        public required DateTime Started { get; init; }
        public string? Name { get; set; }
        public SessionSummary? Summary { get; set; }
        public string When => string.IsNullOrWhiteSpace(Name) ? Started.ToString("g") : Name + "\n" + Started.ToString("g");
        public string Length => Summary is { } s ? SessionChart.Clock(s.ActiveSeconds) : "…";
        public double? Score => Summary?.Overall is { } o ? Math.Round(o) : null;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public ForzaAnalysisView(ForzaAnalysisSettings settings)
    {
        this.settings = settings;

        // ---- left: the sessions
        var left = new StackPanel();
        var list = new StackPanel();
        list.Children.Add(Ui.Header(T("Sessions")));
        list.Children.Add(Secondary(T("ForzaWheel logs a session while its input overlay shows (Record a session log, in its Input overlay block): it starts when something moves and ends after two minutes of nothing.")));
        sessions.Margin = new Thickness(0, 8, 0, 0);
        sessions.Columns.Add(new DataGridTextColumn { Header = T("Session"), Binding = new Binding(nameof(Item.When)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), SortMemberPath = nameof(Item.Started) });
        sessions.Columns.Add(new DataGridTextColumn { Header = T("Driving"), Binding = new Binding(nameof(Item.Length)), Width = DataGridLength.Auto });
        sessions.Columns.Add(new DataGridTextColumn { Header = T("Score"), Binding = new Binding(nameof(Item.Score)), Width = DataGridLength.Auto });
        sessions.SelectionChanged += (_, _) => { if (sessions.SelectedItem is Item i) Open(i); };
        list.Children.Add(sessions);
        list.Children.Add(listStatus);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SmallButton(T("Refresh"), RefreshList));
        buttons.Children.Add(SmallButton(T("Delete"), Delete, confirm: true));
        buttons.Children.Add(SmallButton(T("Open folder"), () =>
        {
            Directory.CreateDirectory(SessionLog.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SessionLog.Folder}\"") { UseShellExecute = true });
        }));
        list.Children.Add(buttons);
        left.Children.Add(Ui.Card(list));
        left.Children.Add(Ui.Card(HowCard()));
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ---- right: the session
        BuildRight();
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(leftScroll);
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);
        Content = grid;

        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshList(); };
        ShowSession(null);
    }

    // ---------------------------------------------------------------- the session list

    private bool summarising;

    private void RefreshList()
    {
        var files = SessionLog.List();
        string? keep = (sessions.SelectedItem as Item)?.File.Name ?? settings.Selected;
        var items = files.Select(f => new Item
        {
            File = f,
            Started = SessionLog.StartedOf(f),
            Name = settings.Names.GetValueOrDefault(f.Name),
            Summary = settings.Summaries.GetValueOrDefault(f.Name) is { } s && s.Version == PedalAnalysis.Version && s.Size == f.Length ? s : null,
        }).ToList();
        // summaries of sessions that are gone
        foreach (var gone in settings.Summaries.Keys.Where(k => files.All(f => !f.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList()) settings.Summaries.Remove(gone);
        sessions.ItemsSource = items;
        listStatus.Text = items.Count == 0
            ? T("No sessions yet. In ForzaWheel, show the input overlay and drive: the session appears here.")
            : P(items.Count, "{0} session", "{0} sessions");
        var pick = items.FirstOrDefault(i => i.File.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        if (pick != null && sessions.SelectedItem != pick) sessions.SelectedItem = pick;
        _ = SummariseAsync(items);
    }

    /// <summary>Works out the summaries the list is missing, one at a time in the background.</summary>
    private async Task SummariseAsync(List<Item> items)
    {
        if (summarising) return;
        summarising = true;
        try
        {
            foreach (var item in items.Where(i => i.Summary == null))
            {
                // one being written now: later
                if (DateTime.Now - item.File.LastWriteTime < TimeSpan.FromSeconds(10)) continue;
                var summary = await Task.Run(() => { try { return Summarise(new PedalAnalysis(SessionData.Load(item.File.FullName)), item.File.Length); } catch { return null; } });
                if (summary == null) continue;
                item.Summary = summary;
                settings.Summaries[item.File.Name] = summary;
                item.Changed();
            }
            settings.Save();
        }
        finally { summarising = false; }
    }

    private static SessionSummary Summarise(PedalAnalysis a, long size) => new()
    {
        Version = PedalAnalysis.Version,
        Size = size,
        Seconds = a.Data.Seconds,
        ActiveSeconds = a.ActiveS,
        Overall = Nullable(a.OverallScore),
        BrakeRelease = Nullable(a.BrakeReleaseScore),
        ThrottleApply = Nullable(a.ThrottleApplyScore),
        Zones = a.Brakes.Count,
    };

    private static double? Nullable(double v) => double.IsNaN(v) ? null : v;

    private void Delete()
    {
        if (sessions.SelectedItem is not Item item) return;
        try { item.File.Delete(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), F("It couldn't be deleted: {0}", ex.Message), T("Forza Analysis")); return; }
        settings.Names.Remove(item.File.Name);
        settings.Summaries.Remove(item.File.Name);
        settings.Selected = null;
        settings.Save();
        RefreshList();
    }

    private async void Open(Item item)
    {
        settings.Selected = item.File.Name;
        loading?.Cancel();
        var cancel = loading = new CancellationTokenSource();
        busy.Text = T("Analysing…");
        busy.Visibility = Visibility.Visible;
        PedalAnalysis? a = null;
        string? problem = null;
        try { a = await Task.Run(() => new PedalAnalysis(SessionData.Load(item.File.FullName))); }
        catch (Exception ex) { problem = ex.Message; }
        if (cancel.IsCancellationRequested) return;
        busy.Visibility = Visibility.Collapsed;
        if (a == null) { ShowSession(null, F("The session couldn't be read: {0}", problem)); return; }
        current = item;
        if (item.Summary == null || item.Summary.Size != item.File.Length)
        {
            item.Summary = settings.Summaries[item.File.Name] = Summarise(a, item.File.Length);
            item.Changed();
        }
        ShowSession(a);
    }

    private Item? current;

    // ---------------------------------------------------------------- the session

    private void BuildRight()
    {
        right.Children.Add(busy);

        var head = new StackPanel();
        nameBox.ToolTip = T("A name for this session (the car, the track…)");
        nameBox.LostFocus += (_, _) => SaveName();
        nameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveName(); };
        head.Children.Add(nameBox);
        about.SetResourceReference(StyleProperty, "SecondaryText");
        head.Children.Add(about);
        head.Children.Add(scores);
        head.Children.Add(split);
        head.Children.Add(splitLegend);
        head.Children.Add(numbers);
        right.Children.Add(Ui.Card(head));

        var trace = new StackPanel();
        var traceHead = new DockPanel();
        var options = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(options, Dock.Right);
        var steer = new CheckBox { Content = T("Steering"), IsChecked = settings.ShowSteering, Margin = new Thickness(12, 0, 0, 0) };
        steer.Click += (_, _) => { settings.ShowSteering = chart.ShowSteering = steer.IsChecked == true; chart.InvalidateVisual(); };
        var clutch = new CheckBox { Content = T("Clutch"), IsChecked = settings.ShowClutch, Margin = new Thickness(12, 0, 0, 0) };
        clutch.Click += (_, _) => { settings.ShowClutch = chart.ShowClutch = clutch.IsChecked == true; chart.InvalidateVisual(); };
        chart.ShowSteering = settings.ShowSteering;
        chart.ShowClutch = settings.ShowClutch;
        options.Children.Add(steer);
        options.Children.Add(clutch);
        traceHead.Children.Add(options);
        traceHead.Children.Add(Ui.Header(T("Trace")));
        trace.Children.Add(traceHead);
        trace.Children.Add(Secondary(T("Throttle green, brake red, steering about the middle line; brake zones are shaded and numbered, and the marks at the top are worth a look (point at one). Mouse wheel zooms, drag moves along, a double-click shows it all; click a brake zone to pick it. The strip under it is the whole session.")));
        chart.Margin = new Thickness(0, 8, 0, 0);
        chart.ZoneClicked += z => Pick(z, fromChart: true);
        trace.Children.Add(chart);
        right.Children.Add(Ui.Card(trace));

        var compare = new Grid();
        compare.ColumnDefinitions.Add(new ColumnDefinition());
        compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        compare.ColumnDefinitions.Add(new ColumnDefinition());
        var bl = new StackPanel();
        var brakeHead = new DockPanel();
        var align = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        align.Items.Add(new ComboBoxItem { Content = T("At the brake point"), Tag = false });
        align.Items.Add(new ComboBoxItem { Content = T("At the release"), Tag = true });
        align.SelectedIndex = settings.AlignRelease ? 1 : 0;
        align.ToolTip = T("Line the zones up where the brake goes on, to compare the applications, or where the release starts, to compare the releases");
        align.SelectionChanged += (_, _) => { settings.AlignRelease = align.SelectedIndex == 1; ShowOverlays(); };
        DockPanel.SetDock(align, Dock.Right);
        brakeHead.Children.Add(align);
        brakeHead.Children.Add(Ui.Header(T("Brake zones compared")));
        bl.Children.Add(brakeHead);
        bl.Children.Add(Secondary(T("Every zone over each other: the dashed line is the average, the picked one is bold. A good one is a quick, firm rise and a long, even fall.")));
        brakeOverlay.Margin = new Thickness(0, 8, 0, 0);
        brakeOverlay.Picked += o => { if (o is BrakeZone z) Pick(z); };
        bl.Children.Add(brakeOverlay);
        compare.Children.Add(Ui.Card(bl));
        var tl = new StackPanel();
        tl.Children.Add(Ui.Header(T("Throttle applications compared")));
        tl.Children.Add(Secondary(T("Every time the throttle goes on, from where it started: a good one rises in one smooth, progressive movement.")));
        throttleOverlay.Margin = new Thickness(0, 8, 0, 0);
        throttleOverlay.Picked += o => { if (o is ThrottleMove m) PickThrottle(m); };
        tl.Children.Add(throttleOverlay);
        var tc = Ui.Card(tl);
        Grid.SetColumn(tc, 2);
        compare.Children.Add(tc);
        right.Children.Add(compare);

        var bz = new StackPanel();
        bz.Children.Add(Ui.Header(T("Brake zones")));
        bz.Children.Add(Secondary(T("Apply: from 10% to 90% of the peak. Hold: at 90% or more of the level held. Release: from there to letting go. Smooth: 100% is a release in one movement. Trail: how much of the release was with the wheel turned. Coast: from letting go to the throttle.")));
        brakeGrid.Margin = new Thickness(0, 8, 0, 0);
        Column(brakeGrid, "#", nameof(BrakeZone.Number), null);
        Column(brakeGrid, T("At"), nameof(BrakeZone.Clock), null, sort: nameof(BrakeZone.Start));
        Column(brakeGrid, T("Peak"), nameof(BrakeZone.PeakPct), "{0:0}%");
        Column(brakeGrid, T("Apply"), nameof(BrakeZone.ApplyMs), "{0:0} ms");
        Column(brakeGrid, T("Hold"), nameof(BrakeZone.HoldS), "{0:0.00} s");
        Column(brakeGrid, T("Release"), nameof(BrakeZone.ReleaseS), "{0:0.00} s");
        Column(brakeGrid, T("Smooth"), nameof(BrakeZone.ReleaseSmoothPct), "{0:0}%");
        Column(brakeGrid, T("Trail"), nameof(BrakeZone.Trail), "{0:0}%");
        Column(brakeGrid, T("Coast"), nameof(BrakeZone.Coast), "{0:0.00} s");
        Column(brakeGrid, T("Apply score"), nameof(BrakeZone.ApplyScore), "{0:0}");
        Column(brakeGrid, T("Release score"), nameof(BrakeZone.ReleaseScore), "{0:0}");
        Column(brakeGrid, T("Notes"), nameof(BrakeZone.NotesText), null, star: true);
        brakeGrid.SelectionChanged += (_, _) => { if (brakeGrid.SelectedItem is BrakeZone z && !picking) Pick(z); };
        bz.Children.Add(brakeGrid);
        right.Children.Add(Ui.Card(bz));

        var th = new StackPanel();
        th.Children.Add(Ui.Header(T("Throttle")));
        th.Children.Add(Secondary(T("Every application (on) and release (off) of a quarter of the travel or more. Time: from 10% to 90% of the way. Smooth: 100% is in one movement.")));
        throttleGrid.Margin = new Thickness(0, 8, 0, 0);
        Column(throttleGrid, T("At"), nameof(ThrottleMove.Clock), null, sort: nameof(ThrottleMove.Start));
        Column(throttleGrid, T("Way"), nameof(ThrottleMove.Way), null);
        Column(throttleGrid, T("From"), nameof(ThrottleMove.FromPct), "{0:0}%");
        Column(throttleGrid, T("To"), nameof(ThrottleMove.ToPct), "{0:0}%");
        Column(throttleGrid, T("Time"), nameof(ThrottleMove.Ms), "{0:0} ms");
        Column(throttleGrid, T("Smooth"), nameof(ThrottleMove.SmoothPct), "{0:0}%");
        Column(throttleGrid, T("Score"), nameof(ThrottleMove.Score), "{0:0}");
        Column(throttleGrid, T("Notes"), nameof(ThrottleMove.NotesText), null, star: true);
        throttleGrid.SelectionChanged += (_, _) => { if (throttleGrid.SelectedItem is ThrottleMove m && !picking) PickThrottle(m); };
        th.Children.Add(throttleGrid);
        right.Children.Add(Ui.Card(th));

        var tp = new StackPanel();
        tp.Children.Add(Ui.Header(T("Tips")));
        tp.Children.Add(tips);
        right.Children.Add(Ui.Card(tp));
    }

    private static void Column(DataGrid grid, string header, string path, string? format, string? sort = null, bool star = false)
    {
        var b = new Binding(path);
        if (format != null) b.StringFormat = format;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = b,
            SortMemberPath = sort ?? path,
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) { } : DataGridLength.Auto,
            MinWidth = star ? 160 : 0,
        });
    }

    private void SaveName()
    {
        if (current == null) return;
        string name = nameBox.Text.Trim();
        if (name.Length == 0 || name == current.Started.ToString("g")) settings.Names.Remove(current.File.Name);
        else settings.Names[current.File.Name] = name;
        current.Name = settings.Names.GetValueOrDefault(current.File.Name);
        current.Changed();
        settings.Save();
    }

    private void ShowSession(PedalAnalysis? a, string? problem = null)
    {
        analysis = a;
        chart.Analysis = a;
        scores.Children.Clear();
        numbers.Children.Clear();
        tips.Children.Clear();
        split.Children.Clear();
        split.ColumnDefinitions.Clear();
        splitLegend.Children.Clear();
        brakeGrid.ItemsSource = a?.Brakes;
        throttleGrid.ItemsSource = a?.Throttle;
        ShowOverlays();
        nameBox.IsEnabled = a != null;
        if (a == null)
        {
            nameBox.Text = T("No session");
            about.Text = problem ?? T("Pick a session on the left.");
            return;
        }
        nameBox.Text = current?.Name ?? a.Data.Started.ToString("g");
        int ups = a.Throttle.Count(m => m.Up);
        about.Text = string.Join(" · ", a.Data.Started.ToString("f"),
            F("{0} long, {1} driving", SessionChart.Clock(a.Data.Seconds), SessionChart.Clock(a.ActiveS)),
            P(a.Brakes.Count, "{0} brake zone", "{0} brake zones"), P(ups, "{0} throttle application", "{0} throttle applications"));

        Tile(T("Overall"), a.OverallScore, T("The scores below together, the brake release counting most"));
        Tile(T("Brake application"), a.BrakeApplyScore, T("How firmly and cleanly the brake goes on: in one movement, without slowly building or spiking and backing off"));
        Tile(T("Brake release"), a.BrakeReleaseScore, T("How gradually and cleanly the brake comes off: one even movement, not let go of at the end or pressed again"));
        Tile(T("Throttle application"), a.ThrottleApplyScore, T("How progressively the throttle goes on: in one movement, without lifting on the way or stabbing it"));
        Tile(T("Throttle release"), a.ThrottleReleaseScore, T("How cleanly the throttle comes off: in one movement"));
        Tile(T("Steering"), a.SteeringScore, T("Fewer small corrections score higher"));
        Tile(T("Consistency"), a.ConsistencyScore, T("How alike the brake zones are: peak, application and release times (4 zones or more)"));

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

        foreach (var tip in a.Tips)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap });
            tips.Children.Add(row);
        }
        if (a.Tips.Count == 0) tips.Children.Add(Secondary(T("Nothing stands out: keep it up.")));
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

    // ---------------------------------------------------------------- how it's scored

    private static UIElement HowCard()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Header(T("What it looks at")));
        s.Children.Add(Secondary(T("Brake: a quick, firm application to the pressure you need (no slow build-up, no spike and back off), a steady hold, then a long, even release into the corner (trail braking), not let go of in one go at the end or pressed again.")));
        s.Children.Add(Secondary(T("Throttle: on progressively in one movement out of the corner, without lifting on the way or stabbing it; off cleanly.")));
        s.Children.Add(Secondary(T("Also: throttle and brake together, coasting between them, the brake held at 100%, small steering corrections, and how alike the brake zones are.")));
        s.Children.Add(Secondary(T("Scores are 0 to 100: green 85 and over, amber 65 and over.")));
        return s;
    }

    // ---------------------------------------------------------------- helpers (as ForzaWheel's)

    private static TextBlock Secondary(string text) => Ui.Note(text, 4);

    /// <summary>A small button; with <paramref name="confirm"/>, Core's Confirm? first (something that can't be undone).</summary>
    private static Button SmallButton(string text, Action click, bool confirm = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        if (confirm) ConfirmButton.Arm(b, click); else b.Click += (_, _) => click();
        return b;
    }
}
