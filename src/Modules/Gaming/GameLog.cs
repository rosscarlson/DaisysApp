using System.Globalization;
using System.IO;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Gaming;

/// <summary>One second of a game's performance, as logged.</summary>
internal sealed record GameSecond(DateTime Time, double Fps, double FrametimeMs, double Low1Fps, double MaxFrametimeMs,
    double GpuPercent, double GpuTempC, double GpuClockMHz, double GpuPowerW, double VramMB, double CpuPercent, double GameCpuPercent,
    double RamMB, bool Recording);

/// <summary>A whole play session's summary.</summary>
internal sealed record GameSession(DateTime Start, DateTime End, string Exe, string Name, double Seconds, double AvgFps, double Low1Fps,
    double Low01Fps, double AvgFrametimeMs, double AvgGpuPercent, double MaxGpuTempC, double MaxVramMB);

/// <summary>
/// The performance history: %LOCALAPPDATA%\DaisysApp\logs\gaming\. A row a second per game while it's in front, in
/// &lt;exe&gt;\yyyy-MM.csv, and a line per play session in sessions.csv. About 350 KB an hour of play.
/// </summary>
internal static class GameLog
{
    public static string Folder { get; } = Path.Combine(AppPaths.LogFolder, "gaming");
    private static string SessionsFile => Path.Combine(Folder, "sessions.csv");
    private const string SecondsHeader = "time,fps,frametime_ms,low1_fps,max_frametime_ms,gpu_pct,gpu_temp_c,gpu_clock_mhz,gpu_power_w,vram_mb,cpu_pct,game_cpu_pct,ram_mb,recording";
    private const string SessionsHeader = "start,end,exe,name,seconds,avg_fps,low1_fps,low01_fps,avg_frametime_ms,avg_gpu_pct,max_gpu_temp_c,max_vram_mb";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string GameFolder(string exe) => Path.Combine(Folder, Safe(Path.GetFileNameWithoutExtension(exe)));

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string N(double v, string format = "0.#") => double.IsFinite(v) ? v.ToString(format, Inv) : "";
    private static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : double.NaN;

    private static string Quote(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static void Append(string exe, IEnumerable<GameSecond> rows)
    {
        try
        {
            var byMonth = rows.GroupBy(r => r.Time.ToString("yyyy-MM", Inv));
            foreach (var month in byMonth)
            {
                string dir = GameFolder(exe);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, month.Key + ".csv");
                var sb = new StringBuilder();
                if (!File.Exists(file)) sb.Append(SecondsHeader).Append("\r\n");
                foreach (var r in month)
                    sb.Append(r.Time.ToString("yyyy-MM-ddTHH:mm:ss", Inv)).Append(',')
                      .Append(N(r.Fps)).Append(',').Append(N(r.FrametimeMs, "0.##")).Append(',').Append(N(r.Low1Fps)).Append(',')
                      .Append(N(r.MaxFrametimeMs, "0.##")).Append(',').Append(N(r.GpuPercent, "0")).Append(',').Append(N(r.GpuTempC, "0")).Append(',')
                      .Append(N(r.GpuClockMHz, "0")).Append(',').Append(N(r.GpuPowerW, "0")).Append(',').Append(N(r.VramMB, "0")).Append(',')
                      .Append(N(r.CpuPercent, "0")).Append(',').Append(N(r.GameCpuPercent, "0")).Append(',').Append(N(r.RamMB, "0")).Append(',')
                      .Append(r.Recording ? "1" : "").Append("\r\n");
                File.AppendAllText(file, sb.ToString());
            }
        }
        catch (Exception ex) { ErrorLog.Write("Gaming history: writing", ex); }
    }

    public static void AppendSession(GameSession s)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            if (!File.Exists(SessionsFile)) sb.Append(SessionsHeader).Append("\r\n");
            sb.Append(string.Join(",", s.Start.ToString("yyyy-MM-ddTHH:mm:ss", Inv), s.End.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
                Quote(s.Exe), Quote(s.Name), N(s.Seconds, "0"), N(s.AvgFps), N(s.Low1Fps), N(s.Low01Fps), N(s.AvgFrametimeMs, "0.##"),
                N(s.AvgGpuPercent, "0"), N(s.MaxGpuTempC, "0"), N(s.MaxVramMB, "0"))).Append("\r\n");
            File.AppendAllText(SessionsFile, sb.ToString());
        }
        catch (Exception ex) { ErrorLog.Write("Gaming history: writing the session", ex); }
    }

    public static List<GameSession> Sessions()
    {
        var list = new List<GameSession>();
        try
        {
            if (!File.Exists(SessionsFile)) return list;
            foreach (string line in File.ReadLines(SessionsFile).Skip(1))
            {
                var f = SplitCsv(line);
                if (f.Count < 12 || !DateTime.TryParse(f[0], Inv, DateTimeStyles.None, out var start) || !DateTime.TryParse(f[1], Inv, DateTimeStyles.None, out var end)) continue;
                list.Add(new GameSession(start, end, f[2], f[3], D(f[4]), D(f[5]), D(f[6]), D(f[7]), D(f[8]), D(f[9]), D(f[10]), D(f[11])));
            }
        }
        catch (Exception ex) { ErrorLog.Write("Gaming history: reading sessions", ex); }
        return list;
    }

    /// <summary>The game's logged seconds between two times.</summary>
    public static List<GameSecond> Seconds(string exe, DateTime from, DateTime to)
    {
        var list = new List<GameSecond>();
        try
        {
            string dir = GameFolder(exe);
            for (var m = new DateTime(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
            {
                string file = Path.Combine(dir, m.ToString("yyyy-MM", Inv) + ".csv");
                if (!File.Exists(file)) continue;
                foreach (string line in File.ReadLines(file).Skip(1))
                {
                    var f = line.Split(',');
                    if (f.Length < 14 || !DateTime.TryParse(f[0], Inv, DateTimeStyles.None, out var t) || t < from || t > to) continue;
                    list.Add(new GameSecond(t, D(f[1]), D(f[2]), D(f[3]), D(f[4]), D(f[5]), D(f[6]), D(f[7]), D(f[8]), D(f[9]), D(f[10]), D(f[11]), D(f[12]), f[13] == "1"));
                }
            }
        }
        catch (Exception ex) { ErrorLog.Write("Gaming history: reading", ex); }
        return list;
    }

    /// <summary>Deletes monthly files older than <paramref name="months"/> months (0 = keep everything).</summary>
    public static void Prune(int months)
    {
        if (months <= 0 || !Directory.Exists(Folder)) return;
        try
        {
            string cutoff = DateTime.Now.AddMonths(-months).ToString("yyyy-MM", Inv);
            foreach (string file in Directory.EnumerateFiles(Folder, "????-??.csv", SearchOption.AllDirectories))
                if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(file), cutoff) < 0) File.Delete(file);
        }
        catch (Exception ex) { ErrorLog.Write("Gaming history: tidying", ex); }
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
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
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
