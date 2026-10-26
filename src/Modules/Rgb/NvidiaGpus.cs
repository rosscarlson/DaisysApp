using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// NVIDIA's driver library (nvapi64.dll, installed with every NVIDIA driver): its graphics cards, and the I2C bus on
/// each, where the card makers put their lighting chips. No administrator rights needed. Its functions are found by
/// number through nvapi_QueryInterface (the I2C ones aren't in NVIDIA's public headers; the numbers are OpenRGB's).
/// </summary>
internal static unsafe class Nvapi
{
    private static readonly object gate = new();
    private static bool loaded, ok;
    private static delegate* unmanaged[Cdecl]<uint, IntPtr> query;
    private static delegate* unmanaged[Cdecl]<IntPtr*, int*, int> enumGpus;
    private static delegate* unmanaged[Cdecl]<IntPtr, uint*, uint*, uint*, uint*, int> pciIds;
    private static delegate* unmanaged[Cdecl]<IntPtr, byte*, int> fullName;
    private static delegate* unmanaged[Cdecl]<IntPtr, I2cInfo*, uint*, int> i2cWrite, i2cRead;

    public static bool Load()
    {
        lock (gate)
        {
            if (loaded) return ok;
            loaded = true;
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib) || !NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var q)) return false;
            query = (delegate* unmanaged[Cdecl]<uint, IntPtr>)q;
            var init = (delegate* unmanaged[Cdecl]<int>)query(0x0150E828);
            enumGpus = (delegate* unmanaged[Cdecl]<IntPtr*, int*, int>)query(0xE5AC921F);
            pciIds = (delegate* unmanaged[Cdecl]<IntPtr, uint*, uint*, uint*, uint*, int>)query(0x2DDFB66E);
            fullName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)query(0xCEEE8E9F);
            i2cWrite = (delegate* unmanaged[Cdecl]<IntPtr, I2cInfo*, uint*, int>)query(0x283AC65A);
            i2cRead = (delegate* unmanaged[Cdecl]<IntPtr, I2cInfo*, uint*, int>)query(0x4D7B0709);
            ok = init != null && enumGpus != null && pciIds != null && i2cWrite != null && i2cRead != null && init() == 0;
            return ok;
        }
    }

    public sealed record Gpu(IntPtr Handle, int Index, ushort Device, ushort SubVendor, ushort SubDevice, string Name);

    public static List<Gpu> Gpus()
    {
        var list = new List<Gpu>();
        if (!Load()) return list;
        var handles = stackalloc IntPtr[64];
        var buf = stackalloc byte[64];
        int count = 0;
        if (enumGpus(handles, &count) != 0) return list;
        for (int i = 0; i < count; i++)
        {
            uint dev, sub, rev, ext;
            if (pciIds(handles[i], &dev, &sub, &rev, &ext) != 0) continue;
            string name = "NVIDIA GPU";
            if (fullName != null)
            {
                if (fullName(handles[i], buf) == 0) name = Marshal.PtrToStringAnsi((IntPtr)buf) ?? name;
            }
            list.Add(new Gpu(handles[i], i, (ushort)(dev >> 16), (ushort)sub, (ushort)(sub >> 16), name));
        }
        return list;
    }

    // NV_I2C_INFO_V3 (64 bytes on 64-bit Windows)
    [StructLayout(LayoutKind.Sequential)]
    public struct I2cInfo
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDdcPort;
        public byte DeviceAddress;   // the 7-bit address shifted left
        public byte* RegisterAddress;
        public uint RegisterAddressSize;
        public byte* Data;
        public uint Size;
        public uint Speed;           // 0xFFFF: the old field, unused
        public int SpeedKhz;         // 0: the default
        public byte PortId;
        public uint IsPortIdSet;
    }

    /// <summary>One transfer on the card's I2C port 1 (where its lighting chip is). 0 when it went through.</summary>
    public static int Transfer(IntPtr gpu, bool write, byte address, byte? register, Span<byte> data)
    {
        byte reg = register ?? 0;
        uint unknown = 0;
        fixed (byte* d = data)
        {
            var info = new I2cInfo
            {
                Version = (uint)((3 << 16) | sizeof(I2cInfo)),
                DeviceAddress = (byte)(address << 1),
                RegisterAddress = register == null ? null : &reg,
                RegisterAddressSize = register == null ? 0u : 1u,
                Data = d,
                Size = (uint)data.Length,
                Speed = 0xFFFF,
                PortId = 1,
                IsPortIdSet = 1,
            };
            lock (gate) return write ? i2cWrite(gpu, &info, &unknown) : i2cRead(gpu, &info, &unknown);
        }
    }
}

/// <summary>A graphics card's I2C bus as an <see cref="ISmbus"/>.</summary>
internal sealed class NvapiBus : ISmbus
{
    private readonly IntPtr gpu;

    public NvapiBus(Nvapi.Gpu gpu)
    {
        this.gpu = gpu.Handle;
        Name = $"NVIDIA I2C, GPU {gpu.Index}";
    }

    public string Name { get; }

    public int? ReadByte(byte address)
    {
        Span<byte> b = stackalloc byte[1];
        return Nvapi.Transfer(gpu, false, address, null, b) == 0 ? b[0] : null;
    }

    public int? ReadByteData(byte address, byte register)
    {
        Span<byte> b = stackalloc byte[1];
        return Nvapi.Transfer(gpu, false, address, register, b) == 0 ? b[0] : null;
    }

    public bool WriteByteData(byte address, byte register, byte value)
    {
        Span<byte> b = stackalloc byte[] { value };
        return Nvapi.Transfer(gpu, true, address, register, b) == 0;
    }

    public bool WriteWordData(byte address, byte register, ushort value)
    {
        Span<byte> b = stackalloc byte[] { (byte)value, (byte)(value >> 8) };
        return Nvapi.Transfer(gpu, true, address, register, b) == 0;
    }

    public bool WriteBlockData(byte address, byte register, ReadOnlySpan<byte> data)
    {
        // SMBus block: the length, then the bytes
        Span<byte> b = stackalloc byte[data.Length + 1];
        b[0] = (byte)data.Length;
        data.CopyTo(b[1..]);
        return Nvapi.Transfer(gpu, true, address, register, b) == 0;
    }
}

/// <summary>
/// The lights on NVIDIA graphics cards Daisy's App knows: ASUS cards (an ENE chip at 0x67, as on the TUF and ROG Strix
/// RTX 30, 40 and 50 series) and EVGA's GTX 1070 / 1080 / 1080 Ti FTW2, FTW3, SC2 and K|NGP|N (its own chip, one colour
/// per zone). Each is checked by reading first, and only changed once it answered as that chip does.
/// </summary>
internal static class NvidiaGpus
{
    private const ushort Asus = 0x1043, Evga = 0x3842;
    private static readonly HashSet<ushort> EvgaPascal = new() { 0x1B06 /* 1080 Ti */, 0x1B80 /* 1080 */, 0x1B81 /* 1070 */ };

    public static List<IRgbDevice> Find()
    {
        var found = new List<IRgbDevice>();
        List<Nvapi.Gpu> gpus;
        try { gpus = Nvapi.Gpus(); }
        catch (Exception ex) { Log.Here.Warn($"NVIDIA's driver library didn't answer: {ex.Message}"); return found; }
        foreach (var g in gpus)
        {
            Log.Here.Debug(() => $"GPU {g.Index}: {g.Name} (10DE:{g.Device:X4}, {g.SubVendor:X4}:{g.SubDevice:X4})");
            var bus = new NvapiBus(g);
            try
            {
                if (g.SubVendor == Asus && EneChip.Open(bus, 0x67) is { } chip)
                {
                    if (chip.Known) found.Add(new EneDevice($"gpu:{g.SubVendor:X4}{g.SubDevice:X4}:{g.Index}", g.Name, DeviceKind.Gpu, new[] { chip }, bus.Name));
                    else Log.Here.Info($"{g.Name}: an ENE chip Daisy's App doesn't know yet ({chip.Version}), left alone");
                }
                else if (g.SubVendor == Evga && EvgaPascal.Contains(g.Device) && EvgaGp102.Find(bus) is { Count: > 0 } zones)
                {
                    found.Add(new EvgaGp102Device($"gpu:{g.SubVendor:X4}{g.SubDevice:X4}:{g.Index}", g.Name, zones, bus.Name));
                }
            }
            catch (Exception ex) { Log.Here.Warn($"{g.Name}: its lights didn't answer: {ex.Message}"); }
        }
        return found;
    }
}

/// <summary>
/// EVGA's Pascal (GP102 / GP104) cards: a zone per chip address (the name plate at 0x4A, the backplate at 0x4F or
/// 0x2A), each one colour, either off or on. Colours are set inside a begin / colour / end handshake on register 0x0E.
/// The protocol as OpenRGB's EVGAGP102Controller documents it.
/// </summary>
internal sealed class EvgaGp102
{
    private const byte RegValid = 0x04, RegMode = 0x0C, RegCommand = 0x0E;
    private const byte Begin = 0xE5, Colour = 0xE9, End = 0xE0;

    private readonly ISmbus bus;
    private readonly byte address, ready, clear;
    private readonly byte[] colourRegs;
    public string Zone { get; }

    private EvgaGp102(ISmbus bus, string zone, byte address, byte[] colourRegs, byte ready, byte clear)
    {
        this.bus = bus;
        Zone = zone;
        this.address = address;
        this.colourRegs = colourRegs;
        this.ready = ready;
        this.clear = clear;
    }

    public static List<EvgaGp102> Find(ISmbus bus)
    {
        var all = new[]
        {
            new EvgaGp102(bus, "Name plate", 0x4A, new byte[] { 0x09, 0x0A, 0x0B }, 0x03, 0x00),
            new EvgaGp102(bus, "Backplate", 0x2A, new byte[] { 0x30, 0x31, 0x32 }, 0xE9, 0xE0),   // K|NGP|N
            new EvgaGp102(bus, "Backplate", 0x4F, new byte[] { 0x30, 0x31, 0x32 }, 0x03, 0x00),   // FTW3
        };
        return all.Where(z => z.Valid()).ToList();
    }

    private bool Valid()
    {
        for (int i = 0; i < 3; i++)
            if (bus.ReadByteData(address, RegValid) is 0x1F or 0x91) return true;
        return false;
    }

    /// <summary>The zone's mode (0 off, 1 a colour) and colour as the chip has them.</summary>
    public (int? Mode, Rgb Colour) Read() => (bus.ReadByteData(address, RegMode),
        new Rgb((byte)(bus.ReadByteData(address, colourRegs[0]) ?? 0), (byte)(bus.ReadByteData(address, colourRegs[1]) ?? 0), (byte)(bus.ReadByteData(address, colourRegs[2]) ?? 0)));

    public void SetMode(bool on) => bus.WriteByteData(address, RegMode, on ? (byte)1 : (byte)0);

    public void SetColour(Rgb c)
    {
        bus.WriteByteData(address, RegCommand, Begin);
        bus.WriteByteData(address, RegCommand, Colour);
        if (bus.ReadByteData(address, RegCommand) != ready) return;
        bus.WriteByteData(address, colourRegs[0], c.R);
        bus.WriteByteData(address, colourRegs[1], c.G);
        bus.WriteByteData(address, colourRegs[2], c.B);
        bus.WriteByteData(address, RegCommand, End);
        _ = bus.ReadByteData(address, RegCommand) == clear;
    }
}

/// <summary>An EVGA Pascal card: off or one colour by itself; Daisy's App draws the other effects, a colour per zone.</summary>
internal sealed class EvgaGp102Device : IRgbDevice
{
    private readonly List<EvgaGp102> zones;
    private readonly Rgb[] last;
    private bool? on;

    public EvgaGp102Device(string id, string name, List<EvgaGp102> zones, string bus)
    {
        Id = id;
        Name = name;
        this.zones = zones;
        last = new Rgb[zones.Count];
        Array.Fill(last, new Rgb(1, 2, 3));   // none sent yet
        About = $"EVGA ({string.Join(", ", zones.Select(z => z.Zone))}) · {bus}";
    }

    public string Id { get; }
    public string Name { get; }
    public DeviceKind Kind => DeviceKind.Gpu;
    public string About { get; }
    public IReadOnlyCollection<LightEffect> Effects { get; } = new[] { LightEffect.Off, LightEffect.Static };
    public bool HasSpeed => false;
    public int Leds => zones.Count;
    public bool NeedsAdmin => false;
    public object Owner => this;

    public void Apply(LightSetting s)
    {
        if (s.Effect == LightEffect.Off) { Mode(false); return; }
        Mode(true);
        Show(Enumerable.Repeat(s.Rgb, zones.Count).ToArray());
    }

    public void Save() { }   // it keeps nothing through a restart (OpenRGB found no way)

    public void Show(Rgb[] colours)
    {
        Mode(true);
        for (int i = 0; i < zones.Count && i < colours.Length; i++)
        {
            if (colours[i] == last[i]) continue;
            zones[i].SetColour(colours[i]);
            last[i] = colours[i];
        }
    }

    private void Mode(bool now)
    {
        if (on == now) return;
        // off: black too (this PC's 1080 Ti FTW3 read its mode back unchanged after a 0, so the mode alone may not do it)
        foreach (var z in zones)
        {
            if (!now) z.SetColour(Rgb.Black);
            z.SetMode(now);
        }
        on = now;
        if (!now) Array.Fill(last, new Rgb(1, 2, 3));   // set again once on
    }

    public void Dispose() { }
}
