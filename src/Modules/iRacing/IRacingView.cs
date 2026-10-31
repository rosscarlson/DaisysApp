using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// The iRacing tab: you and your licences, Sync (what's new since last time, everything the first time), filters
/// (category, time, official only), then for the races shown: tiles of totals and rates, a summary in words, your
/// iRating and safety rating over time, races a month, a year of race days, when in the week you race, where you
/// start and finish, places gained, incidents, the tracks, cars and series you race most, records, and tables of
/// it all (tracks, layouts, cars, series, seasons, years, iRacing's own totals, every race).
/// </summary>
internal sealed partial class IRacingView : UserControl
{
    private readonly IRacingSettings settings;
    private readonly IRacingApi api;
    private RaceData data = RaceData.Load();
    private RaceStats? stats;
    private CancellationTokenSource? syncing;
    private bool built, loaded;

    // header
    private readonly TextBlock name = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock about = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel licences = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button syncButton = new() { Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button cancelButton = new() { Padding = new Thickness(10, 5, 10, 5), Visibility = Visibility.Collapsed };
    private readonly TextBlock syncStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 420 };
    private readonly ProgressBar progress = new() { Height = 4, Margin = new Thickness(0, 6, 0, 0), Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Border setupCard;
    /// <summary>The header's left side (name, about, licences): the export's top.</summary>
    private readonly StackPanel headerLeft = new();
    /// <summary>The tables' card: the export has its tables as HTML tables instead.</summary>
    private Border? tablesCard;

    // filters
    private readonly ComboBox category = new() { MinWidth = 150, Margin = new Thickness(6, 0, 18, 0) };
    private readonly ComboBox range = new() { MinWidth = 150, Margin = new Thickness(6, 0, 18, 0) };
    private readonly CheckBox official = new() { Content = T("Official races only"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock shown = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };

    // the dashboard
    private readonly StackPanel dashboard = new();
    private readonly WrapPanel tiles = new();
    private readonly StackPanel summary = new();
    private readonly Donut categories = new() { Height = 170 };
    private readonly Donut outcomes = new() { Height = 170 };
    private readonly TimeChart iRating = new() { Height = 260 };
    private readonly TimeChart safety = new() { Height = 200 };
    private readonly ColumnChart months = new() { Height = 220 };
    private readonly CalendarHeat calendar = new();
    private readonly WeekHeat week = new();
    private readonly ColumnChart finishes = new() { Height = 200 };
    private readonly ColumnChart startsChart = new() { Height = 200 };
    private readonly ColumnChart gains = new() { Height = 200 };
    private readonly ColumnChart incidents = new() { Height = 200 };
    private readonly BarList trackLaps = new();
    private readonly BarList trackAllLaps = new();
    private readonly BarList carAllLaps = new();
    private readonly WrapPanel trackTime = new();
    private readonly ComboBox session = new() { MinWidth = 150, Margin = new Thickness(6, 0, 18, 0) };
    private readonly ComboBox trackFilter = new() { Width = 230, Margin = new Thickness(6, 0, 18, 0) };
    private readonly ComboBox carFilter = new() { Width = 230, Margin = new Thickness(6, 0, 18, 0) };
    private readonly BarList carRaces = new();
    private readonly BarList seriesRaces = new();
    private readonly BarList trackWins = new();
    private readonly BarList trackFinish = new();
    private readonly BarList carIRating = new();
    private readonly WrapPanel records = new();
    private readonly WrapPanel extra = new();
    private readonly TextBlock calendarNote = new();

    // tables
    private readonly DataGrid tracksGrid = Grid(), layoutsGrid = Grid(), carsGrid = Grid(), seriesGrid = Grid(), seasonsGrid = Grid(), yearsGrid = Grid(), classesGrid = Grid();
    private readonly DataGrid careerGrid = Grid(), yearlyGrid = Grid(), racesGrid = Grid();
    private readonly TextBox search = new() { Width = 260, Margin = new Thickness(0, 0, 0, 6) };

    public IRacingView(IRacingSettings settings)
    {
        this.settings = settings;
        api = new IRacingApi(settings);
        api.Status += s => Dispatcher.BeginInvoke(() => syncStatus.Text = s);

        var root = new StackPanel();
        root.Children.Add(Ui.Card(Header()));
        setupCard = Ui.Card(Setup());
        root.Children.Add(setupCard);
        root.Children.Add(Ui.Card(Filters()));
        root.Children.Add(dashboard);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            if (!built) Build();
            ShowHeader();
            if (!loaded) { loaded = true; Refresh(); }
            if (settings.SyncWhenOpened && settings.HasCredentials && syncing == null
                && (data.Synced is not { } last || DateTime.UtcNow - last > TimeSpan.FromHours(Math.Max(1, settings.SyncHours))))
                _ = SyncAsync();
        };
    }

    /// <summary>The settings changed (signing in): show it.</summary>
    public void SettingsChanged()
    {
        api.SignOut();
        if (built) ShowHeader();
    }

    /// <summary>The races kept were deleted (Settings): start again.</summary>
    public void DataDeleted()
    {
        syncing?.Cancel();
        data = new RaceData();
        if (built) { ShowHeader(); Refresh(); }
    }

    // ---------------------------------------------------------------- the header and syncing

    private UIElement Header()
    {
        var dock = new DockPanel();
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 260 };
        DockPanel.SetDock(right, Dock.Right);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        syncButton.Content = T("Sync");
        syncButton.SetResourceReference(StyleProperty, "AccentButton");
        syncButton.ToolTip = T("Fetch your races from iRacing: the ones since the last time (all of them the first time), your ratings and totals");
        syncButton.Click += (_, _) => _ = SyncAsync();
        cancelButton.Content = T("Stop");
        cancelButton.Click += (_, _) => syncing?.Cancel();
        var exportButton = new Button { Content = T("Export"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = T("Save this page as a web page to share") };
        exportButton.Click += (_, _) => ExportPage();
        var importButton = new Button { Content = T("Import"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = T("Read in an export made in the browser with the export script (iracing_raw.json, or races_all.csv): see Settings → Gaming → iRacing") };
        importButton.Click += (_, _) => ImportFile();
        var settingsButton = new Button { Content = T("Settings"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0) };
        settingsButton.Click += (_, _) => AppNavigation.OpenSettings("iRacing");
        buttons.Children.Add(syncButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(exportButton);
        buttons.Children.Add(importButton);
        buttons.Children.Add(settingsButton);
        right.Children.Add(buttons);
        right.Children.Add(progress);
        syncStatus.SetResourceReference(StyleProperty, "SecondaryText");
        syncStatus.TextAlignment = TextAlignment.Right;
        right.Children.Add(syncStatus);
        dock.Children.Add(right);

        var left = headerLeft;
        left.Children.Add(name);
        about.SetResourceReference(StyleProperty, "SecondaryText");
        left.Children.Add(about);
        left.Children.Add(licences);
        dock.Children.Add(left);
        return dock;
    }

    private UIElement Setup()
    {
        var s = new StackPanel();
        s.Children.Add(HeaderText(T("Connect to iRacing")));
        s.Children.Add(Secondary(T("Two ways in. Without an OAuth client: export your history in the browser with the export script (Settings → Gaming → iRacing has it, and the steps), then Import it here. With one (a client ID and secret from iRacing): fill it in with your email and password under Settings → Gaming → iRacing, then Sync: the first time it fetches every session since you joined, later only what's new.")));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var import = new Button { Content = T("Import"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        import.Click += (_, _) => ImportFile();
        var open = new Button { Content = T("Open the settings"), Padding = new Thickness(12, 4, 12, 4) };
        open.Click += (_, _) => AppNavigation.OpenSettings("iRacing");
        row.Children.Add(import);
        row.Children.Add(open);
        s.Children.Add(row);
        return s;
    }

    private void ShowHeader()
    {
        setupCard.Visibility = settings.HasCredentials || data.Races.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        syncButton.IsEnabled = settings.HasCredentials && syncing == null;
        name.Text = data.Name.Length > 0 ? data.Name : T("iRacing");
        var parts = new List<string>();
        if (data.CustId > 0) parts.Add(F("Member {0}", data.CustId));
        if (data.MemberSince is { } since) parts.Add(F("since {0:d}", since));
        if (data.Club.Length > 0) parts.Add(data.Club);
        if (data.Races.Count > 0) parts.Add(P(data.Races.Count, "{0} session kept", "{0} sessions kept") + F(" ({0} races)", data.Races.Count(r => r.IsRace)));
        about.Text = parts.Count > 0 ? string.Join(" · ", parts) : T("Not synced yet.");
        if (syncing == null)
            syncStatus.Text = string.Join("\n", new[]
            {
                data.Synced is { } synced ? F("Last synced {0:g}", synced.ToLocalTime()) : settings.HasCredentials ? T("Not synced yet: press Sync.") : "",
                data.Imported is { } imported ? F("Last imported {0:g}", imported.ToLocalTime()) : "",
            }.Where(x => x.Length > 0));

        licences.Children.Clear();
        foreach (var l in data.Licenses)
        {
            var b = new Border { Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 1) };
            b.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            if (l.Color.Length == 6 || l.Color.Length == 7)
                try { b.BorderBrush = ChartBase.Hex(l.Color.StartsWith('#') ? l.Color : "#" + l.Color); b.BorderThickness = new Thickness(4, 1, 1, 1); } catch { }
            var st = new StackPanel();
            var cat = new TextBlock { Text = l.Category, FontSize = 12 };
            cat.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            st.Children.Add(cat);
            string cls = l.Class.Replace("Class ", "", StringComparison.OrdinalIgnoreCase);
            st.Children.Add(new TextBlock { Text = $"{cls} {l.SafetyRating:0.00}", FontSize = 15, FontWeight = FontWeights.SemiBold });
            st.Children.Add(new TextBlock { Text = l.IRating > 0 ? F("{0} iR", l.IRating) : T("no iRating yet"), FontSize = 12 });
            b.Child = st;
            b.ToolTip = F("{0}: {1}, safety rating {2:0.00}", l.Category, l.Class, l.SafetyRating) + "\n" + F("iRating {0}, TT rating {1}, CPI {2:0.0}", l.IRating, l.TTRating, l.Cpi);
            licences.Children.Add(b);
        }
    }

    private async Task SyncAsync()
    {
        if (syncing != null || !settings.HasCredentials) return;
        var cancel = syncing = new CancellationTokenSource();
        syncButton.IsEnabled = false;
        cancelButton.Visibility = Visibility.Visible;
        progress.Visibility = Visibility.Visible;
        progress.Value = 0;
        bool first = data.Races.Count == 0;
        int before = data.Races.Count;
        var work = data;
        try
        {
            await Task.Run(() => work.SyncAsync(api, settings.IncludeHosted, (text, f) => Dispatcher.BeginInvoke(() =>
            {
                syncStatus.Text = text;
                progress.Value = f;
            }), cancel.Token));
            syncStatus.Text = first ? P(work.Races.Count, "Synced: {0} race.", "Synced: {0} races.")
                : P(work.Races.Count - before, "Synced: {0} new race.", "Synced: {0} new races.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            syncStatus.Text = T("Stopped. What was fetched is kept: Sync carries on from there.");
            // (not when they were deleted meanwhile)
            if (ReferenceEquals(work, data)) try { work.Prepare(); work.Save(); } catch { }
        }
        catch (IRacingException ex) { syncStatus.Text = ex.Message; }
        catch (Exception ex)
        {
            Log.Here.Error("iRacing: sync failed", ex);
            syncStatus.Text = F("The sync failed: {0}", ex.Message);
            if (ReferenceEquals(work, data)) try { work.Prepare(); work.Save(); } catch { }
        }
        finally
        {
            syncing = null;
            cancelButton.Visibility = Visibility.Collapsed;
            progress.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(work, data))
            {
                string status = syncStatus.Text;
                ShowHeader();
                syncStatus.Text = status;
                Refresh();
            }
        }
    }

    // ---------------------------------------------------------------- filters

    private UIElement Filters()
    {
        var p = new WrapPanel();
        p.Children.Add(new TextBlock { Text = T("Category"), VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(category);
        p.Children.Add(new TextBlock { Text = T("Track"), VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(trackFilter);
        p.Children.Add(new TextBlock { Text = T("Car"), VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(carFilter);
        trackFilter.ToolTip = T("Everything below for one track (its older versions too), most-driven first");
        carFilter.ToolTip = T("Everything below for one car, most-driven first");
        trackFilter.SelectionChanged += (_, _) => { if (!filling && trackFilter.SelectedItem is ComboBoxItem i) { settings.Track = (string)i.Tag; settings.Save(); Refresh(); } };
        carFilter.SelectionChanged += (_, _) => { if (!filling && carFilter.SelectedItem is ComboBoxItem i) { settings.Car = (string)i.Tag; settings.Save(); Refresh(); } };
        p.Children.Add(new TextBlock { Text = T("Time"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var (key, label) in Ranges) range.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        range.SelectedIndex = Math.Max(0, Array.FindIndex(Ranges, r => r.Key == settings.Range));
        p.Children.Add(range);
        p.Children.Add(new TextBlock { Text = T("Sessions"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var (key, label) in SessionChoices) session.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        session.SelectedIndex = Math.Max(0, Array.FindIndex(SessionChoices, r => r.Key == settings.Session));
        session.ToolTip = T("Races (hosted ones too), qualifying, practice (and track days), or every session. The time on track below always counts every session.");
        session.SelectionChanged += (_, _) => { if (session.SelectedItem is ComboBoxItem i) { settings.Session = (string)i.Tag; settings.Save(); Refresh(); } };
        p.Children.Add(session);
        official.IsChecked = settings.OfficialOnly;
        p.Children.Add(official);
        shown.SetResourceReference(StyleProperty, "SecondaryText");
        p.Children.Add(shown);
        category.SelectionChanged += (_, _) => { if (!filling && category.SelectedItem is ComboBoxItem i) { settings.Category = (string)i.Tag; settings.Save(); Refresh(); } };
        range.SelectionChanged += (_, _) => { if (range.SelectedItem is ComboBoxItem i) { settings.Range = (string)i.Tag; settings.Save(); Refresh(); } };
        official.Click += (_, _) => { settings.OfficialOnly = official.IsChecked == true; settings.Save(); Refresh(); };
        return p;
    }

    private static readonly (string Key, string Label)[] SessionChoices =
    {
        (Sessions.Race, T("Races")), (Sessions.Qualify, T("Qualifying")), (Sessions.Practice, T("Practice")), ("All", T("Every session")),
    };

    private static readonly (string Key, string Label)[] Ranges =
    {
        ("All", T("All time")), ("Year", T("This year")), ("Twelve", T("The last 12 months")), ("Ninety", T("The last 90 days")), ("Thirty", T("The last 30 days")),
    };

    private bool filling;

    private void FillCategories()
    {
        filling = true;
        category.Items.Clear();
        category.Items.Add(new ComboBoxItem { Content = T("All"), Tag = "" });
        foreach (var c in data.Races.Select(r => r.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => ChartBase.CategorySlot(c)))
            category.Items.Add(new ComboBoxItem { Content = c, Tag = c });
        category.SelectedItem = category.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Category) ?? category.Items[0];
        void Fill(ComboBox box, Func<Race, string> key, string chosen, string all)
        {
            box.Items.Clear();
            box.Items.Add(new ComboBoxItem { Content = all, Tag = "" });
            foreach (var g in data.Races.Where(r => key(r).Length > 0).GroupBy(key).OrderByDescending(g => g.Sum(r => r.Laps)))
                box.Items.Add(new ComboBoxItem { Content = F("{0} ({1} laps)", g.Key, N(g.Sum(r => r.Laps))), Tag = g.Key });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chosen) ?? box.Items[0];
        }
        Fill(trackFilter, r => r.TrackName, settings.Track, T("All tracks"));
        Fill(carFilter, r => r.Car, settings.Car, T("All cars"));
        filling = false;
    }

    /// <summary>Where the time chosen starts (local time), or null for all time.</summary>
    private DateTime? RangeStart()
    {
        var now = DateTime.Now;
        return settings.Range switch
        {
            "Year" => new DateTime(now.Year, 1, 1),
            "Twelve" => now.AddMonths(-12),
            "Ninety" => now.AddDays(-90),
            "Thirty" => now.AddDays(-30),
            _ => null,
        };
    }

    /// <summary>The races (or sessions) shown: the filters, the kind of session too.</summary>
    private List<Race> Filtered() => InRange().Where(r => settings.Session == "All" || r.Session == settings.Session
        || (settings.Session == Sessions.Qualify && r.Session == Sessions.TimeTrial)).ToList();

    /// <summary>Every session in the category and time chosen (for the time on track).</summary>
    private List<Race> InRange()
    {
        string cat = (category.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var from = RangeStart();
        string track = (trackFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "", car = (carFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return data.Races.Where(r => (cat.Length == 0 || r.Category == cat) && (from == null || r.Local >= from) && (!settings.OfficialOnly || r.Official)
            && (track.Length == 0 || r.TrackName == track) && (car.Length == 0 || r.Car == car)).ToList();
    }

    // ---------------------------------------------------------------- the dashboard

    private void Build()
    {
        built = true;
        FillCategories();

        dashboard.Children.Add(Ui.Card(Stack(tiles)));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("In short")), summary)));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("Time on track")), Secondary(T("Every session, races, qualifying and practice (whatever the Sessions filter says), for the category, track, car and time chosen.")), Spaced(trackTime))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Laps at each track, every session")), Spaced(trackAllLaps))),
            Ui.Card(Stack(HeaderText(T("Laps in each car, every session")), Spaced(carAllLaps)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Races by category")), categories)),
            Ui.Card(Stack(HeaderText(T("How your races ended")), outcomes))));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("iRating over time")), Secondary(T("From iRacing's own record, per category, over the time chosen above (all time: from your first race). Point at it for the values on a day.")), Spaced(iRating))));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("Safety rating over time")), Secondary(T("Your licence class and safety rating per category, as iRacing records them: each class is a step up (R, D, C, B, A), the safety rating the part within it. Point at it for the class and rating on a day.")), Spaced(safety))));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("Races a month")), Spaced(months))));
        calendarNote.SetResourceReference(StyleProperty, "SecondaryText");
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Race days")), calendarNote, Spaced(calendar))),
            Ui.Card(Stack(HeaderText(T("When you race")), Secondary(T("Days of the week and hours of the day (your time).")), Spaced(week)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Where you finish")), Spaced(finishes))),
            Ui.Card(Stack(HeaderText(T("Where you start")), Spaced(startsChart)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Places gained or lost")), Secondary(T("From the start to the finish, each race.")), Spaced(gains))),
            Ui.Card(Stack(HeaderText(T("Incidents a race")), Spaced(incidents)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Most laps: tracks")), Spaced(trackLaps))),
            Ui.Card(Stack(HeaderText(T("Most races: cars")), Spaced(carRaces)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Most races: series")), Spaced(seriesRaces))),
            Ui.Card(Stack(HeaderText(T("Wins and podiums: tracks")), Secondary(T("Wins, then the other podiums.")), Spaced(trackWins)))));
        dashboard.Children.Add(Pair(
            Ui.Card(Stack(HeaderText(T("Your best tracks")), Secondary(T("How often you finish in the top 5 at each track (three races or more), and your average finish there.")), Spaced(trackFinish))),
            Ui.Card(Stack(HeaderText(T("iRating won and lost: cars")), Secondary(T("The iRating each car has earned you (blue) or cost you (red), all its races together.")), Spaced(carIRating)))));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("Records")), Secondary(T("Double-click one to open its results on iRacing.")), Spaced(records))));
        dashboard.Children.Add(Ui.Card(Stack(HeaderText(T("More numbers")), Spaced(extra))));
        tablesCard = Ui.Card(Tables());
        dashboard.Children.Add(tablesCard);
    }

    private void Refresh()
    {
        if (!built) return;
        FillCategories();
        var races = Filtered();
        stats = new RaceStats(races);
        shown.Text = data.Races.Count == 0 ? "" : P(races.Count, "{0} race shown", "{0} races shown");
        dashboard.Visibility = data.Races.Count == 0 && data.Career.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        try { Render(stats); }
        catch (Exception ex) { Log.Here.Error("iRacing: showing the races failed", ex); }
    }

    private void Render(RaceStats s)
    {
        // ---- tiles
        tiles.Children.Clear();
        string Pct(int n) => s.Count == 0 ? "–" : (100.0 * n / s.Count).ToString("0.0") + "%";
        Tile(N(s.Count), T("Races"), F("{0:d} to {1:d}", s.First.ToLocalTime(), s.Last.ToLocalTime()));
        Tile(N(s.Wins), T("Wins"), F("{0} of your races", Pct(s.Wins)));
        Tile(N(s.Podiums), T("Podiums"), F("{0} of your races", Pct(s.Podiums)));
        Tile(N(s.Top5), T("Top 5"), F("{0} of your races", Pct(s.Top5)));
        Tile(N(s.Poles), T("Poles"), F("{0} of your races; front row {1}", Pct(s.Poles), N(s.FrontRow)));
        Tile(Pct(s.Wins), T("Win rate"), null);
        Tile(N(s.Laps), T("Laps"), F("{0} a race", s.Count == 0 ? 0 : s.Laps / s.Count));
        Tile(N(s.LapsLed), T("Laps led"), F("{0:0.0}% of your laps, in {1} races", s.Laps == 0 ? 0 : 100.0 * s.LapsLed / s.Laps, s.LedRaces));
        if (s.Km > 0) Tile(N(s.Km), T("Kilometres"), F("{0} miles", N(s.Km / 1.609344)));
        if (s.Hours > 0) Tile(N(s.Hours), T("Hours"), T("In race sessions, from their start to their end"));
        Tile(s.AvgStart.ToString("0.0"), T("Average start"), null);
        Tile(s.AvgFinish.ToString("0.0"), T("Average finish"), null);
        Tile((s.AvgGained >= 0 ? "+" : "") + s.AvgGained.ToString("0.0"), T("Places gained a race"), T("On average, from the start to the finish"));
        Tile(s.AvgIncidents.ToString("0.0"), T("Incidents a race"), F("{0} incidents in all; {1:0.0} every 100 laps", N(s.Incidents), s.IncPer100Laps));
        Tile(N(s.Clean), T("Clean races"), F("{0} with no incidents", Pct(s.Clean)));
        if (s.Dnfs > 0) Tile(N(s.Dnfs), T("Not finished"), T("More than a lap short of the winner's distance"));
        Tile(N(s.Tracks), T("Tracks"), P(s.Configs, "{0} layout", "{0} layouts"));
        Tile(N(s.Cars), T("Cars"), null);
        Tile(N(s.Series), T("Series"), P(s.Seasons, "in {0} season", "in {0} seasons"));
        if (s.Races.Any(r => r.HasIRating)) Tile((s.IRatingNet >= 0 ? "+" : "") + N(s.IRatingNet), T("iRating won"), F("Best race +{0}, worst {1}", s.IRatingBestGain, s.IRatingWorstLoss));
        if (s.AvgSof > 0) Tile(N(s.AvgSof), T("Average SoF"), F("Strength of field; {0:0.0} drivers a race", s.AvgField));
        if (s.Points > 0) Tile(N(s.Points), T("Championship points"), null);

        // ---- in short
        summary.Children.Clear();
        if (s.Count > 0)
        {
            Line(F("{0} races over {1} days with racing, from {2:D} to {3:D}.", N(s.Count), N(s.Days), s.First.ToLocalTime(), s.Last.ToLocalTime()));
            Line(F("{0} wins, {1} podiums and {2} top-5 finishes: you win {3} of your races and finish on the podium in {4}.", N(s.Wins), N(s.Podiums), N(s.Top5), Pct(s.Wins), Pct(s.Podiums)));
            if (s.FavouriteTrack.Length > 0) Line(F("Your track is {0} ({1} laps), your car the {2} and your series {3}.", s.FavouriteTrack, N(s.ByTrack.First(t => t.Name == s.FavouriteTrack).Laps), s.FavouriteCar, s.FavouriteSeries));
            Line(F("You race most on {0}s, around {1}; your busiest day was {2:D}, with {3} races, and your longest run of days in a row with a race is {4}.", s.FavouriteDay, s.FavouriteHour, s.BusiestDay, s.BusiestDayCount, s.DayStreak));
            Line(F("You start {0:0.0} and finish {1:0.0} on average, {2} a race.", s.AvgStart, s.AvgFinish,
                s.AvgGained >= 0 ? F("gaining {0:0.0} places", s.AvgGained) : F("losing {0:0.0} places", -s.AvgGained)));
            Line(F("{0:0.0} incidents a race ({1:0.0} every 100 laps); {2} clean races, {3} in a row at best, and {4} in a row now.", s.AvgIncidents, s.IncPer100Laps, N(s.Clean), s.CleanStreak, s.CurrentCleanStreak));
            if (s.WinStreak > 1 || s.PodiumStreak > 1) Line(F("Your best runs: {0} wins in a row, {1} podiums in a row, {2} top 5s in a row.", s.WinStreak, s.PodiumStreak, s.Top5Streak));
            if (s.Km > 0) Line(F("{0} km driven in races ({1} miles): {2:0.0} times around the Earth.", N(s.Km), N(s.Km / 1.609344), s.Km / 40075));
        }
        else Line(data.Races.Count == 0 ? T("No races yet: press Sync.") : T("No races match the filters."));

        // ---- categories and outcomes
        categories.Show(s.ByCategory.Select(g => new Donut.Part(g.Name, g.Races, ChartBase.CategorySlot(g.Name))).ToList(), N(s.Count), T("races"));
        outcomes.Show(new List<Donut.Part>
        {
            new(T("Wins"), s.Wins, 0),
            new(T("2nd and 3rd"), s.Podiums - s.Wins, 1),
            new(T("4th and 5th"), s.Top5 - s.Podiums, 2),
            new(T("6th to 10th"), s.Top10 - s.Top5, 3),
            new(T("Further back"), s.Races.Count(r => r.Finish > 10), -1),
        }, Pct(s.Top5), T("top 5"));

        // ---- ratings (iRacing's record: not filtered by time)
        string cat = settings.Category;
        // from the time chosen (all time: from your first race, not the rating every account starts with)
        DateTime? ratingsFrom = RangeStart()?.ToUniversalTime() ?? (data.Races.Count > 0 ? data.Races.Min(r => r.Start).AddDays(-14) : null);
        iRating.Show(data.IRating.Where(p => cat.Length == 0 || p.Key == cat).OrderBy(p => ChartBase.CategorySlot(p.Key))
            .Select(p => new TimeChart.Series(p.Key, p.Value, ChartBase.CategorySlot(p.Key))).ToList(), from: ratingsFrom);
        safety.Show(data.Safety.Where(p => cat.Length == 0 || p.Key == cat).OrderBy(p => ChartBase.CategorySlot(p.Key))
            .Select(p => new TimeChart.Series(p.Key, p.Value, ChartBase.CategorySlot(p.Key))).ToList(), Sessions.Licence, ratingsFrom,
            v => Sessions.Class((int)Math.Round(v)), 1);

        // ---- months
        bool many = s.Months.Count > 36;
        var monthItems = many
            ? s.Months.GroupBy(m => (m.Month.Year - 1) * 4 + (m.Month.Month - 1) / 3).Select(g =>
            {
                var q = g.First().Month;
                int races = g.Sum(m => m.Races), wins = g.Sum(m => m.Wins), pods = g.Sum(m => m.Podiums);
                string label = $"{q:yy} Q{(q.Month - 1) / 3 + 1}";
                return new BarItem(label, new double[] { wins, pods - wins, races - pods }, new[] { label, P(races, "{0} race", "{0} races"), P(wins, "{0} win", "{0} wins"), P(pods, "{0} podium", "{0} podiums"), P(g.Sum(m => m.Laps), "{0} lap", "{0} laps") });
            }).ToList()
            : s.Months.Select(m => new BarItem(m.Month.ToString("MMM yy"), new double[] { m.Wins, m.Podiums - m.Wins, m.Races - m.Podiums },
                new[] { m.Month.ToString("MMMM yyyy"), P(m.Races, "{0} race", "{0} races"), P(m.Wins, "{0} win", "{0} wins"), P(m.Podiums, "{0} podium", "{0} podiums"), P(m.Laps, "{0} lap", "{0} laps") })).ToList();
        months.Show(monthItems, new[] { T("Wins"), T("2nd and 3rd"), T("Other races") }, new[] { 0, 1, -1 });

        // ---- days
        var end = s.Count > 0 ? s.Last.ToLocalTime().Date : DateTime.Today;
        if (settings.Range == "All" && s.Count > 0) end = DateTime.Today > end && (DateTime.Today - end).TotalDays < 60 ? DateTime.Today : end;
        calendar.Show(s.PerDay, end);
        calendarNote.Text = F("The year up to {0:d}: the darker, the more races that day. {1} days with a race in all, {2} in a row at most.", end, N(s.Days), s.DayStreak);
        week.Show(s.Week);

        // ---- places
        finishes.Show(Places(s.Finishes, T("Finished {0}")));
        startsChart.Show(Places(s.Starts, T("Started {0}")));
        gains.Show(s.Gains.Select((n, i) =>
        {
            int g = i - 10;
            string label = g <= -10 ? "≤−10" : g >= 10 ? "≥+10" : g > 0 ? "+" + g : g.ToString();
            return new BarItem(label, new double[] { n }, new[] { g == 0 ? T("Finished where you started") : g > 0 ? P(g, "Gained {0} place", "Gained {0} places") + (g >= 10 ? "+" : "") : P(-g, "Lost {0} place", "Lost {0} places") + (g <= -10 ? "+" : ""), P(n, "{0} race", "{0} races") }, g < 0 ? 7 : g == 0 ? -1 : 0);
        }).ToList());
        incidents.Show(s.IncidentCounts.Select((n, i) => new BarItem(i == 16 ? "16+" : i + "x", new double[] { n }, new[] { i == 16 ? T("16 incidents or more") : P(i, "{0} incident", "{0} incidents"), P(n, "{0} race", "{0} races") }, 0)).ToList());

        // ---- tracks, cars, series
        trackLaps.Show(s.ByTrack.OrderByDescending(t => t.Laps).Take(15).Select(t => new BarItem(t.Name, t.Laps, GroupTip(t))).ToList());
        carRaces.Show(s.ByCar.Take(15).Select(c => new BarItem(c.Name, c.Races, GroupTip(c))).ToList());
        seriesRaces.Show(s.BySeries.Take(15).Select(c => new BarItem(c.Name, c.Races, GroupTip(c))).ToList());
        trackWins.Show(s.ByTrack.Where(t => t.Podiums > 0).OrderByDescending(t => t.Wins).ThenByDescending(t => t.Podiums).Take(15)
            .Select(t => new BarItem(t.Name, new double[] { t.Wins, t.Podiums - t.Wins }, GroupTip(t), Shown: F("{0} / {1}", t.Wins, t.Podiums))).ToList(),
            legend: new[] { T("Wins"), T("2nd and 3rd") });
        trackFinish.Show(s.ByTrack.Where(t => t.Races >= 3).OrderByDescending(t => t.Top5 / (double)t.Races).ThenBy(t => t.AvgFinish).Take(15)
            .Select(t => new BarItem(t.Name, new[] { 100.0 * t.Top5 / t.Races }, GroupTip(t), Shown: F("{0:0}% · avg P{1:0.0}", 100.0 * t.Top5 / t.Races, t.AvgFinish))).ToList());
        var irCars = s.ByCar.Where(c => c.IRatingChange != 0).OrderByDescending(c => c.IRatingChange).ToList();
        carIRating.Show(irCars.Take(10).Concat(irCars.Skip(10).Where(c => c.IRatingChange < 0).TakeLast(5)).Distinct()
            .Select(c => new BarItem(c.Name, new double[] { Math.Abs(c.IRatingChange) }, GroupTip(c), c.IRatingChange >= 0 ? 0 : 7, F("{0:+0;-0}", c.IRatingChange))).ToList());

        // ---- time on track (every session)
        RenderTimeOnTrack(InRange());

        // ---- records
        records.Children.Clear();
        foreach (var r in s.Records) records.Children.Add(RecordTile(r));

        // ---- more
        extra.Children.Clear();
        if (s.Count > 0)
        {
            var finished = s.Races.Where(r => r.Finish > 0).ToList();
            Number(T("Median finish"), finished.Count > 0 ? "P" + finished.Select(r => r.Finish).OrderBy(x => x).ElementAt(finished.Count / 2) : "–");
            Number(T("Top 10"), F("{0} ({1})", N(s.Top10), Pct(s.Top10)));
            Number(T("Average field"), F("{0:0.0} drivers", s.AvgField));
            Number(T("Wins from pole"), N(s.Races.Count(r => r.Win && r.Pole)));
            Number(T("Wins not from pole"), N(s.Races.Count(r => r.Win && !r.Pole)));
            Number(T("Poles converted"), s.Poles == 0 ? "–" : F("{0:0}%", 100.0 * s.Races.Count(r => r.Win && r.Pole) / s.Poles));
            Number(T("Clean wins"), N(s.Races.Count(r => r.Win && r.Clean)));
            Number(T("Races leading a lap"), F("{0} ({1})", N(s.LedRaces), Pct(s.LedRaces)));
            Number(T("Official races"), N(s.Races.Count(r => r.Official)));
            Number(T("Hosted races"), N(s.Races.Count(r => r.Hosted)));
            Number(T("Races a week"), F("{0:0.0}", s.Count / Math.Max(1, (s.Last - s.First).TotalDays / 7)));
            Number(T("Days with a race"), N(s.Days));
            Number(T("Longest day streak"), P(s.DayStreak, "{0} day", "{0} days"));
            Number(T("Longest win streak"), N(s.WinStreak));
            Number(T("Longest podium streak"), N(s.PodiumStreak));
            Number(T("Longest clean streak"), N(s.CleanStreak));
            Number(T("Clean streak now"), N(s.CurrentCleanStreak));
            if (s.Races.Any(r => r.OldSubLevel >= 0)) Number(T("Safety rating won"), F("{0:+0.00;-0.00}", s.SafetyNet));
            Number(T("Car classes"), N(s.ByClass.Count));
            Number(T("Different winners of races you didn't win"), N(s.Races.Where(r => !r.Win && r.Winner.Length > 0).Select(r => r.Winner).Distinct().Count()));
            var nemesis = s.Races.Where(r => !r.Win && r.Winner.Length > 0 && r.Winner != data.Name).GroupBy(r => r.Winner).MaxBy(g => g.Count());
            if (nemesis != null && nemesis.Count() > 1) Number(T("Won most races you were in"), F("{0} ({1})", nemesis.Key, nemesis.Count()));
        }

        // ---- tables
        tracksGrid.ItemsSource = s.ByTrack;
        layoutsGrid.ItemsSource = s.ByTrackConfig;
        carsGrid.ItemsSource = s.ByCar;
        seriesGrid.ItemsSource = s.BySeries;
        seasonsGrid.ItemsSource = s.BySeason;
        yearsGrid.ItemsSource = s.ByYear;
        classesGrid.ItemsSource = s.ByClass;
        careerGrid.ItemsSource = data.Career;
        yearlyGrid.ItemsSource = data.Yearly.OrderByDescending(y => y.Year).ThenBy(y => y.Category).ToList();
        FilterRaces();
    }

    private void RenderTimeOnTrack(List<Race> all)
    {
        trackTime.Children.Clear();
        void Add(string label, string value, string? tip = null)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 26, 10), MinWidth = 140, ToolTip = tip };
            var l = new TextBlock { Text = label, FontSize = 12 };
            l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            s.Children.Add(l);
            s.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold });
            trackTime.Children.Add(s);
        }
        int Laps(string kind) => all.Where(r => Of(r) == kind).Sum(r => r.Laps);
        static string Of(Race r) => r.Session == Sessions.TimeTrial ? Sessions.Qualify : r.Session;
        double hours = all.Where(r => r.End > r.Start && r.End - r.Start < TimeSpan.FromHours(30)).Sum(r => (r.End - r.Start).TotalHours);
        Add(T("Sessions"), N(all.Count), F("{0} races, {1} qualifying, {2} practice", all.Count(r => r.IsRace), all.Count(r => Of(r) == Sessions.Qualify), all.Count(r => r.Session == Sessions.Practice)));
        Add(T("Laps in all"), N(all.Sum(r => r.Laps)));
        Add(T("Race laps"), N(Laps(Sessions.Race)));
        Add(T("Qualifying laps"), N(Laps(Sessions.Qualify)));
        Add(T("Practice laps"), N(Laps(Sessions.Practice)));
        if (hours > 0) Add(T("Hours in sessions"), N(hours), T("From each session's start to its end (a practice session's whole length, however long you drove in it)"));
        double km = all.Sum(r => r.Km);
        if (km > 0) Add(T("Kilometres"), N(km), F("{0} miles", N(km / 1.609344)));
        Add(T("Tracks driven"), N(all.Select(r => r.TrackName).Where(t => t.Length > 0).Distinct().Count()));
        Add(T("Cars driven"), N(all.Select(r => r.Car).Where(c => c.Length > 0).Distinct().Count()));
        Add(T("Incidents in all"), N(all.Sum(r => r.Incidents)), F("{0:0.0} every 100 laps", all.Sum(r => r.Laps) is > 0 and int laps ? 100.0 * all.Sum(r => r.Incidents) / laps : 0));

        string[] legend = { T("Race"), T("Qualifying"), T("Practice") };
        string[] Tip(IGrouping<string, Race> g) => new[]
        {
            g.Key,
            F("{0} laps: {1} racing, {2} qualifying, {3} practice", N(g.Sum(r => r.Laps)), N(g.Where(r => r.IsRace).Sum(r => r.Laps)), N(g.Where(r => Of(r) == Sessions.Qualify).Sum(r => r.Laps)), N(g.Where(r => r.Session == Sessions.Practice).Sum(r => r.Laps))),
            F("{0} sessions, {1} races", g.Count(), g.Count(r => r.IsRace)),
            g.Sum(r => r.Km) > 0 ? F("{0} km", N(g.Sum(r => r.Km))) : "",
            F("Last driven {0:d}", g.Max(r => r.Start).ToLocalTime()),
        };
        BarItem Item(IGrouping<string, Race> g) => new(g.Key, new double[]
        {
            g.Where(r => r.IsRace).Sum(r => r.Laps), g.Where(r => Of(r) == Sessions.Qualify).Sum(r => r.Laps), g.Where(r => r.Session == Sessions.Practice).Sum(r => r.Laps),
        }, Tip(g));
        trackAllLaps.Show(all.Where(r => r.Track.Length > 0).GroupBy(r => r.TrackName).OrderByDescending(g => g.Sum(r => r.Laps)).Take(20).Select(Item).ToList(), legend: legend);
        carAllLaps.Show(all.Where(r => r.Car.Length > 0).GroupBy(r => r.Car).OrderByDescending(g => g.Sum(r => r.Laps)).Take(20).Select(Item).ToList(), legend: legend);
    }

    private static List<BarItem> Places(int[] counts, string format)
    {
        int last = Array.FindLastIndex(counts, n => n > 0);
        return counts.Take(Math.Max(5, last + 1)).Select((n, i) =>
        {
            string label = i == 20 ? "21+" : "P" + (i + 1);
            return new BarItem(i == 20 ? "21+" : (i + 1).ToString(), new double[] { n }, new[] { F(format, label), P(n, "{0} race", "{0} races") }, i == 0 ? 0 : i < 3 ? 1 : i < 5 ? 2 : i < 10 ? 3 : -1);
        }).ToList();
    }

    private static string[] GroupTip(RaceGroup g) => new[]
    {
        g.Name,
        F("{0} races, {1} laps ({2} led)", N(g.Races), N(g.Laps), N(g.LapsLed)),
        F("{0} wins, {1} podiums, {2} top 5s, {3} poles", g.Wins, g.Podiums, g.Top5, g.Poles),
        F("Best {0}, average start {1:0.0}, finish {2:0.0}", g.BestText, g.AvgStart, g.AvgFinish),
        F("{0:0.0} incidents a race, {1} clean", g.IncPerRace, g.Clean),
        g.IRatingChange != 0 ? F("iRating {0:+0;-0}", g.IRatingChange) : "",
        F("Last raced {0}", g.LastText),
    };

    // ---------------------------------------------------------------- tables

    private UIElement Tables()
    {
        var s = new StackPanel();
        s.Children.Add(HeaderText(T("Tables")));
        s.Children.Add(Secondary(T("Click a column's heading to sort by it. Every race: double-click one to open its results on iRacing.")));
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        var page = new ContentControl();
        void Tab(string header, UIElement content)
        {
            var chip = new RadioButton { Content = header, GroupName = "iRacingTables", Margin = new Thickness(0, 0, 6, 6), IsChecked = chips.Children.Count == 0 };
            chip.SetResourceReference(StyleProperty, "ChipToggle");
            chip.Checked += (_, _) => page.Content = content;
            if (chips.Children.Count == 0) page.Content = content;
            chips.Children.Add(chip);
        }

        foreach (var (grid, first) in new[] { (tracksGrid, T("Track")), (layoutsGrid, T("Track and layout")), (carsGrid, T("Car")), (seriesGrid, T("Series")), (seasonsGrid, T("Season")), (yearsGrid, T("Year")), (classesGrid, T("Car class")) })
            GroupColumns(grid, first, grid == tracksGrid ? T("Layouts") : grid == carsGrid ? T("Class") : grid == seriesGrid ? T("Category") : null);
        Tab(T("Tracks"), tracksGrid);
        Tab(T("Layouts"), layoutsGrid);
        Tab(T("Cars"), carsGrid);
        Tab(T("Car classes"), classesGrid);
        Tab(T("Series"), seriesGrid);
        Tab(T("Seasons"), seasonsGrid);
        Tab(T("Years"), yearsGrid);

        // every race
        var races = new StackPanel();
        search.ToolTip = T("Find races by series, car, track or winner");
        search.TextChanged += (_, _) => FilterRaces();
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        searchRow.Children.Add(new TextBlock { Text = T("Find"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
        searchRow.Children.Add(search);
        races.Children.Add(searchRow);
        Column(racesGrid, T("When"), nameof(Race.Local), "{0:g}");
        Column(racesGrid, T("Series"), nameof(Race.Series), null, wide: true);
        Column(racesGrid, T("Car"), nameof(Race.Car), null, wide: true);
        Column(racesGrid, T("Track"), nameof(Race.TrackFull), null, wide: true);
        Column(racesGrid, T("Start"), nameof(Race.StartPos), null);
        Column(racesGrid, T("Finish"), nameof(Race.Finish), null);
        Column(racesGrid, T("In class"), nameof(Race.FinishInClass), null);
        Column(racesGrid, T("Gained"), nameof(Race.Gained), "{0:+0;-0;0}");
        Column(racesGrid, T("Field"), nameof(Race.Drivers), null);
        Column(racesGrid, T("SoF"), nameof(Race.Sof), null);
        Column(racesGrid, T("Laps"), nameof(Race.Laps), null);
        Column(racesGrid, T("Led"), nameof(Race.LapsLed), null);
        Column(racesGrid, T("Inc"), nameof(Race.Incidents), null);
        Column(racesGrid, T("iRating"), nameof(Race.IRatingChange), "{0:+0;-0;0}");
        Column(racesGrid, T("Points"), nameof(Race.Points), null);
        Column(racesGrid, T("Winner"), nameof(Race.Winner), null);
        Column(racesGrid, T("Session"), nameof(Race.SessionText), null);
        Column(racesGrid, T("Kind"), nameof(Race.Kind), null);
        racesGrid.MaxHeight = 560;
        racesGrid.MouseDoubleClick += (_, _) => { if (racesGrid.SelectedItem is Race r) OpenResult(r); };
        races.Children.Add(racesGrid);
        Tab(T("Every race"), races);

        // iRacing's own totals
        var career = new StackPanel();
        career.Children.Add(Secondary(T("As iRacing counts them (all your official races, whatever the filters above), per category, then per year.")));
        foreach (var grid in new[] { careerGrid, yearlyGrid })
        {
            if (grid == yearlyGrid) Column(grid, T("Year"), nameof(CareerStat.Year), null);
            Column(grid, T("Category"), nameof(CareerStat.Category), null, wide: true);
            Column(grid, T("Starts"), nameof(CareerStat.Starts), null);
            Column(grid, T("Wins"), nameof(CareerStat.Wins), null);
            Column(grid, T("Top 5"), nameof(CareerStat.Top5), null);
            Column(grid, T("Poles"), nameof(CareerStat.Poles), null);
            Column(grid, T("Avg start"), nameof(CareerStat.AvgStart), "{0:0.0}");
            Column(grid, T("Avg finish"), nameof(CareerStat.AvgFinish), "{0:0.0}");
            Column(grid, T("Laps"), nameof(CareerStat.Laps), "{0:N0}");
            Column(grid, T("Laps led"), nameof(CareerStat.LapsLed), "{0:N0}");
            Column(grid, T("Inc a race"), nameof(CareerStat.AvgIncidents), "{0:0.00}");
            Column(grid, T("Avg points"), nameof(CareerStat.AvgPoints), "{0:0}");
            Column(grid, T("Win %"), nameof(CareerStat.WinPct), "{0:0.0}");
            Column(grid, T("Top 5 %"), nameof(CareerStat.Top5Pct), "{0:0.0}");
            Column(grid, T("Laps led %"), nameof(CareerStat.LapsLedPct), "{0:0.0}");
            grid.Margin = new Thickness(0, 8, 0, 0);
            career.Children.Add(grid);
        }
        yearlyGrid.MaxHeight = 420;
        Tab(T("iRacing's totals"), career);

        s.Children.Add(chips);
        s.Children.Add(page);
        return s;
    }

    private static void GroupColumns(DataGrid grid, string first, string? detail)
    {
        Column(grid, first, nameof(RaceGroup.Name), null, wide: true);
        if (detail != null) Column(grid, detail, nameof(RaceGroup.Detail), null);
        Column(grid, T("Races"), nameof(RaceGroup.Races), null);
        Column(grid, T("Wins"), nameof(RaceGroup.Wins), null);
        Column(grid, T("Podiums"), nameof(RaceGroup.Podiums), null);
        Column(grid, T("Top 5"), nameof(RaceGroup.Top5), null);
        Column(grid, T("Poles"), nameof(RaceGroup.Poles), null);
        Column(grid, T("Win %"), nameof(RaceGroup.WinPct), "{0:0.0}");
        Column(grid, T("Best"), nameof(RaceGroup.BestText), null, sort: nameof(RaceGroup.Best));
        Column(grid, T("Avg start"), nameof(RaceGroup.AvgStart), "{0:0.0}");
        Column(grid, T("Avg finish"), nameof(RaceGroup.AvgFinish), "{0:0.0}");
        Column(grid, T("Gained"), nameof(RaceGroup.AvgGained), "{0:+0.0;-0.0;0}");
        Column(grid, T("Laps"), nameof(RaceGroup.Laps), "{0:N0}");
        Column(grid, T("Led"), nameof(RaceGroup.LapsLed), "{0:N0}");
        Column(grid, T("Km"), nameof(RaceGroup.Km), "{0:N0}");
        Column(grid, T("Inc a race"), nameof(RaceGroup.IncPerRace), "{0:0.0}");
        Column(grid, T("Clean"), nameof(RaceGroup.Clean), null);
        Column(grid, T("Avg SoF"), nameof(RaceGroup.AvgSof), "{0:N0}");
        Column(grid, T("iRating"), nameof(RaceGroup.IRatingChange), "{0:+0;-0;0}");
        Column(grid, T("Last"), nameof(RaceGroup.LastText), null, sort: nameof(RaceGroup.Last));
        grid.MaxHeight = 520;
    }

    private void FilterRaces()
    {
        if (stats == null) return;
        string q = search.Text.Trim();
        IEnumerable<Race> list = stats.Races.Reverse();
        if (q.Length > 0)
            list = list.Where(r => r.Series.Contains(q, StringComparison.CurrentCultureIgnoreCase) || r.Car.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || r.TrackFull.Contains(q, StringComparison.CurrentCultureIgnoreCase) || r.Winner.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        racesGrid.ItemsSource = list.ToList();
    }

    /// <summary>Import: an export from the browser (the export script) into the races kept.</summary>
    public void ImportFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Import an iRacing export"),
            Filter = T("iRacing export") + " (iracing_raw.json, races_all.csv)|*.json;*.csv|" + T("All files") + "|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (syncing != null) { syncStatus.Text = T("Wait for the sync to finish (or stop it) first."); return; }
        try
        {
            var r = RaceImport.Import(data, dialog.FileName);
            if (!built) Build();
            ShowHeader();
            Refresh();
            syncStatus.Text = F("Imported: {0} sessions added, {1} updated", r.Added, r.Updated) + "\n" + F("({0} races, {1} practice; {2} with iRating changes)", r.Races, r.Practices, r.Rated);
        }
        catch (IRacingException ex) { syncStatus.Text = ex.Message; }
        catch (Exception ex)
        {
            Log.Here.Error("iRacing: import failed", ex);
            syncStatus.Text = F("It couldn't be imported: {0}", ex.Message);
        }
    }

    private static void OpenResult(Race r)
    {
        try { Process.Start(new ProcessStartInfo($"https://members-ng.iracing.com/web/racing/results-stats/results?subsessionid={r.SubsessionId}") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Here.Warn("iRacing: couldn't open the results page", ex); }
    }

    // ---------------------------------------------------------------- pieces

    private void Tile(string value, string label, string? tip)
    {
        var b = new Border { MinWidth = 120, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ToolTip = tip };
        b.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold });
        var l = new TextBlock { Text = label, FontSize = 12 };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(l);
        b.Child = s;
        tiles.Children.Add(b);
    }

    private UIElement RecordTile(Record r)
    {
        var b = new Border { Width = 250, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        b.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var s = new StackPanel();
        var title = new TextBlock { Text = r.Title, FontSize = 12 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(title);
        s.Children.Add(new TextBlock { Text = r.Value, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var where = new TextBlock { Text = $"{r.Race.Local:d} · {r.Race.TrackFull}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        s.Children.Add(where);
        var car = new TextBlock { Text = r.Race.Car.Length > 0 ? r.Race.Car : r.Race.Series, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        car.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(car);
        b.Child = s;
        b.ToolTip = string.Join("\n", new[] { r.Race.Series, r.Race.Car, r.Race.TrackFull, F("Started {0}, finished {1} of {2}", r.Race.StartPos, r.Race.Finish, r.Race.Drivers), F("{0} laps, {1} led, {2} incidents", r.Race.Laps, r.Race.LapsLed, r.Race.Incidents), T("Double-click for its results on iRacing") }.Where(x => x.Length > 0));
        b.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) OpenResult(r.Race); };
        return b;
    }

    private void Number(string label, string value)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 26, 10), MinWidth = 140 };
        var l = new TextBlock { Text = label, FontSize = 12 };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(l);
        s.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold });
        extra.Children.Add(s);
    }

    private void Line(string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(bullet, Dock.Left);
        row.Children.Add(bullet);
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        summary.Children.Add(row);
    }

    private static string N(double v) => v.ToString("N0", CultureInfo.CurrentCulture);

    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true };

    private static void Column(DataGrid grid, string header, string path, string? format, string? sort = null, bool wide = false)
    {
        var b = new Binding(path);
        if (format != null) b.StringFormat = format;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = b,
            SortMemberPath = sort ?? path,
            Width = wide ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            MinWidth = wide ? 140 : 0,
        });
    }

    private static Grid Pair(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left);
        System.Windows.Controls.Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 10, 0, 0);
        return e;
    }

    private static TextBlock HeaderText(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "CardHeader");
        return t;
    }

    private static TextBlock Secondary(string text) => Ui.Note(text, 4);
}
