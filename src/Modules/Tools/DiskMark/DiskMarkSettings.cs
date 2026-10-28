using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>Saved in %APPDATA%\DaisysApp\DiskMark.json.</summary>
public sealed class DiskMarkSettings
{
    private const string FileName = "DiskMark";

    /// <summary>The drive picked on the left ("C:\").</summary>
    public string Drive { get; set; } = "C:\\";

    public string Profile { get; set; } = Profiles.Default;
    public List<TestDef> CustomTests { get; set; } = Profiles.DefaultTests();
    public BenchMode Mode { get; set; } = BenchMode.ReadWrite;

    /// <summary>Times each test runs (the best is kept), 1–9.</summary>
    public int Count { get; set; } = 5;
    /// <summary>The test file's size.</summary>
    public int SizeMiB { get; set; } = 1024;
    /// <summary>How long each run measures.</summary>
    public int Seconds { get; set; } = 5;
    /// <summary>The pause between runs (lets the drive cool down and its cache empty).</summary>
    public int IntervalSeconds { get; set; } = 5;
    /// <summary>Write zeros rather than random bytes (drives that compress score higher).</summary>
    public bool Zeros { get; set; }

    /// <summary>"GB/s" or "MB/s" (Settings → Tools → DiskMark).</summary>
    public string Unit { get; set; } = "GB/s";
    /// <summary>The result bars on a logarithmic scale, so a random 4K bar still shows beside a sequential one.</summary>
    public bool LogScale { get; set; }

    /// <summary>Which test the history graph shows ("SEQ1M Q8T1").</summary>
    public string? HistoryTest { get; set; }

    public bool ScheduleOn { get; set; }
    /// <summary>When the next scheduled run is due.</summary>
    public DateTime? NextRun { get; set; }
    /// <summary>Hours between scheduled runs; 0 = once.</summary>
    public int RepeatHours { get; set; } = 24;
    /// <summary>The drives a scheduled run tests ("C:\", "D:\").</summary>
    public List<string> ScheduleDrives { get; set; } = [];

    public static DiskMarkSettings Load() => JsonStore.Load<DiskMarkSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);

    public List<TestDef> Tests() => Profiles.Tests(Profile, CustomTests);

    /// <summary>Roughly how long a run takes on one drive (the file's writing not counted).</summary>
    public TimeSpan Estimate()
    {
        int parts = Tests().Count * (Mode == BenchMode.ReadWrite ? 2 : 1) * Math.Max(1, Count);
        return TimeSpan.FromSeconds(parts * Seconds + Math.Max(0, parts - 1) * IntervalSeconds);
    }
}
