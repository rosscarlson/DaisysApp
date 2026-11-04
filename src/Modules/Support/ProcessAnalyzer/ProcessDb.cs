using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>How risky a program is: Safe … Dangerous.</summary>
public enum Threat { Safe, Low, Medium, High, Dangerous }

/// <summary>Whether a program needs to run: Windows' own (Required) … one that can go (Unnecessary).</summary>
public enum Necessity { Required, Recommended, Optional, Unnecessary, Unknown }

/// <summary>How much of the PC a program is expected to use (what's "too much" for it).</summary>
public enum Resource { Light, Moderate, Heavy }

/// <summary>
/// What's known about one program, by its name (the file's, lower case, without .exe: what Windows and Hardware →
/// Performance's log call it). The same object goes to Daisy's Server and into exports (camelCase JSON).
/// </summary>
public sealed class KnownProcess
{
    public string Name { get; set; } = "";
    /// <summary>What it is, in a few words ("Google Chrome").</summary>
    public string Title { get; set; } = "";
    /// <summary>Who makes it: what its signature or file says, matched loosely (contains); alternatives split by "|".</summary>
    public string Publisher { get; set; } = "";
    public string Category { get; set; } = "Unknown";
    public Threat Threat { get; set; } = Threat.Medium;
    public Necessity Necessity { get; set; } = Necessity.Unknown;
    public Resource Resource { get; set; } = Resource.Moderate;
    public string Description { get; set; } = "";
    /// <summary>Folders it should run from (with %SystemRoot%-style variables, or a part such as "\Steam\"); empty: anywhere.</summary>
    public List<string> Paths { get; set; } = new();
    /// <summary>seed (the list it comes with), auto (worked out here), user, import or server.</summary>
    public string Source { get; set; } = "auto";
    /// <summary>A person (or the list it comes with) classified it: beats an automatic one in a merge.</summary>
    public bool Reviewed { get; set; }
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    /// <summary>The PC that last changed it.</summary>
    public string By { get; set; } = "";
    /// <summary>The server's version when it last changed there (0: never on a server).</summary>
    public long Rev { get; set; }
    /// <summary>VirusTotal's verdict on the file (the SHA-256 it's for): detections of engines, when.</summary>
    public string? Sha256 { get; set; }
    public int? VtMalicious { get; set; }
    public int? VtEngines { get; set; }
    public DateTime? VtChecked { get; set; }
    /// <summary>The server's average across its PCs (sent by it, not kept as an edit): CPU %, memory MB, PCs.</summary>
    public double? FleetCpu { get; set; }
    public double? FleetMemoryMB { get; set; }
    public int? FleetPcs { get; set; }

    public KnownProcess Copy()
    {
        var c = (KnownProcess)MemberwiseClone();
        c.Paths = new List<string>(Paths);
        return c;
    }

    /// <summary>The key: lower case, no ".exe".</summary>
    public static string Key(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe") ? name[..^4] : name;
    }

    /// <summary>Whether <paramref name="other"/> should replace this one in a merge of two edits: reviewed beats
    /// automatic, then the newer one.</summary>
    public bool LosesTo(KnownProcess other)
    {
        if (other.Reviewed != Reviewed) return other.Reviewed;
        return other.Updated > Updated;
    }
}

/// <summary>Per-PC figures for one program, from this module's own looks (not exported or sent as an edit).</summary>
public sealed class Observed
{
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public long Samples { get; set; }
    public double AvgCpu { get; set; }
    public double PeakCpu { get; set; }
    public double AvgMemoryMB { get; set; }
    public double PeakMemoryMB { get; set; }
    /// <summary>The paths it ran from here.</summary>
    public List<string> Paths { get; set; } = new();

    public void Add(double cpu, double memoryMB, DateTime now)
    {
        if (Samples == 0) FirstSeen = now;
        LastSeen = now;
        Samples++;
        // a running mean, weighted so the last few days count most once it has plenty of samples
        double w = 1.0 / Math.Min(Samples, 5000);
        AvgCpu += (cpu - AvgCpu) * w;
        AvgMemoryMB += (memoryMB - AvgMemoryMB) * w;
        PeakCpu = Math.Max(PeakCpu, cpu);
        PeakMemoryMB = Math.Max(PeakMemoryMB, memoryMB);
    }
}

/// <summary>
/// The process database: %LOCALAPPDATA%\DaisysApp\ProcessAnalyzer\processes.json, every program known (the list it
/// comes with, <c>known.json</c>, those classified here, imported or from Daisy's Server), the server's version it's in
/// step with, and what's been seen on this PC. One instance (<see cref="Db"/>); thread-safe; saved soon after a change.
/// </summary>
public sealed class ProcessDb
{
    public const string Format = "DaisysApp.Processes";
    public static string Folder { get; } = Path.Combine(Path.GetDirectoryName(AppPaths.LogFolder)!, "ProcessAnalyzer");
    private static string FilePath => Path.Combine(Folder, "processes.json");

    // (made on first use: the static fields below, the JSON options among them, are set by then)
    private static readonly Lazy<ProcessDb> db = new(Load);
    public static ProcessDb Db => db.Value;

    private readonly object gate = new();
    private Dictionary<string, KnownProcess> entries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Observed> observed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly Debounce saveSoon;
    private DateTime lastObserveSave;

    /// <summary>The server's database version this one has every change up to (0: never synced).</summary>
    public long Version { get; private set; }
    /// <summary>The server it was synced with (a different one starts again from 0).</summary>
    public string Server { get; private set; } = "";

    /// <summary>Something changed (any thread).</summary>
    public event Action? Changed;

    private ProcessDb() => saveSoon = new Debounce(1500, Save);

    public KnownProcess? Get(string name)
    {
        lock (gate) return entries.TryGetValue(KnownProcess.Key(name), out var e) ? e : null;
    }

    public List<KnownProcess> All()
    {
        lock (gate) return entries.Values.OrderBy(e => e.Name).ToList();
    }

    public int Count { get { lock (gate) return entries.Count; } }

    public Observed? SeenOf(string name)
    {
        lock (gate) return observed.TryGetValue(KnownProcess.Key(name), out var o) ? o : null;
    }

    /// <summary>Adds this PC's figures for the programs running now (name → CPU %, memory MB, path).</summary>
    public void Observe(IEnumerable<(string Name, double Cpu, double MemoryMB, string? Path)> now)
    {
        var t = DateTime.Now;
        lock (gate)
        {
            foreach (var (name, cpu, mem, path) in now)
            {
                string key = KnownProcess.Key(name);
                if (!observed.TryGetValue(key, out var o)) observed[key] = o = new Observed();
                o.Add(cpu, mem, t);
                if (path != null && !o.Paths.Contains(path, StringComparer.OrdinalIgnoreCase) && o.Paths.Count < 8) o.Paths.Add(path);
            }
        }
        // (the figures alone are saved at most once a minute: the tab looks every 2 seconds)
        if (t - lastObserveSave > TimeSpan.FromMinutes(1)) { lastObserveSave = t; saveSoon.Poke(); }
    }

    /// <summary>Stores an entry made or edited here (marked to send to the server).</summary>
    public void Set(KnownProcess e)
    {
        e.Name = KnownProcess.Key(e.Name);
        e.Updated = DateTime.UtcNow;
        e.By = Environment.MachineName;
        lock (gate)
        {
            entries[e.Name] = e;
            dirty.Add(e.Name);
        }
        Changed?.Invoke();
        saveSoon.Poke();
    }

    /// <summary>Adds an automatic classification for a program not known yet (no change if one's there by now).</summary>
    public bool AddAuto(KnownProcess e)
    {
        e.Name = KnownProcess.Key(e.Name);
        lock (gate)
        {
            if (entries.ContainsKey(e.Name)) return false;
            e.By = Environment.MachineName;
            entries[e.Name] = e;
            dirty.Add(e.Name);
        }
        Changed?.Invoke();
        saveSoon.Poke();
        return true;
    }

    /// <summary>Puts back the automatic one (a user's classification forgotten here; the server's stays there).</summary>
    public void Remove(string name)
    {
        lock (gate) { entries.Remove(KnownProcess.Key(name)); dirty.Remove(KnownProcess.Key(name)); }
        Changed?.Invoke();
        saveSoon.Poke();
    }

    /// <summary>Merges entries from a file or the server: (added, replaced). <paramref name="fromServer"/>: they're the
    /// server's (not sent back); <paramref name="serverFirst"/>: a new client's first sync, where the server wins.</summary>
    public (int Added, int Replaced) Merge(IEnumerable<KnownProcess> incoming, string source, bool fromServer, bool serverFirst = false)
    {
        int added = 0, replaced = 0;
        lock (gate)
        {
            foreach (var e in incoming)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) continue;
                e.Name = KnownProcess.Key(e.Name);
                bool had = entries.TryGetValue(e.Name, out var mine);
                // the server's is the master: it replaces ours unless we've changed ours since (then the newer wins, or
                // the server's on a new client's first sync); a reviewed one is never replaced by an automatic one
                bool take = !had || (fromServer && (serverFirst || !dirty.Contains(e.Name))
                    ? !(mine!.Reviewed && !e.Reviewed)
                    : mine!.LosesTo(e));
                if (!take)
                {
                    // ours stays; the server's figures still come with it
                    if (fromServer) { mine!.Rev = Math.Max(mine.Rev, e.Rev); mine.FleetCpu = e.FleetCpu; mine.FleetMemoryMB = e.FleetMemoryMB; mine.FleetPcs = e.FleetPcs; }
                    continue;
                }
                if (!fromServer) { e.Source = e.Source is "seed" or "auto" ? e.Source : "import"; dirty.Add(e.Name); }
                else { if (e.Source != "seed" && e.Source != "auto") e.Source = "server"; dirty.Remove(e.Name); }
                entries[e.Name] = e;
                if (had) replaced++; else added++;
            }
        }
        if (added + replaced > 0) { Changed?.Invoke(); saveSoon.Poke(); }
        return (added, replaced);
    }

    /// <summary>The entries changed here since they were last sent (marked sent: they're sent again if the channel goes
    /// before the server answers, as <see cref="MarkDirty"/> puts them back).</summary>
    public List<KnownProcess> TakeDirty()
    {
        lock (gate)
        {
            var list = dirty.Where(entries.ContainsKey).Select(n => entries[n].Copy()).ToList();
            dirty.Clear();
            saveSoon.Poke();
            return list;
        }
    }

    public void MarkDirty(IEnumerable<string> names)
    {
        lock (gate) foreach (var n in names) dirty.Add(n);
        saveSoon.Poke();
    }

    public int DirtyCount { get { lock (gate) return dirty.Count; } }

    /// <summary>In step with the server's <paramref name="version"/> (of <paramref name="server"/>).</summary>
    public void SetVersion(string server, long version)
    {
        lock (gate) { Server = server; Version = version; }
        Changed?.Invoke();
        saveSoon.Poke();
    }

    // ---------------------------------------------------------------- export and import

    /// <summary>The database as an export file's JSON: every entry but the list it comes with (unless changed).</summary>
    public string Export()
    {
        List<KnownProcess> list;
        lock (gate) list = entries.Values.Where(e => e.Source != "seed").OrderBy(e => e.Name).ToList();
        var o = new JsonObject
        {
            ["format"] = Format, ["formatVersion"] = 1, ["exported"] = DateTime.UtcNow, ["client"] = Environment.MachineName,
            ["serverVersion"] = Version, ["entries"] = JsonSerializer.SerializeToNode(list, Json),
        };
        return o.ToJsonString(Json);
    }

    /// <summary>Reads an export (or a bare list of entries).</summary>
    public static List<KnownProcess> ReadExport(string json)
    {
        var node = JsonNode.Parse(json);
        var array = node as JsonArray ?? node?["entries"] as JsonArray ?? throw new FormatException(T("Not a process database export"));
        return array.Deserialize<List<KnownProcess>>(Json) ?? new();
    }

    // ---------------------------------------------------------------- the file

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class FileShape
    {
        public long Version { get; set; }
        public string Server { get; set; } = "";
        public int SeedVersion { get; set; }
        public List<KnownProcess> Entries { get; set; } = new();
        public List<string> Dirty { get; set; } = new();
        public Dictionary<string, Observed> Observed { get; set; } = new();
    }

    private static ProcessDb Load()
    {
        var db = new ProcessDb();
        FileShape? f = null;
        try { if (File.Exists(FilePath)) f = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(FilePath), Json); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Process database: {ex.Message} (started again)"); }
        if (f != null)
        {
            db.Version = f.Version;
            db.Server = f.Server;
            foreach (var e in f.Entries) if (e.Name.Length > 0) db.entries[e.Name] = e;
            db.dirty = new HashSet<string>(f.Dirty, StringComparer.OrdinalIgnoreCase);
            db.observed = new Dictionary<string, Observed>(f.Observed, StringComparer.OrdinalIgnoreCase);
        }
        db.AddSeeds(f?.SeedVersion ?? 0);
        return db;
    }

    /// <summary>The list it comes with: added where a program isn't known, and replacing an older copy of the list's
    /// own entry (never an entry from anywhere else).</summary>
    private void AddSeeds(int had)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProcessAnalyzer.known.json");
            if (s == null) return;
            var seed = JsonSerializer.Deserialize<SeedFile>(s, Json);
            if (seed == null) return;
            seedVersion = seed.Version;
            var when = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
            foreach (var e in seed.Entries)
            {
                e.Name = KnownProcess.Key(e.Name);
                e.Source = "seed";
                e.Reviewed = true;
                e.Updated = when;
                e.By = "Daisy's App";
                if (!entries.TryGetValue(e.Name, out var mine) || (mine.Source == "seed" && seed.Version > had) || (mine.Source == "auto" && !mine.Reviewed))
                    entries[e.Name] = e;
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Process database: the known list: {ex.Message}"); }
    }

    private int seedVersion;

    private sealed class SeedFile
    {
        public int Version { get; set; }
        public List<KnownProcess> Entries { get; set; } = new();
    }

    /// <summary>Saves now (the app closing).</summary>
    public void Flush()
    {
        saveSoon.Cancel();
        Save();
    }

    public void Save()
    {
        FileShape f;
        lock (gate)
        {
            f = new FileShape
            {
                Version = Version, Server = Server, SeedVersion = seedVersion,
                Entries = entries.Values.OrderBy(e => e.Name).ToList(), Dirty = dirty.ToList(),
                Observed = new Dictionary<string, Observed>(observed),
            };
            foreach (var o in f.Observed.Values) { o.AvgCpu = Math.Round(o.AvgCpu, 3); o.AvgMemoryMB = Math.Round(o.AvgMemoryMB, 1); }
        }
        try
        {
            Directory.CreateDirectory(Folder);
            JsonStore.WriteAtomic(FilePath, JsonSerializer.Serialize(f, Json));
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Process database not saved: {ex.Message}"); }
    }
}
