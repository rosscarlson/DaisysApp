using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Server;
using DaisysApp.Shared;
using DaisysServer.Protocol;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.17.0) Share Session (since 4.17.16 the Shared Session Manager), from the Analysis card's share icon (Import
/// was there before): this PC's sessions shared on the server, taken off it, or a file imported; each session shared
/// by itself (a toggle at the bottom); under it every session shared on the server, everyone's, searched as you type
/// (every column, the box at the top right) and filtered from a right-click on a header, sorted by a header. Compare
/// downloads the one picked (once) into Imported and puts it in Session 2; a double-click does too and closes the window
/// (since 4.17.21 into Session 1, Session 2 emptied). Since 4.17.21 the Driver column is who shared it, by their display
/// name on the server.
/// One window. Since 4.17.11 also from Session 1's and 2's Import: searched for Session 1's track (a note when none are
/// shared), what's compared (or imported from a file) going into that slot. Since 4.17.16 the sessions to share ticked
/// in a drop-down (Shift ticks those between), shared one after the other as their track (no title or description to
/// type; no Export, nor Remove: Delete does), the Session Manager's Share ticking those picked there; every button at
/// the bottom. Since 4.18.0 on ForzaLeaderBoard (<see cref="SessionServer.Leaderboard"/>; Daisy's Server before), with
/// <b>Account</b> (<see cref="AccountWindow"/>: signing in, or another server) and <b>View on ForzaLeaderBoard</b>.
/// </summary>
internal sealed class ShareWindow
{
    private static ShareWindow? open;

    private readonly SessionsView view;
    private readonly Window window;
    /// <summary>(since 4.17.16) The sessions to share: a drop-down of this PC's, a box each.</summary>
    private readonly ToggleButton pick = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 5, 8, 5) };
    private readonly TextBlock pickText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel pickList = new();
    private readonly List<(SessionsView.Item Item, CheckBox Box, TextBlock State)> boxes = new();
    private readonly HashSet<string> ticked = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The box last ticked or not: Shift and another does those between the same.</summary>
    private int? lastBox;
    private readonly TextBlock shareState = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button share = Tool("\ue72d", T("Share to Server"), new Thickness(0)),
        import = Tool("\ued25", T("Import from a file"), new Thickness(8, 0, 0, 0));
    private readonly Button auto = Tool("\uE895", T("Share each session automatically"), new Thickness(0, 0, 8, 0));
    /// <summary>(since 4.18.0) ForzaLeaderBoard: the account signed in with (or another server).</summary>
    private readonly Button account = Tool("\uE77B", T("Account"), new Thickness(0, 0, 8, 0));

    private readonly TextBox search = new() { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Every column") };
    private readonly TextBlock status = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column, CanUserSortColumns = true,
    };
    private readonly HeaderFilter drivers = new(T("Driver")), games = new(T("Game")), tracks = new(T("Track")), cars = new(T("Car")), classes = new(T("Class")),
        videos = new(T("Video")), sharers = new(T("Shared by"));
    private readonly Button compare = Tool("", T("Compare"), new Thickness(24, 0, 0, 0)), watch = Tool("", T("Open on YouTube"), new Thickness(8, 0, 0, 0)),
        delete = Tool("", T("Delete from Server"), new Thickness(8, 0, 0, 0)),
        page = Tool("\uE774", T("View on ForzaLeaderBoard"), new Thickness(8, 0, 0, 0));
    private Action disarmDelete = () => { };
    private readonly List<Row> rows = new();
    private ICollectionView? shown;
    private bool busy;
    /// <summary>(since 4.17.11) Where Compare (and Import) put a session: Session 1 or 2.</summary>
    private int slot = 2;
    private bool listed;

    private sealed class Row
    {
        public required ServerSession S { get; init; }
        public DateTime Date => S.Started;
        public string DateText => S.Started == DateTime.MinValue ? "" : SessionsView.When(S.Started);
        /// <summary>(since 4.17.21) Their display name on the server (what the share was sent with before).</summary>
        public string Driver => S.Anonymous ? F("{0} (anonymous)", S.Name) : S.Name;
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
        public string SharedBy => S.Client.Length > 0 ? $"{S.User} · {S.Client}" : S.User;
        public DateTime Shared => S.SharedAt;
        public string SharedText => S.SharedAt == DateTime.MinValue ? "" : SessionsView.When(S.SharedAt);
        /// <summary>Every column's text, for the search.</summary>
        public string All => string.Join("\n", DateText, Driver, Game, Track, Car, Class, LapsText, BestText, VideoText, SharedBy, SharedText);
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
    /// <param name="check">(since 4.17.16) The sessions ticked to share (the Session Manager's picked; Session 1).</param>
    public static void Show(FrameworkElement owner, SessionsView view, int slot = 2, string? find = null, IEnumerable<string>? check = null)
    {
        if (open is { window.IsLoaded: true } was)
        {
            was.slot = slot;
            if (check != null) was.Tick(check);
            if (find != null) was.search.Text = find;
            if (was.window.WindowState == WindowState.Minimized) was.window.WindowState = WindowState.Normal;
            was.window.Activate();
            return;
        }
        var w = open = new ShareWindow(owner, view) { slot = slot };
        if (find != null) w.search.Text = find;
        if (check != null) w.Tick(check);
        w.window.Closed += (_, _) => open = null;
        w.window.Show();
    }

    private ShareWindow(FrameworkElement owner, SessionsView view)
    {
        this.view = view;
        // (Share Session 4.17.0–4.17.15)
        window = Ui.Dialog(T("Shared Session Manager"), owner, 1180, 720);
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
        share.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        share.Click += async (_, _) => await Share();
        import.Click += (_, _) => view.Import(slot);
        compare.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        compare.Click += (_, _) => Compare();
        watch.Click += (_, _) => { if (Picked is { S.Video: { } url }) Ui.Open(url); };
        page.Click += (_, _) => { if (Picked is { } r && SessionServer.PageOf(r.S.Id) is { } url) Ui.Open(url); };
        account.Click += (_, _) => AccountWindow.Show(window, () => _ = List());
        disarmDelete = ConfirmButton.ArmIcon(delete, async () => { if (Picked is { S.Mine: true } r) await Unshare(r.S.Id); });
        var refresh = Tool("", T("Refresh"), new Thickness(8, 0, 0, 0));
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
        // (since 4.17.16 Share and Import here, Refresh with them)
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { account, auto, close } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { share, import, compare, watch, page, delete, refresh } });
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);

        BuildGrid();
        body.Children.Add(grid);
        window.Content = body;

        Fill();
        view.Listed += Listed;
        ShareQueue.Changed += ShareChanged;
        SessionServer.Shared.Pushed += Pushed;
        SessionServer.Leaderboard.Changed += LinkChanged;
        window.Closed += (_, _) =>
        {
            view.Listed -= Listed;
            ShareQueue.Changed -= ShareChanged;
            SessionServer.Shared.Pushed -= Pushed;
            SessionServer.Leaderboard.Changed -= LinkChanged;
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
        panel.Children.Add(new TextBlock { Text = T("Sessions to share"), Margin = new Thickness(0, 0, 0, 4) });
        var chevron = new TextBlock { Text = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        chevron.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        DockPanel.SetDock(chevron, Dock.Right);
        pick.Content = new DockPanel { Children = { chevron, pickText } };
        System.Windows.Automation.AutomationProperties.SetName(pick, T("Sessions to share"));
        var frame = new Border
        {
            Child = new ScrollViewer { Content = pickList, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Padding = new Thickness(10, 8, 6, 8), BorderThickness = new Thickness(1),
        };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        frame.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        frame.SetBinding(FrameworkElement.MinWidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = pick });
        var popup = new Popup { PlacementTarget = pick, Placement = PlacementMode.Bottom, StaysOpen = false, Child = frame, VerticalOffset = 2 };
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = pick, Mode = BindingMode.TwoWay });
        // (a click on the button while it's open shuts it, not opens it again)
        popup.Opened += (_, _) => pick.IsHitTestVisible = false;
        popup.Closed += (_, _) => pick.IsHitTestVisible = true;
        shareState.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        shareState.MaxWidth = 420;
        DockPanel.SetDock(shareState, Dock.Right);
        panel.Children.Add(new DockPanel { Children = { shareState, pick } });
        return panel;
    }

    /// <summary>This PC's sessions (not imported ones) in the drop-down, a box each, those ticked kept.</summary>
    private void Fill()
    {
        var mine = view.Items.Where(i => !i.Imported).ToList();
        ticked.IntersectWith(mine.Select(i => i.Key));
        boxes.Clear();
        pickList.Children.Clear();
        lastBox = null;
        var clear = new Button { Content = T("Clear"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            ticked.Clear();
            ShareChanged();
        };
        pickList.Children.Add(clear);
        foreach (var i in mine)
        {
            var state = new TextBlock { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            state.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            DockPanel.SetDock(state, Dock.Right);
            // (in a TextBlock: an underscore in a name isn't an access key)
            var line = new TextBlock { Text = i.Line, VerticalAlignment = VerticalAlignment.Center };
            var box = new CheckBox
            {
                Content = new DockPanel { Children = { state, line } }, Margin = new Thickness(0, 2, 8, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            System.Windows.Automation.AutomationProperties.SetName(box, i.Line);
            int k = boxes.Count;
            box.Click += (_, _) => Ticked(k, box.IsChecked == true);
            boxes.Add((i, box, state));
            pickList.Children.Add(box);
        }
        ShareChanged();
    }

    /// <summary>A box ticked or not; with Shift, those between it and the last one too.</summary>
    private void Ticked(int k, bool on)
    {
        int from = k, to = k;
        if (lastBox is int last && last < boxes.Count && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) (from, to) = (Math.Min(last, k), Math.Max(last, k));
        for (int n = from; n <= to; n++)
        {
            if (!boxes[n].Box.IsEnabled) continue;
            if (on) ticked.Add(boxes[n].Item.Key); else ticked.Remove(boxes[n].Item.Key);
        }
        lastBox = k;
        ShareChanged();
    }

    /// <summary>(since 4.17.16) The sessions ticked: these.</summary>
    private void Tick(IEnumerable<string> keys)
    {
        ticked.Clear();
        foreach (var k in keys) ticked.Add(k);
        Fill();
    }

    private void Listed() => window.Dispatcher.BeginInvoke(Fill);

    /// <summary>Each session's share (shared or going: not ticked, its box greyed out), the drop-down's words, and what's
    /// going.</summary>
    private void ShareChanged()
    {
        if (!window.Dispatcher.CheckAccess()) { window.Dispatcher.BeginInvoke(ShareChanged); return; }
        string going = "";
        foreach (var (i, box, state) in boxes)
        {
            bool done = SessionShares.Of(i.Key) != null, now = ShareQueue.Progress(i.Key) != null;
            if (done || now) ticked.Remove(i.Key);
            box.IsEnabled = !done && !now;
            box.IsChecked = ticked.Contains(i.Key);
            state.Text = ShareQueue.State(i);
            if (now) going = F("{0}: {1}", ShareQueue.TitleOf(i), state.Text);
        }
        var picked = boxes.Where(b => ticked.Contains(b.Item.Key)).ToList();
        pickText.Text = boxes.Count == 0 ? T("No sessions of yours yet") : picked.Count == 0 ? T("Pick the sessions to share")
            : picked.Count == 1 ? picked[0].Item.Line : F("{0} sessions", picked.Count);
        // (what's going, gone when it's done; a problem stays)
        if (going.Length > 0 || !shareState.Text.StartsWith(F("Couldn't be shared: {0}", ""), StringComparison.Ordinal)) shareState.Text = going;
        Enable();
    }

    /// <summary>(since 4.17.16) The sessions ticked shared, the oldest first, one after the other (the title bar saying
    /// which and how far).</summary>
    private async Task Share()
    {
        if (!SessionServer.Leaderboard.Configured) { shareState.Text = T("The server isn't set up (Account)"); return; }
        var these = boxes.Where(b => ticked.Contains(b.Item.Key)).Select(b => b.Item).OrderBy(i => i.Started).ToList();
        if (these.Count == 0) return;
        pick.IsChecked = false;
        shareState.Text = "";
        var problem = await ShareQueue.ShareMany(these, view.Driver);
        if (problem != null) shareState.Text = F("Couldn't be shared: {0}", problem);
        // (ForzaLeaderBoard's boards are made again every 5 minutes)
        else if (SessionServer.Leaderboard.UsesBuiltIn) shareState.Text = T("Shared. It'll be on the leaderboards in a few minutes.");
        await List();
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
        Column(sharers.Header(), nameof(Row.SharedBy));
        Column(T("Shared"), nameof(Row.SharedText), nameof(Row.Shared));
        foreach (var f in Filters) f.Changed += Show;
        HeaderFilter.Attach(grid, Values);
        tracks.Label = v => rows.Count(r => r.Track == v && Passes(r, tracks)) is > 1 and var n ? $"{v} ({n})" : v;
        grid.SelectionChanged += (_, _) => { disarmDelete(); Enable(); };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow) Compare(open: true);
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Picked != null) { Compare(open: true); e.Handled = true; }
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
        if (!SessionServer.Leaderboard.Configured)
        {
            status.Text = T("The server isn't set up (Account)");
            rows.Clear();
            Show();
            return;
        }
        status.Text = T("Connecting…");
        try
        {
            var list = await SessionServer.Shared.ListAsync();
            rows.Clear();
            rows.AddRange(list.Select(s => new Row { S = s }));
            listed = true;
            Show();
        }
        catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException)
        {
            status.Text = F("Server: {0}", SessionServer.Friendly(ex));
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
        if (SessionServer.Leaderboard.Configured) status.Text = "";
        ShowNone();
        Enable();
    }

    /// <summary>(since 4.17.11) The search finding none of the sessions shared: said.</summary>
    private void ShowNone()
    {
        if (!listed || !SessionServer.Leaderboard.Configured) return;
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

    private void LinkChanged() => window.Dispatcher.BeginInvoke(() => { if (!SessionServer.Leaderboard.Configured) _ = List(); });

    private void Enable()
    {
        share.IsEnabled = ticked.Count > 0 && SessionServer.Leaderboard.Configured;
        pick.IsEnabled = boxes.Count > 0;
        var r = Picked;
        compare.IsEnabled = r != null && !busy;
        watch.IsEnabled = r?.S.Video != null;
        delete.IsEnabled = r?.S.Mine == true;
        page.IsEnabled = r != null && SessionServer.PageOf(r.S.Id) != null;
    }

    /// <summary>The session picked put in Compare: one of this PC's straight from its log, else downloaded (once) into
    /// Imported. <paramref name="open"/> (a double-click, Enter): since 4.17.21 it's Session 1, Session 2 emptied (its
    /// list that track's sessions), and the window closes once it's there.</summary>
    private async void Compare(bool open = false)
    {
        if (Picked is not { } r || busy) return;
        var all = SessionShares.All;
        string? key = all.Shared.FirstOrDefault(s => s.Value.Id == r.S.Id).Key;
        if (key != null && !File.Exists(Path.Combine(SessionLog.Folder, key))) key = null;
        if (key == null)
        {
            busy = true;
            Enable();
            try
            {
                var progress = new Progress<double>(p => status.Text = F("Downloading {0:0}%", p * 100));
                key = await SessionServer.Shared.FetchAsync(r.S.Id, r.S.Name, r.S.Title, progress);
                status.Text = "";
                Show();
            }
            catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException or InvalidDataException or FormatException)
            {
                status.Text = F("It couldn't be downloaded: {0}", SessionServer.Friendly(ex));
                return;
            }
            finally
            {
                busy = false;
                Enable();
            }
        }
        await view.CompareDownloaded(key, open ? 1 : slot, alone: open);
        if (open) window.Close();
    }
}
