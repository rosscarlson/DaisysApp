using DaisysApp.Logging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DaisysApp.Shell;

/// <summary>
/// Draws <see cref="TitleBadges"/> in the main window's title bar: green bubbles just right of the title text ("Daisy's
/// App v1.3"). Windows draws the title bar itself, so the bubbles are a small see-through window of their own, owned by
/// the main window (so it stays just above it), placed over the caption after the title text, and moved with it. A click
/// on a bubble opens its module (<see cref="TitleBadges.Badge.Open"/>); dragging one drags the window, and the gaps between
/// bubbles (see-through) go to the title bar as ever.
/// </summary>
internal sealed class TitleBadgeStrip
{
    private static readonly Brush Green = Frozen(Color.FromRgb(0x2E, 0xA0, 0x43));

    private readonly Window main;
    private Window? strip;
    private readonly StackPanel bubbles = new() { Orientation = Orientation.Horizontal };

    public TitleBadgeStrip(Window main)
    {
        this.main = main;
        TitleBadges.Changed += () => main.Dispatcher.BeginInvoke(Refresh);
        main.LocationChanged += (_, _) => Place();
        main.SizeChanged += (_, _) => Place();
        main.StateChanged += (_, _) => Refresh();
        main.IsVisibleChanged += (_, _) => Refresh();
        main.DpiChanged += (_, _) => main.Dispatcher.BeginInvoke(Place);
        main.ContentRendered += (_, _) => Refresh();
        main.Closed += (_, _) => strip?.Close();
    }

    /// <summary>Shows the bubbles there are (none: nothing), where they go.</summary>
    public void Refresh()
    {
        var all = TitleBadges.All;
        bool show = all.Count > 0 && main.IsVisible && main.WindowState != WindowState.Minimized && new WindowInteropHelper(main).Handle != IntPtr.Zero;
        if (!show)
        {
            strip?.Hide();
            return;
        }
        bubbles.Children.Clear();
        foreach (var b in all)
        {
            var bubble = new Border
            {
                Background = b.Color is { } c && ColorConverter.ConvertFromString(c) is Color colour ? Frozen(colour) : Green,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 0, 8, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = b.Text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI") },
                ToolTip = b.Tip,
            };
            if (b.Open is { } open) Clickable(bubble, open);
            bubbles.Children.Add(bubble);
        }
        if (strip == null)
        {
            strip = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Content = bubbles,
                Owner = main,
                Title = "Title badges",
            };
            strip.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(strip).Handle;
                long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
            };
            strip.SizeChanged += (_, _) => Place();
        }
        if (!strip.IsVisible) strip.Show();
        Place();
    }

    /// <summary>A click (pressed and let go without moving) opens <paramref name="open"/>; moving while pressed drags the
    /// main window, as the title bar under it would.</summary>
    private void Clickable(Border bubble, string open)
    {
        bubble.Cursor = System.Windows.Input.Cursors.Hand;
        Point? down = null;
        bubble.MouseLeftButtonDown += (_, e) =>
        {
            down = e.GetPosition(bubble);
            bubble.CaptureMouse();
            e.Handled = true;
        };
        bubble.MouseMove += (_, e) =>
        {
            if (down is not { } from || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var v = e.GetPosition(bubble) - from;
            if (Math.Abs(v.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(v.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            bubble.ReleaseMouseCapture();
            var hwnd = new WindowInteropHelper(main).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        };
        bubble.MouseLeftButtonUp += (_, e) =>
        {
            bool click = down != null;
            down = null;
            bubble.ReleaseMouseCapture();
            e.Handled = true;
            if (!click) return;
            Log.App.Info($"Title bubble clicked: {open}");
            if (!AppNavigation.OpenTab(open)) Log.App.Warn($"Title bubble: no tab for {open}");
        };
    }

    /// <summary>Over the caption, after the title text, centred on the caption buttons' height; hidden if it would reach them.</summary>
    private void Place()
    {
        if (strip is not { IsVisible: true }) return;
        var hwnd = new WindowInteropHelper(main).Handle;
        var own = new WindowInteropHelper(strip).Handle;
        if (hwnd == IntPtr.Zero || own == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var outer)) return;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT visible, Marshal.SizeOf<RECT>()) != 0) visible = outer;
        // the caption buttons (relative to the window): their top and bottom are the caption's
        if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out RECT buttons, Marshal.SizeOf<RECT>()) != 0)
            buttons = new RECT { Left = outer.Right - outer.Left - 140, Top = visible.Top - outer.Top, Right = outer.Right - outer.Left, Bottom = visible.Top - outer.Top + 30 };
        double scale = VisualTreeHelper.GetDpi(main).DpiScaleX;
        // the title: the small icon then the text, as Windows lays them out (8 px in, a 16 px icon, 5 px gap)
        var font = SystemFonts.CaptionFontFamily ?? new FontFamily("Segoe UI");
        var text = new FormattedText(main.Title, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font, SystemFonts.CaptionFontStyle, SystemFonts.CaptionFontWeight, FontStretches.Normal), SystemFonts.CaptionFontSize, Brushes.White, scale);
        int textLeft = visible.Left + (int)Math.Round(29 * scale);
        int x = textLeft + (int)Math.Ceiling(text.WidthIncludingTrailingWhitespace * scale) + (int)Math.Round(10 * scale);
        int captionTop = outer.Top + buttons.Top, captionHeight = Math.Max(1, buttons.Bottom - buttons.Top);
        if (!GetWindowRect(own, out var mine)) return;
        int w = mine.Right - mine.Left, h = mine.Bottom - mine.Top;
        int y = captionTop + (captionHeight - h) / 2;
        bool fits = x + w <= outer.Left + buttons.Left - (int)(8 * scale);
        strip.Opacity = fits ? 1 : 0;
        SetWindowPos(own, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private const int GWL_EXSTYLE = -20, DWMWA_CAPTION_BUTTON_BOUNDS = 5, DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
}
