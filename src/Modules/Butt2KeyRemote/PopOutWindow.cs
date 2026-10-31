using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Shared.Widgets;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// A ButtScreen in a window of its own, used with the mouse or a touchscreen as on a device: a widget (Core's
/// <see cref="Widget"/>, as every widget in the app), so it never takes the focus (the keys its buttons press go to the
/// game or program in front). Right-click it (or hold a finger on the space between buttons) to edit it: drag it,
/// stretch it from any edge (it keeps the ButtScreen's shape), how solid it is, on top, Locked, Edit ButtScreen, Done,
/// and ✕ to close it. Otherwise the space between buttons drags it (unless it's locked). Remembered, open or not, for
/// the next start.
/// </summary>
internal sealed class PopOutWindow : Widget
{
    private static int count;

    private readonly ButtHub hub;
    private readonly PopOutPlace place;
    private readonly ScreenVisual visual;
    private readonly Dictionary<object, ButtButton> pressing = new();
    private readonly DispatcherTimer hold = new() { Interval = TimeSpan.FromSeconds(0.8) };

    public ButtScreen Screen { get; private set; }

    /// <summary>Who pressed, for the engine (and for its Show a ButtScreen buttons).</summary>
    public string Source { get; } = "popout:" + Interlocked.Increment(ref count);

    /// <param name="close">Its ✕ (the hub closes it).</param>
    public PopOutWindow(ButtHub hub, ButtScreen screen, PopOutPlace place, Action close)
        : base(place, hub.Save, close, card: false, firstSize: new Size(320, 320.0 * Shape(screen)))
    {
        this.hub = hub;
        this.place = place;
        Screen = screen;
        Title = screen.Name;
        MinWidth = 120;
        MinHeight = 40;
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Aspect = Shape(screen);

        visual = new ScreenVisual(screen);
        Body = new Viewbox { Child = visual, Stretch = Stretch.Fill };

        var locked = new CheckBox { Content = T("Locked"), VerticalAlignment = VerticalAlignment.Center, IsChecked = place.Locked, ToolTip = T("The space between buttons doesn't move it") };
        locked.Click += (_, _) => { place.Locked = locked.IsChecked == true; hub.Save(); };
        AddOption(locked);
        var edit = new Button { Content = T("Edit ButtScreen"), Padding = new Thickness(10, 3, 10, 3) };
        edit.Click += (_, _) => hub.Edit(Screen);
        AddOption(edit);

        visual.MouseLeftButtonDown += (_, e) => { if (e.StylusDevice == null) PointerDown(e.MouseDevice, e.GetPosition(visual), e); };
        visual.MouseLeftButtonUp += (_, e) => { if (e.StylusDevice == null) PointerUp(e.MouseDevice); };
        visual.TouchDown += (_, e) => { PointerDown(e.TouchDevice, e.GetTouchPoint(visual).Position, null); e.TouchDevice.Capture(visual); e.Handled = true; };
        visual.TouchUp += (_, e) => { hold.Stop(); PointerUp(e.TouchDevice); visual.ReleaseTouchCapture(e.TouchDevice); e.Handled = true; };
        visual.LostMouseCapture += (_, _) => PointerUp(Mouse.PrimaryDevice);
        // a finger held on the space between buttons edits it (a touchscreen has no right-click)
        hold.Tick += (_, _) => { hold.Stop(); BeginEdit(); };
        Closed += (_, _) => { hold.Stop(); foreach (var k in pressing.Keys.ToList()) PointerUp(k); hub.Engine.LetGo(Source); };
    }

    private static double Shape(ButtScreen s) => s.Height / (double)Math.Max(1, s.Width);

    /// <summary>Draws the ButtScreen again (it was edited).</summary>
    public void Redraw()
    {
        Title = Screen.Name;
        visual.Render(Screen);
        pressing.Clear();
        Aspect = Shape(Screen);
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
        if (Editing) return;
        var b = Screen.Buttons.LastOrDefault(x => Screen.RectOf(x).Contains(p));
        if (b == null)
        {
            if (mouse == null) { hold.Stop(); hold.Start(); }
            // the space between buttons moves it (Windows moves it; Core's widget saves where it ends up)
            else if (!place.Locked) { mouse.Handled = true; try { DragMove(); } catch (InvalidOperationException) { } }
            return;
        }
        hold.Stop();
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
}
