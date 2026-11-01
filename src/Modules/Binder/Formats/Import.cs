using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Binder.Formats;

/// <summary>What importing a game's own bindings did.</summary>
public sealed record ImportReport(string File, int Bound, int Added, List<string> Refused);

/// <summary>
/// Reads the bindings a <c>sheet</c> title's game keeps in its own file, where Binder can read it: the title's options
/// <c>import</c> (which reader: <c>acc</c>, ACC's controls.json; <c>sc</c>, Star Citizen's actionmaps.xml) and <c>importFile</c>
/// (where it is: under the folder picked, its install folder, or <c>documents:</c> and the like). Each binding goes to the
/// action whose <c>game</c> is the game's name for it; the game's names Binder doesn't list come in as actions of their own
/// ("From the game").
/// </summary>
public static class Import
{
    public const string ExtraPrefix = "game:";

    public static bool Can(TitleDef def) => def.Option("import").Length > 0 && def.Option("importFile").Length > 0;

    /// <summary>The game's file: where the title says, or under the folder picked (its install folder: looked for a few
    /// folders down, so <c>D:\RSI</c> finds <c>StarCitizen\LIVE\user\…</c>).</summary>
    public static string? FindFile(TitleDef def, string folder)
    {
        if (!Can(def)) return null;
        var file = def.Option("importFile");
        if (file.Contains(':') || file.StartsWith('%')) return Paths.Expand(file).FirstOrDefault(File.Exists);
        if (folder.Length == 0 || !Directory.Exists(folder)) return null;
        var direct = Path.Combine(folder, file);
        if (File.Exists(direct)) return direct;
        try
        {
            var name = Path.GetFileName(file);
            var tail = Path.DirectorySeparatorChar + file.TrimStart('\\', '/');
            return Directory.EnumerateFiles(folder, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 7, IgnoreInaccessible = true })
                .FirstOrDefault(f => f.EndsWith(tail, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The game's bindings into <paramref name="data"/>: the actions its file names get what it has (up to two), the
    /// rest keep theirs.</summary>
    public static ImportReport Apply(TitleData data, string file)
    {
        var read = data.Def.Option("import").ToLowerInvariant() switch
        {
            "acc" => Acc(file),
            "sc" => StarCitizen(file),
            var other => throw new BinderException(F("No reader called {0}", other)),
        };
        var byGame = new Dictionary<string, BinderAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in data.Actions)
            foreach (var g in GameNames(data.Def, a)) byGame.TryAdd(g, a);
        var placed = new Dictionary<BinderAction, List<Bind>>();
        var refused = new List<string>();
        int added = 0, bound = 0;
        foreach (var (game, b) in read)
        {
            if (!byGame.TryGetValue(game, out var a))
            {
                a = Extra(game, b.IsAxis, data.Actions.Count);
                data.Actions.Add(a);
                byGame[game] = a;
                added++;
            }
            if (!placed.TryGetValue(a, out var list)) placed[a] = list = new();
            if (list.Count >= TemplateFormat.SlotCount || list.Any(x => x.SameInput(b))) continue;
            if (TemplateFormat.RefuseAny(a, b) is { } why) { refused.Add($"{a.Label}: {Describe.Text(b, _ => null)} · {why}"); continue; }
            list.Add(b);
            bound++;
        }
        foreach (var (a, list) in placed)
            data.Binds[a.Id] = Enumerable.Range(0, TemplateFormat.SlotCount).Select(i => i < list.Count ? list[i] : Bind.None).ToArray();
        Logging.Log.Here.Info($"Binder: imported {file} into {data.Def.Name}: {bound} bindings, {added} actions added, {refused.Count} refused");
        return new ImportReport(file, bound, added, refused);
    }

    private static IEnumerable<string> GameNames(TitleDef def, BinderAction a)
    {
        if (a.Id.StartsWith(ExtraPrefix, StringComparison.Ordinal)) { yield return a.Id[ExtraPrefix.Length..]; yield break; }
        if (def.Action(a.Id) is { } d) foreach (var g in d.Game) yield return g;
    }

    /// <summary>An action the game has that the title's JSON doesn't list, named from the game's own ("CycleCamera":
    /// "Cycle Camera", "v_view_mode": "View Mode").</summary>
    public static BinderAction Extra(string game, bool axis, int order)
    {
        var words = Regex.Replace(Regex.Replace(game, "^(v|ui|pc|vehicle)_", ""), "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').Trim();
        var label = words.Length == 0 ? game : char.ToUpperInvariant(words[0]) + words[1..];
        return new BinderAction { Id = ExtraPrefix + game, Label = label, GameLabel = game, Group = T("From the game"), IsAxis = axis, Order = order };
    }

    // ---------------------------------------------------------------- devices

    /// <summary>A controller as Binder names it, from DirectInput's instance (plugged in) or its product GUID (whose first part
    /// is the product and vendor: 0x0201231D a VKB 231D:0201), the <paramref name="nth"/> of its kind otherwise.</summary>
    private static DeviceId? Device(Guid instance, uint product, int nth)
    {
        var list = GameControllers.List();
        var ids = Input.Nth.Of(list.Select(c => (c.Vid, c.Pid)));
        int i = list.FindIndex(c => c.Instance == instance);
        if (i >= 0) return ids[i];
        int vid = (int)(product & 0xFFFF), pid = (int)(product >> 16);
        return vid == 0 ? null : new DeviceId(vid, pid, nth);
    }

    // ---------------------------------------------------------------- Assetto Corsa Competizione: Config\controls.json

    /// <summary>
    /// ACC's controls.json: <c>commandDevices</c>, each with its <c>productId</c> ("33628957-0-0-…": the product GUID's first part
    /// in decimal) and <c>instanceGuid</c>; their <c>raceCommandAxisList</c> (<c>axisIndex</c> as DirectInput's state numbers
    /// them, <c>axisCode</c> "Steer", "Gas"…) and <c>raceCommandButtonList</c> (<c>buttonIndex</c>, or a hat's <c>powIndex</c> and
    /// <c>powValue</c> 0, 9000, 18000, 27000; <c>instantActionCode</c> "GearUp", <c>extendedActionCode</c> held longer). A wheel
    /// is listed once per rim: the same binding twice counts once.
    /// </summary>
    public static List<(string, Bind)> Acc(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var found = new List<(string, Bind)>();
        var nth = new Dictionary<uint, List<Guid>>();
        if (!doc.RootElement.TryGetProperty("commandDevices", out var devices)) throw new BinderException(T("It has no commandDevices"));
        foreach (var d in devices.EnumerateArray())
        {
            var productText = Str(d, "productId").Split('-')[0];
            uint.TryParse(productText, out uint product);
            Guid.TryParse(Str(d, "instanceGuid"), out var instance);
            if (!nth.TryGetValue(product, out var seen)) nth[product] = seen = new();
            if (!seen.Contains(instance)) seen.Add(instance);
            if (Device(instance, product, seen.IndexOf(instance)) is not { } id) continue;
            if (d.TryGetProperty("raceCommandAxisList", out var axes))
                foreach (var a in axes.EnumerateArray())
                {
                    var code = Str(a, "axisCode");
                    if (code.Length == 0 || code == "None" || !a.TryGetProperty("axisIndex", out var ix) || ix.GetInt32() < 0) continue;
                    found.Add((code, Bind.Axis(id, ix.GetInt32())));
                }
            if (d.TryGetProperty("raceCommandButtonList", out var buttons))
                foreach (var b in buttons.EnumerateArray())
                {
                    Bind? bind = null;
                    int button = Int(b, "buttonIndex"), pov = Int(b, "powIndex");
                    if (button >= 0) bind = Bind.Button(id, button);
                    else if (pov >= 0) bind = Bind.HatDir(id, pov, Int(b, "powValue") / 9000 & 3);
                    if (bind == null) continue;
                    foreach (var field in new[] { "instantActionCode", "extendedActionCode" })
                    {
                        var code = Str(b, field);
                        if (code.Length > 0 && code != "None") found.Add((code, bind));
                    }
                }
        }
        // (the keyboard's, by key name where it has one)
        if (doc.RootElement.TryGetProperty("keyboardSettings", out var kb) && kb.TryGetProperty("raceCommandButtonList", out var keys))
            foreach (var k in keys.EnumerateArray())
            {
                var code = Str(k, "instantActionCode");
                if (code.Length == 0 || code == "None" || Keys.ParseDefault(Spaced(Str(k, "keyName"))) is not { } key) continue;
                found.Add((code, key));
            }
        return found;

        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
        static string Spaced(string s) => Regex.Replace(s, "([a-z])([A-Z0-9])", "$1 $2");
    }

    // ---------------------------------------------------------------- Star Citizen: user\client\0\Profiles\default\actionmaps.xml

    private static readonly string[] ScAxes = ["x", "y", "z", "rotx", "roty", "rotz", "slider1", "slider2"];
    private static readonly string[] ScHat = ["up", "right", "down", "left"];

    private static readonly Dictionary<string, string> ScKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "Space", ["lshift"] = "Left Shift", ["rshift"] = "Right Shift", ["lctrl"] = "Left Ctrl", ["rctrl"] = "Right Ctrl",
        ["lalt"] = "Left Alt", ["ralt"] = "Right Alt", ["enter"] = "Enter", ["escape"] = "Esc", ["tab"] = "Tab", ["backspace"] = "Backspace",
        ["capslock"] = "Caps Lock", ["up"] = "Up", ["down"] = "Down", ["left"] = "Left", ["right"] = "Right", ["home"] = "Home", ["end"] = "End",
        ["pgup"] = "Page Up", ["pgdn"] = "Page Down", ["insert"] = "Insert", ["delete"] = "Delete", ["minus"] = "-", ["equals"] = "=",
        ["lbracket"] = "[", ["rbracket"] = "]", ["semicolon"] = ";", ["apostrophe"] = "'", ["comma"] = ",", ["period"] = ".", ["slash"] = "/",
        ["backslash"] = "\\", ["grave"] = "`", ["np_add"] = "Numpad +", ["np_subtract"] = "Numpad -", ["np_multiply"] = "Numpad *",
        ["np_divide"] = "Numpad /", ["np_period"] = "Numpad .", ["np_enter"] = "Numpad Enter", ["numlock"] = "Num Lock",
        ["scrolllock"] = "Scroll Lock", ["pause"] = "Pause", ["print"] = "Print Screen",
    };

    /// <summary>
    /// Star Citizen's actionmaps.xml (only what differs from its defaults): <c>&lt;options type="joystick" instance="1"
    /// Product="Name {0201231D-…}"/&gt;</c> names each jsN; <c>&lt;action name="v_pitch"&gt;&lt;rebind input="js1_y"/&gt;</c> its
    /// bindings: <c>js1_button5</c> (from 1), <c>js1_hat1_up</c>, <c>js1_x</c>/<c>rotz</c>/<c>slider1</c>, <c>kb1_lshift+w</c>,
    /// <c>mo1_mouse1</c>, <c>mo1_maxis_x</c>. An empty input (<c>js1_ </c>) unbinds it.
    /// </summary>
    public static List<(string, Bind)> StarCitizen(string file)
    {
        var doc = XDocument.Load(file);
        var sticks = new Dictionary<string, DeviceId>();
        var nth = new Dictionary<uint, int>();
        foreach (var o in doc.Descendants("options").Where(o => (string?)o.Attribute("type") == "joystick"))
        {
            var product = (string?)o.Attribute("Product") ?? "";
            var m = Regex.Match(product, @"\{([0-9A-Fa-f]{8})-");
            if (!m.Success) continue;
            uint p = Convert.ToUInt32(m.Groups[1].Value, 16);
            nth[p] = nth.TryGetValue(p, out int n) ? n + 1 : 0;
            if (Device(Guid.Empty, p, nth[p]) is { } id) sticks["js" + (string?)o.Attribute("instance")] = id;
        }
        var found = new List<(string, Bind)>();
        foreach (var a in doc.Descendants("action"))
        {
            var name = (string?)a.Attribute("name");
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var r in a.Elements("rebind"))
                if (ScBind((string?)r.Attribute("input") ?? "", sticks) is { } b) found.Add((name, b));
        }
        return found;
    }

    private static Bind? ScBind(string input, Dictionary<string, DeviceId> sticks)
    {
        int us = input.IndexOf('_');
        if (us < 0) return null;
        string dev = input[..us], what = input[(us + 1)..].Trim();
        if (what.Length == 0) return null;
        if (dev.StartsWith("kb"))
        {
            var mods = Mods.None;
            var parts = what.Split('+');
            foreach (var p in parts[..^1])
                mods |= p switch { "lshift" or "rshift" => Mods.Shift, "lctrl" or "rctrl" => Mods.Ctrl, "lalt" or "ralt" => Mods.Alt, _ => Mods.None };
            var key = parts[^1];
            var label = ScKeys.TryGetValue(key, out var l) ? l : key.StartsWith("np_") ? "Numpad " + key[3..] : key.ToUpperInvariant();
            return Keys.Parse(label) is { } k ? k with { Mods = mods } : null;
        }
        if (dev.StartsWith("mo"))
        {
            return what switch
            {
                "mouse1" => new Bind(BindKind.Mouse, 0), "mouse2" => new Bind(BindKind.Mouse, 1), "mouse3" => new Bind(BindKind.Mouse, 2),
                "mouse4" => new Bind(BindKind.Mouse, 3), "mouse5" => new Bind(BindKind.Mouse, 4), "mwheel_up" => new Bind(BindKind.Mouse, 5),
                "mwheel_down" => new Bind(BindKind.Mouse, 6), "maxis_x" => new Bind(BindKind.MouseAxis, 0), "maxis_y" => new Bind(BindKind.MouseAxis, 1),
                _ => null,
            };
        }
        if (!dev.StartsWith("js") || !sticks.TryGetValue(dev, out var id)) return null;
        if (what.StartsWith("button") && int.TryParse(what[6..], out int button) && button > 0) return Bind.Button(id, button - 1);
        var hat = Regex.Match(what, @"^hat(\d+)_(up|right|down|left)$");
        if (hat.Success) return Bind.HatDir(id, int.Parse(hat.Groups[1].Value) - 1, Array.IndexOf(ScHat, hat.Groups[2].Value));
        int axis = Array.IndexOf(ScAxes, what);
        return axis >= 0 ? Bind.Axis(id, axis) : null;
    }
}
