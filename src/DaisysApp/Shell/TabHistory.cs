using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Settings;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Shell;

/// <summary>
/// Back and forward through the tabs and sub-tabs as they were opened, as in a browser: the mouse's back and forward
/// buttons over the window, and two shortcuts (Core's Hotkeys, unbound to start with) in Settings → Shortcuts. A place
/// is every row of tabs showing, at every level: the "TabButton" chosen in each (a tab, its sub-tab, a sub-tab's own
/// sub-tabs, Audio's, Settings' pages, AI's sessions, Notepad's and Terminal's tabs), so any module's tabs count
/// without it doing anything. A place whose tab has since been closed is skipped. Opening another place while back
/// drops the ones ahead of it.
/// </summary>
internal sealed class TabHistory
{
    private const int Most = 50;

    private readonly Window window;
    private readonly List<RadioButton[]> places = new();
    private int at = -1;
    private bool going, queued;
    private DateTime mouseAt;
    private Style? tabStyle;

    public TabHistory(Window window, AppSettings settings)
    {
        this.window = window;
        // (any tab button checked anywhere, by a click, a module or AppNavigation)
        window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is RadioButton b && IsTab(b)) Seen();
        }), true);

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

    private bool IsTab(RadioButton b)
    {
        tabStyle ??= window.TryFindResource("TabButton") as Style;
        return b.Style != null && b.Style == tabStyle;
    }

    /// <summary>A tab opened: noted once things settle (a tab then its sub-tab, as AppNavigation opens them, is one place).</summary>
    private void Seen()
    {
        if (going || queued) return;
        queued = true;
        window.Dispatcher.BeginInvoke(() =>
        {
            queued = false;
            if (going) return;
            var here = Here();
            if (here.Length == 0 || (at >= 0 && places[at].SequenceEqual(here))) return;
            places.RemoveRange(at + 1, places.Count - at - 1);
            places.Add(here);
            if (places.Count > Most) places.RemoveAt(0);
            at = places.Count - 1;
        }, DispatcherPriority.Background);
    }

    /// <summary>The tab chosen in each row of tabs showing, outer ones first.</summary>
    private RadioButton[] Here()
    {
        var chosen = new List<RadioButton>();
        void Walk(DependencyObject o)
        {
            if (o is UIElement { Visibility: not Visibility.Visible }) return;
            if (o is RadioButton { IsChecked: true } b && IsTab(b)) chosen.Add(b);
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(o); i < n; i++) Walk(VisualTreeHelper.GetChild(o, i));
        }
        Walk(window);
        return chosen.ToArray();
    }

    private void Go(int step, string how)
    {
        var here = Here();
        int to = at + step;
        // (past places whose tab was closed, and ones that would change nothing)
        while (to >= 0 && to < places.Count && (places[to].Any(b => PresentationSource.FromVisual(b) == null) || places[to].SequenceEqual(here)))
            to += step;
        if (to < 0 || to >= places.Count) return;
        at = to;
        going = true;
        try
        {
            // outer first: each shows the row of the next
            foreach (var b in places[to]) b.IsChecked = true;
        }
        finally { going = false; }
        Log.App.Debug($"{(step < 0 ? "Back" : "Forward")} ({how}): {string.Join(" > ", places[to].Select(b => AutomationProperties.GetName(b)))}");
    }
}
