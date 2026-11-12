using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex;

/// <summary>A backup of the database: this app's (a folder) or Plex's own (dated copies beside the database).</summary>
public sealed record Backup(string Name, DateTime When, string LibraryFile, string? BlobsFile, long Size, bool Plexs, bool? Ok, string? Problem, string? Before = null)
{
    public string WhenText => When.ToString("g");
    public string SizeText => Sizes.Bytes(Size);
    public string Kind => Plexs ? T("Plex's own") : Before == "repair" ? T("Before a repair") : Before == "restore" ? T("Before a restore") : T("This app's");
    public string State => Ok == true ? T("checked OK") : Ok == false ? Problem ?? T("failed") : "";
}

/// <summary>
/// The database's backups: a PowerShell script this module writes (%LOCALAPPDATA%\DaisysApp\Plex\PlexBackup.ps1) that
/// copies both databases with Plex SQLite's VACUUM INTO (a consistent copy while Plex runs), checks the copies, exports
/// Plex's settings from the registry and keeps the newest N; run by the Database tab's Back Up Now, and on a schedule by
/// a task in Task Scheduler (\DaisysApp\Plex Backup), so it runs whether this app is running or not.
/// </summary>
public static class Backups
{
    public const string TaskName = "Plex Backup";
    private static string TaskPath => $"\\{AppPaths.ShortName}\\{TaskName}";
    private static readonly Regex folderName = new(@"^Plex (\d{4}-\d\d-\d\d \d{6})$");
    private static readonly Regex beforeName = new(@"^Before (repair|restore) (\d{4}-\d\d-\d\d \d{6})$");

    public static string ScriptPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "Plex", "PlexBackup.ps1");

    /// <summary>This app's backups (newest first), then Plex's own (in its backup folder; the Databases folder by default).</summary>
    public static List<Backup> List(PlexSettings s, string? plexBackupFolder = null)
    {
        var list = new List<Backup>();
        string dest = s.BackupFolderOrDefault;
        if (Directory.Exists(dest))
            foreach (var d in new DirectoryInfo(dest).EnumerateDirectories("Plex *"))
            {
                var m = folderName.Match(d.Name);
                if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HHmmss", null, System.Globalization.DateTimeStyles.None, out var when)) continue;
                string lib = Path.Combine(d.FullName, PlexLocal.LibraryDb), blobs = Path.Combine(d.FullName, PlexLocal.BlobsDb);
                bool? ok = null;
                string? problem = null;
                try
                {
                    string json = Path.Combine(d.FullName, "backup.json");
                    if (File.Exists(json))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(json));
                        ok = doc.RootElement.TryGetProperty("Ok", out var o) && o.ValueKind == JsonValueKind.True;
                        if (doc.RootElement.TryGetProperty("Error", out var e)) problem = e.GetString();
                    }
                }
                catch { }
                if (!File.Exists(lib)) { ok = false; problem ??= T("incomplete"); }
                list.Add(new Backup(d.Name, when, lib, File.Exists(blobs) ? blobs : null, PlexLocal.FolderSize(d.FullName), false, ok, problem));
            }
        // the database as it was before a repair or restore (with its write-ahead log)
        if (Directory.Exists(dest))
            foreach (var d in new DirectoryInfo(dest).EnumerateDirectories("Before *"))
            {
                var m = beforeName.Match(d.Name);
                string lib = Path.Combine(d.FullName, PlexLocal.LibraryDb), blobs = Path.Combine(d.FullName, PlexLocal.BlobsDb);
                if (!m.Success || !File.Exists(lib) || !DateTime.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd HHmmss", null, System.Globalization.DateTimeStyles.None, out var when)) continue;
                list.Add(new Backup(d.Name, when, lib, File.Exists(blobs) ? blobs : null, PlexLocal.FolderSize(d.FullName), false, null, null, m.Groups[1].Value));
            }
        list.Sort((a, b) => b.When.CompareTo(a.When));

        string? own = plexBackupFolder is { Length: > 0 } ? plexBackupFolder : PlexLocal.DatabaseFolder;
        var plexs = new List<Backup>();
        if (own != null && Directory.Exists(own))
            foreach (var f in new DirectoryInfo(own).EnumerateFiles(PlexLocal.LibraryDb + "-????-??-??"))
            {
                string date = f.Name[(PlexLocal.LibraryDb.Length + 1)..];
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var when)) continue;
                var blobs = new FileInfo(Path.Combine(own, PlexLocal.BlobsDb + "-" + date));
                plexs.Add(new Backup(f.Name, f.LastWriteTime.Date == when ? f.LastWriteTime : when, f.FullName, blobs.Exists ? blobs.FullName : null,
                    f.Length + (blobs.Exists ? blobs.Length : 0), true, null, null));
            }
        list.AddRange(plexs.OrderByDescending(b => b.When));
        return list;
    }

    /// <summary>The newest of this app's backups that worked, or null.</summary>
    public static Backup? Newest(PlexSettings s)
    {
        try { return List(s).FirstOrDefault(b => !b.Plexs && b.Before == null && b.Ok != false); }
        catch { return null; }
    }

    // ---------------------------------------------------------------- the script

    /// <summary>Writes the script with the current folders (again whenever they change; it finds Plex SQLite by itself if it moved).</summary>
    public static string WriteScript(PlexSettings s)
    {
        string Q(string? v) => "'" + (v ?? "").Replace("'", "''") + "'";
        string script = $$"""
            # Backs up Plex Media Server's database: both databases copied with Plex SQLite's VACUUM INTO (a consistent copy
            # while Plex runs), each copy checked, Plex's settings exported from the registry, the newest {{s.BackupKeep}} kept.
            # Written by Daisy's App (Plex → Database), run by Task Scheduler (\{{AppPaths.ShortName}}\{{TaskName}}) or Back Up Now.
            # It doesn't need Daisy's App to run.
            $ErrorActionPreference = 'Stop'
            $Sqlite = {{Q(PlexLocal.Sqlite)}}
            $Databases = {{Q(PlexLocal.DatabaseFolder)}}
            $Dest = {{Q(s.BackupFolderOrDefault)}}
            $Keep = {{Math.Max(1, s.BackupKeep)}}

            New-Item -ItemType Directory -Force -Path $Dest | Out-Null
            $log = Join-Path $Dest 'Backups.log'
            function Say([string]$text) {
                $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $text
                Add-Content -LiteralPath $log -Value $line -Encoding UTF8
                Write-Output $line
            }
            $dir = Join-Path $Dest ('Plex ' + (Get-Date -Format 'yyyy-MM-dd HHmmss'))
            try {
                if (-not (Test-Path -LiteralPath $Sqlite)) {
                    $p = Get-Process 'Plex Media Server' -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($p) { $Sqlite = Join-Path (Split-Path $p.Path) 'Plex SQLite.exe' }
                }
                if (-not (Test-Path -LiteralPath $Sqlite)) { throw "Plex SQLite wasn't found ($Sqlite)" }
                New-Item -ItemType Directory -Force -Path $dir | Out-Null
                $ok = $true
                $checks = @{}
                foreach ($name in '{{PlexLocal.LibraryDb}}', '{{PlexLocal.BlobsDb}}') {
                    $src = Join-Path $Databases $name
                    if (-not (Test-Path -LiteralPath $src)) { if ($name -like '*library.db') { throw "$src isn't there" } else { continue } }
                    $target = Join-Path $dir $name
                    $t = $target.Replace('\', '/').Replace("'", "''")
                    $out = & $Sqlite $src "VACUUM INTO '$t';" 2>&1 | Out-String
                    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $target)) { throw "${name} couldn't be copied: $($out.Trim())" }
                    $check = (& $Sqlite $target 'PRAGMA quick_check;' 2>&1 | Out-String).Trim()
                    $checks[$name] = $check
                    if ($check -ne 'ok') { $ok = $false }
                    Say ("${name}: " + [math]::Round((Get-Item -LiteralPath $target).Length / 1MB) + " MB, check: $check")
                }
                & reg.exe export 'HKCU\Software\Plex, Inc.\Plex Media Server' (Join-Path $dir 'Preferences.reg') /y | Out-Null
                [ordered]@{ When = (Get-Date).ToString('o'); Ok = $ok; Checks = $checks } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir 'backup.json') -Encoding UTF8
                Say ("Backed up to $dir" + $(if ($ok) { '' } else { ' (a copy failed its check)' }))
                # keep the newest $Keep good backups; a run that was stopped part way leaves a folder without backup.json
                $all = Get-ChildItem -LiteralPath $Dest -Directory | Where-Object { $_.Name -match '^Plex \d{4}-\d\d-\d\d \d{6}$' } | Sort-Object Name -Descending
                $good = $all | Where-Object {
                    $j = Join-Path $_.FullName 'backup.json'
                    (Test-Path -LiteralPath $j) -and ((Get-Content -LiteralPath $j -Raw | ConvertFrom-Json).Ok -eq $true)
                }
                $old = @($good | Select-Object -Skip $Keep) + @($all | Where-Object { $good -notcontains $_ -and $_.LastWriteTime -lt (Get-Date).AddHours(-12) })
                foreach ($d in $old | Where-Object { $_ }) {
                    Remove-Item -LiteralPath $d.FullName -Recurse -Force
                    Say "Removed the old backup $($d.Name)"
                }
                if ($ok) { exit 0 } else { exit 2 }
            } catch {
                Say "FAILED: $_"
                # what it copied is no use: only the folder and what went wrong are kept
                if (Test-Path -LiteralPath $dir) {
                    Get-ChildItem -LiteralPath $dir -Filter '*.db' | Remove-Item -Force -ErrorAction SilentlyContinue
                    [ordered]@{ When = (Get-Date).ToString('o'); Ok = $false; Error = "$_" } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir 'backup.json') -Encoding UTF8
                }
                exit 1
            }

            """;
        Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
        // with a BOM: Windows PowerShell reads a file without one in the ANSI code page
        File.WriteAllText(ScriptPath, script.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
        return ScriptPath;
    }

    /// <summary>Runs the script now (as the task would); its exit code and what it said.</summary>
    public static async Task<(int Code, string Output)> RunNowAsync(PlexSettings s, CancellationToken cancel)
    {
        string script = WriteScript(s);
        Log.Here.Info("Backing up Plex's database");
        var r = await PlexLocal.Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), cancel,
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script);
        Log.Here.Info($"Backup finished ({r.Code}): {r.Output.Split('\n').LastOrDefault()?.Trim()}");
        return r;
    }

    // ---------------------------------------------------------------- the task

    /// <summary>Registers (or replaces) the scheduled task with the settings' time and days; null if it worked, else why not.</summary>
    public static async Task<string?> ScheduleAsync(PlexSettings s)
    {
        string script = WriteScript(s);
        if (!TimeSpan.TryParse(s.BackupTime, out var at)) at = TimeSpan.FromHours(4);
        string start = (DateTime.Today + at).ToString("yyyy-MM-ddTHH:mm:ss");
        string schedule = s.BackupWeekly
            ? $"<ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek><{s.BackupDay}/></DaysOfWeek></ScheduleByWeek>"
            : "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>";
        string user = System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string args = System.Security.SecurityElement.Escape($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"");
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Backs up Plex Media Server's database (set up in Daisy's App → Plex → Database; runs without it).</Description></RegistrationInfo>
              <Triggers><CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled>{schedule}</CalendarTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <!-- Priority 4 (normal): at Task Scheduler's default 7 the backup's disk work runs at low priority -->
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT4H</ExecutionTimeLimit>
                <Priority>4</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>powershell.exe</Command><Arguments>{args}</Arguments></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), "DaisysApp-PlexBackup.xml");
        await File.WriteAllTextAsync(file, xml, Encoding.Unicode);
        try
        {
            var r = await PlexLocal.Run(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Create", "/TN", TaskPath, "/XML", file, "/F");
            if (r.Code != 0) { Log.Here.Warn($"Couldn't schedule the backup: {r.Output}"); return r.Output; }
            Log.Here.Info($"Scheduled Plex's backup: {(s.BackupWeekly ? s.BackupDay.ToString() + "s" : "daily")} at {at:hh\\:mm}");
            return null;
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static async Task<string?> UnscheduleAsync()
    {
        if (!await ScheduledAsync()) return null;
        var r = await PlexLocal.Run(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Delete", "/TN", TaskPath, "/F");
        if (r.Code != 0) return r.Output;
        Log.Here.Info("Removed the scheduled backup");
        return null;
    }

    public static async Task<bool> ScheduledAsync() =>
        (await PlexLocal.Run(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Query", "/TN", TaskPath)).Code == 0;

    /// <summary>When the task runs next, from the settings.</summary>
    public static DateTime Next(PlexSettings s)
    {
        if (!TimeSpan.TryParse(s.BackupTime, out var at)) at = TimeSpan.FromHours(4);
        var t = DateTime.Today + at;
        if (s.BackupWeekly) while (t.DayOfWeek != s.BackupDay || t <= DateTime.Now) t = t.AddDays(1);
        else if (t <= DateTime.Now) t = t.AddDays(1);
        return t;
    }
}
