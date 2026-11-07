using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Shared.Win32;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Black screens: a black, borderless, topmost window over every monitor (for OLEDs: as good as off), no pointer on
/// them. Kept on top and fitted to the monitors (one plugged in or out) every 2 s while on; a double-click on one
/// ends it, as the shortcut does.
/// </summary>
internal sealed class Blackout : IDisposable
{
    private readonly List<(Window Window, PixelRect Bounds)> windows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>Raised when the monitors go black or come back (a double-click too), for the log.</summary>
    public event Action? Changed;

    public bool On => windows.Count > 0;

    public Blackout() => timer.Tick += (_, _) => Keep();

    public void Set(bool on)
    {
        if (on == On) return;
        if (on) Show(); else Hide();
        Changed?.Invoke();
    }

    private void Show()
    {
        foreach (var r in Monitors())
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
        timer.Start();
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
        var now = Monitors();
        if (!now.SequenceEqual(windows.Select(x => x.Bounds)))
        {
            Hide();
            Show();
            return;
        }
        foreach (var (w, _) in windows) WindowTools.BringToTop(w);
    }

    private static List<PixelRect> Monitors()
    {
        var list = new List<PixelRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT r, IntPtr _) =>
        {
            list.Add(PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public void Dispose() => Hide();

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
}
