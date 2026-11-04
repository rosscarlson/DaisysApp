using System.IO;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>A program's processes together, as the table shows them: what's known about it and what's found.</summary>
public sealed class Assessment
{
    public required string Name { get; init; }
    public required KnownProcess Entry { get; init; }
    /// <summary>The entry wasn't in the database before this look (it was just worked out).</summary>
    public bool New { get; init; }
    public List<ProcInfo> Procs { get; init; } = new();
    public FileFacts? Facts { get; init; }
    /// <summary>The worst of the entry's threat and what was found here.</summary>
    public Threat Threat { get; set; }
    /// <summary>What was found, worst first: (how bad, what).</summary>
    public List<(Threat Level, string Text)> Findings { get; } = new();
    public double Cpu { get; set; }
    public double MemoryMB { get; set; }
    /// <summary>Over what its <see cref="Resource"/> allows: now, or on average in its history.</summary>
    public bool OverNow { get; set; }
    public bool OverHistory { get; set; }
    /// <summary>The suggestion ("" for none) and how much it matters (the table's default order).</summary>
    public string Suggestion { get; set; } = "";
    public double Score { get; set; }
}

/// <summary>
/// Works out what a program is when the database doesn't know it (from its signature, its file's details and where it
/// runs from), and assesses each running program: its file checked against what's known (a Windows name running from
/// elsewhere, another publisher, no signature, a temporary folder), its use against what's expected of it, and a
/// suggestion when it's risky, likely unnecessary or heavier than it should be.
/// </summary>
internal static class Classifier
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string Temp = Path.GetTempPath().TrimEnd('\\');
    private static readonly string Downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>What's "too much" for each <see cref="Resource"/>: CPU % of the whole PC, and private memory in MB.</summary>
    public static (double Cpu, double MemoryMB) Limit(Resource r) => r switch
    {
        Resource.Light => (3, 300),
        Resource.Moderate => (15, 1500),
        _ => (60, 8000),
    };

    /// <summary>An entry for a program the database doesn't know, from what its file says.</summary>
    public static KnownProcess Guess(string name, string? path, FileFacts? f)
    {
        var e = new KnownProcess { Name = name, Source = "auto", Reviewed = false };
        string lower = (path ?? "").ToLowerInvariant();
        string n = name.ToLowerInvariant();
        e.Title = f is { Description.Length: > 0 } ? f.Description : f is { Product.Length: > 0 } ? f.Product : name;
        e.Publisher = f is { Signer.Length: > 0 } ? f.Signer : f?.Company ?? "";
        bool signed = f?.Sign is SignState.Signed or SignState.Catalog;
        bool microsoft = signed && f!.Signer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        bool inWindows = path != null && Under(path, Windows);

        if (microsoft && inWindows) { e.Category = "Windows"; e.Necessity = Necessity.Recommended; e.Threat = Threat.Safe; e.Resource = Resource.Light; }
        else if (microsoft) { e.Category = "Microsoft"; e.Necessity = Necessity.Optional; e.Threat = Threat.Safe; }
        else
        {
            // not signed is worth a look; a file that couldn't be read (a protected process) isn't, by itself
            e.Threat = signed || f == null ? Threat.Low : Threat.Medium;
            e.Necessity = Necessity.Unknown;
            e.Category =
                n.Contains("update") || n.Contains("updater") ? "Updater" :
                n.Contains("crash") || n.Contains("telemetry") || n.Contains("report") ? "Telemetry" :
                lower.Contains(@"\steamapps\") || lower.Contains(@"\epic games\") || lower.Contains(@"\games\") || lower.Contains(@"\riot games\") ? "Gaming" :
                n.Contains("helper") || n.Contains("agent") || n.Contains("tray") || n.Contains("service") || n.EndsWith("svc") ? "Background" :
                "App";
            if (e.Category is "Updater" or "Telemetry") { e.Necessity = Necessity.Unnecessary; e.Resource = Resource.Light; }
            if (e.Category == "Background") e.Necessity = Necessity.Optional;
        }
        if (path != null)
        {
            string folder = Path.GetDirectoryName(path) ?? "";
            if (inWindows) e.Paths.Add(Variables(folder));
        }
        e.Description = Describe(e, f);
        return e;
    }

    private static string Describe(KnownProcess e, FileFacts? f)
    {
        var parts = new List<string>();
        if (f is { Product.Length: > 0 } && f.Product != e.Title) parts.Add(f.Product);
        if (f is { Company.Length: > 0 }) parts.Add(F("by {0}", f.Company));
        return parts.Count == 0 ? T("Classified automatically from its file") : string.Join(" ", parts) + " · " + T("classified automatically");
    }

    /// <summary>Assesses a program's running processes against its entry, history and settings.</summary>
    public static void Assess(Assessment a, ProgramHistory? history, Dictionary<int, (SortedSet<int> Listening, int Connected)>? net)
    {
        var e = a.Entry;
        var f = a.Facts;
        var paths = a.Procs.Select(p => p.Path).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        a.Cpu = a.Procs.Sum(p => p.Cpu);
        a.MemoryMB = a.Procs.Sum(p => p.MemoryMB);

        // where it runs from
        foreach (var path in paths)
        {
            if (e.Reviewed && e.Paths.Count > 0 && !e.Paths.Any(p => Matches(path, p)))
                a.Findings.Add((e.Category == "Windows" ? Threat.Dangerous : Threat.High,
                    F("Runs from {0}, not where {1} belongs", Path.GetDirectoryName(path) ?? path, e.Title.Length > 0 ? e.Title : a.Name)));
            if (path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase))
                a.Findings.Add((Threat.Dangerous, T("Runs from the Recycle Bin")));
            else if (Under(path, Temp))
                a.Findings.Add((Threat.High, T("Runs from a temporary folder")));
            else if (Under(path, Downloads))
                a.Findings.Add((Threat.Medium, T("Runs from Downloads")));
        }

        // its file
        if (f != null)
        {
            if (f.Sign == SignState.Untrusted)
                a.Findings.Add((Threat.High, F("Its signature isn't valid ({0})", f.Signer.Length > 0 ? f.Signer : T("unknown signer"))));
            else if (f.Sign == SignState.Unsigned)
            {
                bool installed = Under(f.Path, ProgramFiles) || Under(f.Path, ProgramFilesX86) || Under(f.Path, Windows);
                // (a reviewed program's classification already knows it isn't signed)
                a.Findings.Add((installed || e.Reviewed ? Threat.Low : Threat.Medium, installed ? T("Not signed") : T("Not signed, and not installed in Program Files")));
            }
            if (e.Publisher.Length > 0 && e.Reviewed && f.Sign is SignState.Signed or SignState.Catalog && f.Signer.Length > 0 && !Publishes(e.Publisher, f.Signer))
                a.Findings.Add((Threat.High, F("Signed by {0}, not {1}", f.Signer, e.Publisher.Replace("|", " / "))));
            if (f.Hidden) a.Findings.Add((Threat.Medium, T("Its file is hidden")));
            // renamed: a copy of a risky program under another name matters (an apphost's .dll, a rebrand don't)
            if (f.OriginalName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(KnownProcess.Key(f.OriginalName), a.Name, StringComparison.OrdinalIgnoreCase)
                && ProcessDb.Db.Get(f.OriginalName) is { } original && original.Threat >= Threat.Medium)
                a.Findings.Add((Threat.High, F("Renamed: it's {0} ({1})", f.OriginalName, original.Title)));
        }
        else if (paths.Count == 0 && a.Procs.Count > 0 && e.Source == "auto")
            a.Findings.Add((Threat.Low, T("Its file can't be read (protected, or another user's)")));

        // on the network, unsigned
        if (net != null && f?.Sign is SignState.Unsigned or SignState.Untrusted)
        {
            var ports = a.Procs.SelectMany(p => net.TryGetValue(p.Pid, out var c) ? c.Listening : Enumerable.Empty<int>()).Distinct().ToList();
            if (ports.Count > 0) a.Findings.Add((e.Reviewed ? Threat.Low : Threat.Medium, F("Not signed, and listening on port {0}", string.Join(", ", ports.Take(5)))));
        }

        // VirusTotal
        if (e.VtMalicious is int vt && vt > 0)
            a.Findings.Add((vt >= 3 ? Threat.Dangerous : Threat.High, F("VirusTotal: {0} of {1} engines flag it", vt, e.VtEngines ?? 0)));

        a.Findings.Sort((x, y) => y.Level.CompareTo(x.Level));
        a.Threat = (Threat)Math.Max((int)e.Threat, a.Findings.Count == 0 ? 0 : (int)a.Findings[0].Level);

        // its use
        var (cpuLimit, memLimit) = Limit(e.Resource);
        a.OverNow = a.Cpu > cpuLimit || a.MemoryMB > memLimit;
        a.OverHistory = history != null && history.TopCpuRows >= 30 && history.AvgCpuWhenTop > cpuLimit && history.TopCpuShare > 0.05;

        // the suggestion
        string s = "";
        double score = (int)a.Threat * 100;
        if (a.Threat == Threat.Dangerous) s = a.Findings.Count > 0 ? a.Findings[0].Text : T("Known to be dangerous");
        else if (a.Threat == Threat.High) s = a.Findings.Count > 0 ? a.Findings[0].Text : T("Risky");
        else if (e.Necessity == Necessity.Unnecessary) { s = T("Likely unnecessary"); score += 60; }
        else if (a.Threat == Threat.Medium) s = a.Findings.Count > 0 ? a.Findings[0].Text : T("Unknown: worth a look");
        if (a.OverNow || a.OverHistory)
        {
            string use = a.OverNow ? F("Using more than expected ({0:0.#}% CPU, {1})", a.Cpu, Sizes.Bytes((long)(a.MemoryMB * 1048576)))
                : F("Often busy: {0:0.#}% CPU on average when among the busiest", history!.AvgCpuWhenTop);
            s = s.Length == 0 ? use : s + " · " + use;
            score += 50;
        }
        if (s.Length > 0 && e.Necessity == Necessity.Required && a.Threat < Threat.High) score -= 40;
        a.Suggestion = s;
        a.Score = score + Math.Min(a.Cpu, 100) / 2;
    }

    // ---------------------------------------------------------------- paths

    /// <summary>Whether <paramref name="path"/> is in a folder named by an entry: a full folder (variables allowed,
    /// its subfolders too) or a part to find in it ("\Steam\").</summary>
    public static bool Matches(string path, string pattern)
    {
        string p = Environment.ExpandEnvironmentVariables(pattern).TrimEnd('\\');
        if (p.Length == 0) return true;
        if (Path.IsPathRooted(p)) return Under(path, p);
        return path.Contains(p, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Under(string path, string folder) =>
        folder.Length > 0 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Windows folder with %SystemRoot% in place of where Windows is.</summary>
    private static string Variables(string folder) =>
        Under(folder + "\\", Windows) ? "%SystemRoot%" + folder[Windows.Length..] : folder;

    /// <summary>Whether a signer is the publisher an entry names (or one of them: "Microsoft|.NET"), loosely.</summary>
    public static bool Publishes(string publisher, string signer) =>
        publisher.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => Loose(signer).Contains(Loose(p)) || Loose(p).Contains(Loose(signer)));

    private static string Loose(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
