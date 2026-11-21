using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DaisysApp.Applets.SimEffects.Analysis.Sessions;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since SimEffects Analysis 0.4.0) The Analysis tab of a product that hides the sessions
/// (<c>SimEffects/Analysis/Sessions</c>: they're looked at on ForzaLeaderBoard's website, each one shared by itself
/// once signed in). Race Videos: each race recorded, uploaded to YouTube (who can watch it), its file kept or not, Delete
/// Incomplete Race; the ForzaLeaderBoard account; then Uploads: the latest sessions, how their share and YouTube upload
/// went, a right-click sending one again.
/// </summary>
internal sealed class UploadsView : UserControl
{
    private readonly AnalysisApplet applet;
    private readonly SessionsView sessions;
    private readonly CheckBox record, upload, keep;
    private readonly ComboBox privacy = new() { Width = 120, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock youTubeNote = Ui.Note(""), accountText = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button signIn = new() { Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid table = Ui.Table();
    private readonly TextBlock empty = Ui.Note(T("None yet"));
    private bool showing, due;
    private const int Listed = 50;

    private sealed record Row(SessionsView.Item Item, string When, string Track, string Car, string Session, string SessionTip, string Video, string? Link)
    {
        public string Best => Item.Best > 0 ? Blocks.LapTime(Item.Best) : "";
    }

    public UploadsView(AnalysisApplet applet, SessionsView sessions, TextBlock status)
    {
        this.applet = applet;
        this.sessions = sessions;
        var s = applet.Settings;
        var all = RaceVideos.All;

        var videos = new StackPanel();
        videos.Children.Add(Ui.Header(T("Race Videos")));
        record = new CheckBox { Content = T("Record each race"), IsChecked = s.RecordVideo, Margin = new Thickness(0, 8, 0, 8) };
        ToolTipService.SetShowOnDisabled(record, true);
        record.Click += (_, _) => { applet.SetRecordVideo(record.IsChecked == true); Show(); };
        videos.Children.Add(record);
        upload = new CheckBox { Content = T("Upload to YouTube"), IsChecked = all.YouTubeAuto, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("When its recording is saved") };
        upload.Click += (_, _) => { all.YouTubeAuto = upload.IsChecked == true; all.Save(); Show(); };
        foreach (var p in Enum.GetValues<YouTubePrivacy>())
            privacy.Items.Add(new ComboBoxItem { Content = p switch { YouTubePrivacy.Private => T("Private"), YouTubePrivacy.Public => T("Public"), _ => T("Unlisted") }, Tag = p });
        privacy.SelectedIndex = (int)all.YouTubePrivacy;
        privacy.ToolTip = T("Who can watch it");
        AutomationProperties.SetName(privacy, T("Upload as"));
        privacy.SelectionChanged += (_, _) => { if (!showing && privacy.SelectedItem is ComboBoxItem { Tag: YouTubePrivacy p }) { all.YouTubePrivacy = p; all.Save(); } };
        videos.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0), Children = { upload, privacy } });
        // (small: a race's video on YouTube is what shows it was driven as it says)
        var verify = Ui.Note(T("YouTube Required for Verification"), new Thickness(48, 2, 0, 0));
        verify.ToolTip = T("Without its video on YouTube, a race can't be verified if it's questioned");
        videos.Children.Add(verify);
        youTubeNote.Margin = new Thickness(48, 2, 0, 0);
        videos.Children.Add(youTubeNote);
        keep = new CheckBox { Content = T("Keep the video file"), IsChecked = !all.YouTubeDeleteLocal, Margin = new Thickness(24, 8, 0, 0), ToolTip = T("Once it's on YouTube") };
        keep.Click += (_, _) => { all.YouTubeDeleteLocal = keep.IsChecked != true; all.Save(); };
        videos.Children.Add(keep);
        var incomplete = new CheckBox { Content = T("Delete Incomplete Race"), IsChecked = s.DeleteIncomplete, Margin = new Thickness(0, 12, 0, 0),
            ToolTip = T("A race left before its finish: its session and video deleted") };
        incomplete.Click += (_, _) => { s.DeleteIncomplete = incomplete.IsChecked == true; s.Save(); };
        videos.Children.Add(incomplete);

        var account = new StackPanel();
        account.Children.Add(Ui.Header("ForzaLeaderBoard"));
        var accountRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var site = new Button { Content = T("Open the website"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        site.Click += (_, _) => Ui.Open(SessionServer.Website);
        signIn.Click += (_, _) => AccountWindow.Show(this, Show);
        DockPanel.SetDock(site, Dock.Right);
        DockPanel.SetDock(signIn, Dock.Right);
        accountRow.Children.Add(site);
        accountRow.Children.Add(signIn);
        accountRow.Children.Add(accountText);
        account.Children.Add(accountRow);
        (status.Parent as Panel)?.Children.Remove(status);
        status.Margin = new Thickness(0, 10, 0, 0);
        account.Children.Add(status);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var videosCard = SimUi.Card(videos);
        videosCard.Margin = new Thickness(0, 0, 6, 12);
        var accountCard = SimUi.Card(account);
        accountCard.Margin = new Thickness(6, 0, 0, 12);
        Grid.SetColumn(accountCard, 1);
        top.Children.Add(videosCard);
        top.Children.Add(accountCard);

        // Uploads: about ten rows, then it scrolls
        table.MaxHeight = 330;
        table.RowHeight = 28;
        table.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(table, T("Uploads"));
        table.Columns.Add(Ui.Column(T("When"), nameof(Row.When), 120));
        table.Columns.Add(Ui.Column(T("Track"), nameof(Row.Track), -2));
        table.Columns.Add(Ui.Column(T("Car"), nameof(Row.Car), -2));
        table.Columns.Add(Ui.Column(T("Best"), nameof(Row.Best), 80, right: true));
        var session = Ui.Column(T("Session"), nameof(Row.Session), 150);
        session.CellStyle = TipStyle(nameof(Row.SessionTip));
        table.Columns.Add(session);
        var video = Ui.Column("YouTube", nameof(Row.Video), 130);
        video.CellStyle = TipStyle(nameof(Row.Link));
        table.Columns.Add(video);
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is Row { Link: { } url } && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Ui.Open(url); };
        table.ContextMenu = new ContextMenu();
        table.ContextMenuOpening += (_, e) => { if (!Menu()) e.Handled = true; };
        table.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(table, d) is DataGridRow r) table.SelectedItem = r.Item;
        };
        empty.Margin = new Thickness(0, 8, 0, 0);
        var uploads = new StackPanel();
        uploads.Children.Add(Ui.Header(T("Uploads")));
        uploads.Children.Add(table);
        uploads.Children.Add(empty);

        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        page.Children.Add(top);
        page.Children.Add(SimUi.Card(uploads));
        Content = SimUi.Scroll(page);

        // (shown again at most twice a second: an upload's progress comes often)
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { if (due && IsVisible) { due = false; Fill(); } };
        timer.Start();
        void Due() => due = true;
        ShareQueue.Changed += Due;
        RaceUploads.Changed += Due;
        RaceVideos.Changed += Due;
        sessions.Listed += Due;
        SessionServer.Leaderboard.Changed += () => Dispatcher.BeginInvoke(() => { Show(); Due(); });
        YouTube.Changed += () => Dispatcher.BeginInvoke(() => { Show(); Due(); });
        DaisysApp.Shell.ScreenRecordings.Changed += Show;
        IsVisibleChanged += (_, _) => { if (IsVisible) { Show(); Fill(); } };
        Show();
        Fill();
    }

    private static Style TipStyle(string path)
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding(path)));
        return style;
    }

    /// <summary>The options' state: Record greyed out while Recording is off, Upload's while it isn't recording, Keep's
    /// while it isn't uploading; YouTube's account and ForzaLeaderBoard's.</summary>
    private void Show()
    {
        showing = true;
        var all = RaceVideos.All;
        bool can = DaisysApp.Shell.ScreenRecordings.Recorder != null;
        record.IsEnabled = can;
        record.ToolTip = can ? T("With the Recording tab's settings") : T("Needs the Recording tab");
        record.IsChecked = applet.Settings.RecordVideo;
        upload.IsChecked = all.YouTubeAuto;
        upload.IsEnabled = privacy.IsEnabled = record.IsChecked == true;
        privacy.SelectedIndex = (int)all.YouTubePrivacy;
        keep.IsChecked = !all.YouTubeDeleteLocal;
        keep.IsEnabled = upload.IsEnabled && all.YouTubeAuto;
        youTubeNote.Text = all.YouTubeAuto && !YouTube.SignedIn ? T("Sign in to YouTube in Settings → Accounts") : "";
        youTubeNote.Visibility = youTubeNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var board = SessionServer.Leaderboard;
        accountText.Text = board.SignedIn ? F("Signed in as {0}", board.Settings.User) : T("Not signed in: sessions wait to be shared");
        accountText.FontWeight = board.SignedIn ? FontWeights.SemiBold : FontWeights.Normal;
        accountText.SetResourceReference(TextBlock.ForegroundProperty, board.SignedIn ? "SuccessBrush" : "TextSecondaryBrush");
        signIn.Content = board.SignedIn ? T("Account") : T("Sign In");
        showing = false;
    }

    /// <summary>The latest sessions of this PC, newest first.</summary>
    private void Fill()
    {
        if (!IsVisible) { due = true; return; }
        var kept = (table.SelectedItem as Row)?.Item.Key;
        var rows = sessions.Items.Where(i => !i.Imported).Take(Listed).Select(i =>
        {
            var (state, tip) = ShareQueue.Status(i);
            var (video, link) = VideoState(i.Key);
            return new Row(i, SessionsView.When(i.Started), i.TrackName, i.CarName, state, tip, video, link);
        }).ToList();
        table.ItemsSource = rows;
        table.SelectedItem = rows.FirstOrDefault(r => r.Item.Key == kept);
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Its race video on YouTube: the link, how far its upload is, waiting, failed (why), not uploaded or no video.</summary>
    private static (string Text, string? Link) VideoState(string key)
    {
        var link = RaceVideos.Of(key);
        if (link?.YouTube is { } url) return (T("Yes"), url);
        if (RaceUploads.Progress(key) is double p) return (double.IsNaN(p) ? T("Waiting") : F("Uploading {0:0}%", p * 100), null);
        if (RaceUploads.Failure(key) is { } f) return (T("Failed"), f);
        if (link is not { HasFile: true }) return (T("No video"), null);
        return (link.Upload ? T("Waiting") : T("Not uploaded"), null);
    }

    /// <summary>The row's right-click: Retry (or Upload) what didn't go, Watch on YouTube, Open on the website.</summary>
    private bool Menu()
    {
        if (table.SelectedItem is not Row row) return false;
        var menu = table.ContextMenu!;
        menu.Items.Clear();
        var item = row.Item;
        var link = RaceVideos.Of(item.Key);
        bool videoLeft = link is { HasFile: true, YouTube: null } && RaceUploads.Progress(item.Key) == null;
        bool shareLeft = SessionShares.Of(item.Key) == null && ShareQueue.Progress(item.Key) == null;
        if (videoLeft || shareLeft)
        {
            bool failed = RaceUploads.Failure(item.Key) != null || ShareQueue.Failure(item.Key) != null;
            var retry = new MenuItem { Header = failed ? T("Retry") : T("Upload") };
            retry.Click += (_, _) => Retry(item, videoLeft, shareLeft);
            menu.Items.Add(retry);
        }
        if (link?.YouTube is { } url)
        {
            var watch = new MenuItem { Header = T("Watch on YouTube") };
            watch.Click += (_, _) => Ui.Open(url);
            menu.Items.Add(watch);
        }
        if (SessionShares.Of(item.Key) is { } shared && SessionServer.PageOf(shared.Id) is { } page)
        {
            var open = new MenuItem { Header = T("Open on the website") };
            open.Click += (_, _) => Ui.Open(page);
            menu.Items.Add(open);
        }
        return menu.Items.Count > 0;
    }

    /// <summary>Sends what's left again: its video to YouTube, and the session (given the video's link once it's up).</summary>
    private async void Retry(SessionsView.Item item, bool video, bool share)
    {
        if (video)
        {
            if (YouTube.SignedIn) RaceUploads.Queue(new[] { item.Key });
            else DaisysApp.Shell.Notifications.Show(T("Analysis"), T("Sign in to YouTube in Settings → Accounts"));
        }
        if (!share) return;
        if (!SessionServer.Leaderboard.SignedIn) { AccountWindow.Show(this, Show); return; }
        if (await ShareQueue.Share(item, ShareQueue.TitleOf(item), "", sessions.Driver) is { } problem)
            DaisysApp.Shell.Notifications.Show(T("Analysis"), problem);
    }
}
