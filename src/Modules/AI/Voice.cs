using System.Collections.Concurrent;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The AI's voice (since 3.25.2): Windows' voices through SAPI's COM objects (no package): its desktop voices and the
/// OneCore ones (Speech_OneCore, the larger set Windows 10/11 install), spoken one after another on a thread of its own.
/// A session speaks with "DaisysApp.exe --say text" (Core's Speech), as its system prompt says when a voice is picked.
/// </summary>
public static class Voice
{
    public const string Off = "Off";
    private const string Desktop = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
    private const string OneCore = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private static readonly BlockingCollection<(string Text, string Voice)> queue = new();
    private static Thread? worker;

    /// <summary>What a Claude Code session is told (--append-system-prompt): short, as it's sent on every call.</summary>
    public static string Prompt => $"You can speak aloud to the user, only when they ask you to: run \"{Environment.ProcessPath}\" {Shell.Speech.Argument} \"text\" (short, plain sentences).";

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
                        if (t.GetDescription() is string n && !names.Contains(n)) names.Add(n);
                }
                catch (Exception ex) { Logging.Log.Here.Debug($"Voices in {cat}: {ex.Message}"); }
        });
        return names;
    }

    /// <summary>Speaks the text with the voice named ("" = Windows' default), after anything still being said.</summary>
    public static void Say(string text, string voice)
    {
        if (voice == Off || text.Trim().Length == 0) return;
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
                // (a new one each time: Windows' default voice is the one it starts with)
                dynamic sp = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!)!;
                if (Token(voice) is { } token) sp.Voice = token;
                sp.Speak(text, 0);
            }
            catch (Exception ex) { Logging.Log.Here.Warn($"Speaking: {ex.Message}"); }
    }

    private static dynamic? Token(string name)
    {
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
