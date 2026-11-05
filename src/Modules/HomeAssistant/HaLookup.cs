using System.Text.Json;
using System.Windows;
using DaisysApp.Applets.HomeAssistant.Views;
using DaisysApp.Shell;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>
/// Home Assistant's answer to Core's <see cref="DeviceLookups"/> (Support → Network Scan's device window): the device
/// with the MAC among its connections, else the one whose own page is at the address, else the one an entity names
/// with the address or MAC (a router's trackers, an IP sensor), else one named as the device is; its area, maker,
/// model, version and its entities' values; Open opens its window.
/// </summary>
internal static class HaLookup
{
    public const string Id = "HomeAssistant";

    public static void Register(HaService service, HaHotkeys hotkeys) =>
        DeviceLookups.Add(Id, T("Home Assistant"), "", () => service.Settings.HasServer && service.Settings.Token.Length > 0, (q, ct) => FindAsync(service, hotkeys, q, ct));

    private static readonly string[] IpAttributes = { "ip", "ip_address", "ip_addr", "host", "local_ip", "ipv4_address" };
    private static readonly string[] MacAttributes = { "mac", "mac_address" };

    private static async Task<DeviceLookups.Match?> FindAsync(HaService service, HaHotkeys hotkeys, DeviceLookups.Query q, CancellationToken ct)
    {
        for (int i = 0; i < 60 && !service.Connected && (service.Connecting || service.Error == null); i++) await Task.Delay(250, ct);
        if (!service.Connected) throw new Exception(service.Error ?? T("Not connected to Home Assistant."));

        string mac = DeviceLookups.Mac(q.Mac);
        HaDevice? device = null;
        HaEntity? entity = null; // an entity on its own, when it's that and no device
        string by = "";

        if (mac.Length > 0 && service.Devices.FirstOrDefault(d => d.Macs.Any(m => DeviceLookups.Mac(m) == mac)) is { } d1) { device = d1; by = T("MAC"); }
        if (device == null && service.Devices.FirstOrDefault(d => HostOf(d.ConfigUrl) == q.Ip) is { } d2) { device = d2; by = T("address"); }
        if (device == null)
        {
            // an entity that names the address or MAC: a tracker's attributes, a sensor whose state is the address
            foreach (var e in service.Entities)
            {
                bool ip = e.State == q.Ip || IpAttributes.Any(a => e.Text(a) == q.Ip);
                bool m = mac.Length > 0 && MacAttributes.Any(a => DeviceLookups.Mac(e.Text(a)) == mac);
                if (!ip && !m) continue;
                by = m ? T("MAC") : T("address");
                if (service.Device(service.DeviceOf(e.EntityId)) is { } d3 && !d3.Service) { device = d3; break; }
                entity ??= e;
            }
        }
        if (device == null && entity == null)
        {
            var loose = q.Names.Select(DeviceLookups.Loose).Where(n => n.Length >= 3).ToHashSet();
            if (service.Devices.FirstOrDefault(d => !d.Service && loose.Contains(DeviceLookups.Loose(d.Name))) is { } d4) { device = d4; by = T("name"); }
        }
        if (device == null && entity == null) return null;

        var lines = new List<DeviceLookups.Line>();
        if (device == null)
        {
            // a tracker or sensor that belongs to no device: just it
            var e = entity!;
            lines.Add(new(Domains.OneName(e.Domain), Value(e)));
            foreach (var a in IpAttributes.Concat(MacAttributes).Append("source_type"))
                if (e.Text(a) is { Length: > 0 } v) lines.Add(new(a.Replace('_', ' '), v));
            var key = service.ThingOf(e.EntityId)?.Key;
            return new DeviceLookups.Match(e.FriendlyName, by, lines, key == null ? null : () => ThingWindow.Open(service, hotkeys, key, Application.Current?.MainWindow));
        }

        var area = service.Area(device.AreaId);
        var thing = service.Thing("device:" + device.Id);
        area ??= service.Area(thing?.AreaId);
        if (area != null) lines.Add(new(T("Area"), service.Floor(area.FloorId) is { } floor ? $"{area.Name} · {floor.Name}" : area.Name));
        if (device.Manufacturer.Length > 0) lines.Add(new(T("Maker"), device.Manufacturer));
        if (device.Model.Length > 0) lines.Add(new(T("Model"), device.Model));
        if (device.SwVersion.Length > 0) lines.Add(new(T("Version"), device.SwVersion));
        if (device.Macs.Count > 0) lines.Add(new(T("MAC"), string.Join(", ", device.Macs)));
        if (device.ConfigUrl is { Length: > 0 } url) lines.Add(new(T("Page"), url));
        // its entities: what the box shows first, then the rest (configuration and diagnostics), a dozen at most
        var ids = thing != null ? thing.Shown.Concat(thing.All.Except(thing.Shown)).ToList()
            : service.Entities.Where(e => service.DeviceOf(e.EntityId) == device.Id).Select(e => e.EntityId).ToList();
        foreach (var id in ids.Take(12))
            if (service.Entity(id) is { } e)
                lines.Add(new(thing != null ? service.ShortName(thing, e) : e.FriendlyName, Value(e), e.Unavailable ? "#E45959" : null));
        if (ids.Count > 12) lines.Add(new("", F("and {0} more", ids.Count - 12)));

        return new DeviceLookups.Match(device.Name, by, lines, thing == null ? null : () => ThingWindow.Open(service, hotkeys, thing.Key, Application.Current?.MainWindow));
    }

    /// <summary>An entity's value; a time (a button's last press, a timestamp sensor) as local time.</summary>
    private static string Value(HaEntity e) =>
        e.State.Contains('T') && DateTimeOffset.TryParse(e.State, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
            ? t.LocalDateTime.ToString("g") : e.ValueText();

    /// <summary>The address in a device's own page's URL ("http://192.168.1.20/" → 192.168.1.20), or null.</summary>
    private static string? HostOf(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;
}
