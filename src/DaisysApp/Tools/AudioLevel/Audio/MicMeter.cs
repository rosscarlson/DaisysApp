using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Tools.AudioLevel.Audio;

/// <summary>Which part of the spectrum to read: everything, the 500 Hz–2 kHz calibration band, or the subwoofer band.</summary>
public enum MicBand { Wide, Mains, Lfe }

/// <summary>Mic level in dB relative to the mic's full scale (not SPL) for each band, plus the raw sample peak.</summary>
public readonly record struct MicReading(double WideDb, double MainsDb, double LfeDb, double Peak)
{
    public double Get(MicBand band) => band switch
    {
        MicBand.Mains => MainsDb,
        MicBand.Lfe => LfeDb,
        _ => WideDb,
    };
}

/// <summary>
/// Captures a microphone (first channel only) and accumulates filtered power in three bands at once.
/// Two independent accumulators: one drained by the live meter, one for timed measurements.
/// </summary>
public sealed class MicMeter : IDisposable
{
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly int channels, bytesPerSample;
    private readonly bool isFloat;
    private readonly double fs;
    private readonly object gate = new();
    private readonly Biquad wideHp, mainsHp1, mainsHp2, mainsLp1, mainsLp2, lfeHp, lfeLp1, lfeLp2;
    private double appliedLfeHz;
    private volatile float lfeBandHz = 120;
    private Accumulator meter, measure;

    /// <summary>Raised (on a capture thread) when recording stops unexpectedly, e.g. the mic is unplugged.</summary>
    public event Action<Exception?>? Stopped;

    public MicMeter(MMDevice device)
    {
        this.device = device;
        capture = new WasapiCapture(device, true, 50);
        var f = capture.WaveFormat;
        channels = f.Channels;
        fs = f.SampleRate;
        bytesPerSample = f.BitsPerSample / 8;
        isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubtype);
        if (isFloat ? bytesPerSample != 4 : bytesPerSample is not (2 or 3 or 4))
            throw new NotSupportedException($"Unsupported microphone format: {f}");

        const double q = 0.7071;
        wideHp = Biquad.HighPass(fs, 20, q);
        mainsHp1 = Biquad.HighPass(fs, 400, q);
        mainsHp2 = Biquad.HighPass(fs, 400, q);
        mainsLp1 = Biquad.LowPass(fs, 2500, q);
        mainsLp2 = Biquad.LowPass(fs, 2500, q);
        lfeHp = Biquad.HighPass(fs, 20, q);
        appliedLfeHz = lfeBandHz;
        lfeLp1 = Biquad.LowPass(fs, appliedLfeHz, q);
        lfeLp2 = Biquad.LowPass(fs, appliedLfeHz, q);

        capture.DataAvailable += OnData;
        capture.RecordingStopped += OnRecordingStopped;
    }

    public string Name => device.FriendlyName;

    /// <summary>Upper edge of the subwoofer measurement band (should be a little above the LFE test-signal cutoff).</summary>
    public double LfeBandHz { set => lfeBandHz = (float)Math.Clamp(value, 40, fs / 4); }

    public void Start() => capture.StartRecording();

    public MicReading TakeMeterReading()
    {
        lock (gate)
        {
            var r = meter.Read();
            meter = default;
            return r;
        }
    }

    public void BeginMeasure()
    {
        lock (gate) measure = default;
    }

    public MicReading EndMeasure()
    {
        lock (gate) return measure.Read();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float lfeHz = lfeBandHz;
        if (lfeHz != appliedLfeHz)
        {
            appliedLfeHz = lfeHz;
            lfeLp1.SetLowPass(fs, lfeHz, 0.7071);
            lfeLp2.SetLowPass(fs, lfeHz, 0.7071);
        }

        int frameBytes = bytesPerSample * channels;
        int frames = e.BytesRecorded / frameBytes;
        double w = 0, m = 0, l = 0, peak = 0;
        var buf = e.Buffer;
        for (int i = 0; i < frames; i++)
        {
            double x = ReadSample(buf, i * frameBytes);
            double ax = Math.Abs(x);
            if (ax > peak) peak = ax;
            double a = wideHp.Process(x);
            double b = mainsLp2.Process(mainsLp1.Process(mainsHp2.Process(mainsHp1.Process(x))));
            double c = lfeLp2.Process(lfeLp1.Process(lfeHp.Process(x)));
            w += a * a;
            m += b * b;
            l += c * c;
        }

        lock (gate)
        {
            meter.Add(w, m, l, peak, frames);
            measure.Add(w, m, l, peak, frames);
        }
    }

    private double ReadSample(byte[] b, int o)
    {
        if (isFloat) return BitConverter.ToSingle(b, o);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, o) / 32768.0,
            3 => (b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) / 8388608.0,
            _ => BitConverter.ToInt32(b, o) / 2147483648.0,
        };
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(e.Exception);

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
        device.Dispose();
    }

    private struct Accumulator
    {
        private double w, m, l, peak;
        private long n;

        public void Add(double sw, double sm, double sl, double pk, int count)
        {
            w += sw; m += sm; l += sl; n += count;
            if (pk > peak) peak = pk;
        }

        public readonly MicReading Read() => n == 0
            ? new MicReading(-120, -120, -120, 0)
            : new MicReading(ToDb(w / n), ToDb(m / n), ToDb(l / n), peak);

        private static double ToDb(double power) => 10 * Math.Log10(Math.Max(power, 1e-12));
    }
}
