using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Resizer;

/// <summary>A saved window size/position for one application (a port of Resize Rabbit's profile).</summary>
public sealed class ResizeProfile
{
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Profile";
    /// <summary>Executable name, e.g. "RRRE64.exe" (matched case-insensitively, with or without ".exe").</summary>
    public string ProcessName { get; set; } = "";
    /// <summary>Applied automatically when the process starts (needs the process watcher).</summary>
    public bool Auto { get; set; }
    /// <summary>Milliseconds to wait after the process starts before applying automatically.</summary>
    public int Delay { get; set; }
    /// <summary>Null = keep the window's current size, only move it.</summary>
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int WindowPosX { get; set; }
    public int WindowPosY { get; set; }
    public bool RemoveBorders { get; set; }
    /// <summary>Only with <see cref="RemoveBorders"/>: push a Store/UWP game's leftover title bar above the screen.</summary>
    public bool ShiftTitlebarOffscreen { get; set; }
    /// <summary>Global hotkey, e.g. "Ctrl+Alt+F1". May be shared by several profiles.</summary>
    public string? Shortcut { get; set; }
    /// <summary>Null = top level; otherwise a member of that group.</summary>
    public Guid? GroupUuid { get; set; }
    /// <summary>Position among its siblings (top level, or within its group).</summary>
    public int Order { get; set; }

    public ResizeProfile Clone() => (ResizeProfile)MemberwiseClone();
}

/// <summary>A named set of profiles that can share a hotkey (applies every running member).</summary>
public sealed class ResizeGroup
{
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Group";
    public string? Shortcut { get; set; }
    public bool Collapsed { get; set; }
    public int Order { get; set; }

    public ResizeGroup Clone() => (ResizeGroup)MemberwiseClone();
}

/// <summary>Everything the Resizer keeps, in %APPDATA%\DaisysApp\Resizer.json.</summary>
public sealed class ResizerData
{
    private const string FileName = "Resizer";

    /// <summary>Apply profiles marked Automatic when their program starts.</summary>
    public bool ProcessWatcherEnabled { get; set; }
    /// <summary>How often to look for newly started programs.</summary>
    public int PollRateMs { get; set; } = 1000;
    public List<ResizeProfile> Profiles { get; set; } = new();
    public List<ResizeGroup> Groups { get; set; } = new();

    public static ResizerData Load()
    {
        if (!JsonStore.Exists(FileName))
        {
            // first run: bring over Resize Rabbit's profiles, groups and watcher settings if it's installed
            var data = new ResizerData();
            if (Legacy.Import(data, Legacy.RabbitFolder, includeSettings: true) > 0) data.Save();
            return data;
        }
        return JsonStore.Load<ResizerData>(FileName);
    }

    public void Save() => JsonStore.Save(FileName, this);

    /// <summary>Reads profiles (and groups) saved by Resize Rabbit or Resize Raccoon, whose JSON uses snake_case names.</summary>
    public static class Legacy
    {
        public static string RabbitFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.jasbone.resizerabbit");

        public static string RaccoonFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.resizeraccoon.dev");

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        private sealed class LegacySettings
        {
            public bool ProcessWatcherEnabled { get; set; }
            public int PollRate { get; set; } = 1000;
        }

        /// <summary>Adds profiles and groups not already present (matched by id). Returns how many profiles were added.</summary>
        public static int Import(ResizerData data, string folder, bool includeSettings = false)
        {
            int added = 0;
            try
            {
                var have = data.Groups.Select(g => g.Uuid).ToHashSet();
                foreach (var g in Read<ResizeGroup>(Path.Combine(folder, "groups.json")))
                    if (have.Add(g.Uuid)) data.Groups.Add(g);

                var haveProfiles = data.Profiles.Select(p => p.Uuid).ToHashSet();
                foreach (var p in Read<ResizeProfile>(Path.Combine(folder, "profiles.json")))
                {
                    if (!haveProfiles.Add(p.Uuid)) continue;
                    if (p.GroupUuid is Guid gid && data.Groups.All(g => g.Uuid != gid)) p.GroupUuid = null;
                    data.Profiles.Add(p);
                    added++;
                }

                string settingsFile = Path.Combine(folder, "user_settings.json");
                if (includeSettings && File.Exists(settingsFile) &&
                    JsonSerializer.Deserialize<LegacySettings>(File.ReadAllText(settingsFile), Options) is { } s)
                {
                    data.ProcessWatcherEnabled = s.ProcessWatcherEnabled;
                    if (s.PollRate >= 100) data.PollRateMs = s.PollRate;
                }
            }
            catch { /* unreadable legacy data: import what we could */ }
            return added;
        }

        public static bool HasData(string folder) => File.Exists(Path.Combine(folder, "profiles.json"));

        private static List<T> Read<T>(string file)
        {
            if (!File.Exists(file)) return new();
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), Options) ?? new();
        }
    }
}
