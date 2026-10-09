using System.IO;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming;

public enum OverlayMode { Simple, Medium, Advanced }
public enum OverlayAlign { Left, Right }
public enum CaptureSource { Monitor, Region, App }
public enum RateControl { Cbr, Vbr }
public enum HdrRecording { Sdr, Hdr10 }

/// <summary>Saved in %APPDATA%\DaisysApp\Gaming.json.</summary>
public sealed class GamingSettings
{
    private const string FileName = "Gaming";

    // ---------------------------------------------------------------- overlay
    public bool OverlayVisible { get; set; } = true;
    /// <summary>Only while a game is in front (otherwise whenever it's switched on).</summary>
    public bool OverlayOnlyInGames { get; set; } = true;
    public OverlayMode Mode { get; set; } = OverlayMode.Medium;
    public OverlayAlign Align { get; set; } = OverlayAlign.Left;
    public bool GraphFps { get; set; } = true;
    public bool GraphFrametime { get; set; } = true;
    public bool GraphVram { get; set; } = true;
    public string BackgroundColor { get; set; } = "#000000";
    public double BackgroundOpacity { get; set; } = 0.55;
    public string TextColor { get; set; } = "#FFFFFF";
    public double Scale { get; set; } = 1.0;
    public bool Locked { get; set; }
    /// <summary>Physical-pixel position of the overlay's anchor corner (top-left, or top-right when right-aligned).</summary>
    public int X { get; set; } = 40;
    public int Y { get; set; } = 40;
    public bool PositionSet { get; set; }
    /// <summary>Leave the overlay out of recordings and screenshots.</summary>
    public bool HideFromCapture { get; set; } = true;

    // ---------------------------------------------------------------- recording
    public CaptureSource Source { get; set; } = CaptureSource.Monitor;
    /// <summary>e.g. "\\.\DISPLAY1"; empty = the primary monitor.</summary>
    public string Monitor { get; set; } = "";
    /// <summary>Region in physical desktop pixels.</summary>
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    /// <summary>The program to record (exe name); empty = the game in front when recording starts.</summary>
    public string AppExe { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Profile { get; set; } = "High";
    public List<RecordingProfile> Profiles { get; set; } = RecordingProfile.Defaults();
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public bool RecordCursor { get; set; } = true;
    public HdrRecording Hdr { get; set; } = HdrRecording.Sdr;

    // ---------------------------------------------------------------- hotkeys
    public string? RecordHotkey { get; set; } = "Ctrl+Alt+F9";
    public string? OverlayHotkey { get; set; } = "Ctrl+Alt+F10";
    public string? ModeHotkey { get; set; } = "Ctrl+Alt+F11";

    // ---------------------------------------------------------------- games and history
    /// <summary>Programs seen showing frames full screen (or added), by exe name in lower case.</summary>
    public Dictionary<string, GameEntry> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LogHistory { get; set; } = true;
    public int KeepHistoryMonths { get; set; } = 12;
    /// <summary>The user was added to Performance Log Users and needs to sign out and in again.</summary>
    public bool PermissionPending { get; set; }

    [JsonIgnore]
    public RecordingProfile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Name == Profile) ?? Profiles.FirstOrDefault() ?? RecordingProfile.Defaults()[0];

    [JsonIgnore]
    public string RecordingFolder => string.IsNullOrWhiteSpace(Folder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), AppPaths.DisplayName)
        : Folder;

    public string FriendlyName(string exe) =>
        Games.TryGetValue(exe, out var g) && !string.IsNullOrWhiteSpace(g.Name) ? g.Name : Path.GetFileNameWithoutExtension(exe);

    public static GamingSettings Load()
    {
        var s = JsonStore.Load<GamingSettings>(FileName);
        // JSON loses the comparer
        s.Games = new Dictionary<string, GameEntry>(s.Games ?? new(), StringComparer.OrdinalIgnoreCase);
        if (s.Profiles == null || s.Profiles.Count == 0) s.Profiles = RecordingProfile.Defaults();
        s.Scale = Math.Clamp(s.Scale, 0.5, 4);
        s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0, 1);
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>A program that's been recognised as a game.</summary>
public sealed class GameEntry
{
    /// <summary>The name shown and logged, e.g. "Forza Horizon 6" for forzahorizon6.exe.</summary>
    public string Name { get; set; } = "";
    /// <summary>Treat it as a game: show the overlay and log its frame rates. Off = ignore it.</summary>
    public bool Track { get; set; } = true;
    public DateTime LastSeen { get; set; }
}

/// <summary>A set of recording settings (High, Medium, Low, or the user's own).</summary>
public sealed class RecordingProfile
{
    public string Name { get; set; } = "";
    /// <summary>"H264", "HEVC" or "AV1".</summary>
    public string Codec { get; set; } = "HEVC";
    public int Fps { get; set; } = 60;
    /// <summary>Output height in pixels; 0 = the same as what's recorded.</summary>
    public int Height { get; set; }
    public RateControl RateControl { get; set; } = RateControl.Vbr;
    /// <summary>Average bit rate, Mbit/s.</summary>
    public double BitrateMbps { get; set; } = 80;
    /// <summary>Peak bit rate for VBR, Mbit/s.</summary>
    public double MaxBitrateMbps { get; set; } = 100;
    /// <summary>0 fastest … 100 best quality (the encoder's preset; the encoder chip does the work either way).</summary>
    public int Quality { get; set; } = 100;
    public int KeyframeSeconds { get; set; } = 2;
    public int AudioKbps { get; set; } = 192;

    public RecordingProfile Clone() => (RecordingProfile)MemberwiseClone();

    public static List<RecordingProfile> Defaults() => new()
    {
        new() { Name = "High", Codec = "HEVC", Fps = 60, Height = 0, RateControl = RateControl.Vbr, BitrateMbps = 80, MaxBitrateMbps = 100, Quality = 100, AudioKbps = 192 },
        new() { Name = "Medium", Codec = "HEVC", Fps = 60, Height = 1440, RateControl = RateControl.Vbr, BitrateMbps = 40, MaxBitrateMbps = 50, Quality = 70, AudioKbps = 160 },
        new() { Name = "Low", Codec = "H264", Fps = 30, Height = 1080, RateControl = RateControl.Vbr, BitrateMbps = 15, MaxBitrateMbps = 20, Quality = 50, AudioKbps = 128 },
    };
}
