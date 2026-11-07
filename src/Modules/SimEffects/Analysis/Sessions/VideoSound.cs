using DaisysApp.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.8.1) A race video's sound while it plays: its audio track read with Media Foundation (NAudio's
/// <see cref="MediaFoundationReader"/>), converted to the device's own format and played on the app's output
/// (Settings → General → Audio, else Windows' default). Started with the video's frames and stopped with them.
/// </summary>
internal sealed class VideoSound(string path) : IDisposable
{
    private MediaFoundationReader? reader;
    private MediaFoundationResampler? resampler;
    private WasapiOut? output;
    /// <summary>The file has no sound, or it couldn't be read: not tried again.</summary>
    private bool none;

    /// <summary>Plays from <paramref name="seconds"/> into the video.</summary>
    public void Play(double seconds)
    {
        Stop();
        if (none) return;
        try
        {
            if (reader == null)
            {
                reader = new MediaFoundationReader(path);
                Logging.Log.Here.Info($"Analysis: the race video's sound: {reader.WaveFormat}");
            }
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, reader.TotalTime.TotalSeconds));
            var device = Device();
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
            resampler = new MediaFoundationResampler(reader, DeviceService.GetMixFormat(device)) { ResamplerQuality = 60 };
            output.Init(resampler);
            output.Play();
        }
        catch (Exception ex)
        {
            none = true;
            Stop();
            Logging.Log.Here.Info($"Analysis: the race video's sound can't be played: {ex.Message}");
        }
    }

    public void Stop()
    {
        try { output?.Stop(); } catch { }
        output?.Dispose();
        output = null;
        resampler?.Dispose();
        resampler = null;
    }

    /// <summary>The app's output (Settings → General → Audio), else Windows' default.</summary>
    private static MMDevice Device()
    {
        var e = new MMDeviceEnumerator();
        if (AppAudio.Output is { } id) try { return e.GetDevice(id); } catch { }
        return e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public void Dispose()
    {
        Stop();
        reader?.Dispose();
        reader = null;
    }
}
