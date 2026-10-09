using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// One audio track of a recording: what the PC is playing (WASAPI loopback of the default playback device) or the
/// default microphone, as 16-bit stereo at 48 or 44.1 kHz for the AAC encoder. Keeps in step with the video's clock:
/// silence is filled in when Windows sends nothing (nothing playing), and a little is dropped if the device's clock
/// runs ahead.
/// </summary>
internal sealed class AudioTrack : IDisposable
{
    private readonly IWaveIn capture;
    private readonly int channels;
    private readonly int inRate;
    private readonly bool isFloat;
    private readonly int bytesPerSample;
    private readonly Action<byte[], int, long> write; // pcm, bytes, start in 100 ns units
    private readonly object gate = new();
    private long startTicks;
    private long framesWritten;
    private bool running;
    private double resamplePos; // only when the device runs at another rate
    private float lastL, lastR;
    private readonly Timer silenceTimer;

    public int SampleRate { get; }
    public string Name { get; }

    public AudioTrack(bool microphone, Action<byte[], int, long> write)
    {
        this.write = write;
        if (microphone)
        {
            var mic = new WasapiCapture(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications), true, 20);
            Name = mic.WaveFormat.ToString();
            capture = mic;
        }
        else
        {
            capture = new WasapiLoopbackCapture();
            Name = capture.WaveFormat.ToString();
        }
        var wf = capture.WaveFormat;
        channels = wf.Channels;
        inRate = wf.SampleRate;
        bytesPerSample = wf.BitsPerSample / 8;
        isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat ||
                  wf is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        SampleRate = inRate is 44100 or 48000 ? inRate : 48000;
        capture.DataAvailable += OnData;
        silenceTimer = new Timer(_ => FillSilence(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts capturing, with time zero at <paramref name="startTicks"/> (Stopwatch ticks).</summary>
    public void Start(long startTicks)
    {
        this.startTicks = startTicks;
        running = true;
        capture.StartRecording();
        silenceTimer.Change(200, 200);
    }

    private long ExpectedFrames() => (long)((Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency * SampleRate);

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!running || e.BytesRecorded == 0) return;
        int frames = e.BytesRecorded / (bytesPerSample * channels);
        var pcm = Convert(e.Buffer, frames, out int outFrames);
        lock (gate)
        {
            if (!running) return;
            long expectedEnd = ExpectedFrames();
            long expectedStart = expectedEnd - outFrames;
            // behind (nothing was sent for a while, or the recording just started): fill the gap with silence
            if (expectedStart - framesWritten > SampleRate / 20) WriteSilence(expectedStart - framesWritten);
            int skip = 0;
            // ahead (the device's clock is faster than ours): drop the start of this packet
            if (framesWritten - expectedStart > SampleRate / 10) skip = (int)Math.Min(outFrames, framesWritten - expectedStart - SampleRate / 50);
            if (skip < outFrames) WriteFrames(pcm, skip, outFrames - skip);
        }
    }

    private void FillSilence()
    {
        lock (gate)
        {
            if (!running) return;
            long behind = ExpectedFrames() - framesWritten;
            if (behind > SampleRate / 5) WriteSilence(behind - SampleRate / 20); // leave room for a packet on its way
        }
    }

    private void WriteSilence(long frames)
    {
        while (frames > 0)
        {
            int n = (int)Math.Min(frames, SampleRate / 10);
            WriteFrames(new byte[n * 4], 0, n);
            frames -= n;
        }
    }

    private void WriteFrames(byte[] pcm, int firstFrame, int frames)
    {
        var bytes = firstFrame == 0 ? pcm : pcm.AsSpan(firstFrame * 4, frames * 4).ToArray();
        write(bytes, frames * 4, framesWritten * 10_000_000L / SampleRate);
        framesWritten += frames;
    }

    /// <summary>To 16-bit stereo: surround is folded down (centre and rear at -3 dB), mono doubled, other rates resampled.</summary>
    private byte[] Convert(byte[] buffer, int frames, out int outFrames)
    {
        var stereo = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            int b = i * channels * bytesPerSample;
            float S(int ch) => Sample(buffer, b + ch * bytesPerSample);
            float l, r;
            if (channels == 1) l = r = S(0);
            else
            {
                l = S(0);
                r = S(1);
                if (channels >= 3) { float c = S(2) * 0.707f; l += c; r += c; }
                if (channels >= 6) { l += S(4) * 0.707f; r += S(5) * 0.707f; }
                if (channels >= 8) { l += S(6) * 0.707f; r += S(7) * 0.707f; }
            }
            stereo[i * 2] = l;
            stereo[i * 2 + 1] = r;
        }

        if (inRate == SampleRate)
        {
            outFrames = frames;
            var outBytes = new byte[frames * 4];
            for (int i = 0; i < frames * 2; i++) WriteShort(outBytes, i * 2, stereo[i]);
            return outBytes;
        }

        // linear resampling (only devices set to rates AAC doesn't take, e.g. 96 kHz), between the previous packet's
        // last frame and this one's: position 0 is that last frame, position k is this packet's frame k - 1
        double step = (double)inRate / SampleRate;
        var result = new List<byte>((int)(frames / step + 2) * 4);
        var tmp = new byte[4];
        while (resamplePos < frames)
        {
            int i = (int)resamplePos;
            double t = resamplePos - i;
            float l0 = i == 0 ? lastL : stereo[(i - 1) * 2], r0 = i == 0 ? lastR : stereo[(i - 1) * 2 + 1];
            float l1 = stereo[i * 2], r1 = stereo[i * 2 + 1];
            WriteShort(tmp, 0, (float)(l0 + (l1 - l0) * t));
            WriteShort(tmp, 2, (float)(r0 + (r1 - r0) * t));
            result.AddRange(tmp);
            resamplePos += step;
        }
        resamplePos -= frames;
        lastL = stereo[^2];
        lastR = stereo[^1];
        outFrames = result.Count / 4;
        return result.ToArray();
    }

    private float Sample(byte[] b, int offset)
    {
        if (isFloat) return BitConverter.ToSingle(b, offset);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, offset) / 32768f,
            3 => ((b[offset] | b[offset + 1] << 8 | (sbyte)b[offset + 2] << 16)) / 8388608f,
            4 => BitConverter.ToInt32(b, offset) / 2147483648f,
            _ => 0,
        };
    }

    private static void WriteShort(byte[] b, int offset, float v)
    {
        short s = (short)(Math.Clamp(v, -1f, 1f) * 32767);
        b[offset] = (byte)s;
        b[offset + 1] = (byte)(s >> 8);
    }

    public void Stop()
    {
        lock (gate) running = false;
        silenceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        try { capture.StopRecording(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        silenceTimer.Dispose();
        capture.DataAvailable -= OnData;
        capture.Dispose();
    }
}
