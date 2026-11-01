using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DaisysApp.Applets.Binder.Input;

/// <summary>
/// Controllers through the game's own SDL2.dll (FSO's), so their order, GUIDs and button and axis numbers are the game's
/// exactly (from FotG Controls): its device order and GUIDs (which FSO writes), and a device's axes numbered among the ones it
/// has (X, Y, Z, the rotations, the sliders: a stick with only Y and X is Axis 1 X, Axis 2 Y). Polled on
/// the UI thread. Only one SDL2.dll can be loaded in the app: another title's needs a restart.
/// </summary>
public sealed class SdlSource : IInputSource
{
    private sealed class Stick
    {
        public required IntPtr Handle;
        public required LiveDevice Device;
        public required int Axes;
    }

    private readonly List<Stick> sticks = new();
    private readonly Dictionary<LiveDevice, double[]> rest = new();
    private int lastCount = -1;
    private bool started;

    public SdlSource(string? sdlPath)
    {
        if (sdlPath == null || !File.Exists(sdlPath)) { Problem = T("The game's SDL2.dll wasn't found: controllers can't be read"); return; }
        if (Sdl.IsLoaded && !string.Equals(Sdl.LoadedFrom, sdlPath, StringComparison.OrdinalIgnoreCase))
        {
            Problem = T("Restart Daisy's App to read controllers for this title");
            return;
        }
        if (!Sdl.TryLoad(sdlPath!, out var err)) { Problem = F("The game's SDL2.dll couldn't be loaded: {0}", err); return; }
        Sdl.SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
        Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (Sdl.SDL_InitSubSystem(Sdl.SDL_INIT_JOYSTICK) < 0) { Problem = F("SDL couldn't read controllers: {0}", Sdl.Error); return; }
        started = true;
        Enumerate();
    }

    public InputKind Kind => InputKind.Sdl;
    public IReadOnlyList<LiveDevice> Devices { get; private set; } = [];
    public string? Problem { get; }
    public event Action? DevicesChanged;

    public void Refresh() { if (started) Enumerate(); }

    /// <summary>A device's vendor and product from its SDL GUID (bytes 4–5 and 8–9, little-endian).</summary>
    public static (int Vid, int Pid) VidPid(string guid)
    {
        if (guid.Length < 20) return (0, 0);
        int Hex16(int at) => int.Parse(guid.Substring(at + 2, 2) + guid.Substring(at, 2), NumberStyles.HexNumber);
        try { return (Hex16(8), Hex16(16)); } catch { return (0, 0); }
    }

    private void Enumerate()
    {
        foreach (var s in sticks) Sdl.SDL_JoystickClose(s.Handle);
        sticks.Clear();
        rest.Clear();
        int n = Sdl.SDL_NumJoysticks();
        var opened = new List<(IntPtr H, string Guid, string Name, int Index)>();
        for (int i = 0; i < n; i++)
        {
            var h = Sdl.SDL_JoystickOpen(i);
            if (h == IntPtr.Zero) continue;
            var buf = new byte[33];
            Sdl.SDL_JoystickGetGUIDString(Sdl.SDL_JoystickGetGUID(h), buf, buf.Length);
            opened.Add((h, Encoding.ASCII.GetString(buf).TrimEnd('\0').ToUpperInvariant(), Marshal.PtrToStringUTF8(Sdl.SDL_JoystickName(h)) ?? F("Controller {0}", i + 1), i));
        }
        var ids = Nth.Of(opened.Select(o => VidPid(o.Guid)));
        for (int i = 0; i < opened.Count; i++)
        {
            var (h, guid, name, index) = opened[i];
            int axes = Math.Max(0, Sdl.SDL_JoystickNumAxes(h)), buttons = Math.Max(0, Sdl.SDL_JoystickNumButtons(h)), hats = Math.Max(0, Sdl.SDL_JoystickNumHats(h));
            var d = new LiveDevice
            {
                Id = ids[i], Name = name, SdlGuid = guid, SdlIndex = index,
                Buttons = buttons, Hats = hats,
                AxisNames = Enumerable.Range(0, axes).Select(a => F("Axis {0}", a + 1)).ToArray(),
                Axis = new double[axes], Button = new bool[buttons], Hat = new int[hats],
            };
            sticks.Add(new Stick { Handle = h, Device = d, Axes = axes });
            Read(sticks[^1]);
            rest[d] = (double[])d.Axis.Clone();
        }
        lastCount = n;
        Devices = sticks.Select(s => s.Device).ToList();
        DevicesChanged?.Invoke();
    }

    public List<InputEvent> Poll()
    {
        var events = new List<InputEvent>();
        if (!started) return events;
        Sdl.SDL_PumpEvents();
        Sdl.SDL_FlushEvents(0, 0xFFFF);
        if (Sdl.SDL_NumJoysticks() != lastCount) { Enumerate(); return events; }
        long now = Environment.TickCount64;
        foreach (var s in sticks)
        {
            var d = s.Device;
            var wasButtons = (bool[])d.Button.Clone();
            var wasHats = (int[])d.Hat.Clone();
            Read(s);
            for (int b = 0; b < d.Buttons; b++)
                if (d.Button[b] && !wasButtons[b]) { events.Add(new ButtonDown(d, b)); d.ActiveAt = now; }
            for (int h = 0; h < d.Hats; h++)
            {
                int newly = d.Hat[h] & ~wasHats[h];
                for (int dir = 0; dir < 4; dir++)
                    if ((newly & (1 << dir)) != 0) { events.Add(new HatDown(d, h, dir)); d.ActiveAt = now; }
            }
            var r = rest[d];
            for (int a = 0; a < s.Axes; a++)
                if (Math.Abs(d.Axis[a] - r[a]) > 0.03) { r[a] = d.Axis[a]; d.ActiveAt = now; }
        }
        return events;
    }

    private static void Read(Stick s)
    {
        var d = s.Device;
        for (int a = 0; a < s.Axes; a++) d.Axis[a] = Math.Clamp(Sdl.SDL_JoystickGetAxis(s.Handle, a) / 32767.0, -1, 1);
        for (int b = 0; b < d.Buttons; b++) d.Button[b] = Sdl.SDL_JoystickGetButton(s.Handle, b) != 0;
        // (SDL's hat bits: 1 up, 2 right, 4 down, 8 left, as ours)
        for (int h = 0; h < d.Hats; h++) d.Hat[h] = Sdl.SDL_JoystickGetHat(s.Handle, h) & 0x0F;
    }

    public void Dispose()
    {
        if (!started) return;
        foreach (var s in sticks) Sdl.SDL_JoystickClose(s.Handle);
        sticks.Clear();
        Sdl.SDL_QuitSubSystem(Sdl.SDL_INIT_JOYSTICK);
        started = false;
    }
}

/// <summary>The few SDL2 joystick calls used, from the DLL the game ships (from FotG Controls).</summary>
internal static class Sdl
{
    private const string Lib = "SDL2";
    public const uint SDL_INIT_JOYSTICK = 0x00000200;

    private static IntPtr handle;

    public static bool IsLoaded => handle != IntPtr.Zero;
    public static string? LoadedFrom { get; private set; }

    /// <summary>Loads SDL2.dll from a path; only the first that loads stays.</summary>
    public static bool TryLoad(string path, out string error)
    {
        error = "";
        if (IsLoaded) return true;
        try
        {
            handle = NativeLibrary.Load(path);
            LoadedFrom = path;
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, _, _) => name == Lib ? handle : IntPtr.Zero);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SdlGuid { public ulong A, B; }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetError();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PumpEvents();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FlushEvents(uint minType, uint maxType);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickOpen(int deviceIndex);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_JoystickClose(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickName(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern SdlGuid SDL_JoystickGetGUID(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_JoystickGetGUIDString(SdlGuid guid, [Out] byte[] buffer, int size);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumAxes(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumButtons(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumHats(IntPtr joystick);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_JoystickGetAxis(IntPtr joystick, int axis);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetButton(IntPtr joystick, int button);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetHat(IntPtr joystick, int hat);

    public static string Error => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "";
}
