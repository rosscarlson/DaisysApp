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

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The Analysis tab. Top: Record Laps (on / off), what it's reading, the units. Then the place the car is, to save as a
/// track (since 3.27.1; a new track found asked for a name there before), the blocks (each pops out as a widget: its button, or right-click → Make Widget), the open widgets, and the
/// laps: each track's bests (overall and per class, a tile each), best by track, car and class, every lap (filtered by
/// track, car and class), and the tracks (renamed, deleted).
/// </summary>
internal sealed class AnalysisView : UserControl
{
    private readonly AnalysisApplet applet;
    private readonly AnalysisSettings settings;
    private readonly ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("");
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<BlockView> blocks = new();
    private readonly StackPanel tracks = new();
    private readonly TextBlock here = Ui.Secondary(""), saved = Ui.Secondary("");
    private readonly TextBox tagBox = new() { Width = 260, Margin = new Thickness(0, 8, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private string hereText = "";
    private readonly UniformGrid trackTiles = new() { Columns = 4 };
    private readonly StackPanel carName = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox carBox = new() { Width = 170, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox trackFilter = Filter(T("Track")), carFilter = Filter(T("Car")), classFilter = Filter(T("Class"));
    private readonly DataGrid best = Table(), laps = Table();
    private readonly TextBlock lapCount = Ui.Secondary("");
    private int car = -1;
    private string? carGame;
    private bool filling;

    public AnalysisView(AnalysisApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = Ui.OnOff(T("Record laps"), settings.Enabled, applet.SetEnabled);
        onOff.ToolTip = T("Record laps");
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(onOffKey, T("On / off shortcut"));
        ShowKey();
        onOffKey.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), applet.Hotkey); ShowKey(); };
        var (units, temperature) = AnalysisSettingsView.UnitBoxes(applet);
        temperature.Margin = new Thickness(6, 0, 0, 0);
        // (since 3.24.7) the tracks and places, to a JSON file
        var export = FlagButton(T("Export"), T("Export the tracks to a file"));
        export.Click += (_, _) => ExportTracks();
        var unitBoxes = new StackPanel { Orientation = Orientation.Horizontal, Children = { units, temperature, export } };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        DockPanel.SetDock(unitBoxes, Dock.Right);
        top.Children.Add(unitBoxes);
        top.Children.Add(status);

        // the blocks, each a card that pops out as a widget: the car and the race, then the ones for driving
        Border BlockCard(Block block, bool last)
        {
            var view = new BlockView(block);
            blocks.Add(view);
            var popOut = VolumeControl.IconButton("\uE8A7");
            popOut.ToolTip = T("Pop out");
            AutomationProperties.SetName(popOut, F("Pop out {0}", block.Title));
            popOut.Click += (_, _) => applet.MakeWidget(block);
            // (since 3.27.1) its widget's on / off shortcut
            var key = applet.WidgetKey(block);
            var keyButton = VolumeControl.IconButton("\uE7FC");
            keyButton.Margin = new Thickness(0, 0, 4, 0);
            AutomationProperties.SetName(keyButton, F("{0} widget shortcut", block.Title));
            void ShowWidgetKey() => ShowKey(keyButton, key, T("Widget on / off shortcut"));
            ShowWidgetKey();
            keyButton.Click += (_, _) => { HotkeyWindow.Open(T("Widget on / off shortcut"), Window.GetWindow(this), key); ShowWidgetKey(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { keyButton, popOut } };
            var content = new StackPanel { Children = { Ui.HeaderLine(block.Title, buttons), view } };
            if (block == Blocks.Vehicle) content.Children.Add(carName);
            var card = Ui.Card(content);
            card.Margin = new Thickness(0, 0, last ? 0 : 12, 12);
            Widget.Offer(card, () => applet.MakeWidget(block));
            return card;
        }
        var row = new UniformGrid { Columns = 3 };
        row.Children.Add(BlockCard(Blocks.Vehicle, false));
        row.Children.Add(BlockCard(Blocks.Tires, false));
        row.Children.Add(BlockCard(Blocks.Laps, true));
        var driving = new UniformGrid { Columns = 3 };
        driving.Children.Add(BlockCard(Blocks.Performance, false));
        driving.Children.Add(BlockCard(Blocks.Timing, false));
        driving.Children.Add(BlockCard(Blocks.Position, true));
        // a name for the car being driven (Forza sends only its number)
        AutomationProperties.SetName(carBox, T("Car name"));
        carBox.ToolTip = T("Car name");
        carBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveCar(); };
        carName.Children.Add(carBox);
        carName.Children.Add(Ui.Button(T("Name Car"), SaveCar));
        carName.Children[1].SetValue(MarginProperty, new Thickness(6, 0, 0, 0));
        carName.Visibility = Visibility.Collapsed;

        foreach (var f in new[] { trackFilter, carFilter, classFilter }) f.SelectionChanged += (_, _) => { if (!filling) RenderLaps(); };
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (label, box) in new[] { (T("Track"), trackFilter), (T("Car"), carFilter), (T("Class"), classFilter) })
        {
            filters.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            box.Margin = new Thickness(0, 0, 16, 0);
            filters.Children.Add(box);
        }
        Columns(best, (T("Game"), "Game", null), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null), (T("PI"), "Pi", null),
            (T("Best"), "BestText", "Best"), (T("Laps"), "Laps", null), (T("Last driven"), "LastText", "Last"));
        Columns(laps, (T("When"), "WhenText", "When"), (T("Game"), "Game", null), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null),
            (T("PI"), "Pi", null), (T("Lap"), "Lap", null), (T("Time"), "TimeText", "Time"), (T("Gap"), "GapText", "Gap"));

        // the place the car is, saved as a track whenever it's asked (where an event starts)
        AutomationProperties.SetName(tagBox, T("Track name"));
        tagBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TagHere(); };
        var tagSave = Ui.Button(T("Save"), TagHere, accent: true);
        tagSave.Margin = new Thickness(0, 8, 12, 0);
        saved.VerticalAlignment = VerticalAlignment.Center;
        saved.Margin = new Thickness(0, 8, 0, 0);
        saved.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("New track")), here, new WrapPanel { Children = { tagBox, tagSave, saved } } } }));
        page.Children.Add(row);
        page.Children.Add(driving);
        page.Children.Add(Widget.ListCard());
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Tracks")), trackTiles } }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Best laps")), filters, best } }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderLine(T("Laps"), lapCount), laps } }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Track names")), tracks } }));
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(Ui.Scroll(page));
        Content = dock;
        applet.Store.Changed += RenderStats;
    }

    public void ShowOnOff() => Ui.ShowOnOff(onOff, settings.Enabled);

    private void ShowKey() => ShowKey(onOffKey, applet.Hotkey, T("On / off shortcut"));

    /// <summary>A shortcut button: in the accent while it has a key, which its tooltip names.</summary>
    private static void ShowKey(Button button, Hotkey key, string tip)
    {
        ((TextBlock)button.Content).SetResourceReference(TextBlock.ForegroundProperty, key.Value != null ? "AccentBrush" : "TextBrush");
        button.ToolTip = key.Value is { } k ? $"{tip}: {k}" : tip;
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

    /// <summary>A button with the flag and a word (Export), at the top right.</summary>
    private static Button FlagButton(string text, string tip)
    {
        var flag = new TextBlock { Text = "", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        flag.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { flag, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } },
            Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip,
        };
        AutomationProperties.SetName(button, text);
        return button;
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
            t.Game, Name = settings.TrackName(t.Id), t.X, t.Y, t.Z, t.Length, t.Laps, t.Racers, t.Found,
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

    /// <summary>Where the car is now, and the track starting nearest it (within the radius a race's start is looked for).</summary>
    private void ShowHere(Reading now)
    {
        string text;
        if (now.Frame is not { HasRace: true } f || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) text = T("No place from the game yet");
        else
        {
            text = F("Here: {0:0}, {1:0}", f.PosX, f.PosZ);
            if (applet.Nearest() is { } n) text += " · " + F("{0}, {1:0} m away", settings.TrackName(n.Track.Id), n.Metres);
        }
        if (text != hereText) here.Text = hereText = text;
    }

    /// <summary>Save: the place the car is, as a track with the name typed; the box empties for the next.</summary>
    private void TagHere()
    {
        string name = tagBox.Text.Trim();
        if (name.Length == 0) { tagBox.Focus(); return; }
        if (applet.NameHere(name) is not { } t)
        {
            saved.Text = T("No place from the game yet");
            return;
        }
        tagBox.Clear();
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
        RenderTiles();
        RenderTracks();
    }

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
                b.Time, Blocks.LapTime(b.Time), g.Count(), last, last.ToString("g", CultureInfo.CurrentCulture));
        }).OrderBy(r => r.Track).ThenBy(r => r.Best).ToList();
        best.ItemsSource = groups;
        best.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // every lap, newest first, with its gap to the best of its track, car and class
        var bests = picked.GroupBy(l => (l.Track, l.Car, l.Class)).ToDictionary(g => g.Key, g => g.Min(l => l.Time));
        var rows = picked.OrderByDescending(l => l.At).Take(1000).Select(l =>
        {
            float gap = l.Time - bests[(l.Track, l.Car, l.Class)];
            return new LapRow(l.At, l.At.ToString("g", CultureInfo.CurrentCulture), l.Game, settings.TrackName(l.Track), settings.CarName(l.Game, l.Car),
                Blocks.ClassName(l.Class, l.Game), l.Pi, l.Lap, l.Time, Blocks.LapTime(l.Time), gap, gap > 0.0005f ? $"+{gap:0.000}" : T("Best"));
        }).ToList();
        laps.ItemsSource = rows;
        laps.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        lapCount.Text = picked.Count == 0 ? T("No laps yet") : P(picked.Count, "{0} lap", "{0} laps");
    }

    /// <summary>A tile per track: its name and laps, the best lap overall, then each class's best (with its car), the
    /// fastest class first.</summary>
    private void RenderTiles()
    {
        trackTiles.Children.Clear();
        ((FrameworkElement)((FrameworkElement)trackTiles.Parent).Parent).Visibility = settings.Tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var byTrack = applet.Store.All.ToLookup(l => l.Track);
        foreach (var t in settings.Tracks)
        {
            var mine = byTrack[t.Id].ToList();
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
            foreach (int c in ForzaSource.Classes(t.Game))
                Line(F("{0} Class", Blocks.ClassName(c, t.Game)), mine.Where(l => l.Class == c).MinBy(l => l.Time), car: true);
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
            trackTiles.Children.Add(tile);
        }
    }

    private void RenderTracks()
    {
        tracks.Children.Clear();
        if (settings.Tracks.Count == 0) tracks.Children.Add(Ui.Secondary(T("None yet: a track is added when a race on it ends its first lap or finishes, or with Name.")));
        foreach (var t in settings.Tracks)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var box = new TextBox { Text = t.Name, Width = 220, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, F("Name of {0}", settings.TrackName(t.Id)));
            box.LostFocus += (_, _) => { if (box.Text.Trim() != t.Name) Rename(t, box.Text); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter && box.Text.Trim() != t.Name) Rename(t, box.Text); };
            var delete = Ui.Confirm(T("Delete"), () =>
            {
                settings.Tracks.Remove(t);
                settings.Save();
                applet.Store.RemoveTrack(t.Id);
                Logging.Log.Here.Info($"Analysis: track {t.Id} deleted with its laps");
                RenderStats();
            }, T("Deletes the track and its laps"));
            DockPanel.SetDock(box, Dock.Left);
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(box);
            row.Children.Add(delete);
            int count = applet.Store.All.Count(l => l.Track == t.Id);
            row.Children.Add(new TextBlock
            {
                Text = $"{t.Game} · {P(count, "{0} lap", "{0} laps")} · {(t.Length > 0 ? F("a lap {0}", Distance(t.Length)) : T("no race yet"))} · {F("found {0:d}", t.Found)}",
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            });
            tracks.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- pieces

    private sealed record BestRow(string Game, string Track, string Car, string Class, int Pi, float Best, string BestText, int Laps, DateTime Last, string LastText);

    private sealed record LapRow(DateTime When, string WhenText, string Game, string Track, string Car, string Class, int Pi, int Lap, float Time, string TimeText, float Gap, string GapText);

    private static ComboBox Filter(string name)
    {
        var c = new ComboBox { Width = 170, DisplayMemberPath = "Item2", SelectedValuePath = "Item1" };
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

    /// <summary>Text columns, each sorted by its own value where the text isn't (times, dates).</summary>
    private static void Columns(DataGrid grid, params (string Header, string Path, string? SortBy)[] columns)
    {
        foreach (var (header, path, sortBy) in columns)
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), SortMemberPath = sortBy ?? path });
    }
}
