using System.Text.RegularExpressions;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.Logs;

/// <summary>
/// Finds where Daisy's App stopped without shutting down properly: every run logs "… starting (process N)" when it
/// starts and "Exited cleanly" when it ends, so a start that follows another start with no clean exit between them
/// means the earlier run crashed, hung and was ended, or was stopped with Windows or the PC. A flag row
/// (<see cref="LogRow.IsShutdownFlag"/>) goes in just after that run's last entry, where whatever broke will be.
/// </summary>
internal static partial class ShutdownCheck
{
    [GeneratedRegex(@"^.+ \S+ starting \(process (\d+)")]
    private static partial Regex StartLine();

    /// <summary>Adds the flags to <paramref name="rows"/> (newest first, as loaded) and returns how many.</summary>
    public static int Flag(List<LogRow> rows)
    {
        var flags = new List<LogRow>();
        LogRow? start = null, last = null;
        string process = "";
        // oldest first
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var r = rows[i];
            if (r.Source.Equals(Log.AppName, StringComparison.OrdinalIgnoreCase))
            {
                if (StartLine().Match(r.Message) is { Success: true } m)
                {
                    if (start != null && last != null) flags.Add(FlagFor(start, process, last, r));
                    start = r;
                    process = m.Groups[1].Value;
                }
                else if (r.Message.StartsWith("Exited cleanly", StringComparison.Ordinal)) start = null;
            }
            if (start != null) last = r;
        }
        if (flags.Count == 0) return 0;
        rows.AddRange(flags);
        rows.Sort((a, b) => b.Time.CompareTo(a.Time));
        return flags.Count;
    }

    private static LogRow FlagFor(LogRow start, string process, LogRow last, LogRow next) =>
        // a moment after the dead run's last entry, so it sorts just above it
        new(last.Time.AddTicks(1), LogLevel.Error, Log.AppName, "",
            F("⚠ Daisy's App didn't shut down properly. The run that started at {0:G} (process {1}) never logged a clean exit: its last entry, just below this, was at {2:G}, and the app started again at {3:G}. It crashed, stopped responding and was ended, or Windows or the PC stopped first; the entries just below are the last it wrote.",
                start.Time, process, last.Time, next.Time))
        { IsShutdownFlag = true };
}
