using System.Text;

namespace DaisysApp.Shared;

/// <summary>The few CSV helpers the logs need (Excel-compatible quoting).</summary>
public static class Csv
{
    /// <summary>The text in quotes, with quotes inside doubled.</summary>
    public static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>Quotes the text only if it needs it (a comma or a quote in it).</summary>
    public static string QuoteIfNeeded(string s) => s.Contains(',') || s.Contains('"') ? Quote(s) : s;

    /// <summary>One line's fields, with quoted fields (and doubled quotes inside them) undone.</summary>
    public static List<string> Split(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
