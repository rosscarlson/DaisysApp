using System.IO;
using System.Text.Json.Nodes;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Files dropped on the box (since 3.26.7): sent as their paths, which the AI reads itself. On a session on another
/// PC, the files go with the message (`files` on `send`, [{name, data}]) and that PC keeps them in
/// %LOCALAPPDATA%\DaisysApp\AI attachments\&lt;session&gt;\&lt;time&gt;\ (two weeks), and sends those paths.
/// </summary>
public static class Attachments
{
    /// <summary>The most a message's files may be on the wire (a frame is at most 64 MB, base64 adds a third).</summary>
    public const long MaxBytes = 40L << 20;

    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaisysApp", "AI attachments");

    /// <summary>The line the user's bubble shows: 📎 and the files' names.</summary>
    public static string Line(IReadOnlyList<string> paths) => "\U0001F4CE " + string.Join(", ", paths.Select(Path.GetFileName));

    /// <summary>What's added to the message the AI gets: where the files are.</summary>
    public static string Note(IReadOnlyList<string> paths) =>
        "Files attached (read them from these paths):\n" + string.Join("\n", paths.Select(p => "- " + p));

    /// <summary>The files as JSON for the other PC; null (and why) if they're too big or can't be read.</summary>
    public static JsonArray? Json(IReadOnlyList<string> paths, out string? problem)
    {
        problem = null;
        try
        {
            long total = paths.Sum(p => new FileInfo(p).Length);
            if (total > MaxBytes) { problem = F("The files are {0} MB; at most {1} MB go to another PC.", total >> 20, MaxBytes >> 20); return null; }
            return new JsonArray(paths.Select(p => (JsonNode)new JsonObject { ["name"] = Path.GetFileName(p), ["data"] = Convert.ToBase64String(File.ReadAllBytes(p)) }).ToArray());
        }
        catch (Exception ex) { problem = ex.Message; return null; }
    }

    /// <summary>Files sent from another PC, kept here for the session: their paths.</summary>
    public static List<string> Save(string session, JsonNode? files)
    {
        var paths = new List<string>();
        if (files is not JsonArray a || a.Count == 0) return paths;
        Clean();
        string dir = Path.Combine(Root, Safe(session), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        foreach (var f in a.OfType<JsonObject>())
        {
            string name = Safe(f["name"]?.GetValue<string>() ?? "file");
            string path = Path.Combine(dir, name);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
            File.WriteAllBytes(path, Convert.FromBase64String(f["data"]?.GetValue<string>() ?? ""));
            paths.Add(path);
        }
        Logging.Log.Here.Info($"Files from another PC for session {session}: {string.Join(", ", paths)}");
        return paths;
    }

    private static string Safe(string name)
    {
        name = Path.GetFileName(name);
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().Length == 0 ? "file" : name;
    }

    /// <summary>Folders of files more than two weeks old go.</summary>
    private static void Clean()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            foreach (var d in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Where(d => Path.GetFileName(d).Length == 15))
                if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-14)) Directory.Delete(d, true);
        }
        catch (Exception ex) { Logging.Log.Here.Debug($"Cleaning AI attachments: {ex.Message}"); }
    }
}
