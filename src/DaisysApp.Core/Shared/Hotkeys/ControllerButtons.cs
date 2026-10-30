using System.Runtime.InteropServices;
using System.Windows;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Game controller / wheel / button box presses, read with the classic Windows joystick API (up to 16 controllers,
/// buttons 1–32 each). Polling only runs while something holds a <see cref="Listen"/> handle. Button names look like
/// "Controller 2 Button 7" and can be stored wherever a keyboard shortcut string is. The controllers are looked for when
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

    public static bool IsButton(string? name) => name != null && name.StartsWith("Controller ", StringComparison.OrdinalIgnoreCase);

    public static string Name(int controller, int button) => $"Controller {controller + 1} Button {button + 1}";

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

            // Asking about an unplugged controller is slow: all of them only when polling starts or on Rescan
            if (scan || rescan)
            {
                scan = rescan = false;
                connected.Clear();
                lost.Clear();
                for (int id = 0; id < MaxControllers; id++)
                    if (GetPos(id, ref info) == 0) { connected.Add(id); previous[id] = info.dwButtons; }
            }

            long now = Environment.TickCount64;
            foreach (int id in connected)
            {
                if (lost.TryGetValue(id, out long retry) && now < retry) continue;
                if (GetPos(id, ref info) != 0) { lost[id] = now + RetryMs; continue; }
                if (lost.Remove(id)) { previous[id] = info.dwButtons; continue; }
                uint down = info.dwButtons & ~previous[id];
                previous[id] = info.dwButtons;
                for (int b = 0; down != 0; b++, down >>= 1)
                    if ((down & 1) != 0) Raise(Name(id, b));
            }

            Thread.Sleep(PollMs);
        }
    }

    private static void Raise(string name)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() => Pressed?.Invoke(name));
    }

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
