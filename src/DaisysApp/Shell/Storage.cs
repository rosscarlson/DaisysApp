using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// What the app and its tools keep on disk as they run: the performance and sensor history, the logs, the USB monitor's
/// logs, ForzaWheel's driving sessions, the AI agent's conversations, iRacing's races, the EQ wizard's runs and downloaded updates. Each
/// kind with its files, size and oldest date; the ones ticked can have their files older than so many days deleted,
/// now or each day by themselves (Settings → General → Storage). Settings themselves are never touched.
/// </summary>
internal static class Storage
{
    /// <summary>One kind of data: where it is, which files are it, and what it is.</summary>
    public sealed record Kind(string Id, string Name, string About, string Folder, Func<FileInfo, bool> Is, bool Recursive);

    public sealed record Usage(Kind Kind, int Files, long Bytes, DateTime? Oldest);

    private static string Logs => AppPaths.LogFolder;

    public static IReadOnlyList<Kind> Kinds { get; } = new[]
    {
        new Kind("performance", T("Performance and sensor history"), T("Hardware → Performance's and Sensors' history (a file a day): the history graphs and the busiest processes over time"),
            Path.Combine(Logs, "performance"), _ => true, true),
        new Kind("logs", T("Logs"), T("What the app and each tool did, a file a day each (the oldest are deleted after 7 days anyway)"),
            Logs, f => f.Name != "running.marker" && !f.Name.StartsWith("usbmon_", StringComparison.OrdinalIgnoreCase)
                && !f.FullName.StartsWith(Path.Combine(Logs, "performance") + "\\", StringComparison.OrdinalIgnoreCase)
                && !f.FullName.StartsWith(Path.Combine(Logs, "network") + "\\", StringComparison.OrdinalIgnoreCase)
                && !f.FullName.StartsWith(Path.Combine(Logs, "diskmark") + "\\", StringComparison.OrdinalIgnoreCase)
                && !f.FullName.StartsWith(Path.Combine(Logs, "stresstests") + "\\", StringComparison.OrdinalIgnoreCase), true),
        new Kind("network", T("Network logs"), T("Support → Networking Tools' ping and speed test logs and its list of events"),
            Path.Combine(Logs, "network"), _ => true, false),
        new Kind("diskmark", T("Disk benchmarks"), T("Tools → DiskMark's results, a file per run"),
            Path.Combine(Logs, "diskmark"), _ => true, false),
        new Kind("stresstests", T("Stress test runs"), T("Support → Stress Tests' runs, a file each"),
            Path.Combine(Logs, "stresstests"), f => f.Name != "running.json", false),
        new Kind("maptiles", T("Map tiles"), T("Support → Networking Tools → Traceroute's map pieces, downloaded from OpenStreetMap"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "MapTiles"), _ => true, true),
        new Kind("usb", T("USB monitor logs"), T("Hardware → USB Monitor's log of every device change, a file each time the app starts"),
            Logs, f => f.Name.StartsWith("usbmon_", StringComparison.OrdinalIgnoreCase), false),
        new Kind("forza", T("Driving sessions"), T("ForzaWheel's logs of your inputs, which Forza Analysis analyses"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.DaisysAppName, "ForzaSessions"), _ => true, false),
        new Kind("ai", T("AI conversations"), T("The AI tab's conversations with Gemini, OpenAI, Ollama and other providers (Claude Code keeps its own, in your user folder's .claude)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "AI sessions"), _ => true, false),
        new Kind("iracing", T("iRacing races"), T("Gaming → iRacing's copy of your races, ratings and totals (deleted, the next sync fetches them all again)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "iRacing"), _ => true, false),
        new Kind("eq", T("EQ wizard runs"), T("Audio → Levels' EQ wizard: what each run measured"),
            Path.Combine(AppPaths.SettingsFolder, "Logs"), _ => true, true),
        new Kind("updates", T("Downloaded updates"), T("Installers the app downloaded to update itself"),
            Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-Update"), _ => true, false),
    };

    private static IEnumerable<FileInfo> FilesOf(Kind k)
    {
        var dir = new DirectoryInfo(k.Folder);
        if (!dir.Exists) return Enumerable.Empty<FileInfo>();
        try { return dir.EnumerateFiles("*", k.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(k.Is).ToList(); }
        catch { return Enumerable.Empty<FileInfo>(); }
    }

    public static Usage Measure(Kind k)
    {
        var files = FilesOf(k).ToList();
        return new Usage(k, files.Count, files.Sum(f => f.Length), files.Count == 0 ? null : files.Min(f => f.LastWriteTime));
    }

    /// <summary>Deletes the kind's files last written more than <paramref name="days"/> days ago (ones in use are left);
    /// how many, and how much, went.</summary>
    public static (int Files, long Bytes) Clean(Kind k, int days)
    {
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, days));
        int n = 0;
        long bytes = 0;
        foreach (var f in FilesOf(k).Where(f => f.LastWriteTime < cutoff))
        {
            try { long size = f.Length; f.Delete(); n++; bytes += size; } catch { }
        }
        // empty folders left behind (not the kind's own)
        if (k.Recursive && Directory.Exists(k.Folder))
            foreach (var d in Directory.EnumerateDirectories(k.Folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        if (n > 0) Log.App.Info($"Storage: {k.Id}: {n} files older than {days} days deleted ({bytes / 1048576.0:0.0} MB)");
        return (n, bytes);
    }

    /// <summary>"12.4 MB"</summary>
    public static string Size(long b) => b >= 1L << 30 ? F("{0:0.0} GB", b / 1073741824.0) : b >= 1 << 20 ? F("{0:0.0} MB", b / 1048576.0) : b >= 1024 ? F("{0:0} KB", b / 1024.0) : F("{0} bytes", b);

    private static DispatcherTimer? daily;

    /// <summary>The daily clean-up (Settings → General → Storage), if it's on: now (a little after the start), then each day.</summary>
    public static void StartAutoClean(AppSettings settings)
    {
        daily ??= new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        daily.Tick -= Tick;
        daily.Tick += Tick;
        daily.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        first.Tick += (_, _) => { first.Stop(); Tick(null, EventArgs.Empty); };
        first.Start();

        void Tick(object? s, EventArgs e)
        {
            int days = settings.AutoCleanDays;
            var ids = settings.AutoCleanKinds.ToList();
            if (days <= 0 || ids.Count == 0) return;
            _ = Task.Run(() =>
            {
                foreach (var k in Kinds.Where(k => ids.Contains(k.Id)))
                    try { Clean(k, days); } catch (Exception ex) { ErrorLog.Write($"Storage clean-up of {k.Id}", ex); }
            });
        }
    }
}

/// <summary>Settings → General → Storage: the table of what's kept, and deleting the old.</summary>
internal sealed class StoragePanel : StackPanel
{
    private readonly AppSettings settings;
    private readonly Grid table = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock total = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox days = new() { Width = 120, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox auto = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<string, CheckBox> ticks = new();
    private bool measuring;

    public StoragePanel(AppSettings settings)
    {
        this.settings = settings;
        var head = new DockPanel();
        var refresh = new Button { Content = T("Refresh"), Padding = new Thickness(10, 3, 10, 3) };
        refresh.Click += async (_, _) => await MeasureAsync();
        DockPanel.SetDock(refresh, Dock.Right);
        head.Children.Add(refresh);
        var title = new TextBlock { Text = T("Storage") };
        title.SetResourceReference(StyleProperty, "CardHeader");
        head.Children.Add(title);
        Children.Add(head);
        var about = new TextBlock { Text = T("What the app and its tools keep on disk as they run, and how much. Tick the ones to clean up and delete their files older than so many days, now or each day. Your settings are never touched."), TextWrapping = TextWrapping.Wrap };
        about.SetResourceReference(StyleProperty, "SecondaryText");
        Children.Add(about);

        foreach (var w in new[] { 26.0, double.NaN, 70, 90, 110, 70 })
            table.ColumnDefinitions.Add(double.IsNaN(w) ? new ColumnDefinition() : new ColumnDefinition { Width = new GridLength(w) });
        Children.Add(table);
        Children.Add(total);

        foreach (int d in new[] { 7, 14, 30, 60, 90, 180, 365 }) days.Items.Add(new ComboBoxItem { Content = P(d, "{0} day", "{0} days"), Tag = d });
        int keep = settings.AutoCleanDays > 0 ? settings.AutoCleanDays : 30;
        days.SelectedItem = days.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == keep) ?? days.Items[2];
        days.SelectionChanged += (_, _) => SaveAuto();
        var delete = new Button { Content = T("Delete now…"), Padding = new Thickness(12, 4, 12, 4) };
        delete.Click += async (_, _) => await DeleteAsync();
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = T("From the ticked ones, delete files older than"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(days);
        row.Children.Add(delete);
        Children.Add(row);
        auto.Content = new TextBlock { Text = T("Do this by itself each day (the ticked ones, older than the days above)"), TextWrapping = TextWrapping.Wrap };
        auto.IsChecked = settings.AutoCleanDays > 0;
        auto.Click += (_, _) => SaveAuto();
        Children.Add(auto);
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Children.Add(status);

        IsVisibleChanged += async (_, _) => { if (IsVisible) await MeasureAsync(); };
    }

    private int Days => days.SelectedItem is ComboBoxItem { Tag: int d } ? d : 30;
    private List<string> Ticked => ticks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToList();

    private void SaveAuto()
    {
        settings.AutoCleanDays = auto.IsChecked == true ? Days : 0;
        settings.AutoCleanKinds = Ticked;
        settings.Save();
    }

    private async Task MeasureAsync()
    {
        if (measuring) return;
        measuring = true;
        status.Text = T("Adding it up…");
        var measures = await Task.Run(() => Storage.Kinds.Select(Storage.Measure).ToList());
        measuring = false;
        status.Text = "";
        table.Children.Clear();
        table.RowDefinitions.Clear();
        void Cell(UIElement e, int row, int col, bool right = false)
        {
            if (e is FrameworkElement f) { f.VerticalAlignment = VerticalAlignment.Center; f.Margin = new Thickness(0, 3, 8, 3); if (right) f.HorizontalAlignment = HorizontalAlignment.Right; }
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            table.Children.Add(e);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (text, col) in new[] { (T("What"), 1), (T("Files"), 2), (T("Size"), 3), (T("Oldest"), 4) })
        {
            var h = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
            h.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Cell(h, 0, col, right: col is 2 or 3);
        }
        int r = 1;
        foreach (var m in measures)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool was = ticks.TryGetValue(m.Kind.Id, out var old) ? old.IsChecked == true : settings.AutoCleanKinds.Contains(m.Kind.Id);
            var tick = new CheckBox { IsChecked = was, IsEnabled = m.Files > 0 || was };
            tick.Click += (_, _) => SaveAuto();
            ticks[m.Kind.Id] = tick;
            Cell(tick, r, 0);
            Cell(new TextBlock { Text = m.Kind.Name, ToolTip = m.Kind.About + "\n" + m.Kind.Folder, TextWrapping = TextWrapping.Wrap }, r, 1);
            Cell(new TextBlock { Text = m.Files.ToString("N0") }, r, 2, right: true);
            Cell(new TextBlock { Text = Storage.Size(m.Bytes), FontWeight = m.Bytes > 50L << 20 ? FontWeights.SemiBold : FontWeights.Normal }, r, 3, right: true);
            Cell(new TextBlock { Text = m.Oldest is { } o ? o.ToString("d") : "–" }, r, 4);
            var open = new Button { Content = T("Open"), Padding = new Thickness(8, 1, 8, 1), IsEnabled = Directory.Exists(m.Kind.Folder) };
            string folder = m.Kind.Folder;
            open.Click += (_, _) => { try { Process.Start("explorer.exe", $"\"{folder}\""); } catch { } };
            Cell(open, r, 5);
            r++;
        }
        total.Text = F("{0} in all", Storage.Size(measures.Sum(m => m.Bytes)));
    }

    private async Task DeleteAsync()
    {
        var kinds = Storage.Kinds.Where(k => Ticked.Contains(k.Id)).ToList();
        if (kinds.Count == 0) { status.Text = T("Tick what to clean up first."); return; }
        int d = Days;
        if (MessageBox.Show(Window.GetWindow(this), F("Delete the files older than {0} from {1}? This can't be undone.", P(d, "{0} day", "{0} days"), string.Join(", ", kinds.Select(k => k.Name))),
                T("Storage"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        status.Text = T("Deleting…");
        var (files, bytes) = await Task.Run(() =>
        {
            int n = 0;
            long b = 0;
            foreach (var k in kinds) { var (kn, kb) = Storage.Clean(k, d); n += kn; b += kb; }
            return (n, b);
        });
        await MeasureAsync();
        status.Text = files == 0 ? T("Nothing was that old.") : F("{0} deleted, {1} freed.", P(files, "{0} file", "{0} files"), Storage.Size(bytes));
    }
}
