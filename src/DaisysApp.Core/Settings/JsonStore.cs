using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisysApp.Settings;

/// <summary>
/// Loads and saves one settings object per JSON file in <see cref="AppPaths.SettingsFolder"/>.
/// The shell uses "settings"; each tool keeps its own file, so adding a tool never touches shared settings.
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PathFor(string name) => Path.Combine(AppPaths.SettingsFolder, name + ".json");

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static T Load<T>(string name) where T : new()
    {
        try
        {
            string path = PathFor(name);
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                Logging.Log.App.Debug($"Settings loaded: {name}.json ({json.Length} characters)");
                return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
            }
            Logging.Log.App.Debug($"Settings: no {name}.json yet, using the defaults");
        }
        catch (Exception ex)
        {
            // corrupt settings: fall back to defaults
            Logging.Log.App.Warn($"Settings file {name}.json couldn't be read, so the defaults are used", ex);
        }
        return new T();
    }

    public static void Save<T>(string name, T value)
    {
        lock (soon) if (soon.Remove(name, out var waiting)) waiting.Cancel();
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsFolder);
            WriteAtomic(PathFor(name), JsonSerializer.Serialize(value, Options));
            Logging.Log.App.Debug($"Settings saved: {name}.json");
        }
        catch (Exception ex) { Logging.Log.App.Warn($"Couldn't save the settings file {name}.json", ex); } // non-fatal
    }

    /// <summary>
    /// Writes the text to a .tmp beside the file, then puts it in the file's place, so a crash or a full disk mid-write
    /// leaves the old file whole (since Core 0.55: <see cref="Save"/> wrote in place before).
    /// </summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    private static readonly Dictionary<string, Shared.Debounce> soon = new();
    private static readonly Dictionary<string, object?> latest = new();

    static JsonStore() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();

    /// <summary>
    /// Saves the settings <paramref name="ms"/> after the last call for this name, so a slider dragged or text typed
    /// writes the file once, not on every change. Saved at exit too, if still waiting (and by <see cref="Flush"/>).
    /// Call it on the thread that changes <paramref name="value"/> (the UI thread, usually): it's written there.
    /// </summary>
    public static void SaveSoon<T>(string name, T value, int ms = 600)
    {
        Shared.Debounce d;
        lock (soon)
        {
            latest[name] = value;
            if (!soon.TryGetValue(name, out d!))
                soon[name] = d = new Shared.Debounce(ms, () =>
                {
                    object? v;
                    lock (soon) latest.Remove(name, out v);
                    Save(name, (T)v!);
                });
        }
        d.Poke();
    }

    /// <summary>Writes every save that's waiting (the app closing).</summary>
    public static void Flush()
    {
        List<Shared.Debounce> waiting;
        lock (soon) waiting = soon.Values.ToList();
        foreach (var d in waiting) try { d.Flush(); } catch { }
    }
}
