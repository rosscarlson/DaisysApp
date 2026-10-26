using System.IO;
using System.Runtime.InteropServices;
using DaisysApp.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// ASUS Aura's USB controller (USB 0B05, "AURA LED Controller"), on ASUS motherboards since about 2019: the board's own
/// lights, its 12 V RGB headers and its addressable (ARGB) headers, where fans and strips plug in. A HID device, so no
/// driver or rights are needed. 65-byte reports, id 0xEC: 0x82 firmware, 0xB0 the config table (how many LEDs and
/// headers), 0x35 an effect for a channel, 0x36 the colours of the board's LEDs, 0x3B an effect for an addressable-only
/// controller, 0x3F 0x55 keep it. The effects run on the controller itself. The protocol as OpenRGB's
/// AsusAuraUSBController documents it.
/// </summary>
internal sealed class AuraUsb : IDisposable
{
    private const ushort Vendor = 0x0B05, UsagePage = 0xFF72;
    private static readonly HashSet<ushort> Mainboards = new() { 0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED };
    private static readonly HashSet<ushort> Addressables = new() { 0x1867, 0x1872, 0x18A3, 0x18A5 };

    private readonly Stream stream;
    private readonly int outLength, inLength;
    private readonly object gate = new();
    private bool commit;

    public ushort Product { get; }
    public string Firmware { get; private set; } = "";
    public bool Mainboard { get; }
    /// <summary>The zones, in channel order: the board's own LEDs (on a motherboard controller), then each ARGB header.</summary>
    public List<Zone> Zones { get; } = new();

    public sealed record Zone(int Channel, int FirstLed, int Leds, bool Addressable, int Header);

    /// <summary>On the device's HID handle (opened for overlapped I/O); a test gives a stream of its own.</summary>
    internal AuraUsb(Stream stream, ushort product, int outLength, int inLength)
    {
        this.stream = stream;
        Product = product;
        this.outLength = outLength;
        this.inLength = inLength;
        Mainboard = Mainboards.Contains(product);
    }

    public static List<IRgbDevice> Find()
    {
        var found = new List<IRgbDevice>();
        foreach (var (path, pid, outLen, inLen) in Hid.Paths(Vendor, UsagePage))
        {
            if (!Mainboards.Contains(pid) && !Addressables.Contains(pid)) continue;
            var h = Hid.Open(path, overlapped: true);
            if (h.IsInvalid) { Log.Here.Warn($"ASUS Aura {pid:X4}: couldn't open it ({Marshal.GetLastPInvokeError()})"); continue; }
            var aura = new AuraUsb(new FileStream(h, FileAccess.ReadWrite, 1, isAsync: true), pid, outLen, inLen);
            try
            {
                if (!aura.Start()) { aura.Dispose(); continue; }
            }
            catch (Exception ex) { Log.Here.Warn($"ASUS Aura {pid:X4}: {ex.Message}"); aura.Dispose(); continue; }
            found.AddRange(aura.Devices(BoardName()));
            Log.Here.Info($"ASUS Aura {pid:X4} ({aura.Firmware}): " + string.Join(", ", aura.Zones.Select(z => z.Addressable ? $"ARGB {z.Header}" : P(z.Leds, "{0} LED", "{0} LEDs"))));
        }
        return found;
    }

    internal IEnumerable<IRgbDevice> Devices(string board) =>
        Zones.Select(z => new AuraZone(this, z, $"aura:{Product:X4}:{z.Channel}", z.Addressable ? F("ARGB Header {0}", z.Header) : board));

    /// <summary>The board's name as Windows has it from the BIOS, e.g. "TUF GAMING Z790-PLUS WIFI".</summary>
    internal static string BoardName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (k?.GetValue("BaseBoardProduct") is string p && p.Trim().Length > 0) return p.Trim();
        }
        catch { }
        return T("Motherboard");
    }

    internal bool Start()
    {
        var fw = Ask(0x82, 0x02);
        if (fw != null) Firmware = System.Text.Encoding.ASCII.GetString(fw, 2, 16).Split('\0')[0].Trim();
        var config = Ask(0xB0, 0x30);
        if (config == null) { Log.Here.Warn($"ASUS Aura {Product:X4} ({Firmware}): no config table"); return false; }
        Log.Here.Debug(() => $"ASUS Aura {Product:X4} config: {Convert.ToHexString(config, 4, 60)}");
        int headers = config[4 + 0x02];
        if (Mainboard)
        {
            int boardLeds = config[4 + 0x1B];
            int channel = 0, led = 0;
            if (boardLeds > 0) { Zones.Add(new Zone(channel++, led, boardLeds, false, 0)); led += boardLeds; }
            for (int i = 0; i < headers; i++) Zones.Add(new Zone(channel++, led++, 1, true, i + 1));
            Send(0x52, 0x53, 0x00, 0x01);   // the effects as the first boards had them (OpenRGB's "Gen1")
        }
        else
        {
            for (int i = 0; i < headers; i++) Zones.Add(new Zone(i, 0, 1, true, i + 1));
        }
        return Zones.Count > 0;
    }

    public void Set(Zone z, LightSetting s)
    {
        byte mode = s.Effect switch
        {
            LightEffect.Static => 1,
            LightEffect.Breathing => 2,
            LightEffect.Flashing => 3,
            LightEffect.ColourCycle => 4,
            LightEffect.Rainbow => 5,
            _ => 0,
        };
        var c = LightSetting.HasColour(s.Effect) ? s.Rgb : Rgb.Black;
        lock (gate)
        {
            if (!Mainboard)
            {
                Send(0x3B, (byte)z.Channel, 0x00, mode, c.R, c.G, c.B);
                return;
            }
            Send(0x35, (byte)z.Channel, 0x00, 0x00, mode);
            // the colour: a bit per LED in the mask (from the zone's first), the colours at 5 + 3 × LED
            int mask = ((1 << z.Leds) - 1) << z.FirstLed;
            var r = Report(0x36);
            r[2] = (byte)(mask >> 8);
            r[3] = (byte)mask;
            for (int i = 0; i < z.Leds; i++)
            {
                int at = 5 + 3 * (z.FirstLed + i);
                if (at + 2 >= r.Length) break;
                r[at] = c.R; r[at + 1] = c.G; r[at + 2] = c.B;
            }
            Write(r);
            commit = true;
        }
    }

    /// <summary>Keeps the effects on the controller (they come back after a restart without Daisy's App).</summary>
    public void Keep()
    {
        lock (gate)
        {
            if (!commit) return;
            Send(0x3F, 0x55);
            commit = false;
        }
    }

    private byte[] Report(byte command)
    {
        var r = new byte[Math.Max(outLength, 65)];
        r[0] = 0xEC;
        r[1] = command;
        return r;
    }

    private void Send(byte command, params byte[] data)
    {
        var r = Report(command);
        data.CopyTo(r, 2);
        Write(r);
    }

    private void Write(byte[] r) => stream.Write(r, 0, r.Length);

    /// <summary>Sends a request and waits (1 s) for the answer whose second byte is <paramref name="reply"/>.</summary>
    private byte[]? Ask(byte command, byte reply)
    {
        lock (gate)
        {
            Send(command);
            var until = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < until)
            {
                var buf = new byte[Math.Max(inLength, 65)];
                using var cancel = new CancellationTokenSource(until - DateTime.UtcNow);
                int n;
                try { n = stream.ReadAsync(buf, 0, buf.Length, cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { return null; }
                if (n >= 64 && buf[0] == 0xEC && buf[1] == reply) return buf;
            }
            return null;
        }
    }

    public void Dispose()
    {
        try { stream.Dispose(); } catch { }
    }
}

/// <summary>One of an Aura controller's zones: the board's lights, or one ARGB header (all the fans chained on it).</summary>
internal sealed class AuraZone : IRgbDevice
{
    private readonly AuraUsb aura;
    private readonly AuraUsb.Zone zone;

    public AuraZone(AuraUsb aura, AuraUsb.Zone zone, string id, string name)
    {
        this.aura = aura;
        this.zone = zone;
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public DeviceKind Kind => zone.Addressable ? DeviceKind.Header : DeviceKind.Motherboard;
    public string About => $"ASUS Aura USB {aura.Product:X4} · {aura.Firmware}";
    public IReadOnlyCollection<LightEffect> Effects => EneChip.ChipEffects;
    public bool HasSpeed => false;
    public int Leds => 0;
    public bool NeedsAdmin => false;
    public object Owner => aura;

    public void Apply(LightSetting s) => aura.Set(zone, s);
    public void Save() => aura.Keep();
    public void Show(Rgb[] colours) { }
    public void Dispose() { if (zone.Channel == aura.Zones[^1].Channel) aura.Dispose(); }
}

/// <summary>Windows' HID devices: the paths of a vendor's collections with one usage page.</summary>
internal static class Hid
{
    public static List<(string Path, ushort Product, int OutLength, int InLength)> Paths(ushort vendor, ushort usagePage)
    {
        var list = new List<(string, ushort, int, int)>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
        if (set == new IntPtr(-1)) return list;
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                var detail = Marshal.AllocHGlobal(size);
                string? path = null;
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, size, out _, IntPtr.Zero)) path = Marshal.PtrToStringUni(detail + 4);
                }
                finally { Marshal.FreeHGlobal(detail); }
                if (path == null || !path.Contains($"vid_{vendor:x4}", StringComparison.OrdinalIgnoreCase)) continue;
                using var h = Open(path, overlapped: false, access: 0);   // attributes and caps only
                if (h.IsInvalid) continue;
                var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(h, ref attrs) || attrs.VendorID != vendor || !HidD_GetPreparsedData(h, out var data)) continue;
                HIDP_CAPS caps;
                try { if (HidP_GetCaps(data, out caps) != 0x00110000) continue; }
                finally { HidD_FreePreparsedData(data); }
                if (caps.UsagePage == usagePage) list.Add((path, attrs.ProductID, caps.OutputReportByteLength, caps.InputReportByteLength));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    public static SafeFileHandle Open(string path, bool overlapped, uint access = 0xC0000000) =>
        CreateFile(path, access, 3, IntPtr.Zero, 3, overlapped ? 0x40000000u : 0, IntPtr.Zero);

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
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
