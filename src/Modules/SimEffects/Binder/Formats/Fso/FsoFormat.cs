using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.SimEffects.Binder.Input;

namespace DaisysApp.Applets.SimEffects.Binder.Formats.Fso;

/// <summary>
/// FreeSpace Open mods installed by Knossos.NET (Fate of the Galaxy), from FotG Controls: the actions are the engine's
/// (<see cref="ActionCatalog"/>) as the mod's tables change them; the bindings a preset (<c>data\players\presets\name.json</c>);
/// which controller is Joystick 0–3, and Use Mouse, the mod's settings ini. Controllers are read through the game's own SDL.
/// Options: <c>mod</c> (the mod's id, FotG_R). Two slots: Primary and Secondary.
/// </summary>
public sealed class FsoFormat : IBindFormat
{
    public string Id => "fso";
    public InputKind Input => InputKind.Sdl;

    private static readonly string[] SlotKeys = ["Joystick", "Joystick1", "Joystick2", "Joystick3"];

    /// <summary>What the format keeps of a title: the install, the actions the mod hides (written back as they were), and
    /// which device each of Joystick 0–3 is.</summary>
    private sealed class State
    {
        public required GameInstall Install;
        public readonly Dictionary<string, (FsoBind, FsoBind)> Hidden = new();
        public readonly Dictionary<string, ModAction> Mod = new();
        /// <summary>The device in each slot (a Vid of 0: one not known, numbered as its slot).</summary>
        public readonly DeviceId?[] Slots = new DeviceId?[4];
        public readonly string?[] Ini = new string?[4];
        public bool SlotsChanged, MouseChanged, UseMouse;
    }

    private static State Of(TitleData d) => (State)d.State!;

    private static GameInstall Open(TitleDef def, string folder) => GameInstall.Open(folder, def.Option("mod", "FotG_R"), def.Name);

    public IReadOnlyList<Check> Check(TitleDef def, string folder)
    {
        try
        {
            var gi = Open(def, folder);
            return
            [
                new(F("{0} {1}", def.Name, gi.ModVersion), true, gi.ModFolder),
                new(gi.EngineVersion is { } v ? F("FSO {0} and its SDL2.dll", v) : T("FSO's SDL2.dll"), gi.SdlPath != null && File.Exists(gi.SdlPath), gi.SdlPath),
                new(T("Presets folder"), true, gi.PresetFolder),
            ];
        }
        catch (Exception ex) { return [new(ex.Message, false)]; }
    }

    public TitleData Load(TitleDef def, TitleSetup setup)
    {
        var gi = Open(def, setup.Folder);
        var st = new State { Install = gi };
        for (int i = 0; i < 4; i++)
        {
            var raw = gi.ReadSetting("Input", SlotKeys[i]);
            st.Ini[i] = string.IsNullOrWhiteSpace(raw) || raw == "null" ? null : raw;
        }
        // (the slots' devices, numbered among alike ones in their SDL order, as SdlSource numbers them)
        var saved = Enumerable.Range(0, 4).Select(i => (Slot: i, Parsed: ParseSlot(st.Ini[i]))).Where(x => x.Parsed != null).OrderBy(x => x.Parsed!.Value.Id).ToList();
        var seen = new Dictionary<(int, int), int>();
        foreach (var (slot, parsed) in saved)
        {
            var vp = SdlSource.VidPid(parsed!.Value.Guid);
            seen[vp] = seen.TryGetValue(vp, out int n) ? n + 1 : 0;
            st.Slots[slot] = new DeviceId(vp.Vid, vp.Pid, seen[vp]);
        }
        st.UseMouse = gi.ReadSetting("Input", "UseMouse")?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        var data = new TitleData { Def = def, Setup = setup, State = st };
        int order = 0;
        foreach (var a in gi.Actions.OrderBy(a => a.Def.IsAxis ? 0 : 1).ThenBy(a => a.Tab).ThenBy(a => a.Def.Id))
        {
            st.Mod[a.Def.Name] = a;
            if (a.Disabled) { st.Hidden[a.Def.Name] = (a.DefaultPrimary, a.DefaultSecondary); continue; }
            var json = def.Action(a.Def.Name);
            if (json?.Hidden == true) { st.Hidden[a.Def.Name] = (a.DefaultPrimary, a.DefaultSecondary); continue; }
            string label = json?.Label ?? FlightName(a);
            data.Actions.Add(new BinderAction
            {
                Id = a.Def.Name, Label = label, GameLabel = label == a.Label ? null : a.Label,
                Group = json?.Group ?? (a.Def.IsAxis ? T("Axes") : TabName(a.Tab)),
                IsAxis = a.Def.IsAxis, Centred = a.Def.Type == CcType.AxisRel, Role = json?.Role, Order = order++,
                Defaults = [ToBind(st, a.DefaultPrimary), ToBind(st, a.DefaultSecondary)],
            });
        }

        var preset = Preset(setup);
        if (GameInstall.IsValidPresetName(preset, out _) && File.Exists(gi.PresetPath(preset)))
        {
            var read = gi.ReadPreset(preset);
            foreach (var a in data.Actions)
                if (read.TryGetValue(a.Id, out var b)) data.Binds[a.Id] = [ToBind(st, b.Primary), ToBind(st, b.Secondary)];
            foreach (var name in st.Hidden.Keys.ToList())
                if (read.TryGetValue(name, out var b)) st.Hidden[name] = b;
        }
        data.Summary = $"{def.Name} {gi.ModVersion}" + (gi.EngineVersion is null ? "" : $" · FSO {gi.EngineVersion}");
        return data;
    }

    private static string Preset(TitleSetup s) => s.Option("preset", "MyControls");

    private static string TabName(Tab t) => t switch
    {
        Tab.Targeting => T("Targeting"),
        Tab.Ship => T("Ship"),
        Tab.Weapons => T("Weapons"),
        _ => T("Computer"),
    };

    /// <summary>Flight-sim names for the rotations: Roll and Yaw for the game's Bank and Turn (asked for in FotG Controls).</summary>
    private static string FlightName(ModAction a) => a.Def.Name switch
    {
        "JOY_HEADING_AXIS" => "Yaw Axis",
        "JOY_BANK_AXIS" or "BANK_LEFT" or "BANK_RIGHT" or "BANK_WHEN_PRESSED" => Regex.Replace(a.Label, @"\bBank\b", "Roll"),
        "YAW_LEFT" or "YAW_RIGHT" => Regex.Replace(a.Label, @"\bTurn\b", "Yaw"),
        _ => a.Label,
    };

    // ---------------------------------------------------------------- FSO's bindings and ours

    private static readonly string[] MouseButtons = FsoBind.MouseButtons;

    private static Bind ToBind(State st, FsoBind f)
    {
        var b = f.Normalized();
        if (b.IsNone || !b.IsLoadable) return Bind.None;
        bool inverted = b.Flags.HasFlag(Ccf.Inverted);
        switch (b.Cid)
        {
            case Cid.Keyboard:
            {
                var s = b.Input;
                var mods = Mods.None;
                if (s.StartsWith("ALT-")) { mods |= Mods.Alt; s = s[4..]; }
                if (s.StartsWith("SHIFT-")) { mods |= Mods.Shift; s = s[6..]; }
                return Keys.FromFso(s) is { } code ? Bind.Key(code, mods) : Bind.None;
            }
            case Cid.Mouse:
                return b.IsAxis
                    ? new Bind(BindKind.MouseAxis, Array.IndexOf(FsoBind.MouseAxes, b.Input), Inverted: inverted)
                    : new Bind(BindKind.Mouse, Array.IndexOf(MouseButtons, b.Input));
            default:
            {
                int slot = b.JoySlot;
                var dev = st.Slots[slot] ?? new DeviceId(0, 0, slot);
                if (b.IsAxis) return Bind.Axis(dev, Array.IndexOf(FsoBind.JoyAxes, b.Input), inverted);
                int n = int.Parse(b.Input);
                return n >= 128 ? Bind.HatDir(dev, 0, n - 128) : Bind.Button(dev, n);
            }
        }
    }

    private static FsoBind ToFso(State st, Bind b, bool assign)
    {
        switch (b.Kind)
        {
            case BindKind.Key:
                return Keys.Fso(b.Code) is { } name && !b.Mods.HasFlag(Mods.Ctrl)
                    ? FsoBind.Key((b.Mods.HasFlag(Mods.Alt) ? "ALT-" : "") + (b.Mods.HasFlag(Mods.Shift) ? "SHIFT-" : "") + name) : FsoBind.None;
            case BindKind.Mouse:
                return b.Code >= 0 && b.Code < MouseButtons.Length ? FsoBind.MouseButton(MouseButtons[b.Code]) : FsoBind.None;
            case BindKind.MouseAxis:
                return b.Code is >= 0 and < 3 ? FsoBind.Axis(Cid.Mouse, FsoBind.MouseAxes[b.Code]).WithInverted(b.Inverted) : FsoBind.None;
            case BindKind.Button or BindKind.Hat or BindKind.Axis when b.Half == 0:
            {
                int slot = SlotOf(st, b.Device!.Value, assign);
                if (slot < 0) return FsoBind.None;
                var cid = Cid.Joy0 + slot;
                return b.Kind switch
                {
                    BindKind.Axis => b.Code is >= 0 and < 8 ? FsoBind.Axis(cid, FsoBind.JoyAxes[b.Code]).WithInverted(b.Inverted) : FsoBind.None,
                    BindKind.Hat => FsoBind.JoyButton(cid, 128 + b.Code),
                    _ => FsoBind.JoyButton(cid, b.Code),
                };
            }
            default:
                return FsoBind.None;
        }
    }

    /// <summary>The Joystick 0–3 a device is (a free one given to it if <paramref name="assign"/>), or -1.</summary>
    private static int SlotOf(State st, DeviceId d, bool assign)
    {
        if (d.Vid == 0 && d.Pid == 0) return d.Nth;
        int at = Array.IndexOf(st.Slots, d);
        if (at >= 0 || !assign) return at;
        at = Array.IndexOf(st.Slots, null);
        if (at >= 0) { st.Slots[at] = d; st.SlotsChanged = true; }
        return at;
    }

    // ---------------------------------------------------------------- what it takes

    public int Slots(BinderAction a) => 2;
    public string SlotName(BinderAction a, int slot) => slot == 0 ? T("Primary") : T("Secondary");

    public string? Refuse(TitleData data, BinderAction a, int slot, Bind b)
    {
        if (b.IsNone) return null;
        var st = Of(data);
        if (b.Kind == BindKind.Other || b.Half != 0) return T("FSO can't take that");
        if (a.IsAxis && !b.IsAxis) return T("This takes an axis: move a stick, throttle or the mouse");
        if (!a.IsAxis && b.IsAxis) return T("This takes a button or key: axes only steer and throttle in FSO");
        switch (b.Kind)
        {
            case BindKind.Key:
                if (b.Mods.HasFlag(Mods.Ctrl)) return T("FSO has no Ctrl combinations (Ctrl on its own works)");
                if (Keys.Fso(b.Code) == null) return T("FSO doesn't know that key");
                if (Keys.IsModifier(b.Code) && b.Mods != Mods.None) return T("FSO doesn't take that");
                break;
            case BindKind.Mouse when b.Code > 8:
                return T("FSO doesn't take that");
            case BindKind.Hat when b.Hat != 0:
                return T("FSO binds only a controller's first hat");
            case BindKind.Button when b.Code >= 128:
                return T("FSO takes a controller's first 128 buttons");
            case BindKind.Axis when b.Code >= 8:
                return T("FSO reads a controller's first 8 axes");
        }
        if (b.IsController && SlotOf(st, b.Device!.Value, false) < 0 && !st.Slots.Contains(null))
            return T("All four of FSO's joysticks are taken: free one in Devices");
        return null;
    }

    // ---------------------------------------------------------------- writing

    public void Save(TitleData data, IInputSource input)
    {
        var st = Of(data);
        var name = Preset(data.Setup);
        if (!GameInstall.IsValidPresetName(name, out var err)) throw new BinderException(err);
        var byId = data.Actions.ToDictionary(a => a.Id);
        var list = new List<(string, FsoBind, FsoBind)>();
        foreach (var m in st.Install.Actions)
        {
            (FsoBind p, FsoBind s) = byId.TryGetValue(m.Def.Name, out var a)
                ? (ToFso(st, data.Get(a)[0], true), ToFso(st, data.Get(a)[1], true))
                : st.Hidden.TryGetValue(m.Def.Name, out var h) ? h : (m.DefaultPrimary, m.DefaultSecondary);
            if (!p.IsLoadable) p = FsoBind.None;
            if (!s.IsLoadable) s = FsoBind.None;
            list.Add((m.Def.Name, p, s));
        }
        st.Install.WritePreset(name, list);
        WriteIni(st, input);
    }

    private static void WriteIni(State st, IInputSource input)
    {
        if (!st.SlotsChanged && !st.MouseChanged) return;
        var values = new Dictionary<string, string>();
        if (st.SlotsChanged)
        {
            for (int i = 0; i < 4; i++)
            {
                var live = st.Slots[i] is { Vid: not 0 } d ? input.Find(d) : null;
                if (live?.SdlGuid != null) values[SlotKeys[i]] = new JsonObject { ["guid"] = live.SdlGuid, ["id"] = live.SdlIndex }.ToJsonString();
                else if (st.Slots[i] == null && ParseSlot(st.Ini[i]) is { } saved && input.Devices.Any(x => x.SdlGuid == saved.Guid))
                    values[SlotKeys[i]] = "null"; // a device plugged in now was moved out of this slot
                // (else the slot keeps a device that isn't plugged in now)
            }
        }
        if (st.MouseChanged) values["UseMouse"] = st.UseMouse ? "true" : "false";
        if (values.Count > 0) st.Install.WriteSettings("Input", values);
        foreach (var (k, v) in values)
        {
            int i = Array.IndexOf(SlotKeys, k);
            if (i >= 0) st.Ini[i] = v == "null" ? null : v;
        }
        st.SlotsChanged = st.MouseChanged = false;
    }

    private static (string Guid, int Id)? ParseSlot(string? json)
    {
        if (json == null) return null;
        try
        {
            var n = JsonNode.Parse(json);
            var guid = (string?)n?["guid"];
            return guid == null ? null : (guid.ToUpperInvariant(), (int?)n?["id"] ?? 0);
        }
        catch { return null; }
    }

    public string? SdlPath(TitleData data) => Of(data).Install.SdlPath;

    // ---------------------------------------------------------------- its own lines

    public FrameworkElement? Details(TitleData data, Action changed, Action reload)
    {
        var st = Of(data);
        var presets = new ComboBox { IsEditable = true, Width = 180, Text = Preset(data.Setup), ToolTip = T("In the game: Options → Control Config → Next Preset, then Accept") };
        System.Windows.Automation.AutomationProperties.SetName(presets, T("Preset"));
        foreach (var p in st.Install.ListPresets()) presets.Items.Add(p);
        var error = Ui.Secondary();
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Take()
        {
            var name = presets.Text.Trim();
            if (name == Preset(data.Setup)) return;
            if (!GameInstall.IsValidPresetName(name, out var err)) { error.Text = err; return; }
            error.Text = "";
            data.Setup.Options["preset"] = name;
            // (an existing preset is read; a new name gets these bindings)
            if (File.Exists(st.Install.PresetPath(name))) reload(); else changed();
        }
        presets.SelectionChanged += (_, e) => { if (e.AddedItems.Count > 0 && e.AddedItems[0] is string s) { presets.Text = s; Take(); } };
        presets.LostKeyboardFocus += (_, _) => Take();
        presets.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Take(); };

        var mouse = new CheckBox { Content = T("Use Mouse"), IsChecked = st.UseMouse, Margin = new Thickness(0, 6, 0, 0), ToolTip = T("The game's own Use Mouse option") };
        Ui.OnToggle(mouse, on => { st.UseMouse = on; st.MouseChanged = true; changed(); });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = T("Preset"), Width = 70, VerticalAlignment = VerticalAlignment.Center }, presets } };
        return new StackPanel { Children = { row, error, mouse } };
    }

    public FrameworkElement? DeviceExtra(TitleData data, LiveDevice device, Action changed)
    {
        var st = Of(data);
        var box = new ComboBox { Width = 110, ToolTip = T("Which of the game's Joystick 0–3 it is") };
        System.Windows.Automation.AutomationProperties.SetName(box, F("Joystick for {0}", device.Name));
        box.Items.Add(T("Not used"));
        for (int i = 0; i < 4; i++) box.Items.Add(F("Joystick {0}", i));
        box.SelectedIndex = Array.IndexOf(st.Slots, device.Id) + 1;
        box.SelectionChanged += (_, _) =>
        {
            int slot = box.SelectedIndex - 1;
            for (int i = 0; i < 4; i++) if (st.Slots[i] == device.Id) st.Slots[i] = null;
            if (slot >= 0) st.Slots[slot] = device.Id;
            st.SlotsChanged = true;
            changed();
        };
        return box;
    }
}
