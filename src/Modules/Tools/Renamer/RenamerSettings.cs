using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Renamer;

/// <summary>Saved in %APPDATA%\DaisysApp\Renamer.json.</summary>
public sealed class RenamerSettings
{
    private const string FileName = "Renamer";

    /// <summary>The folder listed last.</summary>
    public string Folder { get; set; } = "";
    public List<string> RecentFolders { get; set; } = new();

    // what the list shows
    /// <summary>Which files, e.g. "*.jpg; *.png" ("*" for all).</summary>
    public string Mask { get; set; } = "*";
    public bool Subfolders { get; set; }
    public bool ShowFiles { get; set; } = true;
    public bool ShowFolders { get; set; }
    public bool ShowHidden { get; set; }

    /// <summary>The panels as they were left.</summary>
    public RenameOptions Options { get; set; } = new();

    /// <summary>The panels' height under the list.</summary>
    public double PanelsHeight { get; set; } = 330;

    /// <summary>The last rename (for Undo), and when.</summary>
    public List<RenamePair> LastRename { get; set; } = new();
    public DateTime? LastRenameAt { get; set; }

    public static RenamerSettings Load() => JsonStore.Load<RenamerSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
