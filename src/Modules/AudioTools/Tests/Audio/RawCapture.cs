using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Tests.Audio;

/// <summary>
/// Asks Windows for a mic's raw signal: no driver or Windows sound processing (noise suppression, automatic gain,
/// echo cancellation, "voice" EQ). Analog mic inputs often have those on, and they wreck measurements: echo
/// cancellation in particular subtracts the test signal being played, by a different amount for each speaker.
/// NAudio doesn't expose IAudioClient2::SetClientProperties, so this calls it on the capture's audio client before
/// the stream is initialised.
/// </summary>
internal static class RawCapture
{
    private static readonly Guid IAudioClient2 = new("726778CD-F60A-4EDA-82DE-E47610CD78AA");
    private static readonly Guid DeviceProps = new("8943B373-388C-4395-B557-BC6DBAFFAFDB"); // PKEY_Devices_AudioDevice_RawProcessingSupported, pid 2

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientProperties
    {
        public uint cbSize;
        public int bIsOffload;
        public int eCategory;  // AudioCategory_Other
        public int Options;    // AUDCLNT_STREAMOPTIONS_RAW = 1
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetClientPropertiesFn(IntPtr self, ref ClientProperties props);

    /// <summary>Whether the device says it can give its raw signal.</summary>
    public static bool Supported(MMDevice device)
    {
        try
        {
            var props = device.Properties;
            for (int i = 0; i < props.Count; i++)
            {
                var key = props.Get(i);
                if (key.formatId == DeviceProps && key.propertyId == 2) return props[i].Value is bool b && b;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Switches a not-yet-started capture to raw mode. Returns null when done, or why it couldn't be.</summary>
    public static string? TryEnable(WasapiCapture capture, MMDevice device)
    {
        if (!Supported(device)) return "the device doesn't offer raw mode";
        IntPtr unk = IntPtr.Zero, client2 = IntPtr.Zero;
        try
        {
            var audioClient = typeof(WasapiCapture).GetField("audioClient", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(capture);
            var com = audioClient == null ? null : typeof(AudioClient).GetField("audioClientInterface", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(audioClient);
            if (com == null) return "the audio client couldn't be reached";
            unk = Marshal.GetIUnknownForObject(com);
            var iid = IAudioClient2;
            int hr = Marshal.QueryInterface(unk, ref iid, out client2);
            if (hr != 0) return $"no IAudioClient2 (0x{hr:X8})";
            // vtable: IUnknown (3) + IAudioClient (12) + IsOffloadCapable, then SetClientProperties
            IntPtr fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(client2), 16 * IntPtr.Size);
            var set = Marshal.GetDelegateForFunctionPointer<SetClientPropertiesFn>(fn);
            var p = new ClientProperties { cbSize = (uint)Marshal.SizeOf<ClientProperties>(), eCategory = 0, Options = 1 };
            hr = set(client2, ref p);
            return hr == 0 ? null : $"SetClientProperties failed (0x{hr:X8})";
        }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            if (client2 != IntPtr.Zero) Marshal.Release(client2);
            if (unk != IntPtr.Zero) Marshal.Release(unk);
        }
    }
}
