using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Tools.SetDelay;

/// <summary>
/// Captures a microphone (first channel) for timing measurements: keeps a live peak/RMS meter all the time, and records
/// the raw samples between <see cref="BeginRecording"/> and <see cref="EndRecording"/>.
/// </summary>
public sealed class MicRecorder : IDisposable
{
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly int channels, bytesPerSample;
    private readonly bool isFloat;
    private readonly object gate = new();
    private readonly List<float> samples = new();
    private bool recording;
    private double meterPeak, meterSum;
    private long meterCount;

    /// <summary>Raised (on a capture thread) when recording stops unexpectedly, e.g. the mic is unplugged.</summary>
    public event Action<Exception?>? Stopped;

    public MicRecorder(MMDevice device)
    {
        this.device = device;
        capture = new WasapiCapture(device, true, 20);
        var f = capture.WaveFormat;
        channels = f.Channels;
        SampleRate = f.SampleRate;
        bytesPerSample = f.BitsPerSample / 8;
        isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubtype);
        if (isFloat ? bytesPerSample != 4 : bytesPerSample is not (2 or 3 or 4))
            throw new NotSupportedException($"Unsupported microphone format: {f}");
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    public int SampleRate { get; }

    public void Start() => capture.StartRecording();

    public void BeginRecording()
    {
        lock (gate)
        {
            samples.Clear();
            recording = true;
        }
    }

    public float[] EndRecording()
    {
        lock (gate)
        {
            recording = false;
            var result = samples.ToArray();
            samples.Clear();
            return result;
        }
    }

    /// <summary>Peak (0–1) and RMS level in dBFS since the last call.</summary>
    public (double Peak, double RmsDb) TakeMeter()
    {
        lock (gate)
        {
            double rms = meterCount > 0 ? Math.Sqrt(meterSum / meterCount) : 0;
            var r = (meterPeak, 20 * Math.Log10(Math.Max(rms, 1e-6)));
            meterPeak = meterSum = 0;
            meterCount = 0;
            return r;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int frameBytes = bytesPerSample * channels;
        int frames = e.BytesRecorded / frameBytes;
        lock (gate)
        {
            for (int i = 0; i < frames; i++)
            {
                float x = ReadSample(e.Buffer, i * frameBytes);
                float ax = Math.Abs(x);
                if (ax > meterPeak) meterPeak = ax;
                meterSum += x * x;
                if (recording) samples.Add(x);
            }
            meterCount += frames;
        }
    }

    private float ReadSample(byte[] b, int o)
    {
        if (isFloat) return BitConverter.ToSingle(b, o);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, o) / 32768f,
            3 => (b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) / 8388608f,
            _ => BitConverter.ToInt32(b, o) / 2147483648f,
        };
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
        device.Dispose();
    }
}
