using System.Diagnostics.Eventing.Reader;

namespace DaisysApp.Logging;

/// <summary>One entry from a Windows event log.</summary>
public sealed record WindowsEvent(DateTime Time, LogLevel Level, string Provider, int Id, string Message, string LogName);

/// <summary>
/// Reads Windows' event logs (Application, System, and any other channel this user may read; Security needs admin).
/// </summary>
public static class WindowsEvents
{
    /// <summary>
    /// The newest entries of <paramref name="logName"/> since <paramref name="since"/> (at most <paramref name="max"/>),
    /// newest first. <paramref name="filter"/> is an extra XPath condition on System, e.g. "Level&lt;=2".
    /// </summary>
    public static List<WindowsEvent> Read(string logName, DateTime since, int max, string? filter = null, CancellationToken ct = default)
    {
        long ms = Math.Max(0, (long)(DateTime.Now - since).TotalMilliseconds);
        string xpath = $"*[System[TimeCreated[timediff(@SystemTime) <= {ms}]{(filter != null ? " and " + filter : "")}]]";
        var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
        var list = new List<WindowsEvent>();
        using var reader = new EventLogReader(query);
        while (list.Count < max && !ct.IsCancellationRequested)
        {
            using var record = reader.ReadEvent();
            if (record == null) break;
            list.Add(Convert(record, logName));
        }
        return list;
    }

    /// <summary>Crashes and hangs of DaisysApp.exe that Windows recorded in the Application log since then.</summary>
    public static List<WindowsEvent> AppCrashes(DateTime since, int max = 20)
    {
        try
        {
            return Read("Application", since, 500,
                    "(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime'] or Provider[@Name='Application Hang'] or Provider[@Name='Windows Error Reporting'])")
                .Where(e => e.Message.Contains("DaisysApp.exe", StringComparison.OrdinalIgnoreCase))
                .Take(max).ToList();
        }
        catch { return []; }
    }

    /// <summary>The names of every event log on this PC that has entries, sorted.</summary>
    public static List<string> LogNames()
    {
        var names = new List<string>();
        using var session = new EventLogSession();
        foreach (var name in session.GetLogNames())
        {
            try
            {
                var info = session.GetLogInformation(name, PathType.LogName);
                if (info.RecordCount is > 0) names.Add(name);
            }
            catch { /* not readable by this user */ }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static WindowsEvent Convert(EventRecord r, string logName)
    {
        string message;
        try { message = r.FormatDescription() ?? ""; }
        catch { message = ""; }
        if (message.Length == 0)
        {
            try { message = string.Join(" · ", r.Properties.Select(p => p.Value?.ToString())); }
            catch { }
        }
        var level = r.Level switch
        {
            1 or 2 => LogLevel.Error,
            3 => LogLevel.Warning,
            5 => LogLevel.Debug,
            _ => LogLevel.Info,
        };
        // Security audit failures have no level; their keyword says so
        if (r.Level is null or 0 && r.Keywords is long k && (k & 0x10000000000000) != 0) level = LogLevel.Warning;
        return new WindowsEvent(r.TimeCreated ?? DateTime.MinValue, level, r.ProviderName ?? "", r.Id, message.Trim(), logName);
    }
}
