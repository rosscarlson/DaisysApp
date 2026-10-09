using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Which upscaling and frame generation libraries a game has loaded (DLSS, DLSS Frame Gen, Ray Reconstruction, FSR,
/// XeSS, Reflex), with their versions. It only lists the game's loaded files, as Task Manager or Process Explorer would:
/// nothing is read from, or put into, the game. Loaded doesn't always mean switched on in the game's options (a game may
/// load DLSS and run without it); which mode a game is in can only be seen from inside it, e.g. with
/// <see cref="NvidiaIndicator"/>. A game whose anti-cheat hides its files shows nothing.
/// </summary>
internal static class GameTech
{
    private static readonly (string Prefix, string Name, int Order)[] Known =
    {
        ("nvngx_dlss.dll", "DLSS", 0),
        ("nvngx_dlssg.dll", "DLSS Frame Gen", 1),
        ("nvngx_dlssd.dll", "Ray Reconstruction", 2),
        ("amd_fidelityfx_framegeneration", "FSR Frame Gen", 4),
        ("ffx_frameinterpolation", "FSR Frame Gen", 4),
        ("amd_fidelityfx", "FSR", 3),
        ("ffx_fsr", "FSR", 3),
        ("libxess_fg.dll", "XeSS Frame Gen", 6),
        ("libxess.dll", "XeSS", 5),
        ("sl.reflex.dll", "Reflex", 7),
        ("nvlowlatencyvk.dll", "Reflex", 7),
    };

    /// <summary>e.g. "DLSS 3.8.10 · DLSS Frame Gen 3.8.10 · Reflex"; "" for none; null when the game's files can't be listed.</summary>
    public static string? Describe(uint pid)
    {
        var found = new Dictionary<string, (string Text, int Order)>();
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snap == INVALID_HANDLE_VALUE) return null;
        try
        {
            var e = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            for (bool ok = Module32FirstW(snap, ref e); ok; ok = Module32NextW(snap, ref e))
            {
                string file = e.szModule.ToLowerInvariant();
                foreach (var (prefix, name, order) in Known)
                {
                    if (!file.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (!found.ContainsKey(name))
                    {
                        string v = name == "Reflex" ? "" : Version(e.szExePath);
                        found[name] = (v.Length > 0 ? $"{name} {v}" : name, order);
                    }
                    break;
                }
            }
        }
        finally { CloseHandle(snap); }
        return string.Join(" · ", found.Values.OrderBy(v => v.Order).Select(v => v.Text));
    }

    /// <summary>"3.8.10" from the file's version.</summary>
    private static string Version(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            if (v.FileMajorPart == 0 && v.FileMinorPart == 0) return "";
            return v.FileBuildPart > 0 ? $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}" : $"{v.FileMajorPart}.{v.FileMinorPart}";
        }
        catch { return ""; }
    }

    private const uint TH32CS_SNAPMODULE = 0x8, TH32CS_SNAPMODULE32 = 0x10;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Module32FirstW(IntPtr snap, ref MODULEENTRY32W entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Module32NextW(IntPtr snap, ref MODULEENTRY32W entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// NVIDIA's own DLSS on-screen indicator: with it on, DLSS writes its mode, render resolution and version (and Frame
/// Generation's state) in a corner of every DLSS game. It's a driver setting under HKLM, so changing it asks for
/// administrator approval; a game picks it up when it starts.
/// </summary>
internal static class NvidiaIndicator
{
    private const string Key = @"SOFTWARE\NVIDIA Corporation\Global\NGXCore";
    private const int On = 0x400;

    /// <summary>An NVIDIA driver with DLSS is installed.</summary>
    public static bool Available
    {
        get
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(Key); return k != null; }
            catch { return false; }
        }
    }

    public static bool IsOn
    {
        get
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(Key);
                return k?.GetValue("ShowDlssIndicator") is int v && v != 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Switches it (DLSS's indicator and Frame Generation's detailed one); false if it was declined or failed.</summary>
    public static bool Set(bool on)
    {
        string k = @"HKLM\" + Key;
        string cmd = $"reg add \"{k}\" /v ShowDlssIndicator /t REG_DWORD /d {(on ? On : 0)} /f && " +
                     $"reg add \"{k}\" /v DLSSG_IndicatorText /t REG_DWORD /d {(on ? 2 : 0)} /f";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p == null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0 && IsOn == on;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // the approval was declined
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("NVIDIA DLSS indicator", ex);
            return false;
        }
    }
}
