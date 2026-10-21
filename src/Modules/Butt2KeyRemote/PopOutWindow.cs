using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// A ButtScreen in a window of its own, used with the mouse or a touchscreen as on a device. It never takes the focus
/// (so the keys its buttons press go to the game or program in front), has no frame: drag it by the space between
/// buttons, resize it with the corner grip (it keeps its shape), and right-click it for how solid it is, keeping it
/// on top, locking it in place, editing the ButtScreen and closing it. Remembered, open or not, for the next start.
/// </summary>
internal sealed class PopOutWindow : Window
{
    private static int count;

    private readonly ButtHub hub;
    private readonly PopOutPlace place;
    private readonly ScreenVisual visual;
    private readonly Border grip = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent };
    private readonly Dictionary<object, ButtButton> pressing = new();

    // dragging the window, or resizing it by the grip
    private enum Drag { None, Move, Resize }
    private Drag drag;
    private Point dragStart;
    private double startLeft, startTop, startWidth;

    public ButtScreen Screen { get; private set; }

    /// <summary>Who pressed, for the engine (and for its Show a ButtScreen buttons).</summary>
    public string Source { get; } = "popout:" + Interlocked.Increment(ref count);

    public PopOutWindow(ButtHub hub, ButtScreen screen, PopOutPlace place)
    {
        this.hub = hub;
        this.place = place;
        Screen = screen;
        Title = screen.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = place.OnTop;
        Opacity = Math.Clamp(place.Opacity, 0.2, 1);
        Stylus.SetIsPressAndHoldEnabled(this, false);

        visual = new ScreenVisual(screen);
        var view = new Viewbox { Child = visual, Stretch = Stretch.Fill };
        // the grip: three little lines in the corner
        var lines = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 20,8 L 8,20 M 20,13 L 13,20 M 20,18 L 18,20"),
            Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), StrokeThickness = 1.5,
        };
        grip.Child = lines;
        grip.ToolTip = T("Drag to resize");
        var root = new Grid();
        root.Children.Add(view);
        root.Children.Add(grip);
        Content = root;
        Fit();

        if (place.Left is { } left && place.Top is { } top && OnAScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        SourceInitialized += (_, _) =>
        {
            // never the active window, so the game keeps the keyboard; and not in Alt+Tab
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        ShowLock();

        visual.MouseLeftButtonDown += (_, e) => { if (e.StylusDevice == null) PointerDown(e.MouseDevice, e.GetPosition(visual), e); };
        visual.MouseLeftButtonUp += (_, e) => { if (e.StylusDevice == null) PointerUp(e.MouseDevice); EndDrag(); };
        visual.TouchDown += (_, e) => { PointerDown(e.TouchDevice, e.GetTouchPoint(visual).Position, null); e.TouchDevice.Capture(visual); e.Handled = true; };
        visual.TouchUp += (_, e) => { PointerUp(e.TouchDevice); visual.ReleaseTouchCapture(e.TouchDevice); e.Handled = true; };
        visual.LostMouseCapture += (_, _) => { PointerUp(Mouse.PrimaryDevice); EndDrag(); };
        visual.MouseMove += (_, e) => DragMove(e);
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (place.Locked) return;
            StartDrag(Drag.Resize);
            e.Handled = true;
        };
        grip.MouseMove += (_, e) => DragMove(e);
        grip.MouseLeftButtonUp += (_, _) => EndDrag();
        MouseRightButtonUp += (_, e) => { Menu(); e.Handled = true; };
        Closed += (_, _) => { foreach (var k in pressing.Keys.ToList()) PointerUp(k); hub.Engine.LetGo(Source); };
    }

    /// <summary>The window's size: its width, and the height that keeps the ButtScreen's shape.</summary>
    private void Fit()
    {
        Width = Math.Clamp(place.Width, 120, 3000);
        Height = Width * Screen.Height / Math.Max(1, Screen.Width);
    }

    /// <summary>Draws the ButtScreen again (it was edited).</summary>
    public void Redraw()
    {
        Title = Screen.Name;
        visual.Render(Screen);
        pressing.Clear();
        Fit();
    }

    /// <summary>Shows another ButtScreen in this window (a Show a ButtScreen button was pressed).</summary>
    public void Switch(ButtScreen to)
    {
        if (to == Screen) return;
        foreach (var k in pressing.Keys.ToList()) PointerUp(k);
        var from = Screen;
        Screen = to;
        hub.Moved(this, from, to);
        Redraw();
    }

    // ---- pressing buttons ----

    private void PointerDown(object device, Point p, MouseButtonEventArgs? mouse)
    {
        var b = Screen.Buttons.LastOrDefault(x => Screen.RectOf(x).Contains(p));
        if (b == null)
        {
            // the space between buttons moves the window
            if (mouse != null && !place.Locked) { StartDrag(Drag.Move); mouse.Handled = true; }
            return;
        }
        if (pressing.ContainsKey(device)) return;
        pressing[device] = b;
        if (mouse != null) { visual.CaptureMouse(); mouse.Handled = true; }
        if (visual.VisualOf(b) is { } v) v.Pressed = true;
        hub.Engine.Down(Source, Screen.Id, b.Id);
    }

    private void PointerUp(object device)
    {
        if (!pressing.Remove(device, out var b)) return;
        if (device is MouseDevice && visual.IsMouseCaptured) visual.ReleaseMouseCapture();
        if (!pressing.ContainsValue(b))
        {
            if (visual.VisualOf(b) is { } v) v.Pressed = false;
            hub.Engine.Up(Source, Screen.Id, b.Id);
        }
    }

    // ---- moving and resizing (in screen units, from where the pointer is on the screen) ----

    private Point PointerOnScreen()
    {
        GetCursorPos(out var pt);
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(new Point(pt.X, pt.Y)) ?? new Point(pt.X, pt.Y);
    }

    private void StartDrag(Drag what)
    {
        drag = what;
        dragStart = PointerOnScreen();
        startLeft = Left;
        startTop = Top;
        startWidth = Width;
        if (what == Drag.Resize) grip.CaptureMouse(); else visual.CaptureMouse();
    }

    private void DragMove(MouseEventArgs e)
    {
        if (drag == Drag.None || e.LeftButton != MouseButtonState.Pressed) return;
        var d = PointerOnScreen() - dragStart;
        if (drag == Drag.Move)
        {
            Left = startLeft + d.X;
            Top = startTop + d.Y;
        }
        else
        {
            // keeps its shape: whichever way the pointer went further sets the size
            double ratio = Screen.Height / (double)Math.Max(1, Screen.Width);
            double w = startWidth + Math.Max(d.X, d.Y / ratio);
            place.Width = Math.Clamp(w, 120, 3000);
            Fit();
        }
    }

    private void EndDrag()
    {
        if (drag == Drag.None) return;
        var was = drag;
        drag = Drag.None;
        if (grip.IsMouseCaptured) grip.ReleaseMouseCapture();
        if (visual.IsMouseCaptured && pressing.Count == 0) visual.ReleaseMouseCapture();
        if (was == Drag.Move) { place.Left = Left; place.Top = Top; }
        hub.Save();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded) { place.Left = Left; place.Top = Top; }
    }

    private static bool OnAScreen(double left, double top) =>
        left + 40 > SystemParameters.VirtualScreenLeft && top + 20 > SystemParameters.VirtualScreenTop
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

    // ---- the right-click menu ----

    private void Menu()
    {
        var menu = new ContextMenu();
        var head = new MenuItem { Header = Screen.Name, IsEnabled = false };
        menu.Items.Add(head);
        menu.Items.Add(new Separator());
        var solid = new MenuItem { Header = T("How solid") };
        foreach (int percent in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20 })
        {
            int p = percent;
            var item = new MenuItem { Header = F("{0}%", p), IsCheckable = true, IsChecked = Math.Abs(place.Opacity * 100 - p) < 5 };
            item.Click += (_, _) => { place.Opacity = p / 100.0; Opacity = place.Opacity; hub.Save(); };
            solid.Items.Add(item);
        }
        menu.Items.Add(solid);
        var top = new MenuItem { Header = T("Keep on top"), IsCheckable = true, IsChecked = place.OnTop };
        top.Click += (_, _) => { place.OnTop = !place.OnTop; Topmost = place.OnTop; hub.Save(); };
        menu.Items.Add(top);
        var locked = new MenuItem { Header = T("Lock in place"), IsCheckable = true, IsChecked = place.Locked, ToolTip = T("It can't be moved or resized; every touch presses") };
        locked.Click += (_, _) => { place.Locked = !place.Locked; ShowLock(); hub.Save(); };
        menu.Items.Add(locked);
        var size = new MenuItem { Header = T("Size") };
        foreach (int w in new[] { 200, 280, 360, 480, 640, 800 })
        {
            int width = w;
            var item = new MenuItem { Header = F("{0} wide", width), IsCheckable = true, IsChecked = Math.Abs(place.Width - width) < 1 };
            item.Click += (_, _) => { place.Width = width; Fit(); hub.Save(); };
            size.Items.Add(item);
        }
        size.IsEnabled = !place.Locked;
        menu.Items.Add(size);
        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = T("Edit this ButtScreen…") };
        edit.Click += (_, _) => hub.Edit(Screen);
        menu.Items.Add(edit);
        var close = new MenuItem { Header = T("Close") };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void ShowLock() => grip.Visibility = place.Locked ? Visibility.Collapsed : Visibility.Visible;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern long SetWindowLongPtr(nint hwnd, int index, long value);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
}
