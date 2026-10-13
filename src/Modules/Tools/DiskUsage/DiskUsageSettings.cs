using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>Saved in %APPDATA%\DaisysApp\DiskUsage.json.</summary>
public sealed class DiskUsageSettings
{
    private const string FileName = "DiskUsage";

    /// <summary>The drive or folder scanned last.</summary>
    public string Path { get; set; } = "C:\\";

    /// <summary>Read an NTFS drive's file table directly (much faster; needs administrator permission, which Windows asks for).</summary>
    public bool UseFileTable { get; set; } = true;

    /// <summary>The treemap under the tree (it can be hidden for more rows).</summary>
    public bool ShowTreemap { get; set; } = true;

    public double TreemapHeight { get; set; } = 300;
    public double TypesWidth { get; set; } = 360;

    public static DiskUsageSettings Load() => JsonStore.Load<DiskUsageSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
