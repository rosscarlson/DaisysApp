using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using DaisysApp.Applets.Binder.Formats;

namespace DaisysApp.Applets.Binder;

/// <summary>What the example device draws: a cockpit's stick and throttle, or a wheel and pedals.</summary>
public enum TitleKind { Space, Flight, Racing }

/// <summary>
/// A game Binder knows, from its JSON file (<c>Titles\*.json</c> beside the module, or the user's own in
/// <c>%APPDATA%\DaisysApp\Binder Titles\</c>): how to find its files, which format they're in, and what its actions are
/// called and do. See README.md for the fields.
/// </summary>
public sealed class TitleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TitleKind Kind { get; set; }
    /// <summary>The code that reads and writes its files: "fso", "elite", "ac" or "sheet" (Binder keeps them).</summary>
    public string Format { get; set; } = "";
    /// <summary>What the folder to pick is (the Browse window's title).</summary>
    public string? FolderHint { get; set; }
    /// <summary>Where the folder usually is, tried in order (<see cref="Paths.Expand"/>).</summary>
    public List<string> Find { get; set; } = new();
    /// <summary>Its programs' names, without .exe: "running" while one is.</summary>
    public List<string> Programs { get; set; } = new();
    /// <summary>The format's own settings (FSO's mod id, Elite's defaults file…).</summary>
    public Dictionary<string, string> Options { get; set; } = new();
    /// <summary>Groups by a pattern on the action's id, first match wins (for formats whose files have none).</summary>
    public List<GroupRule> Groups { get; set; } = new();
    /// <summary>The actions: all of them (Assetto Corsa), or names, groups and roles for ones the game's files list.</summary>
    public List<ActionDef> Actions { get; set; } = new();

    [JsonIgnore] public string File { get; set; } = "";
    [JsonIgnore] public IBindFormat? Reader => FormatList.For(Format);

    public string Option(string key, string fallback = "") => Options.TryGetValue(key, out var v) ? v : fallback;

    public ActionDef? Action(string id) => Actions.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public string GroupFor(string id, string fallback) => RuleFor(id)?.Name ?? fallback;

    /// <summary>When an action works, from its group's rule (Elite's ship, SRV, on foot…); "" for always.</summary>
    public string ContextFor(string id) => RuleFor(id)?.Context ?? "";

    private GroupRule? RuleFor(string id) => Groups.FirstOrDefault(g => Regex.IsMatch(id, g.Match, RegexOptions.IgnoreCase));
}

public sealed class GroupRule
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    /// <summary>When its actions work: bindings conflict only within one context.</summary>
    public string? Context { get; set; }
}

/// <summary>An action in a title's JSON.</summary>
public sealed class ActionDef
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public string? Group { get; set; }
    public string? Role { get; set; }
    public bool? Axis { get; set; }
    public bool? Centred { get; set; }
    /// <summary>Its default bindings, as keys are written ("Up", "Shift+T").</summary>
    public List<string> Keys { get; set; } = new();
    /// <summary>Its slots where the format lets a title say (Assetto Corsa: "axis", "button", "key").</summary>
    public List<string> Slots { get; set; } = new();
    /// <summary>Not listed (the format keeps it as it is).</summary>
    public bool Hidden { get; set; }
}

/// <summary>The titles' JSON files, read once.</summary>
public static class Titles
{
    private static List<TitleDef>? all;

    public static string UserFolder => Path.Combine(AppPaths.SettingsFolder, "Binder Titles");
    public static string BuiltInFolder => Path.Combine(Path.GetDirectoryName(typeof(Titles).Assembly.Location) ?? "", "Titles");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Every title, the user's own first (one with a built-in one's id replaces it).</summary>
    public static IReadOnlyList<TitleDef> All => all ??= Read();

    public static TitleDef? Get(string id) => All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static void Reload() => all = null;

    private static List<TitleDef> Read()
    {
        var found = new Dictionary<string, TitleDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { BuiltInFolder, UserFolder })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<TitleDef>(System.IO.File.ReadAllText(file), Options);
                    if (t == null || t.Id.Length == 0 || t.Name.Length == 0) { Logging.Log.Here.Warn($"Binder: {file} has no id or name"); continue; }
                    if (t.Reader == null || t.Format.Equals(TemplateFormat.FormatId, StringComparison.OrdinalIgnoreCase)) { Logging.Log.Here.Warn($"Binder: {file}: no format called “{t.Format}”"); continue; }
                    t.File = file;
                    found[t.Id] = t;
                }
                catch (Exception ex) { Logging.Log.Here.Warn($"Binder: {file} couldn't be read: {ex.Message}"); }
            }
        }
        Logging.Log.Here.Info($"Binder: titles {string.Join(", ", found.Values.Select(t => t.Name))}");
        return found.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>Paths in a title's JSON: %VARIABLES%, <c>documents:</c> (the Documents folder, wherever OneDrive keeps it) and
/// <c>steam:</c> (a folder in any of Steam's libraries).</summary>
public static class Paths
{
    /// <summary>Every place the path can mean (a steam: one, one per library), those that exist first.</summary>
    public static IEnumerable<string> Expand(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith("documents:", StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), path[10..].TrimStart('\\', '/'));
        else if (path.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var lib in SteamLibraries())
                yield return Path.Combine(lib, "steamapps", "common", path[6..].TrimStart('\\', '/'));
        }
        else yield return path;
    }

    /// <summary>The first place that exists, as a folder or file.</summary>
    public static string? First(string path) => Expand(path).FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));

    private static List<string>? libraries;

    /// <summary>Steam's libraries, from its libraryfolders.vdf.</summary>
    public static List<string> SteamLibraries()
    {
        if (libraries != null) return libraries;
        var found = new List<string>();
        string? steam = null;
        try { steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; } catch { }
        steam ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        steam = steam.Replace('/', '\\');
        found.Add(steam);
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    found.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        return libraries = found.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
    }

    /// <summary>Copies a file to <c>file.daisysapp.bak</c> before Binder first changes it (once: an older copy stays).</summary>
    public static void BackupOnce(string file)
    {
        try
        {
            var bak = file + ".daisysapp.bak";
            if (File.Exists(file) && !File.Exists(bak)) File.Copy(file, bak);
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Binder: couldn't back up {file}: {ex.Message}"); }
    }

    public static void WriteAtomically(string path, string content, System.Text.Encoding encoding)
    {
        BackupOnce(path);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, encoding);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>A title Binder has been set up for: the game, its folder and the format's own choices. In Binder.json.</summary>
public sealed class TitleSetup
{
    /// <summary>The title's id (<see cref="TitleDef.Id"/>).</summary>
    public string Title { get; set; } = "";
    public string Folder { get; set; } = "";
    /// <summary>The format's choices (FSO's preset name and Use Mouse).</summary>
    public Dictionary<string, string> Options { get; set; } = new();

    public string Option(string key, string fallback = "") => Options.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>A line of what was looked for in a title's folder; one not <paramref name="Needed"/> (the game's defaults) can be
/// missing.</summary>
public sealed record Check(string Text, bool Ok, string? Path = null, bool Needed = true);

/// <summary>The input a title reads controllers through, so they're numbered as the game numbers them.</summary>
public enum InputKind { DirectInput, Sdl }

/// <summary>A title's actions and bindings, as read from its files; the format keeps what else it needs in <see cref="State"/>.</summary>
public sealed class TitleData
{
    public required TitleDef Def { get; init; }
    public required TitleSetup Setup { get; init; }
    public List<BinderAction> Actions { get; init; } = new();
    /// <summary>Each action's bindings, a slot each.</summary>
    public Dictionary<string, Bind[]> Binds { get; init; } = new();
    /// <summary>A line about it ("Fate of the Galaxy 1.2 · FSO 26.0.1").</summary>
    public string Summary { get; set; } = "";
    public object? State { get; set; }

    public IBindFormat Format => Def.Reader!;

    public Bind[] Get(BinderAction a) =>
        Binds.TryGetValue(a.Id, out var b) && b.Length == Format.Slots(a) ? b : Binds[a.Id] = Enumerable.Range(0, Format.Slots(a)).Select(i => i < a.Defaults.Length ? a.Defaults[i] : Bind.None).ToArray();
}

/// <summary>A game's files: finding them, reading the actions and bindings, writing them back, and what it can take.</summary>
public interface IBindFormat
{
    string Id { get; }
    InputKind Input { get; }

    /// <summary>Whether the title is set up only with its folder found (false: Binder keeps the bindings itself).</summary>
    bool NeedsFolder => true;

    /// <summary>What's looked for in the folder, each found or not; the folder is usable when all are.</summary>
    IReadOnlyList<Check> Check(TitleDef def, string folder);

    /// <summary>Reads the actions and bindings (throws <see cref="BinderException"/> when it can't).</summary>
    TitleData Load(TitleDef def, TitleSetup setup);

    /// <summary>Writes the bindings back into the game's files.</summary>
    void Save(TitleData data, Input.IInputSource input);

    /// <summary>How many bindings an action has, and each one's name.</summary>
    int Slots(BinderAction a);
    string SlotName(BinderAction a, int slot);

    /// <summary>Whether Binder can turn the axis round (its Invert box).</summary>
    bool CanInvert(BinderAction a) => a.IsAxis;

    /// <summary>Why the game can't take this binding in this slot, or null if it can.</summary>
    string? Refuse(TitleData data, BinderAction a, int slot, Bind b);

    /// <summary>The format's own lines under the title's details (FSO's preset name); null if none. <paramref name="changed"/>:
    /// the bindings are to be written; <paramref name="reload"/>: read again (another preset picked).</summary>
    FrameworkElement? Details(TitleData data, Action changed, Action reload) => null;

    /// <summary>What's shown beside a device in the Devices block (FSO's Joystick 0–3); null if nothing.</summary>
    FrameworkElement? DeviceExtra(TitleData data, Input.LiveDevice device, Action changed) => null;

    /// <summary>The DLL SDL is read through, for <see cref="InputKind.Sdl"/>.</summary>
    string? SdlPath(TitleData data) => null;
}
