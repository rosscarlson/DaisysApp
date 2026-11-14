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
/// (since 4.17.0) Share Session, from the Analysis card's share icon (Import was there before): one of this PC's sessions
/// (picked in a drop-down; Session 1 at first) shared on Daisy's Server once, with a title and description (the track,
/// car and so on at first) beside each other, taken off it, exported to a file or a file imported; each session shared
/// by itself (a toggle at the bottom); under it every session shared on the server, everyone's, searched as you type
/// (every column, the box at the top right) and filtered from a right-click on a header, sorted by a header. Compare
/// downloads the one picked (once) into Imported and puts it in Session 2; a double-click does too and closes the window.
/// One window. Since 4.17.11 also from Session 1's and 2's Import: searched for Session 1's track (a note when none are
/// shared), what's compared (or imported from a file) going into that slot.
/// </summary>
internal sealed class ShareWindow
{
    private static ShareWindow? open;

    private readonly SessionsView view;
    private readonly Window window;
    private readonly ComboBox pick = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock shareState = new() { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox title = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button share = Tool("", T("Share on Daisy's Server"), new Thickness(0)), unshare = Tool("", T("Remove from Daisy's Server"), new Thickness(8, 0, 0, 0)),
        export = Tool("", T("Export to a file"), new Thickness(8, 0, 0, 0)), import = Tool("", T("Import from a file"), new Thickness(8, 0, 0, 0));
    private readonly Button auto = Tool("\uE895", T("Share each session automatically"), new Thickness(0, 0, 8, 0));
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
    /// <summary>(since 4.17.11) Where Compare (and Import) put a session: Session 1 or 2.</summary>
    private int slot = 2;
    private bool listed;

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

    /// <param name="slot">(since 4.17.11) Where Compare puts a session: Session 1 or 2.</param>
    /// <param name="find">(since 4.17.11) Searched for at first (Session 1's track).</param>
    public static void Show(FrameworkElement owner, SessionsView view, int slot = 2, string? find = null)
    {
        if (open is { window.IsLoaded: true } was)
        {
            was.slot = slot;
            if (find != null) was.search.Text = find;
            if (was.window.WindowState == WindowState.Minimized) was.window.WindowState = WindowState.Normal;
            was.window.Activate();
            return;
        }
        var w = open = new ShareWindow(owner, view) { slot = slot };
        if (find != null) w.search.Text = find;
        w.window.Closed += (_, _) => open = null;
        w.window.Show();
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
        var header = Ui.Header(T("All Shared Sessions"));
        header.VerticalAlignment = VerticalAlignment.Center;
        header.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(header, Dock.Left);
        head.Children.Add(header);
        search.Width = 320;
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        search.TextChanged += (_, _) => { shown?.Refresh(); ShowNone(); };
        DockPanel.SetDock(search, Dock.Right);
        head.Children.Add(search);
        var label = new TextBlock { Text = T("Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Right);
        head.Children.Add(label);
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
        auto.Click += (_, _) =>
        {
            var all = SessionShares.All;
            all.Auto = !all.Auto;
            // (from now: not every session kept before)
            if (all.Auto) all.AutoSince = DateTime.Now;
            all.Save();
            ShowAuto();
            if (all.Auto) view.Refresh();
        };
        ShowAuto();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { refresh, auto, close } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { compare, watch, delete } });
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);

        BuildGrid();
        body.Children.Add(grid);
        window.Content = body;

        Fill(view.Session1);
        view.Listed += Listed;
        ShareQueue.Changed += ShareChanged;
        SessionServer.Pushed += Pushed;
        ServerLink.Changed += LinkChanged;
        window.Closed += (_, _) =>
        {
            view.Listed -= Listed;
            ShareQueue.Changed -= ShareChanged;
            SessionServer.Pushed -= Pushed;
            ServerLink.Changed -= LinkChanged;
        };
        window.Loaded += (_, _) => search.Focus();
        Enable();
        _ = List();
    }

    /// <summary>The automatic sharing button: blue while it's on.</summary>
    private void ShowAuto()
    {
        bool on = SessionShares.All.Auto;
        if (on) auto.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        else auto.ClearValue(FrameworkElement.StyleProperty);
        System.Windows.Automation.AutomationProperties.SetItemStatus(auto, on ? T("On") : T("Off"));
    }

    // ---------------------------------------------------------------- the session to share

    private FrameworkElement BuildShare()
    {
        var panel = new StackPanel();
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 4; r++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock Label(string name, int row, int column)
        {
            var l = new TextBlock { Text = name, Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 4) };
            Grid.SetRow(l, row);
            Grid.SetColumn(l, column);
            form.Children.Add(l);
            return l;
        }
        Label(T("Session"), 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(pick, T("Session"));
        pick.SelectionChanged += (_, _) => { if (!filling) ShowPicked(); };
        Grid.SetRow(pick, 1);
        form.Children.Add(pick);
        var titleLabel = Label(T("Title"), 2, 0);
        System.Windows.Automation.AutomationProperties.SetName(title, T("Title"));
        Grid.SetRow(title, 3);
        form.Children.Add(title);
        Label(T("Description"), 0, 2);
        System.Windows.Automation.AutomationProperties.SetName(description, T("Description"));
        Grid.SetRow(description, 1);
        Grid.SetRowSpan(description, 3);
        Grid.SetColumn(description, 2);
        form.Children.Add(description);
        // (the description as tall as the session and title beside it, however long it is)
        void Fit() => description.Height = pick.ActualHeight + titleLabel.ActualHeight + 12 + title.ActualHeight;
        pick.SizeChanged += (_, _) => Fit();
        title.SizeChanged += (_, _) => Fit();
        panel.Children.Add(form);

        var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        share.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        share.Click += async (_, _) => await Share();
        disarmUnshare = ConfirmButton.ArmIcon(unshare, async () => { if (item != null && SessionShares.Of(item.Key) is { } s) await Unshare(s.Id); });
        export.Click += (_, _) =>
        {
            if (item == null) return;
            // (Session 1: the lap picked, as Compare's export did; another: the whole session)
            if (view.Session1?.Key == item.Key && view.CanExport) view.Export();
            else view.Export(item);
        };
        import.Click += (_, _) => view.Import(slot);
        shareState.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        // (Share, gone once it's shared, or Remove; then Export and Import)
        share.Margin = unshare.Margin = export.Margin = import.Margin = new Thickness(0, 0, 8, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { share, unshare, export, import } };
        DockPanel.SetDock(tools, Dock.Left);
        actions.Children.Add(tools);
        actions.Children.Add(shareState);
        panel.Children.Add(actions);
        return panel;
    }

    private bool filling;

    /// <summary>This PC's sessions in the drop-down (not imported ones), <paramref name="keep"/> picked (else the one
    /// picked, else the newest).</summary>
    private void Fill(SessionsView.Item? keep)
    {
        string? key = keep is { Imported: false } ? keep.Key : item?.Key;
        var mine = view.Items.Where(i => !i.Imported).ToList();
        filling = true;
        pick.ItemsSource = mine;
        pick.SelectedItem = mine.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? mine.FirstOrDefault();
        filling = false;
        var was = item;
        item = pick.SelectedItem as SessionsView.Item;
        // (the same session listed again: what's being typed stays)
        if (was == null || was.Key != item?.Key) ShowPicked();
        else ShareChanged();
    }

    private void Listed() => window.Dispatcher.BeginInvoke(() => Fill(null));

    /// <summary>The session picked, its share and the title and description it has (or would have).</summary>
    private void ShowPicked()
    {
        item = pick.SelectedItem as SessionsView.Item;
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
        shareState.Text = item == null ? T("No sessions of yours yet")
            : ShareQueue.Progress(item.Key) != null ? state
            : ShareQueue.Failure(item.Key) is { } why ? F("Couldn't be shared: {0}", why)
            : shared != null ? F("Shared {0}", shared.Shared.ToString("g", CultureInfo.CurrentCulture))
            : state.Length > 0 ? state : T("Not shared");
        Enable();
    }

    private async Task Share()
    {
        if (item == null || item.Imported || SessionShares.Of(item.Key) != null) return;
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
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) Compare(close: true);
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Picked != null) { Compare(close: true); e.Handled = true; }
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
            listed = true;
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
        if (ServerLink.Configured) status.Text = "";
        ShowNone();
        Enable();
    }

    /// <summary>(since 4.17.11) The search finding none of the sessions shared: said.</summary>
    private void ShowNone()
    {
        if (!listed || !ServerLink.Configured) return;
        string find = search.Text.Trim();
        if (find.Length > 0 && shown != null && shown.IsEmpty) status.Text = F("No shared sessions for {0}", find);
        else if (status.Text.StartsWith(F("No shared sessions for {0}", ""), StringComparison.Ordinal)) status.Text = "";
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
        // (shared once only: Share goes, Remove's there instead)
        share.Visibility = shared != null ? Visibility.Collapsed : Visibility.Visible;
        share.IsEnabled = own && !going && ServerLink.Configured;
        unshare.Visibility = shared != null ? Visibility.Visible : Visibility.Collapsed;
        unshare.IsEnabled = !going;
        title.IsEnabled = description.IsEnabled = own;
        title.IsReadOnly = description.IsReadOnly = shared != null || going;
        pick.IsEnabled = view.Items.Any(i => !i.Imported);
        export.IsEnabled = own;
        var r = Picked;
        compare.IsEnabled = r != null && !busy;
        watch.IsEnabled = r?.S.Video != null;
        delete.IsEnabled = r?.S.Mine == true;
    }

    /// <summary>The session picked put in Compare: one of this PC's straight from its log, else downloaded (once) into
    /// Imported. <paramref name="close"/> (a double-click, Enter): the window closes once it's there.</summary>
    private async void Compare(bool close = false)
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
        await view.CompareDownloaded(key, slot);
        if (close) window.Close();
    }
}
