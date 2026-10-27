using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Shared;
using Microsoft.Win32;

namespace DaisysApp.Applets.Tools.Logs;

/// <summary>
/// One Logs tab: a source's entries in a table (newest first) with a time range, a level filter and a search, the
/// selected entry in full below, and Copy / Export. Clicking a column's header opens its menu: sort by it and, for Level
/// and Source, show or hide each value. Loads when it's first shown and on Refresh; Daisy's App's own logs
/// also add new lines as they're written, while the tab is showing.
/// </summary>
public partial class LogPage : UserControl, IDisposable
{
    private const int MaxShown = 100_000;
    private const int MaxValues = 300;

    private readonly LogsSettings settings;
    private readonly ObservableCollection<LogRow> shown = new();
    private readonly ConcurrentQueue<LogRow> incoming = new();
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private List<LogRow> all = new();
    private CancellationTokenSource? loadCts;
    private volatile bool showing;
    private bool loaded, stale, ready;
    private readonly LogSort sort;
    private readonly Dictionary<string, HashSet<string>> hidden = new();
    private readonly Dictionary<DataGridColumn, object> headers = new();

    public LogSource Source { get; }

    public LogPage(LogSource source, LogsSettings settings)
    {
        Source = source;
        this.settings = settings;
        InitializeComponent();
        Grid.ItemsSource = shown;
        DescriptionText.Text = source.Description;

        int hours = settings.RangeHours.TryGetValue(source.Key, out int h) ? h : source is AppLogSource ? 24 : 24;
        RangeBox.SelectedItem = RangeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == hours.ToString()) ?? RangeBox.Items[1];
        LevelBox.SelectedIndex = Math.Clamp(settings.LevelFilter.TryGetValue(source.Key, out int lv) ? lv : 0, 0, 3);
        sort = settings.Sort.TryGetValue(source.Key, out var so) ? so : new LogSort();
        if (settings.Hidden.TryGetValue(source.Key, out var hid))
            foreach (var (column, values) in hid) hidden[column] = new HashSet<string>(values, StringComparer.Ordinal);
        EventColumn.Visibility = source.HasEvents ? Visibility.Visible : Visibility.Collapsed;
        ShowHeaders();
        ready = true;
        if (source.Clear is { } clear)
        {
            ClearButton.Visibility = Visibility.Visible;
            ConfirmButton.Arm(ClearButton, () => ClearLog(clear));
        }

        liveTimer.Tick += (_, _) => DrainLive();
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); ApplyFilter(); };
        if (source.IsLive) Log.Written += OnWritten;
        IsVisibleChanged += (_, e) =>
        {
            showing = e.NewValue is true;
            if (showing)
            {
                if (!loaded || stale) _ = LoadAsync();
                if (Source.IsLive) liveTimer.Start();
            }
            else liveTimer.Stop();
        };
    }

    private DateTime Since => DateTime.Now.AddHours(RangeHours);
    private int RangeHours => -(RangeBox.SelectedItem is ComboBoxItem { Tag: string t } && int.TryParse(t, out int h) ? h : 24);

    private LogLevel MinLevel => LevelBox.SelectedIndex switch
    {
        1 => LogLevel.Info,
        2 => LogLevel.Warning,
        3 => LogLevel.Error,
        _ => LogLevel.Debug,
    };

    // ---------------------------------------------------------------- loading

    public async Task LoadAsync()
    {
        loadCts?.Cancel();
        var cts = loadCts = new CancellationTokenSource();
        loaded = true;
        stale = false;
        while (incoming.TryDequeue(out _)) { } // the load has them
        LoadingText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = T("Loading…");
        var since = Since;
        var sw = Stopwatch.StartNew();
        try
        {
            var rows = await Task.Run(() => Source.Load(since, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            all = rows;
            ApplyFilter();
            StatusText.Text = F("Loaded at {0:T} in {1:0.0} s.", DateTime.Now, sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            all = new();
            ApplyFilter();
            StatusText.Text = T("Windows doesn't let this log be read without administrator rights.");
        }
        catch (Exception ex)
        {
            all = new();
            ApplyFilter();
            StatusText.Text = T("Couldn't load the log: ") + ex.Message;
            Log.Here.Warn($"Couldn't load {Source.Key}", ex);
        }
        finally
        {
            if (loadCts == cts) LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private bool Passes(LogRow r, LogLevel min, string search)
    {
        if (r.Level < min) return false;
        foreach (var (column, set) in hidden)
            if (set.Count > 0 && set.Contains(ValueOf(r, column))) return false;
        return search.Length == 0
            || r.Message.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Source.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Event.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplyFilter()
    {
        var min = MinLevel;
        string search = SearchBox.Text.Trim();
        var selected = Grid.SelectedItem as LogRow;
        var rows = new List<LogRow>();
        int matches = 0;
        foreach (var r in all)
        {
            if (!Passes(r, min, search)) continue;
            matches++;
            if (rows.Count < MaxShown) rows.Add(r);
        }
        if (!IsNewestFirst) rows.Sort(Compare);
        shown.Clear();
        foreach (var r in rows) shown.Add(r);
        if (selected != null && shown.Contains(selected)) Grid.SelectedItem = selected;
        UpdateCount(matches);
        int flags = shown.Count(r => r.IsShutdownFlag);
        NextFlagButton.Visibility = flags > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextFlagText.Text = P(flags, "{0} improper shutdown", "{0} improper shutdowns");
    }

    /// <summary>Selects the next improper shutdown down the table (back to the top after the last), and shows it.</summary>
    private void NextFlag_Click(object sender, RoutedEventArgs e)
    {
        int from = Grid.SelectedItem is LogRow sel ? shown.IndexOf(sel) : -1;
        for (int k = 1; k <= shown.Count; k++)
        {
            var r = shown[(from + k) % shown.Count];
            if (!r.IsShutdownFlag) continue;
            Grid.SelectedItem = r;
            Grid.ScrollIntoView(r);
            Grid.Focus();
            return;
        }
    }

    private void UpdateCount(int matches)
    {
        CountText.Text = matches == all.Count
            ? P(all.Count, "{0:N0} entry", "{0:N0} entries")
            : F("{0:N0} of {1:N0} entries", matches, all.Count);
        if (matches > shown.Count) CountText.Text += " · " + F("showing the newest {0:N0}", shown.Count);
    }

    // ---------------------------------------------------------------- live

    private void OnWritten(string logName, LogEntry entry)
    {
        // any thread; never log from here (it would come straight back)
        if (!showing) { stale = true; return; }
        if (Source.Live(logName, entry) is { } row) incoming.Enqueue(row);
    }

    private void DrainLive()
    {
        if (incoming.IsEmpty || !loaded) return;
        var min = MinLevel;
        string search = SearchBox.Text.Trim();
        var batch = new List<LogRow>();
        while (incoming.TryDequeue(out var r)) batch.Add(r);
        all.InsertRange(0, Enumerable.Reverse(batch));
        foreach (var r in batch)
            if (Passes(r, min, search)) shown.Insert(InsertAt(r), r);
        while (shown.Count > MaxShown) shown.RemoveAt(shown.Count - 1);
        UpdateCount(shown.Count == all.Count ? all.Count : all.Count(r => Passes(r, min, search)));
    }

    // ---------------------------------------------------------------- column menus

    private bool IsNewestFirst => sort.Column == "Time" && sort.Descending;

    private static string ValueOf(LogRow r, string column) => column == "Level" ? r.Level.ToString() : r.Source;

    private static long EventNumber(LogRow r) => long.TryParse(r.Event, out long n) ? n : -1;

    /// <summary>The tab's sort; ties newest first.</summary>
    private int Compare(LogRow a, LogRow b)
    {
        int c = sort.Column switch
        {
            "Level" => a.Level.CompareTo(b.Level),
            "Source" => string.Compare(a.Source, b.Source, StringComparison.CurrentCultureIgnoreCase),
            "Event" => EventNumber(a).CompareTo(EventNumber(b)),
            _ => a.Time.CompareTo(b.Time),
        };
        if (sort.Descending) c = -c;
        return c != 0 ? c : b.Time.CompareTo(a.Time);
    }

    /// <summary>Where a new row goes in the table: before the first it doesn't sort after.</summary>
    private int InsertAt(LogRow r)
    {
        int lo = 0, hi = shown.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Compare(r, shown[mid]) <= 0) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    private string? KeyOf(DataGridColumn column) =>
        column == TimeColumn ? "Time" : column == LevelColumn ? "Level" : column == SourceColumn ? "Source" : column == EventColumn ? "Event" : null;

    /// <summary>A column header's menu: its sorts and, for Level and Source, a tick box per value (Message has none).</summary>
    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true; // the menu sorts
        if (KeyOf(e.Column) is not { } key) return;
        var menu = new ContextMenu { MaxHeight = SystemParameters.WorkArea.Height * 0.7, PlacementTarget = HeaderOf(e.Column), Placement = PlacementMode.Bottom };
        MenuItem Item(string text, Action click, bool staysOpen = false)
        {
            var m = new MenuItem { Header = text, StaysOpenOnClick = staysOpen };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
            return m;
        }
        void SortItem(string text, bool descending) =>
            Item(text, () => SortBy(key, descending)).IsChecked = sort.Column == key && sort.Descending == descending;
        switch (key)
        {
            case "Time": SortItem(T("Newest first"), true); SortItem(T("Oldest first"), false); break;
            case "Level": SortItem(T("Errors first"), true); SortItem(T("Debug first"), false); break;
            case "Event": SortItem(T("Sort low to high"), false); SortItem(T("Sort high to low"), true); break;
            default: SortItem(T("Sort A to Z"), false); SortItem(T("Sort Z to A"), true); break;
        }

        if (key is "Level" or "Source")
        {
            menu.Items.Add(new Separator());
            if (!hidden.TryGetValue(key, out var set)) hidden[key] = set = new HashSet<string>(StringComparer.Ordinal);
            var counts = all.GroupBy(r => ValueOf(r, key)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var v in set) counts.TryAdd(v, 0); // hidden ones stay listed, to show them again
            var values = key == "Level"
                ? counts.Keys.OrderByDescending(v => Enum.TryParse<LogLevel>(v, out var l) ? (int)l : -1).ToList()
                : counts.Keys.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
            var boxes = new List<(MenuItem Item, string Value)>();
            Item(T("Show all"), () => { set.Clear(); foreach (var b in boxes) b.Item.IsChecked = true; FiltersChanged(); }, staysOpen: true);
            Item(T("Hide all"), () => { foreach (var b in boxes) { set.Add(b.Value); b.Item.IsChecked = false; } FiltersChanged(); }, staysOpen: true);
            menu.Items.Add(new Separator());
            foreach (var v in values.Take(MaxValues))
            {
                string label = key == "Level" && Enum.TryParse<LogLevel>(v, out var l) ? LogRow.LevelName(l) : v.Length == 0 ? T("(blank)") : v;
                var m = new MenuItem { Header = $"{label}  ({counts[v]:N0})", IsCheckable = true, IsChecked = !set.Contains(v), StaysOpenOnClick = true };
                m.Click += (_, _) =>
                {
                    if (m.IsChecked) set.Remove(v); else set.Add(v);
                    FiltersChanged();
                };
                boxes.Add((m, v));
                menu.Items.Add(m);
            }
            if (values.Count > MaxValues)
                menu.Items.Add(new MenuItem { Header = F("…and {0} more", values.Count - MaxValues), IsEnabled = false });
        }
        menu.IsOpen = true;
    }

    private DataGridColumnHeader? HeaderOf(DataGridColumn column)
    {
        var todo = new Stack<DependencyObject>([Grid]);
        while (todo.Count > 0)
        {
            var d = todo.Pop();
            if (d is DataGridColumnHeader h && h.Column == column) return h;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) todo.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        return null;
    }

    private void SortBy(string column, bool descending)
    {
        sort.Column = column;
        sort.Descending = descending;
        settings.Sort[Source.Key] = sort;
        ShowHeaders();
        ApplyFilter();
        if (Grid.SelectedItem is { } sel) Grid.ScrollIntoView(sel);
        else if (shown.Count > 0) Grid.ScrollIntoView(shown[0]);
    }

    private void FiltersChanged()
    {
        settings.Hidden[Source.Key] = hidden.Where(h => h.Value.Count > 0).ToDictionary(h => h.Key, h => h.Value.ToList());
        ShowHeaders();
        ApplyFilter();
    }

    /// <summary>The sorted column's arrow, and a filter mark on a column with values hidden.</summary>
    private void ShowHeaders()
    {
        foreach (var column in Grid.Columns)
        {
            string? key = KeyOf(column);
            column.SortDirection = key != sort.Column ? null
                : sort.Descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
            if (!headers.ContainsKey(column)) headers[column] = column.Header;
            if (key == null || !hidden.TryGetValue(key, out var set) || set.Count == 0) { column.Header = headers[column]; continue; }
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = headers[column]?.ToString() ?? "" });
            var mark = new TextBlock { Text = "\uE71C", FontSize = 11, Margin = new Thickness(5, 2, 0, 0) };
            mark.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            panel.Children.Add(mark);
            column.Header = panel;
        }
    }

    // ---------------------------------------------------------------- toolbar

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        settings.RangeHours[Source.Key] = -RangeHours;
        if (loaded) _ = LoadAsync();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        settings.LevelFilter[Source.Key] = LevelBox.SelectedIndex;
        ApplyFilter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not LogRow r)
        {
            DetailsBox.Text = "";
            return;
        }
        var head = new List<string> { r.TimeText, r.LevelText, r.Source };
        if (r.Event.Length > 0) head.Add(F("event {0}", r.Event));
        if (r.Thread.Length > 0) head.Add(F("thread {0}", r.Thread));
        DetailsBox.Text = string.Join("   ", head) + Environment.NewLine + Environment.NewLine + r.Message;
    }

    /// <summary>The selected entries if more than one is selected, otherwise everything shown; in the table's order.</summary>
    private List<LogRow> RowsToCopy()
    {
        if (Grid.SelectedItems.Count > 1)
        {
            var picked = Grid.SelectedItems.Cast<LogRow>().ToHashSet();
            return shown.Where(picked.Contains).ToList();
        }
        return shown.ToList();
    }

    private string Header(int count) =>
        $"{Source.Title} · {P(count, "{0:N0} entry", "{0:N0} entries")} · {AppPaths.DisplayName} {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version} · {Environment.MachineName} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRows(RowsToCopy());

    private void GridCopy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CopyRows(Grid.SelectedItems.Count > 0 ? shown.Where(Grid.SelectedItems.Cast<LogRow>().ToHashSet().Contains).ToList() : RowsToCopy());
        e.Handled = true;
    }

    private void CopyRows(List<LogRow> rows)
    {
        if (rows.Count == 0) { StatusText.Text = T("Nothing to copy."); return; }
        var sb = new StringBuilder(Header(rows.Count)).AppendLine().AppendLine();
        foreach (var r in rows) sb.AppendLine(r.ToText());
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(sb.ToString());
                StatusText.Text = P(rows.Count, "Copied {0:N0} entry to the clipboard.", "Copied {0:N0} entries to the clipboard.");
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5) { Thread.Sleep(50); } // another app has the clipboard open
            catch (Exception ex)
            {
                StatusText.Text = T("Couldn't copy: ") + ex.Message;
                return;
            }
        }
    }

    private void ClearLog(Func<int> clear)
    {
        loadCts?.Cancel();
        while (incoming.TryDequeue(out _)) { }
        int files = clear();
        all = new();
        ApplyFilter();
        DetailsBox.Text = "";
        StatusText.Text = P(files, "Cleared: {0} file deleted.", "Cleared: {0} files deleted.");
        Log.Here.Info($"Cleared {Source.Key}: {files} files deleted");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var zip = Source.ZipExport;
        string safe = string.Concat(Source.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c)).Replace("--", "-");
        var dialog = new SaveFileDialog
        {
            Title = T("Export the log"),
            FileName = $"{safe}-{DateTime.Now:yyyy-MM-dd-HHmm}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Filter = T("Text file (the entries shown)") + "|*.txt|" + T("CSV file (the entries shown)") + "|*.csv"
                     + (zip != null ? "|" + T("Zip of the entries shown, with Windows' crash records (to send for help)") + "|*.zip" : ""),
            FilterIndex = zip != null ? 3 : 1,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string path = dialog.FileName;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip" && zip != null) zip(path, shown.ToList());
            else
            {
                var rows = shown.ToList();
                var sb = new StringBuilder();
                if (ext == ".csv")
                {
                    sb.AppendLine("Time,Level,Source,Event,Message");
                    foreach (var r in rows) sb.AppendLine(r.ToCsv());
                }
                else
                {
                    sb.AppendLine(Header(rows.Count)).AppendLine();
                    foreach (var r in rows) sb.AppendLine(r.ToText());
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            Log.Here.Info($"Exported {Source.Key} to {path}");
            StatusText.Text = F("Saved {0}", path);
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            StatusText.Text = T("Couldn't export: ") + ex.Message;
            Log.Here.Warn($"Couldn't export {Source.Key}", ex);
        }
    }

    public void Dispose()
    {
        Log.Written -= OnWritten;
        liveTimer.Stop();
        loadCts?.Cancel();
    }
}
