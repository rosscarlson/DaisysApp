using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Vortice.DXGI;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>A program kept on a graphics card (since 3.34.0): which, and where its program file is found.</summary>
public sealed class GpuRule
{
    /// <summary>The program file last seen (its full path).</summary>
    public string Path { get; set; } = "";
    /// <summary>Windows' choice: 1 the power saving card (an iGPU), 2 the high performance one.</summary>
    public int Preference { get; set; }
    /// <summary>The card's name when it was picked.</summary>
    public string Gpu { get; set; } = "";
}

/// <summary>
/// Which graphics card a program runs on (the Processes list's Restart on…): Windows' own setting (Settings → Display →
/// Graphics), a value per program file in <c>HKCU\Software\Microsoft\DirectX\UserGpuPreferences</c>, which a program
/// reads as it starts. A program that updates into a new folder each time (Discord's <c>app-1.0.9…</c>) would lose it,
/// so the choice is kept by the program's name (<see cref="PerformanceSettings.GpuRules"/>) and written again for each
/// copy of it in the folder above a numbered folder: at the start, and as soon as an update puts a new one there.
/// </summary>
public sealed class GpuPrefs : IDisposable
{
    private const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private readonly PerformanceSettings settings;
    private readonly List<FileSystemWatcher> watchers = new();
    private (long Saving, long Fast)? cards;

    public GpuPrefs(PerformanceSettings settings) => this.settings = settings;

    /// <summary>The power saving and the high performance cards' LUIDs, as Windows ranks them (the same one twice with one card).</summary>
    public (long Saving, long Fast) Cards
    {
        get
        {
            if (cards is { } c) return c;
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
                cards = (First(factory, GpuPreference.MinimumPower), First(factory, GpuPreference.HighPerformance));
            }
            catch { cards = (0, 0); }
            return cards.Value;

            static long First(IDXGIFactory6 f, GpuPreference p)
            {
                for (uint i = 0; f.EnumAdapterByGpuPreference(i, p, out IDXGIAdapter1? a).Success && a != null; i++)
                    using (a)
                    {
                        var d = a.Description1;
                        if ((d.Flags & AdapterFlags.Software) == 0) return ((long)d.Luid.HighPart << 32) | d.Luid.LowPart;
                    }
                return 0;
            }
        }
    }

    public void Start()
    {
        foreach (var (name, rule) in settings.GpuRules) Watch(name, rule);
    }

    /// <summary>The rule for a program, by its file's name.</summary>
    public GpuRule? RuleFor(string path) => settings.GpuRules.GetValueOrDefault(System.IO.Path.GetFileName(path));

    /// <summary>Keeps <paramref name="path"/>'s program (and its copies after updates) on the card of <paramref name="preference"/>.</summary>
    public void Choose(string path, int preference, string gpu)
    {
        string name = System.IO.Path.GetFileName(path);
        var rule = new GpuRule { Path = path, Preference = preference, Gpu = gpu };
        settings.GpuRules[name] = rule;
        settings.Save();
        Write(path, preference);
        Watch(name, rule);
        Logging.Log.Here.Info($"Performance: {name} kept on {gpu} (GpuPreference={preference}, {path})");
    }

    /// <summary>Windows picks again: the rule and its values go.</summary>
    public void Forget(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (!settings.GpuRules.Remove(name, out var rule)) return;
        settings.Save();
        foreach (var w in watchers.Where(w => w.Filter.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList()) { w.Dispose(); watchers.Remove(w); }
        foreach (var file in Copies(name, rule).Append(path).Distinct(StringComparer.OrdinalIgnoreCase)) Write(file, 0);
        Logging.Log.Here.Info($"Performance: {name} no longer kept on a graphics card");
    }

    /// <summary>The folder a program's updates land in: the one above a numbered folder ("app-1.0.9205"), else its own.</summary>
    private static string Root(string path, out bool deep)
    {
        string folder = System.IO.Path.GetDirectoryName(path) ?? "";
        deep = Regex.IsMatch(System.IO.Path.GetFileName(folder), @"\d+\.\d+") && Directory.GetParent(folder) is not null;
        return deep ? Directory.GetParent(folder)!.FullName : folder;
    }

    private static IEnumerable<string> Copies(string name, GpuRule rule)
    {
        try
        {
            string root = Root(rule.Path, out bool deep);
            if (!Directory.Exists(root)) return [];
            var found = Directory.EnumerateFiles(root, name).ToList();
            if (deep) foreach (var sub in Directory.EnumerateDirectories(root)) found.AddRange(Directory.EnumerateFiles(sub, name));
            return found;
        }
        catch { return []; }
    }

    /// <summary>Writes the rule for every copy there is, and watches for new ones.</summary>
    private void Watch(string name, GpuRule rule)
    {
        foreach (var file in Copies(name, rule)) Write(file, rule.Preference);
        if (watchers.Any(w => w.Filter.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            string root = Root(rule.Path, out bool deep);
            if (!Directory.Exists(root)) return;
            var w = new FileSystemWatcher(root, name) { IncludeSubdirectories = deep, NotifyFilter = NotifyFilters.FileName };
            w.Created += (_, e) => { if (settings.GpuRules.TryGetValue(name, out var r)) Write(e.FullPath, r.Preference); };
            w.Renamed += (_, e) => { if (settings.GpuRules.TryGetValue(name, out var r)) Write(e.FullPath, r.Preference); };
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"Performance: watching for {name}: {ex.Message}"); }
    }

    /// <summary>Sets a program file's GpuPreference (0 takes it out), keeping the value's other settings (HDR and the like).</summary>
    private static void Write(string path, int preference)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            var parts = ((key.GetValue(path) as string) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)).ToList();
            if (preference > 0) parts.Insert(0, $"GpuPreference={preference}");
            string value = string.Concat(parts.Select(p => p + ";"));
            if ((key.GetValue(path) as string) == value) return;
            if (value.Length == 0) key.DeleteValue(path, false);
            else key.SetValue(path, value);
            Logging.Log.Here.Info($"Performance: GPU preference of {path}: {(value.Length == 0 ? "Windows picks" : value)}");
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Performance: the GPU preference of {path}", ex); }
    }

    public void Dispose()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
    }
}
