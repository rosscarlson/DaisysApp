using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>The tab's own settings, in %APPDATA%\DaisysApp\Joy2Key.json. The profiles are files of their own.</summary>
public sealed class Joy2KeySettings
{
    private const string FileName = "Joy2Key";

    /// <summary>Whether controller presses are turned into keys at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The profile in use when no profile's game is in front (or always, without <see cref="AutoSwitch"/>).</summary>
    public string Active { get; set; } = "Default";

    /// <summary>Switch to a profile when one of its games is the window in front.</summary>
    public bool AutoSwitch { get; set; } = true;

    /// <summary>The profile showing in the tab.</summary>
    public string? Showing { get; set; }

    /// <summary>Where JoyToKey's profiles were last imported from.</summary>
    public string? ImportFolder { get; set; }

    /// <summary>Show only the inputs that do something.</summary>
    public bool AssignedOnly { get; set; }

    public static Joy2KeySettings Load() => JsonStore.Load<Joy2KeySettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>
/// One profile (usually one game): the controllers it uses and what each of their buttons, axes and POV directions
/// does. Saved as %APPDATA%\DaisysApp\Joy2Key\Profiles\&lt;name&gt;.json.
/// </summary>
public sealed class J2KProfile
{
    public string Name { get; set; } = "Default";

    /// <summary>Programs (e.g. "eldenring.exe") that switch to this profile while they're in front.</summary>
    public List<string> Programs { get; set; } = new();

    /// <summary>How far (percent) an axis has to move before it counts as pressed.</summary>
    public int Threshold { get; set; } = 50;

    /// <summary>The POV hat's diagonals are inputs of their own; otherwise a diagonal presses both its neighbours.</summary>
    public bool Pov8Way { get; set; }

    public List<J2KDevice> Devices { get; set; } = new();

    public J2KProfile Clone() => JsonSerializer.Deserialize<J2KProfile>(JsonSerializer.Serialize(this, ProfileStore.Options), ProfileStore.Options)!;
}

/// <summary>A controller in a profile, and what its inputs do (keyed by input id: "Button1", "Axis2+", "PovUp"…).</summary>
public sealed class J2KDevice
{
    public string Name { get; set; } = "";

    /// <summary>The controller's USB ids, which find it again whichever order controllers were plugged in. 0 for
    /// one imported from JoyToKey (which only knows "joystick 1, 2…") until it's linked to a real one.</summary>
    public int Vid { get; set; }
    public int Pid { get; set; }

    /// <summary>Which of the controllers with those ids (1 = the first), or with no ids, which controller overall.</summary>
    public int Number { get; set; } = 1;

    public Dictionary<string, J2KAction> Inputs { get; set; } = new();

    [JsonIgnore] public bool HasIds => Vid != 0 || Pid != 0;
}

public enum ActionKind { Keys, Mouse, Run, Profile }

public enum PressMode
{
    /// <summary>The keys stay down while the button is held.</summary>
    Hold,
    /// <summary>One press of <see cref="J2KAction.PressMs"/>, however long the button is held.</summary>
    Tap,
    /// <summary>A press every <see cref="J2KAction.RepeatMs"/> while the button is held.</summary>
    Repeat,
    /// <summary>One push presses the keys down, the next lets them go.</summary>
    Toggle,
}

/// <summary>What one input does.</summary>
public sealed class J2KAction
{
    public ActionKind Kind { get; set; }

    /// <summary>Key ids (see <see cref="KeyCatalog"/>), pressed together in this order and let go in reverse.</summary>
    public List<string> Keys { get; set; } = new();
    public PressMode Mode { get; set; }

    /// <summary>How long a tap (Tap, Repeat, or the short press of a long-press input) holds the keys down.</summary>
    public int PressMs { get; set; } = 50;

    /// <summary>Repeat: time from one press to the next, and the wait before the first repeat (0 = the same).</summary>
    public int RepeatMs { get; set; } = 100;
    public int RepeatDelayMs { get; set; }

    /// <summary>Long press: held this long, the input presses <see cref="LongKeys"/> instead (0 = off).</summary>
    public int LongMs { get; set; }
    public List<string> LongKeys { get; set; } = new();

    /// <summary>Mouse: pixels a second at full tilt (negative = left / up).</summary>
    public int MouseX { get; set; }
    public int MouseY { get; set; }

    /// <summary>Run: a program (or file, or web address) and its arguments.</summary>
    public string? Program { get; set; }
    public string? Arguments { get; set; }

    /// <summary>Profile: the profile to switch to.</summary>
    public string? Profile { get; set; }

    [JsonIgnore] public bool UsesLongPress => Kind == ActionKind.Keys && LongMs > 0 && LongKeys.Count > 0;

    [JsonIgnore]
    public bool IsEmpty => Kind switch
    {
        ActionKind.Keys => Keys.Count == 0 && !UsesLongPress,
        ActionKind.Mouse => MouseX == 0 && MouseY == 0,
        ActionKind.Run => string.IsNullOrWhiteSpace(Program),
        _ => string.IsNullOrWhiteSpace(Profile),
    };

    public J2KAction Clone() => JsonSerializer.Deserialize<J2KAction>(JsonSerializer.Serialize(this, ProfileStore.Options), ProfileStore.Options)!;

    /// <summary>A short description for the input's tile.</summary>
    public string Summary()
    {
        switch (Kind)
        {
            case ActionKind.Mouse:
                return F("Mouse {0}", Direction(MouseX, MouseY));
            case ActionKind.Run:
                return F("Run {0}", Path.GetFileName(Program ?? ""));
            case ActionKind.Profile:
                return F("Profile: {0}", Profile);
        }
        string keys = KeyCatalog.Describe(Keys);
        string text = Mode switch
        {
            PressMode.Tap => F("{0} (tap)", keys),
            PressMode.Repeat => F("{0} (repeat)", keys),
            PressMode.Toggle => F("{0} (toggle)", keys),
            _ => keys,
        };
        if (UsesLongPress)
            text = Keys.Count == 0 ? F("Held: {0}", KeyCatalog.Describe(LongKeys)) : F("{0} · held: {1}", keys, KeyCatalog.Describe(LongKeys));
        return text;
    }

    private static string Direction(int x, int y)
    {
        var parts = new List<string>();
        if (y < 0) parts.Add(T("up"));
        if (y > 0) parts.Add(T("down"));
        if (x < 0) parts.Add(T("left"));
        if (x > 0) parts.Add(T("right"));
        return string.Join(" ", parts);
    }
}

/// <summary>Profiles on disk: one JSON file each in %APPDATA%\DaisysApp\Joy2Key\Profiles.</summary>
public static class ProfileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } = Path.Combine(AppPaths.SettingsFolder, "Joy2Key", "Profiles");

    public static List<J2KProfile> LoadAll()
    {
        var list = new List<J2KProfile>();
        try
        {
            if (Directory.Exists(Folder))
                foreach (var file in Directory.GetFiles(Folder, "*.json"))
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<J2KProfile>(File.ReadAllText(file), Options);
                        if (p == null) continue;
                        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = Path.GetFileNameWithoutExtension(file);
                        if (list.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                        list.Add(p);
                    }
                    catch (Exception ex) { Logging.ErrorLog.Write("Joy 2 Key profile " + Path.GetFileName(file), ex); }
                }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Joy 2 Key profiles", ex); }
        if (list.Count == 0)
        {
            var p = new J2KProfile { Name = "Default" };
            Save(p);
            list.Add(p);
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string PathOf(string name) => Path.Combine(Folder, SafeFileName(name) + ".json");

    public static void Save(J2KProfile p)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathOf(p.Name), JsonSerializer.Serialize(p, Options));
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Saving Joy 2 Key profile", ex); }
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathOf(name)); }
        catch (Exception ex) { Logging.ErrorLog.Write("Deleting Joy 2 Key profile", ex); }
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return s.Length == 0 ? "Profile" : s;
    }

    /// <summary>A name not used by any profile: the name itself, or "name (2)", "name (3)"…</summary>
    public static string FreeName(string name, IEnumerable<J2KProfile> profiles)
    {
        var used = profiles.Select(p => SafeFileName(p.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(SafeFileName(name))) return name;
        for (int i = 2; ; i++)
            if (!used.Contains(SafeFileName($"{name} ({i})"))) return $"{name} ({i})";
    }
}
