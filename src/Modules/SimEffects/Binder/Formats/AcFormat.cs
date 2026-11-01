using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using DaisysApp.Applets.SimEffects.Binder.Input;

namespace DaisysApp.Applets.SimEffects.Binder.Formats;

/// <summary>
/// Assetto Corsa: <c>cfg\controls.ini</c> in its Documents folder, a section per action (JOY, AXLE, BUTTON, KEY), the
/// controllers listed under [CONTROLLERS] (CONn, PGUIDn: DirectInput's product GUID, __IGUIDn: its instance) and JOY the
/// number in that list; KEY a Windows virtual key. The H-shifter's gears are [SHIFTER]'s GEAR_n, with one JOY for all.
/// The actions are the title's JSON's (an id is a section, or "SHIFTER.GEAR_n"); their slots "axis", "button" and "key"
/// (an axis and a button in one section share its JOY). Lines Binder doesn't change stay as they were.
/// </summary>
public sealed class AcFormat : IBindFormat
{
    public string Id => "ac";
    public InputKind Input => InputKind.DirectInput;

    private sealed class State
    {
        public required string File;
        public required List<string> Lines;
        /// <summary>The controllers [CONTROLLERS] lists, by JOY number.</summary>
        public readonly List<DeviceId?> Joys = new();
        /// <summary>Each action's bindings as last read or written: only the ones that differ are written.</summary>
        public readonly Dictionary<string, Bind[]> Read = new();
    }

    private static State Of(TitleData d) => (State)d.State!;

    private static string Ini(string folder) => Path.Combine(folder, "controls.ini");

    public IReadOnlyList<Check> Check(TitleDef def, string folder) =>
        !Directory.Exists(folder) ? [new(F("The folder {0} doesn't exist", folder), false)]
        : [new("controls.ini", File.Exists(Ini(folder)), Ini(folder))];

    // ---------------------------------------------------------------- the ini

    private static (int Start, int End) Section(List<string> lines, string name)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith('[') || !t.EndsWith(']')) continue;
            if (start >= 0) return (start, i);
            if (t[1..^1].Equals(name, StringComparison.OrdinalIgnoreCase)) start = i;
        }
        return (start, lines.Count);
    }

    private static string? Get(List<string> lines, string section, string key)
    {
        var (s, e) = Section(lines, section);
        if (s < 0) return null;
        for (int i = s + 1; i < e; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq > 0 && lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var v = lines[i][(eq + 1)..];
                int semi = v.IndexOf(';');
                return (semi >= 0 ? v[..semi] : v).Trim();
            }
        }
        return null;
    }

    private static int GetInt(List<string> lines, string section, string key)
    {
        var v = Get(lines, section, key);
        if (v == null) return -1;
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(v[2..], NumberStyles.HexNumber, null, out int h) ? h : -1;
        return int.TryParse(v, out int n) ? n : -1;
    }

    private static void Set(List<string> lines, string section, string key, string value)
    {
        var (s, e) = Section(lines, section);
        if (s < 0)
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            return;
        }
        for (int i = s + 1; i < e; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq > 0 && lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = $"{key}={value}"; return; }
        }
        int at = e;
        while (at - 1 > s && lines[at - 1].Trim().Length == 0) at--;
        lines.Insert(at, $"{key}={value}");
    }

    // ---------------------------------------------------------------- reading

    /// <summary>An id's section, and its own key where it has one ("SHIFTER.GEAR_1", "KEYBOARD.GAS").</summary>
    private static (string Section, string? Sub) Where(string id) =>
        id.IndexOf('.') is int dot and > 0 ? (id[..dot], id[(dot + 1)..]) : (id, null);

    private static bool IsGear(string id) => id.StartsWith("SHIFTER.", StringComparison.OrdinalIgnoreCase);

    private static string[] SlotKinds(ActionDef? a, bool axis) =>
        a?.Slots is { Count: > 0 } s ? s.ToArray() : axis ? ["axis"] : ["button", "key"];

    private string[] SlotKinds(BinderAction a) => kinds.TryGetValue(a, out var k) ? k : a.IsAxis ? ["axis"] : ["button", "key"];

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<BinderAction, string[]> kinds = new();

    public TitleData Load(TitleDef def, TitleSetup setup)
    {
        var file = Ini(setup.Folder);
        if (!File.Exists(file)) throw new BinderException(F("No controls.ini in {0}", setup.Folder));
        var st = new State { File = file, Lines = File.ReadAllLines(file).ToList() };
        ReadJoys(st);
        var data = new TitleData { Def = def, Setup = setup, State = st, Summary = $"{def.Name} · controls.ini" };
        int order = 0;
        foreach (var j in def.Actions)
        {
            if (j.Hidden) continue;
            bool axis = j.Axis ?? false;
            var slotKinds = SlotKinds(j, axis);
            var action = new BinderAction
            {
                Id = j.Id, Label = j.Label ?? j.Id, Group = j.Group ?? T("Other"), IsAxis = axis, Centred = j.Centred ?? false, Role = j.Role, Order = order++,
                Defaults = slotKinds.Select(k => k == "key" ? j.Keys.Select(Keys.Parse).FirstOrDefault(b => b != null) ?? Bind.None : Bind.None).ToArray(),
            };
            kinds.AddOrUpdate(action, slotKinds);
            data.Actions.Add(action);
            data.Binds[j.Id] = slotKinds.Select(k => ReadSlot(st, j.Id, k)).ToArray();
            st.Read[j.Id] = (Bind[])data.Binds[j.Id].Clone();
        }
        return data;
    }

    private static void ReadJoys(State st)
    {
        st.Joys.Clear();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int n = 0; ; n++)
        {
            var guid = Get(st.Lines, "CONTROLLERS", $"PGUID{n}");
            if (guid == null) break;
            if (guid.Length >= 8 && int.TryParse(guid[..8], NumberStyles.HexNumber, null, out int pidVid))
            {
                seen[guid] = seen.TryGetValue(guid, out int k) ? k + 1 : 0;
                st.Joys.Add(new DeviceId(pidVid & 0xFFFF, (pidVid >> 16) & 0xFFFF, seen[guid]));
            }
            else st.Joys.Add(null);
        }
    }

    private static Bind ReadSlot(State st, string id, string kind)
    {
        var (section, sub) = Where(id);
        var lines = st.Lines;
        int joy = GetInt(lines, section, "JOY");
        DeviceId? dev = joy >= 0 && joy < st.Joys.Count ? st.Joys[joy] : null;
        switch (kind)
        {
            case "axis":
                int axle = GetInt(lines, section, "AXLE");
                return dev is { } d && axle is >= 0 and < 8 ? Bind.Axis(d, axle) : Bind.None;
            case "button":
                int button = GetInt(lines, section, sub ?? "BUTTON");
                return dev is { } d2 && button >= 0 ? Bind.Button(d2, button) : Bind.None;
            default:
                int vk = GetInt(lines, section, sub ?? "KEY");
                return vk > 0 && Keys.FromVk(vk) is { } code ? Bind.Key(code) : Bind.None;
        }
    }

    // ---------------------------------------------------------------- what it takes

    public int Slots(BinderAction a) => SlotKinds(a).Length;

    public string SlotName(BinderAction a, int slot) => SlotKinds(a)[slot] switch
    {
        "axis" => T("Axis"),
        "button" => T("Controller"),
        _ => T("Key"),
    };

    public string? Refuse(TitleData data, BinderAction a, int slot, Bind b)
    {
        if (b.IsNone) return null;
        var kind = SlotKinds(a)[slot];
        switch (kind)
        {
            case "axis" when b.Kind != BindKind.Axis || b.Half != 0: return T("This takes a controller's axis");
            case "button" when b.Kind == BindKind.Hat: return T("Assetto Corsa doesn't take hats");
            case "button" when b.Kind != BindKind.Button: return T("This takes a controller's button: keys go in Key");
            case "key" when b.Kind != BindKind.Key: return T("This takes a key");
            case "key" when b.Mods != Mods.None: return T("Assetto Corsa doesn't take Shift, Ctrl or Alt with a key");
            case "key" when Keys.ToVk(b.Code) == 0: return T("Assetto Corsa doesn't know that key");
        }
        if (!b.IsController) return null;
        // (one JOY per section: an axis and a button in one, or all the gears, on one controller)
        var binds = data.Get(a);
        for (int i = 0; i < binds.Length; i++)
            if (i != slot && binds[i].IsController && binds[i].Device != b.Device) return T("Its other binding is on another controller: Assetto Corsa takes one");
        if (IsGear(a.Id))
            foreach (var other in data.Actions.Where(o => o != a && IsGear(o.Id)))
                if (data.Get(other).FirstOrDefault(x => x.IsController) is { } g && g.Device != b.Device) return T("Every gear goes on one shifter in Assetto Corsa");
        return null;
    }

    public bool CanInvert(BinderAction a) => false;

    // ---------------------------------------------------------------- writing

    public void Save(TitleData data, IInputSource input)
    {
        var st = Of(data);
        var lines = st.Lines;
        int changed = 0;
        foreach (var a in data.Actions)
        {
            var (section, sub) = Where(a.Id);
            var binds = data.Get(a);
            if (st.Read.TryGetValue(a.Id, out var was) && binds.SequenceEqual(was)) continue;
            st.Read[a.Id] = (Bind[])binds.Clone();
            changed++;
            var k = SlotKinds(a);
            var dev = binds.FirstOrDefault(x => x.IsController)?.Device;
            int joy = dev is { } d ? JoyOf(st, d, input) : -1;
            if (IsGear(a.Id))
            {
                if (dev != null) { Set(lines, section, "JOY", joy.ToString()); Set(lines, section, "ACTIVE", "1"); }
                Set(lines, section, sub!, binds[0].IsController ? binds[0].Code.ToString() : "-1");
                continue;
            }
            if (Get(lines, section, "JOY") != null || dev != null) Set(lines, section, "JOY", joy.ToString());
            for (int i = 0; i < k.Length; i++)
            {
                var b = binds[i];
                switch (k[i])
                {
                    case "axis": Set(lines, section, "AXLE", b.Kind == BindKind.Axis ? b.Code.ToString() : "-1"); break;
                    case "button": Set(lines, section, sub ?? "BUTTON", b.Kind == BindKind.Button ? b.Code.ToString() : "-1"); break;
                    default:
                        Set(lines, section, sub ?? "KEY", b.Kind == BindKind.Key ? $"0x{Keys.ToVk(b.Code):X2} ; {Keys.Label(b.Code)}" : "-1");
                        break;
                }
            }
        }
        if (changed == 0) return;
        Paths.WriteAtomically(st.File, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        Logging.Log.Here.Info($"Binder: Assetto Corsa: {changed} actions written to {st.File}");
    }

    /// <summary>A controller's JOY number, listed under [CONTROLLERS] if it wasn't.</summary>
    private static int JoyOf(State st, DeviceId d, IInputSource input)
    {
        int at = st.Joys.IndexOf(d);
        if (at >= 0) return at;
        int n = st.Joys.Count;
        var live = input.Find(d);
        Set(st.Lines, "CONTROLLERS", $"CON{n}", live?.Name ?? d.ToString());
        if (live != null && live.Instance != Guid.Empty) Set(st.Lines, "CONTROLLERS", $"__IGUID{n}", live.Instance.ToString().ToUpperInvariant());
        Set(st.Lines, "CONTROLLERS", $"PGUID{n}", $"{d.Pid:X4}{d.Vid:X4}-0000-0000-0000-504944564944");
        st.Joys.Add(d);
        return n;
    }

    public FrameworkElement? DeviceExtra(TitleData data, LiveDevice device, Action changed)
    {
        int at = Of(data).Joys.IndexOf(device.Id);
        var t = Ui.Secondary(at >= 0 ? F("JOY {0}", at) : T("Not listed yet"));
        t.ToolTip = T("Its number in controls.ini");
        return t;
    }
}
