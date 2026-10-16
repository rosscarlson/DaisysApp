using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>One line of the comparison: the file on each side and what the sync does with it.</summary>
internal sealed class SyncRow(SyncItem item)
{
    public SyncItem Item { get; } = item;
    public string Path => Item.Path;
    public string LeftSize => Item.LeftDir ? T("folder") : Item.LeftSize is { } s ? Sizes.Bytes(s) : "";
    public string LeftTime => Item.LeftTime is { } t ? t.ToLocalTime().ToString("g") : "";
    public string RightSize => Item.RightDir && Item.InRight ? T("folder") : Item.RightSize is { } s ? Sizes.Bytes(s) : "";
    public string RightTime => Item.RightTime is { } t ? t.ToLocalTime().ToString("g") : "";
    public string Icon => Item.IsDir ? "" : "";

    public string ActionGlyph => Item.Action switch
    {
        SyncAction.Create => "",
        SyncAction.Update => "",
        SyncAction.Delete => "",
        SyncAction.Skip => "",
        SyncAction.Keep => "",
        _ => "",
    };

    public string ActionText => Item.Action switch
    {
        SyncAction.Create => Item.LeftDir ? T("Make") : T("Copy"),
        SyncAction.Update => T("Update"),
        SyncAction.Delete => T("Delete"),
        SyncAction.Skip => T("Leave"),
        SyncAction.Keep => T("Keep"),
        _ => T("Same"),
    };

    public Brush ActionBrush => Item.Action switch
    {
        SyncAction.Create => Ui.Brush("#3DC46A"),
        SyncAction.Update => Ui.Brush("#4CC2FF"),
        SyncAction.Delete => Ui.Brush("#E5484D"),
        SyncAction.Skip => Ui.Brush("#F2C230"),
        _ => Ui.Brush("#8A8A8A"),
    };

    public string? Tip => Item.Note;
}

/// <summary>
/// File Sync (Tools tab), like FreeFileSync: the source on the left, the destination on the right (browse, paste or
/// pick a recent one; swap them), Mirror or Update, Compare to see what would happen, and Synchronize, which opens the
/// sync's own window (check, then progress) and leaves the app free.
/// </summary>
internal sealed class FileSyncView : DockPanel
{
    private readonly FileSyncSettings settings;
    private readonly Action<SyncJob, SyncPlan> openSync;
    private readonly ComboBox saved = new() { Width = 260, DisplayMemberPath = nameof(SavedEntry.Title) };
    private readonly Button saveAs, remove, syncButton;
    /// <summary>The name given with New…, until Save as… keeps it.</summary>
    private string? pendingName;

    private enum EntryKind { Placeholder, New, Pending, Saved }

    /// <summary>A line of the saved syncs list: "Saved syncs" (none picked), "New…", a new name not saved yet, or a saved sync.</summary>
    private sealed record SavedEntry(string Title, EntryKind Kind, SyncJob? Job = null);
    private readonly ComboBox source = new() { IsEditable = true }, destination = new() { IsEditable = true };
    private readonly TextBlock sourceInfo = Ui.Secondary(), destinationInfo = Ui.Secondary();
    private readonly ToggleButton mirror, update;
    private readonly TextBlock modeText = Ui.Secondary(wrap: true);
    private readonly CheckBox byName = new() { Content = T("Name"), IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = T("Files are always matched by their name and folder. With nothing else ticked, a file that's on both sides counts as the same") };
    private readonly CheckBox bySize = new() { Content = T("Size"), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Files differ when their sizes do") };
    private readonly CheckBox byTime = new() { Content = T("Date and time"), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = T("Files differ when their times do (by more than 2 seconds). Untick it when the times are wrong, e.g. after a copy that didn't keep them") };
    private readonly CheckBox byContent = new() { Content = T("Contents (slower)"), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = T("Files of the same size are read on both sides and compared byte by byte") };
    private readonly CheckBox recycle = new() { Content = T("Deleted files and folders go to the Recycle Bin"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox excludes = new() { MinWidth = 300 };
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly DataGrid grid = new();
    private readonly WrapPanel filters = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel history = new();
    private readonly StackPanel background = new();
    private readonly Dictionary<SyncAction, ToggleButton> chips = new();
    private readonly Button compareButton;
    private SyncPlan? plan;
    private CancellationTokenSource? comparing;
    private bool loading;

    public FileSyncView(FileSyncSettings settings, Action<SyncJob, SyncPlan> openSync)
    {
        this.settings = settings;
        this.openSync = openSync;

        // ---- saved syncs on the left, Compare and Sync on the right
        saved.ToolTip = T("Pick a saved sync, or New… to name these folders and settings and save them");
        saveAs = Ui.IconButton("\uE74E", T("Save as…"), T("Save these folders and settings under the name given with New…, to pick again here or run from the tray icon's menu"), (_, _) => SaveAs());
        saveAs.Margin = new Thickness(8, 0, 0, 0);
        remove = Ui.IconButton("\uE74D", "", T("Forget the saved sync (its folders are untouched)"), (_, _) => RemoveSaved());
        remove.Margin = new Thickness(8, 0, 0, 0);
        saved.SelectionChanged += (_, _) => { if (!loading && saved.SelectedItem is SavedEntry e) Dispatcher.BeginInvoke(() => Picked(e)); };
        compareButton = Ui.IconButton("\uE8FD", T("Compare"), T("Read both folders and list what a sync would do (nothing is changed)"), (_, _) => { if (comparing != null) comparing.Cancel(); else _ = CompareAsync(); });
        // (green: the safe look-first step, beside Sync's blue)
        compareButton.SetResourceReference(Control.BackgroundProperty, "SuccessBrush");
        compareButton.SetResourceReference(Control.BorderBrushProperty, "SuccessBrush");
        compareButton.SetResourceReference(Control.ForegroundProperty, "AccentForegroundBrush");
        syncButton = Ui.IconButton("\uE895", T("Sync"), T("Check what's about to happen, untick anything you don't want, then sync (in its own window: the app stays free)"), (_, _) => Synchronize(), accent: true);
        syncButton.Margin = new Thickness(8, 0, 0, 0);
        foreach (var b in new[] { saveAs, remove, compareButton, syncButton }) ToolTipService.SetShowOnDisabled(b, true);
        var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { compareButton, syncButton } };
        DockPanel.SetDock(actions, Dock.Right);
        topRow.Children.Add(actions);
        topRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { saved, saveAs, remove } });
        SetDock(topRow, Dock.Top);
        Children.Add(topRow);

        // ---- the two folders
        var sides = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(64) }, new ColumnDefinition() } };
        sides.Children.Add(Side(T("Source"), "", source, sourceInfo, T("The folder to copy from"), true));
        var swap = Ui.IconButton("", "", T("Swap the source and the destination"), (_, _) => Swap());
        swap.VerticalAlignment = VerticalAlignment.Center;
        swap.HorizontalAlignment = HorizontalAlignment.Center;
        swap.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetColumn(swap, 1);
        sides.Children.Add(swap);
        var right = Side(T("Destination"), "", destination, destinationInfo, T("The folder to copy to (made if it isn't there)"), false);
        Grid.SetColumn(right, 2);
        sides.Children.Add(right);
        var sidesCard = Ui.Card(sides, new Thickness(0, 0, 0, 12));
        SetDock(sidesCard, Dock.Top);
        Children.Add(sidesCard);

        // ---- how
        mirror = Ui.Chip(T("Mirror"), false, T("The destination ends up exactly like the source: new and changed files are copied, and files and folders only in the destination are deleted"));
        update = Ui.Chip(T("Update"), false, T("New and changed files are copied; nothing in the destination is deleted"));
        mirror.Click += (_, _) => SetMode(SyncMode.Mirror);
        update.Click += (_, _) => SetMode(SyncMode.Update);
        foreach (var c in new[] { bySize, byTime, byContent }) c.Click += (_, _) => Changed();
        recycle.Click += (_, _) => Changed();
        excludes.ToolTip = T("Names or paths the sync leaves alone on both sides, separated by ; (wildcards * and ?; end with \\ for folders only; with a \\ inside, a path from the top folder)");
        excludes.LostFocus += (_, _) => Changed();
        var resetExcludes = new Button { Content = T("Usual"), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Put back the usual list (Windows' own files and temporary files)") };
        resetExcludes.Click += (_, _) => { excludes.Text = SyncJob.DefaultExcludes; Changed(); };

        var how = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(110) }, new ColumnDefinition() } };
        void Row(int r, string label, UIElement content)
        {
            how.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Secondary(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(l, r);
            if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(content, r);
            Grid.SetColumn(content, 1);
            how.Children.Add(l);
            how.Children.Add(content);
        }
        Row(0, T("Mode"), new StackPanel { Orientation = Orientation.Horizontal, Children = { mirror, update, modeText } });
        modeText.VerticalAlignment = VerticalAlignment.Center;
        modeText.Margin = new Thickness(8, 0, 0, 0);
        Row(1, T("Compare by"), new StackPanel { Orientation = Orientation.Horizontal, Children = { byName, bySize, byTime, byContent } });
        Row(2, T("Deleted files"), recycle);
        var excludeRow = new DockPanel();
        DockPanel.SetDock(resetExcludes, Dock.Right);
        excludeRow.Children.Add(resetExcludes);
        excludeRow.Children.Add(excludes);
        Row(3, T("Leave alone"), excludeRow);

        var howCard = Ui.Card(how, new Thickness(0, 0, 0, 12));
        SetDock(howCard, Dock.Top);
        Children.Add(howCard);

        // ---- recent syncs, and over them the syncs running in the background
        var historyCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Recent syncs")), history } }, new Thickness(0, 12, 0, 0));
        SetDock(historyCard, Dock.Bottom);
        Children.Add(historyCard);
        SetDock(background, Dock.Bottom);
        Children.Add(background);

        // ---- what comparing found
        BuildGrid();
        foreach (var (a, label) in new[] { (SyncAction.Create, T("Copy")), (SyncAction.Update, T("Update")), (SyncAction.Delete, T("Delete")), (SyncAction.Skip, T("Leave")), (SyncAction.Keep, T("Keep")), (SyncAction.Same, T("Same")) })
        {
            var chip = Ui.Chip(label, a is not (SyncAction.Same or SyncAction.Keep));
            chip.Tag = label;
            chip.Click += (_, _) => Filter();
            chips[a] = chip;
            filters.Children.Add(chip);
        }
        var resultsTop = new StackPanel { Children = { Ui.Header(T("What a sync would do")), filters, status } };
        status.Margin = new Thickness(0, 0, 0, 8);
        var results = new DockPanel();
        DockPanel.SetDock(resultsTop, Dock.Top);
        results.Children.Add(resultsTop);
        results.Children.Add(grid);
        Children.Add(Ui.Card(results));

        LoadCurrent();
        ShowHistory();
        status.Text = T("Pick the two folders, then Compare to see what a sync would do; then Sync.");
        UpdateChips();
        UpdateButtons();
    }

    private static UIElement Pad(FrameworkElement e, double left) { e.Margin = new Thickness(left, 0, 0, 0); return e; }

    private StackPanel Side(string title, string glyph, ComboBox box, TextBlock info, string tip, bool isSource)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var icon = Ui.Icon(isSource ? "" : "", 16);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        head.Children.Add(icon);
        head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        box.ToolTip = tip;
        box.LostFocus += (_, _) => Changed();
        box.SelectionChanged += (_, _) => { if (!loading) Dispatcher.BeginInvoke(Changed, DispatcherPriority.Background); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Changed(); };
        box.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateButtons()));
        var browse = Ui.IconButton("", T("Browse…"), null, (_, _) =>
        {
            if (Ui.PickFolder(isSource ? T("Copy from which folder?") : T("Copy to which folder?"), box.Text) is { } f) { box.Text = f; Changed(); }
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        info.Margin = new Thickness(2, 6, 0, 0);
        return new StackPanel { Children = { head, row, info } };
    }

    // ---------------------------------------------------------------- the settings on the page

    private SyncJob Job => settings.Current;

    private void LoadCurrent()
    {
        loading = true;
        ShowSaved();
        source.ItemsSource = settings.RecentSources.ToList();
        destination.ItemsSource = settings.RecentDestinations.ToList();
        source.Text = Job.Source;
        destination.Text = Job.Destination;
        bySize.IsChecked = Job.CompareSize;
        byTime.IsChecked = Job.CompareTime;
        byContent.IsChecked = Job.CompareContent;
        recycle.IsChecked = Job.RecycleBin;
        excludes.Text = Job.Excludes;
        loading = false;
        ShowMode();
        ShowFolders();
        UpdateButtons();
    }

    /// <summary>The saved syncs list: "Saved syncs" first (picked when none is), "New…", a name not saved yet, then the saved ones.</summary>
    private void ShowSaved()
    {
        bool was = loading;
        loading = true;
        var list = new List<SavedEntry> { new(T("Saved syncs"), EntryKind.Placeholder), new(T("New…"), EntryKind.New) };
        if (pendingName != null) list.Add(new(F("{0} (not saved yet)", pendingName), EntryKind.Pending));
        list.AddRange(settings.Saved.Select(s => new SavedEntry(s.Name, EntryKind.Saved, s)));
        saved.ItemsSource = list;
        saved.SelectedItem = pendingName != null ? list.First(e => e.Kind == EntryKind.Pending)
            : list.FirstOrDefault(e => e.Job != null && e.Job.SameAs(Job) && e.Job.Name == Job.Name) ?? list[0];
        loading = was;
    }

    private void Picked(SavedEntry e)
    {
        switch (e.Kind)
        {
            case EntryKind.Placeholder:
                pendingName = null;
                Job.Name = "";
                settings.Save();
                ShowSaved();
                break;
            case EntryKind.New:
                string suggestion = Path.GetFileName(Job.Source.TrimEnd('\\')) is { Length: > 0 } n ? n : T("My sync");
                if (Ui.Prompt(this, T("New sync"), T("A name for the new sync. Set its folders and settings, then Save as… keeps it."), suggestion, T("OK")) is { } name)
                {
                    pendingName = name;
                    Job.Name = name;
                    settings.Save();
                }
                ShowSaved();
                break;
            case EntryKind.Saved when e.Job != null:
                pendingName = null;
                Load(e.Job);
                break;
        }
        UpdateButtons();
    }

    /// <summary>What can be clicked: Save as… after New…, Compare with both folders, Sync after a comparison that found something to do.</summary>
    private void UpdateButtons()
    {
        saveAs.IsEnabled = pendingName != null;
        saveAs.ToolTip = pendingName != null
            ? F("Save these folders and settings as \"{0}\", to pick again here or run from the tray icon's menu", pendingName)
            : T("Pick New… in the list and name the sync first");
        remove.IsEnabled = saved.SelectedItem is SavedEntry { Kind: EntryKind.Saved };
        bool folders = source.Text.Trim().Length > 0 && destination.Text.Trim().Length > 0;
        compareButton.IsEnabled = comparing != null || folders;
        compareButton.ToolTip = folders || comparing != null ? T("Read both folders and list what a sync would do (nothing is changed)") : T("Pick the source and the destination first");
        bool canSync = comparing == null && plan != null && !plan.NothingToDo;
        syncButton.IsEnabled = canSync;
        syncButton.ToolTip = canSync ? T("Check what's about to happen, untick anything you don't want, then sync (in its own window: the app stays free)")
            : plan is { NothingToDo: true } ? T("The last comparison found nothing to do")
            : T("Compare first: Sync does what the comparison found");
    }

    private void Load(SyncJob j)
    {
        settings.Current = j.Clone();
        settings.Save();
        LoadCurrent();
        ClearResults();
    }

    private void SetMode(SyncMode m)
    {
        Job.Mode = m;
        ShowMode();
        Changed();
    }

    private void ShowMode()
    {
        mirror.IsChecked = Job.Mode == SyncMode.Mirror;
        update.IsChecked = Job.Mode == SyncMode.Update;
        modeText.Text = Job.Mode == SyncMode.Mirror
            ? T("The destination ends up exactly like the source (extra files in it are deleted).")
            : T("New and changed files are copied; nothing is deleted.");
        recycle.IsEnabled = Job.Mode == SyncMode.Mirror;
    }

    /// <summary>Something on the page changed: saved, and the last comparison no longer holds.</summary>
    private void Changed()
    {
        if (loading) return;
        var j = Job;
        string src = source.Text.Trim().Trim('"'), dst = destination.Text.Trim().Trim('"');
        bool size = bySize.IsChecked == true, time = byTime.IsChecked == true, content = byContent.IsChecked == true;
        bool same = j.Source == src && j.Destination == dst && j.CompareSize == size && j.CompareTime == time && j.CompareContent == content && j.RecycleBin == (recycle.IsChecked == true) && j.Excludes == excludes.Text;
        j.Source = src;
        j.Destination = dst;
        j.CompareSize = size;
        j.CompareTime = time;
        j.CompareContent = content;
        j.RecycleBin = recycle.IsChecked == true;
        j.Excludes = excludes.Text;
        // a saved sync stays picked only while nothing differs from it
        if (saved.SelectedItem is SavedEntry { Job: { } s } && !s.SameAs(j)) { j.Name = ""; ShowSaved(); }
        settings.Save();
        ShowFolders();
        if (!same || plan?.Job.Mode != j.Mode) ClearResults();
        UpdateButtons();
    }

    private void ShowFolders()
    {
        Describe(Job.Source, sourceInfo, true);
        Describe(Job.Destination, destinationInfo, false);
    }

    private void Describe(string path, TextBlock info, bool isSource)
    {
        info.ClearValue(TextBlock.ForegroundProperty);
        if (path.Length == 0) { info.Text = isSource ? T("Pick the folder to copy from.") : T("Pick the folder to copy to."); return; }
        bool exists;
        try { exists = Directory.Exists(path); } catch { exists = false; }
        if (exists)
        {
            string? drive = null;
            try
            {
                if (!path.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
                    drive = F("{0}: {1} free", d.Name.TrimEnd('\\'), Sizes.Bytes(d.AvailableFreeSpace));
                }
                else drive = T("a network folder");
            }
            catch { }
            info.Text = plan != null && SamePath(isSource ? plan.Job.Source : plan.Job.Destination, path)
                ? F("{0} files, {1} · {2}", Sizes.Count(isSource ? plan.LeftFiles : plan.RightFiles), Sizes.Bytes(isSource ? plan.LeftBytes : plan.RightBytes), drive ?? "")
                : drive ?? "";
        }
        else
        {
            info.Text = isSource ? T("There's no such folder.") : T("There's no such folder yet: the sync makes it.");
            if (isSource) info.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void Swap()
    {
        (source.Text, destination.Text) = (destination.Text, source.Text);
        Changed();
    }

    private void Remember()
    {
        FileSyncSettings.Remember(settings.RecentSources, Job.Source);
        FileSyncSettings.Remember(settings.RecentDestinations, Job.Destination);
        settings.Save();
        loading = true;
        string s = source.Text, d = destination.Text;
        source.ItemsSource = settings.RecentSources.ToList();
        destination.ItemsSource = settings.RecentDestinations.ToList();
        source.Text = s;
        destination.Text = d;
        loading = false;
    }

    private void SaveAs()
    {
        if (pendingName == null) return;
        Changed();
        if (Ui.Prompt(this, T("Save the sync"), T("A name for this sync (its folders, mode and settings):"), pendingName, T("Save")) is not { } name) return;
        pendingName = null;
        var existing = settings.Saved.FirstOrDefault(s => s.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        var copy = Job.Clone();
        copy.Name = name;
        if (existing != null) settings.Saved[settings.Saved.IndexOf(existing)] = copy;
        else settings.Saved.Add(copy);
        settings.Saved.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        Job.Name = name;
        settings.Save();
        LoadCurrent();
        Log.Here.Info($"Sync saved: {name}");
    }

    private void RemoveSaved()
    {
        if (saved.SelectedItem is not SavedEntry { Job: { } j }) return;
        if (!Ui.Ask(this, F("Forget the saved sync \"{0}\"? Its folders and files aren't touched.", j.Name), T("Forget it"), T("Cancel"))) return;
        settings.Saved.Remove(j);
        Job.Name = "";
        settings.Save();
        LoadCurrent();
    }

    // ---------------------------------------------------------------- checks

    /// <summary>Why the job can't run (no source, the same folder twice, one inside the other), or null.</summary>
    public static string? Problem(SyncJob j)
    {
        if (j.Source.Trim().Length == 0) return T("Pick the folder to copy from (the source).");
        if (j.Destination.Trim().Length == 0) return T("Pick the folder to copy to (the destination).");
        string a, b;
        try { a = Path.GetFullPath(j.Source).TrimEnd('\\') + "\\"; b = Path.GetFullPath(j.Destination).TrimEnd('\\') + "\\"; }
        catch (Exception ex) { return ex.Message; }
        if (!Directory.Exists(a)) return F("The source folder {0} isn't there.", a.TrimEnd('\\'));
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return T("The source and the destination are the same folder.");
        if (b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return T("The destination is inside the source: pick a folder outside it.");
        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return T("The source is inside the destination: pick a folder outside it.");
        return null;
    }

    private void Synchronize()
    {
        Changed();
        if (plan == null || plan.NothingToDo) { UpdateButtons(); return; }
        if (Problem(Job) is { } p) { Error(p); return; }
        Remember();
        openSync(Job.Clone(), plan);
    }

    private void Error(string s)
    {
        status.Text = s;
        status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }

    // ---------------------------------------------------------------- comparing

    private async Task CompareAsync()
    {
        Changed();
        if (Problem(Job) is { } p) { Error(p); return; }
        Remember();
        comparing = new CancellationTokenSource();
        SetCompareButton(true);
        UpdateButtons();
        status.ClearValue(TextBlock.ForegroundProperty);
        var progress = new SyncPlan.Progress();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => status.Text = progress.Stage == "content"
            ? F("Comparing contents… {0} of {1} files", Sizes.Count(Interlocked.Read(ref progress.Compared)), Sizes.Count(progress.ToCompare))
            : F("Reading the folders… {0} in the source, {1} in the destination", Sizes.Count(Interlocked.Read(ref progress.LeftEntries)), Sizes.Count(Interlocked.Read(ref progress.RightEntries)));
        timer.Start();
        var job = Job.Clone();
        try
        {
            var result = await Task.Run(() => SyncPlan.Compare(job, progress, comparing.Token));
            plan = result;
            ShowFolders();
            Filter();
            UpdateChips();
            int copy = result.Count(SyncAction.Create), upd = result.Count(SyncAction.Update), del = result.Count(SyncAction.Delete);
            status.Text = result.NothingToDo
                ? F("In sync: nothing to copy or delete. Compared in {0}.", Sizes.Took(result.Took))
                : F("{0} to copy, {1} to update, {2} to delete · {3} to copy · compared in {4}.", Sizes.Count(copy), Sizes.Count(upd), Sizes.Count(del), Sizes.Bytes(result.BytesToCopy), Sizes.Took(result.Took));
            if (result.Unreadable.Count > 0) status.Text += " " + P(result.Unreadable.Count, "{0} folder couldn't be read.", "{0} folders couldn't be read.");
            Log.Here.Info($"Compared {job.Source} and {job.Destination} ({job.Mode}): {copy} new, {upd} changed, {del} to delete, in {result.Took.TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException) { status.Text = T("Comparing stopped."); }
        catch (Exception ex) { Error(F("Couldn't compare the folders: {0}", ex.Message)); }
        finally
        {
            timer.Stop();
            comparing = null;
            SetCompareButton(false);
            UpdateButtons();
        }
    }

    private void SetCompareButton(bool busy)
    {
        var panel = (StackPanel)compareButton.Content;
        ((TextBlock)panel.Children[0]).Text = busy ? "" : "";
        ((TextBlock)panel.Children[1]).Text = busy ? T("Stop") : T("Compare");
    }

    private void ClearResults()
    {
        if (plan == null) return;
        plan = null;
        grid.ItemsSource = null;
        UpdateChips();
        status.ClearValue(TextBlock.ForegroundProperty);
        status.Text = T("The folders or settings changed: Compare again to see what a sync would do.");
        ShowFolders();
        UpdateButtons();
    }

    private void UpdateChips()
    {
        foreach (var (a, chip) in chips)
        {
            int n = plan?.Count(a) ?? 0;
            chip.Content = plan == null ? (string)chip.Tag : $"{chip.Tag}  {Sizes.Count(n)}";
            chip.Visibility = a == SyncAction.Keep && Job.Mode == SyncMode.Mirror || a == SyncAction.Delete && Job.Mode == SyncMode.Update ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Filter()
    {
        if (plan == null) { grid.ItemsSource = null; return; }
        var show = chips.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToHashSet();
        grid.ItemsSource = plan.Items.Where(i => show.Contains(i.Action) && !(i.Action == SyncAction.Same && i.LeftDir)).Select(i => new SyncRow(i)).ToList();
    }

    private void BuildGrid()
    {
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = true;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.BorderThickness = new Thickness(0);
        grid.EnableRowVirtualization = true;
        grid.MinHeight = 160;
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");

        var name = new FrameworkElementFactory(typeof(StackPanel));
        name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(SyncRow.Icon)));
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.AppendChild(icon);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(SyncRow.Path)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.AppendChild(text);
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SyncRow.Path)));
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("File or folder"), CellTemplate = new DataTemplate { VisualTree = name }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 220, SortMemberPath = nameof(SyncRow.Path) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Source size"), Binding = new Binding(nameof(SyncRow.LeftSize)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Source modified"), Binding = new Binding(nameof(SyncRow.LeftTime)), Width = 130 });

        var act = new FrameworkElementFactory(typeof(StackPanel));
        act.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        act.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SyncRow.Tip)));
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new Binding(nameof(SyncRow.ActionGlyph)));
        glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SyncRow.ActionBrush)));
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        act.AppendChild(glyph);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(SyncRow.ActionText)));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        act.AppendChild(label);
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("Action"), CellTemplate = new DataTemplate { VisualTree = act }, Width = 95, SortMemberPath = nameof(SyncRow.ActionText) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Destination size"), Binding = new Binding(nameof(SyncRow.RightSize)), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Destination modified"), Binding = new Binding(nameof(SyncRow.RightTime)), Width = 140 });

        grid.MouseDoubleClick += (_, e) =>
        {
            if (grid.SelectedItem is not SyncRow r) return;
            string p = Path.Combine(r.Item.InLeft ? Job.Source : Job.Destination, r.Item.Path);
            Ui.ShowInExplorer(p);
        };
    }

    // ---------------------------------------------------------------- recent syncs

    public void ShowHistory()
    {
        history.Children.Clear();
        if (settings.History.Count == 0) { history.Children.Add(Ui.Secondary(T("None yet."))); return; }
        foreach (var run in settings.History.Take(4))
        {
            string what = run.Result switch { "stopped" => T("stopped"), "failed" => T("failed"), _ => run.Errors > 0 ? P(run.Errors, "{0} problem", "{0} problems") : T("done") };
            string name = run.Name.Length > 0 ? run.Name + ": " : "";
            var line = Ui.Secondary($"{run.Started:g} · {name}{run.Source} → {run.Destination} · " +
                F("{0} copied, {1} updated, {2} deleted, {3}", Sizes.Count(run.Copied), Sizes.Count(run.Updated), Sizes.Count(run.Deleted), Sizes.Bytes(run.Bytes)) + " · " + what);
            line.ToolTip = line.Text;
            if (run.Errors > 0 || run.Result != "done") line.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            history.Children.Add(line);
        }
    }

    /// <summary>A sync ran: its line in the recent ones, and the comparison is out of date.</summary>
    public void SyncRan()
    {
        ShowHistory();
        if (plan != null) ClearResults();
    }

    // ---------------------------------------------------------------- syncs in the background

    /// <summary>
    /// A sync whose window was sent to the background: a card above Recent syncs with its progress on one line. Click
    /// it to open it out (the graph and the readouts, Pause and Stop); Pop out, at its top right, brings its window back.
    /// </summary>
    public void ShowInBackground(SyncWindow w)
    {
        if (w.Monitor is not { } m) return;
        var chevron = Ui.Icon("\uE76C", 11);
        chevron.Margin = new Thickness(0, 0, 10, 0);
        var icon = Ui.Icon("\uE895", 15);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var title = new TextBlock { Text = T("Sync") + " · " + w.Caption, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var bar = Ui.Bar(6);
        bar.Maximum = 1;
        bar.Width = 160;
        bar.VerticalAlignment = VerticalAlignment.Center;
        bar.Margin = new Thickness(14, 0, 10, 0);
        var line = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var popOut = Ui.IconButton("\uE8A7", T("Pop out"), T("Back to the sync's own window"), (_, _) => w.PopOut());
        popOut.Margin = new Thickness(10, 0, 0, 0);

        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = T("Click to show or hide its graph") };
        DockPanel.SetDock(popOut, Dock.Right);
        head.Children.Add(popOut);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { bar, line } };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, icon, title } });

        var pause = new Button { Content = w.IsPaused ? T("Resume") : T("Pause"), MinWidth = 90 };
        var stop = new Button { Content = T("Stop"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var dismiss = new Button { Content = T("Close"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, ToolTip = T("Close the finished sync (it's in Recent syncs)") };
        pause.Click += (_, _) => { w.TogglePause(); pause.Content = w.IsPaused ? T("Resume") : T("Pause"); };
        stop.Click += (_, _) => w.AskStop(this);
        dismiss.Click += (_, _) => w.Close();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { pause, stop, dismiss } };
        var details = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        if (w.State is { } state) details.Children.Add(new SyncProgressView(m, () => state));
        details.Children.Add(actions);

        var card = Ui.Card(new StackPanel { Children = { head, details } }, new Thickness(0, 12, 0, 0));
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, popOut)) return;
            bool open = details.Visibility != Visibility.Visible;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "\uE70D" : "\uE76C";
        };
        string? ended = null;
        void Show()
        {
            bar.Value = m.Fraction;
            line.Text = ended ?? (Sizes.Percent(m.Fraction) + "  " + (m.Paused ? T("paused") : Sizes.Speed(m.Rate)) + (m.Left is { } l && !m.Paused ? "  " + F("{0} left", Sizes.Duration(l)) : ""));
        }
        void Remove()
        {
            m.Updated -= Show;
            background.Children.Remove(card);
        }
        m.Updated += Show;
        w.PoppedOut += Remove;
        w.Closed += (_, _) => Remove();
        w.Ended += result =>
        {
            ended = result switch
            {
                "stopped" => T("Stopped"),
                "failed" => T("Failed"),
                _ => m.Errors > 0 ? P(m.Errors, "Done, {0} problem", "Done, {0} problems") : T("Done"),
            } + " · " + Sizes.Bytes(m.Done) + " · " + Sizes.Duration(TimeSpan.FromSeconds(m.Seconds));
            if (result != "done" || m.Errors > 0) line.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            pause.Visibility = stop.Visibility = Visibility.Collapsed;
            dismiss.Visibility = Visibility.Visible;
            Show();
        };
        Show();
        background.Children.Add(card);
    }

    private static bool IsInside(DependencyObject d, DependencyObject parent)
    {
        for (var p = d; p != null; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p == parent) return true;
        return false;
    }

    public void Shutdown() => comparing?.Cancel();
}
