using DaisysApp.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.8.1) A race video's sound while it plays: its audio track read with Media Foundation (NAudio's
/// <see cref="MediaFoundationReader"/>), converted to the device's own format and played on the app's output
/// (Settings → General → Audio, else Windows' default). Started with the video's frames and stopped with them. Since
/// 4.13.3 its volume is its own (<see cref="Gain"/>): 4.10.9–4.13.2 set the output's, which is the device's.
/// </summary>
internal sealed class VideoSound(string path) : IDisposable
{
    private MediaFoundationReader? reader;
    private MediaFoundationResampler? resampler;
    private WasapiOut? output;
    /// <summary>The file has no sound, or it couldn't be read: not tried again.</summary>
    private bool none;
    private volatile float level = 1;

    /// <summary>(since 4.10.9) Its volume, 0 to 1 (the video's volume control), heard at once.</summary>
    public float Level
    {
        get => level;
        set => level = Math.Clamp(value, 0, 1);
    }

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
            output.Init(new Gain(resampler, () => level));
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

    /// <summary>(since 4.13.3) The sound scaled by the volume as it's read (32-bit float or 16-bit samples); the
    /// slider's square, so half way sounds about half as loud.</summary>
    private sealed class Gain(IWaveProvider source, Func<float> level) : IWaveProvider
    {
        private readonly bool floats = source.WaveFormat.BitsPerSample == 32 && (source.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
            || source.WaveFormat is WaveFormatExtensible { SubFormat: var f } && f == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            float g = level();
            g *= g;
            if (g >= 0.999f || n <= 0) return n;
            if (g <= 0) { Array.Clear(buffer, offset, n); return n; }
            if (floats)
                foreach (ref float v in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, n & ~3))) v *= g;
            else if (WaveFormat.BitsPerSample == 16)
                foreach (ref short v in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset, n & ~1))) v = (short)(v * g);
            return n;
        }
    }
}
