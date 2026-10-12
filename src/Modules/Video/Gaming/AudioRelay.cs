using DaisysApp.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// The virtual webcam's sound. A webcam has no sound, and Discord (like any call app) takes sound only from a
/// microphone, so this records one playback device as it plays (the device the game plays on, so it's only the game)
/// and plays it into another: a virtual cable such as Voicemeeter's AUX or VAIO3 input, or VB-CABLE. The cable's other
/// end is a recording device, which Discord can take as its microphone. Resampled and remapped to the cable's format.
/// </summary>
internal sealed class AudioRelay : IDisposable
{
    private static readonly Logger log = Log.Here;

    /// <summary>The playback devices that are on, by name (both the game's device and the cable are playback devices).</summary>
    public static List<(string Id, string Name)> PlaybackDevices()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => { using (d) return (d.ID, d.FriendlyName); })
                .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            log.Warn("Couldn't list the playback devices", ex);
            return [];
        }
    }

    /// <summary>Whether a device looks like a virtual cable (Voicemeeter's inputs, VB-CABLE, other virtual audio cables).</summary>
    public static bool LooksLikeCable(string name) =>
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) || name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private readonly MMDevice fromDevice, toDevice;
    private readonly WasapiLoopbackCapture capture;
    private readonly BufferedWaveProvider buffer;
    private readonly WasapiOut output;

    public string Description { get; }

    public AudioRelay(string fromId, string toId)
    {
        if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("The sound can't be sent to the device it comes from."));
        using var e = new MMDeviceEnumerator();
        fromDevice = e.GetDevice(fromId);
        toDevice = e.GetDevice(toId);
        capture = new WasapiLoopbackCapture(fromDevice);
        buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(1), ReadFully = true };
        capture.DataAvailable += (_, a) => buffer.AddSamples(a.Buffer, 0, a.BytesRecorded);

        var mix = toDevice.AudioClient.MixFormat;
        ISampleProvider sound = buffer.ToSampleProvider();
        if (sound.WaveFormat.SampleRate != mix.SampleRate) sound = new WdlResamplingSampleProvider(sound, mix.SampleRate);
        if (sound.WaveFormat.Channels != mix.Channels) sound = new ChannelMap(sound, mix.Channels);
        output = new WasapiOut(toDevice, AudioClientShareMode.Shared, true, 60);
        output.Init(new SampleToWaveProvider(sound));
        Description = F("{0} → {1}", fromDevice.FriendlyName, toDevice.FriendlyName);
        capture.StartRecording();
        output.Play();
        log.Info($"Sound relay: {fromDevice.FriendlyName} ({capture.WaveFormat}) → {toDevice.FriendlyName} ({mix})");
    }

    public void Dispose()
    {
        try { capture.StopRecording(); } catch { }
        try { output.Stop(); } catch { }
        capture.Dispose();
        output.Dispose();
        fromDevice.Dispose();
        toDevice.Dispose();
    }

    /// <summary>
    /// Sound with another number of channels: front left and right go to front left and right; a source with more
    /// channels is folded down to its first two, one with fewer has the rest left silent.
    /// </summary>
    private sealed class ChannelMap(ISampleProvider source, int channels) : ISampleProvider
    {
        private float[] scratch = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);

        public int Read(float[] dest, int offset, int count)
        {
            int inCh = source.WaveFormat.Channels, frames = count / channels;
            if (scratch.Length < frames * inCh) scratch = new float[frames * inCh];
            int got = source.Read(scratch, 0, frames * inCh) / inCh;
            for (int f = 0; f < got; f++)
            {
                float l = scratch[f * inCh], r = inCh > 1 ? scratch[f * inCh + 1] : l;
                for (int c = 0; c < channels; c++)
                    dest[offset + f * channels + c] = channels == 1 ? (l + r) * 0.5f : c == 0 ? l : c == 1 ? r : 0;
            }
            return got * channels;
        }
    }
}
