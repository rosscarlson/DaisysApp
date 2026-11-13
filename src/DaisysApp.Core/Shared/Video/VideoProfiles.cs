using System.IO;
using System.Text.Json;
using DaisysApp.Logging;
using DaisysApp.Settings;

namespace DaisysApp.Shared.Video;

public enum RateControl { Cbr, Vbr }

/// <summary>A set of video settings (High, Medium, Low, 1080p, 720p, or the user's own): Video → Recording records with
/// one, Plex → Scanner transcodes with one.</summary>
public sealed class RecordingProfile
{
    public string Name { get; set; } = "";
    /// <summary>"H264", "HEVC" or "AV1".</summary>
    public string Codec { get; set; } = "HEVC";
    /// <summary>Recording's frame rate (a transcode keeps the source's).</summary>
    public int Fps { get; set; } = 60;
    /// <summary>Output height in pixels; 0 = the same as the source.</summary>
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
        new() { Name = "1080p", Codec = "HEVC", Fps = 30, Height = 1080, RateControl = RateControl.Vbr, BitrateMbps = 4, MaxBitrateMbps = 6, Quality = 100, AudioKbps = 192 },
        new() { Name = "720p", Codec = "HEVC", Fps = 30, Height = 720, RateControl = RateControl.Vbr, BitrateMbps = 1, MaxBitrateMbps = 2, Quality = 100, AudioKbps = 128 },
    };
}

/// <summary>
/// (since Core 0.64, 4.16.0) The app's one list of video profiles, saved in %APPDATA%\DaisysApp\VideoProfiles.json:
/// edited in Video → Recording's settings, used by its recordings and by Plex → Scanner's transcodes. Until 4.16.0 the
/// list was Video's own (Gaming.json's Profiles), read from there the first time. UI thread.
/// </summary>
public static class VideoProfiles
{
    private const string FileName = "VideoProfiles";
    /// <summary>Profiles added to the defaults since the list was Video's, added to a list read from Gaming.json.</summary>
    private static readonly string[] Added = ["1080p", "720p"];

    private sealed class Store
    {
        public List<RecordingProfile> Profiles { get; set; } = [];
    }

    private static Store? store;
    private static string saved = "";

    /// <summary>Raised after the list changed and was saved.</summary>
    public static event Action? Changed;

    public static List<RecordingProfile> All => Load().Profiles;

    public static RecordingProfile? Find(string? name) => All.FirstOrDefault(p => p.Name == name);

    private static Store Load()
    {
        if (store != null) return store;
        if (JsonStore.Exists(FileName)) store = JsonStore.Load<Store>(FileName);
        else
        {
            store = new Store { Profiles = FromVideo() ?? [] };
            if (store.Profiles.Count > 0)
                foreach (var d in RecordingProfile.Defaults().Where(d => Added.Contains(d.Name) && store.Profiles.All(p => p.Name != d.Name)))
                    store.Profiles.Add(d);
        }
        if (store.Profiles == null || store.Profiles.Count == 0) store.Profiles = RecordingProfile.Defaults();
        if (!JsonStore.Exists(FileName)) JsonStore.Save(FileName, store);
        saved = JsonSerializer.Serialize(store, JsonStore.Options);
        return store;
    }

    /// <summary>Video's list from before 4.16.0, if there is one.</summary>
    private static List<RecordingProfile>? FromVideo()
    {
        try
        {
            string path = JsonStore.PathFor("Gaming");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("Profiles", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.Deserialize<List<RecordingProfile>>(JsonStore.Options)
                : null;
        }
        catch (Exception ex) { Log.Here.Warn($"Video profiles: Gaming.json's couldn't be read: {ex.Message}"); return null; }
    }

    /// <summary>Saves the list if it changed, and says so.</summary>
    public static void Save()
    {
        var s = Load();
        string json = JsonSerializer.Serialize(s, JsonStore.Options);
        if (json == saved) return;
        saved = json;
        JsonStore.Save(FileName, s);
        Changed?.Invoke();
    }

    /// <summary>Back to the defaults.</summary>
    public static void Reset()
    {
        Load().Profiles = RecordingProfile.Defaults();
        Save();
    }
}
