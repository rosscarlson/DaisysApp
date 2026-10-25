using System.Runtime.InteropServices;
using DaisysApp.Logging;
using Microsoft.Win32;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>A display on the virtual screen, in physical pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

/// <summary>A small picture of one display (BGRA, the desktop's way up), and where the display is.</summary>
internal sealed record Thumb(int Number, ScreenRect Bounds, byte[] Pixels, int Width, int Height);

/// <summary>
/// Every display, shrunk: one <see cref="DisplayCapture"/> each (desktop duplication, the picture shrunk on the
/// graphics card so only a few hundred pixels across come back; GDI when duplication can't be had). Read on one
/// thread (the AmbiLight loop); <see cref="Thumbs"/> and <see cref="Displays"/> from any.
/// </summary>
internal sealed class ScreenSampler : IDisposable
{
    private readonly object gate = new();
    private List<DisplayCapture> captures = new();
    private volatile bool changed = true;

    public ScreenSampler() => SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

    private void OnDisplaysChanged(object? sender, EventArgs e) => changed = true;

    /// <summary>The displays, numbered left to right, with Windows' names (\\.\DISPLAY1).</summary>
    public IReadOnlyList<(int Number, ScreenRect Bounds, string Name)> Displays
    {
        get
        {
            lock (gate)
                if (captures.Count > 0) return captures.Select(c => (c.Number, c.Bounds, c.DeviceName)).ToList();
            return Find().Select(d => (d.Number, d.Bounds, d.DeviceName)).ToList(); // before the loop has run
        }
    }

    /// <summary>Displays the lights don't take colours from (Windows' names); set from the settings.</summary>
    public HashSet<string> Off { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The displays the lights use (all of them when none is ticked).</summary>
    public List<ScreenRect> Used
    {
        get
        {
            var all = Displays;
            var on = all.Where(d => !Off.Contains(d.Name)).Select(d => d.Bounds).ToList();
            return on.Count > 0 ? on : all.Select(d => d.Bounds).ToList();
        }
    }

    /// <summary>All the displays together.</summary>
    public ScreenRect Desktop
    {
        get
        {
            var d = Displays;
            if (d.Count == 0) return new ScreenRect(0, 0, 1920, 1080);
            int x = d.Min(m => m.Bounds.X), y = d.Min(m => m.Bounds.Y);
            return new ScreenRect(x, y, d.Max(m => m.Bounds.Right) - x, d.Max(m => m.Bounds.Bottom) - y);
        }
    }

    /// <summary>A new picture of each display (where there is one). On the loop's thread.</summary>
    public void Capture()
    {
        if (changed)
        {
            changed = false;
            var found = Find();
            lock (gate)
            {
                foreach (var c in captures) c.Dispose();
                captures = found.Select(f => new DisplayCapture(f.DeviceName, f.Number, f.Bounds, f.Rotation)).ToList();
            }
            Log.Here.Info($"AmbiLight: {found.Count} display(s): {string.Join(", ", found.Select(f => $"{f.Number} {f.Bounds.W}x{f.Bounds.H} at {f.Bounds.X},{f.Bounds.Y}"))}");
        }
        List<DisplayCapture> now;
        lock (gate) now = captures;
        foreach (var c in now) c.Update();
    }

    /// <summary>The average colour of <paramref name="zone"/> (virtual-screen pixels), over the displays it's on.</summary>
    public (double R, double G, double B) Average(AmbiZone zone)
    {
        double r = 0, g = 0, b = 0, n = 0;
        List<DisplayCapture> now;
        lock (gate) now = captures;
        var off = Off;
        foreach (var c in now)
            if (!off.Contains(c.DeviceName)) c.Sum(zone, ref r, ref g, ref b, ref n);
        return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
    }

    /// <summary>A copy of each display's small picture, for the tab.</summary>
    public List<Thumb> Thumbs()
    {
        List<DisplayCapture> now;
        lock (gate) now = captures;
        return now.Select(c => c.Thumb()).Where(t => t != null).Select(t => t!).ToList();
    }

    private sealed record Found(string DeviceName, int Number, ScreenRect Bounds, ModeRotation Rotation);

    private static List<Found> Find()
    {
        var list = new List<(string Name, ScreenRect Bounds, ModeRotation Rotation)>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                        using (output)
                        {
                            var d = output.Description;
                            if (!d.AttachedToDesktop) continue;
                            var r = d.DesktopCoordinates;
                            if (list.Any(l => l.Name == d.DeviceName)) continue;
                            list.Add((d.DeviceName, new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), d.Rotation));
                        }
            }
        }
        catch (Exception ex) { Log.Here.Warn("AmbiLight: listing the displays", ex); }
        return list.OrderBy(l => l.Bounds.X).ThenBy(l => l.Bounds.Y)
            .Select((l, i) => new Found(l.Name, i + 1, l.Bounds, l.Rotation)).ToList();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        lock (gate)
        {
            foreach (var c in captures) c.Dispose();
            captures = new();
        }
    }
}

/// <summary>
/// One display's small picture. Desktop duplication on the GPU that drives it: each new frame is copied into a texture
/// with mipmaps, shrunk there (GenerateMips) and only the level a few hundred pixels across is read back. When
/// duplication can't be set up (another program holds it, a secure desktop), GDI's StretchBlt shrinks the screen
/// instead, and duplication is tried again after 5 s, then less and less often (up to every 2 minutes).
/// </summary>
internal sealed class DisplayCapture : IDisposable
{
    private const int MaxWidth = 400;
    private static readonly FeatureLevel[] Levels = { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

    public string DeviceName { get; }
    public int Number { get; }
    public ScreenRect Bounds { get; }
    private readonly ModeRotation rotation;

    private readonly object gate = new();
    private byte[] small = Array.Empty<byte>(); // BGRA, the desktop's way up
    private int sw, sh;

    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? mips, staging;
    private ID3D11ShaderResourceView? view;
    private uint level, frameW, frameH;
    private byte[] raw = Array.Empty<byte>();
    private long retryAt;
    private int retryMs = 5000;
    private string? lastError;
    private GdiShrink? gdi;

    public DisplayCapture(string deviceName, int number, ScreenRect bounds, ModeRotation rotation)
    {
        DeviceName = deviceName;
        Number = number;
        Bounds = bounds;
        this.rotation = rotation;
    }

    public void Update()
    {
        if (duplication == null)
        {
            // setting up takes a few hundred ms (a pause in the lights): tried less often while it keeps failing
            if (Environment.TickCount64 >= retryAt)
            {
                if (TrySetUp()) retryMs = 5000;
                else
                {
                    retryAt = Environment.TickCount64 + retryMs;
                    retryMs = Math.Min(120000, retryMs * 2);
                }
            }
            if (duplication == null) { Gdi(); return; }
        }
        try
        {
            var result = duplication.AcquireNextFrame(0, out var info, out IDXGIResource? desktop);
            if (result.Failure)
            {
                desktop?.Dispose();
                if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) return; // nothing changed
                Note($"frame failed (0x{result.Code:X8})");
                Release();
                return;
            }
            try
            {
                if (info.LastPresentTime == 0 && small.Length > 0) { desktop?.Dispose(); return; } // only the pointer moved
                using (desktop)
                using (var frame = desktop!.QueryInterface<ID3D11Texture2D>())
                {
                    var d = frame.Description;
                    if (d.Width != frameW || d.Height != frameH || mips == null) MakeTextures(d.Width, d.Height);
                    context!.CopySubresourceRegion(mips!, 0, 0, 0, 0, frame, 0);
                }
                context.GenerateMips(view!);
                context.CopySubresourceRegion(staging!, 0, 0, 0, 0, mips!, level);
                var map = context.Map(staging!, 0, MapMode.Read);
                try { Store(map.DataPointer, (int)map.RowPitch); }
                finally { context.Unmap(staging!, 0); }
                lastError = null;
            }
            finally { duplication?.ReleaseFrame(); }
        }
        catch (Exception ex)
        {
            Note(ex.Message);
            Release();
        }
    }

    private bool TrySetUp()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
                using (adapter)
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                        using (output)
                        {
                            if (!string.Equals(output.Description.DeviceName, DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, Levels, out device, out context).CheckError();
                            duplication = output1.DuplicateOutput(device!);
                            frameW = frameH = 0;
                            if (gdi != null) Log.Here.Info($"AmbiLight: display {Number} through desktop duplication again");
                            gdi?.Dispose();
                            gdi = null;
                            return true;
                        }
            Note("no graphics card drives it now");
        }
        catch (Exception ex)
        {
            Note($"{ex.Message.Split('\n')[0]} (using GDI meanwhile)");
            Release();
        }
        return false;
    }

    /// <summary>The texture the frame is shrunk in, and the staging one its small level is read back through.</summary>
    private void MakeTextures(uint w, uint h)
    {
        mips?.Dispose(); view?.Dispose(); staging?.Dispose();
        frameW = w;
        frameH = h;
        level = 0;
        while ((w >> (int)level) > MaxWidth && (h >> (int)level) > 8) level++;
        mips = device!.CreateTexture2D(new Texture2DDescription
        {
            Width = w, Height = h, MipLevels = level + 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        view = device.CreateShaderResourceView(mips);
        staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = Math.Max(1, w >> (int)level), Height = Math.Max(1, h >> (int)level), MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        Log.Here.Debug($"AmbiLight: display {Number} {w}x{h} read at {staging.Description.Width}x{staging.Description.Height}{(rotation is ModeRotation.Identity or ModeRotation.Unspecified ? "" : $", {rotation}")}");
    }

    /// <summary>The read-back level, turned the desktop's way up (duplication gives a turned display's picture as it's scanned).</summary>
    private unsafe void Store(IntPtr data, int pitch)
    {
        int w = (int)Math.Max(1, frameW >> (int)level), h = (int)Math.Max(1, frameH >> (int)level);
        if (raw.Length != w * h * 4) raw = new byte[w * h * 4];
        fixed (byte* d = raw)
            for (int y = 0; y < h; y++) Buffer.MemoryCopy((byte*)data + (long)y * pitch, d + y * w * 4, w * 4, w * 4);
        bool turned = rotation is ModeRotation.Rotate90 or ModeRotation.Rotate270;
        int dw = turned ? h : w, dh = turned ? w : h;
        var o = new byte[dw * dh * 4];
        for (int dy = 0; dy < dh; dy++)
            for (int dx = 0; dx < dw; dx++)
            {
                // as Microsoft's DesktopDuplication sample turns dirty rectangles, backwards
                (int sx, int sy) = rotation switch
                {
                    ModeRotation.Rotate90 => (dy, dw - 1 - dx),
                    ModeRotation.Rotate180 => (w - 1 - dx, h - 1 - dy),
                    ModeRotation.Rotate270 => (dh - 1 - dy, dx),
                    _ => (dx, dy),
                };
                Buffer.BlockCopy(raw, (sy * w + sx) * 4, o, (dy * dw + dx) * 4, 4);
            }
        lock (gate) { small = o; sw = dw; sh = dh; }
    }

    private void Gdi()
    {
        int w = Math.Min(MaxWidth, Bounds.W), h = Math.Max(1, Bounds.H * w / Math.Max(1, Bounds.W));
        gdi ??= new GdiShrink();
        var o = gdi.Grab(Bounds, w, h);
        if (o == null) return;
        lock (gate) { small = o; sw = w; sh = h; }
    }

    /// <summary>Adds the zone's part on this display to the sums (every pixel of the small picture inside it).</summary>
    public void Sum(AmbiZone zone, ref double r, ref double g, ref double b, ref double n)
    {
        double x0 = Math.Max(zone.X, Bounds.X), y0 = Math.Max(zone.Y, Bounds.Y);
        double x1 = Math.Min(zone.X + zone.W, Bounds.Right), y1 = Math.Min(zone.Y + zone.H, Bounds.Bottom);
        if (x1 <= x0 || y1 <= y0) return;
        lock (gate)
        {
            if (sw == 0) return;
            double fx = sw / (double)Bounds.W, fy = sh / (double)Bounds.H;
            int px0 = Math.Clamp((int)((x0 - Bounds.X) * fx), 0, sw - 1), px1 = Math.Clamp((int)Math.Ceiling((x1 - Bounds.X) * fx), px0 + 1, sw);
            int py0 = Math.Clamp((int)((y0 - Bounds.Y) * fy), 0, sh - 1), py1 = Math.Clamp((int)Math.Ceiling((y1 - Bounds.Y) * fy), py0 + 1, sh);
            for (int y = py0; y < py1; y++)
                for (int x = px0, i = (y * sw + px0) * 4; x < px1; x++, i += 4)
                {
                    b += small[i];
                    g += small[i + 1];
                    r += small[i + 2];
                    n++;
                }
        }
    }

    public Thumb? Thumb()
    {
        lock (gate) return sw == 0 ? null : new Thumb(Number, Bounds, (byte[])small.Clone(), sw, sh);
    }

    private void Note(string what)
    {
        if (what == lastError) return;
        lastError = what;
        Log.Here.Info($"AmbiLight: display {Number} ({DeviceName}): {what}");
    }

    private void Release()
    {
        staging?.Dispose(); staging = null;
        view?.Dispose(); view = null;
        mips?.Dispose(); mips = null;
        duplication?.Dispose(); duplication = null;
        context?.Dispose(); context = null;
        device?.Dispose(); device = null;
        frameW = frameH = 0;
    }

    public void Dispose()
    {
        Release();
        gdi?.Dispose();
        gdi = null;
    }
}

/// <summary>A part of the screen shrunk by GDI (StretchBlt, halftone) into a small picture.</summary>
internal sealed class GdiShrink : IDisposable
{
    private IntPtr screenDc, memDc, bitmap, oldBitmap, bits;
    private int width, height;

    public byte[]? Grab(ScreenRect from, int w, int h)
    {
        if (screenDc == IntPtr.Zero) screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        if (bitmap == IntPtr.Zero || w != width || h != height)
        {
            Free();
            memDc = CreateCompatibleDC(screenDc);
            var info = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            bitmap = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero) { Free(); return null; }
            oldBitmap = SelectObject(memDc, bitmap);
            SetStretchBltMode(memDc, HALFTONE);
            SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
            width = w;
            height = h;
        }
        if (!StretchBlt(memDc, 0, 0, w, h, screenDc, from.X, from.Y, from.W, from.H, SRCCOPY)) return null;
        GdiFlush();
        var o = new byte[w * h * 4];
        Marshal.Copy(bits, o, 0, o.Length);
        return o;
    }

    private void Free()
    {
        if (memDc != IntPtr.Zero && oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memDc != IntPtr.Zero) DeleteDC(memDc);
        memDc = bitmap = oldBitmap = bits = IntPtr.Zero;
        width = height = 0;
    }

    public void Dispose()
    {
        Free();
        if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        screenDc = IntPtr.Zero;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const int HALFTONE = 4;

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
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
