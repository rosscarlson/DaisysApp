using System.Globalization;

namespace DaisysApp.Applets.Network.Zabbix.Api;

/// <summary>
/// A Zabbix item's unit, and how a value reads in it the way Zabbix shows it: K, M, G… (1024 a step for B and Bps,
/// 1000 for the rest, e.g. bps), times for "s" and "uptime", a date for "unixtime", and units starting with "!" as
/// they are. Two items can share a graph when their units are the same (<see cref="Kind"/>).
/// </summary>
public sealed record ZUnit(string Units) : IGraphUnit
{
    private static readonly Dictionary<string, ZUnit> cache = new(StringComparer.Ordinal);

    public static ZUnit Of(string? units)
    {
        units ??= "";
        lock (cache)
        {
            if (!cache.TryGetValue(units, out var u)) cache[units] = u = new ZUnit(units);
            return u;
        }
    }

    /// <summary>What a graph's axis says (the unit without prefixes).</summary>
    public string Unit => Units.StartsWith('!') ? Units[1..] : Units;

    /// <summary>Items with the same kind can go on one graph.</summary>
    public string Kind => Unit;

    public bool IsPercent => Unit == "%";

    private bool Binary => Unit is "B" or "Bps";

    private static readonly string[] Prefixes = { "", "K", "M", "G", "T", "P", "E" };

    public string Text(double value)
    {
        if (double.IsNaN(value)) return "—";
        if (Units.StartsWith('!')) return Number(value) + (Unit.Length > 0 ? " " + Unit : "");
        switch (Unit)
        {
            case "": return Number(value);
            case "%": return value.ToString(Math.Abs(value) >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " %";
            case "s": return Seconds(value);
            case "uptime": return Uptime(value);
            case "unixtime": return value <= 0 ? T("never") : DateTimeOffset.FromUnixTimeSeconds((long)value).LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        }
        double step = Binary ? 1024 : 1000, v = value;
        int i = 0;
        while (Math.Abs(v) >= step && i < Prefixes.Length - 1) { v /= step; i++; }
        return Number(v) + " " + Prefixes[i] + Unit;
    }

    /// <summary>The axis of a graph: rounder than a value.</summary>
    public string Axis(double value)
    {
        if (Unit is "s" or "uptime") return Seconds(value);
        if (Unit == "unixtime" || Unit == "") return value.ToString("#,0.##", CultureInfo.CurrentCulture);
        return Text(value);
    }

    private static string Number(double v) =>
        v.ToString(Math.Abs(v) >= 100 || v == Math.Floor(v) ? "#,0" : Math.Abs(v) >= 10 ? "0.#" : "0.##", CultureInfo.CurrentCulture);

    private static string Seconds(double s)
    {
        if (s == 0) return "0";
        if (Math.Abs(s) < 1) return (s * 1000).ToString("0.#", CultureInfo.CurrentCulture) + " ms";
        if (Math.Abs(s) < 60) return s.ToString("0.##", CultureInfo.CurrentCulture) + " s";
        var t = TimeSpan.FromSeconds(Math.Abs(s));
        string text = t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
        return s < 0 ? "-" + text : text;
    }

    private static string Uptime(double s)
    {
        if (s < 0) return "—";
        var t = TimeSpan.FromSeconds(s);
        return t.TotalDays >= 1 ? P((long)t.TotalDays, "{0} day", "{0} days") + $", {t.Hours:00}:{t.Minutes:00}" : $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
