using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using DaisysApp.Logging;

namespace DaisysApp.Shell;

/// <summary>
/// Finds the applets: every module under modules\ next to the program (one folder each), loaded here. A module's
/// main assembly is the DLL named after its folder (or DaisysApp.&lt;folder&gt;.dll); a folder with neither has all its
/// DLLs looked through. Each class in it that implements <see cref="IApplet"/> and carries an <see cref="AppletAttribute"/>
/// is an applet. Assemblies a module needs that the app doesn't have (e.g. Mini Mirror's Direct3D wrapper) are found in
/// the module folders when they're first asked for.
///
/// The tabs are built from what's there, so the same program is Daisy's App with every module and a one-tool app with
/// one: applets that name a <see cref="AppletAttribute.Group"/> share a tab, as sub-tabs, when two or more of the group
/// are installed (joining a module's own <see cref="AppletGroup"/> with that id, or making one from the group's
/// <see cref="TabGroupAttribute"/>); one on its own is a tab of its own, without sub-tabs.
/// </summary>
public static class AppletCatalog
{
    /// <summary>
    /// One tab: an applet (<see cref="Type"/>), a module's group with applets from other modules joining it
    /// (<see cref="Tools"/>), or a group made only of those (no Type).
    /// </summary>
    public sealed record Entry(Type? Type, AppletAttribute Meta, string Folder)
    {
        /// <summary>Applets from other modules that are sub-tabs of this one (its <see cref="AppletAttribute.Group"/> members).</summary>
        public IReadOnlyList<Entry> Tools { get; init; } = [];

        public IApplet Create()
        {
            var applet = Type != null ? (IApplet)Activator.CreateInstance(Type)! : new ToolGroup(Meta);
            if (applet is AppletGroup group)
                foreach (var t in Tools) group.AddTool(t.Meta, t.Create, Log.For(t.Folder));
            return applet;
        }
    }

    /// <summary>A tab made only of applets from several modules (Gaming: Joy2Key and ForzaWheel).</summary>
    private sealed class ToolGroup(AppletAttribute meta) : AppletGroup(meta);

    /// <summary>Modules that couldn't be loaded: (folder name, why).</summary>
    public static List<(string Module, string Error)> Failed { get; } = new();

    private static readonly Lazy<IReadOnlyList<Entry>> all = new(Load);

    /// <summary>All applets, in tab order.</summary>
    public static IReadOnlyList<Entry> All => all.Value;

    private static IReadOnlyList<Entry> Load()
    {
        var entries = new List<Entry>();
        var tabGroups = new Dictionary<string, TabGroupAttribute>(StringComparer.OrdinalIgnoreCase);
        string root = Loc.ModulesFolder;
        if (!Directory.Exists(root)) return entries;
        var folders = Directory.GetDirectories(root);
        AssemblyLoadContext.Default.Resolving += (context, name) => FindDependency(folders, context, name);

        foreach (var folder in folders)
        {
            string module = Path.GetFileName(folder);
            try
            {
                foreach (var dll in MainAssemblies(folder))
                {
                    var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                    string fileVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
                    Log.For(module).Info($"Module {module} loaded: {asm.GetName().Name} {fileVersion} from {dll}");
                    Log.App.Info($"Module {module} loaded ({fileVersion})");
                    foreach (var g in asm.GetCustomAttributes<TabGroupAttribute>()) tabGroups.TryAdd(g.Id, g);
                    foreach (var t in Types(asm))
                    {
                        if (t is not { IsClass: true, IsAbstract: false } || !typeof(IApplet).IsAssignableFrom(t)) continue;
                        if (t.GetCustomAttribute<AppletAttribute>() is { } meta) entries.Add(new Entry(t, meta, module));
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write($"Loading module {module}", ex);
                Log.For(module).Error($"Module {module} couldn't be loaded from {folder}", ex);
                Failed.Add((module, (ex.InnerException ?? ex).Message));
            }
        }

        // the same applet Id twice (e.g. an old copy of a module in a second folder): keep the first
        entries = entries.GroupBy(e => e.Meta.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        return Tabs(entries, tabGroups)
            .OrderBy(e => e.Meta.Order).ThenBy(e => e.Meta.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The tabs: applets that share a group go together, when there are two or more of them.</summary>
    private static List<Entry> Tabs(List<Entry> applets, Dictionary<string, TabGroupAttribute> tabGroups)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var tabs = applets.Where(e => e.Meta.Group == null).ToList();
        foreach (var members in applets.Where(e => e.Meta.Group != null).GroupBy(e => e.Meta.Group!, cmp))
        {
            var tools = members.OrderBy(e => e.Meta.GroupOrder).ToList();
            int host = tabs.FindIndex(t => cmp.Equals(t.Meta.Id, members.Key));
            if (host >= 0)
            {
                // a module's own group (Video): they join it
                tabs[host] = tabs[host] with { Tools = tabs[host].Tools.Concat(tools).ToList() };
                Log.App.Info($"{string.Join(", ", tools.Select(t => t.Meta.Id))} {(tools.Count == 1 ? "joins" : "join")} the {members.Key} tab");
            }
            else if (tools.Count >= 2)
            {
                var meta = tabGroups.TryGetValue(members.Key, out var g) ? g.ToApplet()
                    : new AppletAttribute(members.Key, members.Key, tools[0].Meta.Icon) { Order = tools[0].Meta.Order };
                tabs.Add(new Entry(null, meta, tools[0].Folder) { Tools = tools });
                Log.App.Info($"{string.Join(", ", tools.Select(t => t.Meta.Id))} share the {meta.Id} tab");
            }
            else tabs.AddRange(tools); // on its own: a tab of its own, no sub-tabs
        }
        return tabs;
    }

    private static IEnumerable<string> MainAssemblies(string folder)
    {
        string name = Path.GetFileName(folder);
        foreach (var candidate in new[] { name + ".dll", "DaisysApp." + name + ".dll" })
        {
            string path = Path.Combine(folder, candidate);
            if (File.Exists(path)) return new[] { path };
        }
        return Directory.GetFiles(folder, "*.dll");
    }

    private static IEnumerable<Type> Types(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
    }

    private static Assembly? FindDependency(string[] folders, AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var folder in folders)
        {
            string path = Path.Combine(folder, name.Name + ".dll");
            if (!File.Exists(path)) continue;
            try
            {
                var asm = context.LoadFromAssemblyPath(path);
                Log.App.Debug($"Loaded {name} for a module from {path}");
                return asm;
            }
            catch (Exception ex) { ErrorLog.Write($"Loading {name} for a module from {path}", ex); } // e.g. another version is already loaded
        }
        return null;
    }

    /// <summary>
    /// Saves which applets are on (Settings → General and the setup wizard). Ones that are on by default are remembered
    /// when they're off, the others when they're on; ids of modules that aren't installed now are kept, for when they're
    /// back. Takes effect at the next start.
    /// </summary>
    public static void SaveOnOff(Settings.AppSettings settings, IEnumerable<(AppletAttribute Meta, bool On)> applets)
    {
        var list = applets.ToList();
        var shown = list.Select(a => a.Meta.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.DisabledApplets = settings.DisabledApplets.Where(id => !shown.Contains(id))
            .Concat(list.Where(a => a.Meta.OnByDefault && !a.On).Select(a => a.Meta.Id)).ToList();
        settings.EnabledApplets = settings.EnabledApplets.Where(id => !shown.Contains(id))
            .Concat(list.Where(a => !a.Meta.OnByDefault && a.On).Select(a => a.Meta.Id)).ToList();
        settings.Save();
    }

    /// <summary>Every applet in the order the user put the tabs in (one not placed yet goes after those).</summary>
    public static List<Entry> InTabOrder(Settings.AppSettings settings) =>
        All.OrderBy(e => settings.TabOrder.FindIndex(id => id.Equals(e.Meta.Id, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : int.MaxValue).ToList();

    /// <summary>Whether an applet is switched on: on unless switched off, or for one that's off by default, if switched on.</summary>
    public static bool IsOn(AppletAttribute meta, Settings.AppSettings settings) => meta.OnByDefault
        ? !settings.DisabledApplets.Contains(meta.Id, StringComparer.OrdinalIgnoreCase)
        : settings.EnabledApplets.Contains(meta.Id, StringComparer.OrdinalIgnoreCase);
}
