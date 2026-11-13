using System.IO;
using System.Text.Json.Serialization;
using DaisysApp.Settings;
using DaisysApp.Shared.Video;

namespace DaisysApp.Applets.Video.Gaming;

public enum OverlayMode { Simple, Medium, Advanced }
public enum OverlayAlign { Left, Right }
public enum CaptureSource { Monitor, Region, App }
public enum HdrRecording { Sdr, Hdr10 }

/// <summary>Saved in %APPDATA%\DaisysApp\Gaming.json.</summary>
public sealed class GamingSettings
{
    private const string FileName = "Gaming";

    // ---------------------------------------------------------------- overlay
    /// <summary>Off on a new install (0.22): switched on in Video → Overlay, or in the setup wizard's Gaming step.</summary>
    public bool OverlayVisible { get; set; }
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
    /// <summary>Advanced: the game's upscaling and frame generation libraries (DLSS, FSR, XeSS…).</summary>
    public bool ShowGameTech { get; set; } = true;
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
    /// <summary>(since 4.16.0) Core's list, shared with Plex → Scanner (Gaming.json's own until then).</summary>
    [JsonIgnore]
    public List<RecordingProfile> Profiles => VideoProfiles.All;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public bool RecordCursor { get; set; } = true;
    public HdrRecording Hdr { get; set; } = HdrRecording.Sdr;
    /// <summary>The recorded picture's shape: as captured, or the middle of it in 32:9, 21:9, 16:9, 4:3 or 9:16.</summary>
    public OutputAspect OutputAspect { get; set; } = OutputAspect.Captured;

    // ---------------------------------------------------------------- cameras and the virtual webcam
    /// <summary>The cameras laid over recordings and the virtual webcam (Record → Add Camera), bottom one first.</summary>
    public List<CameraPlacement> Cameras { get; set; } = new();
    /// <summary>The virtual webcam's sound: recorded from this playback device as it plays (the game's)…</summary>
    public string? WebcamAudioFrom { get; set; }
    /// <summary>…and played into this one (a virtual cable, whose other end Discord takes as its microphone). Null: no sound.</summary>
    public string? WebcamAudioTo { get; set; }
    /// <summary>The virtual webcam was running when the app closed (stopped by hand: false): it starts again, without sound.</summary>
    public bool WebcamOn { get; set; }

    // ---------------------------------------------------------------- hotkeys
    public string? RecordHotkey { get; set; } = "Ctrl+Alt+F9";
    /// <summary>Start / stop a recording of the monitor, the region or the game or program, whatever Record's choice
    /// is (Record's shortcuts window). None to start.</summary>
    public string? RecordMonitorHotkey { get; set; }
    public string? RecordRegionHotkey { get; set; }
    public string? RecordAppHotkey { get; set; }
    public string? OverlayHotkey { get; set; } = "Ctrl+Alt+F10";
    public string? ModeHotkey { get; set; } = "Ctrl+Alt+F11";
    public string? BenchmarkHotkey { get; set; } = "Ctrl+Alt+F12";
    /// <summary>Mutes and unmutes the virtual webcam: a key combination or a controller / button box button.</summary>
    public string? WebcamMuteHotkey { get; set; } = "Ctrl+Alt+F8";
    /// <summary>Starts and stops the virtual webcam (without sound: Discord keeps your microphone). None to start.</summary>
    public string? WebcamHotkey { get; set; }
    /// <summary>Takes every camera off the picture and puts them back (Cameras off), the webcam still running. None to start.</summary>
    public string? CamerasOffHotkey { get; set; }
    /// <summary>A green Webcam bubble in the title bar while the virtual webcam runs.</summary>
    public bool WebcamTitleBadge { get; set; } = true;

    // ---------------------------------------------------------------- benchmark
    /// <summary>A benchmark runs for <see cref="BenchmarkSeconds"/> (otherwise until it's stopped).</summary>
    public bool BenchmarkTimed { get; set; } = true;
    public int BenchmarkSeconds { get; set; } = 60;

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
        // (4.16.0) Core reads the profiles from Gaming.json once, before this saves it without them
        _ = VideoProfiles.All;
        var s = JsonStore.Load<GamingSettings>(FileName);
        s.Cameras = (s.Cameras ?? new()).Where(c => !string.IsNullOrEmpty(c.DeviceId)).Take(CameraPlacement.Max).ToList();
        foreach (var c in s.Cameras) c.ClampCrop();
        // JSON loses the comparer
        s.Games = new Dictionary<string, GameEntry>(s.Games ?? new(), StringComparer.OrdinalIgnoreCase);
        s.Scale = Math.Clamp(s.Scale, 0.5, 4);
        s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0, 1);
        s.BenchmarkSeconds = Math.Clamp(s.BenchmarkSeconds, 5, 3600);
        return s;
    }

    /// <summary>Saves; the games list is locked, since the background tick adds to it.</summary>
    public void Save()
    {
        lock (Games) JsonStore.Save(FileName, this);
        VideoProfiles.Save();
    }
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
