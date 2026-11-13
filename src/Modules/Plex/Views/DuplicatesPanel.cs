using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaisysApp.Applets.Plex.Scanner;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>A title with more than one file: its files are the rows under it.</summary>
internal sealed class DupGroup(string name, int count, long size, long others)
{
    public override string ToString() => F("{0}   ·   {1} files, {2}   ·   {3} besides the best", name, count, Sizes.Bytes(size), Sizes.Bytes(others));
}

/// <summary>One file of a duplicated title.</summary>
internal sealed class DupRow(MediaFile file, DupGroup group, bool best, bool here)
{
    public MediaFile File { get; } = file;
    public DupGroup Group { get; } = group;
    public bool Best { get; } = best;
    /// <summary>The file is where this PC can reach it.</summary>
    public bool Here { get; } = here;
    public string Name => File.Name;
    public override string ToString() => Name;
    public string Version => $"{File.ResolutionText} {File.CodecText}";
    public string BestText => Best ? "✓" : "";
    public double Mbps => File.Mbps;
    public string MbpsText => File.MbpsText;
    public long Size => File.Size;
    public string SizeText => File.SizeText;
    public string Library => File.Library;
    public DateTime Added => File.Added ?? DateTime.MinValue;
    public string AddedText => File.AddedText;
    public string Path => File.Path;
    public string Where => Here ? File.Path : F("{0} (not found on this PC)", File.Path);
}

/// <summary>
/// Duplicates: titles with more than one file (a 720p and a 1080p of a movie, say): Plex's own list of them (quick), or
/// also the same title in two libraries (reads every title); each title's files under it, the best (highest resolution,
/// then bit rate) ticked; sorted by any column, searched; files deleted from the disk (never a title's last), and the
/// folder rescanned in Plex.
/// </summary>
internal sealed class DuplicatesPanel : StackPanel
{
    private readonly PlexService service;
    private readonly MediaFiles files;
    private PlexSettings S => service.Settings;
    private readonly ComboBox library = new() { Width = 200 };
    private readonly CheckBox across = new() { Content = T("Across libraries"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("Also the same title in two libraries (reads every title: slower)") };
    private readonly TextBox search = new() { Width = 180, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("Search titles and paths") };
    private readonly DataGrid grid = Ui.Table(520);
    private readonly TextBlock summary = Ui.Secondary("", true);
    private readonly TextBlock status = Ui.Note("", 6);
    private readonly Debounce filter;
    private List<DupRow> rows = [];
    private ListCollectionView? view;
    private CancellationTokenSource? reading;

    public DuplicatesPanel(PlexService service, MediaFiles files)
    {
        this.service = service;
        this.files = files;
        filter = new Debounce(TimeSpan.FromMilliseconds(250), () => Dispatcher.BeginInvoke(Filter));
        across.IsChecked = S.DuplicatesAcross;
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        System.Windows.Automation.AutomationProperties.SetName(library, T("Library"));
        library.SelectionChanged += (_, _) => { if (library.SelectedItem is PlexLibrary l) { S.DuplicatesLibrary = l.Key; S.Save(); _ = Load(); } };
        Ui.OnToggle(across, on => { S.DuplicatesAcross = on; S.Save(); _ = Load(); });
        search.TextChanged += (_, _) => filter.Poke();
        var refresh = Ui.IconButton("\uE72C", T("Refresh"), T("Read the library again"), () => { files.Forget(); _ = Load(); });

        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.Columns.Add(Ui.Column(T("Title"), nameof(DupRow.Name), -3));
        grid.Columns.Add(Ui.Column(T("Best"), nameof(DupRow.BestText), 45, nameof(DupRow.Best)));
        grid.Columns.Add(Ui.Column(T("Version"), nameof(DupRow.Version), 95));
        grid.Columns.Add(Ui.Column(T("Bit rate"), nameof(DupRow.MbpsText), 90, nameof(DupRow.Mbps), true));
        grid.Columns.Add(Ui.Column(T("Size"), nameof(DupRow.SizeText), 80, nameof(DupRow.Size), true));
        grid.Columns.Add(Ui.Column(T("Library"), nameof(DupRow.Library), 100));
        grid.Columns.Add(Ui.Column(T("Added"), nameof(DupRow.AddedText), 85, nameof(DupRow.Added)));
        grid.Columns.Add(Ui.Column(T("File"), nameof(DupRow.Where), -4));
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        var header = new FrameworkElementFactory(typeof(TextBlock));
        header.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        header.SetValue(MarginProperty, new Thickness(4, 10, 0, 3));
        grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = header } });
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is DupRow r && r.Here) Ui.ShowInExplorer(S.LocalPath(r.Path)); };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { library, across, search } };
        var buttons = PlexUi.Buttons(
            PlexUi.Small(T("Pick All But the Best"), () => Pick(r => !r.Best), T("Pick every file except each title's best")),
            PlexUi.Small(T("Pick All But the Smallest"), () => Pick(r => r.File != rows.Where(o => o.Group == r.Group).MinBy(o => o.Size)!.File)),
            PlexUi.SmallConfirm(T("Delete Files"), () => _ = Delete(), T("Delete the picked files from the disk (a title's last file is never deleted)")),
            PlexUi.Small(T("Show in Explorer"), () => { if (grid.SelectedItem is DupRow r) Ui.ShowInExplorer(S.LocalPath(r.Path)); }),
            PlexUi.Small(T("Copy Path"), () => { if (grid.SelectedItem is DupRow r) Ui.Copy(S.LocalPath(r.Path)); }));
        buttons.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Duplicates"), refresh), top, summary, grid, buttons, status } }, new Thickness(0)));

        IsVisibleChanged += (_, e) => { if (e.NewValue is true && library.Items.Count == 0) _ = LoadLibraries(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { library.ItemsSource = null; files.Forget(); if (IsVisible) _ = LoadLibraries(); });
    }

    private async Task LoadLibraries()
    {
        if (service.Client == null) { status.Text = T("Plex isn't set up: Settings → Plex."); return; }
        try
        {
            var libs = await ScannerView.VideoLibraries(service);
            library.ItemsSource = libs;
            library.SelectedItem = libs.FirstOrDefault(l => l.Key == S.DuplicatesLibrary) ?? libs.FirstOrDefault();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task Load()
    {
        if (library.SelectedItem is not PlexLibrary lib) return;
        reading?.Cancel();
        var cancel = (reading = new CancellationTokenSource()).Token;
        bool all = across.IsChecked == true;
        grid.ItemsSource = null;
        summary.Text = "";
        try
        {
            var found = new List<MediaFile>();
            var libs = all ? ((List<PlexLibrary>)library.ItemsSource).Where(l => l.Type == lib.Type).ToList() : [lib];
            foreach (var l in libs)
            {
                void Progress(long done, long total) => status.Text = F("Reading {0}: {1:N0} of {2:N0}…", l.Title, done, total);
                status.Text = F("Reading {0}…", l.Title);
                found.AddRange(all ? await files.AllAsync(l, Progress, cancel) : await files.DuplicatesAsync(l, Progress, cancel));
            }
            if (cancel.IsCancellationRequested) return;
            rows = Group(found, all);
            status.Text = "";
            Filter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancel.IsCancellationRequested) status.Text = ex.Message; }
    }

    /// <summary>Files grouped by title (by its Plex id; across libraries by what it was matched to), those with two or more.</summary>
    private List<DupRow> Group(List<MediaFile> found, bool all)
    {
        var list = new List<DupRow>();
        var groups = found.GroupBy(f => all && f.Guid.Length > 0 && !f.Guid.StartsWith("local:") ? f.Guid : f.LibraryKey + "/" + f.ItemKey);
        foreach (var g in groups)
        {
            var fs = g.ToList();
            if (fs.Select(f => f.MediaId).Distinct().Count() < 2) continue;
            var best = fs.OrderByDescending(f => f.Height).ThenByDescending(f => f.BitrateKbps).First();
            long size = fs.Sum(f => f.Size);
            var group = new DupGroup(best.Name, fs.Count, size, size - best.Size);
            foreach (var f in fs.OrderByDescending(f => f == best).ThenByDescending(f => f.Size))
                list.Add(new DupRow(f, group, f == best, File.Exists(S.LocalPath(f.Path))));
        }
        return list;
    }

    private void Filter()
    {
        string q = search.Text.Trim();
        var shown = q.Length == 0 ? rows : rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var groups = new HashSet<DupGroup>();
        foreach (var r in shown) groups.Add(r.Group);
        view = new ListCollectionView(shown);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DupRow.Group)));
        if (q.Length == 0) view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(DupRow.Size), System.ComponentModel.ListSortDirection.Descending));
        grid.ItemsSource = view;
        long extra = shown.Where(r => !r.Best).Sum(r => r.Size);
        summary.Text = groups.Count == 0 ? T("No title has more than one file.")
            : F("{0:N0} titles with more than one file · {1:N0} files besides the best · {2}", groups.Count, shown.Count(r => !r.Best), Sizes.Bytes(extra));
        summary.Margin = new Thickness(0, 0, 0, 6);
    }

    private void Pick(Func<DupRow, bool> which)
    {
        grid.SelectedItems.Clear();
        if (view == null) return;
        foreach (DupRow r in view)
            if (which(r)) grid.SelectedItems.Add(r);
    }

    private async Task Delete()
    {
        var picked = grid.SelectedItems.Cast<DupRow>().ToList();
        if (picked.Count == 0) { status.Text = T("Pick the files to delete."); return; }
        int deleted = 0, kept = 0, failed = 0;
        long freed = 0;
        var folders = new HashSet<(string, string)>();
        foreach (var r in picked)
        {
            // never a title's last file
            if (rows.Count(o => o.Group == r.Group) <= 1) { kept++; continue; }
            string path = S.LocalPath(r.Path);
            try
            {
                await Task.Run(() => File.Delete(path));
                if (File.Exists(path)) throw new IOException(T("still there"));
                Log.Here.Info($"Plex Scanner: deleted duplicate {path} ({r.Size} bytes) of {r.Name}");
                rows.Remove(r);
                files.Removed(r.File);
                folders.Add((r.File.LibraryKey, r.File.Folder));
                freed += r.Size;
                deleted++;
            }
            catch (Exception ex)
            {
                failed++;
                status.Text = F("{0}: {1}", Path.GetFileName(path), ex.Message);
                Log.Here.Warn($"Plex Scanner: couldn't delete {path}: {ex.Message}");
            }
        }
        foreach (var (lib, folder) in folders) await files.RescanFolderAsync(lib, folder);
        // counted again (titles left with one file aren't duplicates any more)
        rows = Group(rows.Select(r => r.File).ToList(), across.IsChecked == true);
        Filter();
        string text = F("{0} deleted, {1} freed.", deleted, Sizes.Bytes(freed));
        if (kept > 0) text += " " + P(kept, "{0} kept: a title's last file.", "{0} kept: each a title's last file.");
        if (failed > 0) text += " " + F("{0} couldn't be deleted.", failed);
        status.Text = text;
    }
}
