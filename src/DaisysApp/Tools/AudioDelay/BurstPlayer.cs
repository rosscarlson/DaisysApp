using NAudio.Wave;

namespace DaisysApp.Tools.AudioDelay;

/// <summary>The test "beep": a 30 ms logarithmic sweep (400 Hz–6 kHz) with soft edges. Sharp to time, easy to find.</summary>
public static class Chirp
{
    public const double Seconds = 0.030;

    public static float[] Make(int sampleRate)
    {
        int n = (int)(Seconds * sampleRate);
        var s = new float[n];
        double f0 = 400, f1 = 6000, k = Math.Log(f1 / f0);
        int fade = (int)(0.003 * sampleRate);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            double phase = 2 * Math.PI * f0 * Seconds / k * (Math.Exp(t / Seconds * k) - 1);
            double env = i < fade ? 0.5 - 0.5 * Math.Cos(Math.PI * i / fade)
                       : i > n - fade ? 0.5 - 0.5 * Math.Cos(Math.PI * (n - i) / fade)
                       : 1;
            s[i] = (float)(Math.Sin(phase) * env);
        }
        return s;
    }
}

/// <summary>
/// Plays chirps at fixed times (seconds from the start of the stream) on every channel, silence in between.
/// <see cref="PositionSeconds"/> tells the caller how far the stream has been read, for switching outputs between beeps.
/// </summary>
public sealed class BurstPlayer : IWaveProvider
{
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly float[] chirp;
    private readonly long[] starts;   // burst start frames
    private readonly float amplitude;
    private readonly int channels, bytesPerSample;
    private readonly bool isFloat;
    private long frame;

    public BurstPlayer(WaveFormat format, IReadOnlyList<double> burstTimes, double amplitude)
    {
        WaveFormat = format;
        channels = format.Channels;
        bytesPerSample = format.BitsPerSample / 8;
        isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubtype);
        bool isPcm = format.Encoding == WaveFormatEncoding.Pcm || (format is WaveFormatExtensible y && y.SubFormat == PcmSubtype);
        if (!(isFloat && bytesPerSample == 4) && !(isPcm && bytesPerSample is 2 or 3 or 4))
            throw new NotSupportedException($"Unsupported output format: {format}");
        chirp = Chirp.Make(format.SampleRate);
        starts = burstTimes.Select(t => (long)Math.Round(t * format.SampleRate)).ToArray();
        this.amplitude = (float)amplitude;
    }

    public WaveFormat WaveFormat { get; }

    public double PositionSeconds => Interlocked.Read(ref frame) / (double)WaveFormat.SampleRate;

    public int Read(byte[] buffer, int offset, int count)
    {
        int frames = count / WaveFormat.BlockAlign;
        long f0 = Interlocked.Read(ref frame);
        int o = offset;
        for (int i = 0; i < frames; i++)
        {
            long f = f0 + i;
            float v = 0;
            foreach (long s in starts)
            {
                long k = f - s;
                if (k >= 0 && k < chirp.Length) { v = chirp[k] * amplitude; break; }
            }
            for (int c = 0; c < channels; c++)
            {
                Write(buffer, o, v);
                o += bytesPerSample;
            }
        }
        Interlocked.Add(ref frame, frames);
        return frames * WaveFormat.BlockAlign;
    }

    private void Write(byte[] b, int o, float v)
    {
        if (isFloat)
        {
            BitConverter.TryWriteBytes(new Span<byte>(b, o, 4), v);
            return;
        }
        switch (bytesPerSample)
        {
            case 2:
                BitConverter.TryWriteBytes(new Span<byte>(b, o, 2), (short)Math.Clamp(v * 32767f, -32768f, 32767f));
                break;
            case 3:
                int s24 = (int)Math.Clamp(v * 8388607f, -8388608f, 8388607f);
                b[o] = (byte)s24; b[o + 1] = (byte)(s24 >> 8); b[o + 2] = (byte)(s24 >> 16);
                break;
            default:
                BitConverter.TryWriteBytes(new Span<byte>(b, o, 4), (int)Math.Clamp(v * 2147483647.0, -2147483648.0, 2147483647.0));
                break;
        }
    }
}
