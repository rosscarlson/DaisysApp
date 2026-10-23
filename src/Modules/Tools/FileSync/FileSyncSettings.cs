using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>Mirror: the destination ends up exactly like the source (extra files deleted). Update: new and changed files copied, nothing deleted.</summary>
public enum SyncMode { Mirror, Update }

/// <summary>Before 0.41: how two files with the same name were compared (now <see cref="SyncJob.CompareSize"/> and the others).</summary>
public enum CompareBy { TimeAndSize, Content }

/// <summary>One sync: two folders, which way, and how.</summary>
public sealed class SyncJob
{
    /// <summary>Folders and files no sync touches unless the user changes it (Windows' own, and temporary files).</summary>
    public const string DefaultExcludes = "Thumbs.db; desktop.ini; ~$*; *.tmp; $RECYCLE.BIN\\; System Volume Information\\";

    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public SyncMode Mode { get; set; } = SyncMode.Mirror;

    // what makes two files with the same name (and folder) the same; with none of these, being on both sides is enough

    /// <summary>The same size.</summary>
    public bool CompareSize { get; set; } = true;

    /// <summary>The same time (within 2 seconds, as FAT drives keep times).</summary>
    public bool CompareTime { get; set; } = true;

    /// <summary>The same contents, read byte by byte (only files of the same size are read).</summary>
    public bool CompareContent { get; set; }

    /// <summary>Before 0.41's choices: Content was size and contents, without the time. Read once, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompareBy? CompareBy
    {
        get => null;
        set { if (value == FileSync.CompareBy.Content) { CompareSize = true; CompareTime = false; CompareContent = true; } }
    }

    /// <summary>Files and folders the sync deletes go to the Recycle Bin (where the destination has one).</summary>
    public bool RecycleBin { get; set; } = true;

    /// <summary>Names or paths to leave alone, separated by ";" (wildcards * and ?; a trailing \ for folders only).</summary>
    public string Excludes { get; set; } = DefaultExcludes;

    public SyncJob Clone() => (SyncJob)MemberwiseClone();

    public bool SameAs(SyncJob o) => Source == o.Source && Destination == o.Destination && Mode == o.Mode && CompareSize == o.CompareSize && CompareTime == o.CompareTime && CompareContent == o.CompareContent && RecycleBin == o.RecycleBin && Excludes == o.Excludes;
}

/// <summary>A sync that ran, for the list of recent ones.</summary>
public sealed class SyncRun
{
    public DateTime Started { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public SyncMode Mode { get; set; }
    public int Copied { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Errors { get; set; }
    /// <summary>Its problems, a line each (the first 50).</summary>
    public List<string> Problems { get; set; } = new();
    public long Bytes { get; set; }
    public double Seconds { get; set; }
    /// <summary>"done", "stopped" or "failed".</summary>
    public string Result { get; set; } = "done";
}

/// <summary>Saved in %APPDATA%\DaisysApp\FileSync.json.</summary>
public sealed class FileSyncSettings
{
    private const string FileName = "FileSync";

    /// <summary>What's on the page now.</summary>
    public SyncJob Current { get; set; } = new();

    /// <summary>Syncs saved by name (Save as…), to pick again or run from the tray.</summary>
    public List<SyncJob> Saved { get; set; } = new();

    public List<string> RecentSources { get; set; } = new();
    public List<string> RecentDestinations { get; set; } = new();

    /// <summary>The last syncs that ran, newest first.</summary>
    public List<SyncRun> History { get; set; } = new();

    public static void Remember(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        list.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
    }

    public static FileSyncSettings Load() => JsonStore.Load<FileSyncSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
