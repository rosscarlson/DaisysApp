using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using Microsoft.Terminal.Wpf;

namespace DaisysApp.Applets.Tools.Terminal;

/// <summary>
/// The Terminal: sessions in tabs, each a shell (CMD, PowerShell or WSL) in Windows Terminal's own control. + opens the
/// default kind (Settings → Tools → Terminal), the arrow beside it any kind; right-click a tab to rename it in place;
/// ✕ closes it (Confirm? first). A session's shell starts when its tab is first shown. Copy and paste as in the
/// console: Ctrl+C copies a selection (else it's Ctrl+C for the shell), Ctrl+V pastes, right-click copies a selection
/// or pastes.
/// </summary>
internal sealed class TerminalView : DockPanel
{
    private readonly TerminalSettings settings;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid pages = new();
    private readonly Button add, more;
    private readonly List<Session> sessions = new();
    private Session? cur;
    private Shells.Look? look;

    private sealed class Session
    {
        public required TerminalSettings.Session Info;
        public TerminalControl? Control;
        public ShellConnection? Connection;
        public nint Hwnd;
        public RadioButton? Tab;
        public TextBlock? Title;
    }

    public TerminalView(TerminalSettings settings)
    {
        this.settings = settings;
        add = new Button { Content = Ui.Icon("", 12), Padding = new Thickness(10, 4, 10, 6), MinHeight = 30 };
        add.SetResourceReference(StyleProperty, "TabActionButton");
        add.Click += (_, _) => New(settings.Default);
        more = new Button { Content = Ui.Icon("", 10), Padding = new Thickness(6, 4, 8, 6), MinHeight = 30, ToolTip = T("New session of another kind") };
        more.SetResourceReference(StyleProperty, "TabActionButton");
        AutomationProperties.SetName(more, T("New session of another kind"));
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var k in Shells.All)
            {
                var item = new MenuItem { Header = Shells.Name(k) };
                item.Click += (_, _) => New(k);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        ShowAddTip();
        var strip = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 6) };
        SetDock(strip, Dock.Top);
        Children.Add(strip);
        Children.Add(pages);

        foreach (var info in settings.Sessions.Where(s => s.Id.Length > 0)) sessions.Add(new Session { Info = info });
        if (sessions.Count == 0) sessions.Add(NewSession(settings.Default));
        BuildTabs();

        ComponentDispatcher.ThreadFilterMessage += Filter;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            Select(sessions.FirstOrDefault(s => s.Info.Id == settings.Selected) ?? sessions[0]);
        };
    }

    /// <summary>The + button's tip names the kind it opens (set in Settings).</summary>
    public void ShowAddTip()
    {
        add.ToolTip = F("New {0} session", Shells.Name(settings.Default));
        AutomationProperties.SetName(add, T("New session"));
    }

    private Session NewSession(ShellKind kind)
    {
        var info = new TerminalSettings.Session { Id = Guid.NewGuid().ToString("N")[..12], Kind = kind, Name = FreeName(kind) };
        settings.Sessions.Add(info);
        settings.Save();
        return new Session { Info = info };
    }

    /// <summary>"CMD 1", "PowerShell 2"…: the kind with the lowest number not taken.</summary>
    private string FreeName(ShellKind kind)
    {
        var taken = new HashSet<string>(settings.Sessions.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        int i = 1;
        while (taken.Contains($"{Shells.Name(kind)} {i}")) i++;
        return $"{Shells.Name(kind)} {i}";
    }

    private void New(ShellKind kind)
    {
        var s = NewSession(kind);
        sessions.Add(s);
        BuildTabs();
        Select(s);
    }

    // ---- the tabs

    private void BuildTabs()
    {
        tabs.Children.Clear();
        foreach (var s in sessions) tabs.Children.Add(TabOf(s));
        tabs.Children.Add(add);
        tabs.Children.Add(more);
        foreach (var s in sessions)
            if (s.Tab?.Content is StackPanel { Children: [.., Button close] }) close.Visibility = sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private RadioButton TabOf(Session s)
    {
        if (s.Tab is { } made)
        {
            made.IsChecked = s == cur;
            return made;
        }
        var title = new TextBlock { Text = s.Info.Name, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Focusable = false };
        // (closing ends the shell, so it's a second click)
        DispatcherTimer? back = null;
        void Plain()
        {
            close.ClearValue(StyleProperty);
            close.Content = "✕";
            close.FontSize = 9;
            close.Padding = new Thickness(5, 1, 5, 1);
            close.Background = Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            close.ToolTip = T("Close this session");
        }
        Plain();
        close.Click += (_, e) =>
        {
            e.Handled = true;
            if (back != null || s.Connection == null)
            {
                back?.Stop();
                back = null;
                Plain();
                Close(s);
                return;
            }
            close.ClearValue(BackgroundProperty);
            close.ClearValue(Control.BorderThicknessProperty);
            close.SetResourceReference(StyleProperty, "DangerButton");
            close.Content = T("Confirm?");
            close.FontSize = 11;
            close.Padding = new Thickness(6, 0, 6, 0);
            back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            back.Tick += (_, _) => { back?.Stop(); back = null; Plain(); };
            back.Start();
        };
        var tab = new RadioButton { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, close } }, Padding = new Thickness(12, 4, 8, 7), MinHeight = 30, Focusable = false };
        tab.SetResourceReference(StyleProperty, "TabButton");
        tab.IsChecked = s == cur;
        tab.Checked += (_, _) => Select(s);
        // (right-click: the name edited in place, as the app's own tabs)
        tab.MouseRightButtonUp += (_, e) => { BeginRename(s); e.Handled = true; };
        AutomationProperties.SetName(tab, s.Info.Name);
        s.Tab = tab;
        s.Title = title;
        return tab;
    }

    /// <summary>The tab's name becomes a text box, selected; Enter keeps it, Esc or clicking away doesn't.</summary>
    private void BeginRename(Session s)
    {
        if (s.Title is not { } title || s.Tab?.Content is not StackPanel header || header.Children.OfType<TextBox>().Any()) return;
        var editor = new TextBox
        {
            Text = s.Info.Name,
            MinWidth = Math.Max(80, title.ActualWidth + 24),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
        };
        AutomationProperties.SetName(editor, T("Tab name"));
        title.Visibility = Visibility.Collapsed;
        header.Children.Insert(0, editor);
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            header.Children.Remove(editor);
            title.Visibility = Visibility.Visible;
            string name = string.Join(" ", editor.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (!keep || name.Length == 0 || name == s.Info.Name) return;
            s.Info.Name = name.Length > 60 ? name[..60] : name;
            title.Text = s.Info.Name;
            AutomationProperties.SetName(s.Tab!, s.Info.Name);
            settings.Save();
        }
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Finish(keep: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Finish(keep: false); e.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => Finish(keep: false);
        editor.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };
    }

    private void Select(Session s)
    {
        cur = s;
        settings.Selected = s.Info.Id;
        settings.Save();
        if (s.Tab != null) s.Tab.IsChecked = true;
        if (s.Control == null) Make(s);
        foreach (var x in sessions)
            if (x.Control != null) x.Control.Visibility = x == s ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(() => { if (cur == s) s.Control?.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Its terminal, and the shell in it once the terminal is on screen.</summary>
    private void Make(Session s)
    {
        Shells.FindNative();
        look ??= Shells.ConsoleLook();
        var control = new TerminalControl { AutoResize = true, Focusable = true };
        AutomationProperties.SetName(control, s.Info.Name);
        s.Control = control;
        pages.Children.Add(control);
        control.Loaded += (_, _) =>
        {
            if (s.Connection != null) return;
            control.SetTheme(look.Theme, look.Font, look.Size, look.Background);
            var connection = new ShellConnection(s.Info.Kind);
            // (exit: the tab closes)
            connection.Exited += () => Dispatcher.BeginInvoke(() => { if (sessions.Contains(s)) Close(s); });
            s.Connection = connection;
            control.Connection = connection;
            s.Hwnd = FindHwnd(control);
            if (cur == s) control.Focus();
        };
    }

    private static nint FindHwnd(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is HwndHost host) return host.Handle;
            if (FindHwnd(child) is var h && h != 0) return h;
        }
        return 0;
    }

    private void Close(Session s)
    {
        int index = sessions.IndexOf(s);
        if (index < 0) return;
        sessions.RemoveAt(index);
        settings.Sessions.Remove(s.Info);
        s.Connection?.Dispose();
        if (s.Control != null) pages.Children.Remove(s.Control);
        s.Hwnd = 0;
        // (never none: closing the last opens a new one)
        if (sessions.Count == 0) sessions.Add(NewSession(settings.Default));
        settings.Save();
        BuildTabs();
        if (s == cur || cur == null) Select(sessions[Math.Min(index, sessions.Count - 1)]);
    }

    // ---- copy and paste, as in the console (the terminal control has none of its own)

    private const int WmKeyDown = 0x0100, WmRButtonUp = 0x0205, WmRButtonDown = 0x0204, VkControl = 0x11, VkShift = 0x10;

    private void Filter(ref MSG msg, ref bool handled)
    {
        if (handled || cur is not { Hwnd: not 0 } s || msg.hwnd != s.Hwnd || s.Control == null || s.Connection == null) return;
        if (msg.message == WmKeyDown && GetKeyState(VkControl) < 0)
        {
            int key = (int)msg.wParam;
            if (key == 'C')
            {
                // (with nothing selected, Ctrl+C goes to the shell)
                if (s.Control.GetSelectedText() is { Length: > 0 } text) { Copy(text); handled = true; }
            }
            else if (key == 'V') { Paste(s); handled = true; }
        }
        else if (msg.message == WmRButtonDown) handled = true;
        else if (msg.message == WmRButtonUp)
        {
            if (s.Control.GetSelectedText() is { Length: > 0 } text) Copy(text);
            else Paste(s);
            handled = true;
        }
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text.TrimEnd()); } catch (ExternalException) { }
    }

    private static void Paste(Session s)
    {
        try { if (Clipboard.ContainsText()) s.Connection?.Paste(Clipboard.GetText()); } catch (ExternalException) { }
    }

    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    /// <summary>The app closing: every shell ended.</summary>
    public void Shutdown()
    {
        ComponentDispatcher.ThreadFilterMessage -= Filter;
        settings.Save();
        foreach (var s in sessions) s.Connection?.Dispose();
    }
}
