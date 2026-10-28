using DaisysApp.Settings;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>Saved in %APPDATA%\DaisysApp\StressTests.json.</summary>
public sealed class StressSettings
{
    private const string FileName = "StressTests";

    /// <summary>The tests ticked (ids from <see cref="Catalog"/>).</summary>
    public List<string> Tests { get; set; } = Catalog.Tests.Where(t => t.Scored).Select(t => t.Id).ToList();

    /// <summary>How long each test runs.</summary>
    public int Seconds { get; set; } = 30;
    /// <summary>Times the whole set runs; 0 = until stopped.</summary>
    public int Loops { get; set; } = 1;
    public StressLogging Logging { get; set; } = StressLogging.Standard;
    public bool StopOnError { get; set; }

    /// <summary>The graphics card the GPU and video tests use (its name; empty = the first).</summary>
    public string Gpu { get; set; } = "";
    /// <summary>The drive the storage test writes its file to ("C:\").</summary>
    public string Drive { get; set; } = "C:\\";
    public int StorageMiB { get; set; } = 1024;
    /// <summary>How much of the free memory the memory integrity test fills.</summary>
    public int MemoryPercent { get; set; } = 60;
    /// <summary>How much of the free video memory the VRAM test fills.</summary>
    public int VramPercent { get; set; } = 85;

    /// <summary>The runs last picked in the comparison window (files).</summary>
    public string? CompareA { get; set; }
    public string? CompareB { get; set; }
    /// <summary>Other PCs' runs opened for comparing (files), newest first.</summary>
    public List<string> Imported { get; set; } = [];

    public static StressSettings Load() => JsonStore.Load<StressSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
