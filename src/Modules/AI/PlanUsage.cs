using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Your Claude plan's usage limits, kept up to date while the app runs, and warnings before you run out.
/// <list type="bullet">
/// <item>Every few minutes it asks Claude Code ("claude -p /usage", which costs nothing: no model is asked) for each
/// window (this 5 hours, this week, per model), how much is used and when it resets, and what's been using it.</item>
/// <item>While a session runs, each request's rate-limit report updates it at once, to the minute.</item>
/// <item>It keeps a week of readings, to show the history and to work out the pace: at this rate, when you'd reach the
/// limit, and whether that's before it resets.</item>
/// <item>It notifies you (a Windows notification) when a window passes the warning levels, when the pace will take
/// you to the limit within the hour, when the limit's reached, and when it resets, so you know you can carry on;
/// and shows a bubble in the title bar while you're close.</item>
/// </list>
/// </summary>
internal sealed class PlanUsage
{
    /// <summary>One limit window: its key ("session", "week", "week (Opus)"…), name, how much is used (0–1) and when it resets.</summary>
    public sealed class Window
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public double Used { get; set; }
        public DateTime? Resets { get; set; }
        /// <summary>How long the window is (to show how far through it you are).</summary>
        public TimeSpan Length => Key == "session" ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
    }

    private readonly AiSettings settings;
    private readonly Func<string?> findExe;
    private readonly Dispatcher ui;
    private readonly DispatcherTimer poll = new();
    private readonly DispatcherTimer minute = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool checking;

    public List<Window> Windows { get; } = new();
    public Window? Session => Windows.FirstOrDefault(w => w.Key == "session");
    public Window? Week => Windows.FirstOrDefault(w => w.Key == "week");
    /// <summary>The limit is reached now (a request was refused), until when.</summary>
    public bool Blocked { get; private set; }
    public DateTime? BlockedUntil { get; private set; }
    /// <summary>"You are currently using your subscription…": how Claude Code is paying.</summary>
    public string? Plan { get; private set; }
    /// <summary>What's been using it (the last day and week), as Claude Code says it.</summary>
    public string? Breakdown { get; private set; }
    public DateTime? Checked { get; private set; }
    public string? Problem { get; private set; }

    /// <summary>Raised on the UI thread when anything changed.</summary>
    public event Action? Changed;

    public PlanUsage(AiSettings settings, Func<string?> findExe, Dispatcher ui)
    {
        this.settings = settings;
        this.findExe = findExe;
        this.ui = ui;
        poll.Tick += (_, _) => _ = CheckAsync();
        minute.Tick += (_, _) => Minute();
        // the last readings, until the first check
        if (settings.Usage.Last is { } last)
        {
            foreach (var w in last) Windows.Add(new Window { Key = w.Key, Label = w.Label, Used = w.Used, Resets = w.Resets });
            Checked = settings.Usage.LastChecked;
        }
    }

    public void Start()
    {
        Interval();
        minute.Start();
        // a first check soon after the app starts
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        first.Tick += (_, _) => { first.Stop(); _ = CheckAsync(); };
        first.Start();
    }

    public void Stop()
    {
        poll.Stop();
        minute.Stop();
    }

    /// <summary>How often it checks: every few minutes (set), more often when you're close to a limit.</summary>
    private void Interval()
    {
        double close = Windows.Count == 0 ? 0 : Windows.Max(w => w.Used);
        int minutes = Math.Clamp(settings.Usage.CheckMinutes, 2, 120);
        if (close * 100 >= settings.Usage.WarnAt || Blocked) minutes = Math.Min(minutes, 3);
        poll.Interval = TimeSpan.FromMinutes(minutes);
        poll.Start();
    }

    // ---------------------------------------------------------------- reading it

    /// <summary>Asks Claude Code ("/usage": no tokens are used).</summary>
    public async Task CheckAsync()
    {
        if (checking) return;
        string? exe = findExe();
        if (exe == null) { Problem = T("Claude Code isn't installed."); Changed?.Invoke(); return; }
        checking = true;
        try
        {
            string text = await Task.Run(() => RunUsage(exe));
            Apply(text);
            Problem = Windows.Count == 0 && Plan == null ? Shorten(text) : null;
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            Logging.Log.Here.Warn("Claude Code /usage: " + ex.Message);
        }
        finally { checking = false; }
        Checked = DateTime.Now;
        Evaluate();
    }

    private static string Shorten(string s) => s.Length > 200 ? s[..200] + "…" : s.Trim();

    private static string RunUsage(string exe)
    {
        var info = ClaudeCli.StartInfo(exe, new[] { "-p", "--output-format", "json", "--no-session-persistence", "/usage" },
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch { } throw new TimeoutException(T("Claude Code didn't answer within a minute.")); }
        string json = output.Result;
        int start = json.IndexOf('{');
        if (start < 0) return json;
        using var doc = JsonDocument.Parse(json[start..]);
        return doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() ?? "" : json;
    }

    /// <summary>
    /// "/usage"'s text: a line per window ("Current session: 54% used · resets Oct 17, 2:40am (America/New_York)",
    /// "Current week (all models): 45% used · resets Oct 22, 5pm (…)"), then what's been using it.
    /// </summary>
    private void Apply(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        Plan = lines.FirstOrDefault(l => l.StartsWith("You are", StringComparison.OrdinalIgnoreCase))?.Trim();
        var found = new List<Window>();
        foreach (var line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^Current (?<what>[^:]+):\s*(?<pct>\d+(?:\.\d+)?)%\s*used(?:\s*[·\-–]\s*resets\s+(?<when>.+))?$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string what = m.Groups["what"].Value.Trim();
            string key = what.StartsWith("session", StringComparison.OrdinalIgnoreCase) ? "session"
                : what.Equals("week (all models)", StringComparison.OrdinalIgnoreCase) || what.Equals("week", StringComparison.OrdinalIgnoreCase) ? "week"
                : what;
            found.Add(new Window
            {
                Key = key,
                Label = LabelOf(key),
                Used = double.Parse(m.Groups["pct"].Value, CultureInfo.InvariantCulture) / 100,
                Resets = m.Groups["when"].Success ? ParseReset(m.Groups["when"].Value) : null,
            });
        }
        int at = Array.FindIndex(lines, l => l.StartsWith("What's contributing", StringComparison.OrdinalIgnoreCase));
        Breakdown = at >= 0 ? string.Join("\n", lines.Skip(at + 1)).Trim() : null;
        if (found.Count > 0)
        {
            bool wasBlocked = Blocked;
            foreach (var f in found) Merge(f.Key, f.Used, f.Resets);
            if (Blocked && Session is { } s && s.Used < 1 && (Week?.Used ?? 0) < 1) Blocked = false;
            if (wasBlocked && !Blocked) BlockedUntil = null;
        }
        Changed?.Invoke();
    }

    private static string LabelOf(string key) => key switch
    {
        "session" => T("This 5 hours"),
        "week" => T("This week"),
        _ => key.StartsWith("week", StringComparison.OrdinalIgnoreCase) ? F("This week {0}", key[4..].Trim()) : key,
    };

    /// <summary>"Oct 17, 2:40am (America/New_York)", "5pm (Europe/London)": in local time.</summary>
    public static DateTime? ParseReset(string text)
    {
        try
        {
            var tzMatch = Regex.Match(text, @"\(([^)]+)\)");
            string body = tzMatch.Success ? text[..tzMatch.Index] : text;
            var time = Regex.Match(body, @"(\d{1,2})(?::(\d{2}))?\s*([ap])\.?m", RegexOptions.IgnoreCase);
            var date = Regex.Match(body, @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+(\d{1,2})", RegexOptions.IgnoreCase);
            if (!time.Success && !date.Success) return null;
            TimeZoneInfo zone = TimeZoneInfo.Local;
            if (tzMatch.Success)
            {
                string id = tzMatch.Groups[1].Value.Trim();
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win)) try { zone = TimeZoneInfo.FindSystemTimeZoneById(win); } catch { } }
            }
            var nowThere = TimeZoneInfo.ConvertTime(DateTime.Now, zone);
            int hour = 0, minute = 0;
            if (time.Success)
            {
                hour = int.Parse(time.Groups[1].Value) % 12;
                if (char.ToLowerInvariant(time.Groups[3].Value[0]) == 'p') hour += 12;
                if (time.Groups[2].Success) minute = int.Parse(time.Groups[2].Value);
            }
            DateTime when;
            if (date.Success)
            {
                int month = DateTime.ParseExact(date.Groups[1].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
                when = new DateTime(nowThere.Year, month, int.Parse(date.Groups[2].Value), hour, minute, 0);
                if (when < nowThere.AddDays(-30)) when = when.AddYears(1);
            }
            else
            {
                when = nowThere.Date.AddHours(hour).AddMinutes(minute);
                if (when < nowThere.AddMinutes(-5)) when = when.AddDays(1);
            }
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(when, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local);
        }
        catch { return null; }
    }

    /// <summary>A session's rate-limit report (with each request): the windows to the minute, and whether you're refused.</summary>
    public void FromRateLimit(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object) return;
        if (info.TryGetProperty("unifiedWindows", out var windows) && windows.ValueKind == JsonValueKind.Object)
            foreach (var w in windows.EnumerateObject())
            {
                if (w.Value.ValueKind != JsonValueKind.Object || !w.Value.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) continue;
                DateTime? resets = w.Value.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).LocalDateTime : null;
                string key = w.Name switch { "five_hour" => "session", "seven_day" => "week", _ => "week (" + Pretty(w.Name.Replace("seven_day_", "")) + ")" };
                Merge(key, u.GetDouble(), resets);
            }
        string status = info.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        bool blocked = status == "rejected";
        if (blocked)
        {
            BlockedUntil = info.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).LocalDateTime : Session?.Resets;
        }
        else BlockedUntil = null;
        Blocked = blocked;
        Checked = DateTime.Now;
        Changed?.Invoke();
        Evaluate();
    }

    private static string Pretty(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');

    private void Merge(string key, double used, DateTime? resets)
    {
        var w = Windows.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (w == null)
        {
            w = new Window { Key = key, Label = LabelOf(key) };
            Windows.Add(w);
            Windows.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        }
        w.Used = Math.Max(0, used);
        if (resets != null) w.Resets = resets;
    }

    private static int Rank(Window w) => w.Key == "session" ? 0 : w.Key == "week" ? 1 : 2;

    // ---------------------------------------------------------------- the pace

    /// <summary>
    /// At the pace of the last readings in this window (the last hour of the 5-hour one, the last day of a week),
    /// how much it goes up an hour, and when it would reach 100% (null if it isn't going up, or not before it resets).
    /// </summary>
    public (double PerHour, DateTime? Full, double AtReset) Pace(Window w)
    {
        var history = settings.Usage.History;
        var span = w.Key == "session" ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24);
        var now = DateTime.Now;
        Func<UsageSample, double> of = w.Key == "session" ? s => s.Session : s => s.Week;
        // only since the window last reset (a drop)
        var recent = new List<UsageSample>();
        foreach (var s in history.Where(s => s.At >= now - span - TimeSpan.FromMinutes(10)))
        {
            if (recent.Count > 0 && of(s) < of(recent[^1]) - 0.03) recent.Clear();
            recent.Add(s);
        }
        recent.Add(new UsageSample { At = now, Session = Session?.Used ?? 0, Week = Week?.Used ?? 0 });
        if (recent.Count < 2) return (0, null, w.Used);
        var first = recent[0];
        double hours = (now - first.At).TotalHours;
        if (hours < 0.15) return (0, null, w.Used);
        double perHour = Math.Max(0, (w.Used - of(first)) / hours);
        double hoursLeft = w.Resets is { } r ? Math.Max(0, (r - now).TotalHours) : double.PositiveInfinity;
        DateTime? full = perHour > 0.0001 && w.Used < 1 ? now.AddHours((1 - w.Used) / perHour) : null;
        if (full != null && w.Resets is { } reset && full > reset) full = null;
        double atReset = double.IsInfinity(hoursLeft) ? w.Used : Math.Min(1, w.Used + perHour * hoursLeft);
        return (perHour, full, atReset);
    }

    // ---------------------------------------------------------------- warnings

    /// <summary>After each reading: the history, the title bar bubble, and any notification due.</summary>
    private void Evaluate()
    {
        var u = settings.Usage;
        var now = DateTime.Now;
        if (Session != null || Week != null)
        {
            var last = u.History.LastOrDefault();
            double s = Session?.Used ?? 0, w = Week?.Used ?? 0;
            if (last == null || now - last.At >= TimeSpan.FromMinutes(2) || Math.Abs(last.Session - s) >= 0.01 || Math.Abs(last.Week - w) >= 0.01)
                u.History.Add(new UsageSample { At = now, Session = s, Week = w });
            u.History.RemoveAll(x => x.At < now.AddDays(-8));
        }
        u.Last = Windows.Select(w => new SavedWindow { Key = w.Key, Label = w.Label, Used = w.Used, Resets = w.Resets }).ToList();
        u.LastChecked = Checked;

        if (u.Notify)
        {
            foreach (var w in Windows)
            {
                string cycle = w.Key + "|" + (w.Resets?.ToString("yyyyMMddHHmm") ?? "");
                double pct = w.Used * 100;
                var (_, full, _) = Pace(w);
                string resets = w.Resets is { } r ? F("It resets {0} ({1}).", When(r), Until(r)) : "";
                if (pct >= u.UrgentAt && Once(cycle + "|urgent"))
                    Notifications.Show(F("Claude: {0} at {1:0}%", w.Label, pct), resets + (full is { } f ? " " + F("At this pace you'll reach the limit around {0:t}.", f) : ""));
                else if (pct >= u.WarnAt && Once(cycle + "|warn"))
                    Notifications.Show(F("Claude: {0} at {1:0}%", w.Label, pct), resets + (full is { } f ? " " + F("At this pace you'll reach the limit around {0:t}.", f) : ""));
                if (u.NotifyForecast && full is { } soon && soon - now < TimeSpan.FromHours(1) && pct >= 40 && pct < u.UrgentAt && Once(cycle + "|pace"))
                    Notifications.Show(T("Claude: you'll reach your limit within the hour"), F("At this pace {0} reaches 100% around {1:t}; it resets {2}.", w.Label, soon, w.Resets is { } r2 ? When(r2) : "?"));
                // remember to say when it resets, if you were close
                if (pct >= u.WarnAt && w.Resets != null) u.ResetWatch[w.Key] = w.Resets.Value;
            }
            if (Blocked && Once("blocked|" + (BlockedUntil?.ToString("yyyyMMddHHmm") ?? "")))
            {
                Notifications.Show(T("Claude: you've reached your plan's limit"), BlockedUntil is { } b ? F("It resets {0} ({1}). The app tells you when it has.", When(b), Until(b)) : T("Claude Code will say when it resets."));
                if (BlockedUntil is { } until) u.ResetWatch["session"] = until;
            }
        }
        foreach (var k in u.Notified.Where(p => p.Value < now.AddDays(-9)).Select(p => p.Key).ToList()) u.Notified.Remove(k);
        settings.Save();
        Badge();
        Interval();
        Changed?.Invoke();
    }

    /// <summary>True the first time for this key (a window's cycle and level).</summary>
    private bool Once(string key)
    {
        if (settings.Usage.Notified.ContainsKey(key)) return false;
        settings.Usage.Notified[key] = DateTime.Now;
        return true;
    }

    /// <summary>Each minute: a window you were close on that has now reset is said (and checked).</summary>
    private void Minute()
    {
        var watch = settings.Usage.ResetWatch;
        var now = DateTime.Now;
        foreach (var (key, at) in watch.ToList())
        {
            if (now < at) continue;
            watch.Remove(key);
            if (settings.Usage.Notify && settings.Usage.NotifyReset)
                Notifications.Show(F("Claude: {0} has reset", LabelOf(key)), T("You can use Claude again as much as your plan allows."));
            if (Windows.FirstOrDefault(w => w.Key == key) is { } w) { w.Used = 0; w.Resets = w.Resets?.Add(w.Length); }
            if (key == "session") { Blocked = false; BlockedUntil = null; }
            settings.Save();
            _ = CheckAsync();
        }
        Badge();
        Changed?.Invoke(); // the countdowns
    }

    private readonly HashSet<object> working = new();

    /// <summary>A Claude Code session here started or stopped working: the title bar's bubble only shows while one is.</summary>
    public void Working(object session, bool busy)
    {
        if (busy ? working.Add(session) : working.Remove(session)) Badge();
    }

    /// <summary>The title bar's bubble, while a Claude Code session is working: amber when a window is past the warning
    /// level, red when you're out.</summary>
    private void Badge()
    {
        var u = settings.Usage;
        var worst = Windows.OrderByDescending(w => w.Used).FirstOrDefault();
        if (!u.TitleBadge || worst == null || working.Count == 0) { TitleBadges.Set("ClaudeUsage", null); return; }
        if (Blocked || worst.Used >= 1)
        {
            var back = BlockedUntil ?? worst.Resets;
            TitleBadges.Set("ClaudeUsage", back is { } b ? F("Claude: limit, back {0:t}", b) : T("Claude: limit"), T("Claude's plan limit is reached"), "#F85149");
        }
        else if (worst.Used * 100 >= u.WarnAt)
            TitleBadges.Set("ClaudeUsage", F("Claude {0:0}%", worst.Used * 100), worst.Resets is { } r ? F("{0}: resets {1}", worst.Label, When(r)) : worst.Label, "#E3B341");
        else TitleBadges.Set("ClaudeUsage", null);
    }

    // ---------------------------------------------------------------- words

    /// <summary>"at 2:40 AM", "tomorrow at 2:40 AM", "Thursday at 5:00 PM", "on Oct 22 at 5:00 PM".</summary>
    public static string When(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return F("at {0:t}", t);
        if (t.Date == today.AddDays(1)) return F("tomorrow at {0:t}", t);
        if (t.Date < today.AddDays(7)) return F("{0:dddd} at {0:t}", t);
        return F("on {0:MMM d} at {0:t}", t);
    }

    /// <summary>"in 2 h 13 min", "in 3 days 4 h", "now".</summary>
    public static string Until(DateTime t)
    {
        var left = t - DateTime.Now;
        if (left.TotalMinutes < 1) return T("now");
        if (left.TotalHours < 1) return F("in {0} min", (int)Math.Ceiling(left.TotalMinutes));
        if (left.TotalDays < 1) return F("in {0} h {1} min", (int)left.TotalHours, left.Minutes);
        return F("in {0} d {1} h", (int)left.TotalDays, left.Hours);
    }
}
