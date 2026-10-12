using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Record → Add Camera: lays cameras over the picture. The canvas is the capture monitor at its own resolution (a
/// snapshot of it behind), each camera a live box on it: drag a box to move it, its corner handle to resize it; the
/// list on the left adds and removes cameras and sets each one's rounded corners and mirroring. Save keeps the layout,
/// which recordings and the virtual webcam then draw (<see cref="GamingSettings.Cameras"/>).
/// </summary>
internal sealed class CameraLayoutWindow : Window
{
    private sealed class Box
    {
        public required CameraPlacement Place;
        public required CameraFeed Feed;
        public required Grid Element;
        public required Image Picture;
        public required Border Outline;
        public required RectangleGeometry Clip;
        public WriteableBitmap? Bitmap;
        public long Seen;
    }

    private readonly GamingSettings settings;
    private readonly int canvasW, canvasH;
    private readonly Canvas canvas = new();
    private readonly List<Box> boxes = new();
    private readonly ListBox list = new() { MinHeight = 120 };
    private readonly ComboBox addBox = new() { MinWidth = 180 };
    private readonly Button addButton = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly Slider cornerSlider = new() { Minimum = 0, Maximum = 50, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly TextBlock cornerText = new() { MinWidth = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox mirrorBox = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button removeButton = new() { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel selectedPanel = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    private readonly DispatcherTimer preview = new() { Interval = TimeSpan.FromMilliseconds(66) };
    private Box? selected;
    private bool loading;

    public CameraLayoutWindow(GamingSettings settings, MonitorInfo monitor)
    {
        this.settings = settings;
        canvasW = monitor.Bounds.Width;
        canvasH = monitor.Bounds.Height;
        Title = T("Cameras");
        Width = 1180;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        // the canvas: the monitor at its own resolution, scaled to fit the window
        canvas.Width = canvasW;
        canvas.Height = canvasH;
        canvas.Background = Brushes.Black;
        if (Snapshot(monitor.Bounds) is { } shot) canvas.Children.Add(new Image { Source = shot, Width = canvasW, Height = canvasH, Opacity = 0.75 });
        canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == canvas || e.OriginalSource is Image { Parent: Canvas }) Select(null); };
        var view = new Border { Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform }, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        view.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        // the side panel
        var side = new StackPanel { Width = 290, Margin = new Thickness(0, 0, 18, 0) };
        side.Children.Add(Header(T("Add a camera")));
        var addRow = new DockPanel();
        DockPanel.SetDock(addButton, Dock.Right);
        addButton.Content = T("Add");
        addButton.Click += (_, _) => AddSelected();
        addRow.Children.Add(addButton);
        addRow.Children.Add(addBox);
        side.Children.Add(addRow);
        side.Children.Add(Header(T("On the picture"), top: 18));
        list.SelectionChanged += (_, _) => { if (!loading) Select(boxes.FirstOrDefault(b => b.Place == (list.SelectedItem as ListBoxItem)?.Tag)); };
        side.Children.Add(list);

        var cornerRow = new DockPanel();
        DockPanel.SetDock(cornerText, Dock.Right);
        cornerRow.Children.Add(cornerText);
        cornerRow.Children.Add(cornerSlider);
        cornerSlider.ValueChanged += (_, _) =>
        {
            cornerText.Text = $"{cornerSlider.Value:0}%";
            if (loading || selected == null) return;
            selected.Place.Corner = cornerSlider.Value / 100;
            Layout(selected);
        };
        mirrorBox.Content = T("Mirror (flip left to right)");
        mirrorBox.Checked += (_, _) => SetMirror(true);
        mirrorBox.Unchecked += (_, _) => SetMirror(false);
        removeButton.Content = T("Remove this camera");
        removeButton.Click += (_, _) => Remove(selected);
        selectedPanel.Children.Add(Header(T("Rounded corners")));
        selectedPanel.Children.Add(cornerRow);
        selectedPanel.Children.Add(mirrorBox);
        selectedPanel.Children.Add(removeButton);
        side.Children.Add(selectedPanel);
        var help = new TextBlock
        {
            Text = F("Drag a camera to move it, and its corner to resize it. The picture is {0} at {1} × {2}; the cameras sit in the same place on a region or a game's window. Up to {3} cameras.", monitor.Label, canvasW, canvasH, CameraPlacement.Max),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0),
        };
        help.SetResourceReference(StyleProperty, "SecondaryText");
        side.Children.Add(help);
        statusText.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
        side.Children.Add(statusText);

        // buttons
        var save = new Button { Content = T("Save"), MinWidth = 90, IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += (_, _) => { Save(); DialogResult = true; };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(side, Dock.Left);
        root.Children.Add(buttons);
        root.Children.Add(side);
        root.Children.Add(view);
        Content = root;

        foreach (var c in CameraFeed.List()) addBox.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c });
        addBox.SelectedIndex = addBox.Items.Count > 0 ? 0 : -1;
        if (addBox.Items.Count == 0) addBox.Items.Add(new ComboBoxItem { Content = T("No cameras found"), IsEnabled = false });
        foreach (var p in settings.Cameras) AddBox(p.Clone());
        Select(boxes.FirstOrDefault());
        Refresh();

        preview.Tick += (_, _) => UpdatePreviews();
        preview.Start();
        Closed += (_, _) =>
        {
            preview.Stop();
            foreach (var b in boxes) b.Feed.Dispose();
        };
    }

    private static TextBlock Header(string text, double top = 0) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 6) };

    // ---------------------------------------------------------------- cameras

    private void AddSelected()
    {
        if (boxes.Count >= CameraPlacement.Max || (addBox.SelectedItem as ComboBoxItem)?.Tag is not CameraDevice device) return;
        // a new one goes bottom-right (then cascading), 24% of the width, in a 16:9 shape until its picture says otherwise
        double w = 0.24, h = Math.Min(0.9, w * canvasW / canvasH * 9 / 16);
        double step = 0.03 * boxes.Count;
        var p = new CameraPlacement
        {
            DeviceId = device.Id, Name = device.Name, W = w, H = h,
            X = Math.Clamp(1 - w - 0.02 - step, 0, 1 - w), Y = Math.Clamp(1 - h - 0.03 - step, 0, 1 - h),
        };
        Select(AddBox(p));
        Refresh();
    }

    private Box AddBox(CameraPlacement p)
    {
        var picture = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var clip = new RectangleGeometry();
        var pictureHost = new Grid { Clip = clip, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)) };
        var label = new TextBlock { Text = p.Name, Foreground = Brushes.White, FontSize = Math.Max(14, canvasH / 40.0), Margin = new Thickness(canvasH / 60.0), VerticalAlignment = VerticalAlignment.Bottom };
        pictureHost.Children.Add(label);
        pictureHost.Children.Add(picture);
        var outline = new Border { BorderThickness = new Thickness(Math.Max(2, canvasW / 640.0)), IsHitTestVisible = false };
        double handleSize = Math.Max(14, canvasW / 90.0);
        var handle = new Thumb { Width = handleSize, Height = handleSize, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE };
        handle.Template = HandleTemplate();
        var element = new Grid { Cursor = Cursors.SizeAll };
        element.Children.Add(pictureHost);
        element.Children.Add(outline);
        element.Children.Add(handle);
        var box = new Box { Place = p, Feed = CameraFeed.Open(p.DeviceId, p.Name), Element = element, Picture = picture, Outline = outline, Clip = clip };
        boxes.Add(box);
        canvas.Children.Add(element);
        HookMove(box);
        handle.DragDelta += (_, e) =>
        {
            p.W = Math.Clamp(p.W + e.HorizontalChange / canvasW, 0.04, 1 - p.X);
            p.H = Math.Clamp(p.H + e.VerticalChange / canvasH, 0.04, 1 - p.Y);
            Layout(box);
        };
        handle.PreviewMouseLeftButtonDown += (_, _) => Select(box);
        Layout(box);
        return box;
    }

    private static ControlTemplate HandleTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.BackgroundProperty, Brushes.White);
        f.SetValue(Border.BorderBrushProperty, Brushes.Black);
        f.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        t.VisualTree = f;
        return t;
    }

    private void HookMove(Box box)
    {
        Point start = default;
        double x0 = 0, y0 = 0;
        bool dragging = false;
        box.Element.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindThumb(d)) return;
            Select(box);
            start = e.GetPosition(canvas);
            (x0, y0) = (box.Place.X, box.Place.Y);
            dragging = box.Element.CaptureMouse();
            e.Handled = true;
        };
        box.Element.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var at = e.GetPosition(canvas);
            box.Place.X = Math.Clamp(x0 + (at.X - start.X) / canvasW, 0, 1 - box.Place.W);
            box.Place.Y = Math.Clamp(y0 + (at.Y - start.Y) / canvasH, 0, 1 - box.Place.H);
            Layout(box);
        };
        box.Element.MouseLeftButtonUp += (_, _) => { dragging = false; box.Element.ReleaseMouseCapture(); };
        box.Element.LostMouseCapture += (_, _) => dragging = false;
    }

    private static bool FindThumb(DependencyObject d)
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d)) if (d is Thumb) return true;
        return false;
    }

    /// <summary>Puts a box where its placement says, with its rounded corners and mirroring.</summary>
    private void Layout(Box b)
    {
        var p = b.Place;
        double w = p.W * canvasW, h = p.H * canvasH, r = Math.Clamp(p.Corner, 0, 0.5) * Math.Min(w, h);
        b.Element.Width = w;
        b.Element.Height = h;
        Canvas.SetLeft(b.Element, p.X * canvasW);
        Canvas.SetTop(b.Element, p.Y * canvasH);
        b.Clip.Rect = new Rect(0, 0, w, h);
        b.Clip.RadiusX = b.Clip.RadiusY = r;
        b.Outline.CornerRadius = new CornerRadius(r);
        b.Picture.RenderTransformOrigin = new Point(0.5, 0.5);
        b.Picture.RenderTransform = p.Mirror ? new ScaleTransform(-1, 1) : Transform.Identity;
    }

    private void Select(Box? box)
    {
        selected = box;
        foreach (var b in boxes)
        {
            if (b == box) b.Outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else b.Outline.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            Panel.SetZIndex(b.Element, b == box ? 2 : 1);
        }
        loading = true;
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == box?.Place);
        selectedPanel.Visibility = box == null ? Visibility.Collapsed : Visibility.Visible;
        if (box != null)
        {
            cornerSlider.Value = Math.Round(box.Place.Corner * 100);
            cornerText.Text = $"{cornerSlider.Value:0}%";
            mirrorBox.IsChecked = box.Place.Mirror;
        }
        loading = false;
    }

    private void SetMirror(bool on)
    {
        if (loading || selected == null) return;
        selected.Place.Mirror = on;
        Layout(selected);
    }

    private void Remove(Box? box)
    {
        if (box == null) return;
        boxes.Remove(box);
        canvas.Children.Remove(box.Element);
        box.Feed.Dispose();
        Select(boxes.LastOrDefault());
        Refresh();
    }

    private void Refresh()
    {
        loading = true;
        list.Items.Clear();
        foreach (var b in boxes) list.Items.Add(new ListBoxItem { Content = b.Place.Name, Tag = b.Place });
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == selected?.Place);
        loading = false;
        addButton.IsEnabled = boxes.Count < CameraPlacement.Max && addBox.SelectedItem is ComboBoxItem { Tag: CameraDevice };
        if (boxes.Count == 0) selectedPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Each box's newest picture (about 15 a second); a camera that can't send says why.</summary>
    private void UpdatePreviews()
    {
        var problems = new List<string>();
        foreach (var b in boxes)
        {
            b.Seen = b.Feed.ReadLatest(b.Seen, (bgra, w, h) =>
            {
                if (b.Bitmap == null || b.Bitmap.PixelWidth != w || b.Bitmap.PixelHeight != h)
                {
                    b.Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                    b.Picture.Source = b.Bitmap;
                }
                b.Bitmap.WritePixels(new Int32Rect(0, 0, w, h), bgra, w * 4, 0);
            });
            if (b.Feed.Error is { } e) problems.Add($"{b.Place.Name}: {e}");
        }
        statusText.Text = string.Join("\n", problems);
    }

    private void Save()
    {
        settings.Cameras = boxes.Select(b => b.Place.Clone()).ToList();
        settings.Save();
    }

    // ---------------------------------------------------------------- the monitor's snapshot

    /// <summary>What's on the monitor now, to place the cameras against (null if Windows won't give it).</summary>
    private static BitmapSource? Snapshot(PixelRect r)
    {
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, r.Width, r.Height);
            old = SelectObject(mem, bmp);
            if (!BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020)) return null; // SRCCOPY
            SelectObject(mem, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
