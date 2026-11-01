using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.Binder.Input;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Binder.Formats;

/// <summary>
/// "sheet": a game whose own files Binder can't write (kept in Xbox's cloud, in a binary file, or not yet known: MSFS 2024,
/// Forza, iRacing…). Its actions and defaults are all in its JSON; Binder keeps the bindings itself
/// (<c>%APPDATA%\DaisysApp\Binder Sheets\&lt;id&gt;.json</c>) as a list to set in the game, copied in and out like any title's.
/// The game's folder is optional (only to see it's installed); its options' "file" names where the game keeps its own.
/// </summary>
public sealed class SheetFormat : IBindFormat
{
    public string Id => "sheet";
    public InputKind Input => InputKind.DirectInput;
    public bool NeedsFolder => false;

    public static string Folder => Path.Combine(AppPaths.SettingsFolder, "Binder Sheets");
    private static string FileFor(TitleDef def) => Path.Combine(Folder, def.Id + ".json");

    public IReadOnlyList<Check> Check(TitleDef def, string folder)
    {
        var list = new List<Check>();
        if (folder.Length == 0) list.Add(new(T("No folder (it's only to see it's installed)"), false, Needed: false));
        else if (!Directory.Exists(folder)) list.Add(new(F("The folder {0} doesn't exist", folder), false, Needed: false));
        else
        {
            list.Add(new(T("The game's folder"), true, folder));
            var own = def.Option("file");
            if (own.Length > 0 && !Import.Can(def)) list.Add(new(F("Its own bindings: {0}", own), File.Exists(Path.Combine(folder, own)), Path.Combine(folder, own), Needed: false));
        }
        if (Import.Can(def))
        {
            var found = Import.FindFile(def, folder);
            list.Add(found != null
                ? new(F("Its bindings: {0}", Path.GetFileName(found)), true, found)
                : new(F("Its bindings ({0}): not found", Path.GetFileName(def.Option("importFile"))), false, def.Option("importFile"), Needed: false));
        }
        return list;
    }

    public TitleData Load(TitleDef def, TitleSetup setup)
    {
        var data = new TitleData { Def = def, Setup = setup, Summary = def.Name };
        int order = 0;
        foreach (var a in def.Actions.Where(a => !a.Hidden))
        {
            var defaults = a.Keys.Select(Keys.ParseDefault).Where(b => b != null).Select(b => b!).Take(TemplateFormat.SlotCount).ToArray();
            if (a.Keys.Count > 0 && defaults.Length == 0) Logging.Log.Here.Warn($"Binder: {def.Name}: {a.Id}'s defaults {string.Join(", ", a.Keys)} aren't keys Binder knows");
            data.Actions.Add(new BinderAction
            {
                Id = a.Id, Label = a.Label ?? a.Id, Group = a.Group ?? def.GroupFor(a.Id, T("Other")), IsAxis = a.Axis ?? false, Centred = a.Centred ?? true,
                Role = a.Role, Defaults = defaults, Context = def.ContextFor(a.Id), Order = order++,
            });
        }
        var file = FileFor(def);
        if (File.Exists(file))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<Bind>>>(File.ReadAllText(file), Templates.Json) ?? new();
            // (the game's own actions an import brought in, that its JSON doesn't list)
            foreach (var (id, b) in saved.Where(p => p.Key.StartsWith(Import.ExtraPrefix, StringComparison.Ordinal) && data.Actions.All(a => a.Id != p.Key)))
                data.Actions.Add(Import.Extra(id[Import.ExtraPrefix.Length..], b.Any(x => x.IsAxis), order++));
            foreach (var a in data.Actions)
                if (saved.TryGetValue(a.Id, out var b))
                    data.Binds[a.Id] = Enumerable.Range(0, TemplateFormat.SlotCount).Select(i => i < b.Count ? b[i] : Bind.None).ToArray();
        }
        return data;
    }

    public void Save(TitleData data, IInputSource input)
    {
        Directory.CreateDirectory(Folder);
        var changed = data.Actions.Where(a => !data.Get(a).SequenceEqual(Defaults(data, a))).ToDictionary(a => a.Id, a => data.Get(a).ToList());
        Paths.WriteAtomically(FileFor(data.Def), JsonSerializer.Serialize(changed, new JsonSerializerOptions(Templates.Json) { WriteIndented = true }), new UTF8Encoding(false));
    }

    private Bind[] Defaults(TitleData data, BinderAction a) =>
        Enumerable.Range(0, Slots(a)).Select(i => i < a.Defaults.Length ? a.Defaults[i] : Bind.None).ToArray();

    public int Slots(BinderAction a) => TemplateFormat.SlotCount;
    public string SlotName(BinderAction a, int slot) => slot == 0 ? T("First") : T("Second");

    public string? Refuse(TitleData data, BinderAction a, int slot, Bind b) => TemplateFormat.RefuseAny(a, b);

    /// <summary>Its options' "deviceNumber" ("js{0}" for Star Citizen: from 1), else 1, 2, 3.</summary>
    public string NumberText(TitleData data, int n) => string.Format(data.Def.Option("deviceNumber", "{0}"), n + 1);

    public FrameworkElement? Details(TitleData data, Action changed, Action reload)
    {
        var note = Ui.Note(T("Binder keeps these: set them the same in the game"));
        var copy = Ui.Button(T("Copy List"), () =>
        {
            try { Clipboard.SetText(List(data)); }
            catch (Exception ex) { Logging.Log.Here.Warn($"Binder: copying {data.Def.Name}'s list: {ex.Message}"); }
        }, T("The bindings as text, a group at a time"));
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        return new StackPanel { Children = { note, copy } };
    }

    /// <summary>The bound actions as text, a group at a time ("  Pitch: VKB Gladiator Y · Mouse Y").</summary>
    public static string List(TitleData data)
    {
        var names = GameControllers.List();
        string? Name(DeviceId d) => names.Where(c => c.Vid == d.Vid && c.Pid == d.Pid).Skip(d.Nth).FirstOrDefault()?.Name;
        var sb = new StringBuilder().AppendLine(data.Def.Name);
        foreach (var g in data.Actions.GroupBy(a => a.Group))
        {
            var lines = g.Select(a => (a, binds: data.Get(a).Where(b => !b.IsNone).ToList())).Where(x => x.binds.Count > 0).ToList();
            if (lines.Count == 0) continue;
            sb.AppendLine().AppendLine(g.Key);
            foreach (var (a, binds) in lines) sb.AppendLine($"  {a.Label}: {string.Join(" · ", binds.Select(b => Describe.Text(b, Name)))}");
        }
        return sb.ToString();
    }
}
