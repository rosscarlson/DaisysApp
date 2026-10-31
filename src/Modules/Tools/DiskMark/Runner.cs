using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>A test's results: the best of its runs for each kind measured.</summary>
public sealed class BenchResult
{
    public TestDef Test { get; set; } = new();
    public Score? Read { get; set; }
    public Score? Write { get; set; }
    public Score? Mix { get; set; }

    public Score? Of(IoKind k) => k switch { IoKind.Read => Read, IoKind.Write => Write, _ => Mix };
}

/// <summary>One run on one drive, as saved in logs\diskmark (a file each).</summary>
public sealed class BenchRun
{
    public DateTime Time { get; set; }
    /// <summary>"C:\".</summary>
    public string Drive { get; set; } = "";
    public string Label { get; set; } = "";
    public string Model { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Profile { get; set; } = Profiles.Default;
    public BenchMode Mode { get; set; }
    public int Count { get; set; }
    public int SizeMiB { get; set; }
    public int Seconds { get; set; }
    public int IntervalSeconds { get; set; }
    public bool Zeros { get; set; }
    public bool Scheduled { get; set; }
    public double TookSeconds { get; set; }
    public List<BenchResult> Results { get; set; } = [];
    public string? Error { get; set; }
    public bool Cancelled { get; set; }

    public string? File;
}

/// <summary>
/// Runs the tests on one drive after another, CrystalDiskMark's way: the test file written first, then every read test
/// (each run Count times, the best kept), then every write test, with the interval between runs; the file deleted after.
/// Keeps every run (logs\diskmark, a file each) and runs the schedule. The page draws what it's doing from here.
/// </summary>
internal sealed class Runner
{
    public enum Step { Idle, Preparing, Measuring, Waiting }

    private readonly DiskMarkSettings settings;
    private readonly object gate = new();
    private readonly List<BenchRun> runs = [];
    private CancellationTokenSource? cancel;

    public static string Folder => Path.Combine(AppPaths.LogFolder, "diskmark");

    /// <summary>Raised (on the runner's thread or the UI's) when anything changes.</summary>
    public event Action? Changed;

    public bool Running { get; private set; }
    public string? Drive { get; private set; }
    public BenchRun? Current { get; private set; }
    public Step Now { get; private set; }
    public TestDef? Test { get; private set; }
    public IoKind Kind { get; private set; }
    /// <summary>1-based, of <see cref="Count"/>.</summary>
    public int Pass { get; private set; }
    public int Count { get; private set; }
    /// <summary>The test file's writing, 0–1.</summary>
    public double Prepared { get; private set; }
    public DateTime StepStarted { get; private set; }
    public double StepSeconds { get; private set; }
    /// <summary>Of the whole run on this drive, 0–1.</summary>
    public double Done { get; private set; }
    /// <summary>Drives still to test after this one (a scheduled run of several).</summary>
    public int DrivesLeft { get; private set; }
    public Bench.Live Live { get; private set; } = new();
    public string? LastError { get; private set; }

    public Runner(DiskMarkSettings settings)
    {
        this.settings = settings;
        Task.Run(Load);
    }

    public IReadOnlyList<BenchRun> Runs { get { lock (gate) return runs.ToList(); } }

    public List<BenchRun> RunsOf(string drive) { lock (gate) return runs.Where(r => r.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Time).ToList(); }

    private void Load()
    {
        var loaded = new List<BenchRun>();
        try
        {
            if (Directory.Exists(Folder))
                foreach (var f in Directory.EnumerateFiles(Folder, "diskmark-*.json"))
                {
                    try
                    {
                        var r = JsonSerializer.Deserialize<BenchRun>(System.IO.File.ReadAllText(f), Settings.JsonStore.Options);
                        if (r != null) { r.File = f; loaded.Add(r); }
                    }
                    catch (Exception ex) { Log.Here.Warn($"DiskMark: couldn't read {f}: {ex.Message}"); }
                }
        }
        catch (Exception ex) { Log.Here.Warn("DiskMark: couldn't read the results", ex); }
        lock (gate) { runs.InsertRange(0, loaded); runs.Sort((a, b) => a.Time.CompareTo(b.Time)); }
        Changed?.Invoke();
    }

    public void Delete(BenchRun run)
    {
        lock (gate) runs.Remove(run);
        try { if (run.File != null) System.IO.File.Delete(run.File); } catch (Exception ex) { Log.Here.Warn($"DiskMark: couldn't delete {run.File}: {ex.Message}"); }
        Changed?.Invoke();
    }

    public void Cancel() => cancel?.Cancel();

    /// <summary>Tests each drive in turn with the settings as they are.</summary>
    public async Task RunAsync(IReadOnlyList<DriveItem> drives, bool scheduled)
    {
        if (Running || drives.Count == 0) return;
        Running = true;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        TitleBadges.Set("DiskMark", T("DiskMark"), T("A disk benchmark is running"), TitleBadges.Green, "DiskMark");
        try
        {
            for (int i = 0; i < drives.Count && !token.IsCancellationRequested; i++)
            {
                DrivesLeft = drives.Count - i - 1;
                var drive = drives[i];
                await Task.Run(() => RunDrive(drive, scheduled, token));
            }
        }
        finally
        {
            Running = false;
            Now = Step.Idle;
            Drive = null;
            Current = null;
            DrivesLeft = 0;
            cancel.Dispose();
            cancel = null;
            TitleBadges.Set("DiskMark", null);
            Changed?.Invoke();
        }
    }

    private void RunDrive(DriveItem drive, bool scheduled, CancellationToken token)
    {
        var tests = settings.Tests();
        var kinds = settings.Mode switch
        {
            BenchMode.Read => new[] { IoKind.Read },
            BenchMode.Write => new[] { IoKind.Write },
            BenchMode.Mix => new[] { IoKind.Mix },
            _ => new[] { IoKind.Read, IoKind.Write },
        };
        int count = Math.Clamp(settings.Count, 1, 9), seconds = Math.Clamp(settings.Seconds, 1, 120), pause = Math.Clamp(settings.IntervalSeconds, 0, 600);
        long size = (long)Math.Clamp(settings.SizeMiB, 16, 65536) << 20;
        var run = new BenchRun
        {
            Time = DateTime.Now, Drive = drive.Root, Label = drive.Label, Model = drive.Model, Kind = drive.Kind,
            Profile = settings.Profile, Mode = settings.Mode, Count = count, SizeMiB = (int)(size >> 20), Seconds = seconds,
            IntervalSeconds = pause, Zeros = settings.Zeros, Scheduled = scheduled,
            Results = tests.Select(t => new BenchResult { Test = t }).ToList(),
        };
        Drive = drive.Root;
        Current = run;
        Count = count;
        Live = new Bench.Live();
        LastError = null;
        Done = 0;
        var clock = Stopwatch.StartNew();
        int parts = tests.Count * kinds.Length * count, part = 0;
        string? folder = null;
        Log.Here.Info($"DiskMark {drive.Letter} ({drive.Model}): {Profiles.Title(run.Profile)}, {settings.Mode}, {count} × {run.SizeMiB} MiB, {seconds} s{(scheduled ? ", scheduled" : "")}");
        try
        {
            folder = MakeFolder(drive.Root);
            var free = new DriveInfo(drive.Root).AvailableFreeSpace;
            if (free < size + (64L << 20)) throw new IOException(F("Not enough free space: {0} needed", Sizes.Bytes(size + (64L << 20))));
            string path = Path.Combine(folder, $"DiskMark-{Guid.NewGuid():N}.tmp");
            Set(Step.Preparing, null, IoKind.Write, 0, 0);
            using var keep = Bench.Prepare(path, size, settings.Zeros, Live, p => { Prepared = p; Changed?.Invoke(); }, token);
            foreach (var kind in kinds)
                foreach (var result in run.Results)
                    for (int pass = 1; pass <= count; pass++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (part > 0 && pause > 0)
                        {
                            Set(Step.Waiting, result.Test, kind, pass, pause);
                            if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(pause))) token.ThrowIfCancellationRequested();
                        }
                        Set(Step.Measuring, result.Test, kind, pass, seconds);
                        var score = Bench.Run(path, size, result.Test, kind, settings.Zeros, seconds, Live, token);
                        var best = result.Of(kind);
                        if (best == null || score.MBps > best.MBps)
                        {
                            if (kind == IoKind.Read) result.Read = score;
                            else if (kind == IoKind.Write) result.Write = score;
                            else result.Mix = score;
                        }
                        part++;
                        Done = (double)part / parts;
                        Changed?.Invoke();
                    }
            foreach (var r in run.Results)
                Log.Here.Info($"DiskMark {drive.Letter} {r.Test.Name}: " + string.Join(", ", kinds.Select(k => r.Of(k) is { } s ? $"{k} {s.MBps:0.00} MB/s, {s.Iops:0} IOPS, {s.LatencyUs:0.0} µs" : "")));
        }
        catch (OperationCanceledException)
        {
            run.Cancelled = true;
            Log.Here.Info($"DiskMark {drive.Letter}: stopped");
        }
        catch (Exception ex)
        {
            run.Error = ex.Message;
            LastError = ex.Message;
            Log.Here.Error($"DiskMark {drive.Letter} failed", ex);
        }
        finally
        {
            if (folder != null) try { Directory.Delete(folder, true); } catch { }
        }
        run.TookSeconds = clock.Elapsed.TotalSeconds;
        if (run.Results.Any(r => r.Read != null || r.Write != null || r.Mix != null)) Save(run);
        Changed?.Invoke();
    }

    private void Set(Step step, TestDef? test, IoKind kind, int pass, double seconds)
    {
        Now = step;
        Test = test;
        Kind = kind;
        Pass = pass;
        StepStarted = DateTime.Now;
        StepSeconds = seconds;
        Changed?.Invoke();
    }

    /// <summary>A folder for the test file at the drive's top (where CrystalDiskMark puts its own), or in TEMP on the
    /// system drive if Windows doesn't allow it there.</summary>
    private static string MakeFolder(string root)
    {
        string name = AppPaths.ShortName + "-DiskMark";
        string at = Path.Combine(root, name);
        try { Directory.CreateDirectory(at); return at; }
        catch (UnauthorizedAccessException) when (Path.GetPathRoot(Path.GetTempPath())!.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            at = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(at);
            return at;
        }
    }

    private void Save(BenchRun run)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            run.File = Path.Combine(Folder, $"diskmark-{run.Drive.TrimEnd('\\', ':')}-{run.Time:yyyyMMdd-HHmmss}.json");
            System.IO.File.WriteAllText(run.File, JsonSerializer.Serialize(run, Settings.JsonStore.Options));
        }
        catch (Exception ex) { Log.Here.Warn("DiskMark: couldn't save the results", ex); }
        lock (gate) runs.Add(run);
    }

    /// <summary>Called every half-minute: starts the scheduled run when it's due (once, if the app wasn't running then).</summary>
    public void Tick()
    {
        if (!settings.ScheduleOn || settings.NextRun is not DateTime due || DateTime.Now < due || Running) return;
        if (settings.RepeatHours <= 0) settings.ScheduleOn = false;
        else
        {
            var next = due;
            while (next <= DateTime.Now) next = next.AddHours(settings.RepeatHours);
            settings.NextRun = next;
        }
        settings.Save();
        var all = Drives.List();
        var drives = settings.ScheduleDrives.Select(d => all.FirstOrDefault(a => a.Root.Equals(d, StringComparison.OrdinalIgnoreCase))).OfType<DriveItem>().ToList();
        if (drives.Count == 0) { Log.Here.Warn("DiskMark: a scheduled run had no drive to test"); Changed?.Invoke(); return; }
        _ = RunAsync(drives, scheduled: true);
    }
}
