using System.Windows;

namespace DaisysApp.Applets.Zabbix.Widgets;

/// <summary>
/// Keeps the Zabbix widgets: shows the saved ones when the app starts, makes new ones (from a host's box, a tile, a
/// graph or a history window), and saves where they are in <see cref="ZabbixSettings.Widgets"/>.
/// </summary>
internal sealed class ZabbixWidgets
{
    private readonly ZabbixService service;
    private readonly Dictionary<Guid, ZabbixWidgetWindow> windows = new();

    /// <summary>Raised when a widget is added, changed or deleted.</summary>
    public event Action? Changed;

    public ZabbixWidgets(ZabbixService service) => this.service = service;

    private ZabbixSettings Settings => service.Settings;

    public IReadOnlyList<ZabbixWidgetDefinition> All => Settings.Widgets;

    public void Start()
    {
        foreach (var def in Settings.Widgets.ToList()) Open(def);
    }

    public void CreateHost(string hostId, string title, Window? near) =>
        Create(new ZabbixWidgetDefinition { Kind = ZabbixWidgetKind.Host, HostId = hostId, Title = title }, near, 260, 190);

    public void CreateGroup(string hostId, string groupKey, string title, Window? near) =>
        Create(new ZabbixWidgetDefinition { Kind = ZabbixWidgetKind.Group, HostId = hostId, Group = groupKey, Title = title }, near, 320, 150);

    public void CreateItems(string hostId, IEnumerable<string> keys, string title, Window? near) =>
        Create(new ZabbixWidgetDefinition { Kind = ZabbixWidgetKind.Items, HostId = hostId, ItemKeys = keys.ToList(), Title = title }, near, 320, 150);

    /// <summary>A new widget beside <paramref name="near"/>, ready to be placed.</summary>
    private void Create(ZabbixWidgetDefinition def, Window? near, int width, int height)
    {
        // in physical pixels, a little in from the window it was made from
        double scale = near != null ? PresentationSource.FromVisual(near)?.CompositionTarget?.TransformToDevice.M11 ?? 1 : 1;
        double left = near?.WindowState == WindowState.Maximized ? 0 : near?.Left ?? 100, top = near?.WindowState == WindowState.Maximized ? 0 : near?.Top ?? 100;
        def.X = (int)((left + 60) * scale);
        def.Y = (int)((top + 80) * scale);
        def.Width = (int)(width * scale);
        def.Height = (int)(height * scale);
        Settings.Widgets.Add(def);
        Settings.Save();
        Open(def)?.BeginEdit();
        Changed?.Invoke();
    }

    public void Edit(ZabbixWidgetDefinition def)
    {
        if (windows.TryGetValue(def.Id, out var w)) w.BeginEdit();
    }

    public void Delete(ZabbixWidgetDefinition def)
    {
        if (windows.Remove(def.Id, out var w)) w.Close();
        Settings.Widgets.RemoveAll(d => d.Id == def.Id);
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>What a widget is, for the list on the tab.</summary>
    public string TitleOf(ZabbixWidgetDefinition def)
    {
        string host = service.Host(def.HostId)?.Display ?? "";
        return def.Kind == ZabbixWidgetKind.Host ? (host.Length > 0 ? host : def.Title) : host.Length > 0 ? $"{host} · {def.Title}" : def.Title;
    }

    private ZabbixWidgetWindow? Open(ZabbixWidgetDefinition def)
    {
        try
        {
            var w = new ZabbixWidgetWindow(def, service, () => { Settings.Save(); Changed?.Invoke(); }, () => Delete(def));
            windows[def.Id] = w;
            w.Show();
            return w;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Zabbix widget", ex);
            return null;
        }
    }

    public void Shutdown()
    {
        foreach (var w in windows.Values) w.Close();
        windows.Clear();
    }
}
