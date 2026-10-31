namespace DaisysApp.Shared;

/// <summary>Sizes, counts and times as the app shows them.</summary>
public static class Sizes
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>As Windows Explorer does: 1,024 steps, three significant figures ("812 KB", "1.46 GB").</summary>
    public static string Bytes(long n)
    {
        if (n < 1024) return n == 1 ? T("1 byte") : F("{0:#,0} bytes", n);
        double v = n;
        int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        string num = v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.#") : v.ToString("0.##");
        return $"{num} {Units[u]}";
    }

    /// <summary>A speed: "48.2 MB/s".</summary>
    public static string Speed(double bytesPerSecond) => bytesPerSecond < 1 ? "—" : Bytes((long)bytesPerSecond).Replace(" bytes", " B") + "/s";

    public static string Count(long n) => n.ToString("#,0");

    /// <summary>"1:02:03" or "2:03".</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>"3.2 s" for a short time, else as <see cref="Duration"/>.</summary>
    public static string Took(TimeSpan t) => t.TotalSeconds < 60 ? F("{0:0.0} s", t.TotalSeconds) : Duration(t);

    public static string Percent(double fraction) => fraction >= 0.995 ? "100%" : fraction < 0.001 && fraction > 0 ? "<0.1%" : (fraction * 100).ToString(fraction < 0.1 ? "0.0" : "0") + "%";
}
