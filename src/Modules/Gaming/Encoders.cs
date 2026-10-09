using System.Runtime.InteropServices;
using DaisysApp.Logging;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.Gaming;

/// <summary>The GPU's video encoders (NVIDIA NVENC, AMD VCN, Intel Quick Sync), as Windows' Media Foundation lists them.</summary>
internal static class Encoders
{
    public static readonly string[] Codecs = { "H264", "HEVC", "AV1" };
    private static Dictionary<string, string>? available;
    private static bool started;
    private static readonly object gate = new();

    public static readonly Guid FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3");   // MFT_FRIENDLY_NAME_Attribute
    public static readonly Guid HardwareUrl = new("2fb866ac-b078-4942-ab6c-003d05cda674");    // MFT_ENUM_HARDWARE_URL_Attribute
    public static readonly Guid TransformAsync = new("f81a699a-649a-497d-8c73-29f8fed6ad7a"); // MF_TRANSFORM_ASYNC

    public static Guid Subtype(string codec) => Fourcc(codec switch { "H264" => "H264", "AV1" => "AV01", _ => "HEVC" });

    public static Guid Fourcc(string s) =>
        new(BitConverter.ToUInt32(System.Text.Encoding.ASCII.GetBytes(s)), 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    public static string Label(string codec) => codec switch { "H264" => "H.264", "HEVC" => "HEVC (H.265)", _ => codec };

    /// <summary>Starts Media Foundation once for the app's lifetime.</summary>
    public static void Startup()
    {
        lock (gate)
        {
            if (started) return;
            MediaFactory.MFStartup(true).CheckError();
            started = true;
        }
    }

    /// <summary>Codec → the hardware encoder's name, for the codecs this PC's GPU can encode.</summary>
    public static Dictionary<string, string> Available()
    {
        lock (gate)
        {
            if (available != null) return available;
            available = new();
            try
            {
                Startup();
                foreach (string codec in Codecs)
                {
                    var output = new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = Subtype(codec) };
                    MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder, (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter),
                        null, output, out IntPtr list, out uint count);
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            using var activate = new IMFActivate(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                            string name = codec;
                            try { name = activate.GetAllocatedString(FriendlyName) ?? codec; } catch { }
                            if (i == 0) available[codec] = name;
                        }
                    }
                    finally { if (list != IntPtr.Zero) Marshal.FreeCoTaskMem(list); }
                }
            }
            catch (Exception ex) { ErrorLog.Write("Gaming: listing the video encoders", ex); }
            return available;
        }
    }

    /// <summary>The largest picture the codec takes (NVENC's H.264 stops at 4096 pixels a side).</summary>
    public static int MaxSize(string codec) => codec == "H264" ? 4096 : 8192;
}
