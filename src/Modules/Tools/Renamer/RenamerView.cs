using System.Collections;
using System.ComponentModel;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Tools.Renamer;

/// <summary>
/// Renamer (Tools tab), like Bulk Rename Utility: a folder's files (and folders) in a list with their new names as you
/// change the panels under it, live; tick the ones to rename, then Rename. Names that clash, or that Windows won't take,
/// are shown in red and left. Undo puts the last rename back.
/// </summary>
internal sealed class RenamerView : DockPanel
{
    private const int MaxItems = 50_000;

    private readonly RenamerSettings settings;
    private RenameOptions options;
    private readonly ComboBox folderBox = new() { IsEditable = true, MinWidth = 300 };
    private readonly TextBox mask = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton files, folders, subfolders, hidden;
    private readonly DataGrid grid = new();
    private readonly TextBlock listStatus = Ui.Secondary();
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly Grid panels = new() { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition() } };
    private readonly StackPanel[] columns = [new(), new(), new()];
    private readonly Dictionary<int, TextBlock> panelHeads = new();
    private readonly Dictionary<int, Border> panelCards = new();
    private readonly Button renameButton, undoButton;
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DataGridColumn folderColumn;
    private List<RenameItem> items = new();
    private ListCollectionView? view;
    private int previewVersion;
    private bool busy;

    public RenamerView(RenamerSettings settings)
    {
        this.settings = settings;
        options = settings.Options;
        AllowDrop = true;
        Drop += OnDrop;
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); _ = PreviewAsync(); };

        // ---- the folder
        foreach (var f in settings.RecentFolders) folderBox.Items.Add(f);
        folderBox.Text = settings.Folder;
        folderBox.ToolTip = T("The folder whose files to rename (type or paste a path, or drop a folder here)");
        folderBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = LoadAsync(); };
        folderBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (folderBox.SelectedItem is string) _ = LoadAsync(); }, DispatcherPriority.Background);
        var browse = Ui.IconButton("", T("Browse…"), null, (_, _) =>
        {
            if (Ui.PickFolder(T("Rename the files in which folder?"), folderBox.Text) is { } f) { folderBox.Text = f; _ = LoadAsync(); }
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var up = Ui.IconButton("", "", T("The folder above"), (_, _) =>
        {
            try { if (Directory.GetParent(folderBox.Text.TrimEnd('\\')) is { } p) { folderBox.Text = p.FullName; _ = LoadAsync(); } } catch { }
        });
        up.Margin = new Thickness(8, 0, 0, 0);
        var refresh = Ui.IconButton("", "", T("Read the folder again (F5)"), (_, _) => _ = LoadAsync());
        refresh.Margin = new Thickness(8, 0, 0, 0);
        var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, up, refresh } };
        DockPanel.SetDock(folderButtons, Dock.Right);
        folderRow.Children.Add(folderButtons);
        folderRow.Children.Add(folderBox);
        SetDock(folderRow, Dock.Top);
        Children.Add(folderRow);

        // ---- which entries (Bulk Rename Utility's Filters panel)
        mask.Text = settings.Mask;
        mask.ToolTip = T("Which files to list: names with * and ?, several separated by ; (e.g. *.jpg; *.png)");
        mask.LostFocus += (_, _) => { if (settings.Mask != mask.Text) { settings.Mask = mask.Text; settings.Save(); _ = LoadAsync(); } };
        mask.KeyDown += (_, e) => { if (e.Key == Key.Enter) { settings.Mask = mask.Text; settings.Save(); _ = LoadAsync(); } };
        files = Ui.Chip(T("Files"), settings.ShowFiles, T("List the files"));
        folders = Ui.Chip(T("Folders"), settings.ShowFolders, T("List the folders too, to rename them"));
        subfolders = Ui.Chip(T("Subfolders"), settings.Subfolders, T("List what's in the folders inside it too"));
        hidden = Ui.Chip(T("Hidden"), settings.ShowHidden, T("List hidden and system files too"));
        foreach (var chip in new[] { files, folders, subfolders, hidden })
            chip.Click += (_, _) =>
            {
                settings.ShowFiles = files.IsChecked == true;
                settings.ShowFolders = folders.IsChecked == true;
                settings.Subfolders = subfolders.IsChecked == true;
                settings.ShowHidden = hidden.IsChecked == true;
                settings.Save();
                _ = LoadAsync();
            };
        var showLabel = Ui.Secondary(T("Show"));
        showLabel.VerticalAlignment = VerticalAlignment.Center;
        showLabel.Margin = new Thickness(0, 0, 8, 0);
        var all = new Button { Content = T("All"), Margin = new Thickness(18, 0, 0, 0), ToolTip = T("Tick every entry") };
        all.Click += (_, _) => Pick(_ => true);
        var none = new Button { Content = T("None"), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Untick every entry") };
        none.Click += (_, _) => Pick(_ => false);
        var invert = new Button { Content = T("Invert"), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Tick the unticked and untick the ticked") };
        invert.Click += (_, _) => Pick(i => !i.Picked);
        listStatus.VerticalAlignment = VerticalAlignment.Center;
        listStatus.Margin = new Thickness(14, 0, 0, 0);
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { showLabel, mask, Pad(files, 10), folders, subfolders, hidden, all, none, invert, listStatus } };
        SetDock(filterRow, Dock.Top);
        Children.Add(filterRow);

        // ---- the bottom bar
        var reset = Ui.IconButton("", T("Reset"), T("Put every panel back as it was (changing nothing)"), (_, _) => ResetPanels());
        undoButton = Ui.IconButton("", T("Undo"), T("Put back the names from the last rename"), (_, _) => _ = UndoAsync());
        undoButton.Margin = new Thickness(8, 0, 0, 0);
        renameButton = Ui.IconButton("", T("Rename"), T("Rename the ticked entries as the New name column shows"), (_, _) => _ = RenameAsync(), accent: true);
        renameButton.MinWidth = 110;
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { reset, undoButton } };
        DockPanel.SetDock(leftButtons, Dock.Left);
        DockPanel.SetDock(renameButton, Dock.Right);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(14, 0, 14, 0);
        bottom.Children.Add(leftButtons);
        bottom.Children.Add(renameButton);
        bottom.Children.Add(status);
        SetDock(bottom, Dock.Bottom);
        Children.Add(bottom);

        // ---- the list over the panels
        folderColumn = BuildGrid();
        for (int c = 0; c < columns.Length; c++)
        {
            Grid.SetColumn(columns[c], c);
            panels.Children.Add(columns[c]);
        }
        BuildPanels();
        PlacePanels();
        var listCard = Ui.Card(grid);
        listCard.Padding = new Thickness(0);
        var panelsScroll = new ScrollViewer { Content = panels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var panelsRow = new RowDefinition { Height = new GridLength(Math.Clamp(settings.PanelsHeight, 150, 900)), MinHeight = 120 };
        var body = new Grid { RowDefinitions = { new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 }, new RowDefinition { Height = new GridLength(8) }, panelsRow } };
        body.Children.Add(listCard);
        var split = new GridSplitter { Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        split.DragCompleted += (_, _) => { settings.PanelsHeight = panelsRow.ActualHeight; settings.Save(); };
        Grid.SetRow(split, 1);
        body.Children.Add(split);
        Grid.SetRow(panelsScroll, 2);
        body.Children.Add(panelsScroll);
        Children.Add(body);

        PreviewKeyDown += (_, e) => { if (e.Key == Key.F5) { _ = LoadAsync(); e.Handled = true; } };
        ShowUndo();
        Loaded += (_, _) => { if (items.Count == 0 && settings.Folder.Length > 0 && Directory.Exists(settings.Folder)) _ = LoadAsync(); };
        status.Text = T("Pick a folder, then change the panels: the New name column shows the result as you go.");
    }

    private static UIElement Pad(FrameworkElement e, double left) { e.Margin = new Thickness(left, 0, 6, 0); return e; }

    // ---------------------------------------------------------------- the list

    private DataGridColumn BuildGrid()
    {
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = true;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.BorderThickness = new Thickness(0);
        grid.EnableRowVirtualization = true;
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");

        var tick = new FrameworkElementFactory(typeof(CheckBox));
        tick.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(RenameItem.Picked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn { Header = "", CellTemplate = new DataTemplate { VisualTree = tick }, Width = 40, CanUserSort = false });

        var name = new FrameworkElementFactory(typeof(StackPanel));
        name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameItem.Icon)));
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.AppendChild(icon);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameItem.Name)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.AppendChild(text);
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("Name"), CellTemplate = new DataTemplate { VisualTree = name }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 180, SortMemberPath = "Name" });

        var newName = new FrameworkElementFactory(typeof(TextBlock));
        newName.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameItem.NewName)));
        newName.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(RenameItem.NewBrush)));
        newName.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(RenameItem.Note)));
        newName.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        newName.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        newName.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("New name"), CellTemplate = new DataTemplate { VisualTree = newName }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 180, SortMemberPath = "NewName" });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Size"), Binding = new Binding(nameof(RenameItem.SizeText)), Width = 90, SortMemberPath = "Size" });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Modified"), Binding = new Binding(nameof(RenameItem.ModifiedText)), Width = 150, SortMemberPath = "Modified" });
        var folder = new DataGridTextColumn { Header = T("Folder"), Binding = new Binding(nameof(RenameItem.Folder)), Width = 180, SortMemberPath = "Folder" };
        grid.Columns.Add(folder);

        // the list's order is the numbering's order: sorted naturally ("2" before "10"), and the preview follows
        grid.Sorting += (_, e) =>
        {
            e.Handled = true;
            if (view == null) return;
            var dir = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            foreach (var c in grid.Columns) c.SortDirection = null;
            e.Column.SortDirection = dir;
            view.CustomSort = new ItemOrder(e.Column.SortMemberPath, dir == ListSortDirection.Descending);
            SchedulePreview();
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || grid.SelectedItems.Count == 0) return;
            bool to = !grid.SelectedItems.Cast<RenameItem>().All(i => i.Picked);
            foreach (RenameItem i in grid.SelectedItems) i.Picked = to;
            e.Handled = true;
        };
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is RenameItem i) Ui.ShowInExplorer(i.Path); };
        return folder;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    /// <summary>As Explorer sorts: numbers by their value ("2" before "10").</summary>
    private sealed class ItemOrder(string key, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not RenameItem a || y is not RenameItem b) return 0;
            int r = key switch
            {
                "NewName" => StrCmpLogicalW(a.NewName, b.NewName),
                "Size" => a.Size.CompareTo(b.Size),
                "Modified" => a.Modified.CompareTo(b.Modified),
                "Folder" => StrCmpLogicalW(a.Folder, b.Folder) is var f && f != 0 ? f : StrCmpLogicalW(a.Name, b.Name),
                _ => StrCmpLogicalW(a.Name, b.Name),
            };
            return descending ? -r : r;
        }
    }

    private void Pick(Func<RenameItem, bool> picked)
    {
        foreach (var i in items) i.Picked = picked(i);
        SchedulePreview();
    }

    private async Task LoadAsync()
    {
        string root = folderBox.Text.Trim().Trim('"');
        if (root.Length == 2 && root[1] == ':') root += "\\";
        if (root.Length == 0) return;
        try { root = Path.GetFullPath(root); } catch { }
        if (!Directory.Exists(root))
        {
            status.Text = F("There's no folder {0}.", root);
            status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            return;
        }
        status.ClearValue(TextBlock.ForegroundProperty);
        settings.Folder = root;
        FileSyncRecent(root);
        settings.Save();
        var keep = items.Where(i => !i.Picked).Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool sub = settings.Subfolders, showFiles = settings.ShowFiles, showFolders = settings.ShowFolders, showHidden = settings.ShowHidden;
        var masks = settings.Mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        listStatus.Text = T("Reading the folder…");
        bool capped = false;
        List<RenameItem> list;
        try
        {
            list = await Task.Run(() =>
            {
                var result = new List<RenameItem>();
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = sub, IgnoreInaccessible = true, ReturnSpecialDirectories = false,
                    AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                };
                foreach (var fsi in new DirectoryInfo(root).EnumerateFileSystemInfos("*", opts))
                {
                    bool dir = fsi is DirectoryInfo;
                    if (dir ? !showFolders : !showFiles) continue;
                    if (fsi.Name.StartsWith(".daisyrename-", StringComparison.Ordinal)) continue;
                    if (!dir && masks.Length > 0 && !masks.Any(m => FileSystemName.MatchesSimpleExpression(m, fsi.Name))) continue;
                    string rel = Path.GetRelativePath(root, Path.GetDirectoryName(fsi.FullName)!);
                    result.Add(new RenameItem
                    {
                        Path = fsi.FullName, Name = fsi.Name, Folder = rel == "." ? "" : rel, IsDir = dir,
                        Size = fsi is FileInfo fi ? fi.Length : 0, Modified = fsi.LastWriteTime, Created = fsi.CreationTime,
                    });
                    if (result.Count >= MaxItems) { capped = true; break; }
                }
                result.Sort((a, b) => StrCmpLogicalW(a.Folder, b.Folder) is var f && f != 0 ? f : a.IsDir != b.IsDir ? (a.IsDir ? -1 : 1) : StrCmpLogicalW(a.Name, b.Name));
                return result;
            });
        }
        catch (Exception ex)
        {
            listStatus.Text = "";
            status.Text = F("Couldn't read {0}: {1}", root, ex.Message);
            status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            return;
        }
        foreach (var i in list)
        {
            if (keep.Contains(i.Path)) i.Picked = false;
            i.PickedChanged += SchedulePreview;
        }
        items = list;
        view = new ListCollectionView(items);
        foreach (var c in grid.Columns) c.SortDirection = null;
        grid.ItemsSource = view;
        folderColumn.Visibility = sub ? Visibility.Visible : Visibility.Collapsed;
        listStatus.Text = capped ? F("The first {0} entries (there are more: narrow the list with Show)", Sizes.Count(MaxItems)) : "";
        await PreviewAsync();
    }

    private void FileSyncRecent(string root)
    {
        settings.RecentFolders.RemoveAll(f => f.Equals(root, StringComparison.OrdinalIgnoreCase));
        settings.RecentFolders.Insert(0, root);
        if (settings.RecentFolders.Count > 12) settings.RecentFolders.RemoveRange(12, settings.RecentFolders.Count - 12);
        string text = folderBox.Text;
        folderBox.Items.Clear();
        foreach (var f in settings.RecentFolders) folderBox.Items.Add(f);
        folderBox.Text = text;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        string p = paths[0];
        folderBox.Text = Directory.Exists(p) ? p : Path.GetDirectoryName(p) ?? p;
        _ = LoadAsync();
    }

    // ---------------------------------------------------------------- the preview

    private void SchedulePreview()
    {
        previewTimer.Stop();
        previewTimer.Start();
    }

    private async Task PreviewAsync()
    {
        int v = ++previewVersion;
        var o = options.Clone();
        var ordered = view?.Cast<RenameItem>().ToList() ?? new();
        settings.Options = options;
        settings.Save();
        foreach (var (n, head) in panelHeads)
        {
            if (o.ActivePanels().Contains(n)) head.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            else head.ClearValue(TextBlock.ForegroundProperty);
        }

        (string Name, RenameStatus Status, string? Note)[] results;
        string? patternError = null;
        try
        {
            results = await Task.Run(() =>
            {
                System.Text.RegularExpressions.Regex? rx = null;
                try { rx = RenameEngine.Pattern(o); }
                catch (ArgumentException ex) { patternError = ex.Message; }
                var r = new (string, RenameStatus, string?)[ordered.Count];
                long counter = 0;
                var perFolder = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < ordered.Count; k++)
                {
                    var it = ordered[k];
                    if (!it.Picked) { r[k] = ("", RenameStatus.NotPicked, null); continue; }
                    long number;
                    if (o.NumberPerFolder) { perFolder.TryGetValue(it.Directory, out number); perFolder[it.Directory] = number + 1; }
                    else number = counter++;
                    if (patternError != null) { r[k] = (it.Name, RenameStatus.Invalid, F("The RegEx (1) isn't valid: {0}", patternError)); continue; }
                    string name;
                    try { name = RenameEngine.NewName(it, o, number, rx); }
                    catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { r[k] = (it.Name, RenameStatus.Invalid, T("The RegEx (1) took too long on this name")); continue; }
                    if (name == it.Name) r[k] = (name, RenameStatus.Same, T("Unchanged"));
                    else if (RenameEngine.Invalid(name) is { } why) r[k] = (name, RenameStatus.Invalid, why);
                    else r[k] = (name, RenameStatus.Ok, null);
                }
                // two names the same, or a name that's taken by something staying
                var changing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < ordered.Count; k++) if (r[k].Item2 == RenameStatus.Ok) changing.Add(ordered[k].Path);
                var targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < ordered.Count; k++)
                {
                    if (r[k].Item2 is not (RenameStatus.Ok or RenameStatus.Same)) continue;
                    string target = Path.Combine(ordered[k].Directory, r[k].Item1);
                    targets[target] = targets.GetValueOrDefault(target) + 1;
                }
                for (int k = 0; k < ordered.Count; k++)
                {
                    if (r[k].Item2 != RenameStatus.Ok) continue;
                    var it = ordered[k];
                    string target = Path.Combine(it.Directory, r[k].Item1);
                    if (targets[target] > 1) r[k] = (r[k].Item1, RenameStatus.Conflict, T("Another entry would get this name too"));
                    else if (!target.Equals(it.Path, StringComparison.OrdinalIgnoreCase) && !changing.Contains(target) && (File.Exists(target) || Directory.Exists(target)))
                        r[k] = (r[k].Item1, RenameStatus.Conflict, T("Something with this name is already there"));
                }
                return r;
            });
        }
        catch (Exception ex)
        {
            Log.Here.Error("Renamer preview failed", ex);
            return;
        }
        if (v != previewVersion) return;

        Brush? Res(string key) => TryFindResource(key) as Brush;
        Brush? ok = Res("AccentBrush"), same = Res("TextSecondaryBrush"), bad = Res("ErrorTextBrush");
        int changes = 0, problems = 0, picked = 0;
        for (int k = 0; k < ordered.Count; k++)
        {
            var (name, st, note) = results[k];
            if (st != RenameStatus.NotPicked) picked++;
            if (st == RenameStatus.Ok) changes++;
            if (st is RenameStatus.Conflict or RenameStatus.Invalid) problems++;
            ordered[k].Show(name, st, note, st switch { RenameStatus.Ok => ok, RenameStatus.Same => same, RenameStatus.NotPicked => null, _ => bad });
        }
        status.ClearValue(TextBlock.ForegroundProperty);
        if (patternError != null)
        {
            status.Text = F("The RegEx (1) isn't valid: {0}", patternError);
            status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
        else
            status.Text = (items.Count == 0 ? T("Nothing listed.") : F("{0} of {1} ticked entries get a new name.", Sizes.Count(changes), Sizes.Count(picked)))
                + (problems > 0 ? " " + P(problems, "{0} can't (red: point at it for why) and is left.", "{0} can't (red: point at them for why) and are left.") : "");
        listStatus.Text = items.Count == 0 ? "" : P(items.Count, "{0} entry listed", "{0} entries listed");
        renameButton.IsEnabled = changes > 0 && !busy;
    }

    // ---------------------------------------------------------------- renaming

    private async Task RenameAsync()
    {
        var pairs = items.Where(i => i.Picked && i.Status == RenameStatus.Ok)
            .Select(i => new RenamePair { From = i.Path, To = Path.Combine(i.Directory, i.NewName), IsDir = i.IsDir }).ToList();
        if (pairs.Count == 0) return;
        int left = items.Count(i => i.Status is RenameStatus.Conflict or RenameStatus.Invalid);
        string ask = P(pairs.Count, "Rename {0} entry?", "Rename {0} entries?") + (left > 0 ? " " + P(left, "{0} in red is left as it is.", "{0} in red are left as they are.") : "")
            + "\n\n" + T("Undo puts the names back afterwards.");
        if (!Ui.Ask(this, ask, T("Rename"), T("Cancel"))) return;
        busy = true;
        renameButton.IsEnabled = undoButton.IsEnabled = false;
        status.Text = T("Renaming…");
        var result = await Task.Run(() => RenameRunner.Run(pairs, deepestFirst: true));
        busy = false;
        settings.LastRename = result.Done;
        settings.LastRenameAt = DateTime.Now;
        settings.Save();
        Log.Here.Info($"Renamed {result.Done.Count} in {settings.Folder} ({result.Failed.Count} failed)");
        await LoadAsync();
        Done(P(result.Done.Count, "Renamed {0} entry.", "Renamed {0} entries."), result);
        ShowUndo();
    }

    private async Task UndoAsync()
    {
        var last = settings.LastRename;
        if (last.Count == 0) return;
        string when = settings.LastRenameAt is { } at ? $" ({at:g})" : "";
        if (!Ui.Ask(this, P(last.Count, "Put back the name of the {0} entry renamed last?", "Put back the names of the {0} entries renamed last?") + when, T("Undo"), T("Cancel"))) return;
        busy = true;
        renameButton.IsEnabled = undoButton.IsEnabled = false;
        status.Text = T("Putting the names back…");
        var back = last.Select(p => new RenamePair { From = p.To, To = p.From, IsDir = p.IsDir }).Reverse().ToList();
        var result = await Task.Run(() => RenameRunner.Run(back, deepestFirst: false));
        busy = false;
        settings.LastRename = new();
        settings.LastRenameAt = null;
        settings.Save();
        Log.Here.Info($"Undid a rename: {result.Done.Count} put back ({result.Failed.Count} failed)");
        await LoadAsync();
        Done(P(result.Done.Count, "Put back {0} name.", "Put back {0} names."), result);
        ShowUndo();
    }

    private void Done(string text, RenameRunner.Result result)
    {
        if (result.Failed.Count == 0) { status.ClearValue(TextBlock.ForegroundProperty); status.Text = text; return; }
        status.Text = text + " " + P(result.Failed.Count, "{0} couldn't be:", "{0} couldn't be:") + " "
            + string.Join("; ", result.Failed.Take(3).Select(f => $"{Path.GetFileName(f.Path)} ({f.Error})")) + (result.Failed.Count > 3 ? "…" : "");
        status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }

    private void ShowUndo()
    {
        undoButton.IsEnabled = settings.LastRename.Count > 0 && !busy;
        undoButton.ToolTip = settings.LastRename.Count > 0
            ? P(settings.LastRename.Count, "Put back the name from the last rename ({0} entry)", "Put back the names from the last rename ({0} entries)")
            : T("Nothing to undo");
    }

    // ---------------------------------------------------------------- the panels

    private void ResetPanels()
    {
        options = new RenameOptions();
        settings.Options = options;
        panels.DataContext = options;
        SchedulePreview();
    }

    private void BuildPanels()
    {
        panels.DataContext = options;
        Binding.AddSourceUpdatedHandler(panels, (_, _) => SchedulePreview());

        Add(1, T("RegEx"),
            Text(T("Match"), nameof(RenameOptions.RegexMatch), T("A regular expression (.NET), e.g. ^(\\d+)-(.*)$")),
            Text(T("Replace"), nameof(RenameOptions.RegexReplace), T("What a match becomes: $1, $2… for its groups, e.g. $2 $1")),
            Checks((T("Include the extension"), nameof(RenameOptions.RegexIncludeExtension)), (T("Match case"), nameof(RenameOptions.RegexMatchCase))));
        Add(2, T("Name"),
            Choice(T("Name"), nameof(RenameOptions.NameMode), (T("Keep"), NameMode.Keep), (T("Remove"), NameMode.Remove), (T("Fixed"), NameMode.Fixed), (T("Reverse"), NameMode.Reverse)),
            Text(T("Fixed"), nameof(RenameOptions.FixedName), T("The name every entry gets (with Fixed): add Numbering (10) to tell them apart")));
        Add(3, T("Replace"),
            Text(T("Replace"), nameof(RenameOptions.Replace), T("Text to find in the name")),
            Text(T("With"), nameof(RenameOptions.ReplaceWith), T("What it becomes (empty to remove it)")),
            Checks((T("Match case"), nameof(RenameOptions.ReplaceMatchCase))));
        Add(4, T("Case"),
            Choice(T("Case"), nameof(RenameOptions.Case), (T("As it is"), CaseMode.Same), (T("lower case"), CaseMode.Lower), (T("UPPER CASE"), CaseMode.Upper), (T("Title Case"), CaseMode.Title), (T("Sentence case"), CaseMode.Sentence)),
            Text(T("Except"), nameof(RenameOptions.CaseExcept), T("Words kept exactly as you write them here, e.g. USB; DVD; of")));
        Add(5, T("Remove"),
            Pair(Int(T("First"), nameof(RenameOptions.RemoveFirst), T("This many characters from the start")), Int(T("Last"), nameof(RenameOptions.RemoveLast), T("This many characters from the end"))),
            Pair(Int(T("From"), nameof(RenameOptions.RemoveFrom), T("The characters from this one (1 is the first)…")), Int(T("To"), nameof(RenameOptions.RemoveTo), T("…to this one"))),
            Text(T("Chars"), nameof(RenameOptions.RemoveChars), T("Each of these characters, wherever it is")),
            Text(T("Words"), nameof(RenameOptions.RemoveWords), T("These whole words (separated by spaces), wherever they are")),
            Choice(T("Crop"), nameof(RenameOptions.Crop), (T("No"), CropMode.None), (T("Before"), CropMode.Before), (T("After"), CropMode.After)),
            Text("", nameof(RenameOptions.CropText), T("Everything before (or after) this text goes; the text stays")),
            Checks((T("Digits"), nameof(RenameOptions.RemoveDigits)), (T("Symbols"), nameof(RenameOptions.RemoveSymbols)), (T("Accents"), nameof(RenameOptions.RemoveAccents)),
                (T("Non-English letters"), nameof(RenameOptions.RemoveHighChars)), (T("Double spaces"), nameof(RenameOptions.RemoveDoubleSpaces)), (T("Trim"), nameof(RenameOptions.RemoveTrim)), (T("Leading dots"), nameof(RenameOptions.RemoveLeadingDots))));
        Add(6, T("Move / Copy"),
            Choice(T("Part"), nameof(RenameOptions.MoveMode), (T("No"), MoveMode.None), (T("Copy the first"), MoveMode.CopyFirst), (T("Copy the last"), MoveMode.CopyLast), (T("Move the first"), MoveMode.MoveFirst), (T("Move the last"), MoveMode.MoveLast)),
            Int(T("Characters"), nameof(RenameOptions.MoveCount), T("How many characters")),
            Choice(T("To"), nameof(RenameOptions.MoveTo), (T("The start"), MoveTarget.Start), (T("The end"), MoveTarget.End), (T("Position"), MoveTarget.Position)),
            Pair(Int(T("Position"), nameof(RenameOptions.MovePosition), T("Where (with Position): 1 is before the first character; negative counts from the end")), Text(T("Sep."), nameof(RenameOptions.MoveSeparator), T("Put between the part and the rest"), 50)));
        Add(7, T("Add"),
            Text(T("Prefix"), nameof(RenameOptions.Prefix), T("Text at the start")),
            Pair(Text(T("Insert"), nameof(RenameOptions.Insert), T("Text put in at a position"), 80), Int(T("At"), nameof(RenameOptions.InsertAt), T("1 is before the first character; negative counts from the end (-1: after the last)"))),
            Text(T("Suffix"), nameof(RenameOptions.Suffix), T("Text at the end (before the extension)")),
            Checks((T("Word space"), nameof(RenameOptions.WordSpace))));
        Add(8, T("Auto Date"),
            Choice(T("Add"), nameof(RenameOptions.DatePlace), (T("No"), AddPlace.None), (T("Prefix"), AddPlace.Prefix), (T("Suffix"), AddPlace.Suffix)),
            Choice(T("Date"), nameof(RenameOptions.DateKind), (T("Modified"), DateKind.Modified), (T("Created"), DateKind.Created), (T("Photo taken"), DateKind.Taken), (T("Today"), DateKind.Now)),
            Format(T("Format"), nameof(RenameOptions.DateFormat)),
            Text(T("Sep."), nameof(RenameOptions.DateSeparator), T("Put between the date and the name")));
        Add(9, T("Append Folder Name"),
            Choice(T("Add"), nameof(RenameOptions.FolderPlace), (T("No"), AddPlace.None), (T("Prefix"), AddPlace.Prefix), (T("Suffix"), AddPlace.Suffix)),
            Text(T("Sep."), nameof(RenameOptions.FolderSeparator), T("Put between the folder's name and the file's")),
            Int(T("Levels"), nameof(RenameOptions.FolderLevels), T("How many folders up: 1 is the one it's in, 2 adds the one above that too")));
        Add(10, T("Numbering"),
            Choice(T("Mode"), nameof(RenameOptions.NumberMode), (T("No"), NumberMode.None), (T("Prefix"), NumberMode.Prefix), (T("Suffix"), NumberMode.Suffix), (T("Prefix and suffix"), NumberMode.Both), (T("Insert at"), NumberMode.Insert)),
            Pair(Int(T("Start at"), nameof(RenameOptions.NumberStart), T("The first entry's number")), Int(T("Step"), nameof(RenameOptions.NumberStep), T("Added for each next entry"))),
            Pair(Int(T("Digits"), nameof(RenameOptions.NumberPad), T("At least this many digits, with zeros in front (3: 001, 002…)")), Int(T("At"), nameof(RenameOptions.NumberAt), T("Where, with Insert at: 1 is before the first character"))),
            Text(T("Sep."), nameof(RenameOptions.NumberSeparator), T("Put between the number and the name")),
            Choice(T("Style"), nameof(RenameOptions.NumberStyle), ("1, 2, 3", NumberStyle.Decimal), ("1, …, 9, A", NumberStyle.Hex), ("a, b, c", NumberStyle.LowerLetters), ("A, B, C", NumberStyle.UpperLetters), ("I, II, III", NumberStyle.UpperRoman), ("i, ii, iii", NumberStyle.LowerRoman)),
            Checks((T("Start again in each folder"), nameof(RenameOptions.NumberPerFolder))));
        Add(11, T("Extension"),
            Choice(T("Extension"), nameof(RenameOptions.ExtensionMode), (T("As it is"), ExtensionMode.Same), (T("lower case"), ExtensionMode.Lower), (T("UPPER CASE"), ExtensionMode.Upper), (T("Title Case"), ExtensionMode.Title),
                (T("New"), ExtensionMode.New), (T("Extra"), ExtensionMode.Extra), (T("Remove"), ExtensionMode.Remove)),
            Text("", nameof(RenameOptions.ExtensionText), T("The new extension (New), or one added after it (Extra), without the dot")));
    }

    /// <summary>
    /// A panel: a card that starts closed (its title only); click the title to open or close it, or hold it and drag
    /// the card to another place, in any of the three columns.
    /// </summary>
    private void Add(int number, string title, params UIElement[] rows)
    {
        var chevron = Ui.Icon("\uE76C", 10);
        chevron.Margin = new Thickness(0, 0, 8, 0);
        var head = new TextBlock { Text = $"{number} · {title}", FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        panelHeads[number] = head;
        var grip = Ui.Icon("\uE76F", 12);
        grip.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        DockPanel.SetDock(grip, Dock.Right);
        var bar = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = T("Click to open or close; hold and drag to move it") };
        bar.Children.Add(grip);
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, head } });
        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        foreach (var r in rows) body.Children.Add(r);
        var card = Ui.Card(new StackPanel { Children = { bar, body } }, new Thickness(0, 0, 10, 10));
        card.Tag = number;
        panelCards[number] = card;
        void Toggle()
        {
            bool open = body.Visibility != Visibility.Visible;
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "\uE70D" : "\uE76C";
        }
        Draggable(bar, card, Toggle);
    }

    // ---------------------------------------------------------------- the panels' places

    /// <summary>The panels in their columns, as last left (or 1, 2, 3 across, 4, 5, 6 under them…).</summary>
    private void PlacePanels()
    {
        foreach (var col in columns) col.Children.Clear();
        var placed = new HashSet<int>();
        var saved = settings.PanelColumns;
        for (int c = 0; c < columns.Length && c < saved.Count; c++)
            foreach (int n in saved[c])
                if (panelCards.TryGetValue(n, out var card) && placed.Add(n)) columns[c].Children.Add(card);
        // any not placed yet (the first time, or a panel that's new): in the shortest column
        foreach (int n in panelCards.Keys.Order())
        {
            if (placed.Contains(n)) continue;
            var shortest = saved.Count == 0 ? columns[(n - 1) % columns.Length] : columns.MinBy(col => col.Children.Count)!;
            shortest.Children.Add(panelCards[n]);
        }
    }

    private void SavePlaces()
    {
        settings.PanelColumns = columns.Select(col => col.Children.OfType<Border>().Where(b => b.Tag is int).Select(b => (int)b.Tag).ToList()).ToList();
        settings.Save();
    }

    private readonly Border dropMark = new() { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 10), IsHitTestVisible = false };

    /// <summary>A click on the title opens or closes the panel; holding it and moving drags the card to another place.</summary>
    private void Draggable(FrameworkElement bar, Border card, Action toggle)
    {
        Point start = default;
        bool pressed = false, dragging = false;
        dropMark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        void End()
        {
            pressed = false;
            card.Opacity = 1;
            Mouse.OverrideCursor = null;
            if (dropMark.Parent is Panel p) p.Children.Remove(dropMark);
        }
        bar.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(panels);
            pressed = true;
            dragging = false;
            bar.CaptureMouse();
            e.Handled = true;
        };
        bar.MouseMove += (_, e) =>
        {
            if (!pressed) return;
            var at = e.GetPosition(panels);
            if (!dragging && (Math.Abs(at.X - start.X) > 5 || Math.Abs(at.Y - start.Y) > 5))
            {
                dragging = true;
                card.Opacity = 0.45;
                Mouse.OverrideCursor = Cursors.SizeAll;
            }
            if (!dragging) return;
            var (col, before) = DropTarget(at, card);
            if (dropMark.Parent is Panel old) old.Children.Remove(dropMark);
            int index = before != null ? col.Children.IndexOf(before) : col.Children.Count;
            col.Children.Insert(index, dropMark);
        };
        bar.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            bool moved = dragging;
            var at = e.GetPosition(panels);
            bar.ReleaseMouseCapture();
            End();
            if (!moved) { toggle(); return; }
            var (col, before) = DropTarget(at, card);
            ((Panel)card.Parent).Children.Remove(card);
            int index = before != null ? col.Children.IndexOf(before) : col.Children.Count;
            col.Children.Insert(index, card);
            SavePlaces();
        };
        bar.LostMouseCapture += (_, _) => { if (pressed && !dragging) pressed = false; else if (pressed) End(); };
    }

    /// <summary>Where a card dropped at <paramref name="at"/> goes: its column, and the card it goes above (null: at the bottom).</summary>
    private (StackPanel Column, Border? Before) DropTarget(Point at, Border dragged)
    {
        double width = Math.Max(1, panels.ActualWidth / columns.Length);
        var col = columns[Math.Clamp((int)(at.X / width), 0, columns.Length - 1)];
        foreach (var b in col.Children.OfType<Border>())
        {
            if (b == dragged || b == dropMark) continue;
            double top = b.TranslatePoint(new Point(0, 0), panels).Y;
            if (at.Y < top + b.ActualHeight / 2) return (col, b);
        }
        return (col, null);
    }

    private static Binding Bind(string path) => new(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, NotifyOnSourceUpdated = true };

    private static FrameworkElement Labeled(string label, FrameworkElement control, double labelWidth = 70)
    {
        var g = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var l = Ui.Secondary(label);
        l.Width = labelWidth;
        l.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(l, Dock.Left);
        g.Children.Add(l);
        g.Children.Add(control);
        return g;
    }

    private static FrameworkElement Text(string label, string path, string? tip = null, double width = double.NaN)
    {
        var box = new TextBox { ToolTip = tip, Width = width };
        box.SetBinding(TextBox.TextProperty, Bind(path));
        return Labeled(label, box, double.IsNaN(width) ? 70 : 34);
    }

    private static FrameworkElement Int(string label, string path, string? tip = null)
    {
        var box = new TextBox { ToolTip = tip, Width = 56, HorizontalAlignment = HorizontalAlignment.Left };
        box.SetBinding(TextBox.TextProperty, Bind(path));
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        return Labeled(label, box, 70);
    }

    private static FrameworkElement Pair(FrameworkElement a, FrameworkElement b)
    {
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        if (b is DockPanel { Children.Count: > 0 } d && d.Children[0] is TextBlock l) { l.Width = double.NaN; l.Margin = new Thickness(8, 0, 6, 0); }
        Grid.SetColumn(b, 1);
        g.Children.Add(a);
        g.Children.Add(b);
        return g;
    }

    private sealed record Option(string Text, object Value);

    private static FrameworkElement Choice(string label, string path, params (string Text, object Value)[] choices)
    {
        var box = new ComboBox { ItemsSource = choices.Select(c => new Option(c.Text, c.Value)).ToList(), DisplayMemberPath = nameof(Option.Text), SelectedValuePath = nameof(Option.Value) };
        box.SetBinding(Selector.SelectedValueProperty, Bind(path));
        return Labeled(label, box);
    }

    private static FrameworkElement Format(string label, string path)
    {
        var box = new ComboBox { IsEditable = true, ToolTip = T("How the date is written: yyyy year, MM month, dd day, HH hour, mm minutes, ss seconds (pick one or type your own)") };
        foreach (var f in new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy-MM-dd HH.mm.ss", "yyyyMMdd_HHmmss", "dd-MM-yyyy", "MM-dd-yyyy", "yyyy-MM" }) box.Items.Add(f);
        box.SetBinding(ComboBox.TextProperty, Bind(path));
        return Labeled(label, box);
    }

    private static FrameworkElement Checks(params (string Text, string Path)[] list)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (text, path) in list)
        {
            var c = new CheckBox { Content = text, Margin = new Thickness(0, 0, 12, 6) };
            c.SetBinding(ToggleButton.IsCheckedProperty, Bind(path));
            wrap.Children.Add(c);
        }
        return wrap;
    }
}
