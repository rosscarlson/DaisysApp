using System.Globalization;

namespace DaisysApp.Applets.Network.Zabbix.Api;

/// <summary>One graph of a group: its title and the items drawn on it, each with its colour.</summary>
public sealed record GraphDef(string Title, IReadOnlyList<(ZItem Item, string Color)> Lines);

/// <summary>
/// A tile on a host's page, like Performance's: CPU, memory, disks, network, temperatures, response time. Its big value,
/// two lines under it, and its graphs (the first one is the tile's).
/// </summary>
public sealed record MetricGroup(string Key, string Title, string Value, string Line1, string Line2, IReadOnlyList<GraphDef> Graphs)
{
    public GraphDef Main => Graphs[0];
    public IEnumerable<ZItem> Items => Graphs.SelectMany(g => g.Lines).Select(l => l.Item).DistinctBy(i => i.ItemId);
    /// <summary>A percentage tile: its graphs go from 0 to 100.</summary>
    public bool Percent => Main.Lines.All(l => l.Item.Unit.IsPercent);
}

/// <summary>
/// Finds the usual things in a host's items, by the keys Zabbix's own templates use (agent and agent 2, Linux and
/// Windows, and the common SNMP and ICMP ones): system.cpu.util, vm.memory.util(ization), vfs.fs(.dependent).size[…,pused],
/// net.if.in / net.if.out, temperatures (by unit), icmppingsec. A host without one of them has no tile for it.
/// </summary>
public static class HostMetrics
{
    public static readonly string[] Palette = { "AccentBrush", "#F7A541", "#7FD07A", "#B48EF0", "#FF7B72", "#F2CC60", "#4CC2FF", "#E58AD8", "#9AA5B1", "#5EEAD4" };

    /// <summary>The key starts the host boxes read for every host (the server returns only these).</summary>
    public static readonly string[] SummaryKeys = { "system.cpu.util", "vm.memory.util", "vm.memory.utilization", "system.uptime", "agent.ping", "icmpping", "zabbix[host," };

    /// <summary>A key's first parameter without quotes ("net.if.in[\"eth0\",bytes]" → eth0).</summary>
    public static string Param(string key, int index = 0)
    {
        int open = key.IndexOf('[');
        if (open < 0 || !key.EndsWith(']')) return "";
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = open + 1; i < key.Length - 1; i++)
        {
            char c = key[i];
            if (c == '"' && (sb.Length == 0 || quoted)) { quoted = !quoted; continue; }
            if (c == '\\' && quoted && i + 1 < key.Length - 1 && key[i + 1] == '"') { sb.Append('"'); i++; continue; }
            if (c == ',' && !quoted) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts.ElementAtOrDefault(index)?.Trim() ?? "";
    }

    private static string Base(string key) => key.IndexOf('[') is int i and >= 0 ? key[..i] : key;

    /// <summary>The host's CPU utilization item: the whole-CPU one, else null.</summary>
    public static ZItem? Cpu(IEnumerable<ZItem> items)
    {
        var cpu = items.Where(i => i.IsNumeric && Base(i.Key) == "system.cpu.util").ToList();
        return cpu.FirstOrDefault(i => i.Key == "system.cpu.util" || i.Key == "system.cpu.util[]")
            ?? cpu.FirstOrDefault(i => Param(i.Key, 0) is "" or "all" && Param(i.Key, 1) == "" && Param(i.Key, 2) is "" or "avg1" && i.Unit.IsPercent)
            ?? cpu.FirstOrDefault(i => i.Name.Contains("CPU utilization", StringComparison.OrdinalIgnoreCase) && i.Unit.IsPercent && !i.Key.Contains("idle"));
    }

    /// <summary>The host's memory utilization item (%), else null.</summary>
    public static ZItem? Memory(IEnumerable<ZItem> items) =>
        items.FirstOrDefault(i => i.IsNumeric && i.Key is "vm.memory.utilization" or "vm.memory.util")
        ?? items.FirstOrDefault(i => i.IsNumeric && i.Key == "vm.memory.size[pused]")
        ?? items.FirstOrDefault(i => i.IsNumeric && i.Unit.IsPercent && i.Name.Contains("memory utilization", StringComparison.OrdinalIgnoreCase));

    public static ZItem? Uptime(IEnumerable<ZItem> items) =>
        items.FirstOrDefault(i => i.IsNumeric && i.Key == "system.uptime") ?? items.FirstOrDefault(i => i.IsNumeric && i.Units == "uptime");

    /// <summary>Builds the tiles for a host's items, in the order Performance has them.</summary>
    public static List<MetricGroup> Build(IReadOnlyList<ZItem> all)
    {
        var items = all.Where(i => i.IsNumeric && !i.NotSupported).ToList();
        var groups = new List<MetricGroup>();
        string Val(ZItem? i) => i == null ? "—" : i.ValueText;

        // CPU: utilization, and the load averages
        if (Cpu(items) is { } cpu)
        {
            var load = items.Where(i => Base(i.Key) == "system.cpu.load").OrderBy(i => i.Key).ToList();
            var load1 = load.FirstOrDefault(i => Param(i.Key, 1) is "" or "avg1");
            var num = items.FirstOrDefault(i => i.Key.StartsWith("system.cpu.num", StringComparison.Ordinal));
            var graphs = new List<GraphDef> { new(T("CPU utilization"), new[] { (cpu, "AccentBrush") }) };
            // the parts of the CPU's time (user, system, iowait…), when the template collects them
            var parts = items.Where(i => Base(i.Key) == "system.cpu.util" && i != cpu && Param(i.Key, 1) is { Length: > 0 } t && t != "idle" && i.Unit.IsPercent).ToList();
            if (parts.Count > 0) graphs.Add(new(T("CPU time"), Colored(parts)));
            if (load.Count > 0) graphs.Add(new(T("Load average"), Colored(load)));
            groups.Add(new("cpu", "CPU", Pct(cpu.Value),
                load1 != null ? F("Load {0}", load1.Value.ToString("0.00", CultureInfo.CurrentCulture)) : "",
                num != null ? P((long)num.Value, "{0} processor", "{0} processors") : "",
                graphs));
        }

        // memory: utilization, what's available, and the swap
        if (Memory(items) is { } mem)
        {
            var total = items.FirstOrDefault(i => i.Key == "vm.memory.size[total]");
            var available = items.FirstOrDefault(i => i.Key == "vm.memory.size[available]") ?? items.FirstOrDefault(i => i.Key == "vm.memory.size[free]");
            var swap = items.FirstOrDefault(i => i.Key is "system.swap.size[,pfree]" or "system.swap.pfree" or "system.swap.size[all,pfree]");
            var graphs = new List<GraphDef> { new(T("Memory utilization"), new[] { (mem, "#B48EF0") }) };
            if (available != null) graphs.Add(new(T("Available memory"), new[] { (available, "#B48EF0") }));
            if (swap != null) graphs.Add(new(T("Free swap space"), new[] { (swap, "#E58AD8") }));
            groups.Add(new("memory", T("Memory"), Pct(mem.Value),
                total != null && available != null ? F("{0} of {1} in use", total.Unit.Text(total.Value - available.Value), total.ValueText)
                    : total != null ? F("{0} installed", total.ValueText) : "",
                swap != null ? F("Swap {0} free", Pct(swap.Value)) : "",
                graphs));
        }

        // disks: each filesystem's space used (%), and the disks' reads and writes
        var fs = items.Where(i => Base(i.Key) is "vfs.fs.size" or "vfs.fs.dependent.size" && Param(i.Key, 1) == "pused").OrderBy(i => Param(i.Key)).ToList();
        if (fs.Count > 0)
        {
            var fullest = fs.MaxBy(i => double.IsNaN(i.Value) ? -1 : i.Value)!;
            var graphs = new List<GraphDef> { new(T("Space used"), Colored(fs)) };
            var rw = items.Where(i => Base(i.Key) is "vfs.dev.read.rate" or "vfs.dev.write.rate" or "vfs.dev.read.await" or "vfs.dev.write.await").ToList();
            var rates = rw.Where(i => i.Key.Contains(".rate")).ToList();
            if (rates.Count > 0) graphs.Add(new(T("Disk reads and writes"), Colored(rates)));
            var util = items.Where(i => Base(i.Key) == "vfs.dev.util").ToList();
            if (util.Count > 0) graphs.Add(new(T("Disk utilization"), Colored(util)));
            groups.Add(new("disk", T("Disks"), Pct(fullest.Value),
                F("Fullest: {0}", Param(fullest.Key)),
                P(fs.Count, "{0} filesystem", "{0} filesystems"),
                graphs));
        }

        // network: each interface's traffic in and out
        var netIn = items.Where(i => Base(i.Key) == "net.if.in" && Param(i.Key, 1) is "" or "bytes").ToList();
        var netOut = items.Where(i => Base(i.Key) == "net.if.out" && Param(i.Key, 1) is "" or "bytes").ToList();
        if (netIn.Count + netOut.Count > 0)
        {
            var names = netIn.Concat(netOut).Select(i => Param(i.Key)).Distinct().OrderBy(n => n).ToList();
            double down = netIn.Select(i => i.Value).Where(double.IsFinite).Sum(), up = netOut.Select(i => i.Value).Where(double.IsFinite).Sum();
            var unit = (netIn.FirstOrDefault() ?? netOut[0]).Unit;
            var lines = new List<(ZItem, string)>();
            int c = 0;
            foreach (var n in names)
            {
                if (netIn.FirstOrDefault(i => Param(i.Key) == n) is { } a) lines.Add((a, Palette[c++ % Palette.Length]));
                if (netOut.FirstOrDefault(i => Param(i.Key) == n) is { } b) lines.Add((b, Palette[c++ % Palette.Length]));
            }
            var graphs = new List<GraphDef> { new(T("Traffic"), lines) };
            var errors = items.Where(i => Base(i.Key) is "net.if.in" or "net.if.out" && Param(i.Key, 1) is "errors" or "dropped").ToList();
            if (errors.Count > 0) graphs.Add(new(T("Errors and dropped packets"), Colored(errors)));
            groups.Add(new("net", T("Network"), unit.Text(down),
                F("Down {0}", unit.Text(down)),
                F("Up {0}", unit.Text(up)) + " · " + P(names.Count, "{0} interface", "{0} interfaces"),
                graphs));
        }

        // temperatures: anything in °C
        var temps = items.Where(i => i.Units is "°C" or "C" or "℃" || (i.Name.Contains("temperature", StringComparison.OrdinalIgnoreCase) && i.Units is "" or "°C")).Take(10).ToList();
        if (temps.Count > 0)
        {
            var hottest = temps.MaxBy(i => double.IsNaN(i.Value) ? -999 : i.Value)!;
            groups.Add(new("temp", T("Temperature"), double.IsNaN(hottest.Value) ? "—" : $"{hottest.Value:0}°",
                F("Hottest: {0}", hottest.Name), P(temps.Count, "{0} sensor", "{0} sensors"),
                new[] { new GraphDef(T("Temperatures"), Colored(temps)) }));
        }

        // response time from the Zabbix server (ICMP ping)
        var pingSec = items.FirstOrDefault(i => Base(i.Key) == "icmppingsec");
        var loss = items.FirstOrDefault(i => Base(i.Key) == "icmppingloss");
        if (pingSec != null)
        {
            var graphs = new List<GraphDef> { new(T("Response time"), new[] { (pingSec, "AccentBrush") }) };
            if (loss != null) graphs.Add(new(T("Packet loss"), new[] { (loss, "#FF7B72") }));
            groups.Add(new("ping", T("Ping"), Val(pingSec), loss != null ? F("Loss {0}", Val(loss)) : "", T("From the Zabbix server"), graphs));
        }
        return groups;
    }

    private static List<(ZItem, string)> Colored(IEnumerable<ZItem> items) =>
        items.Select((i, n) => (i, Palette[n % Palette.Length])).ToList();

    private static string Pct(double v) => double.IsNaN(v) ? "—" : $"{v:0}%";
}
