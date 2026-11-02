using System.Globalization;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Recognition;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Speaking to the AI (since 3.26.6; held since 3.36.12): the app's input (Settings → General → Audio) recorded while the
/// button or key is held, then turned into text by Windows' own speech recognizer (SAPI's desktop dictation, through
/// System.Speech), on this PC: nothing is uploaded. Recorded first and recognized after, so the recognizer's slow start
/// can't lose the first words, and the end of the sound always ends the last phrase.
/// </summary>
public sealed class Dictation : IDisposable
{
    private const int Rate = 16000, BytesPerSecond = Rate * 2;
    private Shared.Audio.AppAudio.Recording? recording;
    private Task<byte[]>? reading;

    public bool Running => recording != null;

    /// <summary>Starts recording; throws if there's no microphone.</summary>
    public void Start()
    {
        var r = Shared.Audio.AppAudio.Record(Rate, 1);
        recording = r;
        reading = Task.Run(() =>
        {
            var sound = new MemoryStream();
            r.CopyTo(sound);
            return sound.ToArray();
        });
    }

    /// <summary>Stops recording and gives the words heard ("" if none); throws if there's no recognizer.</summary>
    public async Task<string> FinishAsync()
    {
        if (recording is not { } r || reading is not { } sound) return "";
        await Task.Delay(250); // (the last syllable often comes as the button goes up)
        r.Finish();
        byte[] heard = await sound;
        r.Dispose();
        recording = null;
        return await Task.Run(() => Recognize(heard));
    }

    /// <summary>The words in 16 kHz mono 16-bit sound. Half a second of silence goes after it, for the last phrase to end.</summary>
    public static string Recognize(byte[] sound)
    {
        if (sound.Length < BytesPerSecond / 4) return "";
        var all = SpeechRecognitionEngine.InstalledRecognizers();
        if (all.Count == 0) throw new InvalidOperationException(T("Windows has no speech recognizer installed"));
        // (the one for Windows' language, else the first)
        var info = all.FirstOrDefault(r => r.Culture.Name == CultureInfo.CurrentUICulture.Name)
                   ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                   ?? all[0];
        var words = new List<string>();
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        using var engine = new SpeechRecognitionEngine(info);
        using var input = new MemoryStream();
        input.Write(sound);
        input.Write(new byte[BytesPerSecond / 2]);
        input.Position = 0;
        engine.LoadGrammar(new DictationGrammar());
        engine.SetInputToAudioStream(input, new SpeechAudioFormatInfo(Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        engine.SpeechRecognized += (_, a) => { if (a.Result.Text is { Length: > 0 } t) words.Add(t); };
        engine.RecognizeCompleted += (_, a) => { error = a.Error; done.Set(); };
        engine.RecognizeAsync(RecognizeMode.Multiple);
        // (it runs much faster than the sound lasts)
        if (!done.Wait(TimeSpan.FromSeconds(60 + sound.Length / BytesPerSecond))) engine.RecognizeAsyncCancel();
        if (error != null) throw error;
        Logging.Log.Here.Info($"Dictation: {sound.Length / BytesPerSecond} s recorded, {words.Count} phrase(s) heard ({info.Name})");
        return string.Join(" ", words);
    }

    public void Dispose()
    {
        recording?.Dispose();
        recording = null;
    }
}
