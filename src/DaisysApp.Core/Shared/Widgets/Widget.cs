using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DaisysApp.Shared.Widgets;

/// <summary>Where a widget is and how it looks, kept in its module's settings.</summary>
public interface IWidgetPlace
{
    /// <summary>Its place and size on screen, in physical pixels (0 width: not placed yet).</summary>
    int X { get; set; }
    int Y { get; set; }
    int Width { get; set; }
    int Height { get; set; }
    /// <summary>The whole widget's opacity, 0.2 – 1.</summary>
    double Opacity { get; set; }
    bool Topmost { get; set; }
}

/// <summary>
/// A widget: something of a module's (a graph, a box, meters) in its own small, borderless window that never takes the
/// focus and isn't in Alt+Tab. While it's being edited (just made, or right-click) it can be dragged anywhere and
/// stretched from any edge or corner, and a strip at the bottom sets its opacity and whether it stays on top (plus the
/// module's <see cref="AddOption"/>s); <b>Done</b> fixes it in place and saves its place. Every widget in the app is
/// one of these (Hardware → Performance, Zabbix, ForzaWheel's input overlay, the Shaker's meters, Button2Key Remote's
/// pop-outs): a new one is too, and what makes one is <see cref="Offer"/>'s right-click <b>Make Widget</b> (<see cref="Menu"/>, with History, or
/// <see cref="MenuItem"/> in a menu of the module's own).
/// </summary>
public class Widget : Window
{
    private readonly IWidgetPlace place;
    private readonly Action save;
    private readonly Action? delete;
    private static readonly List<Widget> open = new();

    /// <summary>The widgets showing now, any module's (on the UI thread).</summary>
    public static IReadOnlyList<Widget> Open => open;

    /// <summary>Raised when a widget opens or closes.</summary>
    public static event Action? OpenChanged;

    /// <summary>The module that made it (its assembly), for <see cref="ListCard"/>.</summary>
    public Assembly Owner { get; }

    /// <summary>Whether it has a Delete (<see cref="Delete"/> does what the strip's button does).</summary>
    public bool CanDelete => delete != null;

    /// <summary>Deletes it as its strip's Delete button does (the module closes it and forgets it).</summary>
    public void Delete() => delete?.Invoke();
    private readonly bool card, clickThrough;
    private readonly Size firstSize;
    private readonly Border? frame;
    private readonly DockPanel layout = new();
    private readonly Border? editStrip;
    private readonly WrapPanel? options;
    private UIElement? body;
    private Brush? edge;
    private bool editing;
    private int grownPx;
    private double? aspect;

    /// <summary>The words every module's menu uses for making a widget.</summary>
    public static string MakeText => T("Make Widget");

    /// <summary>A "Make Widget" menu item, for a module's own right-click menu.</summary>
    public static MenuItem MenuItem(Action make)
    {
        var item = new MenuItem { Header = MakeText };
        item.Click += (_, _) => make();
        return item;
    }

    /// <summary>
    /// The right-click menu of something a widget can be made of (a Performance tile, a Zabbix host, graph or item):
    /// <paramref name="openText"/> (History, Open) and Make Widget. Set as <paramref name="target"/>'s menu (none: the
    /// caller sets it) and returned, for the module's own items after them.
    /// </summary>
    public static ContextMenu Menu(FrameworkElement? target, string openText, Action open, Action make)
    {
        var menu = new ContextMenu();
        var item = new MenuItem { Header = openText };
        item.Click += (_, _) => open();
        menu.Items.Add(item);
        menu.Items.Add(MenuItem(make));
        if (target != null) target.ContextMenu = menu;
        return menu;
    }

    /// <summary>
    /// A widget's delete button, everywhere one is (its edit strip, a module's list of widgets): a trash can that turns
    /// into a red ✕ on the first click (Core's <see cref="ConfirmButton.ArmIcon"/>); a second click deletes it.
    /// </summary>
    public static Button DeleteButton(Action delete, string? name = null)
    {
        var icon = new TextBlock { Text = "", FontSize = 13 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = icon, Padding = new Thickness(8, 4, 8, 4), ToolTip = T("Delete this widget") };
        System.Windows.Automation.AutomationProperties.SetName(b, name == null ? T("Delete this widget") : T("Delete") + " " + name);
        ConfirmButton.ArmIcon(b, delete);
        return b;
    }

    /// <summary>Right-click on <paramref name="target"/> offers "Make Widget" (added to its own menu, if it has one).</summary>
    public static void Offer(FrameworkElement target, Action make)
    {
        target.ContextMenu ??= new ContextMenu();
        if (target.ContextMenu.Items.Count > 0) target.ContextMenu.Items.Add(new Separator());
        target.ContextMenu.Items.Add(MenuItem(make));
    }

    /// <summary>
    /// A "Widgets" card for the calling module's tab: a row per widget of the module's that's open (its title, Edit,
    /// and its <see cref="DeleteButton"/> where it has one); hidden while there are none. Kept up to date as widgets open and close.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static FrameworkElement ListCard()
    {
        var owner = Assembly.GetCallingAssembly();
        var rows = new StackPanel();
        var header = new TextBlock { Text = T("Widgets") };
        header.SetResourceReference(StyleProperty, "CardHeader");
        var card = new Border { Child = new StackPanel { Children = { header, rows } } };
        card.SetResourceReference(StyleProperty, "Card");
        void Fill()
        {
            rows.Children.Clear();
            var mine = open.Where(w => w.Owner == owner).ToList();
            card.Visibility = mine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var w in mine)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                var editIt = new Button { Content = T("Edit"), Padding = new Thickness(10, 2, 10, 2), ToolTip = T("Move, size and set it up") };
                editIt.Click += (_, _) => w.BeginEdit();
                System.Windows.Automation.AutomationProperties.SetName(editIt, T("Edit") + " " + w.Title);
                buttons.Children.Add(editIt);
                if (w.CanDelete)
                {
                    var del = DeleteButton(w.Delete, w.Title);
                    del.Margin = new Thickness(6, 0, 0, 0);
                    buttons.Children.Add(del);
                }
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                row.Children.Add(new TextBlock { Text = w.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                rows.Children.Add(row);
            }
        }
        Fill();
        OpenChanged += Fill;
        return card;
    }

    /// <summary>Raised when editing starts or ends.</summary>
    public event Action? EditingChanged;

    public bool Editing => editing;

    /// <param name="place">Where it is (and its opacity and on top), saved through <paramref name="save"/>.</param>
    /// <param name="delete">The strip's Delete button (none when null).</param>
    /// <param name="card">Drawn as a card (background, border, padding); else the body draws everything.</param>
    /// <param name="strip">The edit strip; without it, <see cref="EndEdit"/> is the module's to call.</param>
    /// <param name="clickThrough">Clicks go through it (to a game) while it isn't being edited.</param>
    /// <param name="firstSize">Its size the first time, in DIPs.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Widget(IWidgetPlace place, Action save, Action? delete = null, bool card = true, bool strip = true, bool clickThrough = false, Size? firstSize = null)
    {
        Owner = Assembly.GetCallingAssembly();
        this.place = place;
        this.save = save;
        this.delete = delete;
        SourceInitialized += (_, _) => { open.Add(this); OpenChanged?.Invoke(); };
        // (closed: its body is let go, so the module can show it again in a new widget)
        Closed += (_, _) =>
        {
            Body = null;
            if (open.Remove(this)) OpenChanged?.Invoke();
        };
        this.card = card;
        this.clickThrough = clickThrough;
        this.firstSize = firstSize ?? new Size(320, 150);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize; // the sizing frame Windows needs to stretch it (invisible here; only offered while editing)
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = place.Topmost;
        Opacity = Math.Clamp(place.Opacity, 0.2, 1);
        MinWidth = 140;
        MinHeight = 70;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        SetResourceReference(ForegroundProperty, "TextBrush");

        if (strip)
        {
            // the edit strip: opacity, on top, the module's options, Done and Delete
            var opacity = new Slider { Minimum = 20, Maximum = 100, Width = 110, VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 10, Value = Math.Round(place.Opacity * 100), ToolTip = T("Opacity") };
            var topmost = new CheckBox { Content = T("On top"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = place.Topmost, ToolTip = T("Stay on top of other windows") };
            opacity.ValueChanged += (_, _) => { place.Opacity = opacity.Value / 100; Opacity = place.Opacity; };
            topmost.Click += (_, _) => { place.Topmost = topmost.IsChecked == true; Topmost = place.Topmost; };
            var opacityLabel = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = T("Opacity") };
            opacityLabel.SetResourceReference(FontFamilyProperty, "IconFont");
            options = new WrapPanel();
            var done = new Button { Content = T("Done"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            done.SetResourceReference(StyleProperty, "AccentButton");
            done.Click += (_, _) => EndEdit();
            var row = new WrapPanel();
            foreach (UIElement e in new UIElement[] { opacityLabel, opacity, topmost, options, done }) row.Children.Add(e);
            if (delete != null)
            {
                var remove = DeleteButton(delete);
                remove.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(remove);
            }
            editStrip = new Border { Child = row, Padding = card ? new Thickness(0, 8, 0, 0) : new Thickness(10, 6, 10, 6), Visibility = Visibility.Collapsed };
            if (!card)
            {
                // (no card behind it: a bar of its own, so it reads over whatever the body draws)
                editStrip.CornerRadius = new CornerRadius(0, 0, 8, 8);
                editStrip.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            }
            DockPanel.SetDock(editStrip, Dock.Bottom);
            layout.Children.Add(editStrip);
        }

        if (card)
        {
            frame = new Border { Child = layout, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 10), BorderThickness = new Thickness(2) };
            frame.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            Content = frame;
        }
        else Content = layout;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (place.Width <= 0)
            {
                // first time: its first size, a little in from the top left of the main screen
                double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
                place.Width = (int)(this.firstSize.Width * scale);
                place.Height = (int)(this.firstSize.Height * scale);
                if (place.X == 0 && place.Y == 0) place.X = place.Y = (int)(80 * scale);
            }
            int width = Math.Max((int)MinWidth, place.Width);
            int height = aspect != null ? HeightFor(width) : Math.Max((int)MinHeight, place.Height);
            SetWindowPos(hwnd, IntPtr.Zero, place.X, place.Y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
            ApplyStyle();
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        MouseRightButtonUp += (_, e) => { if (!editing && editStrip != null) { BeginEdit(); e.Handled = true; } };
    }

    /// <summary>What it shows: above the edit strip, inside the card.</summary>
    public UIElement? Body
    {
        get => body;
        set
        {
            if (body != null) layout.Children.Remove(body);
            body = value;
            if (value != null) layout.Children.Add(value);
        }
    }

    /// <summary>
    /// The body's height over its width, kept as the widget is stretched (null: any shape). Setting it fits the height to
    /// the width it has now (a ButtScreen of another shape, say).
    /// </summary>
    public double? Aspect
    {
        get => aspect;
        set
        {
            aspect = value;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (value == null || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            int h = HeightFor(r.Right - r.Left);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
            if (!editing) SaveBounds();
        }
    }

    private double Scale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    /// <summary>The card's own space (padding and border) across and down, in physical pixels.</summary>
    private (int W, int H) Chrome => card ? ((int)Math.Round(28 * Scale), (int)Math.Round(22 * Scale)) : (0, 0);

    /// <summary>The window's height (with the strip, while it shows) for a width, keeping <see cref="Aspect"/>.</summary>
    private int HeightFor(int width) => (int)Math.Round((width - Chrome.W) * aspect!.Value) + Chrome.H + grownPx;

    private int WidthFor(int height) => (int)Math.Round((height - Chrome.H - grownPx) / aspect!.Value) + Chrome.W;

    /// <summary>A control of the module's in the edit strip, before Done (how much time a graph shows…).</summary>
    public void AddOption(UIElement e)
    {
        if (e is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(10, 0, 0, 0);
        options?.Children.Add(e);
    }

    /// <summary>The card's border colour while it isn't being edited (null: the card's own).</summary>
    public Brush? Edge
    {
        get => edge;
        set
        {
            edge = value;
            if (!editing) ShowEdge();
        }
    }

    private void ShowEdge()
    {
        if (frame == null) return;
        if (editing) frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        else if (edge != null) frame.BorderBrush = edge;
        else frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
    }

    /// <summary>Movable and stretchable, with the edit strip showing.</summary>
    public void BeginEdit()
    {
        if (editing) { Activate(); return; }
        editing = true;
        if (editStrip != null) editStrip.Visibility = Visibility.Visible;
        ShowEdge();
        // fully clear pixels (the rounded corners) let the mouse through: barely-there ones catch it, for the corner handles
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        if (!IsVisible) Show();
        ApplyStyle();
        EditingChanged?.Invoke();
        if (editStrip == null) return;
        // taller by the strip while it shows, so what it shows keeps its size (and shrinks back on Done)
        Dispatcher.BeginInvoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!editing || grownPx > 0 || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            grownPx = (int)Math.Ceiling(editStrip.ActualHeight * scale);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left, r.Bottom - r.Top + grownPx, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Fixed in place again, and saved.</summary>
    public void EndEdit()
    {
        if (!editing) return;
        editing = false;
        if (editStrip != null) editStrip.Visibility = Visibility.Collapsed;
        ShowEdge();
        Background = Brushes.Transparent;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (grownPx > 0 && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left, Math.Max((int)MinHeight, r.Bottom - r.Top - grownPx), SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
        grownPx = 0;
        ApplyStyle();
        SaveBounds();
        EditingChanged?.Invoke();
    }

    /// <summary>A tool window that never takes the focus; one that lets clicks through does so while not being edited.</summary>
    private void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (clickThrough) ex = editing ? (ex | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT : ex | WS_EX_LAYERED | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    private void SaveBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
        {
            place.X = r.Left;
            place.Y = r.Top;
            place.Width = r.Right - r.Left;
            place.Height = Math.Max((int)MinHeight, r.Bottom - r.Top - grownPx);
        }
        save();
    }

    /// <summary>
    /// While editing, Windows itself moves (anywhere on the widget) and resizes (its edges and corners) the window, so it
    /// behaves like any window on any monitor and DPI; the edit strip's controls stay clickable.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && editing)
        {
            int x = unchecked((short)(lParam.ToInt64() & 0xFFFF)), y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            GetWindowRect(hwnd, out var r);
            int size = card ? 8 : 10;
            bool left = x < r.Left + size, right = x >= r.Right - size, top = y < r.Top + size, bottom = y >= r.Bottom - size;
            int hit = top && left ? HTTOPLEFT : top && right ? HTTOPRIGHT : bottom && left ? HTBOTTOMLEFT : bottom && right ? HTBOTTOMRIGHT
                : left ? HTLEFT : right ? HTRIGHT : top ? HTTOP : bottom ? HTBOTTOM : 0;
            if (hit == 0)
            {
                // over the edit strip's controls: they get the click; anywhere else drags the widget
                var p = PointFromScreen(new Point(x, y));
                hit = editStrip != null && InputHitTest(p) is DependencyObject d && IsInside(d, editStrip) && d is not Border ? HTCLIENT : HTCAPTION;
            }
            handled = true;
            return new IntPtr(hit);
        }
        if (msg == WM_SIZING && aspect != null)
        {
            // keeps its shape: the side dragged sets the size, the other follows (top and bottom edges set the width)
            var r = Marshal.PtrToStructure<RECT>(lParam);
            int edge = wParam.ToInt32();
            if (edge is WMSZ_TOP or WMSZ_BOTTOM) r.Right = r.Left + WidthFor(r.Bottom - r.Top);
            else
            {
                int h = HeightFor(r.Right - r.Left);
                if (edge is WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.Top = r.Bottom - h; else r.Bottom = r.Top + h;
            }
            Marshal.StructureToPtr(r, lParam, false);
            handled = true;
            return new IntPtr(1);
        }
        if (msg == WM_EXITSIZEMOVE) SaveBounds();
        // no window menu on a right-click of the "caption" while editing
        if (msg == WM_NCRBUTTONUP && editing) { handled = true; return IntPtr.Zero; }
        return IntPtr.Zero;
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var p = d; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p))
            if (p == container) return true;
        return false;
    }

    private const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232, WM_NCRBUTTONUP = 0xA5, WM_SIZING = 0x214;
    private const int WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
