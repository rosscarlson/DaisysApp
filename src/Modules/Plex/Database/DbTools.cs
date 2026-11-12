using System.IO;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex;

/// <summary>
/// The database's check, repair and restore, on the server's own PC, with Plex SQLite (Plex's own build, with the
/// tokenizers and collations its database needs). The check reads the database while Plex runs; repair and restore
/// close Plex first, keep a copy of the database as it was ("Before repair …" / "Before restore …" in the backup
/// folder), and start Plex again after. Each step is reported through <c>say</c>.
/// </summary>
public static class DbTools
{
    private static string[] Files(string db) => [db, db + "-wal", db + "-shm"];

    /// <summary>PRAGMA integrity_check (or quick_check) of both databases; true if both are "ok".</summary>
    public static async Task<bool> CheckAsync(Action<string> say, bool quick, CancellationToken cancel)
    {
        bool ok = true;
        foreach (var db in new[] { PlexLocal.LibraryPath, PlexLocal.BlobsPath })
        {
            if (db == null || !File.Exists(db)) continue;
            say(F("Checking {0}…", Path.GetFileName(db)));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (code, output) = await PlexLocal.Sql(db, cancel, "-readonly", quick ? "PRAGMA quick_check;" : "PRAGMA integrity_check;");
            bool fine = code == 0 && output.Trim() == "ok";
            ok &= fine;
            say(fine ? F("{0}: OK ({1})", Path.GetFileName(db), Sizes.Took(sw.Elapsed))
                : F("{0}: PROBLEMS\n{1}", Path.GetFileName(db), Trim(output)));
            Log.Here.Info($"Integrity check of {Path.GetFileName(db)}: {(fine ? "ok" : Trim(output, 300))}");
        }
        return ok;
    }

    /// <summary>
    /// Closes Plex, keeps a copy, then for each database: if it checks OK, folds in its write-ahead log, VACUUMs
    /// and REINDEXes it; if not, writes it out as SQL (.dump, or .recover when the dump stops at an error), reads that
    /// into a new database, checks it and puts it in place. Starts Plex again.
    /// </summary>
    public static async Task<bool> RepairAsync(PlexSettings s, Action<string> say, CancellationToken cancel)
    {
        string? folder = PlexLocal.ProgramFolder;
        if (PlexLocal.Sqlite == null) throw new PlexException(T("Plex SQLite wasn't found (it comes with Plex Media Server)."));
        string work = Path.Combine(s.BackupFolderOrDefault, $"Before repair {DateTime.Now:yyyy-MM-dd HHmmss}");
        long need = new[] { PlexLocal.LibraryPath, PlexLocal.BlobsPath }.Sum(p => p != null ? Files(p).Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0) : 0);
        Room(work, need * 3, say);

        bool wasRunning = PlexLocal.Running;
        say(T("Closing Plex Media Server…"));
        if (!await PlexLocal.StopAsync()) throw new PlexException(T("Plex Media Server didn't close: nothing was changed."));
        bool allOk = true;
        try
        {
            Directory.CreateDirectory(work);
            say(F("Copying the database as it is to {0}…", work));
            foreach (var db in new[] { PlexLocal.LibraryPath, PlexLocal.BlobsPath })
                if (db != null)
                    foreach (var f in Files(db).Where(File.Exists))
                        await CopyAsync(f, Path.Combine(work, Path.GetFileName(f)), cancel);

            foreach (var db in new[] { PlexLocal.LibraryPath, PlexLocal.BlobsPath })
            {
                if (db == null || !File.Exists(db)) continue;
                string name = Path.GetFileName(db);
                say(F("Checking {0}…", name));
                var (code, output) = await PlexLocal.Sql(db, cancel, "PRAGMA integrity_check;");
                if (code == 0 && output.Trim() == "ok")
                {
                    say(F("{0} is OK: folding in its log, compacting and reindexing…", name));
                    var r = await PlexLocal.Sql(db, cancel, "PRAGMA wal_checkpoint(TRUNCATE);", "VACUUM;", "REINDEX;", "PRAGMA integrity_check;");
                    bool fine = r.Code == 0 && r.Output.Trim().EndsWith("ok");
                    say(fine ? F("{0}: done.", name) : F("{0}: {1}", name, Trim(r.Output)));
                    allOk &= fine;
                    continue;
                }
                say(F("{0} has problems:\n{1}\nRebuilding it…", name, Trim(output)));
                allOk &= await Rebuild(db, work, say, cancel);
            }
        }
        catch (OperationCanceledException) { say(T("Stopped.")); allOk = false; }
        catch (Exception ex) { say(F("Failed: {0}", ex.Message)); Log.Here.Error("Database repair", ex); allOk = false; }
        finally
        {
            if (wasRunning)
            {
                say(T("Starting Plex Media Server…"));
                PlexLocal.Start(folder);
            }
        }
        say(allOk ? F("Repaired. The database as it was is kept in {0}.", work) : F("Not everything worked: the database as it was is kept in {0} (Restore puts it back).", work));
        Log.Here.Info($"Database repair finished: {(allOk ? "ok" : "with problems")}");
        return allOk;
    }

    private static async Task<bool> Rebuild(string db, string work, Action<string> say, CancellationToken cancel)
    {
        string name = Path.GetFileName(db);
        string sql = Path.Combine(work, name + ".sql"), fresh = Path.Combine(work, name + ".new");
        say(T("Writing it out as SQL…"));
        var r = await PlexLocal.Sql(db, cancel, ".output " + PlexLocal.Quote(sql), ".dump");
        if (r.Code != 0 || EndsWithRollback(sql))
        {
            say(T("The dump stopped at an error: recovering what can be read…"));
            r = await PlexLocal.Sql(db, cancel, ".output " + PlexLocal.Quote(sql), ".recover");
            if (r.Code != 0) { say(F("Couldn't recover it: {0}", Trim(r.Output))); return false; }
        }
        if (File.Exists(fresh)) File.Delete(fresh);
        say(T("Reading it into a new database…"));
        r = await PlexLocal.Sql(fresh, cancel, ".read " + PlexLocal.Quote(sql));
        if (r.Output.Length > 0) say(Trim(r.Output));
        var check = await PlexLocal.Sql(fresh, cancel, "PRAGMA integrity_check;");
        if (check.Code != 0 || check.Output.Trim() != "ok") { say(F("The new database has problems too:\n{0}\nThe old one is left in place.", Trim(check.Output))); return false; }
        foreach (var f in Files(db)) if (File.Exists(f)) File.Delete(f);
        File.Move(fresh, db);
        File.Delete(sql);
        say(F("{0} rebuilt and checked OK.", name));
        return true;
    }

    private static bool EndsWithRollback(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            fs.Seek(-Math.Min(400, fs.Length), SeekOrigin.End);
            using var r = new StreamReader(fs);
            return r.ReadToEnd().Contains("ROLLBACK");
        }
        catch { return true; }
    }

    /// <summary>
    /// Puts a backup back: checks it first, closes Plex, moves the database as it is to "Before restore …", copies the
    /// backup's files in (no log files: the backup is whole), starts Plex.
    /// </summary>
    public static async Task<bool> RestoreAsync(PlexSettings s, Backup backup, Action<string> say, CancellationToken cancel)
    {
        string? folder = PlexLocal.ProgramFolder;
        bool wasRunning = PlexLocal.Running;
        if (PlexLocal.DatabaseFolder is not { } dbs || PlexLocal.LibraryPath is not { } lib || PlexLocal.BlobsPath is not { } blobs) throw new PlexException(T("Plex's database folder wasn't found."));
        say(F("Checking the backup {0}…", backup.Name));
        var (code, output) = await PlexLocal.Sql(backup.LibraryFile, cancel, "-readonly", "PRAGMA quick_check;");
        if (code != 0 || output.Trim() != "ok") throw new PlexException(F("The backup doesn't check OK, so it wasn't restored: {0}", Trim(output, 300)));

        string keep = Path.Combine(s.BackupFolderOrDefault, $"Before restore {DateTime.Now:yyyy-MM-dd HHmmss}");
        Room(dbs, backup.Size, say);
        say(T("Closing Plex Media Server…"));
        if (!await PlexLocal.StopAsync()) throw new PlexException(T("Plex Media Server didn't close: nothing was changed."));
        bool ok = false;
        try
        {
            Directory.CreateDirectory(keep);
            say(F("Moving the database as it is to {0}…", keep));
            foreach (var f in Files(lib).Concat(backup.BlobsFile != null ? Files(blobs) : []).Where(File.Exists))
                File.Move(f, Path.Combine(keep, Path.GetFileName(f)));
            say(T("Copying the backup in…"));
            // (a copy from before a repair or restore has its write-ahead log, with the latest changes, beside it)
            foreach (var (from, to) in new[] { (backup.LibraryFile, lib), (backup.BlobsFile, blobs) })
            {
                if (from == null) continue;
                await CopyAsync(from, to, cancel);
                if (File.Exists(from + "-wal")) await CopyAsync(from + "-wal", to + "-wal", cancel);
            }
            ok = true;
            say(F("Restored {0}. The database it replaced is kept in {1}.", backup.Name, keep));
            Log.Here.Info($"Restored the database from {backup.Name}");
        }
        catch (Exception ex)
        {
            say(F("Failed: {0}", ex.Message));
            Log.Here.Error("Database restore", ex);
            // put back what was moved, if it isn't there
            foreach (var f in Directory.Exists(keep) ? Directory.GetFiles(keep) : [])
                try { string to = Path.Combine(dbs, Path.GetFileName(f)); if (!File.Exists(to)) File.Move(f, to); } catch { }
        }
        finally
        {
            if (wasRunning)
            {
                say(T("Starting Plex Media Server…"));
                PlexLocal.Start(folder);
            }
        }
        return ok;
    }

    private static void Room(string folder, long need, Action<string> say)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder.TrimEnd('\\')) ?? folder);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if (drive.AvailableFreeSpace < need)
                throw new PlexException(F("Not enough room on {0}: {1} needed, {2} free.", drive.Name, Sizes.Bytes(need), Sizes.Bytes(drive.AvailableFreeSpace)));
        }
        catch (PlexException) { throw; }
        catch (Exception ex) { say(F("(Couldn't measure the free space: {0})", ex.Message)); }
    }

    private static async Task CopyAsync(string from, string to, CancellationToken cancel)
    {
        await using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, true);
        await using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
        await src.CopyToAsync(dst, 1 << 20, cancel);
    }

    private static string Trim(string s, int max = 2000) => s.Trim() is var t && t.Length > max ? t[..max] + "…" : s.Trim();
}
