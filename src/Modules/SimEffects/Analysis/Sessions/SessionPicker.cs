using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.13.0) A small window to find a session for Compare's Session 1 or 2, from a click on those words: the
/// sessions the slot can have in a table, sorted by a column's header (newest first at first), Game, Kind and Video
/// filtered from a right-click on theirs, searched by track or car. Picking a row puts it in the slot straight away,
/// the window staying open. One window per slot.
/// </summary>
internal sealed class SessionPicker
{
    private static readonly Dictionary<int, Window> open = new();

    private readonly SessionsView view;
    private readonly int slot;
    private readonly Window window;
    private readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
    };
    private readonly TextBox search = new() { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Track or car") };
    private readonly Button pick = new() { Content = T("Select & Close"), MinWidth = 110, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly HeaderFilter games = new(T("Game")), kinds = new(T("Kind")), videos = new(T("Video"));
    private List<Row> rows = new();
    private ICollectionView? shown;
    private bool filling;

    private sealed class Row
    {
        public required SessionsView.Item Item { get; init; }
        public DateTime Date => Item.Started;
        public string DateText => SessionsView.When(Item.Started);
        public string Game => Item.Imported ? "" : Item.Game;
        public string Track => Item.TrackName;
        public string Car => Item.CarName;
        public string Kind => Item.Imported ? F("Imported ({0})", Item.Driver) : Item.Incomplete ? T("Incomplete") : T("Race");
        public int Laps => Item.Laps;
        public string LapsText => Item.Imported || Item.Laps == 0 ? "" : Item.Laps.ToString();
        public float Best => Item.Best > 0 ? Item.Best : float.MaxValue;
        public string BestText => Item.Best > 0 ? Blocks.LapTime(Item.Best) : "";
        public bool HasVideo => Item.HasVideo;
        public string VideoGlyph => Item.HasVideo ? "" : "";
        public string VideoText => Item.HasVideo ? T("With video") : T("No video");
    }

    public static void Show(FrameworkElement owner, SessionsView view, int slot)
    {
        if (open.TryGetValue(slot, out var there))
        {
            if (there.WindowState == WindowState.Minimized) there.WindowState = WindowState.Normal;
            there.Activate();
            return;
        }
        var w = new SessionPicker(owner, view, slot).window;
        open[slot] = w;
        w.Closed += (_, _) => open.Remove(slot);
        w.Show();
    }

    private SessionPicker(FrameworkElement owner, SessionsView view, int slot)
    {
        this.view = view;
        this.slot = slot;
        window = Ui.Dialog(slot == 1 ? T("Session 1") : T("Session 2"), owner, 860, 480);
        window.MinWidth = 560;
        window.MinHeight = 300;
        window.ResizeMode = ResizeMode.CanResize;

        var body = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };
        var find = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = T("Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        find.Children.Add(label);
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        search.TextChanged += (_, _) => shown?.Refresh();
        find.Children.Add(search);
        DockPanel.SetDock(find, Dock.Top);
        body.Children.Add(find);

        pick.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        pick.IsEnabled = false;
        pick.Click += (_, _) => window.Close();
        var close = new Button { Content = T("Close"), Padding = new Thickness(14, 4, 14, 4) };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { pick, close } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);

        BuildGrid();
        body.Children.Add(grid);
        window.Content = body;

        if (slot == 1) view.Listed += List; else view.OthersListed += List;
        window.Closed += (_, _) => { if (slot == 1) view.Listed -= List; else view.OthersListed -= List; };
        List();
        window.Loaded += (_, _) =>
        {
            if (grid.SelectedItem != null) grid.ScrollIntoView(grid.SelectedItem);
            search.Focus();
        };
    }

    private void BuildGrid()
    {
        grid.SetResourceReference(FrameworkElement.StyleProperty, typeof(DataGrid));
        System.Windows.Automation.AutomationProperties.SetName(grid, T("Sessions"));
        void Column(object header, string path, string? sort = null, double star = 0, bool right = false, FontFamily? font = null)
        {
            var cell = new Style(typeof(TextBlock));
            cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            if (right) cell.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            var column = new DataGridTextColumn
            {
                Header = header, Binding = new Binding(path), SortMemberPath = sort ?? path, ElementStyle = cell,
                Width = star > 0 ? new DataGridLength(star, DataGridLengthUnitType.Star) : DataGridLength.Auto, MinWidth = star > 0 ? 120 : 50,
            };
            if (font != null) column.FontFamily = font;
            grid.Columns.Add(column);
        }
        Column(T("Date"), nameof(Row.DateText), nameof(Row.Date));
        Column(games.Header(), nameof(Row.Game));
        Column(T("Track"), nameof(Row.Track), star: 2);
        Column(T("Car"), nameof(Row.Car), star: 2);
        Column(kinds.Header(), nameof(Row.Kind));
        Column(T("Laps"), nameof(Row.LapsText), nameof(Row.Laps), right: true);
        Column(T("Best"), nameof(Row.BestText), nameof(Row.Best), right: true);
        Column(videos.Header(), nameof(Row.VideoGlyph), nameof(Row.HasVideo), font: new FontFamily("Segoe MDL2 Assets"));
        foreach (var f in new[] { games, kinds, videos }) f.Changed += () => shown?.Refresh();
        HeaderFilter.Attach(grid, Values);
        grid.SelectionChanged += (_, _) =>
        {
            pick.IsEnabled = grid.SelectedItem is Row;
            if (!filling && grid.SelectedItem is Row r) view.Choose(slot, r.Item);
        };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) window.Close();
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && grid.SelectedItem is Row) { window.Close(); e.Handled = true; }
        };
    }

    private IEnumerable<string> Values(HeaderFilter f) =>
        rows.Where(r => Passes(r, f)).Select(r => f == games ? r.Game : f == kinds ? r.Kind : r.VideoText).Where(v => v.Length > 0).Distinct();

    /// <summary>The sessions the slot can have, listed again; the one it has picked, the sort kept.</summary>
    private void List()
    {
        var sorts = shown?.SortDescriptions.ToList() is { Count: > 0 } was ? was : new() { new SortDescription(nameof(Row.Date), ListSortDirection.Descending) };
        rows = view.ChoicesFor(slot).Select(i => new Row { Item = i }).ToList();
        filling = true;
        shown = CollectionViewSource.GetDefaultView(rows);
        shown.Filter = o => o is Row r && Passes(r, null);
        grid.ItemsSource = shown;
        foreach (var s in sorts) shown.SortDescriptions.Add(s);
        foreach (var c in grid.Columns)
            c.SortDirection = sorts.FirstOrDefault(s => s.PropertyName == c.SortMemberPath) is { PropertyName: not null } s ? s.Direction : null;
        grid.SelectedItem = view.ChosenFor(slot) is { } now ? rows.FirstOrDefault(r => r.Item.Key == now.Key) : null;
        filling = false;
    }

    private bool Passes(Row r, HeaderFilter? except)
    {
        string want = search.Text.Trim();
        if (want.Length > 0 && r.Track.IndexOf(want, StringComparison.CurrentCultureIgnoreCase) < 0 && r.Car.IndexOf(want, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        return (except == games || games.Keeps(r.Game)) && (except == kinds || kinds.Keeps(r.Kind)) && (except == videos || videos.Keeps(r.VideoText));
    }
}
