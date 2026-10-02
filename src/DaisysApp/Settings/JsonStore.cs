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
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch { /* corrupt settings: fall back to defaults */ }
        return new T();
    }

    public static void Save<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsFolder);
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(value, Options));
        }
        catch { /* non-fatal */ }
    }
}
