using System.IO;
using System.Text;

namespace DaisysApp.Applets.AI;

/// <summary>REQUESTS.md in a session's folder: each request sent, as written, under the day's date (## yyyy-MM-dd),
/// appended by the app so no AI turn (and none of the plan) is spent on it. A log: never rewritten, only added to.</summary>
public static class RequestLog
{
    public const string FileName = "REQUESTS.md";
    private static readonly object gate = new();

    public static void Add(string folder, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || folder.Length == 0) return;
        try
        {
            string path = Path.Combine(folder, FileName);
            string heading = "## " + DateTime.Now.ToString("yyyy-MM-dd");
            lock (gate)
            {
                var sb = new StringBuilder();
                if (!File.Exists(path)) sb.Append("# Requests\n\nWhat was asked, word for word, by date.\n\n").Append(heading).Append("\n\n");
                else
                {
                    string had = File.ReadAllText(path);
                    if (had.Length > 0 && !had.EndsWith('\n')) sb.Append('\n');
                    if (LastHeading(had) != heading) sb.Append('\n').Append(heading).Append("\n\n");
                    else sb.Append('\n');
                }
                sb.Append(text.Replace("\r\n", "\n")).Append('\n');
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
        }
        catch (Exception e) { Logging.ErrorLog.Write("Writing REQUESTS.md", e); }
    }

    private static string? LastHeading(string text)
    {
        int at = text.LastIndexOf("\n## ", StringComparison.Ordinal);
        if (at < 0) return text.StartsWith("## ") ? text.Split('\n')[0].Trim() : null;
        int end = text.IndexOf('\n', at + 1);
        return (end < 0 ? text[(at + 1)..] : text[(at + 1)..end]).Trim();
    }
}
