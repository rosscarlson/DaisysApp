using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.12.0) The Session Manager, from Session Analysis' wrench: every session listed (the races' logs, then the
/// imported ones) with its game, track, car, laps, best and what it takes on disk (its log and its race video), the
/// totals above; sorted by a column's header (newest first at first), filtered by the boxes in the headers. Load
/// analyses the session picked in the window behind, this one staying open; Show in Folder opens Explorer with its log
/// (or video) picked. One window.
/// </summary>
internal sealed class SessionManager
{
    private static Window? open;

    private readonly SessionsView view;
    private readonly Window window;
    private readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, CanUserSortColumns = true,
    };
    private readonly WrapPanel totals = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly Dictionary<string, TextBox> filters = new();
    private readonly ComboBox videoFilter = new() { Margin = new Thickness(0, 4, 0, 0), MinWidth = 90 };
    private readonly Button load = new() { Content = T("Load"), MinWidth = 90, Padding = new Thickness(14, 4, 14, 4) };
    private readonly Button showLog = new() { Content = T("Show in Folder"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button showVideo = new() { Content = T("Show Video in Folder"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button delete = new() { Content = T("Delete"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly List<Row> rows = new();
    private ICollectionView? shown;

    /// <summary>A session as listed: what it takes on disk read when listed.</summary>
    private sealed class Row
    {
        public required SessionsView.Item Item { get; init; }
        public DateTime Date => Item.Started;
        public string DateText => Item.Started.ToString("g");
        public string Game => Item.Imported ? "" : Item.Game;
        public string Track => Item.TrackName;
        public string Car => Item.CarName;
        public string Kind => Item.Imported ? F("Imported ({0})", Item.Driver) : Item.Incomplete ? T("Incomplete") : T("Race");
        public int Laps => Item.Laps;
        public string LapsText => Item.Imported || Item.Laps == 0 ? "" : Item.Laps.ToString();
        public float Best => Item.Best > 0 ? Item.Best : float.MaxValue;
        public string BestText => Item.Best > 0 ? Blocks.LapTime(Item.Best) : "";
        public long Data { get; init; }
        public string DataText => Sizes.Bytes(Data);
        /// <summary>The race video's file (linked), or null.</summary>
        public string? VideoFile { get; init; }
        /// <summary>Its size: 0 none, -1 linked but gone.</summary>
        public long Video { get; init; }
        public string VideoText => Video > 0 ? Sizes.Bytes(Video) : Video < 0 ? T("Missing") : "—";
        public long Total => Data + Math.Max(0, Video);
        public string TotalText => Sizes.Bytes(Total);
        public string File => Item.File.Name;
    }

    public static void Show(FrameworkElement owner, SessionsView view)
    {
        if (open is { IsLoaded: true })
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        open = new SessionManager(owner, view).window;
        open.Closed += (_, _) => open = null;
        open.Show();
    }

    private SessionManager(FrameworkElement owner, SessionsView view)
    {
        this.view = view;
        window = Ui.Dialog(T("Session Manager"), owner, 1180, 640);
        window.MinWidth = 700;
        window.MinHeight = 360;
        window.ResizeMode = ResizeMode.CanResize;

        var body = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };
        var head = new StackPanel();
        head.Children.Add(Ui.Header(T("Session Manager")));
        head.Children.Add(totals);
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        load.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        load.ToolTip = T("Analyse it in Session Analysis");
        load.Click += (_, _) => Load();
        showLog.ToolTip = T("Open its folder with its log picked");
        showLog.Click += (_, _) => { if (Picked is { } r) Ui.ShowInExplorer(r.Item.File.FullName); };
        showVideo.ToolTip = T("Open its folder with its race video picked");
        showVideo.Click += (_, _) => { if (Picked is { VideoFile: { } v }) Ui.ShowInExplorer(v); };
        System.Windows.Automation.AutomationProperties.SetName(delete, T("Delete Session"));
        ConfirmButton.Arm(delete, () => { if (Picked is { } r) view.Delete(r.Item); });
        delete.ToolTip = T("Delete this session and its video");
        var refresh = new Button { Content = T("Refresh"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) => view.Refresh();
        var close = new Button { Content = T("Close"), Padding = new Thickness(14, 4, 14, 4) };
        close.Click += (_, _) => window.Close();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { refresh, close } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { load, showLog, showVideo, delete } });
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);

        BuildGrid();
        body.Children.Add(grid);
        window.Content = body;

        view.Listed += List;
        window.Closed += (_, _) => view.Listed -= List;
        List();
        if (view.Analysed is { } now && rows.FirstOrDefault(r => r.Item.Key == now.Key) is { } row)
        {
            grid.SelectedItem = row;
            window.Loaded += (_, _) => grid.ScrollIntoView(row);
        }
        Enable();
    }

    private Row? Picked => grid.SelectedItem as Row;

    private void BuildGrid()
    {
        grid.SetResourceReference(FrameworkElement.StyleProperty, typeof(DataGrid));
        // (a header's content as wide as its column: its filter box fills it)
        var headerStyle = new Style(typeof(DataGridColumnHeader), Application.Current.TryFindResource("EventGridColumnHeader") as Style);
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        grid.ColumnHeaderStyle = headerStyle;

        Column(T("Date"), nameof(Row.DateText), nameof(Row.Date), filter: true);
        Column(T("Game"), nameof(Row.Game), filter: true);
        Column(T("Track"), nameof(Row.Track), filter: true, star: 2);
        Column(T("Car"), nameof(Row.Car), filter: true, star: 2);
        Column(T("Kind"), nameof(Row.Kind), filter: true);
        Column(T("Laps"), nameof(Row.LapsText), nameof(Row.Laps), right: true);
        Column(T("Best"), nameof(Row.BestText), nameof(Row.Best), right: true);
        Column(T("Data"), nameof(Row.DataText), nameof(Row.Data), right: true);
        videoFilter.ItemsSource = new[] { T("All"), T("With video"), T("No video") };
        videoFilter.SelectedIndex = 0;
        videoFilter.ToolTip = T("Filter");
        System.Windows.Automation.AutomationProperties.SetName(videoFilter, T("Video filter"));
        videoFilter.SelectionChanged += (_, _) => { shown?.Refresh(); Totals(); };
        Column(T("Video"), nameof(Row.VideoText), nameof(Row.Video), right: true, below: videoFilter);
        Column(T("Total"), nameof(Row.TotalText), nameof(Row.Total), right: true);
        Column(T("File"), nameof(Row.File), filter: true, star: 1.5);

        // (a click in a header's filter box doesn't sort by that column)
        grid.Sorting += (_, e) =>
        {
            if (Keyboard.FocusedElement is Control c && (c is TextBox || c is ComboBox) && c.IsMouseOver) e.Handled = true;
        };
        grid.SelectionChanged += (_, _) => Enable();
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) Load();
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Picked != null) { Load(); e.Handled = true; }
        };
    }

    private void Column(string header, string path, string? sort = null, bool filter = false, double star = 0, bool right = false,
        ListSortDirection? sortFirst = null, FrameworkElement? below = null)
    {
        var title = new TextBlock { Text = header, FontWeight = FontWeights.SemiBold };
        var stack = new StackPanel { Children = { title } };
        if (filter)
        {
            var box = new TextBox { Margin = new Thickness(0, 4, 0, 0), MinWidth = 50, ToolTip = T("Filter") };
            System.Windows.Automation.AutomationProperties.SetName(box, F("{0} filter", header));
            box.TextChanged += (_, _) => { shown?.Refresh(); Totals(); };
            filters[path] = box;
            stack.Children.Add(box);
        }
        else if (below != null) stack.Children.Add(below);
        // (the titles in a line: a hidden box's room under those with none)
        else stack.Children.Add(new TextBox { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Hidden, Focusable = false, IsTabStop = false });
        var cell = new Style(typeof(TextBlock));
        cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 6, 2)));
        if (right) cell.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        var column = new DataGridTextColumn
        {
            Header = stack, Binding = new Binding(path), SortMemberPath = sort ?? path, ElementStyle = cell,
            Width = star > 0 ? new DataGridLength(star, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            MinWidth = star > 0 ? 120 : 64,
        };
        grid.Columns.Add(column);
    }

    /// <summary>The sessions listed again (on open, and whenever Session Analysis lists them), the one picked kept.</summary>
    private void List()
    {
        string? keep = Picked?.Item.Key;
        rows.Clear();
        foreach (var i in view.Items)
        {
            i.File.Refresh();
            long data = i.File.Exists ? i.File.Length : 0;
            string? video = i.Imported ? null : RaceVideos.Of(i.Key)?.File;
            long size = 0;
            if (!string.IsNullOrEmpty(video))
            {
                var v = new FileInfo(video);
                size = v.Exists ? v.Length : -1;
            }
            rows.Add(new Row { Item = i, Data = data, VideoFile = size > 0 ? video : null, Video = size });
        }
        // (the sort kept: a DataGrid given new items drops it)
        var sorts = shown?.SortDescriptions.ToList() is { Count: > 0 } was ? was : new() { new SortDescription(nameof(Row.Date), ListSortDirection.Descending) };
        shown = CollectionViewSource.GetDefaultView(rows.ToList());
        shown.Filter = o => o is Row r && Passes(r);
        grid.ItemsSource = shown;
        foreach (var s in sorts) shown.SortDescriptions.Add(s);
        foreach (var c in grid.Columns)
            c.SortDirection = sorts.FirstOrDefault(s => s.PropertyName == c.SortMemberPath) is { PropertyName: not null } s ? s.Direction : null;
        if (keep != null) grid.SelectedItem = rows.FirstOrDefault(r => r.Item.Key == keep);
        Totals();
        Enable();
    }

    private bool Passes(Row r)
    {
        foreach (var (path, box) in filters)
        {
            string want = box.Text.Trim();
            if (want.Length == 0) continue;
            string have = path switch
            {
                nameof(Row.DateText) => r.DateText,
                nameof(Row.Game) => r.Game,
                nameof(Row.Track) => r.Track,
                nameof(Row.Car) => r.Car,
                nameof(Row.Kind) => r.Kind,
                nameof(Row.File) => r.File,
                _ => "",
            };
            if (have.IndexOf(want, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        }
        return videoFilter.SelectedIndex switch
        {
            1 => r.Video > 0,
            2 => r.Video <= 0,
            _ => true,
        };
    }

    /// <summary>What the sessions take on disk: all of them, and those the filters show.</summary>
    private void Totals()
    {
        totals.Children.Clear();
        void Tile(string name, string value, string? tip = null)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 26, 0), ToolTip = tip };
            var label = new TextBlock { Text = name, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            s.Children.Add(label);
            s.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold });
            totals.Children.Add(s);
        }
        long data = rows.Sum(r => r.Data), video = rows.Sum(r => Math.Max(0, r.Video));
        int videos = rows.Count(r => r.Video > 0);
        Tile(T("Sessions"), Sizes.Count(rows.Count));
        Tile(T("Data"), Sizes.Bytes(data), T("The sessions' logs"));
        Tile(T("Videos"), videos == 0 ? "—" : F("{0} · {1}", Sizes.Bytes(video), Sizes.Count(videos)), T("The race videos: their size, how many"));
        Tile(T("Total"), Sizes.Bytes(data + video));
        var filtered = shown?.Cast<Row>().ToList();
        if (filtered != null && filtered.Count != rows.Count)
            Tile(T("Shown"), F("{0} · {1}", Sizes.Bytes(filtered.Sum(r => r.Total)), Sizes.Count(filtered.Count)), T("The sessions the filters show: their size, how many"));
        // (the videos linked to no session listed: left by a log deleted outside the app)
        var listed = new HashSet<string>(rows.Select(r => r.Item.Key), StringComparer.OrdinalIgnoreCase);
        long loose = 0;
        int looseCount = 0;
        foreach (var (key, link) in RaceVideos.All.Videos)
        {
            if (listed.Contains(key) || string.IsNullOrEmpty(link.File)) continue;
            try { var f = new FileInfo(link.File); if (f.Exists) { loose += f.Length; looseCount++; } } catch { }
        }
        if (looseCount > 0) Tile(T("Videos without a session"), F("{0} · {1}", Sizes.Bytes(loose), Sizes.Count(looseCount)));
        string? drive = rows.Select(r => r.VideoFile).FirstOrDefault(v => v != null) ?? view.Items.FirstOrDefault()?.File.FullName;
        try
        {
            if (drive != null && Path.GetPathRoot(drive) is { Length: > 0 } root)
            {
                var d = new DriveInfo(root);
                Tile(F("Free on {0}", root.TrimEnd('\\')), Sizes.Bytes(d.AvailableFreeSpace), T("Where the race videos are"));
            }
        }
        catch { }
    }

    private void Enable()
    {
        var r = Picked;
        load.IsEnabled = showLog.IsEnabled = delete.IsEnabled = r != null;
        showVideo.IsEnabled = r?.VideoFile != null;
    }

    private void Load()
    {
        if (Picked is not { } r) return;
        view.Load(r.Item);
    }
}
