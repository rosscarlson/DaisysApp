using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Applets.Network.Zabbix.Views;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Network.Zabbix;

/// <summary>
/// Zabbix's answer to Core's <see cref="DeviceLookups"/> (Support → Network Scan's device window): the host with the
/// device's address on an interface, else one whose name, visible name or DNS name is one of the device's names; its
/// availability, interfaces, groups, CPU, memory, uptime and problems; Open shows its page on the Zabbix tab.
/// </summary>
internal static class ZabbixLookup
{
    public const string Id = "Zabbix";

    public static void Register(ZabbixService service, ZabbixView view) =>
        DeviceLookups.Add(Id, T("Zabbix"), "", () => !service.Settings.Off && service.Settings.HasServer, (q, ct) => FindAsync(service, view, q, ct));

    private static async Task<DeviceLookups.Match?> FindAsync(ZabbixService service, ZabbixView view, DeviceLookups.Query q, CancellationToken ct)
    {
        // the hosts are read only while something shows them: read them now when they're old
        if (service.Updated is not { } at || DateTime.Now - at > TimeSpan.FromMinutes(2))
        {
            var before = service.Updated;
            await service.RefreshAsync();
            // (a refresh already under way: wait for it)
            for (int i = 0; i < 80 && service.Updated == before && service.Error == null; i++) await Task.Delay(250, ct);
        }
        if (service.Hosts.Count == 0 && service.Error != null) throw new Exception(service.Error);

        var names = q.Names.Where(n => n.Length > 0).ToList();
        var shortNames = names.Select(DeviceLookups.Short).Where(n => n.Length > 0).ToHashSet();
        var looseNames = names.Select(DeviceLookups.Loose).Where(n => n.Length >= 3).ToHashSet();
        bool ByName(ZHost h) =>
            looseNames.Contains(DeviceLookups.Loose(h.Host)) || looseNames.Contains(DeviceLookups.Loose(h.Name))
            || h.Interfaces.Any(i => i.Dns.Length > 0 && shortNames.Contains(DeviceLookups.Short(i.Dns)));

        var byIp = service.Hosts.Where(h => h.Interfaces.Any(i => i.Ip == q.Ip)).ToList();
        ZHost? host;
        string by;
        if (byIp.Count > 0)
        {
            host = byIp.FirstOrDefault(ByName) ?? byIp.FirstOrDefault(h => h.Monitored) ?? byIp[0];
            by = ByName(host) ? T("address and name") : T("address");
        }
        else
        {
            host = service.Hosts.FirstOrDefault(h => h.Monitored && ByName(h)) ?? service.Hosts.FirstOrDefault(ByName);
            by = T("name");
        }
        if (host == null) return null;

        var lines = new List<DeviceLookups.Line>();
        if (host.Host != host.Display) lines.Add(new(T("Host"), host.Host));
        string status = !host.Monitored ? T("Not monitored")
            : host.Availability switch { Availability.Available => T("Available"), Availability.Unavailable => T("Unavailable"), _ => T("Unknown") };
        if (host.InMaintenance) status += " · " + T("in maintenance");
        lines.Add(new(T("Status"), status, host.Monitored && host.Availability == Availability.Available ? "#7FD07A"
            : host.Monitored && host.Availability == Availability.Unavailable ? "#E45959" : null));
        if (host.Availability == Availability.Unavailable && host.Error.Length > 0) lines.Add(new(T("Why"), host.Error, "#E45959"));
        foreach (var i in host.Interfaces.OrderByDescending(i => i.Main))
            lines.Add(new(T("Interface"), $"{i.TypeName}: {i.Address}{(i.Port.Length > 0 ? ":" + i.Port : "")}"
                + (i.Available == Availability.Unavailable ? " · " + T("unavailable") : "")));
        if (host.Groups.Count > 0) lines.Add(new(T("Groups"), string.Join(", ", host.Groups)));
        if (service.Summary.TryGetValue(host.HostId, out var items))
        {
            if (HostMetrics.Cpu(items) is { LastClock: not null } cpu) lines.Add(new(T("CPU"), cpu.ValueText));
            if (HostMetrics.Memory(items) is { LastClock: not null } mem) lines.Add(new(T("Memory"), mem.ValueText));
            if (HostMetrics.Uptime(items) is { LastClock: not null } up) lines.Add(new(T("Uptime"), up.ValueText));
        }
        var problems = service.ProblemsOf(host.HostId).OrderByDescending(p => p.Severity).ThenByDescending(p => p.Since).ToList();
        lines.Add(new(T("Problems"), problems.Count == 0 ? T("None") : problems.Count.ToString(), problems.Count == 0 ? "#7FD07A" : null));
        foreach (var p in problems.Take(10))
            lines.Add(new(p.SeverityName, $"{p.Description} · {F("since {0:g}", p.Since)}", ZProblem.SeverityColor(p.Severity)));
        if (problems.Count > 10) lines.Add(new("", F("and {0} more", problems.Count - 10)));
        if (host.Description.Length > 0) lines.Add(new(T("Description"), host.Description));

        return new DeviceLookups.Match(host.Display, by, lines, () =>
        {
            if (!AppNavigation.OpenTab("Network/Zabbix")) return;
            view.OpenHost(host.HostId);
        });
    }
}
