using System.ComponentModel;
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

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// Disk Usage (Tools tab): pick a drive or folder and Scan; the folder tree (largest first, or sorted by any column),
/// the file types, and the treemap. Right-click anything to open it, show it in Explorer, copy its path, scan just it or
/// move it to the Recycle Bin.
/// </summary>
internal sealed class DiskUsageView : DockPanel
{
    private const int MaxRows = 2000; // a folder's entries shown at once; the rest as "… and N more"

    private readonly DiskUsageSettings settings;
    private readonly ComboBox pathBox = new() { IsEditable = true, Width = 380, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button scan, browse;
    private readonly ToggleButton fast, mapToggle;
    private readonly TextBlock status = Ui.Secondary();
    private readonly ProgressBar bar = Ui.Bar(4);
    private readonly DataGrid grid = new();
    private readonly DataGrid typesGrid = new();
    private readonly Treemap map = new();
    private readonly TextBlock hover = Ui.Secondary();
    private readonly TextBlock empty;
    private readonly Button showAll;
    private readonly RowDefinition mapRow, splitRow;
    private readonly ColumnDefinition typesColumn;
    private readonly BulkCollection<TreeRow> rows = new();
    private readonly HashSet<int> expanded = new();
    private DiskTree? tree;
    private FileTypes? types;
    private CancellationTokenSource? scanning;
    private string sortKey = "Size";
    private ListSortDirection sortDirection = ListSortDirection.Descending;

    public DiskUsageView(DiskUsageSettings settings)
    {
        this.settings = settings;

        // ---- the bar at the top
        foreach (var d in Drives()) pathBox.Items.Add(d);
        pathBox.Text = settings.Path;
        pathBox.ItemTemplate = DriveTemplate();
        pathBox.ToolTip = T("A drive, or any folder (type or paste its path)");
        pathBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) StartScan(); };
        pathBox.SelectionChanged += (_, _) => { if (pathBox.SelectedItem is Drive d) pathBox.Text = d.Path; };
        browse = Ui.IconButton("", T("Browse…"), T("Pick a folder to scan"), (_, _) =>
        {
            if (Ui.PickFolder(T("Scan which folder?"), pathBox.Text) is { } f) { pathBox.Text = f; StartScan(); }
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        scan = Ui.IconButton("", T("Scan"), T("Read the drive or folder and show what takes the space"), (_, _) => { if (scanning != null) scanning.Cancel(); else StartScan(); }, accent: true);
        scan.Margin = new Thickness(8, 0, 0, 0);
        scan.MinWidth = 96;
        fast = Ui.Chip(T("Fast scan"), settings.UseFileTable,
            T("Read an NTFS drive's file table directly, as WizTree does: a whole drive in seconds. Windows asks for administrator permission each time (unless Daisy's App already has it). Off, or on other drives, folders are read one by one (several at once)."));
        fast.Margin = new Thickness(12, 0, 0, 0);
        fast.VerticalAlignment = VerticalAlignment.Center;
        fast.Click += (_, _) => { settings.UseFileTable = fast.IsChecked == true; settings.Save(); };
        mapToggle = Ui.Chip(T("Treemap"), settings.ShowTreemap, T("Show the treemap under the folders"));
        mapToggle.VerticalAlignment = VerticalAlignment.Center;
        mapToggle.Click += (_, _) => { settings.ShowTreemap = mapToggle.IsChecked == true; settings.Save(); ShowMap(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { pathBox, browse, scan, fast } };
        DockPanel.SetDock(mapToggle, Dock.Right);
        top.Children.Add(mapToggle);
        top.Children.Add(left);
        SetDock(top, Dock.Top);
        Children.Add(top);
        bar.Margin = new Thickness(0, 6, 0, 0);
        bar.Visibility = Visibility.Collapsed;
        var statusPanel = new StackPanel { Margin = new Thickness(2, 0, 0, 10), Children = { status, bar } };
        SetDock(statusPanel, Dock.Top);
        Children.Add(statusPanel);

        // ---- the folder tree
        BuildGrid();
        empty = new TextBlock { Text = T("Pick a drive or a folder at the top, then Scan."), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var treeCard = Ui.Card(new Grid { Children = { grid, empty } });
        treeCard.Padding = new Thickness(0);

        // ---- the file types
        BuildTypesGrid();
        showAll = new Button { Content = T("Show all types"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 6, 0, 8), Visibility = Visibility.Collapsed };
        showAll.Click += (_, _) => { typesGrid.SelectedItem = null; map.Highlight = -1; showAll.Visibility = Visibility.Collapsed; };
        var typesHead = Ui.Header(T("File types"));
        typesHead.Margin = new Thickness(12, 10, 12, 4);
        var typesPanel = new DockPanel();
        DockPanel.SetDock(typesHead, Dock.Top);
        DockPanel.SetDock(showAll, Dock.Bottom);
        typesPanel.Children.Add(typesHead);
        typesPanel.Children.Add(showAll);
        typesPanel.Children.Add(typesGrid);
        var typesCard = Ui.Card(typesPanel);
        typesCard.Padding = new Thickness(0);

        // ---- the treemap
        var mapPanel = new DockPanel();
        hover.Margin = new Thickness(2, 6, 0, 0);
        DockPanel.SetDock(hover, Dock.Bottom);
        mapPanel.Children.Add(hover);
        var mapBorder = new Border { Child = map, CornerRadius = new CornerRadius(4), ClipToBounds = true };
        mapBorder.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        mapPanel.Children.Add(mapBorder);
        map.Hovered += n => hover.Text = n >= 0 && tree != null ? $"{tree.PathOf(n)}  ·  {Sizes.Bytes(tree.Size[n])}" : "";
        map.Clicked += Reveal;
        map.RightClicked += n => { Reveal(n); Menu(n).IsOpen = true; };

        // ---- laid out: tree | types over the treemap
        typesColumn = new ColumnDefinition { Width = new GridLength(Math.Clamp(settings.TypesWidth, 200, 700)) };
        splitRow = new RowDefinition { Height = new GridLength(8) };
        mapRow = new RowDefinition { Height = new GridLength(Math.Clamp(settings.TreemapHeight, 120, 900)), MinHeight = 100 };
        var body = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 }, new ColumnDefinition { Width = new GridLength(8) }, typesColumn },
            RowDefinitions = { new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 }, splitRow, mapRow },
        };
        body.Children.Add(treeCard);
        var vsplit = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(vsplit, 1);
        body.Children.Add(vsplit);
        Grid.SetColumn(typesCard, 2);
        body.Children.Add(typesCard);
        var hsplit = new GridSplitter { Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(hsplit, 1);
        Grid.SetColumnSpan(hsplit, 3);
        body.Children.Add(hsplit);
        Grid.SetRow(mapPanel, 2);
        Grid.SetColumnSpan(mapPanel, 3);
        body.Children.Add(mapPanel);
        vsplit.DragCompleted += (_, _) => { settings.TypesWidth = typesColumn.ActualWidth; settings.Save(); };
        hsplit.DragCompleted += (_, _) => { settings.TreemapHeight = mapRow.ActualHeight; settings.Save(); };
        Children.Add(body);
        ShowMap();
        Status(T("Nothing scanned yet."));
    }

    private void ShowMap()
    {
        bool on = settings.ShowTreemap;
        mapRow.Height = on ? new GridLength(Math.Clamp(settings.TreemapHeight, 120, 900)) : new GridLength(0);
        mapRow.MinHeight = on ? 100 : 0;
        splitRow.Height = new GridLength(on ? 8 : 0);
        map.Show(on ? tree : null, n => types?.ColorOf(n) ?? FileTypes.FolderColor, n => types != null && n < types.TypeOf.Length ? types.TypeOf[n] : -1);
    }

    // ---------------------------------------------------------------- drives

    private sealed record Drive(string Path, string Label, long Total, long Free)
    {
        public override string ToString() => Path;
        public string Detail => Total > 0 ? F("{0} free of {1}", Sizes.Bytes(Free), Sizes.Bytes(Total)) : "";
        public double Used => Total > 0 ? 1 - (double)Free / Total : 0;
    }

    private static IEnumerable<Drive> Drives()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            Drive? item = null;
            try { if (d.IsReady) item = new Drive(d.RootDirectory.FullName, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace); }
            catch { }
            if (item != null) yield return item;
        }
    }

    private static DataTemplate DriveTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var path = new FrameworkElementFactory(typeof(TextBlock));
        path.SetBinding(TextBlock.TextProperty, new Binding(nameof(Drive.Path)));
        path.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        path.SetValue(TextBlock.WidthProperty, 40.0);
        panel.AppendChild(path);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Drive.Label)));
        label.SetValue(TextBlock.WidthProperty, 130.0);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(label);
        var track = new FrameworkElementFactory(typeof(ProgressBar));
        track.SetValue(ProgressBar.WidthProperty, 70.0);
        track.SetValue(ProgressBar.HeightProperty, 6.0);
        track.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        track.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        track.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        track.SetValue(ProgressBar.MaximumProperty, 1.0);
        track.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(Drive.Used)) { Mode = BindingMode.OneWay });
        track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        track.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(track);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(Drive.Detail)));
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.AppendChild(detail);
        return new DataTemplate { VisualTree = panel };
    }

    // ---------------------------------------------------------------- scanning

    private async void StartScan()
    {
        string path = pathBox.Text.Trim().Trim('"');
        if (path.Length == 2 && path[1] == ':') path += "\\";
        if (path.Length == 0) return;
        try { path = Path.GetFullPath(path); } catch { }
        if (!Directory.Exists(path)) { Status(F("There's no folder {0}.", path), error: true); return; }
        settings.Path = path;
        settings.Save();
        pathBox.Text = path;

        scanning = new CancellationTokenSource();
        var cancel = scanning.Token;
        SetBusy(true);
        // the last scan's memory, before the next one takes as much again
        tree = null; types = null;
        rows.ReplaceAll([]);
        typesGrid.ItemsSource = null;
        map.Show(null, _ => 0, _ => -1);
        GC.Collect();

        var progress = new Scanner.Progress();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => ShowProgress(progress, path, DateTime.UtcNow - started);
        timer.Start();
        try
        {
            var r = await Scanner.ScanAsync(path, settings.UseFileTable, progress, cancel);
            timer.Stop();
            progress.Stage = "types";
            Status(T("Sorting out the file types…"));
            var t = r.Tree;
            var ft = await Task.Run(() => FileTypes.Of(t, cancel), cancel);
            tree = t;
            types = ft;
            expanded.Clear();
            expanded.Add(0);
            Rebuild();
            typesGrid.ItemsSource = ft.Types;
            showAll.Visibility = Visibility.Collapsed;
            map.Highlight = -1;
            ShowMap();
            empty.Visibility = Visibility.Collapsed;
            if (rows.Count > 0) grid.SelectedIndex = 0;
            string summary = F("{0}: {1} in {2} files and {3} folders.", t.RootPath, Sizes.Bytes(t.Size[0]), Sizes.Count(t.Files[0]), Sizes.Count(t.Folders[0])) + " " +
                (t.Source == "mft" ? F("Read in {0} from the drive's file table.", Sizes.Took(r.Took)) : F("Read in {0} folder by folder.", Sizes.Took(r.Took)));
            if (t.VolumeTotal > 0) summary += " " + F("The drive has {0} free of {1}.", Sizes.Bytes(t.VolumeFree), Sizes.Bytes(t.VolumeTotal));
            if (r.Note != null) summary += " " + r.Note;
            Status(summary);
            Log.Here.Info($"Scanned {t.RootPath} {t.Source}: {t.Count:#,0} entries, {t.Size[0]:#,0} bytes in {r.Took.TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException) { Status(T("Scan stopped.")); empty.Visibility = Visibility.Visible; }
        catch (Exception ex)
        {
            Log.Here.Error($"Scanning {path} failed", ex);
            Status(F("Couldn't scan {0}: {1}", path, ex.Message), error: true);
            empty.Visibility = Visibility.Visible;
        }
        finally
        {
            timer.Stop();
            scanning = null;
            SetBusy(false);
        }
    }

    private void ShowProgress(Scanner.Progress p, string path, TimeSpan took)
    {
        string text = p.Stage switch
        {
            "elevating" => T("Waiting for Windows' permission to read the drive's file table…"),
            "table" => p.RecordsTotal > 0 ? F("Reading {0}'s file table… {1} ({2} of {3} entries)", path[..2], Sizes.Percent((double)p.Records / p.RecordsTotal), Sizes.Count(p.Records), Sizes.Count(p.RecordsTotal)) : T("Reading the drive's file table…"),
            "loading" or "building" => T("Putting the folders together…"),
            "folders" => F("Reading folders… {0} files and {1} folders, {2}", Sizes.Count(Interlocked.Read(ref p.Files)), Sizes.Count(Interlocked.Read(ref p.Folders)), Sizes.Bytes(Interlocked.Read(ref p.Bytes))),
            _ => T("Scanning…"),
        };
        Status(text + "  " + Sizes.Duration(took));
        if (p.Stage == "table" && p.RecordsTotal > 0) { bar.IsIndeterminate = false; bar.Maximum = 1; bar.Value = (double)p.Records / p.RecordsTotal; }
        else bar.IsIndeterminate = true;
    }

    private void SetBusy(bool busy)
    {
        ((TextBlock)((StackPanel)scan.Content).Children[1]).Text = busy ? T("Stop") : T("Scan");
        ((TextBlock)((StackPanel)scan.Content).Children[0]).Text = busy ? "" : "";
        bar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        bar.IsIndeterminate = true;
        pathBox.IsEnabled = browse.IsEnabled = fast.IsEnabled = !busy;
        if (busy) empty.Visibility = Visibility.Collapsed;
    }

    private void Status(string text, bool error = false)
    {
        status.Text = text;
        if (error) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);
        status.ToolTip = text;
    }

    public void Shutdown() => scanning?.Cancel();

    // ---------------------------------------------------------------- the tree's rows

    private void BuildGrid()
    {
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = true;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.BorderThickness = new Thickness(0);
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.CanUserSortColumns = true;
        grid.EnableRowVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        grid.ItemsSource = rows;
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("Name"), CellTemplate = NameTemplate(), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 220, SortMemberPath = "Name" });
        grid.Columns.Add(new DataGridTemplateColumn { Header = T("% of folder"), CellTemplate = BarTemplate(nameof(TreeRow.BarWidth), nameof(TreeRow.PercentText), 70), Width = 130, SortMemberPath = "Size" });
        grid.Columns.Add(Number(T("Size"), nameof(TreeRow.SizeText), "Size", 95));
        grid.Columns.Add(Number(T("Size on disk"), nameof(TreeRow.AllocText), "Alloc", 95));
        grid.Columns.Add(Number(T("Files"), nameof(TreeRow.FilesText), "Files", 85));
        grid.Columns.Add(Number(T("Folders"), nameof(TreeRow.FoldersText), "Folders", 75));
        grid.Columns.Add(new DataGridTextColumn { Header = T("Modified"), Binding = new Binding(nameof(TreeRow.ModifiedText)), Width = 150, SortMemberPath = "Modified" });
        grid.Columns[2].SortDirection = ListSortDirection.Descending;
        grid.Sorting += (_, e) =>
        {
            e.Handled = true;
            string key = e.Column.SortMemberPath;
            var dir = key == sortKey
                ? (sortDirection == ListSortDirection.Descending ? ListSortDirection.Ascending : ListSortDirection.Descending)
                : key == "Name" ? ListSortDirection.Ascending : ListSortDirection.Descending;
            sortKey = key;
            sortDirection = dir;
            foreach (var c in grid.Columns) c.SortDirection = null;
            e.Column.SortDirection = dir;
            var keep = grid.SelectedItem as TreeRow;
            Rebuild();
            if (keep != null) SelectNode(keep.Node);
        };
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is TreeRow { IsMore: false } r) map.Select(r.Node); };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (Row(e.OriginalSource) is not { } r) return;
            if (r.IsDir) Toggle(r);
            else if (!r.IsMore && tree != null) Ui.Open(tree.PathOf(r.Node));
        };
        grid.PreviewMouseRightButtonDown += (_, e) => { if (RowElement(e.OriginalSource) is { } row) row.IsSelected = true; };
        grid.ContextMenuOpening += (_, e) =>
        {
            if (grid.SelectedItem is not TreeRow { IsMore: false } r) { e.Handled = true; return; }
            grid.ContextMenu = Menu(r.Node);
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (grid.SelectedItem is not TreeRow r) return;
            switch (e.Key)
            {
                case Key.Right when r.IsDir && !r.Expanded: Toggle(r); e.Handled = true; break;
                case Key.Left when r.IsDir && r.Expanded: Toggle(r); e.Handled = true; break;
                case Key.Left when r.Depth > 0 && tree != null: SelectNode(r.IsMore ? r.MoreOf : tree.Parent[r.Node]); e.Handled = true; break;
                case Key.Enter when r.IsDir: Toggle(r); e.Handled = true; break;
                case Key.Delete when !r.IsMore && r.Node > 0: Delete(r.Node); e.Handled = true; break;
            }
        };
    }

    private static DataGridTextColumn Number(string header, string path, string sort, double width)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)));
        return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width, SortMemberPath = sort, ElementStyle = style };
    }

    private DataTemplate NameTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(TreeRow.Indent)));
        panel.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(TreeRow.Tip)));
        var expander = new FrameworkElementFactory(typeof(TextBlock));
        expander.SetBinding(TextBlock.TextProperty, new Binding(nameof(TreeRow.Expander)));
        expander.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        expander.SetValue(TextBlock.FontSizeProperty, 9.0);
        expander.SetValue(TextBlock.WidthProperty, 18.0);
        expander.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        expander.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        expander.SetValue(TextBlock.CursorProperty, Cursors.Hand);
        expander.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
        {
            if ((s as FrameworkElement)?.DataContext is TreeRow r && r.IsDir) { Toggle(r); e.Handled = true; }
        }));
        panel.AppendChild(expander);
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(TreeRow.Icon)));
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetValue(TextBlock.FontSizeProperty, 14.0);
        icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 7, 0));
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.AppendChild(icon);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(TreeRow.Name)));
        name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(name);
        return new DataTemplate { VisualTree = panel };
    }

    private static DataTemplate BarTemplate(string widthPath, string textPath, double track)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var back = new FrameworkElementFactory(typeof(Border));
        back.SetValue(FrameworkElement.WidthProperty, track);
        back.SetValue(FrameworkElement.HeightProperty, 10.0);
        back.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        back.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        back.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        var fill = new FrameworkElementFactory(typeof(Border));
        fill.SetBinding(FrameworkElement.WidthProperty, new Binding(widthPath));
        fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        back.AppendChild(fill);
        panel.AppendChild(back);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    /// <summary>A folder's entries in the chosen order (deleted ones left out).</summary>
    private List<int> Ordered(int node)
    {
        var t = tree!;
        var list = new List<int>(t.ChildCount[node]);
        for (int c = t.FirstChild[node], end = c + t.ChildCount[node]; c < end; c++) if (!t.IsDeleted(c)) list.Add(c);
        bool descending = sortDirection == ListSortDirection.Descending;
        if (sortKey == "Size" && descending) return list; // (as built: largest first)
        Comparison<int> cmp = sortKey switch
        {
            "Name" => (a, b) => t.NameSpan(a).CompareTo(t.NameSpan(b), StringComparison.CurrentCultureIgnoreCase),
            "Alloc" => (a, b) => t.Alloc[a].CompareTo(t.Alloc[b]),
            "Files" => (a, b) => t.Files[a].CompareTo(t.Files[b]),
            "Folders" => (a, b) => t.Folders[a].CompareTo(t.Folders[b]),
            "Modified" => (a, b) => t.Modified[a].CompareTo(t.Modified[b]),
            _ => (a, b) => t.Size[a].CompareTo(t.Size[b]),
        };
        list.Sort(cmp);
        if (descending) list.Reverse();
        if (sortKey == "Name") list = list.Where(t.IsDir).Concat(list.Where(c => !t.IsDir(c))).ToList(); // folders first
        return list;
    }

    /// <summary>The rows under a folder (and under its open folders), at <paramref name="depth"/>.</summary>
    private List<TreeRow> RowsUnder(int node, int depth)
    {
        var t = tree!;
        var result = new List<TreeRow>();
        var kids = Ordered(node);
        int shown = Math.Min(kids.Count, MaxRows);
        for (int k = 0; k < shown; k++)
        {
            int c = kids[k];
            bool open = expanded.Contains(c) && t.IsDir(c);
            result.Add(new TreeRow(t, c, depth, open));
            if (open) result.AddRange(RowsUnder(c, depth + 1));
        }
        if (kids.Count > shown)
        {
            long size = 0, alloc = 0;
            for (int k = shown; k < kids.Count; k++) { size += t.Size[kids[k]]; alloc += t.Alloc[kids[k]]; }
            result.Add(new TreeRow(t, node, depth, false) { MoreOf = node, MoreCount = kids.Count - shown, MoreSize = size, MoreAlloc = alloc });
        }
        return result;
    }

    private void Rebuild()
    {
        if (tree == null) { rows.ReplaceAll([]); return; }
        var list = new List<TreeRow> { new(tree, 0, 0, expanded.Contains(0)) };
        if (expanded.Contains(0)) list.AddRange(RowsUnder(0, 1));
        rows.ReplaceAll(list);
    }

    private void Toggle(TreeRow r)
    {
        if (tree == null || !r.IsDir || tree.ChildCount[r.Node] == 0) return;
        int i = rows.IndexOf(r);
        if (i < 0) return;
        if (r.Expanded)
        {
            expanded.Remove(r.Node);
            int end = i + 1;
            while (end < rows.Count && rows[end].Depth > r.Depth) end++;
            r.Expanded = false;
            rows.RemoveRange(i + 1, end - i - 1);
        }
        else
        {
            expanded.Add(r.Node);
            r.Expanded = true;
            rows.InsertRange(i + 1, RowsUnder(r.Node, r.Depth + 1));
        }
        r.Refresh();
        grid.SelectedItem = r;
        grid.ScrollIntoView(r);
    }

    /// <summary>Shows an entry in the tree (its folders opened), selected.</summary>
    private void Reveal(int node)
    {
        if (tree == null) return;
        for (int p = node == 0 ? -1 : tree.Parent[node]; p >= 0; p = tree.Parent[p]) expanded.Add(p);
        Rebuild();
        SelectNode(node);
    }

    private void SelectNode(int node)
    {
        if (tree == null) return;
        // the entry, or its "… and N more", or the nearest folder above it that's shown
        for (int n = node; n >= 0; n = tree.Parent[n])
        {
            var row = rows.FirstOrDefault(r => r.Node == n && !r.IsMore) ?? (n != node ? null : rows.FirstOrDefault(r => r.IsMore && r.MoreOf == tree.Parent[node]));
            if (row == null) continue;
            grid.SelectedItem = row;
            grid.ScrollIntoView(row);
            map.Select(node);
            return;
        }
    }

    private static DataGridRow? RowElement(object source)
    {
        for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is DataGridRow row) return row;
        return null;
    }

    private static TreeRow? Row(object source) => RowElement(source)?.Item as TreeRow;

    // ---------------------------------------------------------------- the file types

    private void BuildTypesGrid()
    {
        typesGrid.AutoGenerateColumns = false;
        typesGrid.IsReadOnly = true;
        typesGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        typesGrid.BorderThickness = new Thickness(0);
        typesGrid.SelectionMode = DataGridSelectionMode.Single;
        typesGrid.CanUserSortColumns = false;
        typesGrid.EnableRowVirtualization = true;
        typesGrid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(FrameworkElement.WidthProperty, 12.0);
        swatch.SetValue(FrameworkElement.HeightProperty, 12.0);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        swatch.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        swatch.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(FileType.Swatch)));
        panel.AppendChild(swatch);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileType.Name)));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(name);
        typesGrid.Columns.Add(new DataGridTemplateColumn { Header = T("Type"), CellTemplate = new DataTemplate { VisualTree = panel }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 90 });
        typesGrid.Columns.Add(Number(T("Size"), nameof(FileType.SizeText), "", 80));
        typesGrid.Columns.Add(new DataGridTemplateColumn { Header = "%", CellTemplate = BarTemplate(nameof(FileType.BarWidth), nameof(FileType.PercentText), 40), Width = 105 });
        typesGrid.Columns.Add(Number(T("Files"), nameof(FileType.FilesText), "", 75));
        typesGrid.SelectionChanged += (_, _) =>
        {
            if (types == null || typesGrid.SelectedItem is not FileType ft) return;
            map.Highlight = types.Types.IndexOf(ft);
            showAll.Visibility = Visibility.Visible;
        };
    }

    // ---------------------------------------------------------------- an entry's menu

    private ContextMenu Menu(int node)
    {
        var m = new ContextMenu();
        if (tree == null) return m;
        string path = tree.PathOf(node);
        bool dir = tree.IsDir(node);
        MenuItem Item(string text, string glyph, Action click)
        {
            var mi = new MenuItem { Header = text, Icon = Ui.Icon(glyph, 13) };
            mi.Click += (_, _) => click();
            m.Items.Add(mi);
            return mi;
        }
        Item(dir ? T("Open the folder") : T("Open"), "", () => Ui.Open(path));
        Item(T("Show in Explorer"), "", () => Ui.ShowInExplorer(path));
        Item(T("Copy the path"), "", () => { try { Clipboard.SetText(path); } catch { } });
        if (dir && node > 0) Item(T("Scan just this folder"), "", () => { pathBox.Text = path; StartScan(); });
        if (node > 0)
        {
            m.Items.Add(new Separator());
            Item(T("Delete…"), "", () => Delete(node));
        }
        return m;
    }

    private void Delete(int node)
    {
        if (tree == null || node <= 0) return;
        var t = tree;
        string path = t.PathOf(node);
        bool recycle = CanRecycle(path);
        string what = t.IsDir(node)
            ? F("{0}\n\nThis folder and everything in it: {1} in {2} files.", path, Sizes.Bytes(t.Size[node]), Sizes.Count(t.Files[node]))
            : F("{0}\n\n{1}", path, Sizes.Bytes(t.Size[node]));
        string ask = recycle ? T("Move this to the Recycle Bin?") : T("This drive has no Recycle Bin: delete this for good?");
        if (!Ui.Ask(this, ask + "\n\n" + what, recycle ? T("Move to the Recycle Bin") : T("Delete for good"), T("Cancel"), danger: true)) return;
        int r = Native.Recycle([path]);
        bool gone = !File.Exists(path) && !Directory.Exists(path);
        if (!gone)
        {
            Log.Here.Warn($"Couldn't delete {path} (shell error {r})");
            Status(F("Couldn't delete {0}: it may be open in a program, or need administrator rights.", path), error: true);
            return;
        }
        Log.Here.Info($"Deleted {path} ({t.Size[node]:#,0} bytes){(recycle ? " to the Recycle Bin" : "")}");
        int parent = t.Parent[node];
        t.Remove(node);
        Rebuild();
        SelectNode(parent);
        Status(recycle ? F("{0} went to the Recycle Bin.", path) : F("{0} was deleted.", path));
        var cancel = CancellationToken.None;
        Task.Run(() => FileTypes.Of(t, cancel)).ContinueWith(task =>
        {
            if (tree != t || task.Result is not { } ft) return;
            types = ft;
            typesGrid.ItemsSource = ft.Types;
            map.Highlight = -1;
            showAll.Visibility = Visibility.Collapsed;
            ShowMap();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static bool CanRecycle(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var d = new DriveInfo(Path.GetPathRoot(path)!);
            return d.DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }
}
