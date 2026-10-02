using System.IO;
using System.Text.Json;
using DaisysApp.Settings;

namespace DaisysApp.Tools.UsbMonitor;

public sealed class UsbMonitorSettings
{
    private const string FileName = "UsbMonitor";

    /// <summary>Write every event to a per-launch log file in <see cref="AppPaths.LogFolder"/>.</summary>
    public bool LogToFile { get; set; } = true;

    public List<int> ColumnWidths { get; set; } = new();
    public int SortColumn { get; set; } = 0;
    public bool SortAscending { get; set; } = false;

    public static UsbMonitorSettings Load()
    {
        if (!JsonStore.Exists(FileName))
        {
            try
            {
                // one-time import from the standalone app this tool started as (USB Mon): column widths and sort order
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USBMon", "settings.json");
                if (File.Exists(legacy) &&
                    JsonSerializer.Deserialize<UsbMonitorSettings>(File.ReadAllText(legacy), JsonStore.Options) is { } imported)
                {
                    imported.Save();
                    return imported;
                }
            }
            catch { /* unreadable legacy settings: start fresh */ }
        }
        return JsonStore.Load<UsbMonitorSettings>(FileName);
    }

    public void Save() => JsonStore.Save(FileName, this);
}
