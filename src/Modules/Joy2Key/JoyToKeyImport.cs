using System.Globalization;
using System.IO;
using System.Text;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Reads JoyToKey's profiles (.cfg files, in Documents\JoyToKey by default): keyboard assignments with their auto-repeat,
/// "Keyboard (Multi)" short / long presses, mouse movement, clicks and the wheel, and "run a program". What Joy 2 Key
/// can't do yet is listed in <see cref="Result.Skipped"/>.
/// </summary>
public static class JoyToKeyImport
{
    public sealed class Result
    {
        public required J2KProfile Profile;
        public List<string> Skipped { get; } = new();
        public int Assigned => Profile.Devices.Sum(d => d.Inputs.Count);
    }

    /// <summary>JoyToKey's usual folder, if it's there.</summary>
    public static string? DefaultFolder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var f in new[] { Path.Combine(docs, "JoyToKey"), Path.Combine(docs, "JoyToKey_en") })
            if (Directory.Exists(f)) return f;
        return null;
    }

    /// <summary>The profiles in a folder and its subfolders, as (file, name) pairs.</summary>
    public static List<(string File, string Name)> Find(string folder)
    {
        var list = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.cfg", SearchOption.AllDirectories))
        {
            try
            {
                // only JoyToKey's: they have a [Joystick n] section
                if (!File.ReadLines(file).Take(400).Any(l => l.TrimStart().StartsWith("[Joystick", StringComparison.OrdinalIgnoreCase))) continue;
            }
            catch { continue; }
            string rel = Path.GetRelativePath(folder, file);
            string name = Path.ChangeExtension(rel, null).Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');
            list.Add((file, name));
        }
        return list.OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static Result Read(string file, string name)
    {
        var profile = new J2KProfile { Name = name };
        var result = new Result { Profile = profile };
        string? section = null;
        J2KDevice? device = null;
        int threshold = -1;

        string text;
        var bytes = File.ReadAllBytes(file);
        // JoyToKey writes UTF-16 or ANSI depending on its version
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else text = Encoding.Default.GetString(bytes);

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int comment = line.IndexOf("##", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                device = null;
                if (section.StartsWith("Joystick ", StringComparison.OrdinalIgnoreCase) && int.TryParse(section[9..], out int n))
                {
                    device = profile.Devices.FirstOrDefault(d => d.Number == n);
                    if (device == null)
                    {
                        device = new J2KDevice { Name = F("Joystick {0} (from JoyToKey)", n), Number = n };
                        profile.Devices.Add(device);
                    }
                }
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();

            if (section != null && section.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Threshold", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int t)) threshold = t;
                else if (key.Equals("UsePOV8Way", StringComparison.OrdinalIgnoreCase)) profile.Pov8Way = value == "1";
                continue;
            }
            if (section != null && section.Equals("ButtonAlias", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(F("Button combination {0} (combinations aren't supported yet)", key));
                continue;
            }
            if (device == null) continue;
            if (key.Equals("DefaultDisplayName", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Default", StringComparison.OrdinalIgnoreCase)) continue;

            string where = $"{section} {key}";
            string? input = InputId(key);
            if (input == null)
            {
                if (Fields(value).FirstOrDefault() is { } f0 && f0 != "0") result.Skipped.Add(F("{0}: this input isn't supported yet", where));
                continue;
            }
            var action = Action(Fields(value), where, result.Skipped);
            if (action != null && !action.IsEmpty) device.Inputs[input] = action;
        }

        // JoyToKey's threshold: percent in older versions, 0–1000 in newer
        if (threshold > 0) profile.Threshold = Math.Clamp(threshold > 100 ? threshold / 10 : threshold, 5, 95);
        profile.Devices.RemoveAll(d => d.Inputs.Count == 0);
        return result;
    }

    /// <summary>JoyToKey's name for an input → ours: Button01 → Button1, Axis1n → Axis1-, POV1-1 → PovUp.</summary>
    private static string? InputId(string key)
    {
        if (key.StartsWith("Button", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[6..], out int b))
            return b is >= 1 and <= 32 ? $"Button{b}" : null;
        if (key.StartsWith("Axis", StringComparison.OrdinalIgnoreCase) && key.Length >= 6 && int.TryParse(key[4..^1], out int a) && a is >= 1 and <= 6)
            return char.ToLowerInvariant(key[^1]) switch { 'n' => $"Axis{a}-", 'p' => $"Axis{a}+", _ => null };
        if (key.StartsWith("POV1-", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], out int d) && d is >= 1 and <= 8)
            return Joysticks.PovIds[d - 1];
        return null;
    }

    private static List<string> Fields(string value)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in value)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ',' && !quoted) { list.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        list.Add(sb.ToString().Trim());
        return list;
    }

    private static double Num(List<string> f, int i) =>
        i < f.Count && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static J2KAction? Action(List<string> f, string where, List<string> skipped)
    {
        switch (f.FirstOrDefault())
        {
            case "0": case "": case null:
                return null;
            case "1": // keyboard: keys, auto-repeat (presses a second), ?, delay before repeating
            {
                var a = new J2KAction { Keys = Keys(f.ElementAtOrDefault(1), where, skipped) };
                double rate = Num(f, 2);
                if (rate > 0)
                {
                    a.Mode = PressMode.Repeat;
                    a.RepeatMs = Math.Max(10, (int)Math.Round(1000 / rate));
                    a.RepeatDelayMs = (int)Num(f, 4);
                    a.PressMs = Math.Clamp(a.RepeatMs / 2, 1, 50);
                }
                return a;
            }
            case "7": // Keyboard (Multi): a mode, a time, then up to four key sets
            {
                int mode = (int)Num(f, 1), ms = (int)Num(f, 2);
                var a = new J2KAction { Keys = Keys(f.ElementAtOrDefault(3), where, skipped) };
                var second = Keys(f.ElementAtOrDefault(4), where, skipped);
                if (mode == 3 && second.Count > 0)
                {
                    a.LongMs = Math.Max(ms, 50);
                    a.LongKeys = second;
                }
                else if (second.Count > 0)
                    skipped.Add(F("{0}: Keyboard (Multi) mode {1}: only its first keys were brought in", where, mode));
                return a;
            }
            case "2": // mouse: x, y, wheel, left, middle, right…
            {
                int x = (int)Num(f, 1), y = (int)Num(f, 2), wheel = (int)Num(f, 3);
                var clicks = new List<string>();
                if (Num(f, 4) != 0) clicks.Add("MouseLeft");
                if (Num(f, 5) != 0) clicks.Add("MouseMiddle");
                if (Num(f, 6) != 0) clicks.Add("MouseRight");
                if (clicks.Count > 0)
                {
                    if (x != 0 || y != 0 || wheel != 0) skipped.Add(F("{0}: mouse movement together with a click: only the click was brought in", where));
                    return new J2KAction { Keys = clicks };
                }
                if (wheel != 0)
                {
                    if (x != 0 || y != 0) skipped.Add(F("{0}: mouse movement together with the wheel: only the wheel was brought in", where));
                    return new J2KAction
                    {
                        Keys = { wheel > 0 ? "WheelUp" : "WheelDown" }, Mode = PressMode.Repeat,
                        RepeatMs = Math.Clamp(3000 / Math.Abs(wheel), 20, 2000), PressMs = 1,
                    };
                }
                // JoyToKey's speed setting, roughly in pixels a second
                return new J2KAction { Kind = ActionKind.Mouse, MouseX = x * 16, MouseY = y * 16 };
            }
            case "9": // run a program
            {
                string program = f.ElementAtOrDefault(1) ?? "";
                if (program.Length == 0) return null;
                return new J2KAction { Kind = ActionKind.Run, Program = program, Arguments = f.Count > 2 && f[2] != "0" ? f[2] : null };
            }
            default:
                skipped.Add(F("{0}: a JoyToKey function (type {1}) Joy 2 Key doesn't have yet", where, f[0]));
                return null;
        }
    }

    /// <summary>"A2:5B:00:00" (virtual-key codes in hex) → key ids.</summary>
    private static List<string> Keys(string? codes, string where, List<string> skipped)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(codes)) return list;
        foreach (var part in codes.Split(':'))
        {
            if (!int.TryParse(part.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk) || vk == 0) continue;
            var k = vk <= 0xFF ? KeyCatalog.FromVk(vk) : null;
            if (k == null) { skipped.Add(F("{0}: JoyToKey's special key {1:X} (a mouse click or Numpad Enter, set from its right-click menu) isn't known yet", where, vk)); continue; }
            if (!list.Contains(k.Id) && list.Count < KeyPicker.MaxKeys) list.Add(k.Id);
        }
        return list;
    }
}
