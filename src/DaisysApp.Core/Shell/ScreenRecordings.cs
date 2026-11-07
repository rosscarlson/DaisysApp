namespace DaisysApp.Shell;

/// <summary>
/// (since Core 0.60, 4.8.0) Screen recording for other modules: Video → Recording offers its recorder (recording what
/// its Record card has chosen: the monitor, region or game, quality and sound), and SimEffects → Analysis records each
/// race with it. Null while Recording is switched off. Called on the UI thread, and the callbacks come there.
/// </summary>
public static class ScreenRecordings
{
    /// <summary>A recording started: its file, and when its first frame was (the video's 0:00).</summary>
    public sealed record Started(string File, DateTime At);

    public interface IRecorder
    {
        /// <summary>Recording, or starting to (anyone's).</summary>
        bool Busy { get; }

        /// <summary>Starts recording; <paramref name="done"/> gets what started, or (null and) why it couldn't.</summary>
        void Start(Action<Started?, string?> done);

        /// <summary>Stops the recording <see cref="Start"/> began (nothing if the user stopped it, or started their own);
        /// <paramref name="done"/> once the file is finished.</summary>
        void Stop(Action? done = null);
    }

    private static IRecorder? recorder;

    /// <summary>The recorder offered (null: none).</summary>
    public static IRecorder? Recorder => recorder;

    /// <summary>Raised when one is offered or withdrawn.</summary>
    public static event Action? Changed;

    public static void Offer(IRecorder? r)
    {
        recorder = r;
        Changed?.Invoke();
    }
}
