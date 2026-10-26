using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows.Threading;
using DaisysApp.Logging;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>
/// The connection to Home Assistant and what it has said: every entity's state (kept live by its state_changed
/// events), its areas, floors and devices, and the boxes they make (<see cref="Things"/>). Connected while the module
/// runs (so its shortcuts work with the tab closed), connecting again on its own when the connection drops. Its state
/// is changed and <see cref="Changed"/> raised on the UI thread.
/// </summary>
public sealed class HaService : IDisposable
{
    private static readonly string[] RegistryEvents = { "area_registry_updated", "device_registry_updated", "entity_registry_updated", "floor_registry_updated" };

    public HaSettings Settings { get; }
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private HaClient? client;
    private CancellationTokenSource? run;
    private bool disposed;

    private readonly Dictionary<string, HaEntity> states = new();
    private readonly Dictionary<string, HaArea> areas = new();
    private readonly Dictionary<string, HaFloor> floors = new();
    private readonly Dictionary<string, HaDevice> devices = new();
    private readonly Dictionary<string, HaRegistryEntry> registry = new();
    private readonly Dictionary<string, HaThing> thingOf = new(); // entity id → its box
    private Dictionary<string, HaThing> thingByKey = new();

    public HaService(HaSettings settings) => Settings = settings;

    // ---------------------------------------------------------------- state

    public IReadOnlyList<HaThing> Things { get; private set; } = Array.Empty<HaThing>();
    public string? Error { get; private set; }
    public string Version { get; private set; } = "";
    public DateTime? Updated { get; private set; }
    public bool Connected => client != null && Error == null;
    public bool Connecting { get; private set; }
    /// <summary>The last thing Home Assistant refused (a service call), and when.</summary>
    public (string Text, DateTime At)? Problem { get; private set; }

    /// <summary>Raised on the UI thread: with the entities whose state changed, or null when anything else did
    /// (connected, disconnected, devices added or moved, a setting of what's shown).</summary>
    public event Action<IReadOnlyCollection<string>?>? Changed;

    public void RaiseChanged() => Changed?.Invoke(null);

    public HaEntity? Entity(string id) => states.TryGetValue(id, out var e) ? e : null;
    public HaThing? Thing(string key) => thingByKey.TryGetValue(key, out var t) ? t : null;
    public HaThing? ThingOf(string entityId) => thingOf.TryGetValue(entityId, out var t) ? t : null;
    public HaArea? Area(string? id) => id != null && areas.TryGetValue(id, out var a) ? a : null;
    public HaFloor? Floor(string? id) => id != null && floors.TryGetValue(id, out var f) ? f : null;
    public HaDevice? Device(string? id) => id != null && devices.TryGetValue(id, out var d) ? d : null;
    public IEnumerable<HaArea> Areas => areas.Values;
    public IEnumerable<HaFloor> Floors => floors.Values;

    /// <summary>An entity's name in its device's box: "Kitchen Light Power" in "Kitchen Light" is "Power".</summary>
    public string ShortName(HaThing thing, HaEntity e)
    {
        if (thing.DeviceId == null) return e.FriendlyName;
        string n = e.FriendlyName;
        if (n.StartsWith(thing.Name, StringComparison.CurrentCultureIgnoreCase)) n = n[thing.Name.Length..].Trim(' ', '-', ':', '·');
        return n.Length > 0 ? n : Domains.OneName(e.Domain);
    }

    // ---------------------------------------------------------------- the connection

    /// <summary>Connects (again) with the settings as they are now; nothing without an address and a token.</summary>
    public void Reconnect()
    {
        run?.Cancel();
        run = null;
        client?.Dispose();
        client = null;
        if (disposed || !Settings.HasServer || Settings.Token.Length == 0) { Error = null; Connecting = false; RaiseChanged(); return; }
        run = new CancellationTokenSource();
        _ = RunAsync(run.Token);
    }

    private async Task RunAsync(CancellationToken stop)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            HaClient? c = null;
            try
            {
                Connecting = true;
                RaiseChanged();
                c = await HaClient.ConnectAsync(Settings, stop);
                var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                c.Closed += r => ended.TrySetResult(r);
                c.Event += OnEvent;
                await c.SubscribeAsync("state_changed");
                foreach (var ev in RegistryEvents)
                    try { await c.SubscribeAsync(ev); }
                    catch (HaException) { } // (an older Home Assistant without floors)
                stop.ThrowIfCancellationRequested();
                await LoadAsync(c);
                stop.ThrowIfCancellationRequested();
                client = c;
                Version = c.Version;
                Error = null;
                Connecting = false;
                failures = 0;
                Updated = DateTime.Now;
                if (!Settings.Configured || Settings.SetupSkipped) { Settings.Configured = true; Settings.SetupSkipped = false; Settings.Save(); }
                Log.Here.Info($"Connected to Home Assistant {c.Version} at {Settings.BaseUri()}: {states.Count} entities, {devices.Count} devices, {areas.Count} areas; {Things.Count} shown");
                RaiseChanged();
                // a ping now and then, so a connection that died quietly is noticed
                while (true)
                {
                    var done = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(30), stop));
                    if (done == ended.Task) throw new HaException(ended.Task.Result);
                    await done; // (throws when stopped)
                    await c.SendAsync(new Dictionary<string, object?> { ["type"] = "ping" }, TimeSpan.FromSeconds(10));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (stop.IsCancellationRequested) break;
                string msg = ex is HaException ? ex.Message : F("Couldn't connect: {0}", ex.Message);
                if (Error != msg) Log.Here.Warn($"Home Assistant: {msg}");
                Error = msg;
                Connecting = false;
                if (client == c) client = null;
                RaiseChanged();
            }
            finally
            {
                if (c != null) { c.Event -= OnEvent; if (client == c) client = null; c.Dispose(); }
            }
            failures++;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 << Math.Min(failures - 1, 4))), stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Areas, floors, devices and the entity registry (each may be refused to a user that isn't an
    /// administrator: then everything is on its own, with no area), then every state.</summary>
    private async Task LoadAsync(HaClient c)
    {
        await LoadRegistriesAsync(c);
        var all = await c.CommandAsync("get_states");
        states.Clear();
        if (all.ValueKind == JsonValueKind.Array)
            foreach (var s in all.EnumerateArray())
            {
                var e = new HaEntity(s);
                if (e.EntityId.Length > 0) states[e.EntityId] = e;
            }
        Rebuild();
    }

    private bool refusedLogged;

    private async Task LoadRegistriesAsync(HaClient c)
    {
        async Task<JsonElement?> List(string type)
        {
            try { return await c.CommandAsync(type); }
            catch (HaException ex)
            {
                if (!refusedLogged) Log.Here.Info($"Home Assistant: {type}: {ex.Message}");
                return null;
            }
        }
        var a = await List("config/area_registry/list");
        var f = await List("config/floor_registry/list");
        var d = await List("config/device_registry/list");
        var r = await List("config/entity_registry/list");
        refusedLogged = true;
        static IEnumerable<JsonElement> Items(JsonElement? e) => e is { ValueKind: JsonValueKind.Array } x ? x.EnumerateArray() : Enumerable.Empty<JsonElement>();
        static bool Set(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        areas.Clear();
        foreach (var x in Items(a))
            if (HaClient.Str(x, "area_id") is { } id) areas[id] = new HaArea(id, HaClient.Str(x, "name") ?? id, HaClient.Str(x, "floor_id"));
        floors.Clear();
        foreach (var x in Items(f))
            if (HaClient.Str(x, "floor_id") is { } id)
                floors[id] = new HaFloor(id, HaClient.Str(x, "name") ?? id, x.TryGetProperty("level", out var l) && l.TryGetInt32(out int n) ? n : 0);
        devices.Clear();
        foreach (var x in Items(d))
        {
            if (HaClient.Str(x, "id") is not { } id || Set(x, "disabled_by")) continue;
            devices[id] = new HaDevice(id, HaClient.Str(x, "name_by_user") ?? HaClient.Str(x, "name") ?? id, HaClient.Str(x, "area_id"),
                HaClient.Str(x, "manufacturer") ?? "", HaClient.Str(x, "model") ?? "", HaClient.Str(x, "entry_type") == "service");
        }
        registry.Clear();
        foreach (var x in Items(r))
        {
            if (HaClient.Str(x, "entity_id") is not { } id || Set(x, "disabled_by")) continue;
            registry[id] = new HaRegistryEntry(id, HaClient.Str(x, "device_id"), HaClient.Str(x, "area_id"), Set(x, "hidden_by"), Set(x, "entity_category"),
                HaClient.Str(x, "name"), HaClient.Str(x, "original_name"));
        }
    }

    /// <summary>The boxes: a device with its entities, or an entity on its own; entities Home Assistant hides, its
    /// configuration and diagnostic ones, and kinds with nothing to show (<see cref="Domains.Skipped"/>) only in a
    /// device's window.</summary>
    private void Rebuild()
    {
        var things = new List<HaThing>();
        var byDevice = new Dictionary<string, HaThing>();
        thingOf.Clear();
        foreach (var e in states.Values.OrderBy(e => e.EntityId, StringComparer.Ordinal))
        {
            registry.TryGetValue(e.EntityId, out var reg);
            bool inBox = !Domains.Skipped.Contains(e.Domain) && reg is not { Hidden: true } && reg is not { Category: true };
            if (reg?.DeviceId is { } did && devices.TryGetValue(did, out var dev))
            {
                if (!byDevice.TryGetValue(did, out var t))
                    byDevice[did] = t = new HaThing
                    {
                        Key = "device:" + did, Name = dev.Name, DeviceId = did, AreaId = dev.AreaId,
                        About = string.Join(" · ", new[] { dev.Manufacturer, dev.Model }.Where(s => s.Length > 0)),
                    };
                t.All.Add(e.EntityId);
                if (inBox) t.Shown.Add(e.EntityId);
                thingOf[e.EntityId] = t;
            }
            else if (inBox)
            {
                var t = new HaThing { Key = "entity:" + e.EntityId, Name = e.FriendlyName, AreaId = reg?.AreaId, About = Domains.OneName(e.Domain) };
                t.All.Add(e.EntityId);
                t.Shown.Add(e.EntityId);
                things.Add(t);
                thingOf[e.EntityId] = t;
            }
        }
        foreach (var t in byDevice.Values.Where(t => t.Shown.Count > 0))
        {
            // a device without an area: the area its entities have
            if (t.AreaId == null && t.Shown.Select(id => registry.TryGetValue(id, out var r) ? r.AreaId : null).FirstOrDefault(a => a != null) is { } area)
                t.AreaId = area;
            things.Add(t);
        }
        foreach (var t in things)
        {
            var sorted = t.Shown.OrderBy(id => Domains.Rank(states[id].Domain)).ToList();
            t.Shown.Clear();
            t.Shown.AddRange(sorted);
            t.Main = t.Shown.FirstOrDefault(id => Domains.Controllable(states[id].Domain));
            t.Type = states[t.Main ?? t.Shown[0]].Domain;
        }
        Things = things.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        thingByKey = things.ToDictionary(t => t.Key);
    }

    // ---------------------------------------------------------------- live changes

    private readonly ConcurrentQueue<JsonElement> events = new();
    private int draining;
    private DispatcherOperation? registryReload;

    /// <summary>On the client's reading task: queued, and handled on the UI thread a moment later (many at once
    /// when a house is busy).</summary>
    private void OnEvent(int id, JsonElement ev)
    {
        events.Enqueue(ev);
        if (Interlocked.Exchange(ref draining, 1) == 0)
            ui.BeginInvoke(async () =>
            {
                await Task.Delay(150);
                Drain();
            });
    }

    private void Drain()
    {
        Volatile.Write(ref draining, 0);
        if (disposed) return;
        var changed = new HashSet<string>();
        bool structure = false, registries = false;
        while (events.TryDequeue(out var ev))
        {
            if (HaClient.Str(ev, "event_type") != "state_changed") { registries = true; continue; }
            if (!ev.TryGetProperty("data", out var data) || HaClient.Str(data, "entity_id") is not { } id) continue;
            if (!data.TryGetProperty("new_state", out var ns) || ns.ValueKind != JsonValueKind.Object)
            {
                structure |= states.Remove(id);
                continue;
            }
            if (!states.ContainsKey(id)) structure = true;
            states[id] = new HaEntity(ns);
            changed.Add(id);
        }
        if (registries) ReloadRegistriesSoon();
        if (structure) Rebuild();
        Updated = DateTime.Now;
        Changed?.Invoke(structure ? null : changed);
    }

    /// <summary>An area, floor, device or entity was added, renamed or moved: read them again (once, after a pause).</summary>
    private void ReloadRegistriesSoon()
    {
        if (registryReload != null) return;
        registryReload = ui.BeginInvoke(async () =>
        {
            await Task.Delay(2000);
            registryReload = null;
            if (client is not { } c) return;
            try
            {
                await LoadRegistriesAsync(c);
                Rebuild();
                RaiseChanged();
            }
            catch (HaException) { }
        });
    }

    // ---------------------------------------------------------------- doing things

    private readonly Dictionary<string, (double Value, DateTime At)> lastSet = new();

    /// <summary>Calls a service on an entity; what Home Assistant refuses goes in <see cref="Problem"/> and the log.</summary>
    public async void Call(string entityId, string service, Dictionary<string, object?>? data = null, string? domain = null)
    {
        domain ??= entityId[..entityId.IndexOf('.')];
        string what = $"{domain}.{service} {entityId}" + (data is { Count: > 0 } ? " " + JsonSerializer.Serialize(data) : "");
        try
        {
            var c = client ?? throw new HaException(Error ?? T("Not connected to Home Assistant."));
            Log.Here.Info(what);
            await c.CallServiceAsync(domain, service, entityId, data);
        }
        catch (Exception ex)
        {
            Log.Here.Warn($"{what}: {ex.Message}");
            Problem = (F("{0}: {1}", entityId, ex.Message), DateTime.Now);
            RaiseChanged();
        }
    }

    public void Toggle(string entityId)
    {
        if (Entity(entityId) is not { } e) { Log.Here.Warn($"Toggle {entityId}: not known (not connected?)"); return; }
        var (service, data) = Domains.Toggle(e);
        Call(entityId, service, data);
    }

    public void Press(string entityId)
    {
        if (Entity(entityId) is not { } e) return;
        Call(entityId, Domains.PressService(e.Domain));
    }

    public void SetLevel(string entityId, double value)
    {
        if (Entity(entityId) is not { } e) return;
        lastSet[entityId] = (value, DateTime.Now);
        var (service, data) = Domains.SetLevel(e, value);
        Call(entityId, service, data);
    }

    /// <summary>A level one step up or down (a shortcut): presses in quick succession add up before Home Assistant
    /// has said what the last one did.</summary>
    public void Step(string entityId, int direction)
    {
        if (Entity(entityId) is not { } e) { Log.Here.Warn($"Step {entityId}: not known (not connected?)"); return; }
        if (Domains.LevelOf(e) is not { } level) return;
        double from = lastSet.TryGetValue(entityId, out var last) && DateTime.Now - last.At < TimeSpan.FromSeconds(1.5) ? last.Value : level.Value;
        double v = Math.Clamp(level.Min + Math.Round((from - level.Min) / level.Step + direction) * level.Step, level.Min, level.Max);
        if (v == from) return;
        SetLevel(entityId, v);
    }

    public void Start() => Reconnect();

    public void Dispose()
    {
        disposed = true;
        run?.Cancel();
        client?.Dispose();
        client = null;
    }
}
