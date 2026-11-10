using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.12.0) The Session Manager, from the Session card's wrench: every session listed (the races' logs, then the
/// imported ones) with its game, track, car, laps, best and what it takes on disk (its log and its race video), the
/// totals above; sorted by a column's header (newest first at first), filtered from a right-click on it (since 4.13.1;
/// boxes in the headers before). Load analyses the session picked in the window behind, this one staying open; Show in
/// Folder opens Explorer with its log (or video) picked. Since 4.13.1 several can be picked with Ctrl or Shift: Delete
/// takes them all, the others want one. Since 4.13.2 the buttons are icons, their names their tooltips, and the totals
/// tiles at the right. One window.
/// </summary>
internal sealed class SessionManager
{
    private static Window? open;

    private readonly SessionsView view;
    private readonly Window window;
    private readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Extended,
        HeadersVisibility = DataGridHeadersVisibility.Column, CanUserSortColumns = true,
    };
    private readonly WrapPanel totals = new() { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly HeaderFilter days = new(T("Date"), (a, b) => DateTime.Parse(b, CultureInfo.CurrentCulture).CompareTo(DateTime.Parse(a, CultureInfo.CurrentCulture))),
        games = new(T("Game")), tracks = new(T("Track")), cars = new(T("Car")), kinds = new(T("Kind")), videos = new(T("Video"));
    private readonly Button load = Tool("", T("Load"), new Thickness(0)), showLog = Tool("", T("Show in Folder"), new Thickness(8, 0, 0, 0)),
        showVideo = Tool("", T("Show Video in Folder"), new Thickness(8, 0, 0, 0)), delete = Tool("", T("Delete"), new Thickness(8, 0, 0, 0));
    /// <summary>Puts Delete's Confirm? back: what it's for changed.</summary>
    private Action disarm = () => { };
    private readonly List<Row> rows = new();
    private ICollectionView? shown;

    /// <summary>A session as listed: what it takes on disk read when listed.</summary>
    private sealed class Row
    {
        public required SessionsView.Item Item { get; init; }
        public DateTime Date => Item.Started;
        public string DateText => Item.Started.ToString("g");
        public string Day => Item.Started.ToString("d", CultureInfo.CurrentCulture);
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
        public string VideoKind => Video > 0 ? T("With video") : T("No video");
        public long Total => Data + Math.Max(0, Video);
        public string TotalText => Sizes.Bytes(Total);
        public string File => Item.File.Name;
    }

    /// <summary>(since 4.13.2) A button that's only an icon, its name as its tooltip (shown when it's disabled too).</summary>
    private static Button Tool(string glyph, string name, Thickness margin)
    {
        var b = Ui.IconButton(glyph, "", name);
        b.Padding = new Thickness(12, 6, 12, 6);
        b.Margin = margin;
        ToolTipService.SetShowOnDisabled(b, true);
        return b;
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
        DockPanel.SetDock(totals, Dock.Top);
        body.Children.Add(totals);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        load.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        load.Click += (_, _) => Load();
        showLog.Click += (_, _) => { if (Picked is { } r) Ui.ShowInExplorer(r.Item.File.FullName); };
        showVideo.Click += (_, _) => { if (Picked is { VideoFile: { } v }) Ui.ShowInExplorer(v); };
        System.Windows.Automation.AutomationProperties.SetName(delete, T("Delete Session"));
        disarm = ConfirmButton.ArmIcon(delete, () =>
        {
            var picked = grid.SelectedItems.OfType<Row>().Select(r => r.Item).ToList();
            if (picked.Count > 0) view.Delete(picked);
        });
        // (since 4.13.1) where the race videos' options are; this window would cover them
        var recording = Tool("", T("Recording Options"), new Thickness(0, 0, 8, 0));
        recording.Click += (_, _) =>
        {
            if (DaisysApp.Shell.AppNavigation.OpenTab("Video/Recording")) window.Close();
            else DaisysApp.Shell.Notifications.Show(T("Analysis"), T("Needs Video → Recording switched on"));
        };
        var refresh = Tool("", T("Refresh"), new Thickness(0, 0, 8, 0));
        refresh.Click += (_, _) => view.Refresh();
        var close = Tool("", T("Close"), new Thickness(0));
        close.Click += (_, _) => window.Close();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { recording, refresh, close } };
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

    /// <summary>The session picked, when it's only one.</summary>
    private Row? Picked => grid.SelectedItems.Count == 1 ? grid.SelectedItem as Row : null;

    private void BuildGrid()
    {
        grid.SetResourceReference(FrameworkElement.StyleProperty, typeof(DataGrid));
        System.Windows.Automation.AutomationProperties.SetName(grid, T("Sessions"));
        Column(days.Header(), nameof(Row.DateText), nameof(Row.Date));
        Column(games.Header(), nameof(Row.Game));
        Column(tracks.Header(), nameof(Row.Track), star: 2);
        Column(cars.Header(), nameof(Row.Car), star: 2);
        Column(kinds.Header(), nameof(Row.Kind));
        Column(T("Laps"), nameof(Row.LapsText), nameof(Row.Laps), right: true);
        Column(T("Best"), nameof(Row.BestText), nameof(Row.Best), right: true);
        Column(T("Data"), nameof(Row.DataText), nameof(Row.Data), right: true);
        Column(videos.Header(), nameof(Row.VideoText), nameof(Row.Video), right: true);
        Column(T("Total"), nameof(Row.TotalText), nameof(Row.Total), right: true);
        Column(T("File"), nameof(Row.File), star: 1.5);
        foreach (var f in new[] { days, games, tracks, cars, kinds, videos }) f.Changed += () => { shown?.Refresh(); Totals(); };
        HeaderFilter.Attach(grid, Values);
        // (since 4.13.2) how many sessions a track has, when it's more than one
        tracks.Label = v => rows.Count(r => r.Track == v && Passes(r, tracks)) is > 1 and var n ? $"{v} ({n})" : v;

        grid.SelectionChanged += (_, _) => { disarm(); Enable(); };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) Load();
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Picked != null) { Load(); e.Handled = true; }
        };
    }

    private void Column(object header, string path, string? sort = null, double star = 0, bool right = false)
    {
        var cell = new Style(typeof(TextBlock));
        cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 6, 2)));
        if (right) cell.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        var column = new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path), SortMemberPath = sort ?? path, ElementStyle = cell,
            Width = star > 0 ? new DataGridLength(star, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            MinWidth = star > 0 ? 120 : 64,
        };
        grid.Columns.Add(column);
    }

    /// <summary>The sessions listed again (on open, and whenever the Session card lists them), those picked kept.</summary>
    private void List()
    {
        var keep = grid.SelectedItems.OfType<Row>().Select(r => r.Item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        shown.Filter = o => o is Row r && Passes(r, null);
        grid.ItemsSource = shown;
        foreach (var s in sorts) shown.SortDescriptions.Add(s);
        foreach (var c in grid.Columns)
            c.SortDirection = sorts.FirstOrDefault(s => s.PropertyName == c.SortMemberPath) is { PropertyName: not null } s ? s.Direction : null;
        foreach (var r in shown.Cast<Row>().Where(r => keep.Contains(r.Item.Key))) grid.SelectedItems.Add(r);
        Totals();
        Enable();
    }

    /// <summary>Kept by the columns' filters (but <paramref name="except"/>'s, for its own drop-down).</summary>
    private bool Passes(Row r, HeaderFilter? except) =>
        (except == days || days.Keeps(r.Day)) && (except == games || games.Keeps(r.Game)) && (except == tracks || tracks.Keeps(r.Track))
        && (except == cars || cars.Keeps(r.Car)) && (except == kinds || kinds.Keeps(r.Kind)) && (except == videos || videos.Keeps(r.VideoKind));

    /// <summary>A column's values for its filter's drop-down: those the other columns' filters leave.</summary>
    private IEnumerable<string> Values(HeaderFilter f) =>
        rows.Where(r => Passes(r, f)).Select(r => f == days ? r.Day : f == games ? r.Game : f == tracks ? r.Track : f == cars ? r.Car : f == kinds ? r.Kind : r.VideoKind)
            .Where(v => v.Length > 0).Distinct().ToList();

    /// <summary>A tile of the totals (since 4.13.2 as Racing Statistics'): its number, its name, a line and, for a share, a
    /// bar filled that far. The line and the bar keep their room when there's none, so the tiles are level.</summary>
    private void Tile(string name, string value, string sub = "", double? share = null, Color? colour = null, string? tip = null)
    {
        var big = new TextBlock { Text = value, FontFamily = BlockView.RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = 22, FontWeight = FontWeights.SemiBold };
        var label = new TextBlock { Text = name.ToUpperInvariant(), FontFamily = BlockView.RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var line = new TextBlock { Text = sub.Length > 0 ? sub : " ", FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 5, 0, 0), Visibility = share == null ? Visibility.Hidden : Visibility.Visible };
        double f = Math.Clamp(share ?? 0, 0, 1);
        var c = colour ?? Color.FromRgb(0x4D, 0xA3, 0xFF);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, f), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - f), GridUnitType.Star) });
        var rest = new Border { Background = Ui.Brush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(rest, 1);
        bar.Children.Add(new Border { Background = Ui.Brush(c), CornerRadius = new CornerRadius(2) });
        bar.Children.Add(rest);
        var tile = new Border
        {
            Child = new StackPanel { Children = { big, label, line, bar } }, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 12, 8),
            Margin = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(1), MinWidth = 104, ToolTip = tip,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        System.Windows.Automation.AutomationProperties.SetName(tile, name + " " + value + " " + sub);
        totals.Children.Add(tile);
    }

    /// <summary>What the sessions take on disk: all of them, and those the filters show.</summary>
    private void Totals()
    {
        totals.Children.Clear();
        long data = rows.Sum(r => r.Data), video = rows.Sum(r => Math.Max(0, r.Video)), all = data + video;
        int videos = rows.Count(r => r.Video > 0);
        Tile(T("Sessions"), Sizes.Count(rows.Count), videos > 0 ? F("{0} with video", Sizes.Count(videos)) : "");
        Tile(T("Data"), Sizes.Bytes(data), Percent(data, all) + " " + T("of total"), all > 0 ? (double)data / all : 0, tip: T("The sessions' logs"));
        Tile(T("Videos"), videos == 0 ? "—" : Sizes.Bytes(video), P(videos, "{0} video", "{0} videos"), all > 0 ? (double)video / all : 0, Color.FromRgb(0xB0, 0x7C, 0xFF), T("The race videos"));
        Tile(T("Total"), Sizes.Bytes(all), rows.Count > 0 ? F("{0} each", Sizes.Bytes(all / rows.Count)) : "");
        var filtered = shown?.Cast<Row>().ToList();
        if (filtered != null && filtered.Count != rows.Count)
            Tile(T("Shown"), Sizes.Bytes(filtered.Sum(r => r.Total)), P(filtered.Count, "{0} session", "{0} sessions"), tip: T("The sessions the filters show"));
        // (the videos linked to no session listed: left by a log deleted outside the app)
        var listed = new HashSet<string>(rows.Select(r => r.Item.Key), StringComparer.OrdinalIgnoreCase);
        long loose = 0;
        int looseCount = 0;
        foreach (var (key, link) in RaceVideos.All.Videos)
        {
            if (listed.Contains(key) || string.IsNullOrEmpty(link.File)) continue;
            try { var f = new FileInfo(link.File); if (f.Exists) { loose += f.Length; looseCount++; } } catch { }
        }
        if (looseCount > 0) Tile(T("No session"), Sizes.Bytes(loose), P(looseCount, "{0} video", "{0} videos"), tip: T("Videos without a session"));
        string? drive = rows.Select(r => r.VideoFile).FirstOrDefault(v => v != null) ?? view.Items.FirstOrDefault()?.File.FullName;
        try
        {
            if (drive != null && Path.GetPathRoot(drive) is { Length: > 0 } root)
            {
                var d = new DriveInfo(root);
                // (the bar's how full it is; amber and red as it runs out)
                double free = (double)d.AvailableFreeSpace / d.TotalSize;
                var level = free < 0.1 ? Color.FromRgb(0xF0, 0x3E, 0x3E) : free < 0.2 ? Color.FromRgb(0xFF, 0xC8, 0x3D) : Color.FromRgb(0x3D, 0xDC, 0x84);
                Tile(F("Free on {0}", root.TrimEnd('\\')), Sizes.Bytes(d.AvailableFreeSpace), F("{0} of {1}", Percent(d.AvailableFreeSpace, d.TotalSize), Sizes.Bytes(d.TotalSize)),
                    1 - free, level, T("Where the race videos are"));
            }
        }
        catch { }
    }

    private static string Percent(long part, long whole) => (whole > 0 ? 100.0 * part / whole : 0).ToString("0", CultureInfo.CurrentCulture) + "%";

    private void Enable()
    {
        var r = Picked;
        int n = grid.SelectedItems.Count;
        load.IsEnabled = showLog.IsEnabled = r != null;
        showVideo.IsEnabled = r?.VideoFile != null;
        delete.IsEnabled = n > 0;
        delete.ToolTip = n > 1 ? F("Delete these {0} sessions and their videos", n) : T("Delete this session and its video");
    }

    private void Load()
    {
        if (Picked is not { } r) return;
        view.Load(r.Item);
    }
}
