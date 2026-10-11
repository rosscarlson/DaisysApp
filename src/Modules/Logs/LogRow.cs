using System.Globalization;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Logs;

/// <summary>One entry in a log tab, from any source.</summary>
public sealed class LogRow(DateTime time, LogLevel level, string source, string eventId, string message, string thread = "")
{
    public DateTime Time { get; } = time;
    public LogLevel Level { get; } = level;
    /// <summary>Which applet (Daisy's App), provider (Windows) or program (WSL) it's from.</summary>
    public string Source { get; } = source;
    /// <summary>The Windows event id ("" for other logs).</summary>
    public string Event { get; } = eventId;
    public string Message { get; } = message;
    /// <summary>The thread that wrote it (Daisy's App's own logs), shown with the entry in full.</summary>
    public string Thread { get; } = thread;

    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.CurrentCulture);
    public string LevelText => LevelName(Level);

    /// <summary>The message's first line, for the table.</summary>
    public string Summary
    {
        get
        {
            int nl = Message.IndexOfAny(['\r', '\n']);
            string first = nl < 0 ? Message : Message[..nl] + " …";
            return first.Length > 400 ? first[..400] + "…" : first;
        }
    }

    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => T("Debug"),
        LogLevel.Info => T("Info"),
        LogLevel.Warning => T("Warning"),
        _ => T("Error"),
    };

    /// <summary>As one block of text: time, level, [source event], then the message (further lines indented).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ')
          .Append(Log.LevelText(Level).PadRight(5)).Append(" [").Append(Source);
        if (Event.Length > 0) sb.Append(' ').Append(Event);
        if (Thread.Length > 0) sb.Append(" thread ").Append(Thread);
        sb.Append("] ");
        var lines = Message.Replace("\r\n", "\n").Split('\n');
        sb.Append(lines[0]);
        for (int i = 1; i < lines.Length; i++) sb.Append(Environment.NewLine).Append('\t').Append(lines[i]);
        return sb.ToString();
    }

    public static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    public string ToCsv() => string.Join(",",
        Csv(Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)), Csv(Log.LevelText(Level)), Csv(Source), Csv(Event), Csv(Message));
}
