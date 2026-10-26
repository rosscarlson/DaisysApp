using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>
/// Each entity's shortcuts: on / off for one that switches, up / down for one with a level (Core's Hotkeys, so in
/// Settings → Shortcuts too). Kept in <see cref="HaSettings.Keys"/>; only entities given one are registered, the
/// others are made when their shortcut window opens. They work anywhere, the tab open or not.
/// </summary>
internal sealed class HaHotkeys : IDisposable
{
    private const string Prefix = "HA.";

    private readonly HaService service;
    private HaSettings Settings => service.Settings;

    public HaHotkeys(HaService service) => this.service = service;

    public void Register()
    {
        Hotkeys.Replace(Prefix, Settings.Keys.ToList().SelectMany(kv => Specs(kv.Key, kv.Value)));
        Log.Here.Debug($"Shortcuts: {Settings.Keys.Count} entities");
    }

    /// <summary>An entity's shortcuts (made if it had none yet), for its shortcut window.</summary>
    public Hotkey[] For(HaEntity e, string name)
    {
        var keys = Settings.Keys.TryGetValue(e.EntityId, out var k) ? k : new EntityKeys();
        keys.Name = name;
        keys.CanToggle = Domains.CanToggle(e);
        keys.HasLevel = Domains.LevelOf(e) != null;
        return Specs(e.EntityId, keys).Select(x => Hotkeys.Add(x.Spec, x.Pressed)).ToArray();
    }

    /// <summary>Whether an entity can have any (it switches, or has a level).</summary>
    public static bool Offered(HaEntity e) => Domains.CanToggle(e) || Domains.LevelOf(e) != null;

    public bool Has(string entityId) => Settings.Keys.TryGetValue(entityId, out var k) && k.Any;

    /// <summary>An entity's shortcuts in short ("Ctrl+F5 · ▲ F6"), for its tooltip.</summary>
    public string Summary(string entityId) =>
        Settings.Keys.TryGetValue(entityId, out var k) && k.Any
            ? string.Join(" · ", new[] { (k.Toggle, T("on / off")), (k.Up, "▲"), (k.Down, "▼") }.Where(x => x.Item1 != null).Select(x => $"{x.Item2} {x.Item1}"))
            : "";

    private IEnumerable<(HotkeySpec Spec, Action Pressed)> Specs(string entityId, EntityKeys keys)
    {
        var kinds = new List<(string Kind, string Label)>();
        if (keys.CanToggle) kinds.Add(("Toggle", T("On / off")));
        if (keys.HasLevel) { kinds.Add(("Up", T("Up"))); kinds.Add(("Down", T("Down"))); }
        foreach (var (kind, label) in kinds)
            yield return (new HotkeySpec
            {
                Id = $"{Prefix}{kind}:{entityId}", Module = T("HA"), Target = keys.Name, Name = label,
                Get = () => Settings.Keys.TryGetValue(entityId, out var k) ? kind switch { "Toggle" => k.Toggle, "Up" => k.Up, _ => k.Down } : null,
                Set = v =>
                {
                    if (!Settings.Keys.TryGetValue(entityId, out var k)) Settings.Keys[entityId] = k = keys;
                    k.Name = keys.Name;
                    k.CanToggle = keys.CanToggle;
                    k.HasLevel = keys.HasLevel;
                    if (kind == "Toggle") k.Toggle = v; else if (kind == "Up") k.Up = v; else k.Down = v;
                    if (!k.Any) Settings.Keys.Remove(entityId);
                    Settings.Save();
                },
            }, () => Pressed(entityId, kind));
    }

    private void Pressed(string entityId, string kind)
    {
        try
        {
            if (kind == "Toggle") service.Toggle(entityId);
            else service.Step(entityId, kind == "Up" ? 1 : -1);
        }
        catch (Exception ex) { ErrorLog.Write("HA shortcut", ex); }
    }

    public void Dispose() => Hotkeys.Replace(Prefix, Array.Empty<(HotkeySpec, Action)>());
}
