using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DaisysApp.Logging;

namespace DaisysApp.Shell;

/// <summary>
/// The graphics card can stop drawing WPF windows altogether (seen on a GTX 1080 Ti after weeks without a restart: every
/// WPF window plain white, the app itself fine and drawing in software fine; resetting the driver, Win+Ctrl+Shift+B, or
/// restarting Windows fixes it). Once the window shows, this looks at what it shows every few seconds (PrintWindow, from
/// its own thread): the whole inside one colour twice in a row means nothing was drawn, so the app restarts drawing in
/// software (<c>--software</c>, for that run only). Anything drawn ends the check.
/// </summary>
public static class BlankWindowCheck
{
    private const int Every = 4000, Tries = 10;

    public static void Watch(MainWindow window)
    {
        if (window.IsVisible) { Start(window); return; }
        // (started hidden in the tray: checked when it's first shown)
        void Shown(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!window.IsVisible) return;
            window.IsVisibleChanged -= Shown;
            Start(window);
        }
        window.IsVisibleChanged += Shown;
    }

    private static void Start(MainWindow window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        new Thread(() =>
        {
            int blank = 0;
            for (int i = 0; i < Tries; i++)
            {
                Thread.Sleep(Every);
                bool? seen = IsBlank(hwnd);
                if (seen == false) return;
                // (hidden or minimized: can't tell, look again later)
                if (seen == null) { blank = 0; continue; }
                if (++blank < 2) continue;
                Log.App.Warn("The window is still blank: the graphics card isn't drawing it. Restarting, drawn in software this time");
                window.Dispatcher.BeginInvoke(() => window.Restart("--software"));
                return;
            }
        }) { IsBackground = true, Name = "DaisysApp-BlankWindowCheck" }.Start();
    }

    /// <summary>Whether the window's inside is all one colour; null when it can't be seen (hidden, minimized, tiny).</summary>
    private static bool? IsBlank(IntPtr hwnd)
    {
        try
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetClientRect(hwnd, out var r) || r.Right < 100 || r.Bottom < 100) return null;
            int w = r.Right, h = r.Bottom;
            IntPtr screen = GetDC(hwnd), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h), old = SelectObject(mem, bmp);
            try
            {
                if (!PrintWindow(hwnd, mem, 3 /* PW_CLIENTONLY | PW_RENDERFULLCONTENT */)) return null;
                SelectObject(mem, old);
                var info = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
                var pixels = new int[w * h];
                if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref info, 0) != h) return null;
                int first = pixels[0] & 0xFFFFFF;
                for (int i = 0; i < pixels.Length; i += 7)
                    if ((pixels[i] & 0xFFFFFF) != first) return false;
                return true;
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(hwnd, screen);
            }
        }
        catch (Exception ex)
        {
            Log.App.Debug(() => "Checking the window is drawn failed: " + ex.Message);
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, int[] bits, ref BitmapInfoHeader info, uint usage);
}
