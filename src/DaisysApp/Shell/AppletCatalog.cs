using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using DaisysApp.Logging;

namespace DaisysApp.Shell;

/// <summary>
/// Finds the applets: every module under modules\ next to DaisysApp.exe (one folder each), loaded here. A module's
/// main assembly is the DLL named after its folder (or DaisysApp.&lt;folder&gt;.dll); a folder with neither has all its
/// DLLs looked through. Each class in it that implements <see cref="IApplet"/> and carries an <see cref="AppletAttribute"/>
/// is an applet. Assemblies a module needs that the app doesn't have (e.g. Mini Mirror's Direct3D wrapper) are found in
/// the module folders when they're first asked for.
/// </summary>
public static class AppletCatalog
{
    public sealed record Entry(Type Type, AppletAttribute Meta, string Folder)
    {
        public IApplet Create() => (IApplet)Activator.CreateInstance(Type)!;
    }

    /// <summary>Modules that couldn't be loaded: (folder name, why).</summary>
    public static List<(string Module, string Error)> Failed { get; } = new();

    private static readonly Lazy<IReadOnlyList<Entry>> all = new(Load);

    /// <summary>All applets, in tab order.</summary>
    public static IReadOnlyList<Entry> All => all.Value;

    private static IReadOnlyList<Entry> Load()
    {
        var entries = new List<Entry>();
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
        return entries
            .GroupBy(e => e.Meta.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(e => e.Meta.Order).ThenBy(e => e.Meta.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
