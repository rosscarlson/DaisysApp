using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// A template: the buttons a game needs, in groups (Ship, Combat…), each with its label, icon and keys, from which the
/// template wizard makes ButtScreens (or a whole BigButt, with the game's programs). Built-in ones are in the DLL
/// (Templates/*.json); yours are JSON files in %APPDATA%\DaisysApp\Butt2KeyRemote\Templates\ (one of yours with a
/// built-in one's name takes its place). The format is in the README.
/// </summary>
public sealed class ButtTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>The game's programs (e.g. "ForzaHorizon5.exe"): a BigButt made from it gets them as its games.</summary>
    public List<string> Programs { get; set; } = new();
    /// <summary>The grid of each ButtScreen, held portrait (turned for a device held landscape).</summary>
    public int Columns { get; set; } = 4;
    public int Rows { get; set; } = 6;
    /// <summary>A <see cref="ButtTheme"/>'s name, or null for the usual colours.</summary>
    public string? Theme { get; set; }
    public List<TemplateGroup> Groups { get; set; } = new();

    /// <summary>Its file, for one of yours; null for a built-in one.</summary>
    [JsonIgnore] public string? File { get; set; }
    [JsonIgnore] public bool BuiltIn => File == null;
    [JsonIgnore] public int ButtonCount => Groups.Sum(g => g.Buttons.Count);

    public ButtTemplate Clone()
    {
        var t = JsonSerializer.Deserialize<ButtTemplate>(JsonSerializer.Serialize(this, TemplateStore.Options), TemplateStore.Options)!;
        t.File = File;
        return t;
    }
}

public sealed class TemplateGroup
{
    public string Name { get; set; } = "";
    /// <summary>Ticked to start with in the wizard (its buttons keep their own).</summary>
    public bool Selected { get; set; } = true;
    public List<TemplateButton> Buttons { get; set; } = new();
}

public sealed class TemplateButton
{
    public string Label { get; set; } = "";
    /// <summary>An icon from the library, by name.</summary>
    public string? Icon { get; set; }
    /// <summary>Key ids, as Button2Key's (<see cref="KeyCatalog"/>): pressed together, in this order.</summary>
    public List<string> Keys { get; set; } = new();
    public PressMode Mode { get; set; } = PressMode.Tap;
    /// <summary>How many cells it covers, across and down.</summary>
    public int Wide { get; set; } = 1;
    public int High { get; set; } = 1;
    public bool Selected { get; set; } = true;
}

/// <summary>The templates: built in, and yours (a folder of JSON files, which Import copies into).</summary>
public static class TemplateStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // (written for people to read and change: ' and → as they are)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } = Path.Combine(ScreenStore.Folder, "Templates");

    /// <summary>Files of yours the last <see cref="LoadAll"/> couldn't use, and why (for the settings page).</summary>
    public static List<string> Problems { get; private set; } = new();

    /// <summary>Every template, by name: yours in place of a built-in one of the same name.</summary>
    public static List<ButtTemplate> LoadAll()
    {
        var byName = new Dictionary<string, ButtTemplate>(StringComparer.CurrentCultureIgnoreCase);
        var problems = new List<string>();
        var asm = typeof(TemplateStore).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("templates/")))
        {
            try
            {
                using var stream = asm.GetManifestResourceStream(res)!;
                using var reader = new StreamReader(stream);
                if (Parse(reader.ReadToEnd(), out var t, out _)) byName[t!.Name] = t;
            }
            catch (Exception ex) { Logging.ErrorLog.Write("Template " + res, ex); }
        }
        try
        {
            if (Directory.Exists(Folder))
                foreach (var file in Directory.GetFiles(Folder, "*.json"))
                {
                    try
                    {
                        if (Parse(System.IO.File.ReadAllText(file), out var t, out var why)) { t!.File = file; byName[t.Name] = t; }
                        else
                        {
                            problems.Add(F("{0}: {1}", Path.GetFileName(file), why));
                            Logging.Log.Here.Warn($"Template {Path.GetFileName(file)}: {why}");
                        }
                    }
                    catch (Exception ex) { Logging.ErrorLog.Write("Template " + Path.GetFileName(file), ex); }
                }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Templates", ex); }
        Problems = problems;
        return byName.Values.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Reads a template, checking it has a name, buttons, and keys Button2Key knows; <paramref name="why"/> says what's wrong.</summary>
    public static bool Parse(string json, out ButtTemplate? t, out string why)
    {
        t = null;
        try { t = JsonSerializer.Deserialize<ButtTemplate>(json, Options); }
        catch (JsonException ex) { why = F("It isn't a template's JSON ({0}).", ex.Message); return false; }
        if (t == null || string.IsNullOrWhiteSpace(t.Name)) { why = T("It has no name."); return false; }
        t.Name = t.Name.Trim();
        t.Groups.RemoveAll(g => g.Buttons.Count == 0);
        if (t.Groups.Count == 0) { why = T("It has no buttons."); return false; }
        var unknown = t.Groups.SelectMany(g => g.Buttons).SelectMany(b => b.Keys).Where(k => KeyCatalog.Find(k) == null).Distinct().ToList();
        if (unknown.Count > 0) { why = F("Keys Button2Key doesn't know: {0}.", string.Join(", ", unknown)); return false; }
        t.Columns = Math.Clamp(t.Columns, 1, 24);
        t.Rows = Math.Clamp(t.Rows, 1, 24);
        foreach (var b in t.Groups.SelectMany(g => g.Buttons))
        {
            b.Wide = Math.Clamp(b.Wide, 1, t.Columns);
            b.High = Math.Clamp(b.High, 1, t.Rows);
            if (IconLibrary.Find(b.Icon) == null) b.Icon = null;
        }
        why = "";
        return true;
    }

    /// <summary>Copies a file into your templates (it replaces one of yours with the same name): the template, or null and why not.</summary>
    public static ButtTemplate? Import(string path, out string why)
    {
        try
        {
            if (!Parse(System.IO.File.ReadAllText(path), out var t, out why)) return null;
            Directory.CreateDirectory(Folder);
            var same = LoadAll().FirstOrDefault(x => !x.BuiltIn && x.Name.Equals(t!.Name, StringComparison.CurrentCultureIgnoreCase));
            string target = same?.File ?? FreeFile(t!.Name);
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) System.IO.File.Copy(path, target, true);
            t!.File = target;
            return t;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Importing a template", ex);
            why = ex.Message;
            return null;
        }
    }

    /// <summary>Writes a copy of a built-in template into your folder, to change it (yours then takes its place).</summary>
    public static string? MakeYours(ButtTemplate t)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string file = FreeFile(t.Name);
            System.IO.File.WriteAllText(file, JsonSerializer.Serialize(t, Options));
            return file;
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Copying a template", ex); return null; }
    }

    public static void Delete(ButtTemplate t)
    {
        try { if (t.File != null) System.IO.File.Delete(t.File); }
        catch (Exception ex) { Logging.ErrorLog.Write("Deleting a template", ex); }
    }

    private static string FreeFile(string name)
    {
        string bare = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (bare.Length == 0) bare = "template";
        string path = Path.Combine(Folder, bare + ".json");
        for (int i = 2; System.IO.File.Exists(path); i++) path = Path.Combine(Folder, $"{bare} ({i}).json");
        return path;
    }
}

/// <summary>
/// Lays a template's ticked buttons out on ButtScreens: a group at a time, each starting on a new row, so groups that
/// fit share a ButtScreen; a group that doesn't fit in what's left goes on the next one (and one too big for a whole
/// ButtScreen carries on to the next).
/// </summary>
public static class TemplateBuilder
{
    /// <param name="shape">The size the ButtScreens are made at (and their spacing): a device's, or a phone's.</param>
    public static List<ButtScreen> Build(ButtTemplate t, ButtScreen shape)
    {
        bool landscape = shape.Width > shape.Height;
        int columns = landscape ? t.Rows : t.Columns, rows = landscape ? t.Columns : t.Rows;
        var theme = ButtTheme.All.FirstOrDefault(x => x.Name.Equals(t.Theme ?? "", StringComparison.CurrentCultureIgnoreCase));
        var made = new List<(ButtScreen Screen, List<string> Groups)>();
        ButtScreen? s = null;
        List<string> names = new();
        int top = 0;

        void Next()
        {
            s = new ButtScreen { Width = shape.Width, Height = shape.Height, Gap = shape.Gap, Columns = columns, Rows = rows };
            // (text that suits the cells: 40 pixels in a 240-pixel cell)
            s.ButtonStyle.FontSize = Math.Round(Math.Clamp(Math.Min(s.CellWidth, s.CellHeight) * 0.17, 20, 64));
            names = new List<string>();
            made.Add((s, names));
            top = 0;
        }

        foreach (var g in t.Groups)
        {
            var picked = g.Buttons.Where(b => b.Selected).ToList();
            if (picked.Count == 0) continue;
            if (s == null || s.Buttons.Count > 0 && !FitsAll(s, top, picked)) Next();
            names.Add(g.Name);
            int bottom = top;
            foreach (var tb in picked)
            {
                int wide = Math.Min(tb.Wide, columns), high = Math.Min(tb.High, rows);
                if (Place(s!, top, wide, high) is not { } at)
                {
                    Next();
                    names.Add(g.Name);
                    bottom = 0;
                    at = Place(s!, 0, wide, high)!.Value;
                }
                s!.Buttons.Add(new ButtButton
                {
                    Column = at.Column, Row = at.Row, ColumnSpan = wide, RowSpan = high,
                    Label = tb.Label, Icon = tb.Icon, Style = s.ButtonStyle.Clone(),
                    Action = tb.Keys.Count == 0 ? null : new J2KAction { Kind = ActionKind.Keys, Keys = tb.Keys.ToList(), Mode = tb.Mode },
                });
                bottom = Math.Max(bottom, at.Row + high);
            }
            top = bottom;
        }

        foreach (var (screen, groups) in made)
        {
            theme?.Apply(screen);
            string all = F("{0}: {1}", t.Name, string.Join(", ", groups.Distinct()));
            screen.Name = all.Length <= 48 || made.Count == 1 ? all : F("{0} {1}", t.Name, made.FindIndex(m => m.Screen == screen) + 1);
        }
        return made.Select(m => m.Screen).ToList();
    }

    /// <summary>The first free place at or below a row, reading across.</summary>
    private static (int Column, int Row)? Place(ButtScreen s, int from, int wide, int high)
    {
        for (int r = from; r + high <= s.Rows; r++)
            for (int c = 0; c + wide <= s.Columns; c++)
                if (s.Fits(c, r, wide, high)) return (c, r);
        return null;
    }

    private static bool FitsAll(ButtScreen s, int top, List<TemplateButton> buttons)
    {
        var trial = new ButtScreen { Columns = s.Columns, Rows = s.Rows, Buttons = s.Buttons.ToList() };
        foreach (var b in buttons)
        {
            int wide = Math.Min(b.Wide, s.Columns), high = Math.Min(b.High, s.Rows);
            if (Place(trial, top, wide, high) is not { } at) return false;
            trial.Buttons.Add(new ButtButton { Column = at.Column, Row = at.Row, ColumnSpan = wide, RowSpan = high });
        }
        return true;
    }
}
