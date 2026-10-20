using System.Diagnostics;
using System.Runtime.InteropServices;
using DaisysApp.Logging;
using Microsoft.Win32;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.MiniMirror;

/// <summary>
/// What the Video log says about the PC the first time Mini Mirror captures (Windows build, GPUs and drivers, monitors
/// and their HDR, GPU scheduling, multiplane overlays, Windows' game settings), and what's in front when a capture
/// gets stuck: the things that decide whether desktop duplication works, for a mirror that freezes on someone's PC.
/// </summary>
internal static class CaptureDiagnostics
{
    private static readonly Logger log = Log.Here;
    private static int logged;

    public static void LogSystemOnce()
    {
        if (Interlocked.Exchange(ref logged, 1) != 0) return;
        try
        {
            var lines = new List<string> { $"Mini Mirror on {WindowsVersion()}; {GraphicsSettings()}" };
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    string driver = adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out long umd).Success
                        ? $"{umd >> 48 & 0xFFFF}.{umd >> 32 & 0xFFFF}.{umd >> 16 & 0xFFFF}.{umd & 0xFFFF}" : "?";
                    lines.Add($"  GPU {a}: {d.Description} (vendor 0x{d.VendorId:X4}, {d.DedicatedVideoMemory / (1024 * 1024)} MB, driver {driver}{((d.Flags & AdapterFlags.Software) != 0 ? ", software" : "")})");
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                        using (output) lines.Add("    " + Describe(output));
                }
            }
            log.Info(string.Join("\n", lines));
        }
        catch (Exception ex) { log.Info($"Mini Mirror: couldn't describe the PC's graphics ({ex.Message})"); }
    }

    /// <summary>One monitor: where it is, its rotation, and whether Windows runs it as HDR.</summary>
    public static string Describe(IDXGIOutput output)
    {
        var d = output.Description;
        var r = d.DesktopCoordinates;
        string text = $"{d.DeviceName}: {r.Right - r.Left}x{r.Bottom - r.Top} at {r.Left},{r.Top}, {MonitorService.GetRefreshRateHz(d.DeviceName)} Hz{(d.Rotation is ModeRotation.Identity or ModeRotation.Unspecified ? "" : ", rotated " + d.Rotation)}{(d.AttachedToDesktop ? "" : ", not on the desktop")}";
        using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
        if (output6 != null)
        {
            var d1 = output6.Description1;
            bool hdr = d1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
            text += $", {(hdr ? "HDR" : "SDR")} ({d1.BitsPerColor} bits, up to {d1.MaxLuminance:0} nits)";
        }
        return text;
    }

    private static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return $"{key?.GetValue("ProductName")} {key?.GetValue("DisplayVersion")} (build {key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")})";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    /// <summary>Hardware-accelerated GPU scheduling, multiplane overlays and Windows' "Optimizations for windowed games".</summary>
    private static string GraphicsSettings()
    {
        string hags = "default", mpo = "on", games = "default";
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                if (key?.GetValue("HwSchMode") is int h) hags = h == 2 ? "on" : h == 1 ? "off" : h.ToString();
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"))
                if (key?.GetValue("OverlayTestMode") is int m) mpo = m == 5 ? "off (OverlayTestMode 5)" : $"OverlayTestMode {m}";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                if (key?.GetValue("DirectXUserGlobalSettings") is string s && s.Length > 0) games = s.TrimEnd(';');
        }
        catch { }
        return $"GPU scheduling {hags}, multiplane overlay {mpo}, DirectX game settings {games}";
    }

    /// <summary>The window in front: its program, size, and whether it fills the monitor (a full-screen game).</summary>
    public static string Foreground(PixelRect monitor)
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "nothing in front";
            GetWindowThreadProcessId(hwnd, out uint pid);
            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { name = $"process {pid}"; }
            if (!Native.GetWindowRect(hwnd, out var r)) return $"in front: {name}";
            var bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            bool full = !monitor.IsEmpty && bounds.Intersect(monitor).Equals(monitor);
            return $"in front: {name}, {bounds.Width}x{bounds.Height} at {bounds.X},{bounds.Y}{(full ? ", filling this monitor" : "")}";
        }
        catch (Exception ex) { return $"in front: ? ({ex.Message})"; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
