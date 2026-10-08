using System.Numerics;

namespace DaisysApp.Shared.Audio;

/// <summary>One output channel of a device, with a position on the room diagram (0..1 in both axes, front at the top).</summary>
public sealed record SpeakerDef(int Channel, uint MaskBit, string Short, string Name, double X, double Y, bool IsLfe, bool IsHeight);

public sealed record SpeakerLayoutInfo(string Name, IReadOnlyList<SpeakerDef> Speakers, bool ShowListener);

/// <summary>Maps a WAVEFORMATEXTENSIBLE channel mask to named, positioned speakers.</summary>
public static class SpeakerLayout
{
    // Logical size of the room diagram; tiles are positioned on this canvas (it is scaled to fit the window).
    public const double CanvasWidth = 600, CanvasHeight = 560, TileWidth = 108, TileHeight = 104;

    private sealed record Known(string Short, string Name, double X, double Y, bool Lfe = false, bool Height = false);

    // Keys are the KSAUDIO SPEAKER_* bits. Channels in an interleaved stream appear in ascending bit order.
    private static readonly Dictionary<uint, Known> Positions = new()
    {
        [0x1] = new("FL", T("Front Left"), 0.10, 0.095),
        [0x2] = new("FR", T("Front Right"), 0.90, 0.095),
        [0x4] = new("C", T("Center"), 0.50, 0.095),
        [0x8] = new("LFE", T("Low Frequency Effect"), 0.90, 0.30, Lfe: true),
        [0x10] = new("BL", T("Back Left"), 0.25, 0.87),
        [0x20] = new("BR", T("Back Right"), 0.75, 0.87),
        [0x40] = new("FLC", T("Front Left Center"), 0.30, 0.095),
        [0x80] = new("FRC", T("Front Right Center"), 0.70, 0.095),
        [0x100] = new("BC", T("Back Center"), 0.50, 0.87),
        [0x200] = new("SL", T("Side Left"), 0.10, 0.50),
        [0x400] = new("SR", T("Side Right"), 0.90, 0.50),
        [0x800] = new("TC", T("Top Center"), 0.30, 0.50, Height: true),
        [0x1000] = new("TFL", T("Top Front Left"), 0.30, 0.28, Height: true),
        [0x2000] = new("TFC", T("Top Front Center"), 0.50, 0.28, Height: true),
        [0x4000] = new("TFR", T("Top Front Right"), 0.70, 0.28, Height: true),
        [0x8000] = new("TBL", T("Top Back Left"), 0.30, 0.68, Height: true),
        [0x10000] = new("TBC", T("Top Back Center"), 0.50, 0.68, Height: true),
        [0x20000] = new("TBR", T("Top Back Right"), 0.70, 0.68, Height: true),
    };

    public static SpeakerLayoutInfo Build(int channels, uint mask)
    {
        if (BitOperations.PopCount(mask) < channels)
            mask = DefaultMask(channels);

        var bits = new List<uint>();
        for (int b = 0; b < 32 && bits.Count < channels; b++)
            if ((mask & (1u << b)) != 0) bits.Add(1u << b);

        if (channels > 0 && bits.Count == channels && bits.All(Positions.ContainsKey))
        {
            bool hasSides = bits.Contains(0x200u) || bits.Contains(0x400u);
            var list = new List<SpeakerDef>(channels);
            for (int i = 0; i < bits.Count; i++)
            {
                var k = Positions[bits[i]];
                string s = k.Short, n = k.Name;
                if (channels == 1) { s = "M"; n = T("Mono"); }
                else if (!hasSides && bits[i] == 0x10) { s = "SL"; n = T("Surround Left"); }
                else if (!hasSides && bits[i] == 0x20) { s = "SR"; n = T("Surround Right"); }
                list.Add(new SpeakerDef(i, bits[i], s, n, k.X, k.Y, k.Lfe, k.Height));
            }
            return new SpeakerLayoutInfo(Describe(list), list, ShowListener: true);
        }

        return Generic(channels);
    }

    private static string Describe(List<SpeakerDef> list)
    {
        if (list.Count == 1) return T("Mono");
        int lfe = list.Count(s => s.IsLfe);
        int height = list.Count(s => s.IsHeight);
        int mains = list.Count - lfe - height;
        if (mains == 2 && lfe == 0 && height == 0) return T("Stereo");
        return height > 0 ? $"{mains}.{lfe}.{height}" : $"{mains}.{lfe}";
    }

    private static uint DefaultMask(int channels) => channels switch
    {
        1 => 0x4,
        2 => 0x3,
        4 => 0x33,
        6 => 0x3F,
        8 => 0x63F,
        _ => 0,
    };

    /// <summary>Unknown layout: lay the channels out in a simple grid labelled by number.</summary>
    private static SpeakerLayoutInfo Generic(int channels)
    {
        const double gap = 12;
        int cols = Math.Clamp(channels, 1, 5);
        int rows = (channels + cols - 1) / cols;
        double rowStep = Math.Min(TileHeight + 24, (CanvasHeight - TileHeight) / Math.Max(1, rows - 1));
        double totalW = cols * TileWidth + (cols - 1) * gap;
        double x0 = (CanvasWidth - totalW) / 2 + TileWidth / 2;
        double y0 = (CanvasHeight - (rows - 1) * rowStep) / 2;

        var list = new List<SpeakerDef>(channels);
        for (int i = 0; i < channels; i++)
        {
            int r = i / cols, c = i % cols;
            list.Add(new SpeakerDef(i, 0, $"{i + 1}", F("Channel {0}", i + 1),
                (x0 + c * (TileWidth + gap)) / CanvasWidth, (y0 + r * rowStep) / CanvasHeight, false, false));
        }
        return new SpeakerLayoutInfo(F("{0}-channel", channels), list, ShowListener: false);
    }
}
