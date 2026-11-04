using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Game controller / wheel / button box presses, read with the classic Windows joystick API (up to 16 controllers,
/// buttons 1–32 each). Polling only runs while something holds a <see cref="Listen"/> handle. A button is named by its
/// controller's DirectInput instance, which stays the same when Windows numbers the controllers differently:
/// "Controller MOZA R12 Button 7 {guid}" (since Core 0.53; <see cref="Describe"/> shows "MOZA R12 Button 7"), stored
/// wherever a keyboard shortcut string is. One named by number from before ("Controller 2 Button 7") still works, by
/// number, until <see cref="Upgrade"/> names it by the controller that has the number. The controllers are looked for when
/// polling starts and again only on <see cref="Rescan"/> (a module's Refresh button); one that stops answering is asked
/// again on its own every few seconds (since Core 0.46: before, all 16 were asked every 3 s, which a game feels).
/// </summary>
public static class ControllerButtons
{
    private const int MaxControllers = 16;
    private const int PollMs = 25;
    private const int RetryMs = 3000;

    private static readonly object gate = new();
    private static int listeners;
    private static Thread? thread;
    private static volatile bool rescan;

    /// <summary>Raised on the UI thread when a button goes down.</summary>
    public static event Action<string>? Pressed;

    /// <summary>Raised on the UI thread when a button comes up (or its controller stops answering).</summary>
    public static event Action<string>? Released;

    /// <summary>
    /// Held around every call to Windows' classic joystick API (winmm's joy* functions), here and in Button2Key, so it's
    /// never entered from two threads at once while Windows rebuilds its list of controllers (a pad plugged in or
    /// dropping out). (Since Core 0.38.)
    /// </summary>
    public static readonly object JoystickApi = new();

    /// <summary>
    /// Held around every DirectInput call, in every module (ForzaWheel, Button2Key, SimEffects, Calibration): each has
    /// its own DirectInput and threads, and a wheel's force-feedback driver runs inside the app (Fanatec's crashed in it,
    /// with memory corrupted elsewhere before, on the rig), so it's never entered from two threads at once. Never take
    /// <see cref="JoystickApi"/> while holding it. (Since Core 0.44.)
    /// </summary>
    public static readonly object DirectInput = new();

    /// <summary>Looks for the controllers again (all 16 ids), at the next poll. (Since Core 0.46.)</summary>
    public static void Rescan() => rescan = true;

    /// <summary>While true nothing is asked (Devices' Apply Order, which needs the devices let go); a rescan follows.
    /// (Since Core 0.52.)</summary>
    public static bool Paused
    {
        get => paused;
        set { paused = value; if (!value) rescan = true; }
    }
    private static volatile bool paused;

    public static bool IsButton(string? name) => name != null && name.StartsWith("Controller ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The button's name: by its controller's instance when it's known, else by number.</summary>
    public static string Name(int controller, int button) => devices.FirstOrDefault(d => d.Id == controller) is { } d
        ? $"Controller {d.Name} Button {button + 1} {{{d.Instance}}}"
        : $"Controller {controller + 1} Button {button + 1}";

    /// <summary>Whether two shortcuts are the same: two button names of the same button (one named by number: the
    /// controller that has that number now), or the same text.</summary>
    public static bool Same(string? a, string? b)
    {
        if (a == null || b == null) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Parse(a, out var ga, out int na, out int ba) || !Parse(b, out var gb, out int nb, out int bb) || ba != bb) return false;
        if (ga != Guid.Empty && gb != Guid.Empty) return ga == gb;
        return NumberOf(ga, na) is int x && x == NumberOf(gb, nb);
    }

    /// <summary>A shortcut as it's shown: "MOZA R12 Button 7" for a button named by its controller, else as it is.</summary>
    public static string Describe(string value)
    {
        var m = ById.Match(value);
        return m.Success ? $"{m.Groups["name"].Value} Button {m.Groups["b"].Value}" : value;
    }

    /// <summary>A button named by number, named by the controller that has that number now (null: not one, or that
    /// controller isn't plugged in).</summary>
    public static string? Upgrade(string value)
    {
        var m = ByNumber.Match(value);
        if (!m.Success) return null;
        int id = int.Parse(m.Groups["n"].Value) - 1;
        return devices.Any(d => d.Id == id) ? Name(id, int.Parse(m.Groups["b"].Value) - 1) : null;
    }

    /// <summary>Raised on the UI thread when which controller has which number has changed.</summary>
    public static event Action? Identified;

    private static readonly Regex ById = new(@"^Controller (?<name>.+) Button (?<b>\d+) \{(?<g>[0-9a-fA-F-]{36})\}$");
    private static readonly Regex ByNumber = new(@"^Controller (?<n>\d+) Button (?<b>\d+)$");

    private static bool Parse(string name, out Guid instance, out int number, out int button)
    {
        instance = Guid.Empty;
        number = button = -1;
        if (ById.Match(name) is { Success: true } m)
        {
            instance = Guid.Parse(m.Groups["g"].Value);
            button = int.Parse(m.Groups["b"].Value);
            return true;
        }
        if (ByNumber.Match(name) is not { Success: true } n) return false;
        number = int.Parse(n.Groups["n"].Value) - 1;
        button = int.Parse(n.Groups["b"].Value);
        return true;
    }

    private static int? NumberOf(Guid instance, int number) =>
        instance == Guid.Empty ? number : devices.FirstOrDefault(d => d.Instance == instance)?.Id;

    /// <summary>A controller plugged in: its joystick number now, DirectInput instance and name.</summary>
    private sealed record Device(int Id, Guid Instance, string Name);

    // (replaced whole by the polling thread, read from any)
    private static volatile IReadOnlyList<Device> devices = Array.Empty<Device>();

    /// <summary>Starts polling (if it isn't already) until the returned handle is disposed.</summary>
    public static IDisposable Listen()
    {
        lock (gate)
        {
            listeners++;
            if (thread == null)
            {
                thread = new Thread(Poll) { IsBackground = true, Name = "DaisysApp-Controllers" };
                thread.Start();
            }
        }
        return new Handle();
    }

    private sealed class Handle : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (gate) listeners--;
        }
    }

    private static void Poll()
    {
        try { PollButtons(); }
        catch (Exception ex)
        {
            DaisysApp.Logging.ErrorLog.Write("Controller buttons", ex);
            lock (gate) thread = null;
        }
    }

    private static void PollButtons()
    {
        var previous = new uint[MaxControllers];
        var connected = new List<int>();
        var lost = new Dictionary<int, long>();   // a controller that stopped answering: when to ask it again
        bool scan = true;
        var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };

        while (true)
        {
            lock (gate)
            {
                if (listeners <= 0)
                {
                    thread = null;
                    return;
                }
            }

            if (paused)
            {
                Thread.Sleep(PollMs);
                continue;
            }

            // Asking about an unplugged controller is slow: all of them only when polling starts or on Rescan
            if (scan || rescan)
            {
                scan = rescan = false;
                connected.Clear();
                lost.Clear();
                for (int id = 0; id < MaxControllers; id++)
                    if (GetPos(id, ref info) == 0) { connected.Add(id); previous[id] = info.dwButtons; }
                Identify(connected);
            }

            long now = Environment.TickCount64;
            foreach (int id in connected)
            {
                if (lost.TryGetValue(id, out long retry) && now < retry) continue;
                if (GetPos(id, ref info) != 0)
                {
                    lost[id] = now + RetryMs;
                    RaiseUp(id, previous[id]);
                    previous[id] = 0;
                    continue;
                }
                if (lost.Remove(id))
                {
                    // (back: plugged in again, maybe with another number)
                    previous[id] = info.dwButtons;
                    Identify(connected);
                    continue;
                }
                uint down = info.dwButtons & ~previous[id];
                uint up = previous[id] & ~info.dwButtons;
                previous[id] = info.dwButtons;
                RaiseUp(id, up);
                for (int b = 0; down != 0; b++, down >>= 1)
                    if ((down & 1) != 0) Raise(Name(id, b));
            }

            Thread.Sleep(PollMs);
        }
    }

    private static void RaiseUp(int id, uint up)
    {
        for (int b = 0; up != 0; b++, up >>= 1)
            if ((up & 1) != 0)
            {
                string name = Name(id, b);
                Application.Current?.Dispatcher.BeginInvoke(() => Released?.Invoke(name));
            }
    }

    private static void Raise(string name)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() => Pressed?.Invoke(name));
    }

    // ---- which controller has which number: DirectInput's joystick configuration (joy.cpl's), no administrator rights

    private const int ConfigSize = 1176, GuidOffset = 4, TypeOffset = 136, TypeChars = 256;
    private const uint GetInstanceAndType = 1 | 2;   // DIJC_GUIDINSTANCE, DIJC_REGHWCONFIGTYPE

    private static void Identify(List<int> ids)
    {
        var found = new List<Device>();
        try
        {
            lock (DirectInput)
            {
                if (OpenConfig() is { } joy)
                    try
                    {
                        foreach (int id in ids)
                        {
                            var config = new byte[ConfigSize];
                            BitConverter.TryWriteBytes(config, ConfigSize);
                            if (joy.GetConfig(id, config, GetInstanceAndType) != 0) continue;
                            var instance = new Guid(config.AsSpan(GuidOffset, 16));
                            if (instance == Guid.Empty) continue;
                            string type = Encoding.Unicode.GetString(config, TypeOffset, TypeChars * 2);
                            int end = type.IndexOf('\0');
                            if (end >= 0) type = type[..end];
                            found.Add(new Device(id, instance, OemName(type) ?? (type.Length > 0 ? type : $"{id + 1}")));
                        }
                    }
                    finally { Marshal.ReleaseComObject(joy); }
            }
        }
        catch (Exception ex) { DaisysApp.Logging.Log.App.Warn($"Couldn't tell the controllers apart: {ex.Message}"); }
        var before = devices;
        devices = found;
        if (found.SequenceEqual(before)) return;
        DaisysApp.Logging.Log.App.Info("Controller buttons: " + (found.Count == 0 ? "no controllers known by instance"
            : string.Join(", ", found.Select(d => $"{d.Id + 1} {d.Name} {{{d.Instance}}}"))));
        Application.Current?.Dispatcher.BeginInvoke(() => Identified?.Invoke());
    }

    /// <summary>The name Windows shows for a controller type ("VID_346E&amp;PID_0006"), as joy.cpl does.</summary>
    private static string? OemName(string type)
    {
        if (type.Length == 0) return null;
        string key = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{type}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(key);
                if (k?.GetValue("OEMName") is string s && s.Trim().Length > 0) return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
            catch { }
        }
        return null;
    }

    private static IJoyConfig? OpenConfig()
    {
        var iid = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");   // IID_IDirectInput8W
        if (DirectInput8Create(GetModuleHandleW(null), 0x0800, ref iid, out var raw, 0) != 0) return null;
        try { return (IJoyConfig)Marshal.GetObjectForIUnknown(raw); }
        finally { Marshal.Release(raw); }
    }

    [ComImport, Guid("EB0D7DFA-1990-4F27-B4D6-EDF2EEC4A44C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IJoyConfig
    {
        [PreserveSig] int Acquire();
        [PreserveSig] int Unacquire();
        [PreserveSig] int SetCooperativeLevel(IntPtr window, uint flags);
        [PreserveSig] int SendNotify();
        void EnumTypes();
        void GetTypeInfo();
        void SetTypeInfo();
        void DeleteType();
        [PreserveSig] int GetConfig(int id, [In, Out] byte[] config, uint flags);
    }

    [DllImport("dinput8.dll")] private static extern int DirectInput8Create(IntPtr instance, int version, ref Guid iid, out IntPtr result, IntPtr outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    private const int JOY_RETURNBUTTONS = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public uint dwButtons;
        public int dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    private static int GetPos(int id, ref JOYINFOEX info)
    {
        lock (JoystickApi) return joyGetPosEx(id, ref info);
    }

    [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);
}
