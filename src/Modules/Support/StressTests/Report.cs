using System.Globalization;
using System.Net;
using System.Text;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>A row of the comparison: a label, A's and B's text, and how B compares (in %, + when B is better).</summary>
internal sealed record ReportRow(string Label, string A, string B, double? Better = null, double BarA = double.NaN, double BarB = double.NaN, bool Strong = false, bool Differs = false, bool Detail = false);

internal sealed record ReportSection(string Title, string? Note, List<ReportRow> Rows);

/// <summary>
/// Two runs compared: the PCs side by side (CPU, GPU, VRAM, memory, storage…), then the scores (overall, each group,
/// each test, with bars) and every test's numbers, with how much better or worse B is than A in percent. Shown in the
/// comparison window, and copied as text or saved as a web page.
/// </summary>
internal static class Report
{
    public static List<ReportSection> Build(StressRun a, StressRun b)
    {
        var sections = new List<ReportSection>();

        // ---- the PCs
        var sys = new List<ReportRow>();
        void Sys(string label, string x, string y) { if (x.Length > 0 || y.Length > 0) sys.Add(new ReportRow(label, x, y, Differs: !string.Equals(x, y, StringComparison.OrdinalIgnoreCase))); }
        SystemSpec sa = a.System, sb = b.System;
        Sys(T("PC"), sa.Pc, sb.Pc);
        sys.Add(new ReportRow(T("Run"), a.Started.ToString("g"), b.Started.ToString("g")));
        Sys(T("CPU"), sa.Cpu, sb.Cpu);
        Sys(T("Cores / threads"), sa.Cores > 0 ? $"{sa.Cores} / {sa.Threads}" : $"{sa.Threads}", sb.Cores > 0 ? $"{sb.Cores} / {sb.Threads}" : $"{sb.Threads}");
        Sys(T("Base clock"), sa.CpuBaseMHz > 0 ? $"{sa.CpuBaseMHz / 1000.0:0.00} GHz" : "", sb.CpuBaseMHz > 0 ? $"{sb.CpuBaseMHz / 1000.0:0.00} GHz" : "");
        Sys(T("Memory"), sa.RamText, sb.RamText);
        Sys(T("Memory modules"), string.Join("\n", sa.RamModules), string.Join("\n", sb.RamModules));
        Sys(T("GPU"), sa.Gpu?.Name ?? sa.TestedGpu, sb.Gpu?.Name ?? sb.TestedGpu);
        Sys(T("VRAM"), sa.Gpu is { VramGB: > 0 } va ? $"{va.VramGB:0.#} GB" : "", sb.Gpu is { VramGB: > 0 } vb ? $"{vb.VramGB:0.#} GB" : "");
        Sys(T("GPU driver"), sa.Gpu?.Driver ?? "", sb.Gpu?.Driver ?? "");
        Sys(T("Other GPUs"), string.Join("\n", sa.Gpus.Where(g => g != sa.Gpu)), string.Join("\n", sb.Gpus.Where(g => g != sb.Gpu)));
        Sys(T("Storage tested"), sa.TestedDrive, sb.TestedDrive);
        Sys(T("Drives"), string.Join("\n", sa.Drives), string.Join("\n", sb.Drives));
        Sys(T("Motherboard"), sa.Board, sb.Board);
        Sys(T("BIOS"), sa.Bios, sb.Bios);
        Sys(T("Windows"), sa.Os, sb.Os);
        Sys(T("Power plan"), sa.PowerPlan, sb.PowerPlan);
        Sys(T("Each test"), F("{0} s", a.SecondsPerTest), F("{0} s", b.SecondsPerTest));
        Sys(T("Daisy's App"), a.AppVersion, b.AppVersion);
        sections.Add(new ReportSection(T("System"), null, sys));

        // ---- the scores
        var scores = new List<ReportRow>();
        double top = new[] { a.Score, b.Score }.Concat(a.GroupScores.Values).Concat(b.GroupScores.Values).DefaultIfEmpty(1000).Max();
        scores.Add(Score(T("Overall"), a.Score, b.Score, top, strong: true));
        foreach (var g in Catalog.Groups)
        {
            double ga = a.GroupScores.GetValueOrDefault(g), gb = b.GroupScores.GetValueOrDefault(g);
            if (ga > 0 || gb > 0) scores.Add(Score(Catalog.GroupTitle(g), ga, gb, top));
        }
        sections.Add(new ReportSection(T("Scores"), T("1,000 = the reference PC (i7-8086K, DDR4-2133, GTX 1080 Ti, WD SN720); tests that ran on both are what to compare"), scores));

        // ---- each test
        var ids = Catalog.Tests.Select(t => t.Id).Where(id => a.Results.Any(r => r.Id == id) || b.Results.Any(r => r.Id == id)).ToList();
        if (ids.Count > 0)
        {
            var tests = new List<ReportRow>();
            var details = new List<ReportRow>();
            double topTest = ids.SelectMany(id => new[] { a.Averaged(id)?.Score ?? 0, b.Averaged(id)?.Score ?? 0 }).DefaultIfEmpty(1000).Max();
            foreach (var id in ids)
            {
                var info = Catalog.Find(id)!;
                TestResult? ra = a.Averaged(id), rb = b.Averaged(id);
                string name = Catalog.FullName(info.Group, info.Name);
                if (info.Scored) tests.Add(Score(name, ra?.Score ?? 0, rb?.Score ?? 0, topTest, failedA: Why(ra), failedB: Why(rb)));
                details.Add(new ReportRow(name, Device(ra), Device(rb), Strong: true));
                details.Add(Value(info.Name + (info.Unit.Length > 0 ? $" ({info.Unit})" : ""), ra?.Value, rb?.Value, info.Unit, info.LowerIsBetter));
                var names = (ra?.Metrics ?? []).Select(m => m.Name).Concat((rb?.Metrics ?? []).Select(m => m.Name)).Distinct();
                foreach (var m in names)
                {
                    Metric? ma = ra?.MetricNamed(m), mb = rb?.MetricNamed(m);
                    var unit = (ma ?? mb)!.Unit;
                    bool lower = (ma ?? mb)!.LowerIsBetter;
                    // counts (threads, passes, frames) aren't better or worse
                    bool neutral = unit.Length == 0 || unit is "GB" or "MB" or "×" && m != T("Scaling over one thread") || m == T("MSAA") || m == T("Triangles a frame");
                    details.Add(Value(m, ma?.Value, mb?.Value, unit, lower, neutral));
                }
                if ((ra?.Errors ?? 0) > 0 || (rb?.Errors ?? 0) > 0)
                    details.Add(new ReportRow(T("Errors"), (ra?.Errors ?? 0).ToString("#,0"), (rb?.Errors ?? 0).ToString("#,0"), Differs: true));
                Sensor(details, T("CPU clock (average)"), ra?.Sensors.CpuClockAvg, rb?.Sensors.CpuClockAvg, v => $"{v / 1000:0.00} GHz");
                Sensor(details, T("CPU temperature (highest)"), ra?.Sensors.CpuTempMax, rb?.Sensors.CpuTempMax, v => $"{v:0} °C");
                Sensor(details, T("CPU power (highest)"), ra?.Sensors.CpuPowerMax, rb?.Sensors.CpuPowerMax, v => $"{v:0} W");
                if (info.Group is Catalog.Gpu or Catalog.Video or Catalog.Stress)
                {
                    Sensor(details, T("GPU clock (average)"), ra?.Sensors.GpuClockAvg, rb?.Sensors.GpuClockAvg, v => $"{v:0} MHz");
                    Sensor(details, T("GPU temperature (highest)"), ra?.Sensors.GpuTempMax, rb?.Sensors.GpuTempMax, v => $"{v:0} °C");
                    Sensor(details, T("GPU power (highest)"), ra?.Sensors.GpuPowerMax, rb?.Sensors.GpuPowerMax, v => $"{v:0} W");
                }
            }
            if (tests.Count > 0) sections.Add(new ReportSection(T("Tests"), null, tests));
            sections.Add(new ReportSection(T("Details"), T("Averages where a test ran more than once; temperatures and power where the PC could read them"), details));
        }

        // ---- problems
        var problems = new List<ReportRow>();
        string Problems(StressRun r) => string.Join("\n", new[]
        {
            r.Interrupted ? F("Stopped during {0}: the app or Windows closed", r.InterruptedDuring ?? "?") : "",
            r.Errors > 0 ? P(r.Errors, "{0:#,0} error", "{0:#,0} errors") : "",
        }.Concat(r.Events.Select(e => $"{e.Time:T} {e.Source} {e.Id}: {e.Message}")).Where(s => s.Length > 0));
        string pa = Problems(a), pb = Problems(b);
        if (pa.Length > 0 || pb.Length > 0) problems.Add(new ReportRow(T("Problems"), pa.Length > 0 ? pa : T("None"), pb.Length > 0 ? pb : T("None"), Differs: true));
        if (problems.Count > 0) sections.Add(new ReportSection(T("Stability"), null, problems));
        return sections;
    }

    private static string? Why(TestResult? r) => r == null ? T("not run") : r.Failed != null ? T("couldn't run") : null;

    private static string Device(TestResult? r) => r == null ? T("not run") : r.Failed ?? r.Device ?? "";

    private static ReportRow Score(string label, double a, double b, double top, bool strong = false, string? failedA = null, string? failedB = null) =>
        new(label, failedA ?? (a > 0 ? a.ToString("#,0") : "—"), failedB ?? (b > 0 ? b.ToString("#,0") : "—"),
            a > 0 && b > 0 ? (b / a - 1) * 100 : null, a > 0 ? a / top : double.NaN, b > 0 ? b / top : double.NaN, strong);

    private static ReportRow Value(string label, double? a, double? b, string unit, bool lowerIsBetter, bool neutral = false)
    {
        string Text(double? v) => v is double d && !double.IsNaN(d) ? Metric.Format(d, unit) : "—";
        double? better = null;
        if (!neutral && a is double x && b is double y && x > 0 && y > 0 && !double.IsNaN(x) && !double.IsNaN(y))
            better = (lowerIsBetter ? x / y - 1 : y / x - 1) * 100;
        return new ReportRow(label, Text(a), Text(b), better, Detail: true);
    }

    private static void Sensor(List<ReportRow> rows, string label, double? a, double? b, Func<double, string> text)
    {
        bool hasA = a is double x && !double.IsNaN(x), hasB = b is double y && !double.IsNaN(y);
        if (!hasA && !hasB) return;
        rows.Add(new ReportRow(label, hasA ? text(a!.Value) : "—", hasB ? text(b!.Value) : "—", Detail: true));
    }

    public static string Percent(double v) => (v >= 0 ? "+" : "") + v.ToString(Math.Abs(v) < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + "%";

    // ---------------------------------------------------------------- text and a web page

    public static string Text(StressRun a, StressRun b)
    {
        var sb = new StringBuilder();
        sb.AppendLine(T("Stress Tests comparison")).AppendLine($"A: {a.Title}").AppendLine($"B: {b.Title}").AppendLine();
        foreach (var s in Build(a, b))
        {
            sb.AppendLine($"== {s.Title} ==");
            if (s.Note != null) sb.AppendLine(s.Note);
            int w = Math.Min(34, s.Rows.Select(r => r.Label.Length).DefaultIfEmpty(10).Max() + 2);
            foreach (var r in s.Rows)
            {
                var la = r.A.Split('\n');
                var lb = r.B.Split('\n');
                for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
                {
                    string label = i == 0 ? (r.Detail ? "  " : "") + r.Label : "";
                    string diff = i == 0 && r.Better is double d ? "  B " + Percent(d) : "";
                    sb.AppendLine($"{label.PadRight(w)}{(i < la.Length ? la[i] : "").PadRight(40)}  {(i < lb.Length ? lb[i] : "")}{diff}");
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Html(StressRun a, StressRun b)
    {
        static string E(string s) => WebUtility.HtmlEncode(s).Replace("\n", "<br>");
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E(T("Stress Tests comparison"))).Append("</title><style>")
          .Append("body{font-family:Segoe UI,system-ui,sans-serif;background:#1b1d21;color:#e6e6e6;margin:24px;max-width:1200px}")
          .Append("h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 6px;color:#9cc3ff}.note{color:#9a9a9a;font-size:12px;margin-bottom:6px}")
          .Append("table{border-collapse:collapse;width:100%;table-layout:fixed}td,th{padding:5px 8px;border-bottom:1px solid #30343a;vertical-align:top;text-align:left}")
          .Append("th{color:#9a9a9a;font-weight:600}.d td:first-child{padding-left:24px;color:#bdbdbd}.s td{font-weight:600}.diff{white-space:nowrap;text-align:right}")
          .Append(".up{color:#7fd07a}.down{color:#ff7b72}.differs{color:#f2cc60}.bar{height:8px;border-radius:4px;margin-top:4px}.a{background:#58a6ff}.b{background:#f7a541}")
          .Append("</style></head><body>");
        sb.Append("<h1>").Append(E(T("Stress Tests comparison"))).Append("</h1>");
        sb.Append("<div class=\"note\">A: ").Append(E(a.Title)).Append(" &nbsp; · &nbsp; B: ").Append(E(b.Title)).Append("</div>");
        foreach (var s in Build(a, b))
        {
            sb.Append("<h2>").Append(E(s.Title)).Append("</h2>");
            if (s.Note != null) sb.Append("<div class=\"note\">").Append(E(s.Note)).Append("</div>");
            sb.Append("<table><colgroup><col style=\"width:24%\"><col style=\"width:34%\"><col style=\"width:34%\"><col style=\"width:8%\"></colgroup><tr><th></th><th>A</th><th>B</th><th class=\"diff\">").Append(E(T("B against A"))).Append("</th></tr>");
            foreach (var r in s.Rows)
            {
                sb.Append("<tr class=\"").Append(r.Detail ? "d" : r.Strong ? "s" : "").Append("\"><td>").Append(E(r.Label)).Append("</td>");
                foreach (var (text, bar, cls) in new[] { (r.A, r.BarA, "a"), (r.B, r.BarB, "b") })
                {
                    sb.Append("<td").Append(r.Differs ? " class=\"differs\"" : "").Append('>').Append(E(text));
                    if (!double.IsNaN(bar)) sb.Append("<div class=\"bar ").Append(cls).Append("\" style=\"width:").Append((Math.Clamp(bar, 0, 1) * 100).ToString("0.#", CultureInfo.InvariantCulture)).Append("%\"></div>");
                    sb.Append("</td>");
                }
                sb.Append("<td class=\"diff ").Append(r.Better is double d ? d > 0.5 ? "up" : d < -0.5 ? "down" : "" : "").Append("\">")
                  .Append(r.Better is double v ? E(Percent(v)) : "").Append("</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<div class=\"note\" style=\"margin-top:24px\">").Append(E(F("Made by {0}'s Stress Tests on {1:g}", AppPaths.DisplayName, DateTime.Now))).Append("</div></body></html>");
        return sb.ToString();
    }
}
