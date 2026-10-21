using System.IO;
using System.IO.Compression;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The icon library: Material Design Icons (Pictogrammers, pictogrammers.com; Apache 2.0), about 7,000 icons in one
/// style, each a single path on a 24 × 24 grid, so it can be drawn in any colour, here and on any device. Built into the
/// module (Icons/mdi.txt.gz: name, aliases and tags, path, a line each); read the first time it's needed.
/// </summary>
internal static class IconLibrary
{
    public sealed record Icon(string Name, string Words, string Path);

    private static List<Icon>? icons;
    private static Dictionary<string, Icon>? byName;
    private static readonly Dictionary<string, Geometry> geometries = new();
    private static readonly object gate = new();

    public static IReadOnlyList<Icon> All
    {
        get
        {
            lock (gate) return icons ??= Read();
        }
    }

    // (the server reads paths from its own threads, so the first read is locked)
    private static List<Icon> Read()
    {
        var list = new List<Icon>();
        try
        {
            using var stream = typeof(IconLibrary).Assembly.GetManifestResourceStream("mdi.txt.gz");
            if (stream != null)
            {
                using var reader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
                while (reader.ReadLine() is { } line)
                {
                    if (line.StartsWith('#')) continue;
                    var parts = line.Split('\t');
                    if (parts.Length == 3) list.Add(new Icon(parts[0], parts[1], parts[2]));
                }
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Reading the icon library", ex); }
        byName = list.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static Icon? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        _ = All;
        return byName!.TryGetValue(name, out var i) ? i : null;
    }

    /// <summary>The icon's shape (24 × 24), made once and kept (on the UI thread).</summary>
    public static Geometry? GeometryOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (geometries.TryGetValue(name, out var g)) return g;
        if (Find(name) is not { } icon) return null;
        try
        {
            g = Geometry.Parse(icon.Path);
            g.Freeze();
        }
        catch { g = Geometry.Empty; }
        geometries[name] = g;
        return g;
    }

    // the word as a whole part of the name ("car", "car-side") first, then names starting with it ("cart"), then the rest
    private static int Rank(string name, string word) =>
        name == word || name.StartsWith(word + "-") ? 0 : name.Split('-').Contains(word) ? 1 : name.StartsWith(word) ? 2 : name.Contains(word) ? 3 : 4;

    /// <summary>Icons whose name, aliases or tags hold every word searched for (names that start with the first word first).</summary>
    public static List<Icon> Search(string text)
    {
        var words = text.ToLowerInvariant().Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All.ToList();
        return All.Where(i => words.All(w => i.Name.Contains(w) || i.Words.Contains(w)))
            .OrderBy(i => Rank(i.Name, words[0]))
            .ThenBy(i => i.Name.Length)
            .ToList();
    }
}
