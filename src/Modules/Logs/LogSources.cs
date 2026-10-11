using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Logs;

/// <summary>
/// Something a Logs tab can show. Each has a stable key (saved with the open tabs) and loads its entries since a time.
/// New kinds of log are added here and in <see cref="LogSources.Create"/> and the Add menu.
/// </summary>
public abstract class LogSource
{
    public abstract string Key { get; }
    public abstract string Title { get; }

    /// <summary>One line under the tab's toolbar saying what this is.</summary>
    public abstract string Description { get; }

    /// <summary>Whether entries arrive by themselves while the tab is open (<see cref="Live"/>).</summary>
    public virtual bool IsLive => false;

    /// <summary>The entries since <paramref name="since"/>, newest first.</summary>
    public abstract List<LogRow> Load(DateTime since, CancellationToken ct);

    /// <summary>For a live source: whether a new line of a log belongs to this tab.</summary>
    public virtual LogRow? Live(string logName, LogEntry entry) => null;

    /// <summary>A zip of the raw files behind the source, if it has some worth sending; else null.</summary>
    public virtual Action<string>? ZipExport => null;
}

/// <summary>Daisy's App's own logs: every applet's (merged), or one applet's.</summary>
public sealed class AppLogSource(string? name) : LogSource
{
    public string? Name { get; } = name;

    public override string Key => Name == null ? "app" : "app:" + Name;
    public override string Title => Name == null ? T("Daisy's App") : F("Daisy's App: {0}", Name);
    public override string Description => Name == null
        ? F("The app's log and every applet's, merged. One file a day each in {0}, kept for {1} days.", Log.Folder, Log.KeepDays)
        : F("The {0} log. One file a day in {1}, kept for {2} days.", Name, Log.Folder, Log.KeepDays);
    public override bool IsLive => true;

    private IEnumerable<(string Path, string Name, DateTime Day)> FilesSince(DateTime since) =>
        Log.Files().Where(f => f.Day >= since.Date && (Name == null || f.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)));

    public override List<LogRow> Load(DateTime since, CancellationToken ct)
    {
        var rows = new List<LogRow>();
        foreach (var f in FilesSince(since))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var e in Log.ReadFile(f.Path))
                    if (e.Time >= since) rows.Add(Row(f.Name, e));
            }
            catch (Exception ex) { rows.Add(new LogRow(DateTime.Now, LogLevel.Warning, f.Name, "", F("Couldn't read {0}: {1}", f.Path, ex.Message))); }
        }
        rows.Sort((a, b) => b.Time.CompareTo(a.Time));
        return rows;
    }

    public override LogRow? Live(string logName, LogEntry entry) =>
        Name == null || logName.Equals(Name, StringComparison.OrdinalIgnoreCase) ? Row(logName, entry) : null;

    private static LogRow Row(string logName, LogEntry e) => new(e.Time, e.Level, logName, "", e.Message, e.Thread);

    /// <summary>Every log file there is (or this applet's), and what Windows recorded about crashes of the app.</summary>
    public override Action<string>? ZipExport => path =>
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var f in FilesSince(DateTime.MinValue))
        {
            // the file is open for writing: copy what's there
            using var src = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = zip.CreateEntry(Path.GetFileName(f.Path)).Open();
            src.CopyTo(dst);
        }
        var crashes = WindowsEvents.AppCrashes(DateTime.Now.AddDays(-30), 50);
        using (var w = new StreamWriter(zip.CreateEntry("windows-crash-records.txt").Open(), new UTF8Encoding(false)))
        {
            w.WriteLine($"Crashes and hangs of DaisysApp.exe in Windows' Application log, last 30 days: {crashes.Count}");
            foreach (var e in crashes)
            {
                w.WriteLine();
                w.WriteLine($"{e.Time:yyyy-MM-dd HH:mm:ss} {e.Provider} (event {e.Id})");
                w.WriteLine(e.Message);
            }
        }
        using (var w = new StreamWriter(zip.CreateEntry("about.txt").Open(), new UTF8Encoding(false)))
        {
            w.WriteLine($"Exported {DateTime.Now:yyyy-MM-dd HH:mm:ss} from {Environment.MachineName}");
            w.WriteLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
            w.WriteLine($"Program {Environment.ProcessPath}, version {Process.GetCurrentProcess().MainModule?.FileVersionInfo.ProductVersion}");
            w.WriteLine($"Logging: {(Log.DebugEnabled ? "Debug" : "Normal")}");
        }
    };
}

/// <summary>A Windows event log (Application, System, …), optionally narrowed by an XPath condition and a text.</summary>
public sealed class WindowsLogSource(string logName, string? key = null, string? title = null, string? filter = null, string? mustContain = null, string? description = null) : LogSource
{
    public string LogName { get; } = logName;
    public const int MaxEntries = 5000;

    public override string Key => key ?? "win:" + LogName;
    public override string Title => title ?? F("Windows: {0}", LogName);
    public override string Description => description ?? F("Windows' {0} event log (the newest {1:N0} entries in the time chosen), as in Event Viewer.", LogName, MaxEntries);

    public override List<LogRow> Load(DateTime since, CancellationToken ct) =>
        WindowsEvents.Read(LogName, since, mustContain == null ? MaxEntries : MaxEntries * 4, filter, ct)
            .Where(e => mustContain == null || e.Message.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
            .Take(MaxEntries)
            .Select(e => new LogRow(e.Time, e.Level, e.Provider, e.Id.ToString(CultureInfo.InvariantCulture), e.Message))
            .ToList();

    /// <summary>Crashes and hangs of DaisysApp.exe that Windows recorded.</summary>
    public static WindowsLogSource AppCrashes() => new("Application", "win-crashes", T("Daisy's App crashes (Windows)"),
        "(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime'] or Provider[@Name='Application Hang'] or Provider[@Name='Windows Error Reporting'])",
        "DaisysApp.exe", T("What Windows recorded when DaisysApp.exe crashed or stopped responding (Application Error, .NET Runtime, Application Hang and Windows Error Reporting in the Application log)."));
}

/// <summary>A WSL distribution's system journal (or its kernel messages when it has no journal).</summary>
public sealed class WslSource(string distro) : LogSource
{
    public string Distro { get; } = distro;
    public const int MaxEntries = 5000;

    public override string Key => "wsl:" + Distro;
    public override string Title => F("WSL: {0}", Distro);
    public override string Description => F("The system journal of the WSL distribution {0} since it last started (its kernel messages if it has no journal). Loading it starts WSL if it isn't running.", Distro);

    /// <summary>The installed WSL distributions; empty if WSL isn't installed.</summary>
    public static List<string> Distros()
    {
        try
        {
            string wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
            if (!File.Exists(wsl)) return [];
            var (code, output) = Run(wsl, "--list --quiet", 10000, CancellationToken.None);
            if (code != 0) return [];
            return output.Split('\n').Select(s => s.Trim().Trim('\0')).Where(s => s.Length > 0 && !s.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch { return []; }
    }

    public override List<LogRow> Load(DateTime since, CancellationToken ct)
    {
        string wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        long unix = new DateTimeOffset(since).ToUnixTimeSeconds();
        string script = $"if command -v journalctl >/dev/null 2>&1 && journalctl -q -n 1 >/dev/null 2>&1; then journalctl --no-pager -o json --output-fields=PRIORITY,SYSLOG_IDENTIFIER,_COMM,MESSAGE --since=@{unix} -r -n {MaxEntries}; else echo DMESG; dmesg --time-format iso -x 2>/dev/null || dmesg -x; fi";
        var (code, output) = Run(wsl, $"-d {Distro} -u root -e sh -c \"{script}\"", 60000, ct); // (wsl.exe takes quotes round the name as part of it; names have no spaces)
        var rows = new List<LogRow>();
        var lines = output.Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "DMESG")
        {
            foreach (var line in lines.Skip(1)) if (Dmesg(line) is { } r && r.Time >= since) rows.Add(r);
            rows.Reverse();
        }
        else
        {
            foreach (var line in lines) if (Journal(line) is { } r) rows.Add(r);
        }
        if (rows.Count == 0 && code != 0)
            rows.Add(new LogRow(DateTime.Now, LogLevel.Warning, "wsl", "", F("WSL didn't return the log (exit code {0}): {1}", code, output.Trim())));
        return rows.Take(MaxEntries).ToList();
    }

    private static LogLevel Priority(int p) => p <= 3 ? LogLevel.Error : p == 4 ? LogLevel.Warning : p == 7 ? LogLevel.Debug : LogLevel.Info;

    private static LogRow? Journal(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string Str(string name) => root.TryGetProperty(name, out var v) ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                // a message that isn't valid UTF-8 comes as an array of bytes
                JsonValueKind.Array => Encoding.UTF8.GetString(v.EnumerateArray().Select(b => (byte)b.GetInt32()).ToArray()),
                _ => v.ToString(),
            } : "";
            var time = long.TryParse(Str("__REALTIME_TIMESTAMP"), out long us) ? DateTimeOffset.FromUnixTimeMilliseconds(us / 1000).LocalDateTime : DateTime.MinValue;
            int prio = int.TryParse(Str("PRIORITY"), out int p) ? p : 6;
            string source = Str("SYSLOG_IDENTIFIER") is { Length: > 0 } id ? id : Str("_COMM");
            return new LogRow(time, Priority(prio), source, "", NoColours(Str("MESSAGE")));
        }
        catch { return null; }
    }

    // "kern  :info  : 2026-10-10T21:36:08,123456-05:00 message"
    private static LogRow? Dmesg(string line)
    {
        var parts = line.Split(':', 3);
        if (parts.Length < 3) return null;
        string facility = parts[0].Trim(), level = parts[1].Trim(), rest = parts[2].TrimStart();
        int space = rest.IndexOf(' ');
        if (space < 0) return null;
        string stamp = rest[..space].Replace(',', '.');
        if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        var lvl = level switch
        {
            "emerg" or "alert" or "crit" or "err" => LogLevel.Error,
            "warn" => LogLevel.Warning,
            "debug" => LogLevel.Debug,
            _ => LogLevel.Info,
        };
        return new LogRow(t.LocalDateTime, lvl, facility, "", NoColours(rest[(space + 1)..]));
    }

    private static readonly System.Text.RegularExpressions.Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Without the terminal colour codes some programs put in their messages.</summary>
    private static string NoColours(string s) => s.Contains('\x1B') ? Ansi.Replace(s, "") : s;

    private static (int Code, string Output) Run(string exe, string args, int timeoutMs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["WSL_UTF8"] = "1"; // wsl.exe's own messages in UTF-8 too (they're UTF-16 otherwise)
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException(T("WSL took too long to answer."));
        }
        string output = stdout.Result;
        if (output.Length == 0) output = stderr.Result;
        return (p.ExitCode, output);
    }
}

/// <summary>Turns a saved key back into its source, and lists what can be added.</summary>
public static class LogSources
{
    public static LogSource? Create(string key)
    {
        if (key == "app") return new AppLogSource(null);
        if (key.StartsWith("app:", StringComparison.Ordinal)) return new AppLogSource(key[4..]);
        if (key == "win-crashes") return WindowsLogSource.AppCrashes();
        if (key.StartsWith("win:", StringComparison.Ordinal)) return new WindowsLogSource(key[4..]);
        if (key.StartsWith("wsl:", StringComparison.Ordinal)) return new WslSource(key[4..]);
        return null;
    }

    /// <summary>The names of Daisy's App's logs: the app first, then every applet that has written one.</summary>
    public static List<string> AppLogNames() =>
        Log.Files().Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n.Equals(Log.AppName, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
}
