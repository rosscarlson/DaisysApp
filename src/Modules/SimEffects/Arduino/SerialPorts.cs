using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>The PC's COM ports (with Windows' names for them: "USB-SERIAL CH340 (COM5)", "Arduino Uno (COM3)"), and
/// opening one. Windows' own calls, so the module needs no serial port package.</summary>
internal static partial class SerialPorts
{
    public sealed record Info(string Port, string Name)
    {
        public string Display => Name.Length > 0 && Name.Contains(Port, StringComparison.OrdinalIgnoreCase) ? Name : Name.Length > 0 ? $"{Name} ({Port})" : Port;
    }

    /// <summary>Every COM port there is now, by number.</summary>
    public static List<Info> List()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
                foreach (var v in key.GetValueNames())
                    if (key.GetValue(v) is string port) found[port] = "";
        }
        catch { }
        try
        {
            foreach (var name in FriendlyNames())
                if (PortIn().Match(name) is { Success: true } m) found[m.Groups[1].Value] = name;
        }
        catch { }
        return found.Select(p => new Info(p.Key.ToUpperInvariant(), p.Value))
            .OrderBy(i => int.TryParse(i.Port[3..], out int n) ? n : 999).ToList();
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PortIn();

    private static IEnumerable<string> FriendlyNames()
    {
        var ports = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");
        var set = SetupDiGetClassDevs(ref ports, IntPtr.Zero, IntPtr.Zero, 2 /* DIGCF_PRESENT */);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var buffer = new byte[512];
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                if (SetupDiGetDeviceRegistryProperty(set, ref data, 0x0C /* SPDRP_FRIENDLYNAME */, out _, buffer, buffer.Length, out int size) && size > 2)
                    yield return System.Text.Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    /// <summary>Opens a port at 8 data bits, no parity, one stop bit, with DTR and RTS on (opening resets an Uno or
    /// Nano, as the Arduino IDE's monitor does).</summary>
    public static SerialLink Open(string port, int baud) => new(port, baud);

    // ---- Windows ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, int property, out int type, byte[] buffer, int size, out int required);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}

/// <summary>An open COM port: whole writes, and reads that wait a little for bytes.</summary>
internal sealed class SerialLink : IBoardLink
{
    private readonly SafeFileHandle handle;

    public string Port { get; }

    public SerialLink(string port, int baud)
    {
        Port = port;
        handle = CreateFile(@"\\.\" + port, 0xC0000000 /* read, write */, 0, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int e = Marshal.GetLastWin32Error();
            throw new IOException(e switch
            {
                2 or 3 => F("{0} isn't there", port),
                5 => F("{0} is in use by another program", port),
                _ => F("{0} couldn't be opened: {1}", port, new Win32Exception(e).Message),
            });
        }
        var dcb = new DCB { DCBlength = Marshal.SizeOf<DCB>() };
        if (!GetCommState(handle, ref dcb)) Fail("GetCommState");
        dcb.BaudRate = (uint)baud;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        // binary, DTR on, RTS on, no flow control
        dcb.Flags = 1u | 1u << 4 | 1u << 12;
        if (!SetCommState(handle, ref dcb)) Fail("SetCommState");
        // a read returns as soon as a byte is there, or after 20 ms
        var t = new COMMTIMEOUTS { ReadIntervalTimeout = uint.MaxValue, ReadTotalTimeoutMultiplier = uint.MaxValue, ReadTotalTimeoutConstant = 20, WriteTotalTimeoutConstant = 1000 };
        if (!SetCommTimeouts(handle, ref t)) Fail("SetCommTimeouts");
        PurgeComm(handle, 0x0F);
    }

    private void Fail(string what)
    {
        int e = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new IOException($"{Port}: {what}: {new Win32Exception(e).Message}");
    }

    public void Write(byte[] data)
    {
        if (!WriteFile(handle, data, data.Length, out int written, IntPtr.Zero) || written != data.Length)
            throw new IOException(F("{0} stopped answering", Port));
    }

    /// <summary>Reads what's there, waiting up to 20 ms for something; 0 when nothing came.</summary>
    public int Read(byte[] buffer)
    {
        if (!ReadFile(handle, buffer, buffer.Length, out int read, IntPtr.Zero))
            throw new IOException(F("{0} stopped answering", Port));
        return read;
    }

    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct DCB
    {
        public int DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved, XonLim, XoffLim;
        public byte ByteSize, Parity, StopBits;
        public byte XonChar, XoffChar, ErrorChar, EofChar, EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout, ReadTotalTimeoutMultiplier, ReadTotalTimeoutConstant, WriteTotalTimeoutMultiplier, WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle h, ref DCB dcb);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle h, ref DCB dcb);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle h, ref COMMTIMEOUTS t);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(SafeFileHandle h, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle h, byte[] data, int count, out int written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle h, byte[] data, int count, out int read, IntPtr overlapped);
}
