using System.Runtime.InteropServices;
using System.Windows.Threading;
using DaisysApp.Logging;
using Microsoft.Win32;
using static DaisysApp.Shared.Win32.Native;

namespace DaisysApp.Applets.Tools.Keyboards;

/// <summary>
/// The remapping: a low-level keyboard hook on a thread of its own (so a busy UI thread never holds it up), in only
/// while there's a remap switched on or a key being captured. A key remapped is kept back and what it's made to send is
/// sent instead, held for as long as the key is (its repeats too). A key that comes with modifiers (the Copilot key:
/// Win + Shift + F23) matches when they're held; Win, when some remap starts with it, is kept back a moment (60 ms) so
/// a key that sends Win + … never shows Windows the Win (no Start menu, no Copilot); then it goes on as it came. A
/// modifier already sent is let go of first (after a key no one uses, so Win or Alt let go of alone opens nothing).
/// <see cref="Capture"/> keeps back every key until all are let go and says what they were. Sent keys carry
/// <see cref="Marker"/> and are passed by. Windows doesn't let it send keys to a program run as administrator.
/// </summary>
internal sealed class Remapper : IDisposable
{
    private static readonly IntPtr Marker = new(0x4B455953);
    private const int DummyVk = 0xE8; // (unassigned: AutoHotkey's too)
    private const uint HoldMs = 60;

    private readonly Logger log;
    private readonly HookProc keyProc;
    private Thread? thread;
    private uint threadId;
    private volatile Remap[] rules = [];
    private volatile Action<KeyCombo>? capture;
    private Dispatcher? captureTo;

    // the hook thread's own
    private readonly HashSet<int> down = new();
    private readonly List<KBDLLHOOKSTRUCT> held = new();
    private readonly HashSet<int> eaten = new();
    private readonly Dictionary<int, (KeyCombo To, Mods Pressed)> active = new();
    private readonly List<(int Vk, bool Ext, int Scan)> captured = new();
    private readonly HashSet<int> captureDown = new();
    private IntPtr timer;

    public Remapper(Logger log)
    {
        this.log = log;
        keyProc = KeyHook;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>The remaps switched on (none: the hook comes out, unless a key is being captured).</summary>
    public void Set(IEnumerable<Remap> on)
    {
        rules = on.Where(r => r.On && !r.From.Nothing).Select(r => new Remap { From = r.From.Copy(), To = r.To.Copy() }).ToArray();
        Post(WM_RESET);
        Refresh();
    }

    /// <summary>The next key pressed (with what's held with it), kept back from Windows; <paramref name="done"/> on
    /// this thread once every key is let go.</summary>
    public void Capture(Action<KeyCombo> done)
    {
        captureTo = Dispatcher.CurrentDispatcher;
        capture = done;
        Post(WM_RESET);
        Refresh();
    }

    public void CancelCapture()
    {
        capture = null;
        Refresh();
    }

    public bool Running => thread != null;

    private void Refresh()
    {
        if (rules.Length > 0 || capture != null) Start(); else Stop();
    }

    private void Start()
    {
        if (thread != null) return;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Keyboard remaps" };
        thread.Start();
        ready.Wait(2000);
    }

    private void Stop()
    {
        if (thread == null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(2000);
        thread = null;
    }

    private void Post(uint msg)
    {
        if (thread != null) PostThreadMessage(threadId, msg, IntPtr.Zero, IntPtr.Zero);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Post(WM_RESET); // (keys let go on the lock screen go unseen)

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        capture = null;
        rules = [];
        Stop();
    }

    private void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // the thread's message queue, before anyone posts to it
        var hook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) log.Warn($"Keyboard: hook not set (error {Marshal.GetLastWin32Error()})");
        Reset();
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER && msg.wParam == timer) Flush();
            else if (msg.message == WM_RESET) Reset();
            else
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        Reset();
    }

    /// <summary>Starts again: what's kept back goes, what's held is let go.</summary>
    private void Reset()
    {
        Flush();
        foreach (var a in active.Values) SendUp(a.To, a.Pressed);
        active.Clear();
        eaten.Clear();
        down.Clear();
        captured.Clear();
        captureDown.Clear();
    }

    private IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && Key((int)wParam, Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam))) return (IntPtr)1;
        }
        catch (Exception ex) { log.Warn("Keyboard: key", ex); }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True: kept back.</summary>
    private bool Key(int m, KBDLLHOOKSTRUCT k)
    {
        if (k.dwExtraInfo == Marker) return false;
        bool isDown = m is WM_KEYDOWN or WM_SYSKEYDOWN;
        int vk = (int)k.vkCode;
        if (capture != null) return CaptureKey(isDown, k);
        if ((k.flags & LLKHF_INJECTED) != 0) return false;

        bool repeat = isDown && down.Contains(vk);
        if (isDown) down.Add(vk); else down.Remove(vk);

        if (!isDown)
        {
            if (eaten.Remove(vk)) return true;
            if (active.Remove(vk, out var a))
            {
                SendUp(a.To, a.Pressed);
                return true;
            }
            if (held.Count == 0) return false;
            Flush();
            Send(k, true);
            return true;
        }

        if (repeat)
        {
            if (eaten.Contains(vk) || held.Any(h => h.vkCode == k.vkCode)) return true;
            if (active.TryGetValue(vk, out var a))
            {
                if (!a.To.Nothing) Send(a.To.Vk, a.To.Ext, a.To.Scan, false);
                return true;
            }
        }
        else if (Match(k) is { } rule)
        {
            Fire(vk, rule);
            return true;
        }
        else if (Defer(vk))
        {
            held.Add(k);
            if (timer != IntPtr.Zero) KillTimer(IntPtr.Zero, timer);
            timer = SetTimer(IntPtr.Zero, IntPtr.Zero, HoldMs, IntPtr.Zero);
            return true;
        }
        if (held.Count == 0) return false;
        Flush();
        Send(k, false);
        return true;
    }

    /// <summary>The modifiers held (by the keyboard, sent or not), other than this key.</summary>
    private Mods Held(int except)
    {
        var mods = Mods.None;
        foreach (var vk in down) if (vk != except) mods |= KeyNames.ModOf(vk);
        return mods;
    }

    /// <summary>The remap for this key with what's held: the one that needs the most modifiers held.</summary>
    private Remap? Match(KBDLLHOOKSTRUCT k)
    {
        int vk = (int)k.vkCode;
        bool ext = (k.flags & LLKHF_EXTENDED) != 0;
        var mods = Held(vk);
        Remap? best = null;
        foreach (var r in rules)
        {
            var f = r.From;
            if (f.Vk != vk || f.Ext != ext || (vk == KeyNames.NoVk && f.Scan != k.scanCode) || (f.Mods & ~mods) != 0) continue;
            if (best == null || Count(f.Mods) > Count(best.From.Mods)) best = r;
        }
        return best;
    }

    private static int Count(Mods m) => System.Numerics.BitOperations.PopCount((uint)m);

    /// <summary>Kept back: a Win that a remap starts with, then the other modifiers that remap needs.</summary>
    private bool Defer(int vk)
    {
        var mod = KeyNames.ModOf(vk);
        if (mod == Mods.None) return false;
        var mods = mod;
        foreach (var h in held) mods |= KeyNames.ModOf((int)h.vkCode);
        if (held.Count == 0 && mod != Mods.Win) return false;
        foreach (var r in rules) if (KeyNames.ModOf(r.From.Vk) == Mods.None && (mods & ~r.From.Mods) == 0) return true;
        return false;
    }

    /// <summary>The remapped key pressed: the modifiers it came with are kept from Windows (let go of, if they were
    /// sent already), the others kept back go on, and what it's made to send goes down.</summary>
    private void Fire(int vk, Remap rule)
    {
        var need = rule.From.Mods;
        var kept = held.ToList();
        held.Clear();
        StopTimer();
        foreach (var h in kept)
        {
            if ((KeyNames.ModOf((int)h.vkCode) & need) != 0) eaten.Add((int)h.vkCode);
            else Send(h, false);
        }
        var sent = down.Where(d => d != vk && (KeyNames.ModOf(d) & need) != 0 && !eaten.Contains(d)).ToList();
        if (sent.Count > 0)
        {
            if (sent.Any(d => KeyNames.ModOf(d) is Mods.Win or Mods.Alt)) Send(DummyVk, false, 0, false, DummyVk, false, 0, true);
            foreach (var d in sent)
            {
                Send(d, ExtOf(d), 0, true);
                eaten.Add(d);
            }
        }
        var pressed = Mods.None;
        if (!rule.To.Nothing)
        {
            var physical = Held(vk);
            foreach (var m in KeyNames.Each)
                if (rule.To.Mods.HasFlag(m) && (!physical.HasFlag(m) || down.Where(d => KeyNames.ModOf(d) == m).All(eaten.Contains)))
                {
                    Send(KeyNames.VkOf(m), ExtOf(KeyNames.VkOf(m)), 0, false);
                    pressed |= m;
                }
            Send(rule.To.Vk, rule.To.Ext, rule.To.Scan, false);
        }
        active[vk] = (rule.To, pressed);
    }

    private void SendUp(KeyCombo to, Mods pressed)
    {
        if (to.Nothing) return;
        Send(to.Vk, to.Ext, to.Scan, true);
        foreach (var m in KeyNames.Each.Reverse()) if (pressed.HasFlag(m)) Send(KeyNames.VkOf(m), ExtOf(KeyNames.VkOf(m)), 0, true);
    }

    /// <summary>What was kept back goes on, as it came.</summary>
    private void Flush()
    {
        StopTimer();
        if (held.Count == 0) return;
        var kept = held.ToList();
        held.Clear();
        foreach (var h in kept) Send(h, false);
    }

    private void StopTimer()
    {
        if (timer == IntPtr.Zero) return;
        KillTimer(IntPtr.Zero, timer);
        timer = IntPtr.Zero;
    }

    /// <summary>Capturing: every key kept back; once they're all let go, the last that isn't a modifier with the
    /// modifiers pressed before it (or the first modifier, alone).</summary>
    private bool CaptureKey(bool isDown, KBDLLHOOKSTRUCT k)
    {
        if ((k.flags & LLKHF_INJECTED) != 0) return false;
        int vk = (int)k.vkCode;
        if (isDown)
        {
            if (captureDown.Add(vk)) captured.Add((vk, (k.flags & LLKHF_EXTENDED) != 0, (int)k.scanCode));
            return true;
        }
        if (!captureDown.Remove(vk)) return false; // (down before it began)
        if (captureDown.Count > 0) return true;
        int main = captured.FindLastIndex(c => KeyNames.ModOf(c.Vk) == Mods.None);
        var combo = new KeyCombo();
        if (main < 0) main = 0;
        else for (int i = 0; i < main; i++) combo.Mods |= KeyNames.ModOf(captured[i].Vk);
        (combo.Vk, combo.Ext, combo.Scan) = captured[main];
        captured.Clear();
        var done = capture;
        capture = null;
        if (done != null) captureTo?.BeginInvoke(() => { done(combo); Refresh(); });
        return true;
    }

    private static bool ExtOf(int vk) => (MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX) & 0xFF00) == 0xE000;

    /// <summary>Where sent keys go: Windows (a test harness swaps it).</summary>
    private Action<INPUT[]> output = inputs => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    private void Send(KBDLLHOOKSTRUCT k, bool up) =>
        Send((int)k.vkCode, (k.flags & LLKHF_EXTENDED) != 0, (int)k.scanCode, up);

    private void Send(int vk, bool ext, int scan, bool up) => output([Input(vk, ext, scan, up)]);

    private void Send(int vk1, bool ext1, int scan1, bool up1, int vk2, bool ext2, int scan2, bool up2) =>
        output([Input(vk1, ext1, scan1, up1), Input(vk2, ext2, scan2, up2)]);

    /// <summary>A key as Windows' own: its scan code (from its code, if not known) and extended flag.</summary>
    private static INPUT Input(int vk, bool ext, int scan, bool up)
    {
        if (scan == 0 && vk != DummyVk)
        {
            uint sc = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
            scan = (int)(sc & 0xFF);
            ext |= (sc & 0xFF00) == 0xE000;
        }
        uint flags = (ext ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0) | (vk == KeyNames.NoVk ? KEYEVENTF_SCANCODE : 0);
        var input = INPUT.Key((ushort)(vk == KeyNames.NoVk ? 0 : vk), (ushort)scan, flags);
        input.U.ki.dwExtraInfo = Marker;
        return input;
    }


    // Windows' calls

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    private const uint WM_QUIT = 0x12, WM_TIMER = 0x113, WM_RESET = 0x8000 + 0x4B;
    private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
