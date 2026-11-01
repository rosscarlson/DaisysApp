using System.Collections.Concurrent;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The AI's voice (since 3.25.2): Windows' voices through SAPI's COM objects (no package): its desktop voices and the
/// OneCore ones (Speech_OneCore, the larger set Windows 10/11 install), spoken one after another on a thread of its own.
/// Narrator's natural voices (since 3.36.9) aren't open to other programs: SAPI lists them once NaturalVoiceSAPIAdapter
/// is registered on the PC (not shipped), and "" then means Narrator's own voice. The adapter's cloud voices aren't offered.
/// A session speaks with "DaisysApp.exe --say text" (Core's Speech), as its system prompt says when a voice is picked.
/// </summary>
public static class Voice
{
    public const string Off = "Off";
    private const string Desktop = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
    private const string OneCore = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private const string NarratorKey = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Narrator\NoRoam";
    private static readonly BlockingCollection<(string Text, string Voice)> queue = new();
    private static Thread? worker;

    /// <summary>Nothing is spoken (the voice button, since 3.35.3): what's being said stops, and what's waiting is dropped.</summary>
    public static volatile bool Muted;

    /// <summary>What a Claude Code session is told (--append-system-prompt): short, as it's sent on every call.</summary>
    public static string Prompt => $"You can speak aloud to the user: run \"{Environment.ProcessPath}\" {Shell.Speech.Argument} \"text\". Say as little as possible every time: a few plain words, never more than one short sentence, no details, lists, numbers or summaries (those go in your written reply). Speak when they ask you to, and also right before you need to take over the PC they're at (its mouse, keyboard, focus or screen; a few words that vary, like: Need the computer a moment), and when they can have it back (like: All done). Not otherwise.";

    /// <summary>The voices installed (names, each once).</summary>
    public static List<string> Names()
    {
        var names = new List<string>();
        RunSta(() =>
        {
            foreach (string cat in new[] { Desktop, OneCore })
                try
                {
                    dynamic c = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory")!)!;
                    c.SetId(cat, false);
                    foreach (dynamic t in c.EnumerateTokens())
                        if (t.GetDescription() is string n && !n.Contains(" Online ") && !names.Contains(n)) names.Add(n);
                }
                catch (Exception ex) { Logging.Log.Here.Debug($"Voices in {cat}: {ex.Message}"); }
        });
        return names;
    }

    private static string NarratorName => Microsoft.Win32.Registry.GetValue(NarratorKey, "SpeechVoice", null) as string ?? "";

    /// <summary>The voice Narrator is set to, if it's one SAPI can speak with here; else null.</summary>
    public static string? Narrator() => Names().Contains(NarratorName) ? NarratorName : null;

    /// <summary>Speaks the text with the voice named ("" = Narrator's if SAPI has it, else Windows' default), after anything still being said.</summary>
    public static void Say(string text, string voice)
    {
        if (Muted || voice == Off || text.Trim().Length == 0) return;
        lock (queue)
            if (worker == null)
            {
                worker = new Thread(Work) { IsBackground = true, Name = "AI voice" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
        queue.Add((text, voice));
    }

    private static void Work()
    {
        foreach (var (text, voice) in queue.GetConsumingEnumerable())
            try
            {
                if (Muted) continue;
                // (a new one each time: Windows' default voice is the one it starts with)
                dynamic sp = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!)!;
                if (Token(voice) is { } token) sp.Voice = token;
                // the app's output (Settings → General → Audio), if one is picked
                if (Shared.Audio.AppAudio.WaveOut is int device and >= 0)
                {
                    dynamic output = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpMMAudioOut")!)!;
                    output.DeviceId = device;
                    sp.AudioOutputStream = output;
                }
                // (asynchronously, so the mute can cut it off: 1 is SVSFlagsAsync, 2 SVSFPurgeBeforeSpeak)
                sp.Speak(text, 1);
                while (!sp.WaitUntilDone(100))
                    if (Muted) { sp.Speak("", 2); break; }
            }
            catch (Exception ex) { Logging.Log.Here.Warn($"Speaking: {ex.Message}"); }
    }

    private static dynamic? Token(string name)
    {
        if (name.Length == 0) name = NarratorName;
        if (name.Length == 0) return null;
        foreach (string cat in new[] { Desktop, OneCore })
            try
            {
                dynamic c = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory")!)!;
                c.SetId(cat, false);
                foreach (dynamic t in c.EnumerateTokens())
                    if (t.GetDescription() == name) return t;
            }
            catch { }
        return null;
    }

    private static void RunSta(Action a)
    {
        var t = new Thread(() => a()) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }
}
