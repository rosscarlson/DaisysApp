using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.Plex.Scanner;
using DaisysApp.Shared.Video;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>A show, a season or a movie, with what its files take.</summary>
internal sealed class SpaceRow
{
    public string Name { get; init; } = "";
    public override string ToString() => Name;
    public List<MediaFile> Files { get; init; } = [];
    public int Count => Files.Count;
    public long Size { get; init; }
    public string SizeText => Sizes.Bytes(Size);
    public long Each => Count > 0 ? Size / Count : 0;
    public string EachText => Sizes.Bytes(Each);
    /// <summary>"1080p ×40 · 720p ×2".</summary>
    public string Resolutions => Mix(Files.Select(f => f.ResolutionText));
    public string Codecs => Mix(Files.Select(f => f.CodecText));
    public double Mbps { get; init; }
    public string MbpsText => Mbps > 0 ? Mbps.ToString("0.0") : "";
    public double Hours => Files.Sum(f => f.DurationMs) / 3_600_000.0;
    public string HoursText => Hours.ToString("0.0");
    public long After { get; init; }
    public string AfterText => Sizes.Bytes(After);
    public long Saves => Size - After;
    public string SavesText => Saves > 0 ? Sizes.Bytes(Saves) : "";

    private static string Mix(IEnumerable<string> values) =>
        string.Join(" · ", values.Where(v => v.Length > 0).GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}"));

    public static SpaceRow For(string name, List<MediaFile> files, RecordingProfile p, bool aac)
    {
        // a file of two episodes (S01E01-E02) is listed for each
        files = files.DistinctBy(f => f.Path).ToList();
        double ms = files.Sum(f => (double)f.DurationMs);
        return new SpaceRow
        {
            Name = name, Files = files, Size = files.Sum(f => f.Size),
            Mbps = ms > 0 ? files.Sum(f => (double)f.Size * 8) / (ms / 1000) / 1e6 : 0,
            After = files.Sum(f => Transcoder.Estimate(f, p, aac)),
        };
    }
}

/// <summary>
/// Space: what a library's shows (and each one's seasons) or movies take: files, size, size per episode, resolutions,
/// codecs, the bit rate, hours, and roughly what they'd take after a transcode with the video profile picked (files already
/// that small left out). The picked ones go to Transcode's queue.
/// </summary>
internal sealed class SpacePanel : StackPanel
{
    private readonly PlexService service;
    private readonly MediaFiles files;
    private readonly Transcoder transcoder;
    private readonly Action showQueue;
    private PlexSettings S => service.Settings;
    private readonly ComboBox library = new() { Width = 200 };
    private readonly TextBox search = new() { Width = 180, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("Search titles") };
    private readonly DataGrid titles = Ui.Table(420), seasons = Ui.Table(220);
    private readonly TextBlock summary = Ui.Secondary("", true), seasonsHeader = Ui.Header(T("Seasons")), status = Ui.Note("", 6);
    private readonly FrameworkElement seasonsPart;
    private readonly Button addTitles;
    private readonly Debounce filter;
    private List<MediaFile> all = [];
    private List<SpaceRow> rows = [];
    private CancellationTokenSource? reading;
    private bool tv;

    public SpacePanel(PlexService service, MediaFiles files, Transcoder transcoder, Action showQueue)
    {
        this.service = service;
        this.files = files;
        this.transcoder = transcoder;
        this.showQueue = showQueue;
        filter = new Debounce(TimeSpan.FromMilliseconds(250), () => Dispatcher.BeginInvoke(Fill));
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        System.Windows.Automation.AutomationProperties.SetName(library, T("Library"));
        library.SelectionChanged += (_, _) => { if (library.SelectedItem is PlexLibrary l) { S.SpaceLibrary = l.Key; S.Save(); _ = Load(); } };
        search.TextChanged += (_, _) => filter.Poke();
        var profile = ScannerView.ProfileBox(service, transcoder, Fill);
        profile.Margin = new Thickness(10, 0, 0, 0);
        var refresh = Ui.IconButton("\uE72C", T("Refresh"), T("Read the library again"), () => { if (library.SelectedItem is PlexLibrary l) files.Forget(l.Key); _ = Load(); });

        foreach (var g in new[] { titles, seasons })
        {
            g.SelectionMode = DataGridSelectionMode.Extended;
            g.Columns.Add(Ui.Column(T("Title"), nameof(SpaceRow.Name), -3));
            g.Columns.Add(Ui.Column(T("Files"), nameof(SpaceRow.Count), 55, null, true));
            g.Columns.Add(Ui.Column(T("Size"), nameof(SpaceRow.SizeText), 80, nameof(SpaceRow.Size), true));
            g.Columns.Add(Ui.Column(T("Each"), nameof(SpaceRow.EachText), 75, nameof(SpaceRow.Each), true));
            g.Columns.Add(Ui.Column(T("Resolution"), nameof(SpaceRow.Resolutions), -2));
            g.Columns.Add(Ui.Column(T("Codec"), nameof(SpaceRow.Codecs), -1.5));
            g.Columns.Add(Ui.Column(T("Mbit/s"), nameof(SpaceRow.MbpsText), 60, nameof(SpaceRow.Mbps), true));
            g.Columns.Add(Ui.Column(T("Hours"), nameof(SpaceRow.HoursText), 60, nameof(SpaceRow.Hours), true));
            g.Columns.Add(Ui.Column(T("≈ After"), nameof(SpaceRow.AfterText), 80, nameof(SpaceRow.After), true));
            g.Columns.Add(Ui.Column(T("≈ Saves"), nameof(SpaceRow.SavesText), 80, nameof(SpaceRow.Saves), true));
        }
        titles.SelectionChanged += (_, _) => FillSeasons();

        addTitles = PlexUi.Small(T("Transcode"), () => Queue(titles), T("Add the picked titles' files to Transcode's queue (files already this small are skipped)"));
        var addSeasons = PlexUi.Small(T("Transcode Seasons"), () => Queue(seasons), T("Add the picked seasons' files to Transcode's queue"));
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { library, profile, search } };
        summary.Margin = new Thickness(0, 0, 0, 6);
        var b1 = PlexUi.Buttons(addTitles);
        b1.Margin = new Thickness(0, 8, 0, 0);
        var b2 = PlexUi.Buttons(addSeasons);
        b2.Margin = new Thickness(0, 8, 0, 0);
        seasonsHeader.Margin = new Thickness(0, 12, 0, 6);
        seasonsPart = new StackPanel { Children = { seasonsHeader, seasons, b2 } };
        Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Space"), refresh), top, summary, titles, b1, seasonsPart, status } }, new Thickness(0)));

        IsVisibleChanged += (_, e) => { if (e.NewValue is true && library.Items.Count == 0) _ = LoadLibraries(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { library.ItemsSource = null; if (IsVisible) _ = LoadLibraries(); });
    }

    private async Task LoadLibraries()
    {
        if (service.Client == null) { status.Text = T("Plex isn't set up: Settings → Plex."); return; }
        try
        {
            var libs = await ScannerView.VideoLibraries(service);
            library.ItemsSource = libs;
            library.SelectedItem = libs.FirstOrDefault(l => l.Key == S.SpaceLibrary) ?? libs.FirstOrDefault(l => l.Type == "show") ?? libs.FirstOrDefault();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task Load()
    {
        if (library.SelectedItem is not PlexLibrary lib) return;
        reading?.Cancel();
        var cancel = (reading = new CancellationTokenSource()).Token;
        tv = lib.Type == "show";
        titles.ItemsSource = seasons.ItemsSource = null;
        all = [];
        summary.Text = "";
        status.Text = F("Reading {0}…", lib.Title);
        try
        {
            var found = await files.AllAsync(lib, (done, total) => status.Text = F("Reading {0}: {1:N0} of {2:N0}…", lib.Title, done, total), cancel);
            if (cancel.IsCancellationRequested) return;
            all = found;
            status.Text = "";
            Fill();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancel.IsCancellationRequested) status.Text = ex.Message; }
    }

    /// <summary>The titles' rows, again (the profile changed, a search, files deleted or replaced).</summary>
    private void Fill()
    {
        var p = transcoder.Profile;
        bool aac = S.TranscodeAac;
        string q = search.Text.Trim();
        var groups = tv ? all.GroupBy(f => f.ShowKey).Select(g => SpaceRow.For(g.First().Show, g.ToList(), p, aac))
                        : all.GroupBy(f => f.LibraryKey + "/" + f.ItemKey).Select(g => SpaceRow.For(g.First().Name, g.ToList(), p, aac));
        rows = groups.Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Size).ToList();
        titles.ItemsSource = rows;
        long size = rows.Sum(r => r.Size), after = rows.Sum(r => r.After);
        summary.Text = all.Count == 0 ? "" : F("{0:N0} files · {1}", rows.Sum(r => r.Count), Sizes.Bytes(size)) + " · "
            + F("with {0} ({1}) ≈ {2}, saving ≈ {3}", p.Name, ScannerView.Describe(p), Sizes.Bytes(after), Sizes.Bytes(size - after));
        addTitles.Content = tv ? T("Transcode Shows") : T("Transcode Movies");
        seasonsPart.Visibility = tv ? Visibility.Visible : Visibility.Collapsed;
        FillSeasons();
    }

    private void FillSeasons()
    {
        if (!tv) return;
        var picked = titles.SelectedItems.Cast<SpaceRow>().ToList();
        var p = transcoder.Profile;
        seasonsHeader.Text = picked.Count == 1 ? F("Seasons of {0}", picked[0].Name) : T("Seasons");
        seasons.ItemsSource = picked.SelectMany(r => r.Files.GroupBy(f => (f.Show, f.Season)).OrderBy(g => g.Key.Show).ThenBy(g => g.Key.Season)
            .Select(g => SpaceRow.For(picked.Count == 1 ? Season(g.Key.Season) : $"{g.Key.Show} · {Season(g.Key.Season)}", g.OrderBy(f => f.Episode).ToList(), p, S.TranscodeAac))).ToList();
    }

    private static string Season(int n) => n == 0 ? T("Specials") : F("Season {0}", n);

    private void Queue(DataGrid from)
    {
        var picked = from.SelectedItems.Cast<SpaceRow>().SelectMany(r => r.Files).ToList();
        if (picked.Count == 0) { status.Text = T("Pick what to transcode."); return; }
        var p = transcoder.Profile;
        var wanted = picked.Where(f => Transcoder.WouldSkip(f, p) == null).OrderBy(f => f.Show).ThenBy(f => f.Season).ThenBy(f => f.Episode).ThenBy(f => f.Name).ToList();
        int added = transcoder.Add(wanted);
        status.Text = F("{0} files added to the queue; {1} already this small.", added, picked.Count - wanted.Count);
        if (added > 0) showQueue();
    }
}
