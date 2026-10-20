using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Video.MiniMirror;

/// <summary>
/// A monitor's picture through GDI (BitBlt from the screen): slower than desktop duplication, but it doesn't depend on
/// it. Used to tell a really still screen from a duplication that has stopped sending what changes, and instead of the
/// duplication on a monitor where it keeps doing that.
/// </summary>
internal sealed class ScreenGrab : IDisposable
{
    private IntPtr screenDc, memDc, bitmap, oldBitmap, bits;
    private byte[]? sample;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Stride => Width * 4;
    public IntPtr Bits => bits;

    /// <summary>Copies <paramref name="bounds"/> (physical pixels on the virtual screen) into <see cref="Bits"/>.</summary>
    public bool Grab(PixelRect bounds)
    {
        if (bounds.IsEmpty) return false;
        if (screenDc == IntPtr.Zero) screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        if (bitmap == IntPtr.Zero || Width != bounds.Width || Height != bounds.Height)
        {
            FreeBitmap();
            memDc = CreateCompatibleDC(screenDc);
            var info = new BITMAPINFOHEADER { biSize = 40, biWidth = bounds.Width, biHeight = -bounds.Height, biPlanes = 1, biBitCount = 32 };
            bitmap = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero) { FreeBitmap(); return false; }
            oldBitmap = SelectObject(memDc, bitmap);
            Width = bounds.Width;
            Height = bounds.Height;
            sample = null;
        }
        bool ok = BitBlt(memDc, 0, 0, Width, Height, screenDc, bounds.X, bounds.Y, SRCCOPY);
        GdiFlush();
        return ok;
    }

    /// <summary>
    /// Whether the last grab differs from the one before (every 4th row compared; the pointer isn't in GDI's picture,
    /// so moving it doesn't count). Null the first time, with nothing to compare with.
    /// </summary>
    public unsafe bool? ChangedSinceLast()
    {
        int rowBytes = Stride, rows = (Height + 3) / 4;
        var now = new byte[rowBytes * rows];
        for (int r = 0; r < rows; r++)
            new ReadOnlySpan<byte>((byte*)bits + (long)r * 4 * rowBytes, rowBytes).CopyTo(now.AsSpan(r * rowBytes));
        var before = sample;
        sample = now;
        return before == null || before.Length != now.Length ? null : !before.AsSpan().SequenceEqual(now);
    }

    /// <summary>Forgets the last grab, so the next <see cref="ChangedSinceLast"/> starts over.</summary>
    public void ForgetLast() => sample = null;

    private void FreeBitmap()
    {
        if (memDc != IntPtr.Zero && oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memDc != IntPtr.Zero) DeleteDC(memDc);
        memDc = bitmap = oldBitmap = bits = IntPtr.Zero;
        Width = Height = 0;
    }

    public void Dispose()
    {
        FreeBitmap();
        if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        screenDc = IntPtr.Zero;
    }

    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
