using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// (since 4.20.0) A session recorded by SimEffects' Record: its telemetry (a .frames file, <see cref="FrameWriter"/>'s)
/// and the video Video → Recording made of it, with when each began, so they line up. Its json and frames are in
/// <see cref="Folder"/>, the video in Recording's folder.
/// </summary>
public sealed class SimRecording
{
    public string Name { get; set; } = "";
    public Game Game { get; set; }
    /// <summary>The game's version (<see cref="CarNames.Edition"/>: "FH5"…).</summary>
    public string? Edition { get; set; }
    /// <summary>The profile in use.</summary>
    public string Profile { get; set; } = "";
    /// <summary>The last car driven in it.</summary>
    public string Car { get; set; } = "";
    /// <summary>When its telemetry began (its time 0).</summary>
    public DateTime Started { get; set; }
    /// <summary>How long its telemetry is, seconds (0: the app ended while it recorded).</summary>
    public double Seconds { get; set; }
    public string Video { get; set; } = "";
    /// <summary>When the video's first frame was.</summary>
    public DateTime VideoStarted { get; set; }
    /// <summary>Seconds it's moved by hand to line up (+: the telemetry comes later in the video).</summary>
    public double Offset { get; set; }

    /// <summary>Its id: its files' name.</summary>
    [JsonIgnore] public string Id { get; set; } = "";

    [JsonIgnore] public bool HasVideo => Video.Length > 0 && File.Exists(Video);

    /// <summary>Where its telemetry's start is in the video, seconds.</summary>
    [JsonIgnore] public double Shift => (Started - VideoStarted).TotalSeconds + Offset;

    /// <summary>How long it plays: the video's time at the telemetry's end.</summary>
    [JsonIgnore] public double Length => Math.Max(0, Seconds + Shift);

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "SimRecordings");

    [JsonIgnore] public string Frames => Path.Combine(Folder, Id + ".frames");
    [JsonIgnore] private string Json => Path.Combine(Folder, Id + ".json");

    /// <summary>An id of its own for one that begins now.</summary>
    public static string NewId(DateTime at)
    {
        Directory.CreateDirectory(Folder);
        string stamp = at.ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture), id = stamp;
        for (int i = 2; File.Exists(Path.Combine(Folder, id + ".frames")); i++) id = $"{stamp} ({i})";
        return id;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            JsonStore.WriteAtomic(Json, JsonSerializer.Serialize(this, JsonStore.Options));
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Recordings: saving {Id}", ex); }
    }

    /// <summary>Every recording, the newest first.</summary>
    public static List<SimRecording> All()
    {
        var all = new List<SimRecording>();
        if (!Directory.Exists(Folder)) return all;
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<SimRecording>(File.ReadAllText(path), JsonStore.Options) is not { } r) continue;
                r.Id = Path.GetFileNameWithoutExtension(path);
                if (File.Exists(r.Frames)) all.Add(r);
            }
            catch (Exception ex) { Logging.Log.Here.Warn($"Recordings: {Path.GetFileName(path)}: {ex.Message}"); }
        }
        return all.OrderByDescending(r => r.Started).ToList();
    }

    /// <summary>Deletes it, its video too.</summary>
    public void Delete()
    {
        Logging.Log.Here.Info($"Recordings: {Id} ({Name}) deleted{(HasVideo ? $", with {Video}" : "")}");
        try { File.Delete(Json); } catch (Exception ex) { Logging.Log.Here.Warn($"Recordings: deleting {Id}", ex); }
        var files = new[] { Frames, Video };
        Task.Run(async () =>
        {
            foreach (var f in files)
                for (int i = 0; i < 20 && f.Length > 0 && File.Exists(f); i++)
                {
                    // (the video's reader lets go of it a moment later)
                    try { File.Delete(f); }
                    catch (Exception ex)
                    {
                        if (i == 19) Logging.Log.App.Warn($"Recordings: {f} couldn't be deleted: {ex.Message}");
                        await Task.Delay(500);
                    }
                }
        });
    }
}

/// <summary>Saved in %APPDATA%\DaisysApp\SimRecordings.json.</summary>
public sealed class RecordingsSettings
{
    private const string FileName = "SimRecordings";

    /// <summary>The video's volume (0 to 1) and mute.</summary>
    public double Volume { get; set; } = 1;
    public bool Muted { get; set; }
    /// <summary>A recording starts again when it's played to its end.</summary>
    public bool Loop { get; set; }

    private static RecordingsSettings? all;
    public static RecordingsSettings All => all ??= JsonStore.Load<RecordingsSettings>(FileName);
    public void Save() => JsonStore.SaveSoon(FileName, this);
}
