namespace DaisysApp.Shell;

/// <summary>
/// Modules that know devices on the network (Zabbix's hosts, Home Assistant's devices) say what they know of one, for
/// another module that found it (Support → Network Scan's device window, which shows each answer as a column). A module
/// adds its lookup when it starts; whoever asks never needs the module itself. Called and answered on the UI thread.
/// </summary>
public static class DeviceLookups
{
    /// <summary>The device asked about: its address, MAC (aa:bb:… or null) and names (any case, with or without a domain).</summary>
    public sealed record Query(string Ip, string? Mac, IReadOnlyList<string> Names);

    /// <summary>A line of an answer; <see cref="Color"/> is a colour ("#E45959") or a brush resource's key, or null.</summary>
    public sealed record Line(string Label, string Value, string? Color = null);

    /// <summary>What a module knows of the device: its name there, how it was matched, its lines, and a way to open it
    /// in the module (null when there's none).</summary>
    public sealed record Match(string Name, string MatchedBy, IReadOnlyList<Line> Lines, Action? Open = null);

    /// <summary>A module's lookup: its name ("Zabbix") and icon (a Segoe Fluent Icons glyph); whether it can look now
    /// (set up, switched on); it answers null when it doesn't know the device, and throws with a message for the user
    /// when it couldn't look.</summary>
    public sealed record Source(string Id, string Name, string Glyph, Func<bool> Usable, Func<Query, CancellationToken, Task<Match?>> Find);

    private static readonly List<Source> sources = new();

    /// <summary>Adds a lookup (by id: one with the same id is replaced).</summary>
    public static void Add(string id, string name, string glyph, Func<bool> usable, Func<Query, CancellationToken, Task<Match?>> find)
    {
        sources.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        sources.Add(new Source(id, name, glyph, usable, find));
    }

    public static void Remove(string id) => sources.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The lookups that can look now.</summary>
    public static IReadOnlyList<Source> Usable => sources.Where(s => { try { return s.Usable(); } catch { return false; } }).ToList();

    /// <summary>A name as it compares: lower case, without its domain ("Office-PC.lan" → "office-pc").</summary>
    public static string Short(string name)
    {
        string n = name.Trim().TrimEnd('.').ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(n, out _)) return n;
        int dot = n.IndexOf('.');
        return dot > 0 ? n[..dot] : n;
    }

    /// <summary>A name as it compares loosely: <see cref="Short"/> without spaces, dashes and underscores ("Office PC" = "office-pc").</summary>
    public static string Loose(string name) => new(Short(name).Where(char.IsLetterOrDigit).ToArray());

    /// <summary>A MAC as it compares: twelve lower-case hex digits, or "" when it isn't one.</summary>
    public static string Mac(string? mac)
    {
        if (mac == null) return "";
        var hex = new string(mac.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());
        return hex.Length == 12 ? hex : "";
    }
}
