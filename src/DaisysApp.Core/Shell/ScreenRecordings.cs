using System.Windows;
using System.Windows.Interop;
using DaisysApp.Shared.Win32;

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

    /// <summary>(since SimEffects Analysis 0.3.3) A product's one game (SimEffects' profile of it): its name and its
    /// programs (exe names). Recording offers it first and records its window and sound. Null in Daisy's App.</summary>
    public static (string Name, Func<IReadOnlyCollection<string>> Programs)? Game { get; set; }

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

    private static volatile bool recording;

    /// <summary>(since Core 0.61, 4.10.7) Something in the app is recording (not the virtual webcam): the tray icon's
    /// centre turns red.</summary>
    public static bool Recording => recording;

    /// <summary>Raised (on any thread) when <see cref="Recording"/> changes.</summary>
    public static event Action? RecordingChanged;

    /// <summary>The recorder says it is (or no longer is) recording; any thread.</summary>
    public static void SetRecording(bool on)
    {
        starting = false;
        if (recording != on)
        {
            recording = on;
            RecordingChanged?.Invoke();
        }
        UpdateOverlays();
    }

    // ---------------------------------------------------------------- the app's overlays (since Core 0.68, 4.21.11)

    private static volatile bool starting, recordOverlays = true, overlaysHidden;
    private static readonly object overlayGate = new();

    /// <summary>Video → Recording's Record overlays: off, the app's overlays (widgets, the FPS overlay, mirrors) are
    /// left out of the picture while a recording runs (Windows' exclude-from-capture, set once at the start and end).</summary>
    public static bool RecordOverlays
    {
        get => recordOverlays;
        set { recordOverlays = value; UpdateOverlays(); }
    }

    /// <summary>The overlays are being left out now.</summary>
    public static bool OverlaysHidden => overlaysHidden;

    /// <summary>Raised (on any thread) when <see cref="OverlaysHidden"/> changes.</summary>
    public static event Action? OverlaysHiddenChanged;

    /// <summary>The recorder is about to start (any thread), so the overlays are out before its first frame;
    /// <see cref="SetRecording"/> ends it.</summary>
    public static void Starting()
    {
        starting = true;
        UpdateOverlays();
    }

    private static void UpdateOverlays()
    {
        lock (overlayGate)
        {
            bool hide = (starting || recording) && !recordOverlays;
            if (hide == overlaysHidden) return;
            overlaysHidden = hide;
        }
        OverlaysHiddenChanged?.Invoke();
    }

    /// <summary>Leaves the window out of recordings while <see cref="OverlaysHidden"/>, and always while
    /// <paramref name="always"/> says so (its own setting; call <see cref="Apply"/> after it changes).</summary>
    public static void KeepOut(Window w, Func<bool>? always = null)
    {
        void Changed()
        {
            if (w.Dispatcher.CheckAccess()) Apply(w, always);
            else w.Dispatcher.BeginInvoke(() => Apply(w, always));
        }
        w.SourceInitialized += (_, _) => Apply(w, always);
        OverlaysHiddenChanged += Changed;
        w.Closed += (_, _) => OverlaysHiddenChanged -= Changed;
    }

    /// <summary>Sets the window's exclusion from capture now (once it has a handle).</summary>
    public static void Apply(Window w, Func<bool>? always = null)
    {
        if (new WindowInteropHelper(w).Handle == IntPtr.Zero) return;
        WindowTools.ExcludeFromCapture(w, overlaysHidden || always?.Invoke() == true);
    }
}
