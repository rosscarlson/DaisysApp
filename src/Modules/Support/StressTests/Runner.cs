using System.Diagnostics;
using System.IO;
using System.Reflection;
using DaisysApp.Applets.Support.StressTests.Tests;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>
/// Runs the ticked tests one after another (Repeat times, or until stopped), each on its own thread, with the sensors
/// read every second; logs to the Support log (Standard or Maximum); keeps every run (logs\stresstests, a file each).
/// While a run goes, a marker file holds it as far as it's got, so a run the app (or Windows) didn't live through is
/// found at the next start and kept, marked as stopped during the test it was on, with what Windows logged.
/// </summary>
internal sealed class Runner
{
    private readonly StressSettings settings;
    private readonly object gate = new();
    private readonly List<StressRun> runs = [];
    private CancellationTokenSource? cancel;
    private static readonly Logger log = Log.Here;

    public static string Folder => Path.Combine(AppPaths.LogFolder, "stresstests");
    private static string Marker => Path.Combine(Folder, "running.json");

    /// <summary>Raised (on any thread) when a run starts, a test starts or ends, or the list of runs changes.</summary>
    public event Action? Changed;

    public bool Running { get; private set; }
    public StressRun? Current { get; private set; }
    public TestContext? Test { get; private set; }
    /// <summary>Tests done in this run, and how many there are (0 = until stopped).</summary>
    public int Done { get; private set; }
    public int Total { get; private set; }
    public Monitor? Sensors { get; private set; }
    public Reading? Latest => Sensors?.Latest;

    /// <summary>The 3D test's latest frame (BGRA, width, height).</summary>
    public (byte[] Pixels, int Width, int Height)? Frame { get; private set; }
    public long FrameNumber { get; private set; }

    public Runner(StressSettings settings)
    {
        this.settings = settings;
        Task.Run(Load);
    }

    public IReadOnlyList<StressRun> Runs { get { lock (gate) return runs.ToList(); } }

    /// <summary>The run going, as far as it's got (a copy, scored), for the page.</summary>
    public StressRun? Snapshot()
    {
        lock (gate)
        {
            if (Current is not { } run) return null;
            var copy = new StressRun { System = run.System, Started = run.Started, Loops = run.Loops, Results = run.Results.ToList() };
            copy.ComputeScores();
            return copy;
        }
    }

    /// <summary>How many results the run going has.</summary>
    public int ResultCount { get { lock (gate) return Current?.Results.Count ?? 0; } }

    private void Load()
    {
        var loaded = new List<StressRun>();
        try
        {
            Directory.CreateDirectory(Folder);
            RecoverInterrupted();
            foreach (var f in Directory.EnumerateFiles(Folder, "stress-*.json"))
            {
                try { loaded.Add(StressRun.Read(f)); }
                catch (Exception ex) { log.Warn($"Stress Tests: couldn't read {f}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.Warn("Stress Tests: couldn't read the runs", ex); }
        lock (gate) { runs.InsertRange(0, loaded); runs.Sort((a, b) => a.Started.CompareTo(b.Started)); }
        Changed?.Invoke();
    }

    /// <summary>A marker left behind: the app or Windows stopped during that run. Kept as a run of its own.</summary>
    private void RecoverInterrupted()
    {
        if (!File.Exists(Marker)) return;
        try
        {
            var run = StressRun.Read(Marker);
            run.File = null;
            run.Interrupted = true;
            run.Ended = File.GetLastWriteTime(Marker);
            run.Events = WindowsEventsSince(run.Started, DateTime.Now);
            run.ComputeScores();
            string during = run.InterruptedDuring ?? "?";
            log.Error($"Stress Tests: the run started {run.Started:g} didn't finish: the app or Windows stopped during {during} (last heard from {run.Ended:T})");
            foreach (var e in run.Events) log.Warn($"Stress Tests: Windows logged during that run: {e.Time:T} {e.Source} {e.Id}: {e.Message}");
            Save(run);
            lock (gate) runs.Add(run);
            Notifications.Show(T("Stress Tests"), F("The last stress test run stopped during {0}: the app or Windows closed. Its results so far are kept.", during));
        }
        catch (Exception ex) { log.Warn("Stress Tests: couldn't read the run that was going", ex); }
        try { File.Delete(Marker); } catch { }
    }

    public void Delete(StressRun run)
    {
        lock (gate) runs.Remove(run);
        try { if (run.File != null) File.Delete(run.File); } catch (Exception ex) { log.Warn($"Stress Tests: couldn't delete {run.File}: {ex.Message}"); }
        Changed?.Invoke();
    }

    public void Stop() => cancel?.Cancel();

    private static IStressTest Make(string id) => id switch
    {
        "CpuSingle" => new CpuSingle(), "CpuMulti" => new CpuMulti(), "CpuVector" => new CpuVector(),
        "MemBandwidth" => new MemBandwidth(), "MemLatency" => new MemLatency(), "MemIntegrity" => new MemIntegrity(),
        "Storage" => new StorageTest(), "Gpu3D" => new Gpu3D(), "GpuCompute" => new GpuCompute(), "Vram" => new Vram(),
        "Encode" => new Encode(), "Decode" => new Decode(), "AllAtOnce" => new AllAtOnce(),
        _ => throw new ArgumentException(id),
    };

    /// <summary>Runs the ticked tests with the settings as they are.</summary>
    public async Task RunAsync()
    {
        if (Running) return;
        var tests = Catalog.Tests.Where(t => settings.Tests.Contains(t.Id)).ToList();
        if (tests.Count == 0) return;
        Running = true;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        Done = 0;
        Total = settings.Loops > 0 ? tests.Count * settings.Loops : 0;
        Frame = null;
        TitleBadges.Set("StressTests", T("Stress Test"), T("A stress test is running"), TitleBadges.Green, "StressTests");
        Changed?.Invoke();
        try { await Task.Run(() => Run(tests, token)); }
        finally
        {
            Running = false;
            Test = null;
            TitleBadges.Set("StressTests", null);
            Changed?.Invoke();
        }
    }

    private void Run(List<TestInfo> tests, CancellationToken token)
    {
        var spec = SystemSpec.Read(settings.Gpu.Length > 0 ? settings.Gpu : null, settings.Drive);
        var run = new StressRun
        {
            AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "",
            Started = DateTime.Now, System = spec, SecondsPerTest = settings.Seconds, Loops = settings.Loops,
            Logging = settings.Logging, Tests = tests.Select(t => t.Id).ToList(),
        };
        Current = run;
        bool max = settings.Logging == StressLogging.Maximum;
        log.Info($"Stress Tests: starting {string.Join(", ", tests.Select(t => t.Group + " " + t.Name))}; {settings.Seconds} s each, " +
                 $"{(settings.Loops == 0 ? "until stopped" : settings.Loops + "×")}, {settings.Logging} logging{(settings.StopOnError ? ", stopping at the first error" : "")}");
        log.Info($"Stress Tests: {spec.Pc}: {spec.CpuText}; {spec.RamText}; {string.Join(", ", spec.Gpus)}; {spec.Os}");
        if (max) LogDetails(spec);
        Sensors = new Monitor(spec.CpuBaseMHz);
        if (max) Sensors.Read += r => { if (Test is { } t) log.Info($"Stress Tests: [{t.Info.Name} {t.Elapsed.TotalSeconds:0} s] {Metric.Format(t.Live, t.LiveUnit)} · {r.Text()}"); };
        WriteMarker(run, null);
        try
        {
            for (int loop = 1; settings.Loops == 0 || loop <= settings.Loops; loop++)
            {
                foreach (var info in tests)
                {
                    if (token.IsCancellationRequested) break;
                    var c = new TestContext(info, TimeSpan.FromSeconds(Math.Clamp(settings.Seconds, 5, 24 * 3600)), settings, run.Results.ToList(), token, loop)
                    {
                        Preview = (px, w, h) => { Frame = (px, w, h); FrameNumber++; },
                    };
                    RunOne(run, c, loop);
                    Done++;
                    if (settings.StopOnError && c.Errors > 0)
                    {
                        log.Warn($"Stress Tests: stopping: {info.Name} found errors");
                        cancel?.Cancel();
                    }
                }
                if (token.IsCancellationRequested) break;
            }
        }
        finally
        {
            Sensors.Dispose();
            Sensors = null;
        }
        run.Stopped = token.IsCancellationRequested;
        run.Ended = DateTime.Now;
        run.Events = WindowsEventsSince(run.Started, run.Ended);
        foreach (var e in run.Events) log.Warn($"Stress Tests: Windows logged during the run: {e.Time:T} {e.Source} {e.Id}: {e.Message}");
        run.ComputeScores();
        log.Info($"Stress Tests: finished{(run.Stopped ? " (stopped)" : "")} in {(run.Ended - run.Started).TotalMinutes:0.0} min: score {run.Score:0}, " +
                 $"{string.Join(", ", run.GroupScores.Select(g => $"{g.Key} {g.Value:0}"))}; {run.Errors} errors");
        Save(run);
        try { File.Delete(Marker); } catch { }
        lock (gate) runs.Add(run);
        Changed?.Invoke();
    }

    private void RunOne(StressRun run, TestContext c, int loop)
    {
        var info = c.Info;
        Test = c;
        WriteMarker(run, info.Name);
        Changed?.Invoke();
        log.Info($"Stress Tests: {info.Group} {info.Name}{(run.Loops != 1 ? $" (loop {loop})" : "")} starting");
        var started = DateTime.Now;
        var sw = Stopwatch.StartNew();
        // its own thread, so a test that hangs in a driver can be left behind
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Make(info.Id).Run(c); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true, Name = $"StressTests-{info.Id}" };
        thread.Start();
        var limit = c.Length + TimeSpan.FromMinutes(3);
        while (!thread.Join(500))
        {
            if (sw.Elapsed > limit)
            {
                c.Error(F("Didn't finish {0:0} s after its time: left running", (sw.Elapsed - c.Length).TotalSeconds));
                break;
            }
        }
        var r = c.Result;
        if (failure != null)
        {
            r.Failed = failure.Message;
            log.Error($"Stress Tests: {info.Name} failed", failure);
        }
        r.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
        r.Stopped = c.Stopping;
        r.Sensors = Sensors?.Summary(started) ?? new();
        if (r.Failed == null && r.Score == 0) r.Score = info.ScoreOf(r.Value);
        if (r.Errors > 0) r.Score = Math.Round(r.Score); // kept: the errors say what's wrong
        // the CPU's clock falling while a CPU test ran
        if (info.Group is Catalog.Cpu or Catalog.Stress && Sensors?.CpuClocks(started.AddSeconds(2)) is { Count: >= 4 } clocks)
        {
            double top = clocks.Take(Math.Max(2, clocks.Count / 4)).Max(), low = clocks.Skip(clocks.Count / 2).Average();
            if (low < top * 0.85) c.Note(F("The CPU's clock fell from {0:0.00} to {1:0.00} GHz during the test (throttling?)", top / 1000, low / 1000));
        }
        lock (gate) run.Results.Add(r);
        string result = r.Failed != null ? $"couldn't run: {r.Failed}"
            : $"{Metric.Format(r.Value, r.Unit)}{(info.Scored ? $", score {r.Score:0}" : "")}{(r.Metrics.Count > 0 ? " (" + string.Join(", ", r.Metrics.Select(m => $"{m.Name} {m.Text}")) + ")" : "")}";
        string sensors = SensorText(r.Sensors);
        if (r.Errors > 0) log.Error($"Stress Tests: {info.Name}: {result}; {r.Errors} ERRORS{(sensors.Length > 0 ? "; " + sensors : "")}");
        else log.Info($"Stress Tests: {info.Name}: {result}{(r.Device != null ? " on " + r.Device : "")}{(sensors.Length > 0 ? "; " + sensors : "")}");
        Test = null;
        WriteMarker(run, null);
        Changed?.Invoke();
    }

    private static string SensorText(SensorSummary s)
    {
        static string N(double v, string text) => double.IsNaN(v) ? "" : string.Format(text, v);
        return string.Join(", ", new[]
        {
            N(s.CpuClockAvg / 1000, "CPU {0:0.00} GHz avg"), N(s.CpuTempMax, "CPU {0:0} °C max"), N(s.CpuPowerMax, "CPU {0:0} W max"),
            N(s.GpuClockAvg, "GPU {0:0} MHz avg"), N(s.GpuTempMax, "GPU {0:0} °C max"), N(s.GpuPowerMax, "GPU {0:0} W max"),
        }.Where(x => x.Length > 0));
    }

    /// <summary>Maximum logging: everything that might matter if something breaks.</summary>
    private static void LogDetails(SystemSpec s)
    {
        log.Info($"Stress Tests: board {s.Board}; BIOS {s.Bios}; power plan {s.PowerPlan}");
        foreach (var m in s.RamModules) log.Info($"Stress Tests: memory module {m}");
        foreach (var g in s.Gpus) log.Info($"Stress Tests: graphics {g.Name}, {g.VramGB:0.#} GB, driver {g.Driver}");
        foreach (var d in s.Drives) log.Info($"Stress Tests: drive {d}");
        try
        {
            Mem.Status(out ulong total, out ulong available);
            using var p = Process.GetCurrentProcess();
            log.Info($"Stress Tests: memory {available / 1073741824.0:0.0} of {total / 1073741824.0:0.0} GB free; the app uses {p.WorkingSet64 / 1048576.0:0} MB, {p.Threads.Count} threads");
        }
        catch { }
    }

    private void WriteMarker(StressRun run, string? test)
    {
        try
        {
            lock (gate)
            {
                run.InterruptedDuring = test;
                run.Write(Marker);
                run.InterruptedDuring = null;
            }
        }
        catch (Exception ex) { log.Debug(() => $"Stress Tests: marker: {ex.Message}"); }
    }

    private static void Save(StressRun run)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"stress-{run.Started:yyyyMMdd-HHmmss}.json");
            run.Write(path);
            run.File = path;
        }
        catch (Exception ex) { log.Warn("Stress Tests: couldn't save the run", ex); }
    }

    /// <summary>What Windows logged between the times that bears on stability.</summary>
    private static List<RunEvent> WindowsEventsSince(DateTime from, DateTime to)
    {
        var list = new List<RunEvent>();
        try
        {
            string[] providers =
            [
                "Microsoft-Windows-Kernel-Power", "EventLog", "Microsoft-Windows-WHEA-Logger", "Display", "nvlddmkm", "amdkmdag", "amdkmdap",
                "igfx", "igfxn", "disk", "stornvme", "storahci", "Ntfs", "volmgr", "BugCheck", "Microsoft-Windows-Resource-Exhaustion-Detector",
            ];
            string filter = "(" + string.Join(" or ", providers.Select(p => $"Provider[@Name='{p}']")) + ") and (Level=1 or Level=2 or Level=3)";
            foreach (var e in WindowsEvents.Read("System", from.AddSeconds(-5), 200, filter))
            {
                if (e.Time > to.AddMinutes(10)) continue;
                // EventLog's own entries: only the unexpected shutdown one
                if (e.Provider == "EventLog" && e.Id != 6008) continue;
                list.Add(new RunEvent { Time = e.Time, Source = e.Provider, Id = e.Id, Level = e.Level.ToString(), Message = Trim(e.Message) });
            }
            foreach (var e in WindowsEvents.AppCrashes(from, 10))
                list.Add(new RunEvent { Time = e.Time, Source = e.Provider, Id = e.Id, Level = e.Level.ToString(), Message = Trim(e.Message) });
        }
        catch (Exception ex) { log.Debug(() => $"Stress Tests: reading Windows' logs: {ex.Message}"); }
        return list.OrderBy(e => e.Time).Take(50).ToList();
    }

    private static string Trim(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length > 300 ? s[..300] + "…" : s;
    }
}
