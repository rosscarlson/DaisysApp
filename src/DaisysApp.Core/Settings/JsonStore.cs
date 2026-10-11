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
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsFolder);
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(value, Options));
            Logging.Log.App.Debug($"Settings saved: {name}.json");
        }
        catch (Exception ex) { Logging.Log.App.Warn($"Couldn't save the settings file {name}.json", ex); } // non-fatal
    }
}
