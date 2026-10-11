using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DaisysApp.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// The app's logs: one file a day for the app and for each module, named after it with the date
/// (<c>DaisysApp-10-10-26.log</c>, <c>AudioTools-10-10-26.log</c>) in %LOCALAPPDATA%\DaisysApp\logs, kept for
/// <see cref="KeepDays"/> days. Normal logging records what the app does (starting, stopping, settings changed, devices
/// found, errors); Debug (Settings → General → Logging) adds the detail. Each line is written straight to disk, so a
/// crash loses nothing that was logged before it. The Logs tab reads them.
/// <code>
/// static readonly Logger log = Log.For("AudioTools");   // or Log.Here in the module's own code
/// log.Info("Playback started on " + device);
/// log.Debug(() => $"buffer {n} frames");                 // only built when Debug logging is on
/// log.Error("Couldn't open the mic", ex);
/// </code>
/// </summary>
public static class Log
{
    /// <summary>How many days of logs are kept (today and the six before it).</summary>
    public const int KeepDays = 7;

    /// <summary>The date in a log's file name: month, day, two-digit year.</summary>
    public const string DateFormat = "MM-dd-yy";

    /// <summary>The app's own name in the logs.</summary>
    public const string AppName = AppPaths.ShortName;

    /// <summary>Whether Debug lines are written (Settings → General → Logging).</summary>
    public static bool DebugEnabled { get; set; }

    public static string Folder => AppPaths.LogFolder;

    private static readonly ConcurrentDictionary<string, Logger> loggers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app's own log.</summary>
    public static Logger App => For(AppName);

    /// <summary>The log of the module (or the app) whose code calls this.</summary>
    public static Logger Here
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => For(NameOf(Assembly.GetCallingAssembly()));
    }

    /// <summary>The log named <paramref name="name"/> (spaces and punctuation are left out of it).</summary>
    public static Logger For(string name) => loggers.GetOrAdd(CleanName(name), n => new Logger(n));

    /// <summary>Raised for every line written (from any thread): the log's name and the entry.</summary>
    public static event Action<string, LogEntry>? Written;

    internal static void Raise(string name, LogEntry entry)
    {
        try { Written?.Invoke(name, entry); } catch { }
    }

    /// <summary>"DaisysApp.AudioTools" → "AudioTools"; the app and DaisysApp.Core → "DaisysApp".</summary>
    public static string NameOf(Assembly? asm)
    {
        string name = asm?.GetName().Name ?? AppName;
        if (name.Equals(AppName, StringComparison.OrdinalIgnoreCase) || name.Equals(AppName + ".Core", StringComparison.OrdinalIgnoreCase)) return AppName;
        return name.StartsWith(AppName + ".", StringComparison.OrdinalIgnoreCase) ? name[(AppName.Length + 1)..] : name;
    }

    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.Length == 0 ? AppName : sb.ToString();
    }

    public static string FileName(string name, DateTime day) => $"{CleanName(name)}-{day.ToString(DateFormat, CultureInfo.InvariantCulture)}.log";

    public static string PathOf(string name, DateTime day) => Path.Combine(Folder, FileName(name, day));

    private static readonly Regex FileNamePattern = new(@"^(?<name>[A-Za-z0-9]+)-(?<date>\d\d-\d\d-\d\d)\.log$", RegexOptions.Compiled);

    /// <summary>A log file's name and day, or null if it isn't one of ours.</summary>
    public static (string Name, DateTime Day)? ParseFileName(string fileName)
    {
        var m = FileNamePattern.Match(Path.GetFileName(fileName));
        if (!m.Success || !DateTime.TryParseExact(m.Groups["date"].Value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return null;
        return (m.Groups["name"].Value, day);
    }

    /// <summary>Every log file there is, newest day first.</summary>
    public static List<(string Path, string Name, DateTime Day)> Files()
    {
        var list = new List<(string, string, DateTime)>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var path in Directory.GetFiles(Folder, "*.log"))
                if (ParseFileName(path) is { } p) list.Add((path, p.Name, p.Day));
        }
        catch { }
        return list.OrderByDescending(f => f.Item3).ThenBy(f => f.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Deletes logs older than <see cref="KeepDays"/> days (and the old errors.log once it's that old).</summary>
    public static void Prune()
    {
        var oldest = DateTime.Today.AddDays(-(KeepDays - 1));
        foreach (var f in Files())
            if (f.Day < oldest)
                try { File.Delete(f.Path); } catch { }
        try
        {
            string legacy = Path.Combine(Folder, "errors.log");
            if (File.Exists(legacy) && File.GetLastWriteTime(legacy) < oldest) File.Delete(legacy);
        }
        catch { }
    }

    /// <summary>Closes every open log file (on exit).</summary>
    public static void Close()
    {
        foreach (var l in loggers.Values) l.Close();
    }

    // ---------------------------------------------------------------- the line format

    /// <summary>"2026-10-10 21:36:08.123 INFO  [12] text"; further lines of the text are indented by a tab.</summary>
    public static string Format(LogEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ')
          .Append(LevelText(e.Level).PadRight(5)).Append(" [").Append(e.Thread).Append("] ");
        var lines = e.Message.Replace("\r\n", "\n").Split('\n');
        sb.Append(lines[0]);
        for (int i = 1; i < lines.Length; i++) sb.Append(Environment.NewLine).Append('\t').Append(lines[i]);
        return sb.ToString();
    }

    public static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        _ => "ERROR",
    };

    private static readonly Regex LinePattern = new(@"^(?<time>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}) (?<level>DEBUG|INFO|WARN|ERROR)\s+\[(?<thread>[^\]]*)\] ?(?<text>.*)$", RegexOptions.Compiled);

    /// <summary>Reads a log file's entries (it can be open for writing at the time).</summary>
    public static List<LogEntry> ReadFile(string path)
    {
        var entries = new List<LogEntry>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        LogEntry? current = null;
        StringBuilder? more = null;
        void Finish()
        {
            if (current == null) return;
            entries.Add(more == null ? current : current with { Message = more.ToString() });
            current = null;
            more = null;
        }
        while (reader.ReadLine() is { } line)
        {
            var m = LinePattern.Match(line);
            if (m.Success && DateTime.TryParseExact(m.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                Finish();
                var level = m.Groups["level"].Value switch { "DEBUG" => LogLevel.Debug, "INFO" => LogLevel.Info, "WARN" => LogLevel.Warning, _ => LogLevel.Error };
                current = new LogEntry(time, level, m.Groups["thread"].Value, m.Groups["text"].Value);
            }
            else if (current != null)
            {
                more ??= new StringBuilder(current.Message);
                more.Append('\n').Append(line.StartsWith('\t') ? line[1..] : line);
            }
        }
        Finish();
        return entries;
    }
}

/// <summary>One logged line (its message may run over several lines).</summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Thread, string Message);

/// <summary>One log (the app's or a module's): get it from <see cref="Log.For"/> or <see cref="Log.Here"/>.</summary>
public sealed class Logger
{
    private readonly object sync = new();
    private StreamWriter? writer;
    private DateTime writerDay;

    internal Logger(string name) => Name = name;

    public string Name { get; }

    /// <summary>Whether Debug lines are being written: check it before building anything costly to log.</summary>
    public bool IsDebug => Log.DebugEnabled;

    public void Debug(string message) { if (Log.DebugEnabled) Write(LogLevel.Debug, message); }
    public void Debug(Func<string> message) { if (Log.DebugEnabled) Write(LogLevel.Debug, SafeText(message)); }
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, ex == null ? message : $"{message}: {ex}");
    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, ex == null ? message : $"{message}: {ex}");

    private static string SafeText(Func<string> message)
    {
        try { return message(); }
        catch (Exception ex) { return "(couldn't build the log text: " + ex.Message + ")"; }
    }

    public void Write(LogLevel level, string message)
    {
        var t = Thread.CurrentThread;
        var entry = new LogEntry(DateTime.Now, level, t.ManagedThreadId.ToString(CultureInfo.InvariantCulture) + (t.Name is { Length: > 0 } n && n.Length < 40 ? " " + n : ""), message);
        try
        {
            lock (sync)
            {
                if (writer == null || writerDay != entry.Time.Date)
                {
                    writer?.Dispose();
                    writer = null;
                    Directory.CreateDirectory(Log.Folder);
                    var stream = new FileStream(Log.PathOf(Name, entry.Time), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    writerDay = entry.Time.Date;
                }
                writer.WriteLine(Log.Format(entry));
            }
        }
        catch
        {
            // if the log can't be written there's nothing more to do
            lock (sync) { try { writer?.Dispose(); } catch { } writer = null; }
        }
        Log.Raise(Name, entry);
    }

    internal void Close()
    {
        lock (sync)
        {
            try { writer?.Dispose(); } catch { }
            writer = null;
        }
    }
}
