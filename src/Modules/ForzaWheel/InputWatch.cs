using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using DaisysApp.Shared.Win32;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// Log Other Input (4.3.2): while the emulator runs, everything Forza could take as input from something other than
/// the vJoy device (and media keys) goes to the ForzaWheelInput log, so a force-feedback dropout can be matched to what came just before
/// it. Keys (which keyboard, or sent by a program), mouse buttons and movement, buttons, hats and axes of the HID
/// controllers that aren't vJoy or bound (bound ones are hushed), Xbox pads (XInput), devices coming and going, the
/// window in front, and the game's own force-feedback commands (<see cref="Ffb"/>, from <see cref="ForceFeedback"/>).
/// Raw input (passive: nothing is hooked or polled but XInput) on a message-only window on a thread of its own.
/// </summary>
internal sealed class InputWatch : IDisposable
{
    private static readonly Logging.Logger log = Logging.Log.For("ForzaWheelInput");
    private static volatile InputWatch? active;

    /// <summary>A line about the game's force feedback, if the watch runs.</summary>
    public static void Ffb(string text) { if (active != null) log.Info("FFB: " + text); }

    private readonly Func<(HashSet<(int Vid, int Pid)> Bound, string Names)> bound;
    private readonly Thread thread;
    private Dispatcher? dispatcher;
    private HwndSource? window;
    private DispatcherTimer? timer;
    private IntPtr winEvent;
    private WinEventProc? winEventProc;
    private HashSet<(int, int)> boundIds = new();
    private volatile bool stop;
    private long started;
    /// <summary>The controllers' names as DirectInput lists them, by VID and PID (raw input names few of them).</summary>
    private Dictionary<(int, int), string> controllerNames = new();

    /// <param name="bound">The bound controllers' VID and PID (hushed, so left out) and their names, for the first line.</param>
    public InputWatch(Func<(HashSet<(int Vid, int Pid)>, string)> bound)
    {
        this.bound = bound;
        thread = new Thread(Run) { IsBackground = true, Name = "ForzaWheel input log" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void Run()
    {
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var (ids, names) = bound();
            boundIds = ids;
            controllerNames = GameControllers.List().GroupBy(c => (c.Vid, c.Pid)).ToDictionary(g => g.Key, g => g.First().Name);
            log.Info($"Watching other input (left out: vJoy and the bound devices, hushed: {names})");
            window = new HwndSource(new HwndSourceParameters("DaisysApp ForzaWheel input log") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            window.AddHook(WndProc);
            started = Environment.TickCount64;
            var devices = Watched
                .Select(d => new RAWINPUTDEVICE { usUsagePage = d.Page, usUsage = d.Usage, dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwndTarget = window.Handle }).ToArray();
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                log.Warn($"Raw input couldn't be registered (error {Marshal.GetLastWin32Error()})");
            winEventProc = OnForeground;
            winEvent = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, 0);
            OnForeground(IntPtr.Zero, 0, Native.GetForegroundWindow(), 0, 0, 0, 0);
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Tick(), dispatcher);
            active = this;
            if (!stop) Dispatcher.Run();
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ForzaWheel input log", ex); }
        finally
        {
            active = null;
            timer?.Stop();
            if (winEvent != IntPtr.Zero) UnhookWinEvent(winEvent);
            if (window != null)
            {
                var off = Watched
                    .Select(d => new RAWINPUTDEVICE { usUsagePage = d.Page, usUsage = d.Usage, dwFlags = RIDEV_REMOVE }).ToArray();
                RegisterRawInputDevices(off, (uint)off.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                window.Dispose();
            }
            Marshal.FreeHGlobal(buffer);
            log.Info("Stopped watching other input");
        }
    }

    /// <summary>What raw input is asked for: mouse, joystick, gamepad, keyboard, multi-axis, and media keys (consumer
    /// control: Play/Pause, volume… from keyboards, headsets, a Stream Deck).</summary>
    private static readonly (ushort Page, ushort Usage)[] Watched = { (1, 2), (1, 4), (1, 5), (1, 6), (1, 8), (0x0C, 1) };

    public void Dispose()
    {
        stop = true;
        try { dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal); } catch { }
        thread.Join(1000);
    }

    // ---------------------------------------------------------------- devices

    private sealed class Device
    {
        public string Name = "", Path = "";
        public uint Type; // 0 mouse, 1 keyboard, 2 HID
        public bool Skip; // vJoy or bound
        public Hid? Hid;
        // mouse movement: a burst is a move after a second without
        public long MoveStart, MoveLast;
        public int MoveReports, MoveDistance;
        // HID reports that changed, and those that changed nothing logged (noise), each 10 s
        public int Changed, Unlogged;
        public double Largest;
        public string LargestAxis = "";
    }

    private readonly Dictionary<IntPtr, Device> devices = new();
    private readonly HashSet<ushort> held = new();

    private static string TypeName(uint type) => type switch { 0 => "mouse", 1 => "keyboard", _ => "HID" };

    private Device? DeviceOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        if (devices.TryGetValue(handle, out var known)) return known;
        var path = InfoString(handle, RIDI_DEVICENAME);
        if (path == null) return null;
        var info = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(info, 32);
        uint infoSize = 32;
        uint type = GetRawInputDeviceInfoW(handle, RIDI_DEVICEINFO, info, ref infoSize) != uint.MaxValue ? BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(4)) : 2;
        var (vid, pid) = VidPid(path);
        var dev = new Device { Path = path, Type = type, Name = (ProductName(path) ?? controllerNames.GetValueOrDefault((vid, pid)) ?? "unnamed") + (vid >= 0 ? $" ({vid:X4}:{pid:X4})" : "") };
        if (type == 2)
        {
            dev.Skip = (vid == 0x1234 && pid == 0xBEAD) || dev.Name.Contains("vJoy", StringComparison.OrdinalIgnoreCase) || boundIds.Contains((vid, pid));
            if (!dev.Skip) dev.Hid = Hid.Open(handle);
        }
        devices[handle] = dev;
        return dev;
    }

    private static (int, int) VidPid(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(path, @"VID_([0-9A-F]{4}).*?PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? (Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16)) : (-1, -1);
    }

    private static string? ProductName(string path)
    {
        using var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var buf = new char[128];
        return HidD_GetProductString(h, buf, buf.Length * 2) && new string(buf).TrimEnd('\0').Trim() is { Length: > 0 } s ? s : null;
    }

    private static string? InfoString(IntPtr handle, uint what)
    {
        uint size = 0;
        GetRawInputDeviceInfoW(handle, what, null, ref size);
        if (size == 0) return null;
        var buf = new byte[size * 2];
        return GetRawInputDeviceInfoW(handle, what, buf, ref size) == uint.MaxValue ? null : System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    // ---------------------------------------------------------------- raw input

    private IntPtr buffer = IntPtr.Zero;
    private int bufferSize;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == WM_INPUT) OnInput(lParam);
            else if (msg == WM_INPUT_DEVICE_CHANGE) OnDeviceChange((int)wParam, lParam);
        }
        catch (Exception ex) { log.Debug(() => "Input: " + ex.Message); }
        return IntPtr.Zero;
    }

    private void OnDeviceChange(int what, IntPtr handle)
    {
        if (what == 1)
        {
            devices.Remove(handle);
            // a bound controller plugged back in is left out again
            boundIds = bound().Bound;
            var dev = DeviceOf(handle);
            // (registering tells of each device there already: those in the first seconds are the ones there)
            if (dev != null) log.Info($"{(Environment.TickCount64 - started < 3000 ? "Device" : "Device arrived")} ({TypeName(dev.Type)}): {dev.Name}{(dev.Skip ? " (left out)" : "")}");
        }
        else if (devices.Remove(handle, out var gone)) log.Info($"Device gone ({TypeName(gone.Type)}): {gone.Name}");
        else log.Info("Device gone (one not seen before)");
    }

    private void OnInput(IntPtr handle)
    {
        int header = 8 + 2 * IntPtr.Size;
        uint size = 0;
        GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, (uint)header);
        if (size == 0) return;
        if (size > bufferSize)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = Marshal.AllocHGlobal((int)size);
            bufferSize = (int)size;
        }
        if (GetRawInputData(handle, RID_INPUT, buffer, ref size, (uint)header) == uint.MaxValue) return;
        uint type = (uint)Marshal.ReadInt32(buffer, 0);
        IntPtr device = Marshal.ReadIntPtr(buffer, 8);
        var dev = DeviceOf(device);
        if (dev?.Skip == true) return;
        string from = dev == null ? "sent by a program" : dev.Name;
        IntPtr data = buffer + header;
        switch (type)
        {
            case 1: // keyboard
                ushort flags = (ushort)Marshal.ReadInt16(data, 2), vkey = (ushort)Marshal.ReadInt16(data, 6);
                bool up = (flags & 1) != 0;
                if (up) { held.Remove(vkey); break; }
                if (!held.Add(vkey)) break; // held down, repeating
                log.Info($"Key {KeyName(vkey)} (from {from})");
                break;
            case 0: // mouse
                ushort buttons = (ushort)Marshal.ReadInt16(data, 4);
                int dx = Marshal.ReadInt32(data, 12), dy = Marshal.ReadInt32(data, 16);
                if (buttons != 0) log.Info($"Mouse {MouseButtons(buttons, (short)Marshal.ReadInt16(data, 6))} (from {from})");
                bool absolute = (Marshal.ReadInt16(data, 0) & 1) != 0; // a tablet, remote desktop
                if (dx != 0 || dy != 0) Moved(dev, from, absolute ? 1 : Math.Abs(dx) + Math.Abs(dy));
                break;
            case 2: // HID
                if (dev?.Hid is not { } hid) break;
                int each = Marshal.ReadInt32(data, 0), count = Marshal.ReadInt32(data, 4);
                for (int i = 0; i < count; i++)
                {
                    var report = new byte[each];
                    Marshal.Copy(data + 8 + i * each, report, 0, each);
                    Report(dev, hid, report);
                }
                break;
        }
    }

    private readonly Device sent = new() { Name = "sent by a program" };

    private void Moved(Device? dev, string from, int distance)
    {
        dev ??= sent;
        long now = Environment.TickCount64;
        if (now - dev.MoveLast > 1000)
        {
            dev.MoveStart = now;
            dev.MoveReports = dev.MoveDistance = 0;
            log.Info($"Mouse moves (from {from})");
        }
        dev.MoveLast = now;
        dev.MoveReports++;
        dev.MoveDistance += distance;
    }

    private static string MouseButtons(ushort f, short wheel)
    {
        var parts = new List<string>();
        if ((f & 0x001) != 0) parts.Add("left down");
        if ((f & 0x004) != 0) parts.Add("right down");
        if ((f & 0x010) != 0) parts.Add("middle down");
        if ((f & 0x040) != 0) parts.Add("back down");
        if ((f & 0x100) != 0) parts.Add("forward down");
        if ((f & 0x400) != 0) parts.Add($"wheel {wheel}");
        if ((f & 0x800) != 0) parts.Add($"tilt {wheel}");
        if ((f & 0x0AA) != 0) parts.Add("up");
        return string.Join(", ", parts);
    }

    private static string KeyName(ushort vk)
    {
        var def = KeyCatalog.FromVk(vk);
        return def != null ? def.Id : $"0x{vk:X2}";
    }

    // ---------------------------------------------------------------- HID controllers

    private void Report(Device dev, Hid hid, byte[] report)
    {
        var lines = hid.Changes(report, out bool changed, out double largest, out string axis);
        if (changed) dev.Changed++;
        foreach (var line in lines) log.Info($"{dev.Name}: {line}");
        if (changed && lines.Count == 0)
        {
            dev.Unlogged++;
            if (largest > dev.Largest) { dev.Largest = largest; dev.LargestAxis = axis; }
        }
    }

    // ---------------------------------------------------------------- every 50 ms: XInput, summaries

    private long nextSummary = Environment.TickCount64 + 10_000;
    private readonly XInputPad[] pads = Enumerable.Range(0, 4).Select(i => new XInputPad(i)).ToArray();

    private void Tick()
    {
        foreach (var p in pads)
            foreach (var line in p.Poll())
                log.Info(line);
        long now = Environment.TickCount64;
        foreach (var dev in devices.Values.Append(sent))
            if (dev.MoveReports > 0 && now - dev.MoveLast > 1000)
            {
                log.Info($"Mouse stopped ({dev.Name}): {dev.MoveDistance} counts in {dev.MoveReports} reports over {(dev.MoveLast - dev.MoveStart) / 1000.0:0.0} s");
                dev.MoveReports = 0;
            }
        if (now < nextSummary) return;
        nextSummary = now + 10_000;
        foreach (var dev in devices.Values.Where(d => d.Unlogged > 0))
        {
            log.Info($"{dev.Name}: {dev.Unlogged} small changes in 10 s (largest {dev.Largest:0.0}% on {dev.LargestAxis})");
            dev.Unlogged = 0;
            dev.Largest = 0;
        }
    }

    // ---------------------------------------------------------------- the window in front

    private void OnForeground(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        try
        {
            int pid = Native.ProcessOf(hwnd);
            string name = "";
            try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch { }
            var title = new char[256];
            int n = GetWindowTextW(hwnd, title, title.Length);
            log.Info($"In front: {name} \"{new string(title, 0, Math.Max(0, n))}\"");
        }
        catch { }
    }

    // ---------------------------------------------------------------- a HID controller's report, read by HidP

    private sealed class Hid
    {
        private readonly byte[] preparsed;
        private readonly int maxData;
        private readonly Dictionary<ushort, (string Name, int Min, int Max, bool Hat)> values = new();
        private readonly Dictionary<ushort, string> buttons = new();
        private readonly bool reportIds;
        private readonly Dictionary<byte, HashSet<ushort>> on = new();
        private readonly Dictionary<ushort, int> last = new(), logged = new();
        private readonly Dictionary<ushort, long> loggedAt = new();
        private byte[]? lastReport;

        private Hid(byte[] preparsed)
        {
            this.preparsed = preparsed;
            maxData = (int)HidP_MaxDataListLength(0, preparsed);
            var caps = new byte[64];
            if (HidP_GetCaps(preparsed, caps) != HIDP_STATUS_SUCCESS) return;
            ushort nButtons = BinaryPrimitives.ReadUInt16LittleEndian(caps.AsSpan(46)), nValues = BinaryPrimitives.ReadUInt16LittleEndian(caps.AsSpan(48));
            var b = new byte[72 * Math.Max(1, (int)nButtons)];
            if (nButtons > 0 && HidP_GetButtonCaps(0, b, ref nButtons, preparsed) == HIDP_STATUS_SUCCESS)
                for (int i = 0; i < nButtons; i++)
                {
                    var c = b.AsSpan(i * 72);
                    if (c[2] != 0) reportIds = true;
                    ushort page = U16(c, 0), uMin = U16(c, 56), dMin = U16(c, 68), dMax = c[12] != 0 ? U16(c, 70) : dMin;
                    for (ushort d = dMin; d <= dMax && d >= dMin; d++)
                        buttons[d] = page == 9 ? $"button {uMin + d - dMin}" : page == 0x0C ? MediaName(uMin + d - dMin) : $"button 0x{page:X2}/0x{uMin + d - dMin:X2}";
                }
            var v = new byte[72 * Math.Max(1, (int)nValues)];
            if (nValues > 0 && HidP_GetValueCaps(0, v, ref nValues, preparsed) == HIDP_STATUS_SUCCESS)
                for (int i = 0; i < nValues; i++)
                {
                    var c = v.AsSpan(i * 72);
                    if (c[2] != 0) reportIds = true;
                    ushort page = U16(c, 0), uMin = U16(c, 56), dMin = U16(c, 68), dMax = c[12] != 0 ? U16(c, 70) : dMin;
                    if (page is not (1 or 2)) continue; // vendor-defined ones (lights, status) aren't input a game reads
                    int min = BinaryPrimitives.ReadInt32LittleEndian(c[40..]), max = BinaryPrimitives.ReadInt32LittleEndian(c[44..]);
                    int bits = U16(c, 18);
                    if (max <= min && bits is > 0 and < 32) { min = 0; max = (1 << bits) - 1; } // an unsigned maximum read as negative
                    for (ushort d = dMin; d <= dMax && d >= dMin; d++)
                    {
                        int usage = uMin + d - dMin;
                        values[d] = (page == 1 ? AxisName(usage) : $"simulation 0x{usage:X2}", min, max, page == 1 && usage == 0x39);
                    }
                }
        }

        private static string MediaName(int usage) => "media key " + (usage switch
        {
            0xB0 => "Play", 0xB1 => "Pause", 0xB5 => "Next", 0xB6 => "Previous", 0xB7 => "Stop", 0xCD => "Play/Pause",
            0xE2 => "Mute", 0xE9 => "Volume up", 0xEA => "Volume down", _ => $"0x{usage:X2}",
        });

        private static ushort U16(Span<byte> s, int at) => BinaryPrimitives.ReadUInt16LittleEndian(s[at..]);

        private static string AxisName(int usage) => usage switch
        {
            0x30 => "X", 0x31 => "Y", 0x32 => "Z", 0x33 => "Rx", 0x34 => "Ry", 0x35 => "Rz",
            0x36 => "Slider", 0x37 => "Dial", 0x38 => "Wheel", 0x39 => "Hat", _ => $"axis 0x{usage:X2}",
        };

        public static Hid? Open(IntPtr device)
        {
            uint size = 0;
            GetRawInputDeviceInfoW(device, RIDI_PREPARSEDDATA, null, ref size);
            if (size == 0) return null;
            var data = new byte[size];
            return GetRawInputDeviceInfoW(device, RIDI_PREPARSEDDATA, data, ref size) == uint.MaxValue ? null : new Hid(data);
        }

        /// <summary>What a report changed worth a line: buttons pressed or let go, a hat, an axis moved 3% or more
        /// (a line a second at most for each). Whether it changed any button, hat or axis (vendor-defined values aren't
        /// counted), and the largest move (%) not logged, on which axis.</summary>
        public List<string> Changes(byte[] report, out bool changed, out double largest, out string axis)
        {
            var lines = new List<string>();
            changed = false;
            largest = 0;
            axis = "";
            bool same = lastReport != null && report.AsSpan().SequenceEqual(lastReport);
            lastReport = report;
            if (same || maxData <= 0) return lines;
            var list = new HIDP_DATA[maxData];
            uint n = (uint)maxData;
            if (HidP_GetData(0, list, ref n, preparsed, report, (uint)report.Length) != HIDP_STATUS_SUCCESS) return lines;
            byte id = reportIds ? report[0] : (byte)0;
            // the first report is where it rests: switches left on, a hat, axes
            bool baseline = !on.TryGetValue(id, out var was);
            was ??= new HashSet<ushort>();
            var now = new HashSet<ushort>();
            long tick = Environment.TickCount64;
            for (int i = 0; i < n; i++)
            {
                var d = list[i];
                if (values.TryGetValue(d.DataIndex, out var val))
                {
                    int x = (int)d.RawValue;
                    if (last.TryGetValue(d.DataIndex, out int before) && before == x) continue;
                    last[d.DataIndex] = x;
                    if (baseline) { logged[d.DataIndex] = x; continue; }
                    changed = true;
                    if (val.Hat) { lines.Add($"{val.Name} {(x < val.Min || x > val.Max ? "centred" : x.ToString())}"); continue; }
                    double range = Math.Max(1, (double)val.Max - val.Min);
                    int from = logged.TryGetValue(d.DataIndex, out int f) ? f : x;
                    double moved = Math.Abs(x - from) / range * 100;
                    if (!logged.ContainsKey(d.DataIndex)) { logged[d.DataIndex] = x; continue; } // the first report: where it rests
                    if (moved >= 3 && tick - loggedAt.GetValueOrDefault(d.DataIndex) >= 1000)
                    {
                        lines.Add($"{val.Name} {(from - val.Min) / range * 100:0}% → {(x - val.Min) / range * 100:0}%");
                        logged[d.DataIndex] = x;
                        loggedAt[d.DataIndex] = tick;
                    }
                    else if (moved > largest) { largest = moved; axis = val.Name; }
                }
                else if (buttons.ContainsKey(d.DataIndex) && d.On != 0) now.Add(d.DataIndex);
            }
            if (baseline) { on[id] = now; lines.Clear(); return lines; }
            if (!now.SetEquals(was)) changed = true;
            foreach (var b in now.Except(was)) lines.Add($"{buttons[b]} pressed");
            foreach (var b in was.Except(now)) lines.Add($"{buttons[b]} let go");
            on[id] = now;
            return lines;
        }
    }

    // ---------------------------------------------------------------- Xbox pads

    private sealed class XInputPad
    {
        private readonly int index;
        private bool connected, first = true;
        private uint packet;
        private XINPUT_GAMEPAD was;
        private long axesAt;

        public XInputPad(int index) => this.index = index;

        public List<string> Poll()
        {
            var lines = new List<string>();
            bool now;
            XINPUT_STATE s = default;
            try { now = XInputGetState((uint)index, out s) == 0; }
            catch { return lines; } // no XInput
            if (now != connected)
            {
                if (!first || now) lines.Add($"Xbox pad {index + 1} {(now ? "connected" : "gone")}");
                connected = now;
                if (now) { packet = s.dwPacketNumber; was = s.Gamepad; }
            }
            first = false;
            if (!now || s.dwPacketNumber == packet) return lines;
            packet = s.dwPacketNumber;
            var g = s.Gamepad;
            ushort pressed = (ushort)(g.wButtons & ~was.wButtons), released = (ushort)(was.wButtons & ~g.wButtons);
            if (pressed != 0) lines.Add($"Xbox pad {index + 1}: {PadButtons(pressed)} pressed");
            if (released != 0) lines.Add($"Xbox pad {index + 1}: {PadButtons(released)} let go");
            var moved = new List<string>();
            long tick = Environment.TickCount64;
            void Axis(string name, int a, int b, int range) { if (Math.Abs(a - b) * 100 / range >= 8) moved.Add($"{name} {b}→{a}"); }
            Axis("LT", g.bLeftTrigger, was.bLeftTrigger, 255);
            Axis("RT", g.bRightTrigger, was.bRightTrigger, 255);
            Axis("LX", g.sThumbLX, was.sThumbLX, 65535);
            Axis("LY", g.sThumbLY, was.sThumbLY, 65535);
            Axis("RX", g.sThumbRX, was.sThumbRX, 65535);
            Axis("RY", g.sThumbRY, was.sThumbRY, 65535);
            if (moved.Count > 0 && tick - axesAt >= 1000)
            {
                lines.Add($"Xbox pad {index + 1}: {string.Join(", ", moved)}");
                axesAt = tick;
                was = g;
            }
            else was.wButtons = g.wButtons; // small moves count from where it was last logged; buttons always follow
            return lines;
        }

        private static string PadButtons(ushort b)
        {
            string[] names = { "Up", "Down", "Left", "Right", "Start", "Back", "LS", "RS", "LB", "RB", "", "", "A", "B", "X", "Y" };
            return string.Join("+", Enumerable.Range(0, 16).Where(i => (b & (1 << i)) != 0 && names[i].Length > 0).Select(i => names[i]));
        }
    }

    // ---------------------------------------------------------------- Windows

    private const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000, RID_INPUT = 0x10000003;
    private const uint RIDI_PREPARSEDDATA = 0x20000005, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b;
    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
    [StructLayout(LayoutKind.Explicit, Size = 8)] private struct HIDP_DATA { [FieldOffset(0)] public ushort DataIndex; [FieldOffset(4)] public uint RawValue; [FieldOffset(4)] public byte On; }
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, byte[]? data, ref uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputDeviceList(RAWINPUTDEVICELIST[]? list, ref uint count, uint size);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, char[] text, int max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(Microsoft.Win32.SafeHandles.SafeFileHandle device, char[] buffer, int length);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(byte[] preparsed, byte[] caps);
    [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort length, byte[] preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort length, byte[] preparsed);
    [DllImport("hid.dll")] private static extern uint HidP_MaxDataListLength(int type, byte[] preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetData(int type, [In, Out] HIDP_DATA[] list, ref uint length, byte[] preparsed, byte[] report, uint reportLength);
    [DllImport("xinput1_4.dll")] private static extern int XInputGetState(uint index, out XINPUT_STATE state);
}
