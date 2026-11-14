using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Settings;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Shell;

/// <summary>
/// Back and forward through the tabs and sub-tabs as they were opened, as in a browser: the mouse's back and forward
/// buttons over the window, and two shortcuts (Core's Hotkeys, unbound to start with) in Settings → Shortcuts. A place
/// is a tab and, on a tab of sub-tabs (an <see cref="AppletGroup"/>), its sub-tab; opening another place while back
/// drops the ones ahead of it.
/// </summary>
internal sealed class TabHistory
{
    private const int Most = 50;

    private readonly TabStrip tabs;
    private readonly IReadOnlyList<IApplet> applets;
    private readonly Func<string?> currentTab;
    private readonly List<(string Tab, string? Tool)> places = new();
    private int at = -1;
    private bool going, queued;
    private DateTime mouseAt;

    public TabHistory(Window window, TabStrip tabs, IReadOnlyList<IApplet> applets, AppSettings settings, Func<string?> currentTab)
    {
        this.tabs = tabs;
        this.applets = applets;
        this.currentTab = currentTab;
        tabs.Selected += _ => Seen();
        foreach (var g in applets.OfType<AppletGroup>())
            if (g.View is SubTabs sub) sub.Selected += _ => Seen();

        window.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
            mouseAt = DateTime.UtcNow;
            e.Handled = true;
            Go(e.ChangedButton == MouseButton.XButton1 ? -1 : 1, "mouse");
        };
        // (a keyboard's Browser Back / Forward keys, and the mouse's buttons over a control with a window of its own, as
        // Terminal's: WM_APPCOMMAND, which reaches the main window; not twice for one click)
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AppCommand);

        Hotkeys.Add(new HotkeySpec
        {
            Id = "App.Back", Module = AppPaths.DisplayName, Name = T("Back (the tab before)"),
            Get = () => settings.BackKey, Set = v => { settings.BackKey = v; settings.Save(); },
        }, () => Go(-1, "shortcut"));
        Hotkeys.Add(new HotkeySpec
        {
            Id = "App.Forward", Module = AppPaths.DisplayName, Name = T("Forward (the tab after)"),
            Get = () => settings.ForwardKey, Set = v => { settings.ForwardKey = v; settings.Save(); },
        }, () => Go(1, "shortcut"));
    }

    private IntPtr AppCommand(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_APPCOMMAND = 0x0319, BrowserBack = 1, BrowserForward = 2;
        if (msg != WM_APPCOMMAND) return IntPtr.Zero;
        int command = (int)((lParam.ToInt64() >> 16) & 0x0FFF);
        if (command is not (BrowserBack or BrowserForward)) return IntPtr.Zero;
        handled = true;
        if ((DateTime.UtcNow - mouseAt).TotalMilliseconds >= 500) Go(command == BrowserBack ? -1 : 1, "key");
        return new IntPtr(1);
    }

    /// <summary>A tab or sub-tab opened: noted once things settle (a tab then its sub-tab, as AppNavigation opens them, is one place).</summary>
    private void Seen()
    {
        if (going || queued) return;
        queued = true;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            queued = false;
            if (going || Here() is not { } here) return;
            if (at >= 0 && places[at] == here) return;
            places.RemoveRange(at + 1, places.Count - at - 1);
            places.Add(here);
            if (places.Count > Most) places.RemoveAt(0);
            at = places.Count - 1;
        }, DispatcherPriority.Background);
    }

    private (string Tab, string? Tool)? Here()
    {
        if (currentTab() is not { } tab) return null;
        var group = Group(tab);
        return (tab, group?.View is SubTabs sub ? sub.Current : null);
    }

    private AppletGroup? Group(string tab) =>
        applets.OfType<AppletGroup>().FirstOrDefault(g => ((IApplet)g).Meta.Id.Equals(tab, StringComparison.OrdinalIgnoreCase));

    private void Go(int step, string how)
    {
        int to = at + step;
        if (to < 0 || to >= places.Count) return;
        at = to;
        var (tab, tool) = places[to];
        going = true;
        try
        {
            tabs.Select(tab);
            if (tool != null) Group(tab)?.Select(tool);
        }
        finally { going = false; }
        Log.App.Debug($"{(step < 0 ? "Back" : "Forward")} ({how}): {tab}{(tool != null ? "/" + tool : "")}");
    }
}
