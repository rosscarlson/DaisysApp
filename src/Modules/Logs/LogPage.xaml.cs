using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Logging;
using Microsoft.Win32;

namespace DaisysApp.Applets.Logs;

/// <summary>
/// One Logs tab: a source's entries in a table (newest first) with a time range, a level filter and a search, the
/// selected entry in full below, and Copy / Export. Loads when it's first shown and on Refresh; Daisy's App's own logs
/// also add new lines as they're written, while the tab is showing.
/// </summary>
public partial class LogPage : UserControl, IDisposable
{
    private const int MaxShown = 100_000;

    private readonly LogsSettings settings;
    private readonly ObservableCollection<LogRow> shown = new();
    private readonly ConcurrentQueue<LogRow> incoming = new();
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private List<LogRow> all = new();
    private CancellationTokenSource? loadCts;
    private volatile bool showing;
    private bool loaded, stale, ready;

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
        ready = true;

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

    private bool Passes(LogRow r, LogLevel min, string search) =>
        r.Level >= min && (search.Length == 0
            || r.Message.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Source.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || r.Event.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    private void ApplyFilter()
    {
        var min = MinLevel;
        string search = SearchBox.Text.Trim();
        var selected = Grid.SelectedItem as LogRow;
        shown.Clear();
        int matches = 0;
        foreach (var r in all)
        {
            if (!Passes(r, min, search)) continue;
            matches++;
            if (shown.Count < MaxShown) shown.Add(r);
        }
        if (selected != null && shown.Contains(selected)) Grid.SelectedItem = selected;
        UpdateCount(matches);
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
            if (Passes(r, min, search)) shown.Insert(0, r);
        while (shown.Count > MaxShown) shown.RemoveAt(shown.Count - 1);
        UpdateCount(shown.Count == all.Count ? all.Count : all.Count(r => Passes(r, min, search)));
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
                     + (zip != null ? "|" + T("Zip of every log file (to send for help)") + "|*.zip" : ""),
            FilterIndex = zip != null ? 3 : 1,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string path = dialog.FileName;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip" && zip != null) zip(path);
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
