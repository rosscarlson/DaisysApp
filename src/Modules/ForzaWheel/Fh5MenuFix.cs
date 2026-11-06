using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// FH5's menu fix (Steam only). FH5 gives a wheel it doesn't know (vJoy, 1234:BEAD) only its custom profiles, whose
/// menu bindings are empty and can't be set in the game, so the paddles don't change tabs and the d-pad doesn't move in
/// menus. Its known wheels (Fanatec, Logitech, Moza…) have a default profile each in <c>media\inputmappingprofiles.zip</c>,
/// keyed by VID/PID; this adds one for vJoy (as roxrage/forza_moza_mapping does for Moza): the emulator's layout
/// (<see cref="Roles"/>) for driving, and in menus the paddles as LB / RB, the hat as the d-pad, A, B, X, Y, Menu and
/// View. The Store / Game Pass copy's files can't be changed. A game update or Steam's Verify puts the game's zip back
/// (the fix is applied again then); the first time, the game's own is kept beside it as <c>.daisysapp-backup</c>.
/// </summary>
internal static class Fh5MenuFix
{
    public const string Entry = "DefaultRawGameControllerMappingProfileForzaWheelVJoy.xml";
    private const string VidPid = "0x1234bead";
    private const string Backup = ".daisysapp-backup";

    public sealed record Outcome(bool Ok, string Message);

    /// <summary>The Steam copy's inputmappingprofiles.zip, in any Steam library (null: not found).</summary>
    public static string? FindSteamZip()
    {
        foreach (var library in SteamLibraries())
        {
            string zip = Path.Combine(library, "steamapps", "common", "ForzaHorizon5", "media", "inputmappingprofiles.zip");
            if (File.Exists(zip)) return zip;
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var steam = new List<string>();
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p) steam.Add(p.Replace('/', '\\'));
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string q) steam.Add(q);
        }
        catch { }
        steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steam)
        {
            if (seen.Add(root)) yield return root;
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            string text;
            try { text = File.Exists(vdf) ? File.ReadAllText(vdf) : ""; } catch { continue; }
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                string library = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(library)) yield return library;
            }
        }
    }

    /// <summary>Whether the zip has the fix in it now.</summary>
    public static bool IsApplied(string zip)
    {
        try
        {
            using var z = ZipFile.OpenRead(zip);
            return z.GetEntry(Entry) != null;
        }
        catch { return false; }
    }

    /// <summary>Adds (or replaces) the vJoy profile in the zip: built in a copy in %TEMP%, then copied over it (as
    /// administrator if the folder needs it).</summary>
    public static async Task<Outcome> ApplyAsync(string zip)
    {
        string dir = Path.Combine(Path.GetTempPath(), "DaisysApp");
        string temp = Path.Combine(dir, "inputmappingprofiles.zip");
        bool first;
        try
        {
            Directory.CreateDirectory(dir);
            File.Copy(zip, temp, true);
            using (var z = ZipFile.Open(temp, ZipArchiveMode.Update))
            {
                first = z.GetEntry(Entry) == null;
                z.GetEntry(Entry)?.Delete();
                using var w = new StreamWriter(z.CreateEntry(Entry, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(Profile());
            }
        }
        catch (IOException ex) { return new Outcome(false, T("Couldn't read the game's profiles: ") + ex.Message); }
        catch (InvalidDataException ex) { return new Outcome(false, T("Couldn't read the game's profiles: ") + ex.Message); }

        try
        {
            if (first && !File.Exists(zip + Backup)) File.Copy(zip, zip + Backup);
            File.Copy(temp, zip, true);
        }
        catch (UnauthorizedAccessException)
        {
            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var copies = new List<(string, string)>();
            if (first && !File.Exists(zip + Backup)) copies.Add((cmd, $"/c copy /y \"{zip}\" \"{zip + Backup}\""));
            copies.Add((cmd, $"/c copy /y \"{temp}\" \"{zip}\""));
            var result = await Elevated.RunAsync(copies);
            if (!result.Ok) return new Outcome(false, T("Couldn't write the game's profiles: ") + result.Output);
        }
        catch (IOException) { return new Outcome(false, T("The game's profiles are in use: close Forza Horizon 5 and try again.")); }
        finally { try { File.Delete(temp); } catch { } }

        bool ok = IsApplied(zip);
        Logging.Log.Here.Info($"FH5 menu fix applied to {zip}: {ok}");
        return ok
            ? new Outcome(true, T("Done. In Forza Horizon 5: Settings → Controls → Wheel, pick the default profile (remap anything you changed after)."))
            : new Outcome(false, T("Couldn't write the game's profiles."));
    }

    // ---------------------------------------------------------------- the profile

    // WGI counts the vJoy device's buttons from 0 (vJoy button n is n - 1), its axes X, Y, Z, Rx, Ry, Rz as 0–5 and the hat as switch 0
    private static int B(string role) => Roles.Find(role)!.Output - 1;
    private static int A(string role) => Roles.Find(role)!.Output;

    /// <summary>The profile, in the format of the game's own (DefaultRawGameControllerMappingProfileFanatec*.xml).</summary>
    public static string Profile()
    {
        var s = new StringBuilder();
        void Button(string key, int index) => s.Append($"    <Value Version=\"1\" Key=\"{key}\" VidPid=\"{VidPid}\" InputType=\"Button\" Index=\"{index}\" />\n");
        void Hat(string key, string position) => s.Append($"    <Value Version=\"1\" Key=\"{key}\" VidPid=\"{VidPid}\" InputType=\"Switch\" Index=\"0\" SwitchPosition=\"{position}\" />\n");
        void Axis(string key, int index, bool centred = false) => s.Append($"    <Value Version=\"1\" Key=\"{key}\" VidPid=\"{VidPid}\" InputType=\"Axis\" Index=\"{index}\" InvertAxis=\"false\"{(centred ? " DeadzonesAroundCenter=\"true\"" : "")} InnerDeadzone=\"0.0\" OuterDeadzone=\"1.0\" />\n");
        void Pressed(string key, int index) { foreach (var e in new[] { "PRESS", "RELEASE", "REPEAT" }) Button($"INPUTCMD_UI_{key}_{e}", index); }
        void HatPressed(string key, string position) { foreach (var e in new[] { "PRESS", "RELEASE", "REPEAT" }) Hat($"INPUTCMD_UI_{key}_{e}", position); }
        void Context(string name, Action body) { s.Append($"  <Context Version=\"1\" Context=\"{name}\">\n"); body(); s.Append("  </Context>\n\n"); }
        void HatPair(string key, string low, string high) => s.Append(
            $"    <Value Version=\"1\" Key=\"{key}\">\n" +
            $"      <InputCmdLow VidPid=\"{VidPid}\" InputType=\"Switch\" Index=\"0\" SwitchPosition=\"{low}\" />\n" +
            $"      <InputCmdHigh VidPid=\"{VidPid}\" InputType=\"Switch\" Index=\"0\" SwitchPosition=\"{high}\" />\n" +
            "    </Value>\n");
        void Driving()
        {
            Button("INPUTCMD_PAUSE_GAME", B("Start"));
            Button("INPUTCMD_SWITCH_CAMERA", B("Camera"));
            Button("INPUTCMD_HORN", B("Horn"));
            Hat("INPUTCMD_ANNA_ACTIVATE", "Up");
            Hat("INPUTCMD_TELEMETRY_TOGGLE", "Down");
            Hat("INPUTCMD_TELEMETRY_PREV", "Left");
            Hat("INPUTCMD_TELEMETRY_NEXT", "Right");
            Button("INPUTCMD_ACTIVATE", B("Back"));
        }

        s.Append("<Profiles>\n");
        s.Append("<RawGameControllerInputMappingProfile Version=\"1\" Id=\"7c3e9f2a-5d41-4b8e-9a6f-d4a5f0e1b2c3\" UserFacingName=\"IDS_DefaultWheelProfile_Name\" " +
                 $"IsDefaultProfile=\"1\" PrimaryDeviceVidPid=\"{VidPid}\" FFBDeviceVidPid=\"{VidPid}\" FFBMotorIndex=\"0\">\n\n");
        s.Append("  <!-- Daisy's App ForzaWheel: its vJoy wheel (1234:BEAD); in menus the paddles are LB / RB and the hat the d-pad -->\n\n");
        Context("INPUTCONTEXT_RACING", () =>
        {
            Axis("INPUTCMD_GAS", A("Throttle"));
            Axis("INPUTCMD_BRAKE", A("Brake"));
            Axis("INPUTCMD_CLUTCH", A("Clutch"));
            Axis("INPUTCMD_STEERING", A("Steering"), centred: true);
            Axis("INPUTCMD_HANDBRAKE", A("Handbrake"));
            Button("INPUTCMD_SHIFTUP", B("ShiftUp"));
            Button("INPUTCMD_SHIFTDOWN", B("ShiftDown"));
            Button("INPUTCMD_MULLIGAN", B("Rewind"));
            Button("INPUTCMD_LOOK_BACK", B("LookBack"));
            Driving();
            Button("INPUTCMD_GEAR_REVERSE", B("GearR"));
            string[] gears = { "FIRST", "SECOND", "THIRD", "FOURTH", "FIFTH", "SIXTH", "SEVENTH" };
            for (int g = 0; g < gears.Length; g++) Button($"INPUTCMD_GEAR_{gears[g]}", B($"Gear{g + 1}"));
        });
        Context("INPUTCONTEXT_RACING_UI", () =>
        {
            Hat("INPUTCMD_ANNA_ACTIVATE", "Up");
            Hat("INPUTCMD_PHOTO_MODE_TOGGLE", "Down");
            Hat("INPUTCMD_QUICKCHAT_ACTIVATE", "Left");
            Hat("INPUTCMD_RADIO_RIGHT", "Right");
        });
        Context("INPUTCONTEXT_ANNA", () =>
        {
            Hat("INPUTCMD_ANNA_ITEM_1", "Up");
            Hat("INPUTCMD_ANNA_ITEM_2", "Left");
            Hat("INPUTCMD_ANNA_ITEM_3", "Right");
            Hat("INPUTCMD_ANNA_ITEM_4", "Down");
        });
        Context("INPUTCONTEXT_UI", () =>
        {
            foreach (var (key, position) in new[] { ("UP", "Up"), ("DOWN", "Down"), ("LEFT", "Left"), ("RIGHT", "Right") })
            {
                Hat($"INPUTCMD_UI_DPAD_{key}_PRESS", position);
                HatPressed(key, position);
                Hat($"INPUTCMD_UI_BRICKCHALLENGES_{key}", position);
            }
            HatPair("INPUTCMD_UI_MAP_MOVE_LEFTRIGHT", "Left", "Right");
            HatPair("INPUTCMD_UI_MAP_MOVE_UPDOWN", "Down", "Up");
            Pressed("OK", B("A"));
            Button("INPUTCMD_UI_OK_WHILEDOWN", B("A"));
            Pressed("CANCEL", B("B"));
            Button("INPUTCMD_UI_CANCEL_WHILEDOWN", B("B"));
            Pressed("START", B("Start"));
            Pressed("BACK", B("Back"));
            Pressed("X", B("X"));
            Pressed("Y", B("Y"));
            Pressed("LBUMPER", B("ShiftDown"));
            Pressed("RBUMPER", B("ShiftUp"));
            Button("INPUTCMD_MULLIGAN", B("Rewind"));
        });
        Context("INPUTCONTEXT_RACING_CAMERA_ONLY", Driving);
        s.Append("</RawGameControllerInputMappingProfile>\n</Profiles>\n");
        return s.ToString();
    }
}
