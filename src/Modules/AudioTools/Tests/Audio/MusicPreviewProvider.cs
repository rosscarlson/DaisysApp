using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DaisysApp.Shared.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DaisysApp.Applets.AudioTools.Tests.Audio;

/// <summary>
/// A music file played on every speaker of the device, for hearing what the EQ does: the left channel on the speakers
/// on the left, the right on the right, the two mixed on the centre ones and, low-passed, on the subwoofer. Starts
/// over at the end. Written in the device's own mix format, as <see cref="TestSignalProvider"/> is.
/// </summary>
public sealed class MusicPreviewProvider : IWaveProvider, IDisposable
{
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private const float Level = 0.5f; // −6 dB: room for the EQ's boosts

    private readonly MediaFoundationReader reader;
    private readonly ISampleProvider source; // stereo, at the device's rate
    private readonly int channels, bytesPerSample;
    private readonly bool isFloat;
    private readonly int[] place; // each output channel: −1 left, 1 right, 0 centre, 2 subwoofer
    private readonly Biquad lfe1, lfe2;
    private float[] stereo = [], mixed = [];
    private float gain;
    private volatile bool muted;

    public MusicPreviewProvider(string path, WaveFormat format, IReadOnlyList<SpeakerDef> speakers, double lfeCutoffHz)
    {
        WaveFormat = format;
        channels = format.Channels;
        bytesPerSample = format.BitsPerSample / 8;
        isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubtype);
        if (isFloat ? bytesPerSample != 4 : bytesPerSample is not (2 or 3 or 4))
            throw new NotSupportedException($"Unsupported device mix format: {format}");

        reader = new MediaFoundationReader(path);
        ISampleProvider s = reader.ToSampleProvider();
        if (s.WaveFormat.Channels == 1) s = new MonoToStereoSampleProvider(s);
        else if (s.WaveFormat.Channels > 2) s = new MultiplexingSampleProvider([s], 2); // its front left and right
        if (s.WaveFormat.SampleRate != format.SampleRate) s = new WdlResamplingSampleProvider(s, format.SampleRate);
        source = s;

        place = new int[channels];
        foreach (var sp in speakers.Where(sp => sp.Channel >= 0 && sp.Channel < channels))
            place[sp.Channel] = sp.IsLfe ? 2 : sp.X < 0.45 ? -1 : sp.X > 0.55 ? 1 : 0;
        if (channels == 2) { place[0] = -1; place[1] = 1; }
        lfe1 = Biquad.LowPass(format.SampleRate, lfeCutoffHz, 0.7071);
        lfe2 = Biquad.LowPass(format.SampleRate, lfeCutoffHz, 0.7071);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>The file's length, for the log.</summary>
    public TimeSpan Length => reader.TotalTime;

    /// <summary>Fades out (just before stopping, so it doesn't click).</summary>
    public void Mute() => muted = true;

    public int Read(byte[] buffer, int offset, int count)
    {
        int frames = count / WaveFormat.BlockAlign;
        if (stereo.Length < frames * 2) stereo = new float[frames * 2];
        int got = source.Read(stereo, 0, frames * 2);
        if (got < frames * 2)
        {
            // the end of the song: from the top again
            reader.Position = 0;
            got += source.Read(stereo, got, frames * 2 - got);
        }
        Array.Clear(stereo, got, frames * 2 - got);

        int samples = frames * channels;
        if (mixed.Length < samples) mixed = new float[samples];
        float target = muted ? 0 : Level;
        for (int f = 0; f < frames; f++)
        {
            gain += (target - gain) * 0.002f; // a few milliseconds' fade in and out
            float l = stereo[f * 2] * gain, r = stereo[f * 2 + 1] * gain, mid = (l + r) * 0.5f;
            float sub = (float)lfe2.Process(lfe1.Process(mid));
            for (int c = 0; c < channels; c++)
                mixed[f * channels + c] = place[c] switch { -1 => l, 1 => r, 2 => sub, _ => mid };
        }

        var dst = buffer.AsSpan(offset);
        if (isFloat) mixed.AsSpan(0, samples).CopyTo(MemoryMarshal.Cast<byte, float>(dst[..(samples * 4)]));
        else
            for (int i = 0; i < samples; i++)
            {
                float v = Math.Clamp(mixed[i], -1f, 1f);
                switch (bytesPerSample)
                {
                    case 2: BinaryPrimitives.WriteInt16LittleEndian(dst[(i * 2)..], (short)Math.Round(v * 32767f)); break;
                    case 3:
                        int p = (int)Math.Round(v * 8388607f);
                        dst[i * 3] = (byte)p; dst[i * 3 + 1] = (byte)(p >> 8); dst[i * 3 + 2] = (byte)(p >> 16);
                        break;
                    default: BinaryPrimitives.WriteInt32LittleEndian(dst[(i * 4)..], (int)Math.Round(v * 2147483647.0)); break;
                }
            }
        return frames * WaveFormat.BlockAlign;
    }

    public void Dispose() => reader.Dispose();
}
