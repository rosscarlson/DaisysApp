using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// One look at the PC: the processes (<see cref="Scanner"/>), their files' facts, network connections and history,
/// grouped by program; a program the database doesn't know is classified (<see cref="Classifier.Guess"/>) and stored,
/// and each is assessed. Call it off the UI thread, one look at a time; CPU figures are since the previous look.
/// </summary>
internal sealed class Analyzer(ProcessAnalyzerSettings settings)
{
    private readonly Scanner scanner = new();
    private bool primed;
    private static readonly HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);

    public List<Assessment> Run(bool observe)
    {
        if (!primed) { scanner.Scan(); Thread.Sleep(1000); primed = true; }
        var procs = scanner.Scan().Where(p => p.Pid != 0).ToList();
        Scanner.FactsAsync(procs.Select(p => p.Path).Where(p => p != null && Scanner.Facts(p) == null).Cast<string>()).Wait();
        Dictionary<int, (SortedSet<int>, int)>? net = null;
        try { net = Scanner.Connections(); } catch { }
        Dictionary<string, ProgramHistory> history;
        try { history = PerfHistory.Read(settings.HistoryDays); } catch { history = new(); }

        var list = new List<Assessment>();
        foreach (var g in procs.GroupBy(p => KnownProcess.Key(p.Exe)))
        {
            var facts = g.Select(p => Scanner.Facts(p.Path)).Where(f => f != null)
                .OrderByDescending(f => f!.Sign switch { SignState.Untrusted => 3, SignState.Unsigned => 2, SignState.Unknown => 1, _ => 0 })
                .FirstOrDefault();
            var entry = ProcessDb.Db.Get(g.Key);
            bool isNew = false;
            if (entry == null)
            {
                entry = Classifier.Guess(g.Key, g.Select(p => p.Path).FirstOrDefault(p => p != null), facts);
                isNew = ProcessDb.Db.AddAuto(entry);
                if (isNew) Log.Here.Info($"Process Analyzer: new program {g.Key} ({entry.Title}, {entry.Publisher}): {entry.Category}, {entry.Threat}");
                entry = ProcessDb.Db.Get(g.Key) ?? entry;
            }
            var a = new Assessment { Name = g.Key, Entry = entry, New = isNew, Procs = g.ToList(), Facts = facts };
            Classifier.Assess(a, history.GetValueOrDefault(g.Key), net);
            if (settings.Ignored.Contains(g.Key, StringComparer.OrdinalIgnoreCase)) { a.Suggestion = ""; a.Score = Math.Min(a.Score, 0); }
            list.Add(a);
        }
        if (observe) ProcessDb.Db.Observe(list.Select(a => (a.Name, a.Cpu, a.MemoryMB, a.Procs.Select(p => p.Path).FirstOrDefault(p => p != null))));
        return list;
    }

    /// <summary>Automatic VirusTotal lookups (with a key): programs not reviewed and not signed, not looked up in 30 days.</summary>
    public void LookUpUnknown(IEnumerable<Assessment> found)
    {
        if (settings.VirusTotalKey.Length == 0 || !settings.AutoVirusTotal) return;
        foreach (var a in found)
        {
            if (a.Entry.Reviewed || a.Facts == null || a.Facts.Sign is SignState.Signed or SignState.Catalog) continue;
            if (a.Entry.VtChecked is DateTime t && DateTime.UtcNow - t < TimeSpan.FromDays(30)) continue;
            lock (asked) if (!asked.Add(a.Name)) continue;
            string name = a.Name, path = a.Facts.Path;
            _ = Task.Run(async () =>
            {
                try { await Lookup.VirusTotalAsync(settings.VirusTotalKey, path, name); }
                catch (Exception ex) { Log.Here.Info($"Process Analyzer: VirusTotal {name}: {ex.Message}"); }
            });
        }
    }
}
