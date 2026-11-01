using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// A device's page in Windows' Game Controllers (what its Properties button opens: the maker's own where it has one).
/// Windows has no way in but that window, so it's started unseen, the device picked in its list and Properties pressed;
/// when the page closes the window goes with it. Its list and button are found by their ids in joy.cpl's dialog, so it
/// works in every language. If the device can't be found there, the window is shown as it is.
/// </summary>
internal static class JoyProperties
{
    private const int ListId = 1020, PropertiesId = 1002;
    private const int WM_CLOSE = 0x10, BM_CLICK = 0xF5, GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, LWA_ALPHA = 2;
    private static IntPtr showing;

    /// <summary>Opens it (in the background: Windows takes a moment), or brings the one open to the front.
    /// <paramref name="occurrence"/>: which of the devices with this name, from 0.</summary>
    public static Task Open(string name, int occurrence) => Task.Run(() =>
    {
        try { Run(name, occurrence); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Properties of {name}: {ex.Message}"); }
    });

    private static void Run(string name, int occurrence)
    {
        if (showing != IntPtr.Zero && IsWindow(showing)) { SetForegroundWindow(showing); return; }
        // (rundll32 hosts it itself: "control joy.cpl" hands it to a process we couldn't follow)
        using var p = Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,Control_RunDLL joy.cpl") { UseShellExecute = false });
        if (p == null) return;
        AllowSetForegroundWindow(p.Id);
        var dialog = Wait(() => Windows(p.Id).FirstOrDefault(h => GetDlgItem(h, ListId) != IntPtr.Zero), 5000);
        if (dialog == IntPtr.Zero)
        {
            Logging.Log.Here.Warn("Game Controllers didn't open");
            return;
        }
        Visible(dialog, false);
        var button = GetDlgItem(dialog, PropertiesId);
        if (!Pick(GetDlgItem(dialog, ListId), name, occurrence) || Wait(() => IsWindowEnabled(button) ? button : IntPtr.Zero, 2000) == IntPtr.Zero)
        {
            Logging.Log.Here.Warn($"{name} isn't in Game Controllers' list");
            Visible(dialog, true);
            return;
        }
        PostMessageW(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        var sheet = Wait(() => Windows(p.Id).FirstOrDefault(h => h != dialog), 5000);
        if (sheet == IntPtr.Zero)
        {
            Logging.Log.Here.Warn($"The properties of {name} didn't open");
            Visible(dialog, true);
            return;
        }
        Logging.Log.Here.Info($"Properties of {name} opened");
        showing = sheet;
        SetForegroundWindow(sheet);
        while (IsWindow(sheet)) Thread.Sleep(300);
        showing = IntPtr.Zero;
        PostMessageW(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Selects the device in the list (through UI Automation: a list in another program can't be asked directly).</summary>
    private static bool Pick(IntPtr list, string name, int occurrence)
    {
        var items = AutomationElement.FromHandle(list).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().ToList();
        var same = items.Where(i => string.Equals(i.Current.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0) same = items.Where(i => i.Current.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0) return false;
        var item = same[Math.Min(occurrence, same.Count - 1)];
        if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)) return false;
        ((SelectionItemPattern)pattern).Select();
        return true;
    }

    private static void Visible(IntPtr window, bool on)
    {
        if (!on) SetWindowLongPtrW(window, GWL_EXSTYLE, new IntPtr(GetWindowLongPtrW(window, GWL_EXSTYLE).ToInt64() | WS_EX_LAYERED));
        SetLayeredWindowAttributes(window, 0, on ? (byte)255 : (byte)0, LWA_ALPHA);
        if (on) SetForegroundWindow(window);
    }

    private static IntPtr Wait(Func<IntPtr> found, int ms)
    {
        var took = Stopwatch.StartNew();
        while (took.ElapsedMilliseconds < ms)
        {
            var h = found();
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(30);
        }
        return IntPtr.Zero;
    }

    /// <summary>The process's visible top-level windows.</summary>
    private static List<IntPtr> Windows(int process)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint id);
            if (id == process && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int process);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, int flags);
}
