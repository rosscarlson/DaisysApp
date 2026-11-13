using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Server;
using DaisysApp.Shared;
using DaisysServer.Protocol;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.17.0) Share Session, from the Analysis card's share icon (Import was there before): Session 1 shared on
/// Daisy's Server with a title and description (the track, car and so on at first; edited, and sent again once shared),
/// taken off it, exported to a file or a file imported, and each session shared by itself; under it every session
/// shared on the server, everyone's, searched as you type (every column) and filtered from a right-click on a header,
/// sorted by a header. Compare downloads the one picked (once) into Imported and puts it in Session 2. The search starts
/// with Session 1's track. One window.
/// </summary>
internal sealed class ShareWindow
{
    private static Window? open;

    private readonly SessionsView view;
    private readonly Window window;
    private readonly TextBlock session = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock shareState = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox title = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button share = Tool("", T("Share on Daisy's Server"), new Thickness(0)), unshare = Tool("", T("Remove from Daisy's Server"), new Thickness(8, 0, 0, 0)),
        export = Tool("", T("Export to a file"), new Thickness(8, 0, 0, 0)), import = Tool("", T("Import from a file"), new Thickness(8, 0, 0, 0));
    private readonly CheckBox auto = new() { Content = T("Share each session automatically"), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Once it's kept; after its YouTube upload when that's on") };
    private Action disarmUnshare = () => { };

    private readonly TextBox search = new() { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Every column") };
    private readonly TextBlock status = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, CanUserSortColumns = true,
    };
    private readonly HeaderFilter drivers = new(T("Driver")), games = new(T("Game")), tracks = new(T("Track")), cars = new(T("Car")), classes = new(T("Class")),
        videos = new(T("Video")), sharers = new(T("Shared by"));
    private readonly Button compare = Tool("", T("Compare"), new Thickness(0)), watch = Tool("", T("Open on YouTube"), new Thickness(8, 0, 0, 0)),
        delete = Tool("", T("Delete from Daisy's Server"), new Thickness(8, 0, 0, 0));
    private Action disarmDelete = () => { };
    private readonly List<Row> rows = new();
    private ICollectionView? shown;
    private SessionsView.Item? item;
    private bool busy;

    private sealed class Row
    {
        public required ServerSession S { get; init; }
        public DateTime Date => S.Started;
        public string DateText => S.Started == DateTime.MinValue ? "" : SessionsView.When(S.Started);
        public string Driver => S.Driver;
        public string Game => S.Game;
        public string Track => S.Track;
        public string Car => S.Car;
        public string Class => S.Class;
        public int Laps => S.Laps;
        public string LapsText => S.Laps > 0 ? S.Laps.ToString(CultureInfo.CurrentCulture) : "";
        public float Best => S.Best > 0 ? S.Best : float.MaxValue;
        public string BestText => S.Best > 0 ? Blocks.LapTime(S.Best) : "";
        public bool HasVideo => S.Video != null;
        public string VideoGlyph => S.Video != null ? "" : "";
        public string VideoText => S.Video != null ? T("With video") : T("No video");
        public string Title => S.Title;
        public string Tip => S.Description.Length > 0 ? S.Description : S.Title;
        public string SharedBy => S.Client.Length > 0 ? $"{S.User} · {S.Client}" : S.User;
        public DateTime Shared => S.SharedAt;
        public string SharedText => S.SharedAt == DateTime.MinValue ? "" : SessionsView.When(S.SharedAt);
        /// <summary>Every column's text, for the search.</summary>
        public string All => string.Join("\n", DateText, Driver, Game, Track, Car, Class, LapsText, BestText, VideoText, Title, S.Description, SharedBy, SharedText);
    }

    /// <summary>A button that's only an icon, its name as its tooltip (shown when it's disabled too).</summary>
    private static Button Tool(string glyph, string name, Thickness margin)
    {
        var b = Ui.IconButton(glyph, "", name);
        b.Padding = new Thickness(12, 6, 12, 6);
        b.Margin = margin;
        ToolTipService.SetShowOnDisabled(b, true);
        System.Windows.Automation.AutomationProperties.SetName(b, name);
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
        open = new ShareWindow(owner, view).window;
        open.Closed += (_, _) => open = null;
        open.Show();
    }

    private ShareWindow(FrameworkElement owner, SessionsView view)
    {
        this.view = view;
        window = Ui.Dialog(T("Share Session"), owner, 1180, 720);
        window.MinWidth = 760;
        window.MinHeight = 440;
        window.ResizeMode = ResizeMode.CanResize;

        var body = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };
        var top = BuildShare();
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);

        var head = new DockPanel { Margin = new Thickness(0, 18, 0, 8) };
        var header = Ui.Header(T("On Daisy's Server"));
        header.VerticalAlignment = VerticalAlignment.Center;
        header.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(header, Dock.Left);
        head.Children.Add(header);
        var label = new TextBlock { Text = T("Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        head.Children.Add(label);
        search.Width = 320;
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        search.TextChanged += (_, _) => shown?.Refresh();
        DockPanel.SetDock(search, Dock.Left);
        head.Children.Add(search);
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        head.Children.Add(status);
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        compare.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        compare.Click += (_, _) => Compare();
        watch.Click += (_, _) => { if (Picked is { S.Video: { } url }) Ui.Open(url); };
        disarmDelete = ConfirmButton.ArmIcon(delete, async () => { if (Picked is { S.Mine: true } r) await Unshare(r.S.Id); });
        var refresh = Tool("", T("Refresh"), new Thickness(0, 0, 8, 0));
        refresh.Click += (_, _) => _ = List();
        var close = Tool("", T("Close"), new Thickness(0));
        close.Click += (_, _) => window.Close();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { refresh, close } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { compare, watch, delete } });
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);

        BuildGrid();
        body.Children.Add(grid);
        window.Content = body;

        // (Session 1's track, to find others' runs of it)
        if (view.Session1 is { TrackName.Length: > 0 } s1) search.Text = s1.TrackName;
        ShowSession();
        window.Activated += (_, _) => { if (!Same(view.Session1, item)) ShowSession(); };
        ShareQueue.Changed += ShareChanged;
        SessionServer.Pushed += Pushed;
        ServerLink.Changed += LinkChanged;
        window.Closed += (_, _) =>
        {
            ShareQueue.Changed -= ShareChanged;
            SessionServer.Pushed -= Pushed;
            ServerLink.Changed -= LinkChanged;
        };
        window.Loaded += (_, _) => search.Focus();
        Enable();
        _ = List();
    }

    private static bool Same(SessionsView.Item? a, SessionsView.Item? b) => a?.Key == b?.Key;

    // ---------------------------------------------------------------- Session 1

    private FrameworkElement BuildShare()
    {
        var panel = new StackPanel();
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var header = Ui.Header(T("This session"));
        header.VerticalAlignment = VerticalAlignment.Center;
        header.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(header, Dock.Left);
        line.Children.Add(header);
        DockPanel.SetDock(session, Dock.Left);
        line.Children.Add(session);
        shareState.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        line.Children.Add(shareState);
        panel.Children.Add(line);

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition());
        form.RowDefinitions.Add(new RowDefinition());
        void Field(string name, Control box, int row)
        {
            var l = new TextBlock { Text = name, VerticalAlignment = row == 0 ? VerticalAlignment.Center : VerticalAlignment.Top, Margin = new Thickness(0, row == 0 ? 0 : 4, 10, 0) };
            Grid.SetRow(l, row);
            form.Children.Add(l);
            System.Windows.Automation.AutomationProperties.SetName(box, name);
            box.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            form.Children.Add(box);
        }
        Field(T("Title"), title, 0);
        Field(T("Description"), description, 1);
        panel.Children.Add(form);

        var actions = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        share.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        share.Click += async (_, _) => await Share();
        disarmUnshare = ConfirmButton.ArmIcon(unshare, async () => { if (item != null && SessionShares.Of(item.Key) is { } s) await Unshare(s.Id); });
        export.Click += (_, _) => view.Export();
        import.Click += (_, _) => view.Import();
        auto.IsChecked = SessionShares.All.Auto;
        auto.Click += (_, _) =>
        {
            var all = SessionShares.All;
            all.Auto = auto.IsChecked == true;
            // (from now: not every session kept before)
            if (all.Auto) all.AutoSince = DateTime.Now;
            all.Save();
            if (all.Auto) view.Refresh();
        };
        DockPanel.SetDock(auto, Dock.Right);
        actions.Children.Add(auto);
        actions.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { share, unshare, export, import } });
        panel.Children.Add(actions);
        return panel;
    }

    /// <summary>Session 1, its share and the title and description it has (or would have).</summary>
    private void ShowSession()
    {
        item = view.Session1;
        session.Text = item?.Title ?? T("No session picked");
        var shared = item == null ? null : SessionShares.Of(item.Key);
        var (t, d) = item == null ? ("", "") : shared != null ? (shared.Title, shared.Description) : SessionsView.Describe(item);
        title.Text = t;
        description.Text = d;
        ShareChanged();
    }

    private void ShareChanged()
    {
        if (!window.Dispatcher.CheckAccess()) { window.Dispatcher.BeginInvoke(ShareChanged); return; }
        disarmUnshare();
        var shared = item == null ? null : SessionShares.Of(item.Key);
        string state = item == null ? "" : ShareQueue.State(item);
        shareState.Text = item == null ? "" : item.Imported ? T("Someone else's: only your own sessions are shared")
            : ShareQueue.Progress(item.Key) != null ? state
            : ShareQueue.Failure(item.Key) is { } why ? F("Couldn't be shared: {0}", why)
            : shared != null ? F("Shared {0}", shared.Shared.ToString("g", CultureInfo.CurrentCulture))
            : state.Length > 0 ? state : T("Not shared");
        Enable();
    }

    private async Task Share()
    {
        if (item == null || item.Imported) return;
        if (!ServerLink.Configured) { shareState.Text = T("Daisy's Server isn't set up (Settings → General)"); return; }
        string t = title.Text.Trim(), d = description.Text.Trim();
        if (t.Length == 0) t = SessionsView.Describe(item).Title;
        var problem = await ShareQueue.Share(item, t, d, view.Driver);
        if (problem == null) await List();
    }

    private async Task Unshare(string id)
    {
        if (await ShareQueue.Unshare(id) is { } problem) status.Text = problem;
        else
        {
            rows.RemoveAll(r => r.S.Id == id);
            Show();
            ShareChanged();
        }
    }

    // ---------------------------------------------------------------- the server's sessions

    private Row? Picked => grid.SelectedItem as Row;

    private void BuildGrid()
    {
        grid.SetResourceReference(FrameworkElement.StyleProperty, typeof(DataGrid));
        System.Windows.Automation.AutomationProperties.SetName(grid, T("Shared sessions"));
        Column(T("Date"), nameof(Row.DateText), nameof(Row.Date));
        Column(drivers.Header(), nameof(Row.Driver));
        Column(games.Header(), nameof(Row.Game));
        Column(tracks.Header(), nameof(Row.Track), star: 2);
        Column(cars.Header(), nameof(Row.Car), star: 2);
        Column(classes.Header(), nameof(Row.Class));
        Column(T("Laps"), nameof(Row.LapsText), nameof(Row.Laps), right: true);
        Column(T("Best"), nameof(Row.BestText), nameof(Row.Best), right: true);
        Column(videos.Header(), nameof(Row.VideoGlyph), nameof(Row.HasVideo), font: new FontFamily("Segoe MDL2 Assets"));
        Column(T("Title"), nameof(Row.Title), star: 2.5, tip: nameof(Row.Tip));
        Column(sharers.Header(), nameof(Row.SharedBy));
        Column(T("Shared"), nameof(Row.SharedText), nameof(Row.Shared));
        foreach (var f in Filters) f.Changed += Show;
        HeaderFilter.Attach(grid, Values);
        tracks.Label = v => rows.Count(r => r.Track == v && Passes(r, tracks)) is > 1 and var n ? $"{v} ({n})" : v;
        grid.SelectionChanged += (_, _) => { disarmDelete(); Enable(); };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) Compare();
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Picked != null) { Compare(); e.Handled = true; }
        };
    }

    private HeaderFilter[] Filters => new[] { drivers, games, tracks, cars, classes, videos, sharers };

    private void Column(object header, string path, string? sort = null, double star = 0, bool right = false, string? tip = null, FontFamily? font = null)
    {
        var cell = new Style(typeof(TextBlock));
        if (tip != null) cell.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(tip)));
        cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 6, 2)));
        if (right) cell.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        var column = new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path), SortMemberPath = sort ?? path, ElementStyle = cell,
            Width = star > 0 ? new DataGridLength(star, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            MinWidth = star > 0 ? 110 : 54,
        };
        if (font != null) column.FontFamily = font;
        grid.Columns.Add(column);
    }

    private string Value(Row r, HeaderFilter f) =>
        f == drivers ? r.Driver : f == games ? r.Game : f == tracks ? r.Track : f == cars ? r.Car : f == classes ? r.Class : f == videos ? r.VideoText : r.SharedBy;

    private bool Passes(Row r, HeaderFilter? except)
    {
        foreach (var f in Filters)
            if (f != except && !f.Keeps(Value(r, f))) return false;
        if (except != null) return true;
        // (each word anywhere in the row)
        foreach (var word in search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (r.All.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        return true;
    }

    private IEnumerable<string> Values(HeaderFilter f) => rows.Where(r => Passes(r, f)).Select(r => Value(r, f)).Where(v => v.Length > 0).Distinct().ToList();

    /// <summary>The server's sessions, asked for again.</summary>
    private async Task List()
    {
        if (!ServerLink.Configured)
        {
            status.Text = T("Daisy's Server isn't set up (Settings → General)");
            rows.Clear();
            Show();
            return;
        }
        status.Text = T("Connecting…");
        try
        {
            var list = await SessionServer.ListAsync();
            rows.Clear();
            rows.AddRange(list.Select(s => new Row { S = s }));
            Show();
        }
        catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException)
        {
            status.Text = F("Daisy's Server: {0}", ex.Message.TrimEnd('.'));
        }
    }

    /// <summary>The rows shown again (the filters, search and sort kept; the one picked kept).</summary>
    private void Show()
    {
        string? keep = Picked?.S.Id;
        var sorts = shown?.SortDescriptions.ToList() is { Count: > 0 } was ? was : new() { new SortDescription(nameof(Row.Shared), ListSortDirection.Descending) };
        shown = CollectionViewSource.GetDefaultView(rows.ToList());
        shown.Filter = o => o is Row r && Passes(r, null);
        grid.ItemsSource = shown;
        foreach (var s in sorts) shown.SortDescriptions.Add(s);
        foreach (var c in grid.Columns)
            c.SortDirection = sorts.FirstOrDefault(s => s.PropertyName == c.SortMemberPath) is { PropertyName: not null } s ? s.Direction : null;
        if (keep != null) grid.SelectedItem = shown.Cast<Row>().FirstOrDefault(r => r.S.Id == keep);
        int n = shown.Cast<Row>().Count();
        status.Text = ServerLink.Configured ? (n == rows.Count ? P(rows.Count, "{0} session", "{0} sessions") + " · " + ServerLink.Name
            : F("{0} of {1} shown", n, rows.Count)) : status.Text;
        Enable();
    }

    /// <summary>The server's news: a session shared, changed or taken off (by anyone).</summary>
    private void Pushed(string op, JsonObject m)
    {
        window.Dispatcher.BeginInvoke(() =>
        {
            switch (op)
            {
                case "added" or "changed" when m["session"] is JsonObject s:
                    var session = ServerSession.From(s);
                    rows.RemoveAll(r => r.S.Id == session.Id);
                    rows.Add(new Row { S = session });
                    break;
                case "removed":
                    string id = ServerClient.Str(m, "id");
                    rows.RemoveAll(r => r.S.Id == id);
                    break;
                default: return;
            }
            Show();
        });
    }

    private void LinkChanged() => window.Dispatcher.BeginInvoke(() => { if (!ServerLink.Configured) _ = List(); });

    private void Enable()
    {
        bool own = item is { Imported: false };
        bool going = item != null && ShareQueue.Progress(item.Key) != null;
        var shared = item == null ? null : SessionShares.Of(item.Key);
        share.IsEnabled = own && !going && ServerLink.Configured;
        share.ToolTip = shared != null ? T("Update on Daisy's Server") : T("Share on Daisy's Server");
        unshare.IsEnabled = shared != null && !going;
        title.IsEnabled = description.IsEnabled = own;
        export.IsEnabled = view.CanExport;
        var r = Picked;
        compare.IsEnabled = r != null && !busy;
        watch.IsEnabled = r?.S.Video != null;
        delete.IsEnabled = r?.S.Mine == true;
    }

    /// <summary>The session picked put in Compare: one of this PC's straight from its log, else downloaded (once) into
    /// Imported.</summary>
    private async void Compare()
    {
        if (Picked is not { } r || busy) return;
        var all = SessionShares.All;
        string? key = all.Shared.FirstOrDefault(s => s.Value.Id == r.S.Id).Key;
        if (key != null && !File.Exists(Path.Combine(SessionLog.Folder, key))) key = null;
        if (key == null && all.Downloaded.TryGetValue(r.S.Id, out var got) && File.Exists(Path.Combine(SessionLog.Folder, got))) key = got;
        if (key == null)
        {
            busy = true;
            Enable();
            try
            {
                var progress = new Progress<double>(p => status.Text = F("Downloading {0:0}%", p * 100));
                string text = await SessionServer.DownloadAsync(r.S.Id, progress);
                if (!text.Split('\n').Take(12).Any(l => l.StartsWith("ms,steering", StringComparison.Ordinal))) throw new IOException(T("not a session"));
                Directory.CreateDirectory(SessionLog.ImportedFolder);
                string name = string.Join("_", $"{(r.S.Driver.Length > 0 ? r.S.Driver : r.S.User)} - {r.S.Title}".Split(Path.GetInvalidFileNameChars()));
                if (name.Length > 120) name = name[..120];
                string target = Path.Combine(SessionLog.ImportedFolder, name + ".csv");
                for (int k = 2; File.Exists(target); k++) target = Path.Combine(SessionLog.ImportedFolder, $"{name} ({k}).csv");
                await File.WriteAllTextAsync(target, text, new UTF8Encoding(false));
                key = "Imported\\" + Path.GetFileName(target);
                all.Downloaded[r.S.Id] = key;
                all.Save();
                Logging.Log.Here.Info($"Analysis: downloaded {r.S.Id} from Daisy's Server to {target}");
                Show();
            }
            catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException or InvalidDataException or FormatException)
            {
                status.Text = F("It couldn't be downloaded: {0}", ex.Message.TrimEnd('.'));
                return;
            }
            finally
            {
                busy = false;
                Enable();
            }
        }
        await view.CompareDownloaded(key);
        ShowSession();
    }
}
