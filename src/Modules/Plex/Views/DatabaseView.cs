using System.IO;
using System.Windows.Controls;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Database (on the server's own PC): its files and sizes, Check (integrity, while Plex runs), Repair (Plex closed, a
/// copy kept), Back Up Now; the backups' folder, how many to keep and the schedule (a task in Task Scheduler, which runs
/// without this app); every backup (this app's and Plex's own) with Restore.
/// </summary>
internal sealed class DatabaseView : Border
{
    private readonly PlexService service;
    private PlexSettings S => service.Settings;
    private readonly TextBlock files = Ui.Secondary("", wrap: true);
    private readonly TextBox output = Ui.CopyText("", null, 240);
    private readonly StackPanel actions = new();
    private readonly List<Button> busyButtons = [];
    private readonly Button cancel = new() { Content = T("Cancel"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock notLocal = Ui.Note("", 0);
    private readonly DataGrid table = Ui.Table(260);
    private readonly TextBlock scheduleNote = Ui.Note("", 6);
    private readonly TextBlock tableNote = Ui.Note("", 6);
    private readonly Debounce reschedule;
    private CancellationTokenSource? running;
    private string? plexBackupFolder;

    public DatabaseView(PlexService service)
    {
        this.service = service;
        reschedule = new Debounce(TimeSpan.FromSeconds(1), () => Dispatcher.BeginInvoke(async () => await ApplySchedule()));
        var page = new StackPanel();
        page.Children.Add(notLocal);

        // ---- the database
        var check = PlexUi.Small(T("Check"), () => Go(T("Check"), (say, c) => DbTools.CheckAsync(say, false, c)), T("A full integrity check (Plex keeps running; a minute or two on a big database)"));
        var quick = PlexUi.Small(T("Quick Check"), () => Go(T("Quick check"), (say, c) => DbTools.CheckAsync(say, true, c)));
        var repair = PlexUi.SmallConfirm(T("Repair"), () => Go(T("Repair"), (say, _) => DbTools.RepairAsync(S, say, CancellationToken.None), canCancel: false),
            T("Closes Plex, keeps a copy, then compacts and reindexes the database, or rebuilds it if it's damaged; starts Plex again"));
        var now = PlexUi.Small(T("Back Up Now"), () => Go(T("Backup"), async (say, c) =>
        {
            var (code, text) = await Backups.RunNowAsync(S, c);
            foreach (var line in text.Split('\n')) say(line.Trim());
            return code == 0;
        }), T("Copies the database while Plex runs, as the scheduled backup does"));
        busyButtons.AddRange([check, quick, repair, now]);
        cancel.Click += (_, _) => running?.Cancel();
        actions.Children.Add(files);
        var buttons = PlexUi.Buttons(check, quick, repair, now, cancel);
        buttons.Margin = new Thickness(0, 10, 0, 4);
        actions.Children.Add(buttons);
        actions.Children.Add(output);
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Database")), actions } }, new Thickness(0, 0, 0, 12)));

        // ---- backups: where, how many, when
        page.Children.Add(Ui.Card(Schedule(), new Thickness(0, 0, 0, 12)));

        // ---- the backups
        table.Columns.Add(Ui.Column(T("When"), nameof(Backup.WhenText), 140, nameof(Backup.When)));
        table.Columns.Add(Ui.Column(T("Made by"), nameof(Backup.Kind), 100));
        table.Columns.Add(Ui.Column(T("Size"), nameof(Backup.SizeText), 90, nameof(Backup.Size), right: true));
        table.Columns.Add(Ui.Column(T("State"), nameof(Backup.State), 120));
        table.Columns.Add(Ui.Column(T("Where"), nameof(Backup.LibraryFile), -1));
        var restore = PlexUi.SmallConfirm(T("Restore"), () =>
        {
            if (table.SelectedItem is not Backup b) { tableNote.Text = T("Pick a backup first."); return; }
            Go(T("Restore"), (say, c) => DbTools.RestoreAsync(S, b, say, c), canCancel: false);
        }, T("Closes Plex, puts this backup in place of the database (the one it replaces is kept), starts Plex"));
        var open = PlexUi.Small(T("Show in Explorer"), () => { if (table.SelectedItem is Backup b) Ui.ShowInExplorer(b.LibraryFile); });
        var delete = PlexUi.SmallConfirm(T("Delete"), DeleteBackup);
        var refresh = PlexUi.Small(T("Refresh"), ShowBackups);
        busyButtons.Add(restore);
        var tableButtons = PlexUi.Buttons(restore, open, delete, refresh);
        tableButtons.Margin = new Thickness(0, 8, 0, 0);
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Backups")), table, tableButtons, tableNote } }));
        Child = Ui.Page(page);

        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Show(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Show(); });
    }

    private void Show()
    {
        notLocal.Text = service.Connected && !service.IsLocal
            ? T("The server isn't on this PC: the database's check, repair and backups work on the server's own PC (run Daisy's App there).")
            : !PlexLocal.Installed ? T("Plex Media Server isn't on this PC.") : "";
        notLocal.Margin = new Thickness(0, 0, 0, notLocal.Text.Length > 0 ? 12 : 0);
        IsEnabled = PlexLocal.Installed && (service.IsLocal || !service.Connected);
        if (PlexLocal.LibraryPath is { } lib)
        {
            string Size(string f) => File.Exists(f) ? Sizes.Bytes(new FileInfo(f).Length) : "—";
            files.Text = F("Library {0} (log {1}) · Images {2} (log {3}) · in {4}", Size(lib), Size(lib + "-wal"), Size(PlexLocal.BlobsPath!), Size(PlexLocal.BlobsPath + "-wal"), PlexLocal.DatabaseFolder)
                + "\n" + (PlexLocal.Sqlite is { } sq ? F("Plex SQLite: {0}", sq) : T("Plex SQLite wasn't found: Check, Repair and backups need it."));
        }
        _ = LoadPlexBackupFolder();
        ShowBackups();
        _ = ShowSchedule();
    }

    private async Task LoadPlexBackupFolder()
    {
        if (plexBackupFolder != null || service.Client is not { } c) return;
        try
        {
            var prefs = await c.GetAsync("/:/prefs");
            plexBackupFolder = PlexClient.List(prefs, "Setting").Where(x => PlexClient.Str(x, "id") == "ButlerDatabaseBackupPath").Select(x => PlexClient.Str(x, "value")).FirstOrDefault() ?? "";
            ShowBackups();
        }
        catch { }
    }

    private void ShowBackups()
    {
        try
        {
            var list = Backups.List(S, plexBackupFolder);
            table.ItemsSource = list;
            tableNote.Text = list.Count == 0 ? T("No backups yet.") : "";
        }
        catch (Exception ex) { tableNote.Text = ex.Message; }
    }

    private void DeleteBackup()
    {
        if (table.SelectedItem is not Backup b) { tableNote.Text = T("Pick a backup first."); return; }
        try
        {
            if (b.Plexs)
            {
                File.Delete(b.LibraryFile);
                if (b.BlobsFile != null) File.Delete(b.BlobsFile);
            }
            else Directory.Delete(Path.GetDirectoryName(b.LibraryFile)!, true);
            Log.Here.Info($"Deleted the backup {b.Name}");
        }
        catch (Exception ex) { tableNote.Text = ex.Message; }
        ShowBackups();
    }

    /// <summary>Runs a job, its lines in the output box; the buttons are off while it runs.</summary>
    private async void Go(string what, Func<Action<string>, CancellationToken, Task<bool>> job, bool canCancel = true)
    {
        if (running != null) return;
        running = new CancellationTokenSource();
        var token = running.Token;
        foreach (var b in busyButtons) b.IsEnabled = false;
        cancel.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        output.Text = "";
        void Say(string line) => Dispatcher.BeginInvoke(() =>
        {
            if (line.Length == 0) return;
            output.AppendText((output.Text.Length > 0 ? "\n" : "") + $"{DateTime.Now:HH:mm:ss}  {line}");
            output.ScrollToEnd();
        });
        try
        {
            bool ok = await Task.Run(() => job(Say, token));
            Say(ok ? F("{0}: finished.", what) : F("{0}: finished with problems (above).", what));
        }
        catch (OperationCanceledException) { Say(T("Cancelled.")); }
        catch (Exception ex)
        {
            Say(ex is PlexException ? ex.Message : F("Failed: {0}", ex.Message));
            if (ex is not PlexException) Log.Here.Error(what, ex);
        }
        finally
        {
            running = null;
            foreach (var b in busyButtons) b.IsEnabled = true;
            cancel.Visibility = Visibility.Collapsed;
            Show();
            if (what != T("Check") && what != T("Quick check")) service.Reconnect();
        }
    }

    // ---------------------------------------------------------------- the schedule

    private FrameworkElement Schedule()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Header(T("Schedule")));
        var folder = new TextBox { Width = 380, Text = S.BackupFolderOrDefault, ToolTip = T("Each backup is a folder here: Plex yyyy-MM-dd HHmmss") };
        var browse = PlexUi.Small(T("Browse…"), () =>
        {
            if (Ui.PickFolder(T("Where backups go"), S.BackupFolderOrDefault) is { } f) folder.Text = f;
        });
        browse.Margin = new Thickness(6, 0, 0, 0);
        var openFolder = PlexUi.Small(T("Open"), () => { Directory.CreateDirectory(S.BackupFolderOrDefault); Ui.Open(S.BackupFolderOrDefault); });
        openFolder.Margin = new Thickness(6, 0, 0, 0);
        folder.TextChanged += (_, _) => { S.BackupFolder = folder.Text.Trim(); Changed(); };
        p.Children.Add(Row(T("Folder"), folder, browse, openFolder));

        var keep = Ui.Choice(new[] { 3, 5, 7, 10, 14, 30, 60 }.Select(n => (n.ToString(), n)), S.BackupKeep, v => { S.BackupKeep = v; Changed(); }, 70);
        p.Children.Add(Row(T("Keep"), keep, Ui.Secondary(T("  backups (the oldest go)"))));

        var on = new CheckBox { Content = T("Back up on a schedule"), IsChecked = S.BackupScheduled, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("A task in Windows' Task Scheduler: it runs while you're signed in, whether this app is running or not") };
        var how = Ui.Choice(new[] { (T("Daily"), false), (T("Weekly"), true) }, S.BackupWeekly, v => { S.BackupWeekly = v; Changed(); day!.Visibility = v ? Visibility.Visible : Visibility.Collapsed; }, 90);
        how.Margin = new Thickness(12, 0, 0, 0);
        day = Ui.Choice(Enum.GetValues<DayOfWeek>().Select(d => (System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(d), d)), S.BackupDay, v => { S.BackupDay = v; Changed(); }, 110);
        day.Margin = new Thickness(6, 0, 0, 0);
        day.Visibility = S.BackupWeekly ? Visibility.Visible : Visibility.Collapsed;
        var at = new TextBox { Width = 60, Text = S.BackupTime, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("The time, e.g. 04:00") };
        System.Windows.Automation.AutomationProperties.SetName(at, T("Time"));
        at.TextChanged += (_, _) =>
        {
            if (!TimeSpan.TryParse(at.Text.Trim(), out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) return;
            S.BackupTime = $"{t.Hours:00}:{t.Minutes:00}";
            Changed();
        };
        on.Click += (_, _) => { S.BackupScheduled = on.IsChecked == true; Changed(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Children = { on, how, day, new TextBlock { Text = T("at"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, at } };
        p.Children.Add(row);
        p.Children.Add(scheduleNote);
        p.Children.Add(PlexUi.Buttons(PlexUi.Small(T("Backup Log"), () => { string log = Path.Combine(S.BackupFolderOrDefault, "Backups.log"); if (File.Exists(log)) Ui.Open(log); else scheduleNote.Text = T("No backups have run yet."); })));
        return p;
    }

    private ComboBox? day;

    private static FrameworkElement Row(string label, params FrameworkElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        foreach (var i in items) { i.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(i); }
        return row;
    }

    private void Changed()
    {
        S.Save();
        reschedule.Poke();
    }

    /// <summary>The task as the settings say: registered (again) when on, removed when off; the script rewritten either way.</summary>
    private async Task ApplySchedule()
    {
        try
        {
            if (PlexLocal.Installed) Backups.WriteScript(S);
            string? problem = S.BackupScheduled ? await Backups.ScheduleAsync(S) : await Backups.UnscheduleAsync();
            if (problem != null) { scheduleNote.Text = F("Task Scheduler: {0}", problem); return; }
        }
        catch (Exception ex) { scheduleNote.Text = ex.Message; return; }
        await ShowSchedule();
    }

    private async Task ShowSchedule()
    {
        bool there = await Backups.ScheduledAsync();
        var last = Backups.Newest(S);
        string lastText = last != null ? F("Last backup: {0} ({1}).", last.When.ToString("g"), PlexUi.Ago(last.When)) : T("No backup yet.");
        scheduleNote.Text = S.BackupScheduled && !there ? T("The task isn't in Task Scheduler: switch the schedule off and on again.") + " " + lastText
            : there ? F("Next: {0:dddd g} (Task Scheduler: {1}\\{2}).", Backups.Next(S), AppPaths.ShortName, Backups.TaskName) + " " + lastText
            : lastText;
    }
}
