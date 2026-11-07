using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Shared.Win32;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Black screens: a black, borderless, topmost window over every monitor picked (<see cref="Covers"/>; for OLEDs: as
/// good as off), no pointer on them. Kept on top and fitted to the monitors (one plugged in or out) every 2 s while
/// on; a double-click on one ends it, as the shortcut does.
/// </summary>
internal sealed class Blackout : IDisposable
{
    private readonly List<(Window Window, PixelRect Bounds)> windows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>Raised when the monitors go black or come back (a double-click too), for the log.</summary>
    public event Action? Changed;

    public bool On => windows.Count > 0;

    /// <summary>Whether the monitor of this device name (Windows' DISPLAY2…) goes black.</summary>
    public Func<string, bool> Covers { get; set; } = _ => true;

    /// <summary>A monitor: its device name, where it is in real pixels, and whether it's the main one.</summary>
    public sealed record Display(string Name, PixelRect Bounds, bool Primary)
    {
        /// <summary>Its number, as Windows' Display settings mostly show it (the device name's).</summary>
        public string Number => new(Name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
    }

    public Blackout() => timer.Tick += (_, _) => Keep();

    public void Set(bool on)
    {
        if (on == On) return;
        if (on)
        {
            Show();
            if (!On) return; // no monitor picked
        }
        else Hide();
        Changed?.Invoke();
    }

    /// <summary>The monitors picked changed: black over those now, if it's on.</summary>
    public void Refit()
    {
        if (!On) return;
        Hide();
        Show();
        if (!On) Changed?.Invoke();
    }

    private void Show()
    {
        foreach (var r in Picked())
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Black,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true, Cursor = Cursors.None,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 1, Height = 1, Title = "Black screen",
            };
            w.MouseDoubleClick += (_, _) => Set(false);
            WindowTools.MakeToolWindow(w);
            w.Show();
            // twice: the first move to a monitor of another DPI has WPF resize it for that DPI
            WindowTools.SetBounds(w, r);
            WindowTools.SetBounds(w, r);
            WindowTools.BringToTop(w);
            windows.Add((w, r));
        }
        if (On) timer.Start();
    }

    private void Hide()
    {
        timer.Stop();
        foreach (var (w, _) in windows) w.Close();
        windows.Clear();
    }

    /// <summary>Back on top (the taskbar or a game can come above), and over the monitors there are now.</summary>
    private void Keep()
    {
        if (!Picked().SequenceEqual(windows.Select(x => x.Bounds)))
        {
            Hide();
            Show();
            if (!On) Changed?.Invoke();
            return;
        }
        foreach (var (w, _) in windows) WindowTools.BringToTop(w);
    }

    private List<PixelRect> Picked() => Displays().Where(d => Covers(d.Name)).Select(d => d.Bounds).ToList();

    /// <summary>The desktop's monitors, as Windows lists them.</summary>
    public static List<Display> Displays()
    {
        var list = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT r, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(monitor, ref info);
            list.Add(new Display(info.szDevice ?? "", PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public void Dispose() => Hide();

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
}
