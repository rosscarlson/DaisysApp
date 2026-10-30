using System.Globalization;
using System.Speech.Recognition;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Speaking to the AI (since 3.26.6): Windows' own speech recognizer (SAPI's desktop dictation, through System.Speech),
/// on this PC, from the default microphone. Nothing is uploaded: what's recognized is text, put in the box as it comes.
/// One at a time per session; <see cref="Stop"/> lets it finish the phrase it's on.
/// </summary>
public sealed class Dictation : IDisposable
{
    private SpeechRecognitionEngine? engine;

    /// <summary>A phrase recognized (on a thread of the engine's).</summary>
    public event Action<string>? Heard;
    /// <summary>It stopped (by Stop, or an error: the message, else null).</summary>
    public event Action<string?>? Ended;

    public bool Running => engine != null;

    /// <summary>Starts listening; throws if there's no recognizer or microphone.</summary>
    public void Start()
    {
        var all = SpeechRecognitionEngine.InstalledRecognizers();
        if (all.Count == 0) throw new InvalidOperationException(T("Windows has no speech recognizer installed"));
        // (the one for Windows' language, else the first)
        var info = all.FirstOrDefault(r => r.Culture.Name == CultureInfo.CurrentUICulture.Name)
                   ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                   ?? all[0];
        var e = new SpeechRecognitionEngine(info);
        try
        {
            e.LoadGrammar(new DictationGrammar());
            e.SetInputToDefaultAudioDevice();
            e.SpeechRecognized += (_, a) => { if (a.Result.Text is { Length: > 0 } t) Heard?.Invoke(t); };
            e.RecognizeCompleted += (_, a) =>
            {
                if (engine != e) return;
                engine = null;
                e.Dispose();
                Ended?.Invoke(a.Error?.Message);
            };
            engine = e;
            e.RecognizeAsync(RecognizeMode.Multiple);
            Logging.Log.Here.Info($"Dictation started ({info.Name}, {info.Culture.Name})");
        }
        catch { engine = null; e.Dispose(); throw; }
    }

    /// <summary>Stops once the phrase being spoken is recognized (Ended follows).</summary>
    public void Stop() => engine?.RecognizeAsyncStop();

    public void Dispose()
    {
        var e = engine;
        engine = null;
        if (e == null) return;
        try { e.RecognizeAsyncCancel(); } catch { }
        e.Dispose();
    }
}
