using System.Runtime.InteropServices;
using DaisysApp.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// One device's sound: every effect that plays on it, made on the audio thread from what <see cref="EffectSignal"/>
/// asks, kept within the device's Low / High, mixed, scaled by its volume and the master, softly limited, and played
/// on its channels (all, left, right or the LFE). Shared-mode WASAPI, 30 ms.
/// </summary>
internal sealed class ShakerOutput : IWaveProvider, IDisposable
{
    private sealed class Voice(EffectSignal s)
    {
        public readonly EffectSignal S = s;
        public double Phase, GatePhase, KnockPhase;
        public float Amp, Gate, Knock, KnockTop, KnockDecay, Wobble, WobbleTarget, Jitter;
        public bool Rising;
        public int Knocks = s.Knocks;
    }

    private readonly ShakerDevice device;
    private readonly MasterVolume master;
    private readonly Func<EffectSignal[]> signals;
    private readonly WasapiOut output;
    private readonly int channels, rate;
    private readonly bool isFloat;
    private readonly int[] playOn;
    private readonly Random random = new();
    private EffectSignal[]? built;
    private Voice[] voices = Array.Empty<Voice>();
    private float[] mix = Array.Empty<float>();
    private double testPhase;
    private const float Ceiling = 0.9f;
    private float envelope, release;
    private Biquad lowPass1 = new(), lowPass2 = new(), highPass = new();
    private int filterLo, filterHi;
    // what the log says about it once it's played a while
    private DateTime openedAt = DateTime.UtcNow, lastRead;
    private long reads, framesRead;
    private double longestGapMs;
    private bool described;

    /// <summary>A 2nd-order filter (RBJ's cookbook), run one sample at a time.</summary>
    private struct Biquad
    {
        private double b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public static Biquad LowPass(double hz, int rate) => Make(hz, rate, true);
        public static Biquad HighPass(double hz, int rate) => Make(hz, rate, false);

        private static Biquad Make(double hz, int rate, bool low)
        {
            double w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.7071), a0 = 1 + alpha;
            double b = low ? (1 - cos) / 2 : (1 + cos) / 2;
            return new Biquad { b0 = b / a0, b1 = (low ? 1 - cos : -(1 + cos)) / a0, b2 = b / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0 };
        }

        public double Run(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>The loudest sample since the meters last looked (they reset it).</summary>
    public volatile float Peak;
    /// <summary>The device's Test button: a tone at its type's strongest frequency until then.</summary>
    public DateTime TestUntil;
    public volatile bool Failed;
    public string DeviceId => device.DeviceId;

    public ShakerOutput(ShakerDevice device, MasterVolume master, Func<EffectSignal[]> signals, MMDevice mm)
    {
        this.device = device;
        this.master = master;
        this.signals = signals;
        WaveFormat = DeviceService.GetMixFormat(mm);
        channels = WaveFormat.Channels;
        rate = WaveFormat.SampleRate;
        // (32 bits are float unless they're said to be PCM: Windows mixes in float)
        isFloat = WaveFormat.BitsPerSample == 32 && WaveFormat.Encoding != WaveFormatEncoding.Pcm
            && !(WaveFormat is WaveFormatExtensible x && x.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_PCM);
        playOn = Places(device.Channels, WaveFormat);
        output = new WasapiOut(mm, AudioClientShareMode.Shared, true, 50);
        output.PlaybackStopped += (_, e) => { if (e.Exception != null) { Failed = true; Logging.Log.Here.Warn($"Bass Shaker: {device.Name} stopped", e.Exception); } };
        output.Init(this);
        output.Play();
    }

    /// <summary>The channels a device plays on: left is 0 and right 1; the LFE is found from the speaker layout.</summary>
    private static int[] Places(Channels which, WaveFormat f)
    {
        int n = f.Channels;
        if (n == 1) return new[] { 0 };
        switch (which)
        {
            case Channels.Left: return new[] { 0 };
            case Channels.Right: return new[] { 1 };
            case Channels.Lfe:
                uint mask = 0;
                if (f is WaveFormatExtensible && Marshal.SizeOf(f) >= 24)
                {
                    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(f));
                    try { Marshal.StructureToPtr(f, p, false); mask = unchecked((uint)Marshal.ReadInt32(p, 20)); }
                    finally { Marshal.FreeHGlobal(p); }
                }
                var lfe = SpeakerLayout.Build(n, mask).Speakers.Where(s => s.IsLfe).Select(s => s.Channel).ToArray();
                return lfe.Length > 0 ? lfe : Enumerable.Range(0, n).ToArray();
            default: return Enumerable.Range(0, n).ToArray();
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int frames = count / WaveFormat.BlockAlign;
        if (mix.Length < frames) mix = new float[frames];
        Watch(frames);
        try { Render(frames); }
        catch (Exception ex)
        {
            Array.Clear(mix);
            Logging.Log.Here.Debug(() => "Bass Shaker render: " + ex.Message);
        }
        int bytes = WaveFormat.BitsPerSample / 8;
        Array.Clear(buffer, offset, frames * WaveFormat.BlockAlign);
        for (int i = 0; i < frames; i++)
        {
            float x = mix[i];
            foreach (int ch in playOn)
            {
                int at = offset + (i * channels + ch) * bytes;
                if (isFloat) BitConverter.TryWriteBytes(buffer.AsSpan(at, 4), x);
                else if (bytes == 2) BitConverter.TryWriteBytes(buffer.AsSpan(at, 2), (short)(x * 32767));
                else if (bytes == 4) BitConverter.TryWriteBytes(buffer.AsSpan(at, 4), (int)(x * 2147483647.0));
            }
        }
        return frames * WaveFormat.BlockAlign;
    }

    private void Render(int frames)
    {
        var now = signals();
        if (!ReferenceEquals(now, built))
        {
            built = now;
            voices = now.Select(s => new Voice(s)).ToArray();
        }
        Array.Clear(mix, 0, frames);
        float lo = Math.Max(5, device.MinHz), hi = Math.Max(lo + 1, device.MaxHz);
        float smooth = 1 - MathF.Exp(-1f / (rate * 0.015f)), gateSmooth = 1 - MathF.Exp(-1f / (rate * 0.003f));
        float attack = 1f / (rate * 0.004f);
        const float TwoPi = MathF.PI * 2;

        foreach (var v in voices)
        {
            var s = v.S;
            float gain = s.Gain * s.GainOn(device.Key);
            bool here = gain > 0;
            float target = s.Amp * gain;
            float freq = Math.Clamp(s.Freq, lo, hi), gateHz = s.GateHz;
            bool noise = s.Info.Shape == Shape.Noise;
            if (v.Knocks != s.Knocks)
            {
                v.Knocks = s.Knocks;
                if (here)
                {
                    // (rising from where it is, on the same wave: no jump, which would click)
                    if (v.Knock < 0.0005f) v.KnockPhase = 0;
                    v.KnockTop = Math.Max(v.Knock, s.KnockAmp * gain);
                    v.Rising = true;
                    v.KnockDecay = MathF.Exp(MathF.Log(0.05f) / (Math.Max(0.02f, s.KnockSeconds) * rate));
                }
            }
            if (target < 0.0005f && v.Amp < 0.0005f && v.Knock < 0.0005f && !v.Rising) { v.Amp = 0; continue; }
            for (int i = 0; i < frames; i++)
            {
                v.Amp += (target - v.Amp) * smooth;
                float x = 0;
                if (v.Amp > 0.0002f)
                {
                    double f = freq;
                    float a = v.Amp;
                    if (noise)
                    {
                        // rumble: the tone wanders in pitch and strength, about a quarter of its frequency
                        if (random.Next(rate / 12 + 1) == 0) v.WobbleTarget = (float)random.NextDouble();
                        v.Wobble += (v.WobbleTarget - v.Wobble) * 0.0008f;
                        if (random.Next(rate / 20 + 1) == 0) v.Jitter = (float)(random.NextDouble() * 0.4 - 0.2);
                        f = Math.Clamp(freq * (1 + v.Jitter), lo, hi);
                        a *= 0.45f + 0.55f * v.Wobble;
                    }
                    if (gateHz > 0)
                    {
                        v.GatePhase += gateHz / rate;
                        if (v.GatePhase >= 1) v.GatePhase -= 1;
                        v.Gate += ((v.GatePhase < 0.5 ? 1 : 0) - v.Gate) * gateSmooth;
                        a *= v.Gate;
                    }
                    v.Phase += f / rate;
                    if (v.Phase >= 1) v.Phase -= 1;
                    x = MathF.Sin((float)(v.Phase * TwoPi)) * a;
                }
                if (v.Rising || v.Knock > 0.0005f)
                {
                    v.KnockPhase += freq / rate;
                    if (v.KnockPhase >= 1) v.KnockPhase -= 1;
                    if (v.Rising)
                    {
                        v.Knock += v.KnockTop * attack;
                        if (v.Knock >= v.KnockTop) { v.Knock = v.KnockTop; v.Rising = false; }
                    }
                    else v.Knock *= v.KnockDecay;
                    x += MathF.Sin((float)(v.KnockPhase * TwoPi)) * v.Knock;
                }
                else v.Knock = 0;
                mix[i] += x;
            }
        }

        if (filterLo != (int)lo || filterHi != (int)hi)
        {
            filterLo = (int)lo;
            filterHi = (int)hi;
            // a little past High (two filters: 24 dB an octave), and half of Low (so Low itself goes out)
            lowPass1 = Biquad.LowPass(hi * 1.25, rate);
            lowPass2 = Biquad.LowPass(hi * 1.25, rate);
            highPass = Biquad.HighPass(Math.Max(3, lo * 0.5), rate);
            release = MathF.Exp(-1f / (rate * 0.2f));
        }
        bool testing = DateTime.UtcNow < TestUntil;
        float test = testing ? 0.6f : 0;
        float testHz = TransducerType.Of(device.Type).PeakHz;
        float volume = (float)((device.Muted ? 0 : device.Volume) * (master.Muted ? 0 : master.Volume));
        float peak = Peak;
        for (int i = 0; i < frames; i++)
        {
            float x = mix[i] * volume;
            if (testing)
            {
                testPhase += Math.Clamp(testHz, lo, hi) / rate;
                if (testPhase >= 1) testPhase -= 1;
                x += MathF.Sin((float)(testPhase * TwoPi)) * test * (float)(device.Volume * master.Volume);
            }
            // only the device's own range goes out: anything sharp (a pulse switching, a knock) would otherwise add
            // tones in the hundreds of Hz, which speakers play and shakers don't
            x = (float)highPass.Run(lowPass2.Run(lowPass1.Run(x)));
            // a limiter, not a clipper: turned down while it's too loud, back over a fifth of a second (clipping
            // squares the wave, which is a buzz)
            float size = Math.Abs(x);
            envelope = size > envelope ? size : envelope * release;
            if (envelope > Ceiling) x *= Ceiling / envelope;
            mix[i] = x;
            if (Math.Abs(x) > peak) peak = Math.Abs(x);
        }
        Peak = peak;
    }

    /// <summary>Notes how Windows asks for sound (how much, how often), and logs it once after 5 seconds, with any gap
    /// long enough to be heard as a click.</summary>
    private void Watch(int frames)
    {
        var now = DateTime.UtcNow;
        if (reads > 0) longestGapMs = Math.Max(longestGapMs, (now - lastRead).TotalMilliseconds);
        lastRead = now;
        reads++;
        framesRead += frames;
        if (described || now - openedAt < TimeSpan.FromSeconds(5)) return;
        described = true;
        double each = framesRead / (double)reads, ms = each * 1000 / rate;
        Logging.Log.Here.Info($"Bass Shaker: {device.Name} plays {WaveFormat} ({(isFloat ? "float" : WaveFormat.BitsPerSample + "-bit")}) on channel(s) {string.Join(",", playOn)}, {each:0} frames ({ms:0.0} ms) a time, longest wait {longestGapMs:0} ms");
        if (longestGapMs > 50 + ms * 2) Logging.Log.Here.Warn($"Bass Shaker: {device.Name} waited {longestGapMs:0} ms for sound once: a gap, heard as a click");
    }

    public void Dispose()
    {
        try { output.Stop(); } catch { }
        output.Dispose();
    }
}
