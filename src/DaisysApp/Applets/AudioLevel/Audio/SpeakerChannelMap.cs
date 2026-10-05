using System.Numerics;
using NAudio.CoreAudioApi;

using DaisysApp.Shared.Audio;
using DaisysApp.Shared.Voicemeeter;

namespace DaisysApp.Applets.AudioLevel.Audio;

/// <summary>
/// Maps the speakers of the Windows speaker setup (mix format) onto the device's native channels. Virtual devices
/// such as Voicemeeter's are natively 8-channel even when Windows is set to stereo or 5.1; the audio engine places
/// each speaker by position, so FL is native channel 0, SL native channel 6 on a 7.1 device, and so on.
/// </summary>
public static class SpeakerChannelMap
{
    /// <returns>For each mix-format channel, the native channel index or -1.</returns>
    public static int[] Build(MMDevice device, int mixChannels, IReadOnlyList<SpeakerDef> speakers, int nativeChannels)
    {
        uint deviceMask = ReadDeviceFormatMask(device, out int formatChannels);
        if (nativeChannels <= 0) nativeChannels = formatChannels;
        var map = Enumerable.Repeat(-1, mixChannels).ToArray();
        foreach (var s in speakers)
        {
            if (s.Channel >= mixChannels) continue;
            int native = -1;
            if (nativeChannels == mixChannels)
                native = s.Channel;
            else if (deviceMask != 0 && BitOperations.PopCount(deviceMask) == nativeChannels && (deviceMask & s.MaskBit) != 0)
                native = BitOperations.PopCount(deviceMask & (s.MaskBit - 1));
            else if (s.Channel < nativeChannels)
                native = s.Channel;
            map[s.Channel] = native;
        }
        return map;
    }

    /// <summary>The endpoint's native format channel mask (PKEY_AudioEngine_DeviceFormat), or 0.</summary>
    public static uint ReadDeviceFormatMask(MMDevice device, out int channels)
    {
        channels = 0;
        try
        {
            var store = device.Properties;
            var key = PropertyKeys.PKEY_AudioEngine_DeviceFormat;
            if (store.Contains(key) && store[key].Value is byte[] b && b.Length >= 18)
            {
                channels = BitConverter.ToUInt16(b, 2);
                if (BitConverter.ToUInt16(b, 0) == 0xFFFE && b.Length >= 24) return BitConverter.ToUInt32(b, 20);
            }
        }
        catch { /* property missing or unreadable */ }
        return 0;
    }
}
