using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Events' Report (since 3.33.0): a page to read or print (HTML, opened in the browser) for a period: how much of it the
/// connection was down, each outage (from the first Ping target down to the last one back) with how long it lasted and
/// what the Diagnosis said, the other problems (Wi-Fi, adapters, failed speed tests), the address changes, and each day.
/// </summary>
internal static class EventReport
{
    /// <summary>An outage: the targets down in it (their drop lines), and the Diagnosis' lines.</summary>
    private sealed class Outage
    {
        public DateTime From;
        public DateTime? To;
        public readonly List<string> Down = new();
        public readonly List<string> Causes = new();
        public TimeSpan Length(DateTime now) => (To ?? now) - From;
    }

    /// <summary>The outages in <paramref name="list"/> (oldest first): one starts at a target's drop and ends when as many
    /// have come back as went down.</summary>
    private static List<Outage> Outages(IReadOnlyList<NetEvent> list)
    {
        var all = new List<Outage>();
        Outage? open = null;
        int down = 0;
        foreach (var e in list)
        {
            if (e.Kind == EventMonitor.Drop && e.Bad)
            {
                if (open == null)
                {
                    open = new Outage { From = e.Time };
                    all.Add(open);
                    down = 0;
                }
                open.Down.Add(e.Text);
                down++;
            }
            else if (e.Kind == EventMonitor.Drop && open != null)
            {
                down = Math.Max(0, down - 1);
                if (down == 0) { open.To = e.Time; open = null; }
            }
            else if (e.Kind == EventMonitor.Cause && e.Bad && open != null && !open.Causes.Contains(e.Text)) open.Causes.Add(e.Text);
        }
        return all;
    }

    /// <summary>The report for the events since <paramref name="since"/> (null: every one kept), as an HTML page.</summary>
    public static string Html(IReadOnlyList<NetEvent> events, NetworkingSettings settings, DateTime? since, string period)
    {
        var now = DateTime.Now;
        var list = events.Where(e => since == null || e.Time >= since).OrderBy(e => e.Time).ToList();
        var from = since ?? (list.Count > 0 ? list[0].Time : now);
        var span = now - from;
        var outages = Outages(list);
        var downFor = TimeSpan.FromTicks(outages.Sum(o => o.Length(now).Ticks));
        string H(string s) => WebUtility.HtmlEncode(s);
        string When(DateTime t) => t.ToString("ddd d MMM, HH:mm:ss", CultureInfo.CurrentCulture);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(H(T("Network Report"))).Append(" – ").Append(H(Environment.MachineName)).Append("</title><style>")
          .Append("body{font-family:'Segoe UI',Arial,sans-serif;color:#1d1d1f;margin:32px auto;max-width:960px;padding:0 24px;font-size:14px}")
          .Append("h1{font-size:26px;margin:0}h2{font-size:18px;margin:28px 0 8px;border-bottom:2px solid #2e7d32;padding-bottom:4px}")
          .Append(".sub{color:#666;margin:4px 0 20px}.tiles{display:flex;gap:12px;flex-wrap:wrap}")
          .Append(".tile{border:1px solid #ddd;border-radius:8px;padding:10px 14px;min-width:150px}.tile b{display:block;font-size:22px}")
          .Append(".bad{color:#c62828}.good{color:#2e7d32}table{border-collapse:collapse;width:100%}")
          .Append("th,td{text-align:left;padding:6px 8px;border-bottom:1px solid #e3e3e3;vertical-align:top}th{background:#f4f4f4}")
          .Append("th.n,td.n{text-align:right;white-space:nowrap}.small{color:#666;font-size:12px}button{padding:6px 14px;font-size:14px}")
          .Append("@media print{button{display:none}body{margin:0}h2{break-after:avoid}tr{break-inside:avoid}}")
          .Append("</style></head><body>");
        sb.Append("<button onclick=\"window.print()\" style=\"float:right\">").Append(H(T("Print"))).Append("</button>");
        sb.Append("<h1>").Append(H(T("Network Report"))).Append("</h1>");
        sb.Append("<p class=\"sub\">").Append(H(F("{0} · {1}: {2} to {3}", Environment.MachineName, period, When(from), When(now)))).Append("</p>");

        // the summary
        double up = span.TotalSeconds > 0 ? Math.Clamp(1 - downFor.TotalSeconds / span.TotalSeconds, 0, 1) : 1;
        int wifi = list.Count(e => e.Kind == EventMonitor.WiFi && e.Bad), adapters = list.Count(e => e.Kind == EventMonitor.Adapter && e.Bad),
            speed = list.Count(e => e.Kind == EventMonitor.Speed);
        void Tile(string label, string value, bool bad)
            => sb.Append("<div class=\"tile\">").Append(H(label)).Append("<b class=\"").Append(bad ? "bad" : "good").Append("\">").Append(H(value)).Append("</b></div>");
        sb.Append("<h2>").Append(H(T("Summary"))).Append("</h2><div class=\"tiles\">");
        Tile(T("Outages"), outages.Count.ToString(CultureInfo.CurrentCulture), outages.Count > 0);
        Tile(T("Down for"), outages.Count == 0 ? T("none") : NetUi.Duration(downFor), outages.Count > 0);
        Tile(T("Longest"), outages.Count == 0 ? T("none") : NetUi.Duration(outages.Max(o => o.Length(now))), outages.Count > 0);
        Tile(T("Up"), up.ToString("0.00%", CultureInfo.CurrentCulture), up < 0.999);
        Tile(T("Wi-Fi problems"), wifi.ToString(CultureInfo.CurrentCulture), wifi > 0);
        Tile(T("Adapter disconnects"), adapters.ToString(CultureInfo.CurrentCulture), adapters > 0);
        Tile(T("Failed speed tests"), speed.ToString(CultureInfo.CurrentCulture), speed > 0);
        sb.Append("</div>");

        // what the outages were, by the Diagnosis' first words ("Internet connection dropped", "Only … down"…)
        var kinds = outages.Where(o => o.Causes.Count > 0).GroupBy(o => Kind(o.Causes[0])).OrderByDescending(g => g.Count()).ToList();
        if (kinds.Count > 0)
        {
            sb.Append("<h2>").Append(H(T("What they were"))).Append("</h2><table><tr><th>").Append(H(T("Diagnosis"))).Append("</th><th class=\"n\">")
              .Append(H(T("Times"))).Append("</th><th class=\"n\">").Append(H(T("Down for"))).Append("</th></tr>");
            foreach (var g in kinds)
                sb.Append("<tr><td>").Append(H(g.Key)).Append("</td><td class=\"n\">").Append(g.Count()).Append("</td><td class=\"n\">")
                  .Append(H(NetUi.Duration(TimeSpan.FromTicks(g.Sum(o => o.Length(now).Ticks))))).Append("</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h2>").Append(H(T("Outages"))).Append("</h2>");
        if (outages.Count == 0) sb.Append("<p class=\"good\">").Append(H(T("No Ping target stopped answering."))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(H(T("Started"))).Append("</th><th class=\"n\">").Append(H(T("Lasted"))).Append("</th><th>")
              .Append(H(T("What happened"))).Append("</th></tr>");
            foreach (var o in outages.AsEnumerable().Reverse())
            {
                sb.Append("<tr><td>").Append(H(When(o.From))).Append("</td><td class=\"n\">")
                  .Append(H(o.To == null ? F("{0} (still down)", NetUi.Duration(o.Length(now))) : NetUi.Duration(o.Length(now)))).Append("</td><td>");
                foreach (var c in o.Causes) sb.Append("<div class=\"bad\">").Append(H(c)).Append("</div>");
                sb.Append("<div class=\"small\">").Append(H(string.Join(" · ", o.Down))).Append("</div></td></tr>");
            }
            sb.Append("</table>");
        }

        // the other problems, and the changes
        void Table(string title, IEnumerable<NetEvent> rows, string none)
        {
            var r = rows.Reverse().ToList();
            sb.Append("<h2>").Append(H(title)).Append("</h2>");
            if (r.Count == 0) { sb.Append("<p class=\"good\">").Append(H(none)).Append("</p>"); return; }
            sb.Append("<table><tr><th>").Append(H(T("When"))).Append("</th><th>").Append(H(T("What"))).Append("</th></tr>");
            foreach (var e in r)
                sb.Append("<tr><td style=\"white-space:nowrap\">").Append(H(When(e.Time))).Append("</td><td").Append(e.Bad ? " class=\"bad\"" : "").Append('>')
                  .Append(H(e.Text)).Append("</td></tr>");
            sb.Append("</table>");
        }
        Table(T("Other problems"), list.Where(e => e.Bad && e.Kind is EventMonitor.WiFi or EventMonitor.Adapter or EventMonitor.Speed or EventMonitor.Address),
            T("No Wi-Fi, adapter or speed test problems."));
        Table(T("Changes"), list.Where(e => !e.Bad && e.Kind is EventMonitor.WiFi or EventMonitor.Adapter or EventMonitor.Address), T("No changes."));

        // each day
        var days = list.GroupBy(e => e.Time.Date).OrderByDescending(g => g.Key).ToList();
        if (days.Count > 1)
        {
            sb.Append("<h2>").Append(H(T("Each day"))).Append("</h2><table><tr><th>").Append(H(T("Day"))).Append("</th><th class=\"n\">").Append(H(T("Outages")))
              .Append("</th><th class=\"n\">").Append(H(T("Down for"))).Append("</th><th class=\"n\">").Append(H(T("Other problems"))).Append("</th></tr>");
            foreach (var d in days)
            {
                var o = outages.Where(x => x.From.Date == d.Key).ToList();
                sb.Append("<tr><td>").Append(H(d.Key.ToString("ddd d MMM", CultureInfo.CurrentCulture))).Append("</td><td class=\"n\">").Append(o.Count)
                  .Append("</td><td class=\"n\">").Append(H(o.Count == 0 ? "" : NetUi.Duration(TimeSpan.FromTicks(o.Sum(x => x.Length(now).Ticks)))))
                  .Append("</td><td class=\"n\">").Append(d.Count(e => e.Bad && e.Kind != EventMonitor.Drop && e.Kind != EventMonitor.Cause)).Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<h2>").Append(H(T("Watched"))).Append("</h2><table>");
        foreach (var t in settings.Targets.Where(t => t.Address.Trim().Length > 0))
            sb.Append("<tr><td>").Append(H(t.Display)).Append("</td><td>").Append(H(t.What)).Append("</td></tr>");
        sb.Append("</table><p class=\"small\">").Append(H(F("Made by Daisy's App {0}, {1}", typeof(EventReport).Assembly.GetName().Version?.ToString(3) ?? "", When(now))))
          .Append("</p></body></html>");
        return sb.ToString();
    }

    /// <summary>A Diagnosis' kind: its words before the first ":" or "(" ("Internet connection dropped").</summary>
    private static string Kind(string cause)
    {
        int i = cause.IndexOfAny(new[] { ':', '(', ';' });
        return (i > 0 ? cause[..i] : cause).Trim();
    }

    /// <summary>Writes the report to a file in the temporary folder and opens it in the browser (to read, print or save).</summary>
    public static void Open(IReadOnlyList<NetEvent> events, NetworkingSettings settings, DateTime? since, string period)
    {
        var path = Path.Combine(Path.GetTempPath(), string.Concat($"{Environment.MachineName} Network Report {DateTime.Now:yyyy-MM-dd HH-mm}".Split(Path.GetInvalidFileNameChars())) + ".html");
        File.WriteAllText(path, Html(events, settings, since, period), Encoding.UTF8);
        Logging.Log.Here.Info($"Network report ({period}) written to {path}");
        Ui.Open(path);
    }
}
