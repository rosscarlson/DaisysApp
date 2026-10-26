using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using DaisysApp.Logging;
using DaisysApp.Shell;
using Microsoft.Win32;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// The PC's SMBus (where the RAM's lights are, and older ASUS boards' Aura chip), through PawnIO: a small signed driver
/// that runs signed modules (here the official SmbusI801 for Intel and SmbusPIIX4 for AMD, next to this module), the
/// way LibreHardwareMonitor and OpenRGB reach it now. Installed with LibreHardwareMonitor, or from pawnio.eu. Opening it
/// needs administrator rights, so this runs in <see cref="SmbusTask"/>. Every transfer holds the SMBus mutex other
/// monitoring programs share, so they don't talk over each other.
/// </summary>
internal sealed unsafe class PawnIoBus : ISmbus, IDisposable
{
    private const string MutexName = @"Global\Access_SMBUS.HTP.Method";
    private const int Read = 1, Write = 0;
    private const int Byte = 1, ByteData = 2, WordData = 3, BlockData = 5;

    private readonly IntPtr handle;
    private readonly Mutex mutex;

    public string Name { get; }

    private PawnIoBus(IntPtr handle, string name)
    {
        this.handle = handle;
        Name = name;
        mutex = new Mutex(false, MutexName);
    }

    /// <summary>PawnIO's library, where its installer puts it.</summary>
    public static string? LibraryPath()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (k?.GetValue("InstallLocation") is string dir && File.Exists(Path.Combine(dir, "PawnIOLib.dll"))) return Path.Combine(dir, "PawnIOLib.dll");
        }
        catch { }
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");
        return File.Exists(p) ? p : null;
    }

    public static bool Installed => LibraryPath() != null;

    private static bool loaded;
    private static delegate* unmanaged[Stdcall]<IntPtr*, int> open;
    private static delegate* unmanaged[Stdcall]<IntPtr, byte*, nuint, int> load;
    private static delegate* unmanaged[Stdcall]<IntPtr, byte*, ulong*, nuint, ulong*, nuint, nuint*, int> execute;
    private static delegate* unmanaged[Stdcall]<IntPtr, int> close;

    private static void LoadLibrary()
    {
        if (loaded) return;
        string path = LibraryPath() ?? throw new InvalidOperationException("PawnIO isn't installed");
        var lib = NativeLibrary.Load(path);
        open = (delegate* unmanaged[Stdcall]<IntPtr*, int>)NativeLibrary.GetExport(lib, "pawnio_open");
        load = (delegate* unmanaged[Stdcall]<IntPtr, byte*, nuint, int>)NativeLibrary.GetExport(lib, "pawnio_load");
        execute = (delegate* unmanaged[Stdcall]<IntPtr, byte*, ulong*, nuint, ulong*, nuint, nuint*, int>)NativeLibrary.GetExport(lib, "pawnio_execute");
        close = (delegate* unmanaged[Stdcall]<IntPtr, int>)NativeLibrary.GetExport(lib, "pawnio_close");
        loaded = true;
    }

    /// <summary>The SMBus controllers PawnIO can drive here (Intel's, or AMD's first port). Problems go in <paramref name="problems"/>.</summary>
    public static List<PawnIoBus> OpenAll(List<string> problems)
    {
        LoadLibrary();
        var buses = new List<PawnIoBus>();
        string folder = Path.Combine(Path.GetDirectoryName(typeof(PawnIoBus).Assembly.Location)!, "PawnIO");
        foreach (var (file, label) in new[] { ("SmbusI801.bin", "Intel SMBus"), ("SmbusPIIX4.bin", "AMD SMBus") })
        {
            byte[] blob = File.ReadAllBytes(Path.Combine(folder, file));
            IntPtr h;
            int hr = open(&h);
            if (hr != 0) { problems.Add($"PawnIO didn't open (0x{hr:X8})"); return buses; }
            fixed (byte* b = blob) hr = load(h, b, (nuint)blob.Length);
            if (hr != 0)
            {
                close(h);
                // ERROR_NOT_SUPPORTED: not this PC's kind of SMBus
                if (hr != unchecked((int)0x80070032)) problems.Add($"{label}: PawnIO's module didn't load (0x{hr:X8})");
                continue;
            }
            var bus = new PawnIoBus(h, label);
            if (file == "SmbusPIIX4.bin") bus.Call("ioctl_piix4_port_sel", new ulong[] { 0 }, 1);
            buses.Add(bus);
        }
        return buses;
    }

    private int Call(string name, ulong[] input, int outCount, ulong[]? output = null)
    {
        var fn = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        output ??= new ulong[Math.Max(outCount, 1)];
        nuint returned;
        fixed (byte* f = fn)
        fixed (ulong* i = input)
        fixed (ulong* o = output)
            return execute(handle, f, i, (nuint)input.Length, o, (nuint)outCount, &returned);
    }

    /// <summary>One SMBus transfer, with the shared mutex held. Null when nothing answered.</summary>
    private ulong[]? Transfer(byte address, int readWrite, byte command, int protocol, ReadOnlySpan<byte> data)
    {
        var input = new ulong[9];
        input[0] = address;
        input[1] = (ulong)readWrite;
        input[2] = command;
        input[3] = (ulong)protocol;
        Span<byte> union = MemoryMarshal.AsBytes(input.AsSpan(4));
        data.CopyTo(union);
        var output = new ulong[5];
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(1000); } catch (AbandonedMutexException) { held = true; }
            return Call("ioctl_smbus_xfer", input, 5, output) == 0 ? output : null;
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    public int? ReadByte(byte address) => Transfer(address, Read, 0, Byte, default) is { } o ? (int)(o[0] & 0xFF) : null;

    public int? ReadByteData(byte address, byte register) => Transfer(address, Read, register, ByteData, default) is { } o ? (int)(o[0] & 0xFF) : null;

    public bool WriteByteData(byte address, byte register, byte value) => Transfer(address, Write, register, ByteData, stackalloc byte[] { value }) != null;

    public bool WriteWordData(byte address, byte register, ushort value) =>
        Transfer(address, Write, register, WordData, stackalloc byte[] { (byte)value, (byte)(value >> 8) }) != null;

    public bool WriteBlockData(byte address, byte register, ReadOnlySpan<byte> data)
    {
        Span<byte> block = stackalloc byte[data.Length + 1];
        block[0] = (byte)data.Length;
        data.CopyTo(block[1..]);
        return Transfer(address, Write, register, BlockData, block) != null;
    }

    public void Dispose()
    {
        close(handle);
        mutex.Dispose();
    }
}

/// <summary>The lights on the SMBus: ENE chips on RAM (all the sticks as one device) and on older ASUS motherboards.</summary>
public static class SmbusDevices
{
    /// <summary>Where ENE RAM chips are moved to (they all start at 0x77), in OpenRGB's order.</summary>
    private static readonly byte[] RamAddresses = { 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x4F, 0x66, 0x67, 0x39, 0x3A, 0x3B, 0x3C, 0x3D };
    private static readonly byte[] BoardAddresses = { 0x40, 0x4E, 0x4F };

    public const string RamId = "smbus:ram";

    /// <summary>
    /// The ENE chips on one bus. Each RAM stick's chip answers at 0x77 until it's given an address of its own (which it
    /// keeps until the power goes off), so first every chip at 0x77 is moved to a free one of <see cref="RamAddresses"/>,
    /// as OpenRGB does; then each address is checked (reads only). A chip's version says what it's on.
    /// </summary>
    public static List<EneDevice> Find(ISmbus bus, Action<string>? log = null)
    {
        int next = -1;
        for (int slot = 0; slot < 8; slot++)
        {
            if (bus.ReadByte(0x77) == null) break;
            int? answer;
            do
            {
                next++;
                answer = next < RamAddresses.Length ? bus.ReadByte(RamAddresses[next]) : null;
            } while (next < RamAddresses.Length && answer != null);
            if (next >= RamAddresses.Length) break;
            log?.Invoke($"RAM slot {slot} → 0x{RamAddresses[next]:X2}");
            EneChip.Write(bus, 0x77, EneChip.RegSlotIndex, (byte)slot);
            EneChip.Write(bus, 0x77, EneChip.RegI2cAddress, (byte)(RamAddresses[next] << 1));
        }

        var ram = new List<EneChip>();
        var found = new List<EneDevice>();
        foreach (byte a in RamAddresses.Concat(BoardAddresses).Distinct())
        {
            var chip = EneChip.Open(bus, a);
            if (chip == null) continue;
            log?.Invoke($"0x{a:X2}: {chip.Version}, {chip.Leds} LEDs{(chip.Known ? "" : " (not known)")}");
            if (!chip.Known || chip.Leds == 0) continue;
            if (chip.IsRam) ram.Add(chip);
            else if (BoardAddresses.Contains(a))
                found.Add(new EneDevice($"smbus:board:{a:X2}", AuraUsb.BoardName(), DeviceKind.Motherboard, new[] { chip }, bus.Name) { NeedsAdmin = true });
        }
        if (ram.Count > 0)
            found.Insert(0, new EneDevice(RamId, P(ram.Count, "RAM ({0} stick)", "RAM ({0} sticks)"), DeviceKind.Ram, ram, bus.Name) { NeedsAdmin = true });
        return found;
    }

    /// <summary>What <see cref="SmbusTask"/> found, as kept in the settings (so the devices show without asking each time).</summary>
    public sealed class Seen
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DeviceKind Kind { get; set; }
        public string About { get; set; } = "";
    }

    public sealed class Request
    {
        /// <summary>Device id → what to set; empty: only find them.</summary>
        public Dictionary<string, LightSetting> Apply { get; set; } = new();
    }

    public sealed class Result
    {
        public List<Seen> Devices { get; set; } = new();
        public List<string> Problems { get; set; } = new();
        public List<string> Log { get; set; } = new();
    }

    /// <summary>
    /// Finds the SMBus devices and sets them as <paramref name="apply"/> says, in a copy of the app run as administrator
    /// (Windows asks). Null when the question was answered no.
    /// </summary>
    public static async Task<Result?> RunAsync(Dictionary<string, LightSetting> apply)
    {
        string dir = Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-Rgb");
        Directory.CreateDirectory(dir);
        string request = Path.Combine(dir, "request.json"), result = Path.Combine(dir, "result.json");
        File.WriteAllText(request, JsonSerializer.Serialize(new Request { Apply = apply }));
        File.Delete(result);
        int? code = await ModuleTasks.RunAsync<SmbusTask>(new[] { request, result }, elevated: true);
        if (code == null) return null;
        var r = File.Exists(result) ? JsonSerializer.Deserialize<Result>(File.ReadAllText(result)) : null;
        r ??= new Result { Problems = { $"The administrator job ended with {code}" } };
        foreach (var line in r.Log) Log.Here.Debug(() => "SMBus: " + line);
        foreach (var p in r.Problems) Log.Here.Warn("SMBus: " + p);
        return r;
    }
}

/// <summary>Runs as administrator (<see cref="ModuleTasks"/>): args = the request file, the result file.</summary>
public sealed class SmbusTask : IModuleTask
{
    public int Run(string[] args)
    {
        if (args.Length < 2) return 2;
        var result = new SmbusDevices.Result();
        try
        {
            var request = JsonSerializer.Deserialize<SmbusDevices.Request>(File.ReadAllText(args[0])) ?? new();
            var buses = PawnIoBus.OpenAll(result.Problems);
            try
            {
                foreach (var bus in buses)
                {
                    foreach (var d in SmbusDevices.Find(bus, result.Log.Add))
                    {
                        result.Devices.Add(new SmbusDevices.Seen { Id = d.Id, Name = d.Name, Kind = d.Kind, About = d.About });
                        if (!request.Apply.TryGetValue(d.Id, out var s)) continue;
                        d.Apply(s);
                        d.Save();
                        result.Log.Add($"{d.Name}: {s.Effect} {s.Colour} {s.Speed}");
                    }
                }
                if (buses.Count == 0 && result.Problems.Count == 0) result.Problems.Add("No SMBus PawnIO can drive on this PC");
            }
            finally { foreach (var b in buses) b.Dispose(); }
        }
        catch (Exception ex) { result.Problems.Add(ex.Message); }
        File.WriteAllText(args[1], JsonSerializer.Serialize(result));
        return result.Problems.Count == 0 ? 0 : 1;
    }
}
