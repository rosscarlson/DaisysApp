using DaisysApp.Settings;

namespace DaisysApp.Applets.Support.Logs;

/// <summary>Saved in %APPDATA%\DaisysApp\Logs.json: the open tabs and each one's time range and level filter.</summary>
public sealed class LogsSettings
{
    private const string FileName = "Logs";

    /// <summary>The keys of the open tabs, in order (see <see cref="LogSources.Create"/>).</summary>
    public List<string> Open { get; set; } = ["app"];

    /// <summary>The key of the tab showing last.</summary>
    public string? Selected { get; set; }

    /// <summary>Hours back each tab shows, by key.</summary>
    public Dictionary<string, int> RangeHours { get; set; } = new();

    /// <summary>Each tab's level filter (0 everything … 3 errors only), by key.</summary>
    public Dictionary<string, int> LevelFilter { get; set; } = new();

    /// <summary>Each tab's sort (a column's header menu), by key; newest first when missing.</summary>
    public Dictionary<string, LogSort> Sort { get; set; } = new();

    /// <summary>Each tab's hidden values ("Level": level names, "Source": sources), by key, then by column.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> Hidden { get; set; } = new();

    public static LogsSettings Load() => JsonStore.Load<LogsSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>A tab's sort: the column ("Time", "Level", "Source" or "Event") and whether it's descending.</summary>
public sealed class LogSort
{
    public string Column { get; set; } = "Time";
    public bool Descending { get; set; } = true;
}
