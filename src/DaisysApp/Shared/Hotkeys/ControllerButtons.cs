using System.Runtime.InteropServices;
using System.Windows;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Game controller / wheel / button box presses, read with the classic Windows joystick API (up to 16 controllers,
/// buttons 1–32 each). Polling only runs while something holds a <see cref="Listen"/> handle. Button names look like
/// "Controller 2 Button 7" and can be stored wherever a keyboard shortcut string is.
/// </summary>
public static class ControllerButtons
{
    private const int MaxControllers = 16;
    private const int PollMs = 25;
    private const int RescanMs = 3000;

    private static readonly object gate = new();
    private static int listeners;
    private static Thread? thread;

    /// <summary>Raised on the UI thread when a button goes down.</summary>
    public static event Action<string>? Pressed;

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
        long nextScan = 0;
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

            // Asking about an unplugged controller is slow, so look for connected ones only every few seconds.
            if (Environment.TickCount64 >= nextScan)
            {
                connected.Clear();
                for (int id = 0; id < MaxControllers; id++)
                    if (joyGetPosEx(id, ref info) == 0) { connected.Add(id); previous[id] = info.dwButtons; }
                nextScan = Environment.TickCount64 + RescanMs;
            }

            foreach (int id in connected)
            {
                if (joyGetPosEx(id, ref info) != 0) { nextScan = 0; continue; }
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

    [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);
}
