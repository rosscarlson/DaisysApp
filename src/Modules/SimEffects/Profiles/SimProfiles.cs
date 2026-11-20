using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaisysApp.Settings;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// A SimEffects profile: a game (or none: the desktop), its port, the programs that pick it, and every sub-module's
/// settings for it (<see cref="Tools"/>: each tool's own, by its id, as <see cref="ToolScope.Props"/> name them).
/// </summary>
public sealed class SimProfile
{
    public string Name { get; set; } = "";
    /// <summary>(since 4.17.18) Its short name ("FH", "IR"…) for the title bar's SimEffects bubble; set from the game
    /// at first (<see cref="SimProfiles.DefaultShort"/>), editable on the Profile card. Empty: the default.</summary>
    public string? ShortName { get; set; }
    /// <summary>The game whose telemetry every tool reads (<see cref="SimData"/>); null: none.</summary>
    public Game? Game { get; set; }
    public int Port { get; set; } = 8000;
    /// <summary>Which Forza (since 3.24.0): Horizon 4, 5 or 6, Motorsport 7 or 8 (2023), for its cars' names
    /// (<see cref="CarNames"/>) and Analysis' laps; null: the one running (the newest while none is).</summary>
    public int? Version { get; set; }
    /// <summary>Program names (ForzaHorizon5.exe…): with <see cref="AutoStart"/> on, one starting plays this profile.</summary>
    public List<string> Programs { get; set; } = new();
    /// <summary>Auto Start (since 3.23.4): one of <see cref="Programs"/> starting uses and plays this profile, and its
    /// ending stops it (<see cref="GameSwitcher"/>). Null in a file from before: set from the old Game Switching.</summary>
    public bool? AutoStart { get; set; }
    /// <summary>Each tool's settings in this profile, by the tool's id.</summary>
    public Dictionary<string, JsonObject> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

/// <summary>Saved in %APPDATA%\DaisysApp\SimProfiles.json.</summary>
public sealed class SimProfilesSettings
{
    public List<SimProfile> Profiles { get; set; } = new();
    /// <summary>The profile in use, by name.</summary>
    public string? Active { get; set; }
    /// <summary>Game Switching, before 3.23.4 (for all profiles): read once into each profile's <see cref="SimProfile.AutoStart"/>.</summary>
    public bool GameSwitching { get; set; } = true;
    /// <summary>The profile in use is playing (since 3.19.0): stopped, every tool is dark, whatever its ON / OFF.</summary>
    public bool Playing { get; set; } = true;
    /// <summary>The Play / Stop shortcut.</summary>
    public string? PlayKey { get; set; }
}

/// <summary>
/// A tool's part in the profiles: which of its settings' properties are kept per profile, how to tell and switch
/// whether it's on (null for one without an ON / OFF: the shift points), and what to do once a profile's settings are
/// put into it (<see cref="Loaded"/>: it takes them in, as after a change on its tab).
/// </summary>
public sealed class ToolScope
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required object Settings { get; init; }
    public required string[] Props { get; init; }
    public Func<bool>? IsOn { get; init; }
    public Action<bool>? SetOn { get; init; }
    public required Action Loaded { get; init; }
    /// <summary>Before 3.18.0 it had Switch On With Game ticked: on in every game's profile at first.</summary>
    public bool OnWithGames { get; init; }
    /// <summary>A tool new to the profiles (none has its settings yet) is on in the ones this picks (Analysis: Forza Horizon's).</summary>
    public Func<SimProfile, bool>? OnAtFirst { get; init; }
}

/// <summary>
/// The SimEffects profiles (since 3.18.0): one list for the whole module. The profile in use decides the game every
/// tool reads (<see cref="SimData"/>) and each tool's settings: switching it (the Profile drop-down, the Profiles
/// tab, or a game starting: Auto Start, <see cref="GameSwitcher"/>) puts that profile's settings into every tool; a change made in
/// a tool is kept in the profile in use (its settings' Save calls <see cref="Saved"/>). On the UI thread, but for
/// <see cref="Saved"/>, which can come from any thread.
/// </summary>
public static class SimProfiles
{
    private const string FileName = "SimProfiles";
    private static readonly object gate = new();
    private static SimProfilesSettings settings = new();
    private static readonly List<ToolScope> tools = new();
    private static Timer? saveTimer;
    private static bool restoring;

    /// <summary>The profile in use changed, or the list did (on the UI thread).</summary>
    public static event Action? Changed;
    /// <summary>A profile was renamed (old name, new name): the Shaker renames its effects' profile.</summary>
    public static event Action<string, string>? Renamed;

    /// <summary>(since SimEffects Analysis 0.2.1) The one game of a product made for it (SimEffects Analysis: Forza
    /// Horizon): only its profile, always the one in use; null in Daisy's App.</summary>
    public static Game? OnlyGame { get; } = Enum.TryParse<Game>(AppPaths.Product?.Game, out var only) ? only : null;

    public static SimProfilesSettings Settings => settings;
    public static IReadOnlyList<SimProfile> All => settings.Profiles;
    public static IReadOnlyList<ToolScope> Tools => tools;
    /// <summary>The profile in use is playing: its tools that are on run (stopped: none does).</summary>
    public static bool Playing => settings.Playing;

    public static SimProfile Active
    {
        get
        {
            lock (gate) return settings.Profiles.FirstOrDefault(p => p.Name == settings.Active) ?? settings.Profiles[0];
        }
    }

    /// <summary>Loads them: a new install's are <see cref="SimDefaults"/>'s; an install from before 3.18.0 makes them from
    /// the Shaker's profiles, with a Desktop profile for no game.</summary>
    public static void Load()
    {
        bool fresh = !File.Exists(JsonStore.PathFor(FileName));
        settings = JsonStore.Load<SimProfilesSettings>(FileName);
        if (fresh && !File.Exists(JsonStore.PathFor("BassShaker")) && FromDefaults()) { }
        else if (fresh || settings.Profiles.Count == 0) FromShaker();
        foreach (var p in settings.Profiles)
        {
            p.AutoStart ??= settings.GameSwitching && p.Programs.Count > 0;
            p.ShortName ??= DefaultShort(p);
        }
        // (since 4.17.20) every game has its profile, with the game's port and processes, so its game starting plays it
        if (OnlyGame != null) settings.Profiles.RemoveAll(p => p.Game != OnlyGame);
        foreach (var g in Games.All.Cast<Game?>().Append(null).Where(g => OnlyGame == null || g == OnlyGame))
        {
            if (settings.Profiles.Any(p => p.Game == g)) continue;
            var made = New(g);
            made.AutoStart = made.Programs.Count > 0;
            made.ShortName = DefaultShort(made);
            for (int i = 2; settings.Profiles.Any(o => o.Name.Equals(made.Name, StringComparison.CurrentCultureIgnoreCase)); i++) made.Name = $"{New(g).Name} {i}";
            settings.Profiles.Add(made);
            Logging.Log.Here.Info($"SimEffects: profile {made.Name} made");
        }
        if (OnlyGame != null) settings.Active = settings.Profiles[0].Name;
        SimData.Follow(Active);
    }

    /// <summary>A new install: the default profiles, stopped until one's game starts (Auto Start plays it).</summary>
    private static bool FromDefaults()
    {
        var d = SimDefaults.Read();
        if (d.Profiles.Count == 0) return false;
        settings.Profiles.Clear();
        settings.Profiles.AddRange(d.Profiles);
        settings.Active = d.Profiles.Any(p => p.Name == d.Active) ? d.Active : d.Profiles[0].Name;
        settings.Playing = false;
        Logging.Log.Here.Info($"SimEffects profiles: the defaults ({settings.Profiles.Count})");
        Save();
        return true;
    }

    private static void FromShaker()
    {
        settings.Profiles.Clear();
        settings.Profiles.Add(new SimProfile { Name = T("Desktop") });
        try
        {
            var path = JsonStore.PathFor("BassShaker");
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject shaker)
            {
                foreach (var p in shaker["Profiles"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    var name = p["Name"]?.GetValue<string>() ?? "";
                    if (name.Length == 0 || settings.Profiles.Any(o => o.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) continue;
                    settings.Profiles.Add(new SimProfile
                    {
                        Name = name,
                        Game = Enum.TryParse<Game>(p["Game"]?.GetValue<string>(), out var g) ? g : Telemetry.Game.ForzaHorizon,
                        Port = p["Port"] is JsonValue v && v.TryGetValue(out int port) ? port : 8000,
                        Programs = p["Programs"]?.Deserialize<List<string>>(JsonStore.Options) ?? new(),
                    });
                }
                if (shaker["GameSwitching"] is JsonValue gs && gs.TryGetValue(out bool on)) settings.GameSwitching = on;
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("SimEffects profiles: the Shaker's profiles", ex); }
        if (settings.Profiles.Count == 1)
            foreach (var g in Games.All) settings.Profiles.Add(new SimProfile { Name = Games.Title(g), Game = g, Port = Games.Port(g), Programs = Games.Programs(g) });
        settings.Active = settings.Profiles[0].Name;
        Logging.Log.Here.Info($"SimEffects profiles: made from the Shaker's ({settings.Profiles.Count})");
        Save();
    }

    /// <summary>
    /// A tool takes part (in its constructor, once its settings are loaded): the profile in use's settings are put into
    /// them now; a profile without any for it starts with them as they are.
    /// </summary>
    public static void Register(ToolScope scope)
    {
        lock (gate)
        {
            tools.Add(scope);
            foreach (var p in settings.Profiles)
            {
                if (p.Tools.ContainsKey(scope.Id)) continue;
                var copy = Capture(scope);
                if (scope.OnWithGames && p.Game != null && scope.Props.Contains("Enabled")) copy["Enabled"] = true;
                if (scope.OnAtFirst?.Invoke(p) is { } first && scope.Props.Contains("Enabled")) copy["Enabled"] = first;
                p.Tools[scope.Id] = copy;
            }
            Restore(scope, Active);
        }
        SaveSoon();
    }

    /// <summary>A tool's settings were saved: kept in the profile in use. From any thread.</summary>
    public static void Saved(object toolSettings)
    {
        lock (gate)
        {
            if (restoring) return;
            var scope = tools.FirstOrDefault(t => ReferenceEquals(t.Settings, toolSettings));
            if (scope == null) return;
            Active.Tools[scope.Id] = Capture(scope);
        }
        SaveSoon();
    }

    /// <summary>Uses a profile and plays it (or, <paramref name="play"/> false, stops): every tool takes its settings for
    /// it, the ones on in it start.</summary>
    public static void Use(SimProfile p, string why, bool play = true)
    {
        if (p == Active && settings.Active == p.Name) { Play(play, why); return; }
        settings.Playing = play;
        lock (gate)
        {
            foreach (var t in tools) Active.Tools[t.Id] = Capture(t);
            settings.Active = p.Name;
            foreach (var t in tools) Restore(t, p);
        }
        Logging.Log.Here.Info($"SimEffects: profile {p.Name} ({why})");
        SimData.Follow(p);
        foreach (var t in tools)
        {
            try { t.Loaded(); }
            catch (Exception ex) { Logging.ErrorLog.Write($"{t.Id} profile", ex); }
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Plays or stops the profile in use (Play / Stop, its shortcut): its tools that are on start or go dark.</summary>
    public static void Play(bool on, string why)
    {
        if (settings.Playing == on) return;
        settings.Playing = on;
        Logging.Log.Here.Info($"SimEffects: profile {Active.Name} {(on ? "playing" : "stopped")} ({why})");
        foreach (var t in tools)
        {
            try { t.Loaded(); }
            catch (Exception ex) { Logging.ErrorLog.Write($"{t.Id} play", ex); }
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>A tool's ON / OFF in a profile (the Profiles tab): switched now if it's the profile in use.</summary>
    public static bool IsOn(SimProfile p, ToolScope t) =>
        p == Active && t.IsOn != null ? t.IsOn() : p.Tools.TryGetValue(t.Id, out var o) && o["Enabled"] is JsonValue v && v.TryGetValue(out bool on) && on;

    public static void SetOn(SimProfile p, ToolScope t, bool on)
    {
        if (p == Active && t.SetOn != null) { t.SetOn(on); return; }
        lock (gate)
        {
            if (!p.Tools.TryGetValue(t.Id, out var o)) p.Tools[t.Id] = o = new JsonObject();
            o["Enabled"] = on;
        }
        Save();
    }

    /// <summary>Adds a profile (its tools' settings as the one in use's) and uses it (unless <paramref name="use"/> is false).</summary>
    public static void Add(SimProfile p, bool use = true)
    {
        string baseName = p.Name;
        p.AutoStart ??= p.Programs.Count > 0;
        p.ShortName ??= DefaultShort(p);
        lock (gate)
        {
            for (int i = 2; settings.Profiles.Any(o => o.Name.Equals(p.Name, StringComparison.CurrentCultureIgnoreCase)); i++) p.Name = $"{baseName} {i}";
            foreach (var t in tools) p.Tools[t.Id] = Capture(t);
            settings.Profiles.Add(p);
        }
        Logging.Log.Here.Info($"SimEffects: profile {p.Name} added");
        if (use) Use(p, "added");
        else Save();
    }

    /// <summary>A new profile for <paramref name="game"/> (null: Desktop): the game's name, port and processes.</summary>
    private static SimProfile New(Game? game) => game is { } g
        ? new SimProfile { Name = Games.Title(g), Game = g, Port = Games.Port(g), Programs = Games.Programs(g) }
        : new SimProfile { Name = T("Desktop") };

    public static void Remove(SimProfile p)
    {
        if (settings.Profiles.Count < 2) return;
        bool active = p == Active;
        lock (gate) settings.Profiles.Remove(p);
        Logging.Log.Here.Info($"SimEffects: profile {p.Name} deleted");
        if (active) { settings.Active = null; Use(settings.Profiles[0], "the one in use was deleted"); }
        else Edited();
    }

    public static bool Rename(SimProfile p, string name)
    {
        if (name.Length == 0 || settings.Profiles.Any(o => o != p && o.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) return false;
        string old = p.Name;
        lock (gate)
        {
            bool active = settings.Active == old;
            p.Name = name;
            if (active) settings.Active = name;
        }
        Renamed?.Invoke(old, name);
        Edited();
        return true;
    }

    /// <summary>(since 4.17.18) A profile's short name at first: its game's ("FH", "FM", "IR", "ACC", "AC", "ACE"); with
    /// none, its name (up to 8 letters).</summary>
    public static string DefaultShort(SimProfile p) => p.Game switch
    {
        Game.ForzaHorizon => "FH",
        Game.ForzaMotorsport => "FM",
        Game.IRacing => "IR",
        Game.Acc => "ACC",
        Game.AssettoCorsa => "AC",
        Game.AcEvo => "ACE",
        _ => p.Name.Length > 8 ? p.Name[..8] : p.Name,
    };

    /// <summary>(since 4.17.18) The short name shown: its own, else <see cref="DefaultShort"/>.</summary>
    public static string ShortOf(SimProfile p) => string.IsNullOrWhiteSpace(p.ShortName) ? DefaultShort(p) : p.ShortName.Trim();

    /// <summary>A profile's game, port or programs changed: saved, and the telemetry follows.</summary>
    public static void Edited()
    {
        SimData.Follow(Active);
        Save();
        Changed?.Invoke();
    }

    // ---- each tool's settings in and out of a profile

    private static JsonObject Capture(ToolScope t)
    {
        var o = new JsonObject();
        foreach (var name in t.Props)
            if (t.Settings.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } prop)
                o[name] = JsonSerializer.SerializeToNode(prop.GetValue(t.Settings), prop.PropertyType, JsonStore.Options);
        return o;
    }

    private static void Restore(ToolScope t, SimProfile p)
    {
        if (!p.Tools.TryGetValue(t.Id, out var o)) return;
        restoring = true;
        try
        {
            foreach (var name in t.Props)
            {
                if (!o.TryGetPropertyValue(name, out var node) || node == null) continue;
                if (t.Settings.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { CanWrite: true } prop) continue;
                try { prop.SetValue(t.Settings, node.Deserialize(prop.PropertyType, JsonStore.Options)); }
                catch (Exception ex) { Logging.Log.Here.Warn($"SimEffects profile {p.Name}: {t.Id}.{name}", ex); }
            }
        }
        finally { restoring = false; }
    }

    // ---- saving

    private static void SaveSoon()
    {
        lock (gate) (saveTimer ??= new Timer(_ => Save())).Change(500, Timeout.Infinite);
    }

    public static void Save()
    {
        try
        {
            string json;
            lock (gate) json = JsonSerializer.Serialize(settings, JsonStore.Options);
            JsonStore.WriteAtomic(JsonStore.PathFor(FileName), json);
        }
        catch (Exception ex) { Logging.Log.Here.Warn("SimEffects profiles: saving", ex); }
    }
}
