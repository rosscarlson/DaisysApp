using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>
/// A sync in its own window, which leaves the app free (a long sync can run while you do anything else): first what's
/// about to happen (the folders, the mode, and what comparing them found), then Start, then its progress (pause or
/// stop any time), then what it did.
/// </summary>
internal sealed class SyncWindow : Window
{
    private readonly SyncJob job;
    private readonly FileSyncSettings settings;
    private readonly StackPanel body = new();
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
    private readonly CancellationTokenSource cancel = new();
    private SyncRunner? runner;
    private SyncPlan? plan;
    private readonly Dictionary<string, CheckBox> picks = new(); // what to do, ticked in the check step
    private DateTime started;
    private bool running, finished;
    private SyncMonitor? monitor;
    private Button? pause;

    /// <summary>A sync's window is open (the tab's list of them).</summary>
    public static event Action<SyncWindow, bool>? OpenChanged;

    /// <summary>Raised when a sync has run, with its plan and result (the tab shows it in its recent syncs).</summary>
    public event Action<SyncRun>? Ran;

    /// <summary>Background was clicked: the window hides and the tab shows the sync instead (until Pop out).</summary>
    public event Action<SyncWindow>? Backgrounded;

    /// <summary>Pop out was clicked (or the same sync was asked for again): back in its window.</summary>
    public event Action? PoppedOut;

    /// <summary>The sync has finished (done, stopped or failed), with how: shown on its background card.</summary>
    public event Action<string>? Ended;

    public SyncJob Job => job;
    public bool IsRunning => running;
    public SyncMonitor? Monitor => monitor;
    public SyncRunner.Progress? State => runner?.State;
    public bool InBackground { get; private set; }
    public bool IsPaused => runner != null && !runner.Running.IsSet;

    /// <summary>"Name" or "Source → Destination": what the background card calls it.</summary>
    public string Caption => job.Name.Length > 0 ? job.Name : F("{0} → {1}", job.Source, job.Destination);

    /// <param name="compared">The tab's comparison of these folders, or null to compare here (a saved sync from the tray).</param>
    public SyncWindow(SyncJob job, FileSyncSettings settings, SyncPlan? compared = null)
    {
        this.job = job.Clone();
        this.settings = settings;
        Title = (job.Name.Length > 0 ? job.Name + " · " : "") + T("Sync");
        Width = 680;
        SizeToContent = SizeToContent.Height;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Content = new DockPanel { Margin = new Thickness(24, 20, 24, 20), Children = { Bottom(buttons), body } };
        plan = compared;
        Loaded += (_, _) =>
        {
            if (plan == null) _ = CompareAsync();
            else { Settings(); Summary(plan); }
        };
        Closing += (_, e) =>
        {
            if (!running) return;
            if (!Ui.Ask(this, T("Stop this sync? What's been copied so far stays; nothing is left half copied."), T("Stop the sync"), T("Keep going"), danger: true)) { e.Cancel = true; return; }
            cancel.Cancel();
            runner?.Running.Set();
        };
        Closed += (_, _) => { cancel.Cancel(); OpenChanged?.Invoke(this, false); };
        OpenChanged?.Invoke(this, true);
    }

    private static UIElement Bottom(UIElement e) { DockPanel.SetDock(e, Dock.Bottom); return e; }

    /// <summary>Stops it (the app is closing).</summary>
    public void Stop()
    {
        cancel.Cancel();
        runner?.Running.Set();
    }

    // ---------------------------------------------------------------- 1. what's about to happen

    private static TextBlock Heading(string s) => new() { Text = s, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
    private static TextBlock Text(string s, double bottom = 6) => new() { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom), FontSize = 13.5 };

    private static UIElement Line(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(120) }, new ColumnDefinition() } };
        var l = Ui.Secondary(label);
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        return g;
    }

    private void Settings()
    {
        body.Children.Clear();
        body.Children.Add(Heading(T("Check before syncing")));
        body.Children.Add(Line(T("From"), Path.GetFullPath(job.Source)));
        body.Children.Add(Line(T("To"), Path.GetFullPath(job.Destination)));
        body.Children.Add(Line(T("Mode"), job.Mode == SyncMode.Mirror
            ? T("Mirror: the destination ends up exactly like the source. Files and folders only in the destination are deleted.")
            : T("Update: new and changed files are copied. Nothing in the destination is deleted.")));
        body.Children.Add(Line(T("Compare by"), CompareText(job)));
        if (job.Mode == SyncMode.Mirror)
            body.Children.Add(Line(T("Deleted files"), job.RecycleBin
                ? SyncRunner.HasRecycleBin(job.Destination) ? T("Go to the Recycle Bin") : T("Deleted for good: the destination's drive has no Recycle Bin")
                : T("Deleted for good")));
        if (!string.IsNullOrWhiteSpace(job.Excludes)) body.Children.Add(Line(T("Left alone"), job.Excludes));
    }

    /// <summary>"Name, size and date and time", as ticked on the page.</summary>
    public static string CompareText(SyncJob j)
    {
        var parts = new List<string> { T("name") };
        if (j.CompareSize) parts.Add(T("size"));
        if (j.CompareTime) parts.Add(T("date and time"));
        if (j.CompareContent) parts.Add(T("contents (read byte by byte)"));
        string s = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " " + T("and") + " " + parts[^1];
        return char.ToUpper(s[0]) + s[1..] + (parts.Count == 1 ? " " + T("(a file that's on both sides counts as the same)") : "");
    }

    private async Task CompareAsync()
    {
        Settings();
        var note = Ui.Secondary(T("Comparing the folders…"), wrap: true);
        note.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(note);
        var bar = Ui.Bar(4);
        bar.IsIndeterminate = true;
        bar.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(bar);
        SetButtons((T("Cancel"), false, Close));

        var progress = new SyncPlan.Progress();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => note.Text = progress.Stage == "content"
            ? F("Comparing contents… {0} of {1} files", Sizes.Count(Interlocked.Read(ref progress.Compared)), Sizes.Count(progress.ToCompare))
            : FileSyncView.Reading(progress);
        timer.Start();
        try
        {
            plan = await Task.Run(() => SyncPlan.Compare(job, progress, cancel.Token));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            timer.Stop();
            body.Children.Remove(bar);
            note.Text = F("Couldn't compare the folders: {0}", ex.Message);
            Log.Here.Error($"Couldn't compare {job.Source} and {job.Destination}", ex);
            note.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            SetButtons((T("Close"), false, Close));
            return;
        }
        finally { timer.Stop(); }
        body.Children.Remove(bar);
        body.Children.Remove(note);
        Summary(plan);
    }

    private void Summary(SyncPlan p)
    {
        var box = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        int newFiles = p.Count(SyncAction.Create, dirs: false), newFolders = p.Count(SyncAction.Create, dirs: true);
        int updates = p.Count(SyncAction.Update), delFiles = p.Count(SyncAction.Delete, dirs: false), delFolders = p.Count(SyncAction.Delete, dirs: true);
        int skips = p.Count(SyncAction.Skip);
        if (p.NothingToDo)
        {
            box.Children.Add(Text(T("Nothing to do: the destination already has everything.") + (skips > 0 ? " " + P(skips, "({0} file is left as it is: see the tab's list.)", "({0} files are left as they are: see the tab's list.)") : "")));
            body.Children.Add(Ui.Card(box));
            SetButtons((T("Close"), true, Close));
            return;
        }
        // what it's about to do, each with a tick: a last chance to leave something out
        box.Children.Add(new TextBlock { Text = T("What the sync does (untick anything you've changed your mind about):"), TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        long newBytes = p.Items.Where(i => i.Action == SyncAction.Create && !i.LeftDir).Sum(i => i.LeftSize ?? 0);
        long updateBytes = p.Items.Where(i => i.Action == SyncAction.Update).Sum(i => i.LeftSize ?? 0);
        var total = Ui.Secondary(wrap: true);
        Button? start = null;
        void Pick(string key, int count, string text, string? tip = null)
        {
            if (count == 0) return;
            var c = new CheckBox { Content = text, IsChecked = true, Margin = new Thickness(0, 0, 0, 6), ToolTip = tip };
            c.Click += (_, _) => Totals();
            picks[key] = c;
            box.Children.Add(c);
        }
        Pick("copy", newFiles, P(newFiles, "Copy {0} new file", "Copy {0} new files") + $" ({Sizes.Bytes(newBytes)})",
            newFolders > 0 ? T("Files in new folders are only copied when the folders are made too") : null);
        Pick("folders", newFolders, P(newFolders, "Make {0} new folder", "Make {0} new folders"));
        Pick("update", updates, P(updates, "Update {0} changed file", "Update {0} changed files") + $" ({Sizes.Bytes(updateBytes)})");
        Pick("delfiles", delFiles, P(delFiles, "Delete {0} file that's only in the destination", "Delete {0} files that are only in the destination"));
        Pick("delfolders", delFolders, P(delFolders, "Delete {0} folder that's only in the destination (with what's in it)", "Delete {0} folders that are only in the destination (with what's in them)"));
        total.Margin = new Thickness(0, 4, 0, 0);
        box.Children.Add(total);
        void Totals()
        {
            var picked = Picked(p);
            total.Text = F("{0} to copy. Compared in {1}: {2} files in the source, {3} in the destination.",
                Sizes.Bytes(picked.BytesToCopy), Sizes.Took(p.Took), Sizes.Count(p.LeftFiles), Sizes.Count(p.RightFiles));
            if (start != null) start.IsEnabled = !picked.NothingToDo;
        }
        Totals();
        if (skips > 0) box.Children.Add(Ui.Secondary(P(skips, "{0} file is left as it is (the destination's is newer, or a file and a folder share a name).", "{0} files are left as they are (the destination's are newer, or a file and a folder share a name)."), wrap: true));
        if (p.DestinationMissing) box.Children.Add(Ui.Secondary(T("The destination folder doesn't exist yet: it's made."), wrap: true));

        // what to look at twice
        if (p.Job.Mode == SyncMode.Mirror && p.LeftFiles == 0 && p.RightFiles > 0)
            box.Children.Add(Warning(T("The source is empty: mirroring it deletes everything in the destination.")));
        else if (p.Job.Mode == SyncMode.Mirror && delFiles > 0 && delFiles >= Math.Max(50, p.RightFiles / 2))
            box.Children.Add(Warning(F("This deletes {0} of the destination's {1} files. Check the folders are the right way round.", Sizes.Count(delFiles), Sizes.Count(p.RightFiles))));
        if (delFiles + delFolders > 0 && p.Job.RecycleBin && !SyncRunner.HasRecycleBin(p.Job.Destination))
            box.Children.Add(Warning(T("The destination's drive has no Recycle Bin: deleted files are gone for good.")));
        if (p.Unreadable.Count > 0)
        {
            box.Children.Add(Warning(P(p.Unreadable.Count, "{0} folder couldn't be read, so it isn't synced:", "{0} folders couldn't be read, so they aren't synced:")));
            var list = Ui.CopyText(string.Join("\n", p.Unreadable.Take(500).Select(u => $"{u.Path}: {u.Error}")), maxHeight: 120);
            list.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(list);
        }
        body.Children.Add(Ui.Card(box));
        SetButtons((T("Start"), true, Start), (T("Cancel"), false, Close));
        start = (Button)buttons.Children[0];
        Totals();
    }

    private bool Ticked(string key) => !picks.TryGetValue(key, out var c) || c.IsChecked == true;

    /// <summary>The plan with only what's ticked (files in new folders that aren't made are left out too).</summary>
    private SyncPlan Picked(SyncPlan p)
    {
        bool copy = Ticked("copy"), folders = Ticked("folders"), update = Ticked("update"), delFiles = Ticked("delfiles"), delFolders = Ticked("delfolders");
        var notMade = folders ? new HashSet<string>() : p.Items.Where(i => i.Action == SyncAction.Create && i.LeftDir).Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool InNotMade(string path)
        {
            for (int i = path.LastIndexOf('\\'); i > 0; i = path.LastIndexOf('\\', i - 1))
                if (notMade.Contains(path[..i])) return true;
            return false;
        }
        return p.Only(i => i.Action switch
        {
            SyncAction.Create when i.LeftDir => folders,
            SyncAction.Create => copy && !InNotMade(i.Path),
            SyncAction.Update => update,
            SyncAction.Delete when i.RightDir => delFolders,
            SyncAction.Delete => delFiles,
            _ => true,
        });
    }

    private static TextBlock Warning(string s)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        return t;
    }

    private void SetButtons(params (string Text, bool Accent, Action Click)[] list)
    {
        buttons.Children.Clear();
        foreach (var (text, accent, click) in list)
        {
            var b = new Button { Content = text, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            if (accent) { b.SetResourceReference(StyleProperty, "AccentButton"); b.IsDefault = true; }
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
        }
    }

    // ---------------------------------------------------------------- 2. running

    /// <summary>Pauses it, or carries on.</summary>
    public void TogglePause()
    {
        if (runner == null || !running) return;
        if (runner.Running.IsSet) runner.Running.Reset(); else runner.Running.Set();
        if (pause != null) pause.Content = runner.Running.IsSet ? T("Pause") : T("Resume");
    }

    /// <summary>Stops it, after asking (<paramref name="near"/>: whose window the question is over).</summary>
    public void AskStop(DependencyObject near)
    {
        if (runner == null || !running) return;
        if (!Ui.Ask(near, T("Stop this sync? What's been copied so far stays; nothing is left half copied."), T("Stop the sync"), T("Keep going"), danger: true)) return;
        cancel.Cancel();
        runner.Running.Set();
    }

    /// <summary>Hides the window: the tab shows the sync above Recent Syncs.</summary>
    private void SendToBackground()
    {
        InBackground = true;
        Hide();
        Backgrounded?.Invoke(this);
        Log.Here.Info($"Sync sent to the background: {job.Source} → {job.Destination}");
    }

    /// <summary>Brings the window back from the tab.</summary>
    public void PopOut()
    {
        if (!InBackground) { Activate(); return; }
        InBackground = false;
        PoppedOut?.Invoke();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async void Start()
    {
        if (plan == null) return;
        var todo = Picked(plan);
        if (todo.NothingToDo) return;
        if (picks.Values.Any(c => c.IsChecked != true))
            Log.Here.Info("Sync: left out at the last step: " + string.Join(", ", picks.Where(k => k.Value.IsChecked != true).Select(k => k.Key)));
        runner = new SyncRunner(todo);
        running = true;
        started = DateTime.Now;
        Log.Here.Info($"Sync started: {job.Mode} {job.Source} → {job.Destination}");

        body.Children.Clear();
        body.Children.Add(Heading(T("Syncing")));
        var route = Ui.Secondary(F("{0} → {1}", job.Source, job.Destination), wrap: true);
        route.Margin = new Thickness(0, -6, 0, 14);
        body.Children.Add(route);
        monitor = new SyncMonitor(runner);
        body.Children.Add(new SyncProgressView(monitor, () => runner.State));
        monitor.Start();
        SetButtons((T("Background"), false, SendToBackground), (T("Pause"), false, TogglePause), (T("Stop"), false, () => AskStop(this)));
        ((Button)buttons.Children[0]).ToolTip = T("Hide this window and show the sync on the File Sync tab, above Recent Syncs (Pop out there brings it back)");
        pause = (Button)buttons.Children[1];

        string result = "done";
        try { await Task.Run(() => runner.Run(cancel.Token)); }
        catch (OperationCanceledException) { result = "stopped"; }
        catch (Exception ex)
        {
            result = "failed";
            runner.State.Errors.Enqueue(("", ex.Message));
            Log.Here.Error("Sync failed", ex);
        }
        finally
        {
            monitor.Stop();
            running = false;
        }
        Done(result);
    }

    public static void ShowErrors(TextBox errors, SyncRunner.Progress s)
    {
        int n = s.Errors.Count;
        if (n == 0) return;
        errors.Visibility = Visibility.Visible;
        string text = P(n, "{0} problem:", "{0} problems:") + "\n" + string.Join("\n", Lines(s));
        if (errors.Text != text) errors.Text = text;
    }

    /// <summary>The sync's problems, a line each (the first 500).</summary>
    public static IEnumerable<string> Lines(SyncRunner.Progress s) =>
        s.Errors.Take(500).Select(e => e.Path.Length > 0 ? $"{e.Path}: {e.Error}" : e.Error).Append(s.Errors.Count > 500 ? "…" : "").Where(l => l.Length > 0);

    // ---------------------------------------------------------------- 3. what it did

    private void Done(string result)
    {
        if (runner == null) return;
        finished = true;
        var s = runner.State;
        var took = DateTime.Now - started;
        var run = new SyncRun
        {
            Started = started, Name = job.Name, Source = job.Source, Destination = job.Destination, Mode = job.Mode,
            Copied = s.Copied, Updated = s.Updated, Deleted = s.Deleted, Errors = s.Errors.Count, Problems = Lines(s).Take(50).ToList(), Bytes = Interlocked.Read(ref s.BytesDone),
            Seconds = took.TotalSeconds, Result = result,
        };
        settings.History.Insert(0, run);
        if (settings.History.Count > 30) settings.History.RemoveRange(30, settings.History.Count - 30);
        settings.Save();
        Log.Here.Info($"Sync {result} in {took.TotalSeconds:0.0} s: {s.Copied} copied, {s.Updated} updated, {s.Deleted} deleted, {s.Errors.Count} errors ({job.Source} → {job.Destination})");
        Ran?.Invoke(run);
        Ended?.Invoke(result);
        if (!IsLoaded) return; // (closed while it ran)

        body.Children.Clear();
        body.Children.Add(Heading(result == "done" ? (s.Errors.IsEmpty ? T("Done") : T("Done, with problems")) : result == "stopped" ? T("Stopped") : T("The sync failed")));
        body.Children.Add(Text(F("{0} copied, {1} updated, {2} deleted · {3} in {4}.", Sizes.Count(s.Copied), Sizes.Count(s.Updated), Sizes.Count(s.Deleted), Sizes.Bytes(run.Bytes), Sizes.Duration(took))));
        if (result == "stopped") body.Children.Add(Ui.Secondary(T("What was copied before stopping stays; the rest is as it was. Sync again to finish."), wrap: true));
        var errors = Ui.CopyText();
        errors.Margin = new Thickness(0, 8, 0, 0);
        errors.Visibility = Visibility.Collapsed;
        body.Children.Add(errors);
        ShowErrors(errors, s);
        string dst = job.Destination;
        SetButtons((T("Open the destination"), false, () => Ui.Open(dst)), (T("Close"), true, Close));
        if (WindowState == WindowState.Minimized && !InBackground) FlashTaskbar();
    }

    private void FlashTaskbar()
    {
        try
        {
            var info = new FlashInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FlashInfo>(), Window = new System.Windows.Interop.WindowInteropHelper(this).Handle, Flags = 3 | 12, Count = 0 };
            FlashWindowEx(ref info);
        }
        catch { }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public bool Finished => finished;
}
