using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.QuickTools;

/// <summary>
/// What the Tools sub-tab's buttons do. Each runs off the window's thread and returns a few words on how it went (or
/// throws). Jobs that need administrator rights run Windows' own commands with "runas" (Windows asks first, unless the
/// app already runs as administrator); saying No there is <see cref="OperationCanceledException"/>.
/// </summary>
internal static class Actions
{
    // ---- Display ----

    /// <summary>Win+Ctrl+Shift+B: Windows resets the graphics driver (the screen blinks; nothing closes).</summary>
    public static string ResetGraphics()
    {
        byte[] keys = { 0x5B, 0x11, 0x10, 0x42 }; // left Win, Ctrl, Shift, B
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return T("sent");
    }

    /// <summary>Ends this session's Explorer and starts it again (Windows usually does by itself).</summary>
    public static string RestartExplorer()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("explorer").Where(p => p.SessionId == session))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { }
        }
        for (int i = 0; i < 30 && !ExplorerRunning(session); i++) Thread.Sleep(100);
        if (!ExplorerRunning(session)) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        return T("restarted");
    }

    private static bool ExplorerRunning(int session) => Process.GetProcessesByName("explorer").Any(p => p.SessionId == session);

    /// <summary>Has Windows rebuild its icon cache and redraw the icons.</summary>
    public static string RefreshIcons()
    {
        Run("ie4uinit.exe", "-show", elevated: false);
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        return T("refreshed");
    }

    // ---- Network ----

    public static string FlushDns()
    {
        if (!DnsFlushResolverCache()) throw new Win32Exception(Marshal.GetLastWin32Error());
        return T("flushed");
    }

    public static string RenewIp() => Admin("ipconfig /release >nul & ipconfig /renew >nul", T("renewed"));

    public static string ResetNetwork() => Admin("netsh winsock reset >nul && netsh int ip reset >nul", T("done; restart Windows to finish"));

    // ---- System ----

    /// <summary>Restarts Windows' audio services (Audio Endpoint Builder, and Windows Audio, which needs it).</summary>
    public static string RestartAudio() =>
        Admin("net stop audiosrv /y >nul 2>&1 & net stop AudioEndpointBuilder /y >nul 2>&1 & net start AudioEndpointBuilder >nul 2>&1 & net start audiosrv >nul", T("restarted"));

    public static string SyncClock() => Admin("net start w32time >nul 2>&1 & w32tm /resync /force >nul", T("synced"));

    public static string EmptyRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        SHQueryRecycleBin(null, ref info);
        if (info.i64NumItems == 0) return T("already empty");
        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return F("{0} emptied ({1})", P(info.i64NumItems, "{0} item", "{0} items"), Sizes.Bytes(info.i64Size));
    }

    public static string ClearClipboard()
    {
        Application.Current.Dispatcher.Invoke(Clipboard.Clear);
        return T("cleared");
    }

    /// <summary>DISM's RestoreHealth then SFC, in a console window of their own (they take a while and say how far).</summary>
    public static string RepairWindows()
    {
        Start("cmd.exe", "/k DISM /Online /Cleanup-Image /RestoreHealth && sfc /scannow", elevated: true, hidden: false);
        return T("started in its own window");
    }

    // ---- Power ----

    public static string Lock()
    {
        if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
        return T("locked");
    }

    public static string Sleep()
    {
        if (!SetSuspendState(false, false, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return T("woke up");
    }

    public static string SignOut()
    {
        if (!ExitWindowsEx(EWX_LOGOFF, SHTDN_REASON_FLAG_PLANNED)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return T("signing out");
    }

    public static string Restart() => Shutdown("/r /t 0");

    public static string RestartToBios() => Shutdown("/r /fw /t 0");

    public static string AdvancedStartup() => Shutdown("/r /o /t 0");

    public static string ShutDown() => Shutdown("/s /t 0");

    /// <summary>Windows' shutdown command; as administrator when it says access is denied (as /fw needs).</summary>
    private static string Shutdown(string args)
    {
        int code = Run("shutdown.exe", args, elevated: false);
        if (code == 5) code = Run("shutdown.exe", args, elevated: true);
        if (code != 0) throw new InvalidOperationException(F("shutdown said {0}", code));
        return T("on its way");
    }

    // ---- Open ----

    public static string Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return T("opened");
    }

    /// <summary>Windows Terminal as administrator, or PowerShell where it isn't installed.</summary>
    public static string AdminTerminal()
    {
        try { Start("wt.exe", "", elevated: true, hidden: false); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2) { Start("powershell.exe", "", elevated: true, hidden: false); }
        return T("opened");
    }

    // ---- Running Windows' commands ----

    /// <summary>A cmd line as administrator, hidden; <paramref name="done"/> when it exits with 0.</summary>
    private static string Admin(string line, string done)
    {
        int code = Run("cmd.exe", "/c " + line, elevated: true);
        if (code != 0) throw new InvalidOperationException(F("it ended with code {0}", code));
        return done;
    }

    /// <summary>Runs a program hidden and waits for it (2 minutes at most); its exit code.</summary>
    private static int Run(string exe, string args, bool elevated)
    {
        using var p = Start(exe, args, elevated, hidden: true) ?? throw new InvalidOperationException(F("{0} didn't start", exe));
        if (!p.WaitForExit(120_000)) throw new TimeoutException(F("{0} is still running", exe));
        return p.ExitCode;
    }

    private static Process? Start(string exe, string args, bool elevated, bool hidden)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = true,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            Verb = elevated && !ModuleTasks.IsElevated ? "runas" : "",
        };
        try { return Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException(T("Windows' question was answered No")); }
    }

    // ---- Windows ----

    private const uint KEYEVENTF_KEYUP = 0x2;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;
    private const uint EWX_LOGOFF = 0, SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool LockWorkStation();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("powrprof.dll", SetLastError = true)] private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);
    [DllImport("dnsapi.dll", SetLastError = true)] private static extern bool DnsFlushResolverCache();
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
}
