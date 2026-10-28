using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The Analysis tab. Top: Record Laps (on / off), what it's reading, the units. Then a new track to name (when one was
/// found), the blocks (each pops out as a widget: its button, or right-click → Make Widget), the open widgets, and the
/// laps: best by track, car and class, every lap (filtered by track, car and class), and the tracks (renamed, deleted).
/// </summary>
internal sealed class AnalysisView : UserControl
{
    private readonly AnalysisApplet applet;
    private readonly AnalysisSettings settings;
    private readonly ToggleButton onOff;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<BlockView> blocks = new();
    private readonly StackPanel nameTrack = new(), tracks = new();
    private readonly StackPanel carName = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox carBox = new() { Width = 170, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox trackFilter = Filter(T("Track")), carFilter = Filter(T("Car")), classFilter = Filter(T("Class"));
    private readonly DataGrid best = Table(), laps = Table();
    private readonly TextBlock lapCount = Ui.Secondary("");
    private Track? asking;
    private int car = -1;
    private bool filling;

    public AnalysisView(AnalysisApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = Ui.OnOff(T("Record laps"), settings.Enabled, applet.SetEnabled);
        onOff.ToolTip = T("Record laps");
        var units = new ComboBox { ItemsSource = new[] { T("Imperial"), T("Metric") }, SelectedIndex = (int)settings.Units, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(units, T("Units"));
        units.SelectionChanged += (_, _) => { settings.Units = (AnalysisUnits)units.SelectedIndex; settings.Save(); RenderStats(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(units, Dock.Right);
        top.Children.Add(units);
        top.Children.Add(status);

        // the blocks, each a card that pops out as a widget
        var row = new UniformGrid { Columns = Blocks.All.Length };
        foreach (var block in Blocks.All)
        {
            var view = new BlockView(block);
            blocks.Add(view);
            var popOut = VolumeControl.IconButton("");
            popOut.ToolTip = T("Pop out");
            AutomationProperties.SetName(popOut, F("Pop out {0}", block.Title));
            popOut.Click += (_, _) => applet.MakeWidget(block);
            var content = new StackPanel { Children = { Ui.HeaderLine(block.Title, popOut), view } };
            if (block == Blocks.Vehicle) content.Children.Add(carName);
            var card = Ui.Card(content);
            card.Margin = new Thickness(0, 0, block == Blocks.All[^1] ? 0 : 12, 12);
            Widget.Offer(card, () => applet.MakeWidget(block));
            row.Children.Add(card);
        }
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
        Columns(best, (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null), (T("PI"), "Pi", null),
            (T("Best"), "BestText", "Best"), (T("Laps"), "Laps", null), (T("Last driven"), "LastText", "Last"));
        Columns(laps, (T("When"), "WhenText", "When"), (T("Track"), "Track", null), (T("Car"), "Car", null), (T("Class"), "Class", null),
            (T("PI"), "Pi", null), (T("Lap"), "Lap", null), (T("Time"), "TimeText", "Time"), (T("Gap"), "GapText", "Gap"));

        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        page.Children.Add(Ui.Card(nameTrack));
        page.Children.Add(row);
        page.Children.Add(Widget.ListCard());
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Best laps")), filters, best } }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderLine(T("Laps"), lapCount), laps } }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Tracks")), tracks } }));
        ((FrameworkElement)nameTrack.Parent).Visibility = Visibility.Collapsed;

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(Ui.Scroll(page));
        Content = dock;
        applet.Store.Changed += RenderStats;
    }

    public void ShowOnOff() => Ui.ShowOnOff(onOff, settings.Enabled);

    public void Render()
    {
        ShowOnOff();
        RenderStats();
        if (settings.Tracks.LastOrDefault(t => string.IsNullOrWhiteSpace(t.Name)) is { } unnamed) AskName(unnamed, notify: false);
    }

    public void Tick(Reading now)
    {
        var (text, receiving) = applet.Status();
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, receiving ? "SuccessBrush" : "TextSecondaryBrush");
        if (!IsVisible) return;
        foreach (var b in blocks) b.Update(now);
        int c = now.Frame is { HasRace: true, CarOrdinal: > 0 } f ? f.CarOrdinal : -1;
        if (c != car)
        {
            car = c;
            carName.Visibility = c > 0 ? Visibility.Visible : Visibility.Collapsed;
            carBox.Text = c > 0 && settings.Cars.TryGetValue($"FH:{c}", out var n) ? n : "";
        }
    }

    private void SaveCar()
    {
        if (car <= 0) return;
        string name = carBox.Text.Trim();
        if (name.Length > 0) settings.Cars[$"FH:{car}"] = name; else settings.Cars.Remove($"FH:{car}");
        settings.Save();
        Logging.Log.Here.Info($"Analysis: car {car} named \"{name}\"");
        RenderStats();
    }

    // ---------------------------------------------------------------- a new track

    /// <summary>A track was found that has no name: asked for one at the top of the tab (and with a notification, since
    /// the game's likely in front).</summary>
    public void AskName(Track t, bool notify = true)
    {
        asking = t;
        nameTrack.Children.Clear();
        nameTrack.Children.Add(Ui.Header(T("New track")));
        nameTrack.Children.Add(Ui.Secondary(F("A race started at {0:0}, {1:0} with a lap of {2}. Name it:", t.X, t.Z, Distance(t.Length))));
        var box = new TextBox { Width = 260, Margin = new Thickness(0, 8, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, T("Track name"));
        void Save()
        {
            if (box.Text.Trim().Length == 0) return;
            Rename(t, box.Text);
            Done();
        }
        void Done()
        {
            asking = null;
            ((FrameworkElement)nameTrack.Parent).Visibility = Visibility.Collapsed;
            if (settings.Tracks.LastOrDefault(x => string.IsNullOrWhiteSpace(x.Name) && x != t) is { } next) AskName(next, notify: false);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        var save = Ui.Button(T("Save"), Save, accent: true);
        save.Margin = new Thickness(0, 8, 6, 0);
        var later = Ui.Button(T("Later"), Done, tip: T("Name it in Tracks below"));
        later.Margin = new Thickness(0, 8, 6, 0);
        nameTrack.Children.Add(new WrapPanel { Children = { box, save, later } });
        ((FrameworkElement)nameTrack.Parent).Visibility = Visibility.Visible;
        RenderStats();
        if (notify) DaisysApp.Shell.Notifications.Show(T("Analysis"), T("A new track: name it on SimEffects → Analysis"));
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
        Refill(carFilter, applet.Store.All.Select(l => l.Car).Distinct().Select(c => (c.ToString(CultureInfo.InvariantCulture), settings.CarName("FH", c))).OrderBy(x => x.Item2));
        Refill(classFilter, applet.Store.All.Select(l => l.Class).Distinct().OrderBy(c => c).Select(c => (c.ToString(CultureInfo.InvariantCulture), Blocks.ClassName(c))));
        RenderLaps();
        RenderTracks();
    }

    private void RenderLaps()
    {
        string? track = Picked(trackFilter), carId = Picked(carFilter), cls = Picked(classFilter);
        var picked = applet.Store.All.Where(l =>
            (track == null || l.Track == track) &&
            (carId == null || l.Car.ToString(CultureInfo.InvariantCulture) == carId) &&
            (cls == null || l.Class.ToString(CultureInfo.InvariantCulture) == cls)).ToList();
        // best by track, car and class
        var groups = picked.GroupBy(l => (l.Track, l.Car, l.Class)).Select(g =>
        {
            var b = g.MinBy(l => l.Time)!;
            var last = g.Max(l => l.At);
            return new BestRow(settings.TrackName(g.Key.Track), settings.CarName("FH", g.Key.Car), Blocks.ClassName(g.Key.Class), b.Pi,
                b.Time, Blocks.LapTime(b.Time), g.Count(), last, last.ToString("g", CultureInfo.CurrentCulture));
        }).OrderBy(r => r.Track).ThenBy(r => r.Best).ToList();
        best.ItemsSource = groups;
        best.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // every lap, newest first, with its gap to the best of its track, car and class
        var bests = picked.GroupBy(l => (l.Track, l.Car, l.Class)).ToDictionary(g => g.Key, g => g.Min(l => l.Time));
        var rows = picked.OrderByDescending(l => l.At).Take(1000).Select(l =>
        {
            float gap = l.Time - bests[(l.Track, l.Car, l.Class)];
            return new LapRow(l.At, l.At.ToString("g", CultureInfo.CurrentCulture), settings.TrackName(l.Track), settings.CarName("FH", l.Car),
                Blocks.ClassName(l.Class), l.Pi, l.Lap, l.Time, Blocks.LapTime(l.Time), gap, gap > 0.0005f ? $"+{gap:0.000}" : T("Best"));
        }).ToList();
        laps.ItemsSource = rows;
        laps.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        lapCount.Text = picked.Count == 0 ? T("No laps yet") : P(picked.Count, "{0} lap", "{0} laps");
    }

    private void RenderTracks()
    {
        tracks.Children.Clear();
        if (settings.Tracks.Count == 0) tracks.Children.Add(Ui.Secondary(T("None yet: a track is added when the first lap of a race on it ends.")));
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
                Text = $"{P(count, "{0} lap", "{0} laps")} · {F("a lap {0}", Distance(t.Length))} · {F("found {0:d}", t.Found)}",
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            });
            tracks.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- pieces

    private sealed record BestRow(string Track, string Car, string Class, int Pi, float Best, string BestText, int Laps, DateTime Last, string LastText);

    private sealed record LapRow(DateTime When, string WhenText, string Track, string Car, string Class, int Pi, int Lap, float Time, string TimeText, float Gap, string GapText);

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
