using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DaisysApp;

/// <summary>
/// Translations. Each module has a lang folder: the main window's is lang\ next to DaisysApp.exe, and each applet's is
/// in its folder under modules\ (e.g. modules\Performance\lang\). It holds one JSON file per language: es.json,
/// fr.json… Each file maps the English text to
/// its translation ({"Run now": "Ejecutar ahora"}), plus "_language" for the name to show in Settings. en.json lists
/// every text, so a new language is that file copied to the language's code (de.json) with the values translated.
/// Anything missing from a file stays in English.
///
/// Code wraps its text in <see cref="T"/> (or F for text with values in it, P for a count); the module is worked out
/// from the source file's folder. XAML uses {l:Tr 'text'} (<see cref="TrExtension"/>). tools/strings.py collects
/// them all into the en.json files.
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string ShellModule = "Shell";

    private static readonly Dictionary<string, Dictionary<string, string>> byModule = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> all = new();
    private static readonly Dictionary<string, string> moduleOfPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The language in use ("en", "es"…).</summary>
    public static string Language { get; private set; } = English;

    public static bool IsEnglish => Language == English;

    /// <summary>The folder DaisysApp.exe is in (the main window's lang folder is here).</summary>
    public static string Root => AppContext.BaseDirectory;

    /// <summary>The applets' folders, each with its own lang folder.</summary>
    public static string ModulesFolder => Path.Combine(Root, "modules");

    /// <summary>Every lang folder there is, with its module's name.</summary>
    private static IEnumerable<(string Module, string Dir)> LangFolders()
    {
        string shell = Path.Combine(Root, "lang");
        if (Directory.Exists(shell)) yield return (ShellModule, shell);
        if (!Directory.Exists(ModulesFolder)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(ModulesFolder))
        {
            string lang = Path.Combine(dir, "lang");
            if (Directory.Exists(lang)) yield return (Path.GetFileName(dir), lang);
        }
    }

    /// <summary>Loads a language's files from every module folder. Call once, before any window is created.</summary>
    public static void Init(string? language)
    {
        Language = string.IsNullOrWhiteSpace(language) ? English : language.Trim().ToLowerInvariant();
        byModule.Clear();
        all.Clear();
        if (IsEnglish) return;
        foreach (var (module, dir) in LangFolders())
        {
            string file = Path.Combine(dir, Language + ".json");
            if (!File.Exists(file) || Read(file) is not { } dict) continue;
            byModule[module] = dict;
            foreach (var (k, v) in dict) all.TryAdd(k, v);
        }
        if (all.Count == 0) Language = English; // nothing found: stay in English
    }

    private static Dictionary<string, string>? Read(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var d = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                if (!p.Name.StartsWith('_') && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v)
                    d[p.Name] = v;
            return d;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Reading translation " + file, ex);
            return null;
        }
    }

    /// <summary>The languages there are files for (English always), as (code, name in that language).</summary>
    public static List<(string Code, string Name)> Available()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { English };
        try
        {
            foreach (var (_, dir) in LangFolders())
                foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
                    codes.Add(Path.GetFileNameWithoutExtension(f).ToLowerInvariant());
        }
        catch { }
        return codes.Select(c => (c, NameOf(c))).OrderBy(l => l.Item1 == English ? "" : l.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A language's own name: "_language" in its Shell file, else Windows' name for the code, else the code.</summary>
    private static string NameOf(string code)
    {
        try
        {
            string f = Path.Combine(Root, "lang", code + ".json");
            if (File.Exists(f))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("_language", out var n) && n.GetString() is { Length: > 0 } name) return name;
            }
        }
        catch { }
        try
        {
            var c = CultureInfo.GetCultureInfo(code);
            if (!c.EnglishName.StartsWith("Unknown", StringComparison.Ordinal))
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(c.NativeName);
        }
        catch { }
        return code;
    }

    // ---------------------------------------------------------------- code

    /// <summary>
    /// Stands between the real arguments and the compiler-filled file path, so a text argument can never land in the
    /// path by mistake (F("{0} {1}", a, "b") would otherwise pick the one-value overload with "b" as the path).
    /// </summary>
    public readonly struct End;

    /// <summary>The translation of <paramref name="text"/> (the English text) for the calling file's module.</summary>
    public static string T(string text, End _ = default, [CallerFilePath] string file = "") => IsEnglish ? text : Lookup(text, ModuleOf(file));

    /// <summary>
    /// The translation of a format string ({0}, {1:0.0}…), filled in. A translation can move the placeholders around
    /// but must keep them all.
    /// </summary>
    public static string F(string format, object? a, End _ = default, [CallerFilePath] string file = "") => Format(format, file, a);
    public static string F(string format, object? a, object? b, End _ = default, [CallerFilePath] string file = "") => Format(format, file, a, b);
    public static string F(string format, object? a, object? b, object? c, End _ = default, [CallerFilePath] string file = "") => Format(format, file, a, b, c);
    public static string F(string format, object? a, object? b, object? c, object? d, End _ = default, [CallerFilePath] string file = "") => Format(format, file, a, b, c, d);
    public static string F(string format, object? a, object? b, object? c, object? d, object? e, End _ = default, [CallerFilePath] string file = "") => Format(format, file, a, b, c, d, e);

    /// <summary>A count with the singular or plural text ({0} is the count): P(n, "{0} profile", "{0} profiles").</summary>
    public static string P(long n, string one, string many, End _ = default, [CallerFilePath] string file = "") => Format(n == 1 ? one : many, file, n);

    private static string Format(string format, string file, params object?[] args)
    {
        string f = IsEnglish ? format : Lookup(format, ModuleOf(file));
        try { return string.Format(CultureInfo.CurrentCulture, f, args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, format, args); } // a broken translation
    }

    private static string Lookup(string text, string module) =>
        byModule.TryGetValue(module, out var d) && d.TryGetValue(text, out var v) ? v
        : byModule.TryGetValue(ShellModule, out var s) && s.TryGetValue(text, out v) ? v
        : all.TryGetValue(text, out v) ? v
        : text;

    /// <summary>
    /// The module a source file belongs to: its folder under src\Modules\ (which is also its folder under modules\ next
    /// to the exe), otherwise the app itself (the main window, Settings and DaisysApp.Core).
    /// </summary>
    public static string ModuleOf(string file)
    {
        if (moduleOfPath.TryGetValue(file, out var m)) return m;
        var parts = file.Split('\\', '/');
        int i = Array.FindLastIndex(parts, p => p.Equals("Modules", StringComparison.OrdinalIgnoreCase));
        m = i >= 0 && i + 1 < parts.Length - 1 ? parts[i + 1] : ShellModule;
        lock (moduleOfPath) moduleOfPath[file] = m;
        return m;
    }

    // ---------------------------------------------------------------- XAML

    /// <summary>A text from any module (for XAML, where the module isn't known); unchanged if there's none.</summary>
    public static string Any(string text) => IsEnglish || !TryTranslate(text, out var t) ? text : t;

    /// <summary>
    /// The translation, looked up without the spaces around the text (text next to an inline element keeps the space
    /// that separates them) and with them put back.
    /// </summary>
    private static bool TryTranslate(string s, out string t)
    {
        if (all.TryGetValue(s, out t!)) return true;
        string trimmed = s.Trim();
        if (trimmed.Length == s.Length || !all.TryGetValue(trimmed, out var core)) return false;
        int lead = s.Length - s.TrimStart().Length, trail = s.Length - s.TrimEnd().Length;
        t = s[..lead] + core + s[^trail..];
        return true;
    }
}
