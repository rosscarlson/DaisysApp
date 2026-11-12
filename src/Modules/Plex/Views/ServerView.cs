using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Server: the server's state (version and update, plex.tv sign-in, remote access), what's wrong with it (Checks), its
/// libraries (items, unmatched, last scan; Scan, Refresh Metadata, Empty Trash), maintenance (Plex's own jobs: optimise
/// the database, clean bundles, back up…), and on the server's own PC its folders and caches, recent errors in its log,
/// and a restart.
/// </summary>
internal sealed class ServerView : Border
{
    private readonly PlexService service;
    private readonly StackPanel info = new();
    private readonly StackPanel checks = new();
    private readonly DataGrid libraries = Ui.Table();
    private readonly TextBlock libNote = Ui.Note("", 6);
    private readonly TextBlock jobNote = Ui.Note("", 4);
    private readonly Border localCard;
    private readonly StackPanel local = new();
    private readonly TextBox errors = Ui.CopyText("", null, 260);
    private bool loading;
    private DateTime loadedAt;

    public ServerView(PlexService service)
    {
        this.service = service;
        var page = new StackPanel();
        var refresh = Ui.IconButton("\uE72C", T("Refresh"), null, () => _ = Load());

        var top = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) } }, Margin = new Thickness(0, 0, 0, 12) };
        var infoCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Server"), refresh), info } }, new Thickness(0));
        var checksCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Checks")), checks } }, new Thickness(0));
        Grid.SetColumn(checksCard, 2);
        top.Children.Add(infoCard);
        top.Children.Add(checksCard);
        page.Children.Add(top);

        libraries.Columns.Add(Ui.Column(T("Library"), nameof(LibRow.Title), -1));
        libraries.Columns.Add(Ui.Column(T("Type"), nameof(LibRow.TypeName), 90));
        libraries.Columns.Add(Ui.Column(T("Items"), nameof(LibRow.ItemsText), 80, nameof(LibRow.Items), right: true));
        libraries.Columns.Add(Ui.Column(T("Unmatched"), nameof(LibRow.UnmatchedText), 90, nameof(LibRow.Unmatched), right: true));
        libraries.Columns.Add(Ui.Column(T("Scanned"), nameof(LibRow.ScannedText), 130, nameof(LibRow.Scanned)));
        libraries.Columns.Add(Ui.Column(T("Folders"), nameof(LibRow.Folders), -2));
        libraries.SelectionMode = DataGridSelectionMode.Extended;
        var libButtons = PlexUi.Buttons(
            PlexUi.Small(T("Scan"), () => OnLibraries("/library/sections/{0}/refresh", HttpMethod.Get, T("Scanning")), T("Look for new and changed files")),
            PlexUi.SmallConfirm(T("Refresh Metadata"), () => OnLibraries("/library/sections/{0}/refresh?force=1", HttpMethod.Get, T("Refreshing all metadata of")), T("Fetch every item's metadata again (slow on a big library)")),
            PlexUi.Small(T("Analyze"), () => OnLibraries("/library/sections/{0}/analyze", HttpMethod.Put, T("Analyzing")), T("Read the files' media details again")),
            PlexUi.SmallConfirm(T("Empty Trash"), () => OnLibraries("/library/sections/{0}/emptyTrash", HttpMethod.Put, T("Emptying the trash of")), T("Remove items whose files are gone")),
            PlexUi.Small(T("Scan All"), () => Job(T("Scan all libraries"), c => c.SendAsync(HttpMethod.Get, "/library/sections/all/refresh"))));
        libButtons.Margin = new Thickness(0, 8, 0, 0);
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Libraries")), libraries, libButtons, libNote } }, new Thickness(0, 0, 0, 12)));

        var jobs = PlexUi.Buttons(
            PlexUi.Small(T("Optimize Database"), () => Job(T("Optimize the database"), c => c.SendAsync(HttpMethod.Put, "/library/optimize?async=1")), T("Plex's own: compacts the database and its write-ahead log")),
            PlexUi.Small(T("Clean Bundles"), () => Job(T("Clean bundles"), c => c.SendAsync(HttpMethod.Put, "/library/clean/bundles?async=1")), T("Remove metadata of items no longer in a library")),
            PlexUi.Small(T("Back Up Database (Plex)"), () => Job(T("Plex's database backup"), c => c.SendAsync(HttpMethod.Post, "/butler/BackupDatabase")), T("Plex's own backup copy, in its backup folder")),
            PlexUi.Small(T("Clean Old Cache Files"), () => Job(T("Clean old cache files"), c => c.SendAsync(HttpMethod.Post, "/butler/CleanOldCacheFiles"))),
            PlexUi.Small(T("Collect Unused Images"), () => Job(T("Collect unused images"), c => c.SendAsync(HttpMethod.Post, "/butler/GarbageCollectBlobs")), T("Remove images in the database no item uses")),
            PlexUi.Small(T("Check for Plex Update"), () => Job(T("Check for an update"), c => c.SendAsync(HttpMethod.Put, "/updater/check?download=0"))),
            PlexUi.Small(T("Download Logs…"), () => _ = DownloadLogs(), T("Plex's logs as a zip, for its support")),
            PlexUi.Small(T("Open Plex Web"), () => { if (service.Settings.BaseUri() is { } u) Ui.Open(new Uri(u, "/web/index.html").ToString()); }));
        page.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Maintenance")), jobs, jobNote } }, new Thickness(0, 0, 0, 12)));

        localCard = Ui.Card(local, new Thickness(0, 0, 0, 12));
        page.Children.Add(localCard);
        Child = Ui.Page(page);

        IsVisibleChanged += (_, e) => { if (e.NewValue is true && DateTime.Now - loadedAt > TimeSpan.FromSeconds(30)) _ = Load(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { loadedAt = default; if (IsVisible) _ = Load(); });
    }

    private async Task Load()
    {
        if (loading) return;
        loading = true;
        loadedAt = DateTime.Now;
        info.Children.Clear();
        checks.Children.Clear();
        var found = new List<FrameworkElement>();
        try
        {
            if (service.Client is not { } c)
            {
                info.Children.Add(Ui.Secondary(service.Settings.HasServer ? service.Error ?? T("Connecting…") : T("Plex isn't set up: Settings → Plex."), wrap: true));
                localCard.Visibility = Visibility.Collapsed;
                return;
            }
            JsonElement root, account = default, updater = default, butler = default;
            try { root = await c.GetAsync("/"); }
            catch (Exception ex)
            {
                info.Children.Add(Ui.Secondary(ex.Message, wrap: true));
                checks.Children.Add(PlexUi.Finding("error", F("The server doesn't answer: {0}", ex.Message)));
                localCard.Visibility = Visibility.Collapsed;
                return;
            }
            try { account = PlexClient.Obj(await c.GetAsync("/myplex/account"), "MyPlex"); } catch { }
            try { updater = await c.GetAsync("/updater/status"); } catch { }
            try { butler = await c.GetAsync("/butler"); } catch { }

            string? newer = PlexClient.List(updater, "Release").Select(r => PlexClient.Str(r, "version")).FirstOrDefault(v => v != null);
            string mapping = PlexClient.Str(account, "mappingState") ?? PlexClient.Str(root, "myPlexMappingState") ?? "";
            string signIn = PlexClient.Str(account, "signInState") ?? PlexClient.Str(root, "myPlexSigninState") ?? "";
            info.Children.Add(new TextBlock { Text = PlexClient.Str(root, "friendlyName") ?? "", FontSize = 18, FontWeight = FontWeights.SemiBold });
            info.Children.Add(PlexUi.Facts((T("Version"), PlexClient.Str(root, "version")), (T("On"), $"{PlexClient.Str(root, "platform")} {PlexClient.Str(root, "platformVersion")}")));
            info.Children.Add(PlexUi.Facts((T("Account"), PlexClient.Str(root, "myPlexUsername")), (T("Plex Pass"), PlexClient.Flag(root, "myPlexSubscription") ? T("Yes") : T("No"))));
            info.Children.Add(PlexUi.Facts((T("Remote access"), mapping == "mapped" ? F("working ({0}:{1})", PlexClient.Str(account, "publicAddress"), PlexClient.Str(account, "publicPort")) : mapping),
                (T("Local"), PlexClient.Str(account, "privateAddress") is { } pa ? $"{pa}:{PlexClient.Str(account, "privatePort")}" : null)));
            info.Children.Add(PlexUi.Facts((T("Update"), newer != null ? F("{0} available", newer) : T("up to date"))));
            info.Children.Add(PlexUi.Facts((T("This PC"), service.IsLocal ? T("Yes: its database, logs and program are here") : T("No (the database tools need the server's own PC)"))));

            // ---- checks
            if (signIn.Length > 0 && signIn != "ok") found.Add(PlexUi.Finding("error", F("The server isn't signed in to plex.tv ({0}): remote access and sharing don't work. Sign in from Plex Web → Settings → General.", signIn)));
            if (mapping.Length > 0 && mapping != "mapped" && PlexClient.Flag(root, "myPlex"))
                found.Add(PlexUi.Finding("warn", F("Remote access isn't working ({0}{1}). Check port forwarding of {2} on the router.", mapping,
                    PlexClient.Str(account, "mappingError") is { Length: > 0 } me ? ": " + me : "", PlexClient.Str(account, "privatePort") ?? "32400")));
            if (newer != null) found.Add(PlexUi.Finding("info", F("Plex Media Server {0} is available (this is {1}).", newer, PlexClient.Str(root, "version"))));
            var backupTask = PlexClient.List(PlexClient.Obj(butler, "ButlerTasks"), "ButlerTask").FirstOrDefault(b => PlexClient.Str(b, "name") == "BackupDatabase");
            if (backupTask.ValueKind == JsonValueKind.Object && !PlexClient.Flag(backupTask, "enabled"))
                found.Add(PlexUi.Finding("warn", T("Plex's own database backup is switched off (Plex Web → Settings → Scheduled Tasks).")));
            var optimize = PlexClient.List(PlexClient.Obj(butler, "ButlerTasks"), "ButlerTask").FirstOrDefault(b => PlexClient.Str(b, "name") == "OptimizeDatabase");
            if (optimize.ValueKind == JsonValueKind.Object && !PlexClient.Flag(optimize, "enabled"))
                found.Add(PlexUi.Finding("warn", T("Plex's weekly database optimisation is switched off (Plex Web → Settings → Scheduled Tasks).")));

            await LoadLibraries(c, found);
            if (service.IsLocal) found.AddRange(LocalChecks(c));
            if (service.Settings.HasTautulli) found.AddRange(await TautulliChecks());

            if (found.Count == 0) found.Add(PlexUi.Finding("ok", T("Nothing wrong found.")));
            foreach (var f in found) checks.Children.Add(f);
            ShowLocal();
        }
        catch (Exception ex) { Log.Here.Warn($"Server tab: {ex.Message}"); }
        finally { loading = false; }
    }

    // ---------------------------------------------------------------- libraries

    private sealed record LibRow(PlexLibrary Lib, long Items, long Unmatched)
    {
        public string Title => Lib.Title;
        public string TypeName => Lib.TypeName;
        public string Folders => Lib.Folders;
        public DateTime? Scanned => Lib.Scanned;
        public string ItemsText => Sizes.Count(Items);
        public string UnmatchedText => Unmatched > 0 ? Sizes.Count(Unmatched) : "";
        public string ScannedText => Lib.Refreshing ? T("scanning…") : PlexUi.Ago(Lib.Scanned);
    }

    private async Task LoadLibraries(PlexClient c, List<FrameworkElement> found)
    {
        try
        {
            var rows = new List<LibRow>();
            foreach (var lib in await service.LibrariesAsync())
            {
                long items = 0, unmatched = 0;
                try
                {
                    items = await service.CountAsync($"/library/sections/{lib.Key}/all");
                    if (lib.Type is "movie" or "show") unmatched = await service.CountAsync($"/library/sections/{lib.Key}/all?unmatched=1");
                }
                catch { }
                rows.Add(new LibRow(lib, items, unmatched));
            }
            libraries.ItemsSource = rows;
            long totalUnmatched = rows.Sum(r => r.Unmatched);
            if (totalUnmatched > 0) found.Add(PlexUi.Finding("info", P((int)totalUnmatched, "{0} item isn't matched to its metadata (Plex Web: the library → filter Unmatched → Fix Match).", "{0} items aren't matched to their metadata (Plex Web: the library → filter Unmatched → Fix Match).")));
            foreach (var r in rows.Where(r => r.Lib.Scanned is { } s && DateTime.Now - s > TimeSpan.FromDays(7) && r.Lib.Type != "photo"))
                found.Add(PlexUi.Finding("info", F("{0} hasn't been scanned for {1} days.", r.Title, (int)(DateTime.Now - r.Lib.Scanned!.Value).TotalDays)));
        }
        catch (Exception ex) { libNote.Text = ex.Message; }
    }

    private void OnLibraries(string pathFormat, HttpMethod method, string what)
    {
        var picked = libraries.SelectedItems.OfType<LibRow>().ToList();
        if (picked.Count == 0) { libNote.Text = T("Pick a library first (Ctrl or Shift for several)."); return; }
        Job(what + " " + string.Join(", ", picked.Select(p => p.Title)), async c =>
        {
            foreach (var p in picked) await c.SendAsync(method, string.Format(pathFormat, p.Lib.Key));
        }, libNote);
    }

    /// <summary>Runs a request to the server, saying in <paramref name="note"/> that it started (Plex does it in the background).</summary>
    private async void Job(string what, Func<PlexClient, Task> run, TextBlock? note = null)
    {
        note ??= jobNote;
        if (service.Client is not { } c) { note.Text = T("Plex isn't set up: Settings → Plex."); return; }
        note.Text = what + "…";
        try
        {
            await run(c);
            note.Text = F("{0}: started (Now Playing shows what the server's busy with).", what);
            Log.Here.Info($"{what}: asked the server");
        }
        catch (Exception ex)
        {
            note.Text = F("{0}: {1}", what, ex.Message);
            Log.Here.Warn($"{what}: {ex.Message}");
        }
    }

    private async Task DownloadLogs()
    {
        if (service.Client is not { } c) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"Plex logs {DateTime.Now:yyyy-MM-dd HHmm}.zip", Filter = "Zip|*.zip" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        jobNote.Text = T("Downloading Plex's logs…");
        try
        {
            await File.WriteAllBytesAsync(dlg.FileName, await c.BytesAsync("/diagnostics/logs"));
            jobNote.Text = F("Saved {0}.", dlg.FileName);
            Ui.ShowInExplorer(dlg.FileName);
        }
        catch (Exception ex) { jobNote.Text = F("Couldn't download the logs: {0}", ex.Message); }
    }

    // ---------------------------------------------------------------- this PC

    private IEnumerable<FrameworkElement> LocalChecks(PlexClient c)
    {
        var list = new List<FrameworkElement>();
        if (PlexLocal.Sqlite == null) list.Add(PlexUi.Finding("warn", T("Plex SQLite wasn't found beside Plex Media Server: the Database tab's check, repair and backups need it.")));
        if (PlexLocal.LibraryPath is { } db)
        {
            var wal = new FileInfo(db + "-wal");
            if (wal.Exists && wal.Length > 512L * 1024 * 1024)
                list.Add(PlexUi.Finding("warn", F("The database's write-ahead log is {0}: it should be small. Optimize Database (or the Database tab's Repair) shrinks it.", Sizes.Bytes(wal.Length)),
                    PlexUi.Small(T("Optimize"), () => Job(T("Optimize the database"), cc => cc.SendAsync(HttpMethod.Put, "/library/optimize?async=1")))));
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(db)!);
                if (drive.AvailableFreeSpace < 10L * 1024 * 1024 * 1024)
                    list.Add(PlexUi.Finding(drive.AvailableFreeSpace < 2L * 1024 * 1024 * 1024 ? "error" : "warn", F("Only {0} free on {1}, where Plex keeps its data: a full disk corrupts its database.", Sizes.Bytes(drive.AvailableFreeSpace), drive.Name)));
            }
            catch { }
        }
        var last = Backups.Newest(service.Settings);
        if (last == null) list.Add(PlexUi.Finding("warn", T("No backup of the database by this app yet (Database tab).")));
        else if (DateTime.Now - last.When > TimeSpan.FromDays(8)) list.Add(PlexUi.Finding("warn", F("The last backup of the database is {0} old (Database tab).", Sizes.Duration(DateTime.Now - last.When))));
        long leftovers = Leftovers().Sum(l => l.Size);
        if (leftovers > 100L * 1024 * 1024) list.Add(PlexUi.Finding("info", F("Leftovers of past repairs (corrupt databases, broken folders) take {0}: below, under This PC.", Sizes.Bytes(leftovers))));
        int recent = RecentErrors(TimeSpan.FromHours(24)).Count;
        if (recent > 50) list.Add(PlexUi.Finding("info", P(recent, "{0} error in Plex's log in the last day (below).", "{0} errors in Plex's log in the last day (below).")));
        return list;
    }

    private async Task<IEnumerable<FrameworkElement>> TautulliChecks()
    {
        var list = new List<FrameworkElement>();
        if (service.Tautulli is not { } t) return list;
        try
        {
            var s = await t.CallAsync("get_server_info");
            string id = PlexClient.Str(s, "pms_identifier") ?? "";
            if (id.Length > 0 && service.MachineId.Length > 0 && !id.Equals(service.MachineId, StringComparison.OrdinalIgnoreCase))
                list.Add(PlexUi.Finding("warn", F("Tautulli has another identity saved for the server ({0}…, Plex's is {1}…): in Tautulli → Settings → Plex Media Server, Verify the server and save.", id[..Math.Min(8, id.Length)], service.MachineId[..Math.Min(8, service.MachineId.Length)])));
        }
        catch (Exception ex) { list.Add(PlexUi.Finding("warn", F("Tautulli: {0}", ex.Message))); }
        return list;
    }

    /// <summary>What past repairs left behind (Plex's -corrupt- copies, its .broken- folders), with sizes.</summary>
    private static List<(string Path, long Size)> Leftovers()
    {
        var list = new List<(string, long)>();
        try
        {
            if (PlexLocal.DatabaseFolder is { } dbs && Directory.Exists(dbs))
                foreach (var f in new DirectoryInfo(dbs).EnumerateFiles("*-corrupt-*")) list.Add((f.FullName, f.Length));
            if (PlexLocal.DataFolder is { } data)
                foreach (var d in new DirectoryInfo(data).EnumerateDirectories("*.broken-*")) list.Add((d.FullName, PlexLocal.FolderSize(d.FullName)));
        }
        catch { }
        return list;
    }

    private static List<string> RecentErrors(TimeSpan within)
    {
        var lines = new List<string>();
        if (PlexLocal.ServerLog is not { } log || !File.Exists(log)) return lines;
        try
        {
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 8 * 1024 * 1024) fs.Seek(-8 * 1024 * 1024, SeekOrigin.End);
            using var r = new StreamReader(fs);
            var since = DateTime.Now - within;
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                if (!line.Contains(" ERROR - ")) continue;
                // "Nov 12, 2026 15:22:50.174 [9512] ERROR - …"
                if (line.Length > 24 && DateTime.TryParseExact(line[..20], "MMM dd, yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at) && at < since) continue;
                lines.Add(line.Length > 400 ? line[..400] + "…" : line);
            }
        }
        catch { }
        return lines;
    }

    private void ShowLocal()
    {
        local.Children.Clear();
        localCard.Visibility = service.IsLocal ? Visibility.Visible : Visibility.Collapsed;
        if (!service.IsLocal) return;
        local.Children.Add(Ui.Header(T("This PC")));
        local.Children.Add(PlexUi.Facts((T("Data"), PlexLocal.DataFolder), (T("Program"), PlexLocal.ProgramFolder)));
        var sizes = Ui.Secondary("", wrap: true);
        sizes.Text = T("Measuring folders…");
        local.Children.Add(sizes);
        _ = Task.Run(() =>
        {
            string? cache = PlexLocal.CacheFolder;
            var parts = new List<string>
            {
                F("Database {0}", Sizes.Bytes(PlexLocal.FolderSize(PlexLocal.DatabaseFolder))),
                F("Metadata {0}", Sizes.Bytes(PlexLocal.FolderSize(PlexLocal.DataFolder is { } d ? Path.Combine(d, "Metadata") : null))),
                F("Media (thumbnails) {0}", Sizes.Bytes(PlexLocal.FolderSize(PlexLocal.DataFolder is { } d2 ? Path.Combine(d2, "Media") : null))),
                F("Cache {0}", Sizes.Bytes(PlexLocal.FolderSize(cache))),
                F("Photo cache {0}", Sizes.Bytes(PlexLocal.FolderSize(cache is { } c ? Path.Combine(c, "PhotoTranscoder") : null))),
                F("Logs {0}", Sizes.Bytes(PlexLocal.FolderSize(PlexLocal.LogFolder))),
            };
            return string.Join(" · ", parts);
        }).ContinueWith(t => Dispatcher.BeginInvoke(() => sizes.Text = t.Result));

        var buttons = PlexUi.Buttons(
            PlexUi.Small(T("Open Data Folder"), () => Ui.Open(PlexLocal.DataFolder ?? "")),
            PlexUi.Small(T("Open Logs"), () => Ui.Open(PlexLocal.LogFolder ?? "")),
            PlexUi.SmallConfirm(T("Clear Photo Cache"), ClearPhotoCache, T("Plex's scaled posters and images; it makes them again as they're needed")),
            PlexUi.SmallConfirm(T("Restart Plex"), () => _ = Restart(), T("Close Plex Media Server and start it again (streams stop)")));
        buttons.Margin = new Thickness(0, 8, 0, 0);
        local.Children.Add(buttons);

        var leftovers = Leftovers();
        if (leftovers.Count > 0)
        {
            local.Children.Add(Ui.Header(T("Leftovers of Past Repairs"), new Thickness(0, 12, 0, 6)));
            foreach (var (path, size) in leftovers)
            {
                string p = path;
                var del = PlexUi.SmallConfirm(T("Delete"), () => { DeleteLeftover(p); ShowLocal(); });
                local.Children.Add(PlexUi.Finding("info", $"{Path.GetFileName(path)} · {Sizes.Bytes(size)}", del));
            }
        }

        var recent = RecentErrors(TimeSpan.FromHours(24));
        local.Children.Add(Ui.Header(F("Errors in Plex's Log, Last Day ({0})", recent.Count), new Thickness(0, 12, 0, 6)));
        errors.Text = recent.Count == 0 ? T("None.") : string.Join("\n", Grouped(recent));
        local.Children.Add(errors);
    }

    /// <summary>The errors by message (numbers left out), the most frequent first: "37×  15:22:50  [Manager] message".</summary>
    private static IEnumerable<string> Grouped(List<string> lines) =>
        lines.Select(l =>
            {
                int at = l.IndexOf(" ERROR - ", StringComparison.Ordinal);
                string msg = at >= 0 ? l[(at + 9)..] : l;
                return (Time: l.Length > 21 ? l[13..21] : "", Msg: msg, Key: System.Text.RegularExpressions.Regex.Replace(msg, @"\d+", "#"));
            })
            .GroupBy(x => x.Key)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Last().Time)
            .Select(g => $"{g.Count(),5}×  {g.Last().Time}  {g.Last().Msg}");

    private void DeleteLeftover(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else File.Delete(path);
            Log.Here.Info($"Deleted {path}");
        }
        catch (Exception ex) { jobNote.Text = F("Couldn't delete {0}: {1}", Path.GetFileName(path), ex.Message); }
    }

    private void ClearPhotoCache()
    {
        if (PlexLocal.CacheFolder is not { } cache) return;
        string dir = Path.Combine(cache, "PhotoTranscoder");
        int failed = 0;
        long freed = 0;
        if (Directory.Exists(dir))
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                try { long n = f.Length; f.Delete(); freed += n; } catch { failed++; }
        jobNote.Text = F("Photo cache cleared: {0} freed", Sizes.Bytes(freed)) + (failed > 0 ? F(" ({0} in use)", failed) : "");
        Log.Here.Info($"Cleared Plex's photo cache: {Sizes.Bytes(freed)}");
        ShowLocal();
    }

    private async Task Restart()
    {
        string? folder = PlexLocal.ProgramFolder;
        jobNote.Text = T("Closing Plex Media Server…");
        if (!await PlexLocal.StopAsync()) { jobNote.Text = T("Plex Media Server didn't close."); return; }
        jobNote.Text = PlexLocal.Start(folder) ? T("Plex Media Server restarted.") : T("Couldn't start Plex Media Server.");
        await Task.Delay(8000);
        service.Reconnect();
    }
}
