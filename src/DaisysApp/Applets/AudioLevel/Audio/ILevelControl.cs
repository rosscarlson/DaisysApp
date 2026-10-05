namespace DaisysApp.Applets.AudioLevel.Audio;

/// <summary>Per-speaker level control: Windows channel volume, or the app's own gain inside a Voicemeeter bus.</summary>
public interface ILevelControl : IDisposable
{
    /// <summary>Short description for the UI, e.g. "Windows channel volume".</summary>
    string Description { get; }
    double MinDb { get; }
    double MaxDb { get; }
    bool CanControl(int channel);
    double Get(int channel);
    void Set(int channel, double db);

    /// <summary>Raised (possibly on another thread) when a level changed outside the knob, e.g. in Sound settings.</summary>
    event Action? Changed;
}
