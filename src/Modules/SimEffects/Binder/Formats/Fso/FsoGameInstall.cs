using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DaisysApp.Applets.SimEffects.Binder;

namespace DaisysApp.Applets.SimEffects.Binder.Formats.Fso;

/// <summary>Effective action after the mod's controlconfigdefaults.tbl overrides are applied.</summary>
public sealed record ModAction(ActionDef Def, string Label, Tab Tab, bool Disabled, FsoBind DefaultPrimary, FsoBind DefaultSecondary);

/// <summary>
/// An FSO mod installed by Knossos.NET (Fate of the Galaxy): finds it, and reads and writes the files the game keeps its
/// controls in (from FotG Controls).
/// </summary>
public sealed class GameInstall
{
    public string ModId { get; }
    public const int PresetSignature = 0x5f545350; // "PST_"
    public const int MaxPresetNameLength = 26;     // FSO filenames max out at 31 chars incl. ".json"

    public string RootFolder { get; }
    public string ModFolder { get; }
    public string ModVersion { get; }
    /// <summary>FSO user data folder (the one holding data\players).</summary>
    public string UserDataFolder { get; set; }
    public string? EngineFolder { get; }
    public string? EngineVersion { get; }

    public string PresetFolder => Path.Combine(UserDataFolder, "data", "players", "presets");
    public string SettingsIniPath => Path.Combine(UserDataFolder, "data", $"{ModId}_settings.ini");
    public string? SdlPath => EngineFolder is null ? null : Path.Combine(EngineFolder, "SDL2.dll");

    public IReadOnlyList<ModAction> Actions { get; private set; } = [];

    private GameInstall(string modId, string root, string modFolder, string modVersion, string userData, string? engineFolder, string? engineVersion)
    {
        ModId = modId;
        RootFolder = root;
        ModFolder = modFolder;
        ModVersion = modVersion;
        UserDataFolder = userData;
        EngineFolder = engineFolder;
        EngineVersion = engineVersion;
    }

    /// <summary>
    /// Opens an install from the folder the player picked: the game folder (with kn_portable), the
    /// kn_portable folder itself, or a Knossos library folder.
    /// </summary>
    public static GameInstall Open(string folder, string modId, string gameName)
    {
        folder = Path.GetFullPath(folder.Trim().Trim('"'));
        if (!Directory.Exists(folder)) throw new BinderException(F("The folder {0} doesn't exist", folder));

        string portable = Path.GetFileName(folder).Equals("kn_portable", StringComparison.OrdinalIgnoreCase)
            ? folder : Path.Combine(folder, "kn_portable");

        string library;
        string userData;
        if (Directory.Exists(portable))
        {
            library = Path.Combine(portable, "Library");
            userData = Path.Combine(portable, "HardLightProductions", "FreeSpaceOpen");
        }
        else
        {
            // Non-portable Knossos: the library is wherever the user chose, data lives in %APPDATA%.
            library = folder;
            userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HardLightProductions", "FreeSpaceOpen");
        }

        var modFolder = FindModFolder(library, modId)
            ?? throw new BinderException(F("{0} isn't in this folder", gameName));

        var modVersion = Path.GetFileName(modFolder).Replace(modId + "-", "");
        var (engineFolder, engineVersion) = FindEngine(library, modFolder);

        var install = new GameInstall(modId, folder, modFolder, modVersion, userData, engineFolder, engineVersion);
        install.LoadActions();
        return install;
    }

    private static string? FindModFolder(string library, string ModId)
    {
        var candidates = new List<string>();
        foreach (var dir in new[] { Path.Combine(library, ModId), library })
        {
            if (!Directory.Exists(dir)) continue;
            candidates.AddRange(Directory.GetDirectories(dir, ModId + "-*")
                .Where(d => File.Exists(Path.Combine(d, "mod.json"))));
        }
        return candidates
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d)[(ModId.Length + 1)..], out var v) ? v : new Version(0, 0))
            .FirstOrDefault();
    }

    private static (string? Folder, string? Version) FindEngine(string library, string modFolder)
    {
        // The mod's FG_Core package names the FSO version it needs ("id": "FSO", "version": "26.0.1").
        string? wanted = null;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(modFolder, "mod.json")));
            wanted = json?["packages"]?.AsArray()
                .SelectMany(p => p?["dependencies"]?.AsArray() ?? [])
                .FirstOrDefault(d => (string?)d?["id"] == "FSO")?["version"]?.GetValue<string>();
        }
        catch { /* fall back to whatever FSO build is installed */ }

        var bin = Path.Combine(library, "bin");
        if (!Directory.Exists(bin)) return (null, null);
        var builds = Directory.GetDirectories(bin, "FSO-*")
            .OrderByDescending(d => d.EndsWith("-" + wanted, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(d => Path.GetFileName(d));
        foreach (var build in builds)
        {
            var win = Path.Combine(build, "windows");
            if (!Directory.Exists(win)) continue;
            var sdl = Directory.GetFiles(win, "SDL2.dll", SearchOption.AllDirectories)
                .OrderByDescending(p => p.Contains("x64"))
                .FirstOrDefault();
            if (sdl != null) return (Path.GetDirectoryName(sdl), Path.GetFileName(build)[4..]);
        }
        return (null, null);
    }

    // ------------------------------------------------------------------
    // Actions: engine defaults + controlconfigdefaults.tbl + string tables
    // ------------------------------------------------------------------

    private IEnumerable<string> TableFolders() =>
        Directory.GetDirectories(ModFolder)
            .Select(p => Path.Combine(p, "data", "tables"))
            .Where(Directory.Exists)
            .OrderBy(p => p.Contains("FG_Core", StringComparison.OrdinalIgnoreCase) ? 0 : 1);

    private void LoadActions()
    {
        var strings = LoadEnglishStrings();
        var items = ActionCatalog.Engine.ToDictionary(a => a.Name, a => new MutableAction(a));

        var tbl = TableFolders()
            .Select(f => Path.Combine(f, "controlconfigdefaults.tbl"))
            .FirstOrDefault(File.Exists)
            ?? TableFolders().SelectMany(f => Directory.GetFiles(f, "*.tbl"))
                .FirstOrDefault(f => Path.GetFileName(f).Equals("controlconfigdefaults.tbl", StringComparison.OrdinalIgnoreCase));
        if (tbl != null) ApplyControlTable(File.ReadAllText(tbl), items);

        Actions = items.Values
            .Select(m =>
            {
                string label = m.Text;
                if (m.Xstr > 1 && strings.TryGetValue(m.Xstr, out var s)) label = s;
                else if (m.Xstr == 1 && strings.TryGetValue(ActionCatalog.ControlConfigXstr + m.Def.Id, out var s1)) label = s1;
                if (string.IsNullOrWhiteSpace(label)) label = m.Text;
                return new ModAction(m.Def, label, m.Tab, m.Disabled || m.Tab == Tab.None, m.Primary.Normalized(), m.Secondary.Normalized());
            })
            .OrderBy(a => a.Def.Id)
            .ToList();
    }

    private sealed class MutableAction(ActionDef def)
    {
        public ActionDef Def { get; } = def;
        public string Text = def.Text;
        public int Xstr = def.XstrIndex;
        public Tab Tab = def.Tab;
        public CcType Type = def.Type;
        public bool Disabled = def.Disabled;
        public FsoBind Primary = def.Primary;
        public FsoBind Secondary = def.Secondary;
    }

    /// <summary>English strings: strings.tbl #English, then *-lcl.tbm overrides (later files win).</summary>
    private Dictionary<int, string> LoadEnglishStrings()
    {
        var result = new Dictionary<int, string>();
        var files = TableFolders().SelectMany(f =>
        {
            var list = new List<string>();
            var main = Path.Combine(f, "strings.tbl");
            if (File.Exists(main)) list.Add(main);
            list.AddRange(Directory.GetFiles(f, "*-lcl.tbm").OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            return list;
        });
        var line = new Regex(@"^\s*(\d+)\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        foreach (var file in files)
        {
            bool english = false;
            foreach (var raw in File.ReadLines(file, Encoding.Latin1))
            {
                var t = raw.TrimStart();
                if (t.StartsWith('#'))
                {
                    english = t.StartsWith("#English", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!english) continue;
                var m = line.Match(raw);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var id))
                    result[id] = m.Groups[2].Value.Replace("\\\"", "\"");
            }
        }
        return result;
    }

    /// <summary>
    /// Applies the first #ControlConfigOverride section the same way control_config_common_read_section()
    /// does when it overrides the "default" preset.
    /// </summary>
    private static void ApplyControlTable(string text, Dictionary<string, MutableAction> items)
    {
        // Strip comments: ';' to end of line, and /* */ blocks.
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var tokens = new List<(string Key, string Value)>();
        foreach (var raw in text.Split('\n'))
        {
            var lineText = raw;
            int semi = lineText.IndexOf(';');
            if (semi >= 0) lineText = lineText[..semi];
            lineText = lineText.Trim();
            if (lineText.Length == 0) continue;
            if (lineText.StartsWith('#') || lineText.StartsWith('+'))
            {
                tokens.Add((lineText.Split(' ', '\t')[0], ""));
                continue;
            }
            int colon = lineText.IndexOf(':');
            if (lineText.StartsWith('$') && colon > 0)
                tokens.Add((lineText[..(colon + 1)], lineText[(colon + 1)..].Trim()));
        }

        int i = 0;
        // Only the first override section matters (it is the "default" preset the game starts from).
        while (i < tokens.Count && !tokens[i].Key.Equals("#ControlConfigOverride", StringComparison.OrdinalIgnoreCase)) i++;
        if (i >= tokens.Count) return;
        i++;
        if (i < tokens.Count && tokens[i].Key == "$Name:")
        {
            if (!tokens[i].Value.Equals("default", StringComparison.OrdinalIgnoreCase)) return;
            i++;
        }

        while (i < tokens.Count && !tokens[i].Key.StartsWith('#'))
        {
            if (tokens[i].Key != "$FsoBind:") { i++; continue; }
            items.TryGetValue(tokens[i].Value, out var item);
            i++;

            bool disabledSet = false, disabled = false;
            while (i < tokens.Count && tokens[i].Key != "$FsoBind:" && !tokens[i].Key.StartsWith('#'))
            {
                var (key, value) = tokens[i];
                switch (key)
                {
                    case "$Primary:":
                    case "$Secondary:":
                        i++;
                        var bind = ReadBind(tokens, ref i, item);
                        if (item != null)
                        {
                            if (key == "$Primary:") item.Primary = bind; else item.Secondary = bind;
                        }
                        continue;
                    case "$Category:":
                        if (item != null)
                        {
                            item.Tab = value switch
                            {
                                "TARGET_TAB" => Tab.Targeting,
                                "SHIP_TAB" => Tab.Ship,
                                "WEAPON_TAB" => Tab.Weapons,
                                "COMPUTER_TAB" => Tab.Computer,
                                "NO_TAB" => Tab.None,
                                _ => item.Tab,
                            };
                        }
                        break;
                    case "$Text:":
                        if (item != null) { item.Text = value; item.Xstr = 0; }
                        break;
                    case "$Has XStr:":
                        if (item != null && int.TryParse(value, out var x)) item.Xstr = x;
                        break;
                    case "$Disable:":
                        disabledSet = true;
                        disabled = value.StartsWith("t", StringComparison.OrdinalIgnoreCase) ||
                                   value.StartsWith("y", StringComparison.OrdinalIgnoreCase) || value == "1";
                        break;
                    case "+Disable":
                        disabledSet = true;
                        disabled = true;
                        break;
                }
                i++;
            }
            // The engine resets "disabled" for every $FsoBind entry in an override section.
            if (item != null) item.Disabled = disabledSet && disabled;
        }
    }

    private static FsoBind ReadBind(List<(string Key, string Value)> tokens, ref int i, MutableAction? item)
    {
        Cid cid = Cid.None;
        string flags = "", input = "NONE";
        while (i < tokens.Count)
        {
            var (key, value) = tokens[i];
            if (key == "$Controller:") cid = FsoBind.ParseCid(value);
            else if (key == "$Flags:") flags = value;
            else if (key == "$Input:") input = value;
            else break;
            i++;
        }
        var f = FsoBind.ParseFlags(flags);
        if (item != null)
        {
            // Same flag fix-ups stuff_CCF() applies for the action's type.
            if (item.Type is CcType.Trigger or CcType.Continuous) f &= ~(Ccf.Axis | Ccf.Ball);
            else
            {
                f &= ~(Ccf.AxisBtn | Ccf.Hat);
                if ((f & (Ccf.Axis | Ccf.Ball)) == 0) f |= Ccf.Axis;
                if (item.Type == CcType.AxisAbs) f &= ~Ccf.Relative; else f |= Ccf.Relative;
            }
        }
        var bind = new FsoBind(cid, input, f).Normalized();
        return bind.IsLoadable ? bind : FsoBind.None;
    }

    // ------------------------------------------------------------------
    // Presets
    // ------------------------------------------------------------------

    public IReadOnlyList<string> ListPresets()
    {
        if (!Directory.Exists(PresetFolder)) return [];
        return Directory.GetFiles(PresetFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string PresetPath(string name) => Path.Combine(PresetFolder, name + ".json");

    /// <summary>Reads a preset file; returns bindings by action name. Unknown or broken entries are skipped.</summary>
    public Dictionary<string, (FsoBind Primary, FsoBind Secondary)> ReadPreset(string name)
    {
        var result = new Dictionary<string, (FsoBind, FsoBind)>();
        var root = JsonNode.Parse(File.ReadAllText(PresetPath(name)))
            ?? throw new InvalidDataException(T("The file is empty"));
        if ((int?)root["signature"] != PresetSignature)
            throw new InvalidDataException(T("It isn't an FSO controls preset"));
        foreach (var item in root["actions"]?.AsArray() ?? [])
        {
            var action = (string?)item?["bind"];
            if (action == null) continue;
            result[action] = (ReadSlot(item?["primary"]), ReadSlot(item?["secondary"]));
        }
        return result;

        static FsoBind ReadSlot(JsonNode? n)
        {
            if (n == null) return FsoBind.None;
            var b = new FsoBind(FsoBind.ParseCid((string?)n["cid"]), (string?)n["input"] ?? "NONE",
                FsoBind.ParseFlags((string?)n["flags"])).Normalized();
            return b.IsLoadable ? b : FsoBind.None;
        }
    }

    /// <summary>
    /// Writes a preset with the same fields save_preset_file() produces, listing every action.
    /// </summary>
    public void WritePreset(string name, IEnumerable<(string Action, FsoBind Primary, FsoBind Secondary)> bindings)
    {
        Directory.CreateDirectory(PresetFolder);
        var actions = new JsonArray();
        foreach (var (action, primary, secondary) in bindings)
        {
            actions.Add(new JsonObject
            {
                ["bind"] = action,
                ["primary"] = Slot(primary),
                ["secondary"] = Slot(secondary),
            });
        }
        var root = new JsonObject
        {
            ["signature"] = PresetSignature,
            ["version"] = 0,
            ["actions"] = actions,
        };
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        WriteAtomically(PresetPath(name), json, new UTF8Encoding(false));

        static JsonObject Slot(FsoBind b)
        {
            b = b.Normalized();
            return new JsonObject { ["cid"] = b.CidName, ["flags"] = b.FlagsText, ["input"] = b.IsNone ? "NONE" : b.Input };
        }
    }

    public static bool IsValidPresetName(string name, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name)) { error = T("Give the preset a name"); return false; }
        if (name.Length > MaxPresetNameLength) { error = F("Use {0} characters or fewer", MaxPresetNameLength); return false; }
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_\-]+$")) { error = T("Letters, numbers, - and _ only"); return false; }
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase)) { error = T("“default” is the game's own"); return false; }
        return true;
    }

    // ------------------------------------------------------------------
    // Mod settings ini (in-game options: joystick assignment, mouse)
    // ------------------------------------------------------------------

    public string? ReadSetting(string section, string key)
    {
        if (!File.Exists(SettingsIniPath)) return null;
        string? current = null;
        foreach (var raw in File.ReadLines(SettingsIniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1]; continue; }
            if (current == null || !current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Sets several keys in one section, keeping everything else in the file as it was.</summary>
    public void WriteSettings(string section, IReadOnlyDictionary<string, string> values)
    {
        var lines = File.Exists(SettingsIniPath) ? File.ReadAllLines(SettingsIniPath).ToList() : [];
        var pending = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        int sectionStart = -1, sectionEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith('[') || !t.EndsWith(']')) continue;
            if (sectionStart >= 0) { sectionEnd = i; break; }
            if (t[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
        }

        if (sectionStart < 0)
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.AddRange(pending.Select(kv => $"{kv.Key}={kv.Value}"));
            lines.Add("");
        }
        else
        {
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                var k = lines[i][..eq].Trim();
                if (pending.Remove(k, out var v)) lines[i] = $"{k}={v}";
            }
            // Insert new keys after the last non-blank line of the section.
            int insertAt = sectionEnd;
            while (insertAt - 1 > sectionStart && lines[insertAt - 1].Trim().Length == 0) insertAt--;
            lines.InsertRange(insertAt, pending.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsIniPath)!);
        var backup = SettingsIniPath + ".bak";
        if (File.Exists(SettingsIniPath) && !File.Exists(backup)) File.Copy(SettingsIniPath, backup);
        WriteAtomically(SettingsIniPath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
    }

    private static void WriteAtomically(string path, string content, Encoding encoding) => Paths.WriteAtomically(path, content, encoding);
}
