using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The Analysis tab. Top: Record Laps (on / off), what it's reading. Then (since 3.28.0) Name Track (the place the car
/// is, as a track: its name filled in when it's one known; Export) beside Current Location, the blocks (each pops out as
/// a widget: its button, or right-click → Make Widget), Best Times (each track's best overall and per class raced, a
/// tile each, found by track or car), best by track, car and class, every lap (filtered by track, car and class), Discovered Tracks (a table:
/// renamed, sorted, filtered by game, deleted). Every card folds (its chevron, top right; kept).
/// The units are on the Profiles tab's Profile card (since 3.28.0) and in Settings.
/// </summary>
internal sealed class AnalysisView : UserControl
{
    private readonly AnalysisApplet applet;
    private readonly AnalysisSettings settings;
    private readonly ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("");
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<BlockView> blocks = new();
    private readonly TextBlock saved = Ui.Note("");
    private readonly TextBox tagBox = new() { Width = 260, Margin = new Thickness(0, 8, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button addNew;
    private readonly TextBlock placeName = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock placeAt = Ui.Note("");
    /// <summary>The track the car is at (its race's, or the one starting nearest), and the name the tag box was filled
    /// with for it (kept while the user types over it).</summary>
    private Track? matched;
    private string autoText = "", hereText = "";
    private readonly Grid trackTiles = new();
    private readonly TextBox tileSearch = new() { Width = 200, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly StackPanel carName = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox carBox = new() { Width = 170, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox trackFilter = Filter(T("Track")), carFilter = Filter(T("Car")), classFilter = Filter(T("Class"));
    private readonly DataGrid best = Table(), laps = Table(), found = Table(), races = Table();
    private readonly TextBlock raceStats = new() { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock raceCount = Ui.Note("");
    private readonly TextBlock lapCount = Ui.Note(""), noTracks = Ui.Note(T("None yet: a track is added when a race on it ends its first lap or finishes, or with Name Track."));
    private readonly TextBlock gameHeader = new() { Text = T("Game"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button deleteTrack;
    private readonly MapView map;
    private string? gameFilter;
    private int car = -1;
    private string? carGame;
    private bool filling;

    public AnalysisView(AnalysisApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = SimUi.OnOff(T("Record laps"), settings.Enabled, applet.SetEnabled);
        onOff.ToolTip = T("Record laps");
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(onOffKey, T("On / off shortcut"));
        ShowKey();
        onOffKey.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), applet.Hotkey); ShowKey(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        top.Children.Add(status);

        // the blocks, each a card that pops out as a widget, on one row (since 3.31.1)
        Border BlockCard(Block block, bool last)
        {
            var view = new BlockView(block);
            blocks.Add(view);
            var popOut = VolumeControl.IconButton("");
            popOut.ToolTip = T("Pop out");
            AutomationProperties.SetName(popOut, F("Pop out {0}", block.Title));
            popOut.Click += (_, _) => applet.MakeWidget(block);
            // (since 3.27.1) its widget's on / off shortcut
            var key = applet.WidgetKey(block);
            var keyButton = VolumeControl.IconButton("");
            AutomationProperties.SetName(keyButton, F("{0} widget shortcut", block.Title));
            void ShowWidgetKey() => ShowKey(keyButton, key, T("Widget on / off shortcut"));
            ShowWidgetKey();
            keyButton.Click += (_, _) => { HotkeyWindow.Open(T("Widget on / off shortcut"), Window.GetWindow(this), key); ShowWidgetKey(); };
            var card = Foldable("Block." + block.Id, block.Title, view, keyButton, popOut);
            card.Margin = new Thickness(0, 0, last ? 0 : 12, 12);
            Widget.Offer(card, () => applet.MakeWidget(block));
            return card;
        }
        // (since 3.30.2 without Vehicle and Laps: Timing, Position and Name Track have what they showed; since 3.28.2
        // Leaderboard, the field: iRacing's, Forza sends only your place)
        var driving = new UniformGrid { Rows = 1 };
        driving.Children.Add(BlockCard(Blocks.Tires, false));
        driving.Children.Add(BlockCard(Blocks.Performance, false));
        driving.Children.Add(BlockCard(Blocks.Timing, false));
        driving.Children.Add(BlockCard(Blocks.Position, false));
        driving.Children.Add(BlockCard(Blocks.Leaderboard, true));
        // a name for the car being driven (Forza sends only its number; on Name Track since 3.30.2)
        AutomationProperties.SetName(carBox, T("Car name"));
        carBox.ToolTip = T("Car name");
        carBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveCar(); };
        carName.Children.Add(carBox);
        carName.Children.Add(SimUi.Button(T("Name Car"), ThenName(SaveCar)));
        carName.Children[1].SetValue(MarginProperty, new Thickness(6, 0, 0, 0));
        carName.Visibility = Visibility.Collapsed;

        foreach (var f in new[] { trackFilter, carFilter, classFilter }) f.SelectionChanged += (_, _) => { if (!filling) RenderLaps(); };
        // (since 3.31.1 on one row, the boxes sharing its width)
        var filters = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (label, box) in new[] { (T("Track"), trackFilter), (T("Car"), carFilter), (T("Class"), classFilter) })
        {
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(name, filters.ColumnDefinitions.Count - 1);
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
            Grid.SetColumn(box, filters.ColumnDefinitions.Count - 1);
            box.Margin = new Thickness(0, 0, box == classFilter ? 0 : 12, 0);
            filters.Children.Add(name);
            filters.Children.Add(box);
        }
        Columns(best, (T("Game"), "Game", null), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null), (T("PI"), "Pi", null),
            (T("Best"), "BestText", "Best"), (T("Date"), "DateText", "Date"), (T("Laps"), "Laps", null), (T("Last driven"), "LastText", "Last"));
        Columns(laps, (T("When"), "WhenText", "When"), (T("Game"), "Game", null), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null),
            (T("PI"), "Pi", null), (T("Lap"), "Lap", null), (T("Time"), "TimeText", "Time"), (T("Gap"), "GapText", "Gap"));
        // (since 3.28.1) each race finished: where the car started and finished
        Columns(races, (T("When"), "WhenText", "When"), (T("Game"), "Game", null), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null),
            (T("Laps"), "Laps", null), (T("Time"), "TimeText", "Time"), (T("Start"), "StartText", "Start"), (T("Finish"), "FinishText", "Finish"),
            (T("Gained"), "GainedText", "Gained"));
        AutomationProperties.SetName(races, T("Races"));

        // Name Track: the place the car is, saved as a track (where an event starts), or the track it's at renamed
        AutomationProperties.SetName(tagBox, T("Track name"));
        tagBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ThenName(() => TagHere(false))(); };
        var tagSave = SimUi.Button(T("Save"), ThenName(() => TagHere(false)), accent: true);
        tagSave.Margin = new Thickness(0, 8, 6, 0);
        addNew = SimUi.Button(T("Add New"), ThenName(() => TagHere(true)), tip: T("Save this place as another track"));
        addNew.Margin = new Thickness(0, 8, 6, 0);
        addNew.Visibility = Visibility.Collapsed;
        // (since 3.24.7) the tracks and places, to a JSON file
        var export = SimUi.Button(T("Export"), ThenName(ExportTracks), tip: T("Export the tracks to a file"));
        export.Margin = new Thickness(0, 8, 0, 0);
        saved.Margin = new Thickness(0, 6, 0, 0);
        saved.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        var nameCard = Foldable("NameTrack", T("Name Track"), new StackPanel { Children = { new WrapPanel { Children = { tagBox, tagSave, addNew, export } }, saved, carName } });
        var placeCard = Foldable("Location", T("Current Location"), new StackPanel { Children = { placeName, placeAt } });
        nameCard.Margin = new Thickness(0, 0, 12, 12);
        var places = new Grid();
        places.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        places.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(placeCard, 1);
        places.Children.Add(nameCard);
        places.Children.Add(placeCard);

        // Discovered Tracks: a table sorted by any column (its header), Game's filter its funnel
        var funnel = VolumeControl.IconButton("");
        ((TextBlock)funnel.Content).FontSize = 11;
        funnel.Padding = new Thickness(6, 0, 0, 0);
        funnel.ToolTip = T("Filter by game");
        AutomationProperties.SetName(funnel, T("Filter by game"));
        funnel.Click += (_, e) => { GameMenu(funnel); e.Handled = true; };
        TrackColumns(new StackPanel { Orientation = Orientation.Horizontal, Children = { gameHeader, funnel } });
        found.SelectionChanged += (_, _) => deleteTrack!.IsEnabled = found.SelectedItem is TrackRow;
        deleteTrack = SimUi.Confirm(T("Delete"), () =>
        {
            if (found.SelectedItem is not TrackRow r) return;
            settings.Tracks.Remove(r.Track);
            settings.Save();
            applet.Store.RemoveTrack(r.Track.Id);
            Logging.Log.Here.Info($"Analysis: track {r.Track.Id} deleted with its laps");
            RenderStats();
        }, T("Deletes the track picked and its laps"));
        deleteTrack.IsEnabled = false;
        deleteTrack.Margin = new Thickness(0, 0, 4, 0);

        lapCount.VerticalAlignment = VerticalAlignment.Center;
        lapCount.Margin = new Thickness(0, 0, 4, 0);
        raceCount.VerticalAlignment = VerticalAlignment.Center;
        raceCount.Margin = new Thickness(0, 0, 4, 0);
        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        page.Children.Add(places);
        page.Children.Add(driving);
        // (since 3.31.0) the tracks on the game's map
        map = new MapView(applet);
        page.Children.Add(Foldable("Map", T("Map"), map));
        // (since 3.31.1) found as it's typed, by track or car
        AutomationProperties.SetName(tileSearch, T("Search"));
        tileSearch.ToolTip = T("Track or car");
        tileSearch.TextChanged += (_, _) => RenderTiles();
        page.Children.Add(Foldable("BestTimes", T("Best Times"), trackTiles, tileSearch));
        // (since 3.30.2) Best Laps and Laps side by side; since 3.31.1 both tables fill their cards, ending level, the
        // Laps table allowed the height of the filters Best Laps has over its own
        DockPanel.SetDock(filters, Dock.Top);
        filters.SizeChanged += (_, _) => laps.MaxHeight = best.MaxHeight + filters.ActualHeight + filters.Margin.Bottom;
        var bestCard = Foldable("BestLaps", T("Best Laps"), new DockPanel { Children = { filters, best } }, true);
        bestCard.Margin = new Thickness(0, 0, 12, 12);
        var lapsCard = Foldable("Laps", T("Laps"), laps, true, lapCount);
        Grid.SetColumn(lapsCard, 1);
        var lapRow = new Grid { Children = { bestCard, lapsCard } };
        lapRow.ColumnDefinitions.Add(new ColumnDefinition());
        lapRow.ColumnDefinitions.Add(new ColumnDefinition());
        page.Children.Add(lapRow);
        page.Children.Add(Foldable("Races", T("Races"), new StackPanel { Children = { raceStats, races } }, raceCount));
        page.Children.Add(Foldable("Tracks", T("Discovered Tracks"), new StackPanel { Children = { noTracks, found } }, deleteTrack));
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(SimUi.Scroll(page));
        Content = dock;
        applet.Store.Changed += RenderStats;
    }

    public void ShowOnOff() => SimUi.ShowOnOff(onOff, settings.Enabled);

    /// <summary>A Name Track button's action, then (since 3.31.1) the name box focused and selected for the next name
    /// (queued: a file dialog gives the focus back after it closes).</summary>
    private Action ThenName(Action action) => () =>
    {
        action();
        Dispatcher.BeginInvoke(() => { tagBox.Focus(); tagBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    };

    private void ShowKey() => ShowKey(onOffKey, applet.Hotkey, T("On / off shortcut"));

    /// <summary>A shortcut button: in the accent while it has a key, which its tooltip names.</summary>
    private static void ShowKey(Button button, Hotkey key, string tip)
    {
        ((TextBlock)button.Content).SetResourceReference(TextBlock.ForegroundProperty, key.Value != null ? "AccentBrush" : "TextBrush");
        button.ToolTip = key.Value is { } k ? $"{tip}: {k}" : tip;
    }

    private Border Foldable(string id, string title, UIElement body, params UIElement[] right) => Foldable(id, title, body, false, right);

    /// <summary>A card with its title and, top right, these buttons and its fold / open chevron (since 3.28.0): folded,
    /// only the title shows; kept in <see cref="AnalysisSettings.Folded"/>. <paramref name="fill"/>: the body takes all
    /// the height the card is given.</summary>
    private Border Foldable(string id, string title, UIElement body, bool fill, params UIElement[] right)
    {
        var fold = VolumeControl.IconButton("");
        ((TextBlock)fold.Content).FontSize = 11;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var r in right)
        {
            if (r is FrameworkElement e && e.Margin == default) e.Margin = new Thickness(0, 0, 4, 0);
            buttons.Children.Add(r);
        }
        buttons.Children.Add(fold);
        var header = Ui.HeaderRow(title, buttons);
        DockPanel.SetDock(header, Dock.Top);
        var card = SimUi.Card(fill ? new DockPanel { Children = { header, body } } : new StackPanel { Children = { header, body } });
        void Show()
        {
            bool folded = settings.Folded.Contains(id);
            body.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            header.Margin = new Thickness(0, 0, 0, folded ? 0 : 10);
            card.VerticalAlignment = folded ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            ((TextBlock)fold.Content).Text = folded ? "" : "";
            fold.ToolTip = folded ? T("Open") : T("Fold");
            AutomationProperties.SetName(fold, folded ? F("Open {0}", title) : F("Fold {0}", title));
        }
        fold.Click += (_, _) =>
        {
            if (!settings.Folded.Remove(id)) settings.Folded.Add(id);
            settings.Save();
            Show();
        };
        Show();
        return card;
    }

    public void UnitsChanged() => RenderStats();

    public void Render()
    {
        ShowOnOff();
        RenderStats();
    }

    public void Tick(Reading now)
    {
        var (text, receiving) = applet.Status();
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, receiving ? "SuccessBrush" : "TextSecondaryBrush");
        if (!IsVisible) return;
        foreach (var b in blocks) b.Update(now);
        ShowHere(now);
        map.Tick(now);
        int c = now.Frame is { HasRace: true, CarOrdinal: > 0 } f ? f.CarOrdinal : -1;
        if (c != car || now.Game != carGame)
        {
            car = c;
            carGame = now.Game;
            carName.Visibility = c > 0 ? Visibility.Visible : Visibility.Collapsed;
            carBox.Text = c > 0 ? settings.CarName(carGame, c) : "";
        }
    }

    private void SaveCar()
    {
        if (car <= 0) return;
        string name = carBox.Text.Trim();
        string game = carGame ?? LapStore.OldGame;
        settings.Cars.Remove($"FH:{car}");
        // (the list's name, or none: back to the list's)
        if (name.Length > 0 && name != CarNames.Name(game, car)) settings.Cars[$"{game}:{car}"] = name; else settings.Cars.Remove($"{game}:{car}");
        settings.Save();
        Logging.Log.Here.Info($"Analysis: car {car} named \"{name}\"");
        RenderStats();
    }

    /// <summary>Every track and place (game, name, start, length, laps, cars) to a JSON file the user picks.</summary>
    private void ExportTracks()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"DaisysApp-Tracks-{DateTime.Now:yyyy-MM-dd}.json", DefaultExt = ".json", Filter = T("JSON files") + " (*.json)|*.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var list = settings.Tracks.OrderBy(t => t.Game).ThenBy(t => settings.TrackName(t.Id)).Select(t => new
        {
            t.Game, Name = settings.TrackName(t.Id), Type = MapView.Kind(t), Form = MapView.Form(t), t.X, t.Y, t.Z, t.Length, t.Laps, t.Racers, t.Found,
        });
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(new { Tracks = list },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Logging.Log.Here.Info($"Analysis: {settings.Tracks.Count} tracks exported to {dialog.FileName}");
            DaisysApp.Shell.Notifications.Show(T("Analysis"), F("{0} tracks exported", settings.Tracks.Count));
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Analysis: exporting the tracks", ex);
            DaisysApp.Shell.Notifications.Show(T("Analysis"), T("The tracks couldn't be saved"));
        }
    }

    // ---------------------------------------------------------------- the place the car is

    /// <summary>Current Location: the track the car is at (its race's, or the one starting nearest it, within the radius a
    /// race's start is looked for) or Unknown, and where it is; Name Track's box filled with that track's name.</summary>
    private void ShowHere(Reading now)
    {
        var here = applet.Here();
        string name, at;
        if (now.Frame is not { HasRace: true } f || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) { name = T("Unknown"); at = T("No place from the game yet"); }
        else
        {
            name = here is { } h ? settings.TrackName(h.Track.Id) : T("Unknown");
            at = F("{0:0}, {1:0}", f.PosX, f.PosZ) + (here is { } n ? " · " + F("{0:0} m from its start", n.Metres) : "");
        }
        if (name + at != hereText)
        {
            hereText = name + at;
            placeName.Text = name;
            placeAt.Text = at;
        }
        var t = here?.Track;
        if (t == matched) return;
        matched = t;
        addNew.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
        // (what the user's typing stays)
        if (!tagBox.IsKeyboardFocusWithin || tagBox.Text == autoText)
        {
            autoText = t?.Name ?? "";
            tagBox.Text = autoText;
            if (tagBox.IsKeyboardFocusWithin) tagBox.SelectAll();
        }
    }

    /// <summary>Save: the track the car is at renamed, or (none, or Add New) the place it is saved as a track.</summary>
    private void TagHere(bool asNew)
    {
        string name = tagBox.Text.Trim();
        if (name.Length == 0) { tagBox.Focus(); return; }
        if (!asNew && matched != null && settings.Tracks.Contains(matched))
        {
            if (matched.Name != name) Rename(matched, name);
            autoText = tagBox.Text = name;
            saved.Text = F("Saved \"{0}\"", name);
            return;
        }
        if (applet.NameHere(name) is not { } t)
        {
            saved.Text = T("No place from the game yet");
            return;
        }
        matched = t;
        autoText = tagBox.Text = t.Name;
        addNew.Visibility = Visibility.Visible;
        saved.Text = F("Saved \"{0}\" at {1:0}, {2:0}", t.Name, t.X, t.Z);
        RenderStats();
    }

    private void Rename(Track t, string name)
    {
        t.Name = name.Trim();
        settings.Save();
        Logging.Log.Here.Info($"Analysis: track named \"{t.Name}\"");
        RenderStats();
    }

    // ---------------------------------------------------------------- the stats

    private string Distance(float m) => settings.Units == AnalysisUnits.Metric ? $"{m / 1000:0.00} km" : $"{m / 1609.34:0.00} mi";

    private void RenderStats()
    {
        Refill(trackFilter, settings.Tracks.Select(t => (t.Id, settings.TrackName(t.Id))));
        Refill(carFilter, applet.Store.All.Select(l => (l.Game, l.Car)).Distinct().Select(c => ($"{c.Game}:{c.Car}", $"{settings.CarName(c.Game, c.Car)} ({c.Game})")).OrderBy(x => x.Item2));
        // (a class's number can be another letter in another game: Horizon 6's 6 is R, the others' X)
        Refill(classFilter, applet.Store.All.GroupBy(l => l.Class).OrderBy(g => g.Key).Select(g =>
            (g.Key.ToString(CultureInfo.InvariantCulture), string.Join(" / ", g.Select(l => Blocks.ClassName(l.Class, l.Game)).Distinct()))));
        RenderLaps();
        RenderRaces();
        RenderTiles();
        RenderTracks();
        map.Refresh();
    }

    /// <summary>Races: every race finished, newest first, and the stats over them (wins, podiums, the average finish,
    /// places gained).</summary>
    private void RenderRaces()
    {
        var all = applet.Store.Races;
        var rows = all.OrderByDescending(r => r.At).Take(1000).Select(r =>
        {
            int gained = r.Start > 0 && r.Finish > 0 ? r.Start - r.Finish : 0;
            return new RaceRow(r.At, r.At.ToString("g", CultureInfo.CurrentCulture), r.Game, settings.TrackName(r.Track), settings.CarName(r.Game, r.Car),
                Blocks.ClassName(r.Class, r.Game), r.Laps, r.Time, Blocks.LapTime(r.Time), r.Start, Place(r.Start, r.Racers), r.Finish, Place(r.Finish, r.Racers),
                gained, r.Start > 0 && r.Finish > 0 ? gained.ToString("+0;-0;0", CultureInfo.CurrentCulture) : Blocks.None);
        }).ToList();
        Fill(races, rows);
        races.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        raceCount.Text = all.Count == 0 ? T("No races yet") : P(all.Count, "{0} race", "{0} races");
        var placed = all.Where(r => r.Finish > 0).ToList();
        var both = placed.Where(r => r.Start > 0).ToList();
        raceStats.Text = all.Count == 0 ? T("A race is added when it finishes, with where you started and finished.")
            : string.Join(" · ", new[]
            {
                P(placed.Count(r => r.Finish == 1), "{0} win", "{0} wins"),
                P(placed.Count(r => r.Finish <= 3), "{0} podium", "{0} podiums"),
                placed.Count > 0 ? F("Average finish {0:0.0}", placed.Average(r => r.Finish)) : null,
                placed.Count > 0 ? F("Best finish {0}", placed.Min(r => r.Finish)) : null,
                both.Count > 0 ? F("Average start {0:0.0}", both.Average(r => r.Start)) : null,
                both.Count > 0 ? F("Places gained a race {0}", both.Average(r => r.Start - r.Finish).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)) : null,
            }.Where(x => x != null));
    }

    /// <summary>A place: "3 of 12", "3" when how many is unknown, "—" when the place is.</summary>
    internal static string Place(int pos, int of) => pos <= 0 ? Blocks.None : of >= pos ? F("{0} of {1}", pos, of) : pos.ToString(CultureInfo.CurrentCulture);

    private void RenderLaps()
    {
        string? track = Picked(trackFilter), carId = Picked(carFilter), cls = Picked(classFilter);
        var picked = applet.Store.All.Where(l =>
            (track == null || l.Track == track) &&
            (carId == null || $"{l.Game}:{l.Car}" == carId) &&
            (cls == null || l.Class.ToString(CultureInfo.InvariantCulture) == cls)).ToList();
        // best by track, car and class
        var groups = picked.GroupBy(l => (l.Track, l.Game, l.Car, l.Class)).Select(g =>
        {
            var b = g.MinBy(l => l.Time)!;
            var last = g.Max(l => l.At);
            return new BestRow(g.Key.Game, settings.TrackName(g.Key.Track), settings.CarName(g.Key.Game, g.Key.Car), Blocks.ClassName(g.Key.Class, g.Key.Game), b.Pi,
                b.Time, Blocks.LapTime(b.Time), b.At, b.At.ToString("MMM d, yyyy", CultureInfo.CurrentCulture), g.Count(), last, last.ToString("g", CultureInfo.CurrentCulture));
        }).OrderBy(r => r.Track).ThenBy(r => r.Best).ToList();
        Fill(best, groups);
        best.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // every lap, newest first, with its gap to the best of its track, car and class
        var bests = picked.GroupBy(l => (l.Track, l.Car, l.Class)).ToDictionary(g => g.Key, g => g.Min(l => l.Time));
        var rows = picked.OrderByDescending(l => l.At).Take(1000).Select(l =>
        {
            float gap = l.Time - bests[(l.Track, l.Car, l.Class)];
            return new LapRow(l.At, l.At.ToString("g", CultureInfo.CurrentCulture), l.Game, settings.TrackName(l.Track), settings.CarName(l.Game, l.Car),
                Blocks.ClassName(l.Class, l.Game), l.Pi, l.Lap, l.Time, Blocks.LapTime(l.Time), gap, gap > 0.0005f ? $"+{gap:0.000}" : T("Best"));
        }).ToList();
        Fill(laps, rows);
        laps.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        lapCount.Text = picked.Count == 0 ? T("No laps yet") : P(picked.Count, "{0} lap", "{0} laps");
    }

    /// <summary>Best Times: a tile per track, its name and laps, the best lap overall, then the best of each class raced
    /// there (with its car), the fastest class first; the tiles of a row as tall as its tallest. A search (since 3.31.1)
    /// keeps the tracks whose name has it and, on the others, only the laps of the cars whose name has it.</summary>
    private void RenderTiles()
    {
        const int Across = 4;
        trackTiles.Children.Clear();
        trackTiles.RowDefinitions.Clear();
        if (trackTiles.ColumnDefinitions.Count == 0)
            for (int i = 0; i < Across; i++) trackTiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ((FrameworkElement)((FrameworkElement)trackTiles.Parent).Parent).Visibility = settings.Tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var byTrack = applet.Store.All.ToLookup(l => l.Track);
        string search = tileSearch.Text.Trim();
        bool Found(string name) => name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        var cars = search.Length > 0 ? applet.Store.All.Select(l => (l.Game, l.Car)).Distinct().Where(c => Found(settings.CarName(c.Game, c.Car))).ToHashSet() : [];
        int n = 0;
        foreach (var t in settings.Tracks)
        {
            var mine = byTrack[t.Id].ToList();
            if (search.Length > 0 && !Found(settings.TrackName(t.Id)))
            {
                mine.RemoveAll(l => !cars.Contains((l.Game, l.Car)));
                if (mine.Count == 0) continue;
            }
            var lines = new Grid();
            lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lines.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Line(string label, LapRecord? lap, bool car)
            {
                int r = lines.RowDefinitions.Count;
                lines.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = new TextBlock { Text = label + ":", Margin = new Thickness(0, 1, 8, 1) };
                name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var value = new TextBlock
                {
                    Text = lap == null ? Blocks.None : Blocks.LapTime(lap.Time) + (car ? $" ({settings.CarName(lap.Game, lap.Car)})" : ""),
                    Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                };
                value.ToolTip = value.Text;
                Grid.SetRow(name, r); Grid.SetRow(value, r); Grid.SetColumn(value, 1);
                lines.Children.Add(name); lines.Children.Add(value);
            }
            Line(T("Overall"), mine.MinBy(l => l.Time), car: false);
            // (since 3.28.0 only the classes raced there)
            foreach (int c in ForzaSource.Classes(t.Game))
                if (mine.Where(l => l.Class == c).MinBy(l => l.Time) is { } classBest) Line(F("{0} Class", Blocks.ClassName(c, t.Game)), classBest, car: true);
            var count = new TextBlock { Text = mine.Count.ToString(CultureInfo.CurrentCulture), ToolTip = T("Laps"), VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(count, Dock.Right);
            title.Children.Add(count);
            title.Children.Add(new TextBlock { Text = settings.TrackName(t.Id), FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Border
            {
                Child = new StackPanel { Children = { title, lines } }, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            AutomationProperties.SetName(tile, settings.TrackName(t.Id));
            if (n % Across == 0) trackTiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(tile, n / Across);
            Grid.SetColumn(tile, n % Across);
            trackTiles.Children.Add(tile);
            n++;
        }
    }

    /// <summary>Discovered Tracks: a row per track (of the game picked by Game's filter), with the track starting nearest
    /// it, so a sort by Nearest (or Location) puts the ones near each other together.</summary>
    private void RenderTracks()
    {
        var games = settings.Tracks.Select(t => t.Game).Distinct().ToList();
        if (gameFilter != null && !games.Contains(gameFilter)) gameFilter = null;
        gameHeader.Text = gameFilter == null ? T("Game") : F("Game: {0}", gameFilter);
        var rows = settings.Tracks.Where(t => gameFilter == null || t.Game == gameFilter).Select(t =>
        {
            int count = applet.Store.All.Count(l => l.Track == t.Id);
            var near = settings.Tracks.Where(o => o != t && o.Game == t.Game).Select(o => (o, m: MathF.Sqrt((o.X - t.X) * (o.X - t.X) + (o.Y - t.Y) * (o.Y - t.Y) + (o.Z - t.Z) * (o.Z - t.Z))))
                .OrderBy(x => x.m).FirstOrDefault();
            return new TrackRow(this, t)
            {
                Game = t.Game, Laps = count, Length = t.Length, LengthText = t.Length > 0 ? Distance(t.Length) : T("no race yet"),
                LocationText = F("{0:0}, {1:0}", t.X, t.Z), Cell = Cell(t.X, t.Z),
                NearestText = near.o != null ? $"{settings.TrackName(near.o.Id)} · {Distance(near.m)}" : Blocks.None, NearestM = near.o != null ? near.m : float.MaxValue,
                Found = t.Found, FoundText = t.Found.ToString("d", CultureInfo.CurrentCulture),
            };
        }).ToList();
        Fill(found, rows);
        found.Visibility = rows.Count > 0 || gameFilter != null ? Visibility.Visible : Visibility.Collapsed;
        noTracks.Visibility = settings.Tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        deleteTrack.IsEnabled = found.SelectedItem is TrackRow;
    }

    /// <summary>A place's key for sorting by Location: its 500 m square, in the order that keeps neighbouring squares
    /// mostly together (Z-order).</summary>
    private static long Cell(float x, float z)
    {
        uint cx = (uint)Math.Clamp((x + 1_000_000) / 500, 0, 65535), cz = (uint)Math.Clamp((z + 1_000_000) / 500, 0, 65535);
        long key = 0;
        for (int i = 0; i < 16; i++) key |= ((long)((cx >> i) & 1) << (2 * i)) | ((long)((cz >> i) & 1) << (2 * i + 1));
        return key;
    }

    /// <summary>Game's filter: All, or one game.</summary>
    private void GameMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var g in new string?[] { null }.Concat(settings.Tracks.Select(t => t.Game).Distinct().OrderBy(g => g)))
        {
            var item = new MenuItem { Header = g ?? T("All"), IsCheckable = true, IsChecked = g == gameFilter };
            item.Click += (_, _) => { gameFilter = g; RenderTracks(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void TrackColumns(FrameworkElement gameHead)
    {
        // the name, typed over in its row (Enter or leaving it renames the track)
        var box = new FrameworkElementFactory(typeof(TextBox));
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(TrackRow.Name)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        box.SetValue(TextBox.MinWidthProperty, 180.0);
        box.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        box.AddHandler(KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox t) BindingOperations.GetBindingExpression(t, TextBox.TextProperty)?.UpdateSource();
        }));
        found.Columns.Add(new DataGridTemplateColumn { Header = T("Name"), CellTemplate = new DataTemplate { VisualTree = box }, SortMemberPath = nameof(TrackRow.Name) });
        found.Columns.Add(new DataGridTextColumn { Header = gameHead, Binding = new Binding(nameof(TrackRow.Game)), SortMemberPath = nameof(TrackRow.Game) });
        foreach (var (header, path, sortBy) in new (string, string, string)[]
        {
            (T("Laps"), nameof(TrackRow.Laps), nameof(TrackRow.Laps)), (T("Lap"), nameof(TrackRow.LengthText), nameof(TrackRow.Length)),
            (T("Location"), nameof(TrackRow.LocationText), nameof(TrackRow.Cell)), (T("Nearest"), nameof(TrackRow.NearestText), nameof(TrackRow.NearestM)),
            (T("Found"), nameof(TrackRow.FoundText), nameof(TrackRow.Found)),
        })
            found.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), SortMemberPath = sortBy });
        AutomationProperties.SetName(found, T("Discovered Tracks"));
    }

    // ---------------------------------------------------------------- pieces

    private sealed record BestRow(string Game, string Track, string Car, string Class, int Pi, float Best, string BestText, DateTime Date, string DateText, int Laps, DateTime Last, string LastText);

    private sealed record RaceRow(DateTime When, string WhenText, string Game, string Track, string Car, string Class, int Laps, float Time, string TimeText,
        int Start, string StartText, int Finish, string FinishText, int Gained, string GainedText);

    private sealed record LapRow(DateTime When, string WhenText, string Game, string Track, string Car, string Class, int Pi, int Lap, float Time, string TimeText, float Gap, string GapText);

    /// <summary>A Discovered Tracks row: its name renames the track when it's typed over.</summary>
    private sealed class TrackRow(AnalysisView view, Track track)
    {
        public Track Track { get; } = track;
        public string Name
        {
            get => view.settings.TrackName(Track.Id);
            set
            {
                string name = value.Trim();
                if (name == Name || (name.Length == 0 && Track.Name.Length == 0)) return;
                // (after the binding's done: the table is filled again)
                view.Dispatcher.BeginInvoke(() => view.Rename(Track, name));
            }
        }
        public string Game { get; init; } = "";
        public int Laps { get; init; }
        public float Length { get; init; }
        public string LengthText { get; init; } = "";
        public string LocationText { get; init; } = "";
        public long Cell { get; init; }
        public string NearestText { get; init; } = "";
        public float NearestM { get; init; }
        public DateTime Found { get; init; }
        public string FoundText { get; init; } = "";
    }

    private static ComboBox Filter(string name)
    {
        var c = new ComboBox { DisplayMemberPath = "Item2", SelectedValuePath = "Item1" };
        AutomationProperties.SetName(c, name);
        return c;
    }

    /// <summary>A filter's choices: All, then these; the one picked kept if it's still there.</summary>
    private void Refill(ComboBox box, IEnumerable<(string Id, string Name)> items)
    {
        filling = true;
        string? was = Picked(box);
        var list = new List<Tuple<string?, string>> { Tuple.Create<string?, string>(null, T("All")) };
        list.AddRange(items.Select(i => Tuple.Create<string?, string>(i.Id, i.Name)));
        box.ItemsSource = list;
        box.SelectedIndex = Math.Max(0, list.FindIndex(i => i.Item1 == was));
        filling = false;
    }

    private static string? Picked(ComboBox box) => (box.SelectedItem as Tuple<string?, string>)?.Item1;

    private static DataGrid Table() => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 360,
        CanUserAddRows = false, CanUserResizeRows = false, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.None,
    };

    /// <summary>A table's rows, sorted as the user last sorted it (a header's click).</summary>
    private static void Fill(DataGrid grid, System.Collections.IEnumerable rows)
    {
        var sorts = grid.Items.SortDescriptions.ToList();
        var directions = grid.Columns.Select(c => c.SortDirection).ToList();
        grid.ItemsSource = rows;
        foreach (var s in sorts) grid.Items.SortDescriptions.Add(s);
        for (int i = 0; i < directions.Count; i++) grid.Columns[i].SortDirection = directions[i];
    }

    /// <summary>Text columns, each sorted by its own value where the text isn't (times, dates).</summary>
    private static void Columns(DataGrid grid, params (string Header, string Path, string? SortBy)[] columns)
    {
        foreach (var (header, path, sortBy) in columns)
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), SortMemberPath = sortBy ?? path });
    }
}
