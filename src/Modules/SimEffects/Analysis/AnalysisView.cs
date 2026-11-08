using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The Analysis tab. Top: Record Laps (on / off), what it's reading. Then (since 3.28.0) Current Location (where the car
/// is, and Name Track's box: the place as a track, its name filled in when it's one known; Save and Export on its row,
/// since 4.6.2; greyed out with no place from the game; one card since 4.5.1, a third of the row) beside Racing
/// Statistics (since 4.6.2, <see cref="RaceStatsView"/>; beside it since 4.7.0, the Widgets before), the
/// session analysis (since 4.5.0, Forza Analysis' page: <see cref="Sessions.SessionsView"/>; a lap, best lap or race
/// clicked below opens its race there, right-click compares with it), Best Times (each track's best overall and per class
/// raced, a tile each, found by track or car), Best Laps, All Laps and Races a third each (since 4.6.2; the first two and
/// Best Times each with Track, Car and Class), the blocks (since 4.5.0 in one Widgets card, folded at first; since 4.7.0
/// here), the Map, Discovered Tracks (a table: renamed, sorted, filtered by game, deleted). Every card folds (its
/// chevron, top right; kept); since 4.7.0 a row of cards side by side also all together, the last one's blue chevron.
/// The units are on the Profiles tab's Profile card (since 3.28.0) and in Settings.
/// </summary>
internal sealed class AnalysisView : UserControl
{
    private readonly AnalysisApplet applet;
    private readonly AnalysisSettings settings;
    private readonly ToggleButton onOff;
    private readonly Button onOffKey;
    private readonly ToggleButton videoOn;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<BlockView> blocks = new();
    private readonly TextBlock saved = Ui.Note("");
    private readonly TextBox tagBox = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button addNew, tagSave;
    /// <summary>(since 4.10.8) Which of the game's maps it's on (Horizon's share their coordinates), and the game it's for.</summary>
    private readonly ComboBox mapBox = new() { VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private string? mapGame;
    private bool fillingMap;
    /// <summary>The game says where the car is (else Name Track's box is greyed out, "Unknown").</summary>
    private bool placed = true;
    private readonly TextBlock placeAt = Ui.Note("");
    /// <summary>The track the car is at (its race's, or the one starting nearest), and the name the tag box was filled
    /// with for it (kept while the user types over it).</summary>
    private Track? matched;
    private string autoText = "", hereText = "";
    private readonly Grid trackTiles = new();
    private readonly TextBox tileSearch = new() { Width = 200, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly DockPanel carName = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox carBox = new() { VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = false };
    private Button nameCar = null!;
    /// <summary>Track, Car and Class: Best Laps', All Laps' and Best Times' own (since 4.6.2; one set for the first two before).</summary>
    /// <summary>(since 4.8.0) Best Times' game: All games, or one.</summary>
    private readonly ComboBox tileGame = new() { Width = 150, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private bool fillingGames;
    private readonly Filters bestFilters = new(), lapFilters = new(), tileFilters = new();
    private readonly DataGrid best = Table(), laps = Table(), found = Table(), races = Table();
    private readonly RaceStatsView raceStats;
    private readonly TextBlock raceCount = Ui.Note("");
    private readonly TextBlock lapCount = Ui.Note(""), noTracks = Ui.Note(T("None yet: a track is added when a race on it ends its first lap or finishes, or with Name Track."));
    private readonly TextBlock gameHeader = new() { Text = T("Game"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button deleteTrack;
    private readonly MapView map;
    private readonly Sessions.SessionsView raceAnalysis;
    private string? gameFilter;
    private int car = -1;
    private string? carGame;

    public AnalysisView(AnalysisApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        raceStats = new RaceStatsView(r => raceAnalysis!.Open(r.Session, null));
        onOff = SimUi.OnOff(T("Record laps"), settings.Enabled, applet.SetEnabled);
        onOff.ToolTip = T("Record laps");
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        onOffKey = HotkeyWindow.Button(T("On / off shortcut"), () => applet.Hotkey);
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        // (since 4.8.0) each race recorded with Video → Recording's settings; greyed out while Recording is off
        videoOn = new ToggleButton { Content = T("Video"), Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0), IsChecked = settings.RecordVideo };
        videoOn.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(videoOn, T("Record video"));
        ToolTipService.SetShowOnDisabled(videoOn, true);
        videoOn.Click += (_, _) => applet.SetRecordVideo(videoOn.IsChecked == true);
        ShowVideo();
        DaisysApp.Shell.ScreenRecordings.Changed += ShowVideo;
        DockPanel.SetDock(videoOn, Dock.Left);
        top.Children.Add(videoOn);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        top.Children.Add(status);

        // the blocks on one row (since 3.31.1); since 3.33.0 their widgets are SimEffects → Overlays'; since 4.5.0 in one
        // card that folds (folded at first), each open, and without the Leaderboard (an overlay still, for iRacing)
        Border BlockCard(Block block, bool last)
        {
            var view = new BlockView(block);
            blocks.Add(view);
            var header = Ui.HeaderRow(block.Title);
            header.Margin = new Thickness(0, 0, 0, 10);
            var card = new Border { Child = new StackPanel { Children = { header, view } }, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 12),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, last ? 0 : 12, 0) };
            card.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return card;
        }
        // (since 3.30.2 without Vehicle and Laps: Timing, Position and Name Track have what they showed)
        var driving = new UniformGrid { Rows = 1 };
        driving.Children.Add(BlockCard(Blocks.Tires, false));
        driving.Children.Add(BlockCard(Blocks.Performance, false));
        driving.Children.Add(BlockCard(Blocks.Timing, false));
        driving.Children.Add(BlockCard(Blocks.Position, false));
        // (since 4.6.2) the track from your best lap's places, your line and the car
        driving.Children.Add(BlockCard(Blocks.TrackMap, true));
        // a name for the car being driven (Forza sends only its number; on Name Track since 3.30.2)
        AutomationProperties.SetName(carBox, T("Car name"));
        carBox.ToolTip = T("Car name");
        carBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveCar(); };
        // (since 4.6.4 always there, Current Location's second row, greyed out while no car is driven)
        nameCar = Icon("\uE804", T("Name Car"), ThenName(SaveCar));
        nameCar.IsEnabled = false;
        DockPanel.SetDock(nameCar, Dock.Right);
        carName.Children.Add(nameCar);
        carName.Children.Add(carBox);

        bestFilters.Changed += RenderLaps;
        lapFilters.Changed += RenderLaps;
        tileFilters.Changed += RenderTiles;
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
        // (since 4.6.4 icons)
        tagSave = Icon("\uE74E", T("Save"), ThenName(() => TagHere(false)));
        addNew = Icon("\uE710", T("Save this place as another track"), ThenName(() => TagHere(true)));
        addNew.Visibility = Visibility.Collapsed;
        // (since 3.24.7) the tracks and places, to a JSON file
        var export = Icon("\uEDE1", T("Export the tracks to a file"), ThenName(ExportTracks));
        // (in the game, a place without a track: "Unknown", picked whole to type over)
        tagBox.GotKeyboardFocus += (_, _) => { if (tagBox.Text == T("Unknown")) tagBox.Dispatcher.BeginInvoke(tagBox.SelectAll); };
        saved.Margin = new Thickness(0, 4, 0, 0);
        saved.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        // (no room kept for it until there's something to say)
        saved.Visibility = Visibility.Collapsed;
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(saved, (_, _) => saved.Visibility = saved.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
        placeAt.Visibility = Visibility.Collapsed;
        placeAt.Margin = new Thickness(0, 4, 0, 0);
        // (since 4.6.2 the box, Save and Export on one row; since 4.6.4 the track's name only in the box, where the car
        // is under it, the car's name on the second row)
        var tagRow = new DockPanel();
        foreach (var b in new[] { export, addNew, tagSave }) { DockPanel.SetDock(b, Dock.Right); tagRow.Children.Add(b); }
        tagRow.Children.Add(tagBox);
        // (since 4.10.8) the game's map, where it has more than one: picked, or found as it's driven
        mapBox.ToolTip = T("Map");
        AutomationProperties.SetName(mapBox, T("Map"));
        mapBox.SelectionChanged += (_, _) => { if (!fillingMap && mapGame != null && mapBox.SelectedItem is string m) applet.SetMap(mapGame, m, true); };
        // (since 4.5.1 Name Track is in Current Location's card, a quarter of the row, Widgets the rest)
        var placeCard = Foldable("Location", T("Current Location"), new StackPanel
        {
            Children = { tagRow, mapBox, carName, placeAt, saved },
        });
        placeCard.Margin = new Thickness(0, 0, 12, 12);

        // Discovered Tracks: a table sorted by any column (its header), Game's filter its funnel
        var funnel = VolumeControl.IconButton("");
        ((TextBlock)funnel.Content).FontSize = 11;
        funnel.Padding = new Thickness(6, 0, 0, 0);
        funnel.ToolTip = T("Filter by game");
        AutomationProperties.SetName(funnel, T("Filter by game"));
        funnel.Click += (_, e) => { GameMenu(funnel); e.Handled = true; };
        TrackColumns(new StackPanel { Orientation = Orientation.Horizontal, Children = { gameHeader, funnel } });
        // (since 4.5.1 several picked with Ctrl or Shift, deleted together)
        found.SelectionMode = DataGridSelectionMode.Extended;
        found.SelectionChanged += (_, _) => deleteTrack!.IsEnabled = found.SelectedItems.Count > 0;
        deleteTrack = SimUi.Confirm(T("Delete"), () =>
        {
            var picked = found.SelectedItems.OfType<TrackRow>().ToList();
            if (picked.Count == 0) return;
            foreach (var r in picked)
            {
                settings.Tracks.Remove(r.Track);
                applet.Store.RemoveTrack(r.Track.Id);
                Logging.Log.Here.Info($"Analysis: track {r.Track.Id} deleted with its laps");
            }
            settings.Save();
            RenderStats();
        }, T("Deletes the tracks picked and their laps"));
        deleteTrack.IsEnabled = false;
        deleteTrack.Margin = new Thickness(0, 0, 4, 0);

        lapCount.VerticalAlignment = VerticalAlignment.Center;
        lapCount.Margin = new Thickness(0, 0, 4, 0);
        raceCount.VerticalAlignment = VerticalAlignment.Center;
        raceCount.Margin = new Thickness(0, 0, 4, 0);
        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        // (since 4.6.2) the races' numbers (a line over Races before); a last race clicked opens it (since 4.6.4); since
        // 4.7.0 beside Current Location (the Widgets were, now over the Map)
        var statsCard = Foldable("RaceStats", T("Racing Statistics"), raceStats);
        Grid.SetColumn(statsCard, 1);
        var places = new Grid { Children = { placeCard, statsCard } };
        // (since 4.6.4 a third and two thirds; a quarter and three before)
        places.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        places.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        page.Children.Add(places);
        RowFold(placeCard, statsCard);
        // (since 4.5.0) the session analysis: Forza Analysis' page
        raceAnalysis = new Sessions.SessionsView(applet, (id, title, body, fill, right) => Foldable("Race." + id, title, body, fill, right), RowFold);
        foreach (var c in raceAnalysis.Cards) page.Children.Add(c);
        // (since 3.31.1) found as it's typed, by track or car; since 4.6.2 also by Track, Car and Class
        AutomationProperties.SetName(tileSearch, T("Search"));
        tileSearch.ToolTip = T("Track or car");
        tileSearch.TextChanged += (_, _) => RenderTiles();
        var tileTools = tileFilters.Row(150);
        AutomationProperties.SetName(tileGame, T("Game"));
        tileGame.ToolTip = T("Game");
        tileGame.SelectionChanged += (_, _) => { if (!fillingGames) RenderTiles(); };
        tileTools.Children.Insert(0, tileGame);
        tileTools.Children.Add(tileSearch);
        page.Children.Add(Foldable("BestTimes", T("Best Times"), trackTiles, tileTools));
        // (since 4.6.2) Best Laps, All Laps and Races a third each; the tables fill their cards, ending level, Races'
        // allowed the height of the filters the others have over it
        var bestRow = bestFilters.Grid();
        var lapRow = lapFilters.Grid();
        DockPanel.SetDock(bestRow, Dock.Top);
        DockPanel.SetDock(lapRow, Dock.Top);
        bestRow.SizeChanged += (_, _) => races.MaxHeight = best.MaxHeight + bestRow.ActualHeight + bestRow.Margin.Bottom;
        var bestCard = Foldable("BestLaps", T("Best Laps"), new DockPanel { Children = { bestRow, best } }, true);
        bestCard.Margin = new Thickness(0, 0, 12, 12);
        // (in a grid: the table hides itself with no laps, which mustn't open a folded card)
        var lapsCard = Foldable("Laps", T("All Laps"), new DockPanel { Children = { lapRow, new Grid { Children = { laps } } } }, true, lapCount);
        lapsCard.Margin = new Thickness(0, 0, 12, 12);
        Grid.SetColumn(lapsCard, 1);
        var racesCard = Foldable("Races", T("Races"), new Grid { Children = { races } }, true, raceCount);
        Grid.SetColumn(racesCard, 2);
        var tables = new Grid { Children = { bestCard, lapsCard, racesCard } };
        for (int i = 0; i < 3; i++) tables.ColumnDefinitions.Add(new ColumnDefinition());
        page.Children.Add(tables);
        RowFold(bestCard, lapsCard, racesCard);
        // (since 4.7.0 here, over the Map; beside Current Location before)
        page.Children.Add(Foldable("Widgets", T("Widgets"), driving, false, true));
        // (since 3.31.0) the tracks on the game's map (since 4.6.2 over Discovered Tracks)
        map = new MapView(applet);
        applet.MapChanged += () =>
        {
            mapGame = null;
            ShowMap(CarNames.Edition);
            RenderStats();
            map.Follow();
        };
        page.Children.Add(Foldable("Map", T("Map"), map));
        page.Children.Add(Foldable("Tracks", T("Discovered Tracks"), new StackPanel { Children = { noTracks, found } }, deleteTrack));
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(SimUi.Scroll(page));
        Content = dock;
        applet.Store.Changed += RenderStats;
        ClickToOpen(best, r => ((BestRow)r).Record);
        ClickToOpen(laps, r => ((LapRow)r).Record);
        ClickToOpen(races, r => ((RaceRow)r).Record);
    }

    public void ShowOnOff() => SimUi.ShowOnOff(onOff, settings.Enabled);

    private void ShowVideo()
    {
        bool can = DaisysApp.Shell.ScreenRecordings.Recorder != null;
        videoOn.IsEnabled = can;
        videoOn.ToolTip = can ? T("Record each race (Video → Recording's settings)") : T("Needs Video → Recording switched on");
    }

    /// <summary>(since 4.6.4) An icon button, its name as tooltip; Current Location's.</summary>
    private static Button Icon(string glyph, string name, Action click)
    {
        var b = BassShaker.VolumeControl.IconButton(glyph);
        b.ToolTip = name;
        AutomationProperties.SetName(b, name);
        b.Margin = new Thickness(4, 0, 0, 0);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A Name Track button's action, then (since 3.31.1) the name box focused and selected for the next name
    /// (queued: a file dialog gives the focus back after it closes).</summary>
    private Action ThenName(Action action) => () =>
    {
        action();
        Dispatcher.BeginInvoke(() => { tagBox.Focus(); tagBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    };

    private Border Foldable(string id, string title, UIElement body, params UIElement[] right) => Foldable(id, title, body, false, right);

    private Border Foldable(string id, string title, UIElement body, bool fill, params UIElement[] right) => Foldable(id, title, body, fill, false, right);

    /// <summary>A card with its title and, top right, these buttons and its fold / open chevron (since 3.28.0): folded,
    /// only the title shows; kept in <see cref="AnalysisSettings.Folded"/>. <paramref name="fill"/>: the body takes all
    /// the height the card is given. <paramref name="foldedAtFirst"/> (since 4.5.0): folded until it's opened (the id
    /// in Folded then means open).</summary>
    private Border Foldable(string id, string title, UIElement body, bool fill, bool foldedAtFirst, params UIElement[] right)
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
        var fold1 = new Fold { Id = id, FoldedAtFirst = foldedAtFirst, Buttons = buttons };
        folds[card] = fold1;
        void Show()
        {
            bool folded = settings.Folded.Contains(id) != foldedAtFirst;
            body.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            header.Margin = new Thickness(0, 0, 0, folded ? 0 : 10);
            card.VerticalAlignment = folded ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            ((TextBlock)fold.Content).Text = folded ? "" : "";
            fold.ToolTip = folded ? T("Open") : T("Fold");
            AutomationProperties.SetName(fold, folded ? F("Open {0}", title) : F("Fold {0}", title));
        }
        fold1.Show = Show;
        fold.Click += (_, _) =>
        {
            if (!settings.Folded.Remove(id)) settings.Folded.Add(id);
            settings.Save();
            Show();
            fold1.Changed?.Invoke();
        };
        Show();
        return card;
    }

    /// <summary>A card's fold: its id in <see cref="AnalysisSettings.Folded"/>, its title row's buttons, and what shows it.</summary>
    private sealed class Fold
    {
        public required string Id { get; init; }
        public bool FoldedAtFirst { get; init; }
        public required StackPanel Buttons { get; init; }
        public Action Show { get; set; } = () => { };
        /// <summary>Folded or opened by its own chevron (its row's blue one follows).</summary>
        public Action? Changed { get; set; }
    }

    private readonly Dictionary<Border, Fold> folds = new();

    /// <summary>(since 4.7.0) Cards side by side: a blue chevron after the last one's own folds them all, or opens them
    /// all when they're all folded.</summary>
    private void RowFold(params Border[] cards)
    {
        var row = cards.Select(c => folds[c]).ToList();
        bool Folded(Fold f) => settings.Folded.Contains(f.Id) != f.FoldedAtFirst;
        var button = VolumeControl.IconButton("");
        var glyph = (TextBlock)button.Content;
        glyph.FontSize = 11;
        glyph.Foreground = Ui.Brush(Color.FromRgb(0x4D, 0xA3, 0xFF));
        void Show()
        {
            bool all = row.All(Folded);
            glyph.Text = all ? "\uE70D" : "\uE70E";
            button.ToolTip = all ? T("Open the row") : T("Fold the row");
            AutomationProperties.SetName(button, all ? T("Open the row") : T("Fold the row"));
        }
        button.Click += (_, _) =>
        {
            bool fold = !row.All(Folded);
            foreach (var f in row)
            {
                if (Folded(f) == fold) continue;
                if (!settings.Folded.Remove(f.Id)) settings.Folded.Add(f.Id);
                f.Show();
            }
            settings.Save();
            Show();
        };
        foreach (var f in row) f.Changed += Show;
        row[^1].Buttons.Children.Add(button);
        Show();
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
        if (now.Game != mapGame) ShowMap(now.Game);
        ShowHere(now);
        map.Tick(now);
        int c = now.Frame is { HasRace: true, CarOrdinal: > 0 } f ? f.CarOrdinal : -1;
        if (c != car || now.Game != carGame)
        {
            car = c;
            carGame = now.Game;
            carBox.IsEnabled = nameCar.IsEnabled = c > 0;
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
            t.Game, t.Map, Name = settings.TrackName(t.Id), Type = MapView.Kind(t), Form = MapView.Form(t), t.X, t.Y, t.Z, t.Length, t.Laps, t.Racers, t.Found,
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

    /// <summary>Current Location: Name Track's box has the track the car is at (its race's, or the one starting nearest
    /// it, within the radius a race's start is looked for), or Unknown (greyed out with no place from the game), and
    /// under it where the car is.</summary>
    private void ShowHere(Reading now)
    {
        var here = applet.Here();
        bool live = now.Frame is { HasRace: true } f && !(f.PosX == 0 && f.PosY == 0 && f.PosZ == 0);
        string at = now.Frame is { } g && live ? F("{0:0}, {1:0}", g.PosX, g.PosZ) + (here is { } n && !float.IsNaN(n.Metres) ? " · " + F("{0:0} m from its start", n.Metres) : "") : "";
        if (at != hereText)
        {
            hereText = at;
            placeAt.Text = at;
            placeAt.Visibility = at.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var t = live ? here?.Track : null;
        if (live != placed)
        {
            // (since 4.6.2) no place from the game: "Unknown", greyed out
            placed = live;
            tagBox.IsEnabled = tagSave.IsEnabled = live;
            matched = t;
            addNew.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
            autoText = tagBox.Text = t != null ? settings.TrackName(t.Id) : T("Unknown");
            return;
        }
        if (t == matched || !live) return;
        matched = t;
        addNew.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
        // (what the user's typing stays)
        if (!tagBox.IsKeyboardFocusWithin || tagBox.Text == autoText)
        {
            autoText = t != null ? settings.TrackName(t.Id) : T("Unknown");
            tagBox.Text = autoText;
            if (tagBox.IsKeyboardFocusWithin) tagBox.SelectAll();
        }
    }

    /// <summary>The map box: the game's maps, the one it's on picked (none for a game with one map).</summary>
    private void ShowMap(string? game)
    {
        mapGame = game;
        var maps = game != null && AnalysisSettings.Worlds.TryGetValue(game, out var w) ? w : null;
        mapBox.Visibility = maps != null ? Visibility.Visible : Visibility.Collapsed;
        if (maps == null) return;
        fillingMap = true;
        mapBox.ItemsSource = maps;
        mapBox.SelectedItem = settings.MapOf(game) ?? maps[0];
        fillingMap = false;
    }

    /// <summary>Save: the track the car is at renamed, or (none, or Add New) the place it is saved as a track.</summary>
    private void TagHere(bool asNew)
    {
        string name = tagBox.Text.Trim();
        if (name.Length == 0 || (name == T("Unknown") && (asNew || matched == null))) { tagBox.Focus(); tagBox.SelectAll(); return; }
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
        Logging.Log.Here.Info($"Analysis: track named \"{t.Name}\"");
        // (since 4.10.12 a race's track named as a place: the place is where its event is entered)
        settings.AdoptPlaces();
        settings.Save();
        RenderStats();
    }

    // ---------------------------------------------------------------- the stats

    private string Distance(float m) => settings.Units == AnalysisUnits.Metric ? $"{m / 1000:0.00} km" : $"{m / 1609.34:0.00} mi";

    private void RenderStats()
    {
        // (since 4.6.2 A to Z, the classes fastest first)
        var tracks = settings.Tracks.Select(t => (t.Id, settings.TrackName(t.Id))).OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
        var cars = applet.Store.All.Select(l => (l.Game, l.Car)).Distinct().Select(c => ($"{c.Game}:{c.Car}", $"{settings.CarName(c.Game, c.Car)} ({c.Game})"))
            .OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
        // (a class's number can be another letter in another game: Horizon 6's 6 is R, the others' X)
        var classes = applet.Store.All.GroupBy(l => l.Class).OrderByDescending(g => g.Key).Select(g =>
            (g.Key.ToString(CultureInfo.InvariantCulture), string.Join(" / ", g.Select(l => Blocks.ClassName(l.Class, l.Game)).Distinct()))).ToList();
        foreach (var f in new[] { bestFilters, lapFilters, tileFilters }) f.Refill(tracks, cars, classes);
        FillGames();
        RenderLaps();
        RenderRaces();
        RenderTiles();
        RenderTracks();
        map.Refresh();
        raceAnalysis.Refresh();
    }

    /// <summary>(since 4.5.0) A row's lap or race: a click opens its race in the race analysis (that lap picked);
    /// right-click offers Compare With. Rows without a session log (before 4.5.0) do nothing.</summary>
    private void ClickToOpen(DataGrid grid, Func<object, object> record)
    {
        (string? Session, int? Lap) Of(object row) => record(row) switch
        {
            LapRecord l => (l.Session, l.Lap),
            RaceRecord r => (r.Session, null),
            _ => (null, null),
        };
        DataGridRow? RowAt(MouseButtonEventArgs e) =>
            e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow row ? row : null;
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is { } item && Of(item) is { Session: not null } s) raceAnalysis.Open(s.Session, s.Lap);
        };
        grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (RowAt(e)?.Item is { } item && Of(item) is { Session: not null } s) Dispatcher.BeginInvoke(() => raceAnalysis.Open(s.Session, s.Lap));
        };
        grid.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (RowAt(e)?.Item is not { } item || Of(item) is not { Session: not null } s) return;
            var menu = new ContextMenu();
            var open = new MenuItem { Header = T("Analyse") };
            open.Click += (_, _) => raceAnalysis.Open(s.Session, s.Lap);
            var with = new MenuItem { Header = T("Compare With") };
            with.Click += (_, _) => raceAnalysis.CompareWith(s.Session, s.Lap);
            menu.Items.Add(open);
            menu.Items.Add(with);
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    /// <summary>Races: every race finished, newest first, and (since 4.6.2 in Racing Statistics) the numbers over them.</summary>
    private void RenderRaces()
    {
        var all = applet.Store.Races;
        var rows = all.OrderByDescending(r => r.At).Take(1000).Select(r =>
        {
            int gained = r.Start > 0 && r.Finish > 0 ? r.Start - r.Finish : 0;
            return new RaceRow(r, r.At, r.At.ToString("g", CultureInfo.CurrentCulture), r.Game, settings.TrackName(r.Track), settings.CarName(r.Game, r.Car),
                Blocks.ClassName(r.Class, r.Game), r.Laps, r.Time, Blocks.LapTime(r.Time), r.Start, Place(r.Start, r.Racers), r.Finish, Place(r.Finish, r.Racers),
                gained, r.Start > 0 && r.Finish > 0 ? gained.ToString("+0;-0;0", CultureInfo.CurrentCulture) : Blocks.None);
        }).ToList();
        Fill(races, rows);
        races.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        raceCount.Text = all.Count == 0 ? T("No races yet") : P(all.Count, "{0} race", "{0} races");
        raceStats.Render(all);
    }

    /// <summary>A place: "3 of 12", "3" when how many is unknown, "—" when the place is.</summary>
    internal static string Place(int pos, int of) => pos <= 0 ? Blocks.None : of >= pos ? F("{0} of {1}", pos, of) : pos.ToString(CultureInfo.CurrentCulture);

    private void RenderLaps()
    {
        var picked = applet.Store.All.Where(lapFilters.Keeps).ToList();
        // best by track, car and class
        var groups = applet.Store.All.Where(bestFilters.Keeps).GroupBy(l => (l.Track, l.Game, l.Car, l.Class)).Select(g =>
        {
            var b = g.MinBy(l => l.Time)!;
            var last = g.Max(l => l.At);
            return new BestRow(b, g.Key.Game, settings.TrackName(g.Key.Track), settings.CarName(g.Key.Game, g.Key.Car), Blocks.ClassName(g.Key.Class, g.Key.Game), b.Pi,
                b.Time, Blocks.LapTime(b.Time), b.At, b.At.ToString("MMM d, yyyy", CultureInfo.CurrentCulture), g.Count(), last, last.ToString("g", CultureInfo.CurrentCulture));
        }).OrderBy(r => r.Track).ThenBy(r => r.Best).ToList();
        Fill(best, groups);
        best.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // every lap, newest first, with its gap to the best of its track, car and class
        var bests = picked.GroupBy(l => (l.Track, l.Car, l.Class)).ToDictionary(g => g.Key, g => g.Min(l => l.Time));
        var rows = picked.OrderByDescending(l => l.At).Take(1000).Select(l =>
        {
            float gap = l.Time - bests[(l.Track, l.Car, l.Class)];
            return new LapRow(l, l.At, l.At.ToString("g", CultureInfo.CurrentCulture), l.Game, settings.TrackName(l.Track), settings.CarName(l.Game, l.Car),
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
        string? game = (tileGame.SelectedItem as ComboBoxItem)?.Tag as string;
        const int Across = 4;
        trackTiles.Children.Clear();
        trackTiles.RowDefinitions.Clear();
        if (trackTiles.ColumnDefinitions.Count == 0)
            for (int i = 0; i < Across; i++) trackTiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ((FrameworkElement)((FrameworkElement)trackTiles.Parent).Parent).Visibility = settings.Tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var byTrack = applet.Store.All.Where(tileFilters.Keeps).ToLookup(l => l.Track);
        string search = tileSearch.Text.Trim();
        bool Found(string name) => name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        var cars = search.Length > 0 ? applet.Store.All.Select(l => (l.Game, l.Car)).Distinct().Where(c => Found(settings.CarName(c.Game, c.Car))).ToHashSet() : [];
        int n = 0;
        foreach (var t in settings.Tracks)
        {
            if (game != null && t.Game != game) continue;
            var mine = byTrack[t.Id].ToList();
            if (tileFilters.Hides(t.Id, mine.Count)) continue;
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
                // (since 4.5.0) its race in the race analysis
                if (lap?.Session is { } session)
                {
                    value.Cursor = Cursors.Hand;
                    value.MouseLeftButtonUp += (_, _) => raceAnalysis.Open(session, lap.Lap);
                }
                Grid.SetRow(name, r); Grid.SetRow(value, r); Grid.SetColumn(value, 1);
                lines.Children.Add(name); lines.Children.Add(value);
            }
            Line(T("Overall"), mine.MinBy(l => l.Time), car: false);
            // (since 3.28.0 only the classes raced there)
            foreach (int c in Blocks.Classes(t.Game, mine))
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

    /// <summary>(since 4.8.0) Best Times' Game box: All games, then each game with tracks, the one picked kept.</summary>
    private void FillGames()
    {
        string? was = (tileGame.SelectedItem as ComboBoxItem)?.Tag as string;
        fillingGames = true;
        tileGame.Items.Clear();
        tileGame.Items.Add(new ComboBoxItem { Content = T("All games") });
        foreach (var g in settings.Tracks.Select(t => t.Game).Distinct().OrderBy(g => g))
            tileGame.Items.Add(new ComboBoxItem { Content = g, Tag = g });
        tileGame.SelectedItem = tileGame.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == was) ?? tileGame.Items[0];
        fillingGames = false;
    }

    /// <summary>Discovered Tracks: a row per track (of the game picked by Game's filter), with the track starting nearest
    /// it, so a sort by Nearest (or Location) puts the ones near each other together.</summary>
    private void RenderTracks()
    {
        var games = settings.Tracks.Select(t => t.Game).Distinct().ToList();
        if (gameFilter != null && !games.Contains(gameFilter)) gameFilter = null;
        gameHeader.Text = gameFilter == null ? T("Game") : F("Game: {0}", gameFilter);
        // (since 4.6.4 newest first, until a column's sorted)
        var rows = settings.Tracks.Where(t => gameFilter == null || t.Game == gameFilter).OrderByDescending(t => t.Found).Select(t =>
        {
            int count = applet.Store.All.Count(l => l.Track == t.Id);
            var near = settings.Tracks.Where(o => o != t && o.Game == t.Game && o.Map == t.Map).Select(o => (o, m: MathF.Sqrt((o.X - t.X) * (o.X - t.X) + (o.Y - t.Y) * (o.Y - t.Y) + (o.Z - t.Z) * (o.Z - t.Z))))
                .OrderBy(x => x.m).FirstOrDefault();
            return new TrackRow(this, t)
            {
                Game = t.Game, Maps = t.Key == null ? AnalysisSettings.Worlds.GetValueOrDefault(t.Game) : null, Laps = count, Length = t.Length, LengthText = t.Length > 0 ? Distance(t.Length) : T("no race yet"),
                LocationText = F("{0:0}, {1:0}", t.X, t.Z), Cell = Cell(t.X, t.Z),
                NearestText = near.o != null ? $"{settings.TrackName(near.o.Id)} · {Distance(near.m)}" : Blocks.None, NearestM = near.o != null ? near.m : float.MaxValue,
                Found = t.Found, FoundText = t.Found.ToString("d", CultureInfo.CurrentCulture),
            };
        }).ToList();
        Fill(found, rows);
        found.Visibility = rows.Count > 0 || gameFilter != null ? Visibility.Visible : Visibility.Collapsed;
        noTracks.Visibility = settings.Tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        deleteTrack.IsEnabled = found.SelectedItems.Count > 0;
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
        // (since 4.10.8) the game's map it's on, picked in its row (Horizon's; none for the others)
        var pick = new FrameworkElementFactory(typeof(ComboBox));
        pick.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(TrackRow.Maps)));
        pick.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(TrackRow.Map)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        pick.SetBinding(VisibilityProperty, new Binding(nameof(TrackRow.HasMaps)) { Converter = new BooleanToVisibilityConverter() });
        pick.SetValue(ToolTipProperty, T("Map"));
        found.Columns.Add(new DataGridTemplateColumn { Header = T("Map"), CellTemplate = new DataTemplate { VisualTree = pick }, SortMemberPath = nameof(TrackRow.Map) });
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

    private sealed record BestRow(LapRecord Record, string Game, string Track, string Car, string Class, int Pi, float Best, string BestText, DateTime Date, string DateText, int Laps, DateTime Last, string LastText);

    private sealed record RaceRow(RaceRecord Record, DateTime When, string WhenText, string Game, string Track, string Car, string Class, int Laps, float Time, string TimeText,
        int Start, string StartText, int Finish, string FinishText, int Gained, string GainedText);

    private sealed record LapRow(LapRecord Record, DateTime When, string WhenText, string Game, string Track, string Car, string Class, int Pi, int Lap, float Time, string TimeText, float Gap, string GapText);

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
        /// <summary>The game's maps (null: one, or a track the game names).</summary>
        public string[]? Maps { get; init; }
        public bool HasMaps => Maps != null;
        public string? Map
        {
            get => Maps == null ? null : Track.Map ?? Maps[0];
            set
            {
                if (Maps == null || value == null || value == Map) return;
                Track.Map = value == Maps[0] ? null : value;
                view.settings.Save();
                Logging.Log.Here.Info($"Analysis: track {Track.Id} on {value}");
                view.Dispatcher.BeginInvoke(view.RenderStats);
            }
        }
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

    /// <summary>Track, Car and Class (since 4.6.2 one set per card): All, then each, A to Z (classes fastest first); the
    /// one picked kept while it's still there.</summary>
    private sealed class Filters
    {
        private readonly ComboBox track = Box(T("Track"), T("All tracks")), car = Box(T("Car"), T("All cars")), cls = Box(T("Class"), T("All classes"));
        private bool filling;

        public event Action? Changed;

        public Filters()
        {
            foreach (var b in new[] { track, car, cls }) b.SelectionChanged += (_, _) => { if (!filling) Changed?.Invoke(); };
        }

        private static ComboBox Box(string name, string all)
        {
            var c = new ComboBox { DisplayMemberPath = "Item2", SelectedValuePath = "Item1", ToolTip = name, Tag = all, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(c, name);
            return c;
        }

        /// <summary>The three on a row sharing its width.</summary>
        public Grid Grid()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            int i = 0;
            foreach (var b in new[] { track, car, cls })
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
                b.Margin = new Thickness(0, 0, b == cls ? 0 : 8, 0);
                System.Windows.Controls.Grid.SetColumn(b, i++);
                g.Children.Add(b);
            }
            return g;
        }

        /// <summary>The three side by side, each this wide (a card's title row).</summary>
        public StackPanel Row(double width)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var b in new[] { track, car, cls })
            {
                b.Width = width;
                b.Margin = new Thickness(0, 0, 6, 0);
                p.Children.Add(b);
            }
            return p;
        }

        public void Refill(IEnumerable<(string Id, string Name)> tracks, IEnumerable<(string Id, string Name)> cars, IEnumerable<(string Id, string Name)> classes)
        {
            filling = true;
            Fill(track, tracks);
            Fill(car, cars);
            Fill(cls, classes);
            filling = false;
        }

        private static void Fill(ComboBox box, IEnumerable<(string Id, string Name)> items)
        {
            string? was = Picked(box);
            var list = new List<Tuple<string?, string>> { Tuple.Create<string?, string>(null, (string)box.Tag) };
            list.AddRange(items.Select(i => Tuple.Create<string?, string>(i.Id, i.Name)));
            box.ItemsSource = list;
            box.SelectedIndex = Math.Max(0, list.FindIndex(i => i.Item1 == was));
        }

        private static string? Picked(ComboBox box) => (box.SelectedItem as Tuple<string?, string>)?.Item1;

        public bool Keeps(LapRecord l) =>
            (Picked(track) is not { } t || l.Track == t) &&
            (Picked(car) is not { } c || $"{l.Game}:{l.Car}" == c) &&
            (Picked(cls) is not { } k || l.Class.ToString(CultureInfo.InvariantCulture) == k);

        /// <summary>A track's tile is left out: another track picked, or a car or class picked that has no laps there.</summary>
        public bool Hides(string trackId, int laps) =>
            (Picked(track) is { } t && t != trackId) || ((Picked(car) != null || Picked(cls) != null) && laps == 0);
    }

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
