using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using DaisysApp.Applets.Binder.Input;

namespace DaisysApp.Applets.Binder.Formats;

/// <summary>
/// Elite Dangerous: the preset its StartPreset file names, <c>Name.Major.Minor.binds</c> in its Bindings folder (XML, an
/// element per action: Primary and Secondary, or an axis' Binding with Inverted), changed in place so everything Binder
/// doesn't show stays as it was. The defaults are the game's own scheme (option <c>defaults</c>), and its DeviceMappings.xml
/// (option <c>schemes</c>, the ControlSchemes folder) names the controllers it knows; others are their vendor and product
/// in hex. Read through DirectInput, as the game does.
/// </summary>
public sealed class EliteFormat : IBindFormat
{
    public string Id => "elite";
    public InputKind Input => InputKind.DirectInput;

    private static readonly string[] AxisKeys = ["Joy_XAxis", "Joy_YAxis", "Joy_ZAxis", "Joy_RXAxis", "Joy_RYAxis", "Joy_RZAxis", "Joy_UAxis", "Joy_VAxis"];
    private static readonly string[] Dirs = ["Up", "Right", "Down", "Left"];
    private const string NoDevice = "{NoDevice}";

    private sealed class State
    {
        public required string File;
        public required XDocument Doc;
        /// <summary>How the file ended (the game ends it with a line break).</summary>
        public string End = "";
        public readonly Dictionary<string, Bind[]> Read = new();
        /// <summary>The game's names for controllers, by vendor and product.</summary>
        public readonly Dictionary<(int, int), string> Names = new();
        public readonly Dictionary<string, (int Vid, int Pid)> ByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<(int, int)> Pads = new();
    }

    private static State Of(TitleData d) => (State)d.State!;

    // ---------------------------------------------------------------- finding

    private static string? StartPreset(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "StartPreset*.start").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault() : null;

    private static string? PresetName(string? start)
    {
        if (start == null) return null;
        try { return File.ReadAllLines(start).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0); } catch { return null; }
    }

    /// <summary>The preset's file: its newest version.</summary>
    private static string? BindsFile(string folder, string name) =>
        Directory.GetFiles(folder, name + ".*.binds").Concat(Directory.GetFiles(folder, name + ".binds"))
            .OrderByDescending(f => Version.TryParse(Path.GetFileNameWithoutExtension(f)[(name.Length + 1)..], out var v) ? v : new Version(0, 0))
            .FirstOrDefault();

    private static string? Schemes(TitleDef def) => def.Option("schemes") is { Length: > 0 } s ? Paths.First(s) : null;

    private static string? Defaults(TitleDef def) =>
        Schemes(def) is { } dir && def.Option("defaults") is { Length: > 0 } f && File.Exists(Path.Combine(dir, f)) ? Path.Combine(dir, f) : null;

    public IReadOnlyList<Check> Check(TitleDef def, string folder)
    {
        var checks = new List<Check>();
        if (!Directory.Exists(folder)) return [new(F("The folder {0} doesn't exist", folder), false)];
        var start = StartPreset(folder);
        var name = PresetName(start);
        checks.Add(new(name != null ? F("Preset in use: {0}", name) : T("StartPreset file (the preset in use)"), name != null, start));
        if (name != null)
        {
            var file = BindsFile(folder, name);
            checks.Add(file != null ? new(Path.GetFileName(file), true, file) : new(F("{0} is one of the game's own: change a binding in the game once to make Custom", name), false));
        }
        var defaults = Defaults(def);
        checks.Add(new(defaults != null ? F("Defaults: {0}", Path.GetFileName(defaults)) : T("The game's defaults (ControlSchemes)"), defaults != null, defaults, Needed: false));
        return checks;
    }

    // ---------------------------------------------------------------- reading

    public TitleData Load(TitleDef def, TitleSetup setup)
    {
        var name = PresetName(StartPreset(setup.Folder)) ?? throw new BinderException(T("No StartPreset file in this folder"));
        var file = BindsFile(setup.Folder, name) ?? throw new BinderException(F("{0} is one of the game's own: change a binding in the game once to make Custom", name));
        var text = File.ReadAllText(file);
        var st = new State
        {
            File = file, Doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace),
            End = text.EndsWith("\r\n") ? "\r\n" : text.EndsWith('\n') ? "\n" : "",
        };
        ReadDevices(def, st);

        var defaults = new Dictionary<string, Bind[]>();
        if (Defaults(def) is { } d)
        {
            try
            {
                foreach (var e in XDocument.Load(d).Root!.Elements())
                    if (IsAction(e)) defaults[e.Name.LocalName] = ReadAction(st, e);
            }
            catch (Exception ex) { Logging.Log.Here.Warn($"Binder: Elite's defaults {d}: {ex.Message}"); }
        }

        var data = new TitleData { Def = def, Setup = setup, State = st, Summary = $"{def.Name} · {Path.GetFileName(file)}" };
        int order = 0;
        foreach (var e in st.Doc.Root!.Elements())
        {
            if (!IsAction(e)) continue;
            var id = e.Name.LocalName;
            var json = def.Action(id);
            if (json?.Hidden == true) continue;
            bool axis = e.Element("Binding") != null;
            var binds = ReadAction(st, e);
            st.Read[id] = binds;
            data.Binds[id] = (Bind[])binds.Clone();
            data.Actions.Add(new BinderAction
            {
                Id = id, Label = json?.Label ?? Words(id), Group = json?.Group ?? def.GroupFor(id, T("Other")), Context = def.ContextFor(id), IsAxis = axis,
                Centred = json?.Centred ?? true, Role = json?.Role, Order = order++,
                Defaults = defaults.TryGetValue(id, out var db) && db.Length == binds.Length ? db : new Bind[binds.Length].Select(_ => Bind.None).ToArray(),
            });
        }
        return data;
    }

    private static bool IsAction(XElement e) => e.Element("Primary") != null || e.Element("Binding") != null;

    /// <summary>"YawLeftButton" as "Yaw Left Button".</summary>
    private static string Words(string id) =>
        Regex.Replace(Regex.Replace(id.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "), @"\s+", " ").Trim();

    private static void ReadDevices(TitleDef def, State st)
    {
        if (Schemes(def) is not { } dir || !File.Exists(Path.Combine(dir, "DeviceMappings.xml"))) return;
        try
        {
            foreach (var dev in XDocument.Load(Path.Combine(dir, "DeviceMappings.xml")).Root!.Elements())
            {
                var name = dev.Name.LocalName;
                foreach (var vp in new[] { dev }.Concat(dev.Elements("Alternative")))
                {
                    if (!int.TryParse((string?)vp.Element("VID"), NumberStyles.HexNumber, null, out int vid) ||
                        !int.TryParse((string?)vp.Element("PID"), NumberStyles.HexNumber, null, out int pid)) continue;
                    st.Names.TryAdd((vid, pid), name);
                    st.ByName.TryAdd(name, (vid, pid));
                    if (name == "GamePad") st.Pads.Add((vid, pid));
                }
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Binder: Elite's DeviceMappings.xml: {ex.Message}"); }
    }

    private static Bind[] ReadAction(State st, XElement e) =>
        e.Element("Binding") is { } axis
            ? [ReadOne(st, axis) with { Inverted = (string?)e.Element("Inverted")?.Attribute("Value") == "1" }]
            : [ReadOne(st, e.Element("Primary")), ReadOne(st, e.Element("Secondary"))];

    private static Bind ReadOne(State st, XElement? slot)
    {
        if (slot == null) return Bind.None;
        string device = (string?)slot.Attribute("Device") ?? NoDevice, key = (string?)slot.Attribute("Key") ?? "";
        if (device == NoDevice || key.Length == 0) return Bind.None;
        var raw = new Bind(BindKind.Other, Raw: slot.ToString(SaveOptions.DisableFormatting));
        var mods = Mods.None;
        foreach (var m in slot.Elements("Modifier"))
        {
            if ((string?)m.Attribute("Device") != "Keyboard") return raw;
            switch ((string?)m.Attribute("Key"))
            {
                case "Key_LeftShift" or "Key_RightShift": mods |= Mods.Shift; break;
                case "Key_LeftControl" or "Key_RightControl": mods |= Mods.Ctrl; break;
                case "Key_LeftAlt" or "Key_RightAlt": mods |= Mods.Alt; break;
                default: return raw;
            }
        }
        if (device == "Keyboard") return Keys.FromElite(key) is { } code ? Bind.Key(code, mods) : raw;
        if (mods != Mods.None) return raw;
        if (device == "Mouse")
        {
            var m = Regex.Match(key, @"^Mouse_([1-4])$");
            if (m.Success) return new Bind(BindKind.Mouse, int.Parse(m.Groups[1].Value) - 1);
            return key switch
            {
                "Mouse_XAxis" => new Bind(BindKind.MouseAxis, 0),
                "Mouse_YAxis" => new Bind(BindKind.MouseAxis, 1),
                "Mouse_ZAxis" => new Bind(BindKind.MouseAxis, 2),
                "Pos_Mouse_ZAxis" => new Bind(BindKind.Mouse, 5),
                "Neg_Mouse_ZAxis" => new Bind(BindKind.Mouse, 6),
                _ => raw,
            };
        }
        if (DeviceOf(st, device) is not { } dev) return raw;
        var b = Regex.Match(key, @"^Joy_(\d+)$");
        if (b.Success) return Bind.Button(dev, int.Parse(b.Groups[1].Value) - 1);
        var pov = Regex.Match(key, @"^Joy_POV([1-4])(Up|Right|Down|Left)$");
        if (pov.Success) return Bind.HatDir(dev, int.Parse(pov.Groups[1].Value) - 1, Array.IndexOf(Dirs, pov.Groups[2].Value));
        var half = Regex.Match(key, @"^(Pos|Neg)_(Joy_\w+Axis)$");
        int a = Array.IndexOf(AxisKeys, half.Success ? half.Groups[2].Value : key);
        if (a < 0) return raw;
        return new Bind(BindKind.Axis, a, dev, Half: !half.Success ? 0 : half.Groups[1].Value == "Pos" ? 1 : -1);
    }

    private static DeviceId? DeviceOf(State st, string device)
    {
        if (device is "GamePad" or "Keyboard" or "Mouse") return null;
        if (st.ByName.TryGetValue(device, out var vp)) return new DeviceId(vp.Vid, vp.Pid);
        if (device.Length == 8 && int.TryParse(device[..4], NumberStyles.HexNumber, null, out int vid) && int.TryParse(device[4..], NumberStyles.HexNumber, null, out int pid))
            return new DeviceId(vid, pid);
        return null;
    }

    /// <summary>The game's name for a controller.</summary>
    private static string DeviceName(State st, DeviceId d) => st.Names.TryGetValue((d.Vid, d.Pid), out var n) ? n : $"{d.Vid:X4}{d.Pid:X4}";

    // ---------------------------------------------------------------- what it takes

    public int Slots(BinderAction a) => a.IsAxis ? 1 : 2;
    public string SlotName(BinderAction a, int slot) => a.IsAxis ? T("Axis") : slot == 0 ? T("Primary") : T("Secondary");

    public string? Refuse(TitleData data, BinderAction a, int slot, Bind b)
    {
        if (b.IsNone) return null;
        var st = Of(data);
        if (a.IsAxis && !b.IsAxis) return T("This takes an axis: move a stick, throttle or the mouse");
        if (!a.IsAxis && b.IsAxis) return T("This takes a button or key");
        if (b.Kind == BindKind.Other) return b.Raw != null && b.Raw.StartsWith('<') ? null : T("Elite can't take that");
        switch (b.Kind)
        {
            case BindKind.Key when Keys.Elite(b.Code) == null: return T("Elite doesn't know that key");
            case BindKind.Mouse when b.Code is 4 or > 6: return T("Elite doesn't take that mouse button");
            case BindKind.Button when b.Code >= 32: return T("Elite reads a controller's first 32 buttons");
            case BindKind.Hat when b.Hat > 3: return T("Elite reads a controller's first 4 hats");
        }
        if (b.IsController && st.Pads.Contains((b.Device!.Value.Vid, b.Device.Value.Pid))) return T("Elite reads this one as a gamepad: bind it in the game");
        if (b.IsController && b.Device!.Value.Nth > 0) return T("Elite can't tell two of the same controller apart");
        return null;
    }

    // ---------------------------------------------------------------- writing

    public void Save(TitleData data, IInputSource input)
    {
        var st = Of(data);
        var root = st.Doc.Root!;
        int changed = 0;
        foreach (var a in data.Actions)
        {
            var now = data.Get(a);
            if (st.Read.TryGetValue(a.Id, out var was) && now.SequenceEqual(was)) continue;
            var e = root.Element(a.Id);
            if (e == null) continue;
            if (a.IsAxis)
            {
                WriteOne(st, e.Element("Binding")!, now[0]);
                if (e.Element("Inverted") is { } inv) inv.SetAttributeValue("Value", now[0].Inverted ? "1" : "0");
            }
            else
            {
                if (e.Element("Primary") is { } p) WriteOne(st, p, now[0]);
                if (e.Element("Secondary") is { } s) WriteOne(st, s, now[1]);
            }
            st.Read[a.Id] = (Bind[])now.Clone();
            changed++;
        }
        if (changed == 0) return;
        var sb = new StringBuilder();
        using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true }))
            st.Doc.Root!.Save(w);
        Paths.WriteAtomically(st.File, "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\r\n" + sb + st.End, new UTF8Encoding(false));
        Logging.Log.Here.Info($"Binder: Elite: {changed} actions written to {st.File}");
    }

    private static void WriteOne(State st, XElement slot, Bind b)
    {
        if (b.Kind == BindKind.Other && b.Raw != null)
        {
            var old = XElement.Parse(b.Raw);
            slot.ReplaceAttributes(old.Attributes());
            slot.ReplaceNodes(old.Nodes());
            return;
        }
        slot.Elements("Modifier").Remove();
        if (!slot.HasElements) slot.RemoveNodes();
        string device = NoDevice, key = "";
        switch (b.Kind)
        {
            case BindKind.Key:
                device = "Keyboard";
                key = Keys.Elite(b.Code) ?? "";
                foreach (var (m, k) in new[] { (Mods.Shift, "Key_LeftShift"), (Mods.Ctrl, "Key_LeftControl"), (Mods.Alt, "Key_LeftAlt") })
                    if (b.Mods.HasFlag(m)) slot.Add(new XElement("Modifier", new XAttribute("Device", "Keyboard"), new XAttribute("Key", k)));
                break;
            case BindKind.Mouse:
                device = "Mouse";
                key = b.Code switch { 5 => "Pos_Mouse_ZAxis", 6 => "Neg_Mouse_ZAxis", _ => $"Mouse_{b.Code + 1}" };
                break;
            case BindKind.MouseAxis:
                device = "Mouse";
                key = new[] { "Mouse_XAxis", "Mouse_YAxis", "Mouse_ZAxis" }[b.Code];
                break;
            case BindKind.Button:
                device = DeviceName(st, b.Device!.Value);
                key = $"Joy_{b.Code + 1}";
                break;
            case BindKind.Hat:
                device = DeviceName(st, b.Device!.Value);
                key = $"Joy_POV{b.Hat + 1}{Dirs[b.Code]}";
                break;
            case BindKind.Axis:
                device = DeviceName(st, b.Device!.Value);
                key = (b.Half > 0 ? "Pos_" : b.Half < 0 ? "Neg_" : "") + AxisKeys[b.Code];
                break;
        }
        slot.SetAttributeValue("Device", device);
        slot.SetAttributeValue("Key", key);
    }

    public FrameworkElement? DeviceExtra(TitleData data, LiveDevice device, Action changed)
    {
        var t = Ui.Secondary(DeviceName(Of(data), device.Id));
        t.ToolTip = T("The game's name for it");
        return t;
    }
}
