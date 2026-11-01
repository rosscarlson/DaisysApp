using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Applets.Binder.Input;

namespace DaisysApp.Applets.Binder;

/// <summary>
/// A kind of game's usual actions, in the order the Template Wizard asks for them (<c>Templates\*.json</c> beside the module,
/// or the user's own in <c>%APPDATA%\DaisysApp\Binder Templates\</c>: a shooter's can be added). Each action's id is its
/// role, so a template's bindings go into any title whose actions have the same roles.
/// </summary>
public sealed class TemplateDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Its place among the wizard's first choices.</summary>
    public int Order { get; set; }
    /// <summary>What the example device draws while it's set up.</summary>
    public TitleKind Kind { get; set; }
    /// <summary>The titles' kinds it's for, listed first when copying into one.</summary>
    public List<TitleKind> Kinds { get; set; } = new();
    public List<TemplateSection> Sections { get; set; } = new();

    [JsonIgnore] public string File { get; set; } = "";

    public bool For(TitleKind k) => Kinds.Count == 0 ? Kind == k : Kinds.Contains(k);
}

public sealed class TemplateSection
{
    public string Name { get; set; } = "";
    /// <summary>The usual ones, before the more advanced sections.</summary>
    public bool Advanced { get; set; }
    public List<ActionDef> Actions { get; set; } = new();
}

/// <summary>A template the user has filled in (renamable), in Binder.json.</summary>
public sealed class MyTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>Its <see cref="TemplateDef.Id"/>.</summary>
    public string Base { get; set; } = "";
    /// <summary>By role, its two bindings.</summary>
    public Dictionary<string, List<Bind>> Binds { get; set; } = new();
    /// <summary>The section the wizard was last on.</summary>
    public string? Section { get; set; }
}

public static class Templates
{
    private static List<TemplateDef>? all;

    public static string UserFolder => Path.Combine(AppPaths.SettingsFolder, "Binder Templates");
    public static string BuiltInFolder => Path.Combine(Path.GetDirectoryName(typeof(Templates).Assembly.Location) ?? "", "Templates");

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IReadOnlyList<TemplateDef> All => all ??= Read();

    public static TemplateDef? Get(string id) => All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static List<TemplateDef> Read()
    {
        var found = new Dictionary<string, TemplateDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { BuiltInFolder, UserFolder })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<TemplateDef>(File.ReadAllText(file), Json);
                    if (t == null || t.Id.Length == 0 || t.Name.Length == 0 || t.Sections.Count == 0) { Logging.Log.Here.Warn($"Binder: template {file} has no id, name or sections"); continue; }
                    foreach (var a in t.Sections.SelectMany(s => s.Actions))
                    {
                        a.Role ??= a.Id;
                        if (a.Id.Length == 0) a.Id = a.Role;
                    }
                    t.File = file;
                    found[t.Id] = t;
                }
                catch (Exception ex) { Logging.Log.Here.Warn($"Binder: template {file} couldn't be read: {ex.Message}"); }
            }
        }
        return found.Values.OrderBy(t => t.Order).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A template as a title's data, so the bindings list, the example device and copying work on it as on a game.</summary>
    public static TitleData? Data(MyTemplate mine)
    {
        if (Get(mine.Base) is not { } def) return null;
        var title = new TitleDef { Id = "template:" + mine.Id, Name = mine.Name, Kind = def.Kind, Format = TemplateFormat.FormatId };
        var data = new TitleData { Def = title, Setup = new TitleSetup { Title = title.Id }, State = mine, Summary = mine.Name };
        int order = 0;
        foreach (var s in def.Sections)
            foreach (var a in s.Actions)
            {
                // (what it was set to before is its default: going through the wizard again starts from it, Reset goes back to it)
                var defaults = mine.Binds.TryGetValue(a.Id, out var saved)
                    ? Enumerable.Range(0, TemplateFormat.SlotCount).Select(i => i < saved.Count ? saved[i] : Bind.None).ToArray()
                    : a.Keys.Select(Keys.ParseDefault).Where(b => b != null).Select(b => b!).Take(TemplateFormat.SlotCount).ToArray();
                var action = new BinderAction
                {
                    Id = a.Id, Label = a.Label ?? a.Id, Group = s.Name, IsAxis = a.Axis ?? false, Centred = a.Centred ?? true, Role = a.Role,
                    Defaults = defaults, Order = order++,
                };
                data.Actions.Add(action);
                if (saved != null) data.Binds[a.Id] = defaults.ToArray();
            }
        return data;
    }

    /// <summary>How many of its actions have something bound.</summary>
    public static int Bound(MyTemplate mine) => mine.Binds.Count(p => p.Value.Any(b => !b.IsNone));

    /// <summary>
    /// A title's bindings into the template for its kind (the one filled in before, or a new one): each of the template's
    /// actions a title's action with the same role has something bound to gets those; the rest stay as they were.
    /// </summary>
    public static (MyTemplate Template, int Copied)? FromTitle(List<MyTemplate> mine, TitleData title)
    {
        if (All.FirstOrDefault(t => t.For(title.Def.Kind)) is not { } def) return null;
        var into = mine.FirstOrDefault(m => m.Base.Equals(def.Id, StringComparison.OrdinalIgnoreCase));
        if (into == null)
        {
            into = new MyTemplate { Name = F("My {0} Template", def.Name), Base = def.Id };
            mine.Add(into);
        }
        int copied = 0;
        foreach (var a in def.Sections.SelectMany(s => s.Actions))
        {
            var from = title.Actions.FirstOrDefault(t => t.Role != null && t.Role.Equals(a.Role, StringComparison.OrdinalIgnoreCase));
            if (from == null) continue;
            var probe = new BinderAction { Id = a.Id, Label = a.Id, IsAxis = a.Axis ?? false };
            var binds = title.Get(from).Where(b => !b.IsNone && TemplateFormat.RefuseAny(probe, b) == null).Take(TemplateFormat.SlotCount).ToList();
            if (binds.Count == 0) continue;
            while (binds.Count < TemplateFormat.SlotCount) binds.Add(Bind.None);
            into.Binds[a.Id] = binds;
            copied++;
        }
        Logging.Log.Here.Info($"Binder: {title.Def.Name} saved into template {into.Name}: {copied} actions");
        return (into, copied);
    }

    /// <summary>Its sections in order (the wizard's steps).</summary>
    public static List<string> Sections(MyTemplate mine) => Get(mine.Base)?.Sections.Select(s => s.Name).ToList() ?? new();
}

/// <summary>
/// A template's "format": two slots an action, any key, mouse or controller input (an axis for an axis), kept in Binder.json
/// (<see cref="MyTemplate.Binds"/>). Not one a title's JSON can name.
/// </summary>
public sealed class TemplateFormat : IBindFormat
{
    public const string FormatId = "template";
    public const int SlotCount = 2;

    public string Id => FormatId;
    public InputKind Input => InputKind.DirectInput;

    public IReadOnlyList<Check> Check(TitleDef def, string folder) => [];
    public TitleData Load(TitleDef def, TitleSetup setup) => throw new BinderException(T("A template is opened from the Template Wizard"));

    public void Save(TitleData data, IInputSource input)
    {
        if (data.State is not MyTemplate mine) return;
        // (every action, cleared ones too, so one cleared stays cleared and isn't given its usual key again)
        mine.Binds = data.Actions.ToDictionary(a => a.Id, a => data.Get(a).ToList());
    }

    public int Slots(BinderAction a) => SlotCount;
    public string SlotName(BinderAction a, int slot) => slot == 0 ? T("First") : T("Second");

    public string? Refuse(TitleData data, BinderAction a, int slot, Bind b) => RefuseAny(a, b);

    /// <summary>An axis takes an axis (a controller's or the mouse's); anything else takes anything but a whole axis.</summary>
    public static string? RefuseAny(BinderAction a, Bind b)
    {
        if (b.IsNone) return null;
        if (a.IsAxis && !b.IsAxis) return T("This takes an axis");
        if (!a.IsAxis && b.IsAxis) return T("This takes a key or button: move the axis less, or bind it to an axis");
        return null;
    }
}
