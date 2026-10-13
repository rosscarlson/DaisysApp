using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Applets.Network.Zabbix.Views;

namespace DaisysApp.Applets.Network.Zabbix.Widgets;

/// <summary>
/// A widget, as Performance's are: a host's box, one of a host's graphs, or chosen items, in its own small borderless
/// window. While it's being edited (just made, or right-click) it can be dragged anywhere and stretched from any edge
/// or corner, and a strip at the bottom sets its opacity, whether it stays on top, and how much time it shows;
/// <b>Done</b> fixes it in place. Redrawn after each refresh.
/// </summary>
internal sealed class ZabbixWidgetWindow : Window
{
    private readonly ZabbixWidgetDefinition def;
    private readonly ZabbixService service;
    private readonly Action save;
    private readonly Border card;
    private readonly HostBox? hostBox;
    private readonly LineGraph graph = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock value = new() { FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Border editStrip;
    private readonly Slider opacity = new() { Minimum = 20, Maximum = 100, Width = 110, VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 10 };
    private readonly CheckBox topmost = new() { Content = T("On top"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ComboBox minutes = new() { Width = 88, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private IDisposable? watch;
    private bool editing;

    public ZabbixWidgetDefinition Definition => def;

    public ZabbixWidgetWindow(ZabbixWidgetDefinition def, ZabbixService service, Action save, Action delete)
    {
        this.def = def;
        this.service = service;
        this.save = save;
        Title = F("{0} widget", def.Title);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize; // the sizing frame Windows needs to stretch it (invisible here; only offered while editing)
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = def.Topmost;
        Opacity = Math.Clamp(def.Opacity, 0.2, 1);
        MinWidth = 140;
        MinHeight = 70;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        SetResourceReference(ForegroundProperty, "TextBrush");

        // the edit strip: opacity, on top, time span (not for a host's box), Done and Delete
        foreach (int m in new[] { 30, 60, 360, 1440 }) minutes.Items.Add(new ComboBoxItem { Content = m < 60 ? F("{0} min", m) : F("{0} h", m / 60), Tag = m });
        minutes.SelectedItem = minutes.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == def.Minutes) ?? minutes.Items[1];
        minutes.ToolTip = T("How much time the graph shows");
        opacity.Value = Math.Round(def.Opacity * 100);
        opacity.ToolTip = T("Opacity");
        topmost.IsChecked = def.Topmost;
        topmost.ToolTip = T("Stay on top of other windows");
        opacity.ValueChanged += (_, _) => { def.Opacity = opacity.Value / 100; Opacity = def.Opacity; };
        topmost.Click += (_, _) => { def.Topmost = topmost.IsChecked == true; Topmost = def.Topmost; };
        minutes.SelectionChanged += (_, _) => { if (minutes.SelectedItem is ComboBoxItem { Tag: int m }) { def.Minutes = m; Update(); } };
        var done = new Button { Content = T("Done"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        done.SetResourceReference(StyleProperty, "AccentButton");
        done.Click += (_, _) => EndEdit();
        var remove = new Button { Content = "", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3), ToolTip = T("Delete this widget") };
        remove.SetResourceReference(FontFamilyProperty, "IconFont");
        remove.Click += (_, _) => delete();
        var strip = new WrapPanel();
        var opacityLabel = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = T("Opacity") };
        opacityLabel.SetResourceReference(FontFamilyProperty, "IconFont");
        foreach (UIElement e in new UIElement[] { opacityLabel, opacity, topmost }) strip.Children.Add(e);
        if (def.Kind != ZabbixWidgetKind.Host) strip.Children.Add(minutes);
        strip.Children.Add(done);
        strip.Children.Add(remove);
        editStrip = new Border { Child = strip, Padding = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(editStrip, Dock.Bottom);

        var layout = new DockPanel();
        layout.Children.Add(editStrip);
        if (def.Kind == ZabbixWidgetKind.Host)
        {
            hostBox = new HostBox(service, def.HostId, compact: true);
            layout.Children.Add(hostBox);
        }
        else
        {
            var header = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            header.Children.Add(value);
            header.Children.Add(title);
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);
            layout.Children.Add(graph);
        }
        card = new Border { Child = layout, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 10), BorderThickness = new Thickness(2) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // a tool window (not in Alt+Tab) that doesn't take the focus when clicked
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
            SetWindowPos(hwnd, IntPtr.Zero, def.X, def.Y, Math.Max(140, def.Width), Math.Max(70, def.Height), SWP_NOZORDER | SWP_NOACTIVATE);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        MouseRightButtonUp += (_, e) => { if (!editing) { BeginEdit(); e.Handled = true; } };

        // a host's box needs only what every box has; a graph needs the host's items
        watch = service.Watch(def.Kind == ZabbixWidgetKind.Host ? null : def.HostId);
        service.Changed += Update;
        ZabbixHistoryWindow.ScaleChanged += Update;
        Closed += (_, _) =>
        {
            service.Changed -= Update;
            ZabbixHistoryWindow.ScaleChanged -= Update;
            watch?.Dispose();
            watch = null;
        };
        Update();
    }

    /// <summary>The lines this widget draws, from the host's items as they are now.</summary>
    private (string Title, List<(ZItem Item, string Color)> Lines) Lines()
    {
        var items = service.ItemsOf(def.HostId);
        string host = service.Host(def.HostId)?.Display ?? "";
        if (def.Kind == ZabbixWidgetKind.Group)
        {
            var group = HostMetrics.Build(items).FirstOrDefault(g => g.Key == def.Group);
            return ($"{host} · {group?.Title ?? def.Title}", group?.Main.Lines.ToList() ?? new());
        }
        var lines = def.ItemKeys.Select(k => items.FirstOrDefault(i => i.Key == k)).OfType<ZItem>()
            .Select((i, n) => (i, HostMetrics.Palette[n % HostMetrics.Palette.Length])).ToList();
        return ($"{host} · {def.Title}", lines);
    }

    public void Update()
    {
        if (hostBox != null)
        {
            hostBox.Update();
            if (!editing)
            {
                if (hostBox.Edge is { } edge) card.BorderBrush = edge;
                else card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            }
            return;
        }
        var (text, lines) = Lines();
        title.Text = text;
        title.ToolTip = text;
        if (lines.Count == 0)
        {
            value.Text = "";
            graph.Show(Array.Empty<GraphSeries>(), DateTime.Now.AddMinutes(-def.Minutes), DateTime.Now, null, TimeSpan.FromMinutes(5));
            return;
        }
        var first = lines[0].Item;
        value.Text = first.ValueText;
        var to = DateTime.Now;
        var from = to.AddMinutes(-def.Minutes);
        service.Want(lines.Select(l => l.Item), from);
        var unit = first.Unit;
        var series = lines.Select(l => new GraphSeries
        {
            Name = l.Item.Name,
            Metric = l.Item.Unit,
            Points = service.HistoryOf(l.Item.ItemId, from).Select(p => (p.Time, p.Value)).ToList(),
            Color = l.Color,
            Fill = lines.Count == 1,
        }).ToList();
        graph.AxisFormat = unit.Axis;
        graph.Show(series, from, to, unit.IsPercent ? 100 : service.Settings.ScaleMax.TryGetValue(unit.Kind, out double top) ? top : null,
            TimeSpan.FromSeconds(Math.Max(180, service.Settings.RefreshSeconds * 3)));
    }

    /// <summary>Movable and stretchable, with the edit strip showing.</summary>
    public void BeginEdit()
    {
        editing = true;
        editStrip.Visibility = Visibility.Visible;
        card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        // fully clear pixels (the rounded corners) let the mouse through: barely-there ones catch it, for the corner handles
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        if (!IsVisible) Show();
        // taller by the strip while it shows, so the graph keeps its size (and shrinks back on Done)
        Dispatcher.BeginInvoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!editing || grownPx > 0 || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            grownPx = (int)Math.Ceiling(editStrip.ActualHeight * scale);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left, r.Bottom - r.Top + grownPx, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>How much taller the window is while the edit strip shows, in physical pixels.</summary>
    private int grownPx;

    /// <summary>Fixed in place again, and saved.</summary>
    public void EndEdit()
    {
        editing = false;
        editStrip.Visibility = Visibility.Collapsed;
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Background = Brushes.Transparent;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (grownPx > 0 && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right - r.Left, Math.Max(70, r.Bottom - r.Top - grownPx), SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
        grownPx = 0;
        SaveBounds();
        Update();
    }

    private void SaveBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
        {
            def.X = r.Left;
            def.Y = r.Top;
            def.Width = r.Right - r.Left;
            def.Height = Math.Max(70, r.Bottom - r.Top - grownPx);
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
            const int edge = 8;
            bool left = x < r.Left + edge, right = x >= r.Right - edge, top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
            int hit = top && left ? HTTOPLEFT : top && right ? HTTOPRIGHT : bottom && left ? HTBOTTOMLEFT : bottom && right ? HTBOTTOMRIGHT
                : left ? HTLEFT : right ? HTRIGHT : top ? HTTOP : bottom ? HTBOTTOM : 0;
            if (hit == 0)
            {
                // over the edit strip's controls: they get the click; anywhere else drags the widget
                var p = PointFromScreen(new System.Windows.Point(x, y));
                hit = InputHitTest(p) is DependencyObject d && IsInside(d, editStrip) && d is not Border ? HTCLIENT : HTCAPTION;
            }
            handled = true;
            return new IntPtr(hit);
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

    private const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232, WM_NCRBUTTONUP = 0xA5;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
