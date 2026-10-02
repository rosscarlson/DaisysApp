using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace DaisysApp.Tools.AudioLevel.Audio;

public enum SignalType { PinkNoise, PinkNoiseBand, WhiteNoise, Sine }

/// <summary>
/// Generates a test signal on any subset of the device's channels, in the device's own mix format
/// so WASAPI shared mode passes channels straight through. Every channel gets its own uncorrelated
/// noise. Levels are RMS-normalised so the slider reads dBFS RMS for every signal type.
/// </summary>
public sealed class TestSignalProvider : IWaveProvider
{
    private enum SampleKind { Float32, Pcm16, Pcm24, Pcm32 }

    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly int channels;
    private readonly int sampleRate;
    private readonly SampleKind kind;
    private readonly ChannelGenerator[] generators;
    private readonly bool[] isLfe;
    private readonly bool[] enabled;
    private readonly float[] gains;
    private readonly float smoothing;
    private readonly double[] norm = new double[4];
    private volatile LfeTuning lfeTuning;
    private float[] scratch = [];

    private volatile SignalType signal = SignalType.PinkNoise;
    private volatile float level = 0.1f;
    private volatile float sineFrequency = 1000f;
    private volatile bool lfeLowPass = true;
    private volatile bool muted;

    public TestSignalProvider(WaveFormat format, IEnumerable<int> lfeChannels)
    {
        WaveFormat = format;
        channels = format.Channels;
        sampleRate = format.SampleRate;
        kind = DetectKind(format);

        generators = new ChannelGenerator[channels];
        for (int i = 0; i < channels; i++)
            generators[i] = new ChannelGenerator(sampleRate, (uint)Random.Shared.Next(1, int.MaxValue));

        isLfe = new bool[channels];
        foreach (int c in lfeChannels)
            if (c >= 0 && c < channels) isLfe[c] = true;

        enabled = new bool[channels];
        gains = new float[channels];
        smoothing = (float)(1 - Math.Exp(-1.0 / (0.015 * sampleRate))); // ~15 ms fade, no clicks

        norm[(int)SignalType.PinkNoise] = Measure(SignalType.PinkNoise, false);
        norm[(int)SignalType.PinkNoiseBand] = Measure(SignalType.PinkNoiseBand, false);
        norm[(int)SignalType.WhiteNoise] = Measure(SignalType.WhiteNoise, false);
        norm[(int)SignalType.Sine] = Math.Sqrt(2);
        lfeTuning = CreateLfeTuning(DefaultLfeCutoff);
    }

    public WaveFormat WaveFormat { get; }
    public int Channels => channels;

    public SignalType Signal { set => signal = value; }
    public double LevelDb { set => level = (float)Math.Pow(10, value / 20); }
    public double SineFrequency { set => sineFrequency = (float)Math.Clamp(value, 1, sampleRate / 2.0 - 1); }
    public bool LfeLowPass { set => lfeLowPass = value; }

    public const double DefaultLfeCutoff = 80;

    /// <summary>Low-pass corner for the LFE channel. Re-measures the level normalisation here (UI thread), not on the audio thread.</summary>
    public double LfeCutoff
    {
        set
        {
            double hz = Math.Clamp(value, 20, Math.Min(500, sampleRate / 4.0));
            if (Math.Abs(hz - lfeTuning.Cutoff) > 0.01) lfeTuning = CreateLfeTuning(hz);
        }
    }

    private sealed record LfeTuning(double Cutoff, double NormPink, double NormWhite);

    private LfeTuning CreateLfeTuning(double cutoff) =>
        new(cutoff, Measure(SignalType.PinkNoise, true, cutoff), Measure(SignalType.WhiteNoise, true, cutoff));

    public void SetChannelEnabled(int channel, bool on)
    {
        if (channel >= 0 && channel < channels) Volatile.Write(ref enabled[channel], on);
    }

    /// <summary>Fades every channel to silence (used just before stopping to avoid a pop).</summary>
    public void Mute() => muted = true;

    public int Read(byte[] buffer, int offset, int count)
    {
        int frames = count / WaveFormat.BlockAlign;
        int samples = frames * channels;

        if (kind == SampleKind.Float32)
        {
            Render(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, samples * 4)), frames);
        }
        else
        {
            if (scratch.Length < samples) scratch = new float[samples];
            var src = scratch.AsSpan(0, samples);
            Render(src, frames);
            var dst = buffer.AsSpan(offset);
            switch (kind)
            {
                case SampleKind.Pcm16:
                    for (int i = 0; i < samples; i++)
                        BinaryPrimitives.WriteInt16LittleEndian(dst[(i * 2)..], (short)Math.Round(src[i] * 32767f));
                    break;
                case SampleKind.Pcm24:
                    for (int i = 0; i < samples; i++)
                    {
                        int v = (int)Math.Round(src[i] * 8388607f);
                        dst[i * 3] = (byte)v;
                        dst[i * 3 + 1] = (byte)(v >> 8);
                        dst[i * 3 + 2] = (byte)(v >> 16);
                    }
                    break;
                case SampleKind.Pcm32:
                    for (int i = 0; i < samples; i++)
                        BinaryPrimitives.WriteInt32LittleEndian(dst[(i * 4)..], (int)Math.Round(src[i] * 2147483647.0));
                    break;
            }
        }
        return frames * WaveFormat.BlockAlign;
    }

    private void Render(Span<float> dest, int frames)
    {
        var sig = signal;
        float target = muted ? 0f : level;
        float freq = sineFrequency;
        bool lfeLp = lfeLowPass;
        var lt = lfeTuning;

        for (int c = 0; c < channels; c++)
        {
            float t = Volatile.Read(ref enabled[c]) ? target : 0f;
            float g = gains[c];
            if (g == 0f && t == 0f)
            {
                for (int f = 0; f < frames; f++) dest[f * channels + c] = 0f;
                continue;
            }

            var gen = generators[c];
            bool lfe = lfeLp && isLfe[c] && sig != SignalType.Sine;
            if (lfe) gen.SetLfeCutoff(lt.Cutoff);
            double n = lfe ? (sig == SignalType.WhiteNoise ? lt.NormWhite : lt.NormPink) : norm[(int)sig];

            for (int f = 0; f < frames; f++)
            {
                g += (t - g) * smoothing;
                double s = gen.Next(sig, lfe, freq) * n * g;
                dest[f * channels + c] = (float)Math.Clamp(s, -1.0, 1.0);
            }

            if (Math.Abs(g - t) < 1e-6f) g = t;
            gains[c] = g;
        }
    }

    private double Measure(SignalType type, bool lfe, double lfeCutoff = DefaultLfeCutoff)
    {
        var g = new ChannelGenerator(sampleRate, 0x9E3779B9);
        g.SetLfeCutoff(lfeCutoff);
        int warmup = sampleRate / 2, n = sampleRate * 3;
        for (int i = 0; i < warmup; i++) g.Next(type, lfe, 1000);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double s = g.Next(type, lfe, 1000);
            sum += s * s;
        }
        double rms = Math.Sqrt(sum / n);
        return rms > 1e-12 ? 1 / rms : 1;
    }

    private static SampleKind DetectKind(WaveFormat f)
    {
        bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubtype);
        bool isPcm = f.Encoding == WaveFormatEncoding.Pcm || (f is WaveFormatExtensible y && y.SubFormat == PcmSubtype);
        if (isFloat && f.BitsPerSample == 32) return SampleKind.Float32;
        if (isPcm)
        {
            switch (f.BitsPerSample)
            {
                case 16: return SampleKind.Pcm16;
                case 24: return SampleKind.Pcm24;
                case 32: return SampleKind.Pcm32;
            }
        }
        throw new NotSupportedException($"Unsupported device mix format: {f}");
    }

    /// <summary>Per-channel signal state: RNG, pink filter, band/LFE filters, sine phase.</summary>
    private sealed class ChannelGenerator
    {
        private readonly double fs;
        private uint rng;
        private double b0, b1, b2, b3, b4, b5, b6;
        private double phase;
        private readonly Biquad hp20, bandHp1, bandHp2, bandLp1, bandLp2, lfe1, lfe2;
        private double lfeCutoff = DefaultLfeCutoff;

        public ChannelGenerator(int sampleRate, uint seed)
        {
            fs = sampleRate;
            rng = seed == 0 ? 1 : seed;
            const double q = 0.7071;
            hp20 = Biquad.HighPass(fs, 20, q);
            bandHp1 = Biquad.HighPass(fs, 500, q);
            bandHp2 = Biquad.HighPass(fs, 500, q);
            bandLp1 = Biquad.LowPass(fs, 2000, q);
            bandLp2 = Biquad.LowPass(fs, 2000, q);
            lfe1 = Biquad.LowPass(fs, lfeCutoff, q);
            lfe2 = Biquad.LowPass(fs, lfeCutoff, q);
        }

        /// <summary>Retunes the LFE low-pass in place (filter state kept, so no click).</summary>
        public void SetLfeCutoff(double hz)
        {
            if (hz == lfeCutoff) return;
            lfeCutoff = hz;
            lfe1.SetLowPass(fs, hz, 0.7071);
            lfe2.SetLowPass(fs, hz, 0.7071);
        }

        public double Next(SignalType type, bool lfe, float freq)
        {
            if (type == SignalType.Sine)
            {
                double s = Math.Sin(phase);
                phase += 2 * Math.PI * freq / fs;
                if (phase >= 2 * Math.PI) phase -= 2 * Math.PI;
                return s;
            }

            double w = White();
            if (lfe)
            {
                double x = type == SignalType.WhiteNoise ? w : hp20.Process(Pink(w));
                return lfe2.Process(lfe1.Process(x));
            }

            return type switch
            {
                SignalType.WhiteNoise => w,
                SignalType.PinkNoiseBand => bandLp2.Process(bandLp1.Process(bandHp2.Process(bandHp1.Process(Pink(w))))),
                _ => hp20.Process(Pink(w)),
            };
        }

        // xorshift32, uniform in [-1, 1)
        private double White()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return unchecked((int)rng) / 2147483648.0;
        }

        // Paul Kellet's refined pink noise filter (-3 dB/octave)
        private double Pink(double w)
        {
            b0 = 0.99886 * b0 + w * 0.0555179;
            b1 = 0.99332 * b1 + w * 0.0750759;
            b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856;
            b4 = 0.55000 * b4 + w * 0.5329522;
            b5 = -0.7616 * b5 - w * 0.0168980;
            double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362;
            b6 = w * 0.115926;
            return pink * 0.11;
        }
    }
}
