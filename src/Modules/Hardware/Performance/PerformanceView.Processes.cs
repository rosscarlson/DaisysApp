using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>
/// The process table's right-click menu, as Task Manager's: switch to it, end the task (or its whole tree), set its
/// priority, open its file's location, its file's properties, search the web for it, copy its name, PID or path, and its
/// history. Ending one this app can't (another user's, or an elevated one) asks Windows for permission and uses taskkill.
/// </summary>
public partial class PerformanceView
{
    /// <summary>Windows itself: ending one of these can sign you out or stop the PC, so it's asked about first.</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "Registry", "Memory Compression", "explorer" };

    private void InitProcessMenu()
    {
        ProcessGrid.ContextMenu = new ContextMenu();
        // the row under the mouse is the one the menu is for
        ProcessGrid.PreviewMouseRightButtonDown += (_, e) =>
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not DataGridRow) hit = VisualTreeHelper.GetParent(hit);
            if (hit is DataGridRow { Item: ProcessRow row }) ProcessGrid.SelectedItem = row;
        };
        ProcessGrid.ContextMenuOpening += (_, e) =>
        {
            if (ProcessGrid.SelectedItem is not ProcessRow row) { e.Handled = true; return; }
            FillProcessMenu(ProcessGrid.ContextMenu, row);
        };
        ProcessGrid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && ProcessGrid.SelectedItem is ProcessRow row) { e.Handled = true; EndTask(row, tree: false); }
        };
    }

    private void FillProcessMenu(ContextMenu menu, ProcessRow row)
    {
        menu.Items.Clear();
        string? path = ImagePath(row.Pid);
        IntPtr window = MainWindowOf(row.Pid);
        MenuItem Item(string text, Action click, bool enabled = true, string? key = null)
        {
            var m = new MenuItem { Header = text, IsEnabled = enabled, InputGestureText = key ?? "" };
            m.Click += (_, _) => { try { click(); } catch (Exception ex) { Problem(row, ex.Message); } };
            menu.Items.Add(m);
            return m;
        }
        Item(T("Switch to"), () => SwitchTo(window), window != IntPtr.Zero);
        Item(T("End task"), () => EndTask(row, tree: false), key: "Del");
        Item(T("End process tree"), () => EndTask(row, tree: true));
        menu.Items.Add(new Separator());

        var priority = new MenuItem { Header = T("Set priority") };
        int current = PriorityOf(row.Pid);
        foreach (var (cls, name) in new[]
        {
            (ProcessPriorityClass.RealTime, T("Realtime")), (ProcessPriorityClass.High, T("High")), (ProcessPriorityClass.AboveNormal, T("Above normal")),
            (ProcessPriorityClass.Normal, T("Normal")), (ProcessPriorityClass.BelowNormal, T("Below normal")), (ProcessPriorityClass.Idle, T("Low")),
        })
        {
            var m = new MenuItem { Header = name, IsCheckable = true, IsChecked = current == (int)cls };
            m.Click += (_, _) => SetPriority(row, cls);
            priority.Items.Add(m);
        }
        priority.IsEnabled = current != 0;
        menu.Items.Add(priority);
        menu.Items.Add(new Separator());

        Item(T("Open file location"), () => Process.Start("explorer.exe", $"/select,\"{path}\""), path != null);
        Item(T("Properties"), () => ShowProperties(path!), path != null);
        Item(T("Search online"), () => Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + Uri.EscapeDataString(row.Name + (row.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "" : ".exe"))) { UseShellExecute = true }));
        menu.Items.Add(new Separator());
        Item(T("Copy name"), () => Clipboard.SetText(row.Name));
        Item(T("Copy PID"), () => Clipboard.SetText(row.Pid.ToString()));
        Item(T("Copy path"), () => Clipboard.SetText(path!), path != null);
        menu.Items.Add(new Separator());
        Item(T("History…"), () => new HistoryWindow(monitor, log, limits, settings, row.Pid, row.Name) { Owner = Window.GetWindow(this) }.Show());
    }

    // ---------------------------------------------------------------- the actions

    private void EndTask(ProcessRow row, bool tree)
    {
        var owner = Window.GetWindow(this);
        string bare = Path.GetFileNameWithoutExtension(row.Name);
        if (row.Pid == Environment.ProcessId)
        {
            if (MessageBox.Show(owner, F("That's {0} itself. End it?", AppPaths.DisplayName), T("End task"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        }
        else if (Critical.Contains(bare) || row.Pid <= 4)
        {
            if (MessageBox.Show(owner, F("{0} is part of Windows. Ending it can make Windows unstable, sign you out or stop the PC. End it anyway?", row.Name), T("End task"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        }
        else if (tree && MessageBox.Show(owner, F("End {0} and every process it started?", row.Name), T("End process tree"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        try
        {
            using var p = Process.GetProcessById(row.Pid);
            p.Kill(entireProcessTree: tree);
            Logging.Log.Here.Info($"Ended {row.Name} ({row.Pid}){(tree ? " and its tree" : "")}");
        }
        catch (ArgumentException) { } // it had ended already
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            // another user's or an elevated process: taskkill, with Windows' permission
            if (MessageBox.Show(owner, F("{0} couldn't be ended ({1}). Try as administrator? Windows asks for permission.", row.Name, ex.Message), T("End task"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {row.Pid} /F{(tree ? " /T" : "")}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                Logging.Log.Here.Info($"Ended {row.Name} ({row.Pid}) as administrator");
            }
            catch (System.ComponentModel.Win32Exception) { } // said no
        }
    }

    private void SetPriority(ProcessRow row, ProcessPriorityClass cls)
    {
        if (cls == ProcessPriorityClass.RealTime && MessageBox.Show(Window.GetWindow(this), F("Realtime priority can take the PC from everything else, mouse and keyboard included. Set {0} to realtime?", row.Name),
                T("Set priority"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try
        {
            using var p = Process.GetProcessById(row.Pid);
            p.PriorityClass = cls;
            Logging.Log.Here.Info($"Priority of {row.Name} ({row.Pid}) set to {cls}");
        }
        catch (Exception ex) { Problem(row, ex.Message); }
    }

    private void Problem(ProcessRow row, string why) =>
        MessageBox.Show(Window.GetWindow(this), F("That didn't work for {0}: {1}", row.Name, why), T("Processes"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private static void SwitchTo(IntPtr window)
    {
        if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
        SetForegroundWindow(window);
    }

    private static void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), lpVerb = "properties", lpFile = path, nShow = 1, fMask = 0x0000000C }; // SEE_MASK_INVOKEIDLIST
        ShellExecuteEx(ref info);
    }

    // ---------------------------------------------------------------- reading a process (with the least access there is; EnumWindows and the like are in PerformanceView.xaml.cs)

    /// <summary>The program's full path, or null (System, or not allowed).</summary>
    private static string? ImagePath(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) && File.Exists(sb.ToString()) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Its priority class, or 0 if it can't be read.</summary>
    private static int PriorityOf(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return 0;
        try { return (int)GetPriorityClass(h); }
        finally { CloseHandle(h); }
    }

    /// <summary>Its main (visible, top-level, unowned) window, or zero.</summary>
    private static IntPtr MainWindowOf(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == pid && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public string? lpVerb; public string? lpFile; public string? lpParameters; public string? lpDirectory;
        public int nShow; public IntPtr hInstApp; public IntPtr lpIDList; public string? lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern uint GetPriorityClass(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
}
