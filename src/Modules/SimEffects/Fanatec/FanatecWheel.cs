using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.SimEffects.Fanatec;

/// <summary>
/// A Fanatec wheelbase, through Windows' HID calls (as the Linux driver hid-fanatecff talks to it): the rim's rev
/// LEDs are an output report of the base, <c>01 F8 09 08 hi lo 00 00</c> (a bit per LED), and the rim's 3-digit
/// display <c>01 F8 09 01 02 a b c</c> (7-segment bits). Which rim is on comes in the base's input report (byte 30).
/// </summary>
internal sealed class FanatecWheel : IDisposable
{
    public const int Vendor = 0x0EB7;

    /// <summary>The wheelbases (pedals have Fanatec's vendor id too).</summary>
    private static readonly Dictionary<int, string> bases = new()
    {
        [0x0001] = "ClubSport Wheel Base V2", [0x0004] = "ClubSport Wheel Base V2.5", [0x0006] = "Podium Wheel Base DD1",
        [0x0007] = "Podium Wheel Base DD2", [0x0E03] = "CSL Elite Wheel Base", [0x0005] = "CSL Elite Wheel Base (PS4)",
        [0x0020] = "CSL DD", [0x0011] = "CSR Elite", [0x0197] = "Porsche 911 Wheel Base",
    };

    /// <summary>Rims by the id the base reports, and their single-colour rev LEDs (hid-fanatecff's table; 0: none, or
    /// RGB ones, which take another report and aren't driven yet).</summary>
    public static readonly Dictionary<byte, (string Name, int Leds)> Rims = new()
    {
        [0x01] = ("ClubSport Steering Wheel BMW M3 GT2", 9), [0x02] = ("ClubSport Steering Wheel F1 IS", 9),
        [0x03] = ("ClubSport Steering Wheel Porsche 918 RSR", 9), [0x04] = ("ClubSport Universal Hub", 0),
        [0x07] = ("CSL Steering Wheel P1 (Xbox One)", 0), [0x08] = ("CSL Steering Wheel P1 V2", 0),
        [0x0a] = ("ClubSport Steering Wheel Formula V2", 0), [0x10] = ("Podium Steering Wheel DD GT", 9),
        [0x13] = ("ClubSport Steering Wheel BMW V2", 9), [0x14] = ("ClubSport Steering Wheel RS", 9),
        [0x16] = ("ClubSport Steering Wheel F1 Esports V2", 0),
    };

    private readonly SafeFileHandle handle;
    private readonly int outputLength, inputLength;
    private readonly string path;
    private const byte ReportId = 1;

    public string Name { get; }
    public int Product { get; }

    private FanatecWheel(SafeFileHandle handle, string path, int product, string name, int output, int input)
    {
        this.handle = handle;
        this.path = path;
        Product = product;
        Name = name;
        outputLength = output;
        inputLength = input;
    }

    /// <summary>The first Fanatec wheelbase plugged in, or null.</summary>
    public static FanatecWheel? Find()
    {
        foreach (var path in HidPaths())
        {
            var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); continue; }
            var keep = false;
            try
            {
                var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(h, ref attrs) || attrs.VendorID != Vendor || !bases.TryGetValue(attrs.ProductID, out var baseName)) continue;
                if (!HidD_GetPreparsedData(h, out var data)) continue;
                HIDP_CAPS caps;
                try { if (HidP_GetCaps(data, out caps) != 0x00110000 /* HIDP_STATUS_SUCCESS */) continue; }
                finally { HidD_FreePreparsedData(data); }
                // (the base has several collections: the one with the 8-byte output report)
                if (caps.OutputReportByteLength < 8) continue;
                var product = new byte[256];
                string name = HidD_GetProductString(h, product, product.Length) ? System.Text.Encoding.Unicode.GetString(product).TrimEnd('\0') : baseName;
                keep = true;
                Logging.Log.Here.Info($"Fanatec: {name} (0x{attrs.ProductID:X4}), reports {caps.OutputReportByteLength}/{caps.InputReportByteLength} bytes");
                return new FanatecWheel(h, path, attrs.ProductID, name.Length > 0 ? name : baseName, caps.OutputReportByteLength, caps.InputReportByteLength);
            }
            finally { if (!keep) h.Dispose(); }
        }
        return null;
    }

    /// <summary>The rev LEDs: bit 0 is the last LED (as the base numbers them).</summary>
    public bool SetLeds(ushort bits) => Send(0xF8, 0x09, 0x08, (byte)(bits >> 8), (byte)bits, 0, 0);

    /// <summary>The rim's display: up to 3 characters (digits, a few letters, "-"), centred; "" blanks it.</summary>
    public bool SetDisplay(string text)
    {
        var seg = new byte[3];
        text = text.Length > 3 ? text[^3..] : text;
        int start = (3 - text.Length + 1) / 2;
        for (int i = 0; i < text.Length; i++) seg[start + i] = Segments(text[i]);
        return Send(0xF8, 0x09, 0x01, 0x02, seg[0], seg[1], seg[2]);
    }

    private bool Send(params byte[] body)
    {
        var report = new byte[outputLength];
        report[0] = ReportId;
        body.AsSpan(0, Math.Min(body.Length, outputLength - 1)).CopyTo(report.AsSpan(1));
        if (HidD_SetOutputReport(handle, report, report.Length)) return true;
        if (WriteFile(handle, report, report.Length, out _, IntPtr.Zero)) return true;
        return false;
    }

    /// <summary>The rim on the base, from its input report (null when it can't be read within half a second).</summary>
    public byte? ReadRim()
    {
        if (inputLength < 31) return null;
        try
        {
            using var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000 /* overlapped */, IntPtr.Zero);
            if (h.IsInvalid) return null;
            using var stream = new FileStream(h, FileAccess.Read, inputLength, isAsync: true);
            var buffer = new byte[inputLength];
            using var cancel = new CancellationTokenSource(500);
            for (int i = 0; i < 10; i++)
            {
                int n = stream.ReadAsync(buffer, 0, buffer.Length, cancel.Token).GetAwaiter().GetResult();
                // (0xFF there: a report about the pedals instead)
                if (n > 30 && buffer[0] == 0x01 && buffer[30] != 0xFF && buffer[30] != 0) return buffer[30];
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.Log.Here.Debug(() => "Fanatec: input report: " + ex.Message); }
        return null;
    }

    /// <summary>A character's 7 segments (bit 0 top, then clockwise, bit 6 the middle): hid-fanatecff's table.</summary>
    private static byte Segments(char c) => char.ToLowerInvariant(c) switch
    {
        '0' => 63, '1' => 6, '2' => 91, '3' => 79, '4' => 102, '5' => 109, '6' => 125, '7' => 7, '8' => 127, '9' => 103,
        '-' => 64, '_' => 8, 'r' => 80, 'n' => 84, 'p' => 115, 'a' => 119, 'b' => 124, 'c' => 88, 'd' => 94, 'e' => 121,
        'f' => 113, 'h' => 118, 'l' => 56, 'o' => 92, 's' => 109, 't' => 120, 'u' => 62, 'y' => 110,
        _ => 0,
    };

    public void Dispose() => handle.Dispose();

    // ---- Windows' HID ----

    private static IEnumerable<string> HidPaths()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                var detail = Marshal.AllocHGlobal(size);
                try
                {
                    // cbSize: 8 on 64-bit (a DWORD and the first WCHAR, aligned)
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, size, out _, IntPtr.Zero))
                    {
                        string? p = Marshal.PtrToStringUni(detail + 4);
                        if (p != null && p.Contains("vid_0eb7", StringComparison.OrdinalIgnoreCase)) yield return p;
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
    [DllImport("hid.dll")] private static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] report, int length);
    [DllImport("hid.dll")] private static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle h, byte[] data, int count, out int written, IntPtr overlapped);
}
