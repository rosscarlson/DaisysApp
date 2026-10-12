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
/// snapshot of it behind), each camera a live box on it: drag a box to move it; drag a corner or side handle to resize
/// it, or hold Alt and drag one to crop the camera's picture at that edge (the picture stays put and the box trims to
/// it). The list on the left adds and removes cameras and sets each one's rounded corners, mirroring and crop. Save keeps
/// the layout, which recordings and the virtual webcam then draw (<see cref="GamingSettings.Cameras"/>).
/// </summary>
internal sealed class CameraLayoutWindow : Window
{
    private sealed class Box
    {
        public required CameraPlacement Place;
        public required CameraFeed Feed;
        public required Grid Element;
        public required ImageBrush Picture;
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
    private readonly Button resetCropButton = new() { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
        resetCropButton.Content = T("Reset the crop");
        resetCropButton.Click += (_, _) => ResetCrop(selected);
        selectedPanel.Children.Add(Header(T("Rounded corners")));
        selectedPanel.Children.Add(cornerRow);
        selectedPanel.Children.Add(mirrorBox);
        selectedPanel.Children.Add(resetCropButton);
        selectedPanel.Children.Add(removeButton);
        side.Children.Add(selectedPanel);
        var help = new TextBlock
        {
            Text = F("Drag a camera to move it, and a corner or side to resize it. Hold Alt and drag a corner or side to crop the camera's picture there; Alt and drag back out to uncrop. The picture is {0} at {1} × {2}; the cameras sit in the same place on a region or a game's window. Up to {3} cameras.", monitor.Label, canvasW, canvasH, CameraPlacement.Max),
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
        // painted like the recording draws it: the kept part of the picture, cropped further to fill the box (not squashed)
        var picture = new ImageBrush { Stretch = Stretch.UniformToFill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
        var clip = new RectangleGeometry();
        var pictureHost = new Grid { Clip = clip, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)) };
        var label = new TextBlock { Text = p.Name, Foreground = Brushes.White, FontSize = Math.Max(14, canvasH / 40.0), Margin = new Thickness(canvasH / 60.0), VerticalAlignment = VerticalAlignment.Bottom };
        pictureHost.Children.Add(label);
        pictureHost.Children.Add(new System.Windows.Shapes.Rectangle { Fill = picture });
        var outline = new Border { BorderThickness = new Thickness(Math.Max(2, canvasW / 640.0)), IsHitTestVisible = false };
        var element = new Grid { Cursor = Cursors.SizeAll };
        element.Children.Add(pictureHost);
        element.Children.Add(outline);
        var box = new Box { Place = p, Feed = CameraFeed.Open(p.DeviceId, p.Name), Element = element, Picture = picture, Outline = outline, Clip = clip };
        // a handle on each corner and side: drag to resize from there, Alt + drag to crop there
        double handleSize = Math.Max(14, canvasW / 90.0);
        foreach (var edges in new[] { Edge.Left | Edge.Top, Edge.Right | Edge.Top, Edge.Left | Edge.Bottom, Edge.Right | Edge.Bottom, Edge.Left, Edge.Right, Edge.Top, Edge.Bottom })
        {
            var handle = new Thumb
            {
                Width = handleSize, Height = handleSize, Template = HandleTemplate(),
                HorizontalAlignment = edges.HasFlag(Edge.Left) ? HorizontalAlignment.Left : edges.HasFlag(Edge.Right) ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = edges.HasFlag(Edge.Top) ? VerticalAlignment.Top : edges.HasFlag(Edge.Bottom) ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                Cursor = edges is Edge.Left or Edge.Right ? Cursors.SizeWE : edges is Edge.Top or Edge.Bottom ? Cursors.SizeNS
                    : edges is (Edge.Left | Edge.Top) or (Edge.Right | Edge.Bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW,
                ToolTip = T("Drag to resize; hold Alt and drag to crop"),
            };
            handle.DragDelta += (_, e) => DragEdge(box, edges, e.HorizontalChange, e.VerticalChange, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            handle.PreviewMouseLeftButtonDown += (_, _) => Select(box);
            element.Children.Add(handle);
        }
        boxes.Add(box);
        canvas.Children.Add(element);
        HookMove(box);
        Layout(box);
        return box;
    }

    [Flags]
    private enum Edge { Left = 1, Top = 2, Right = 4, Bottom = 8 }

    /// <summary>
    /// A handle dragged by <paramref name="dx"/>, <paramref name="dy"/> (canvas pixels). Without Alt: that edge (or both,
    /// at a corner) moves, resizing the box. With Alt: the camera's picture is cropped at that edge by as much as the edge
    /// moved, and the box trims to it, so the picture itself stays where it was; dragging back out uncrops.
    /// </summary>
    private void DragEdge(Box b, Edge edges, double dx, double dy, bool crop)
    {
        var p = b.Place;
        double bw = p.W * canvasW, bh = p.H * canvasH;
        // how much of the picture one pixel of the box shows, each way
        double perX = (1 - p.CropLeft - p.CropRight) / bw, perY = (1 - p.CropTop - p.CropBottom) / bh;
        const double min = 0.04;
        if (edges.HasFlag(Edge.Left))
        {
            double move = Math.Clamp(dx, -p.X * canvasW, bw - min * canvasW); // the edge's movement, inward positive
            if (crop)
            {
                double c = Math.Clamp(p.CropLeft + move * perX, 0, 1 - CameraPlacement.MinVisible - p.CropRight);
                move = (c - p.CropLeft) / perX;
                p.CropLeft = c;
            }
            p.X += move / canvasW;
            p.W -= move / canvasW;
        }
        if (edges.HasFlag(Edge.Right))
        {
            double move = Math.Clamp(-dx, -(1 - p.X - p.W) * canvasW, bw - min * canvasW);
            if (crop)
            {
                double c = Math.Clamp(p.CropRight + move * perX, 0, 1 - CameraPlacement.MinVisible - p.CropLeft);
                move = (c - p.CropRight) / perX;
                p.CropRight = c;
            }
            p.W -= move / canvasW;
        }
        if (edges.HasFlag(Edge.Top))
        {
            double move = Math.Clamp(dy, -p.Y * canvasH, bh - min * canvasH);
            if (crop)
            {
                double c = Math.Clamp(p.CropTop + move * perY, 0, 1 - CameraPlacement.MinVisible - p.CropBottom);
                move = (c - p.CropTop) / perY;
                p.CropTop = c;
            }
            p.Y += move / canvasH;
            p.H -= move / canvasH;
        }
        if (edges.HasFlag(Edge.Bottom))
        {
            double move = Math.Clamp(-dy, -(1 - p.Y - p.H) * canvasH, bh - min * canvasH);
            if (crop)
            {
                double c = Math.Clamp(p.CropBottom + move * perY, 0, 1 - CameraPlacement.MinVisible - p.CropTop);
                move = (c - p.CropBottom) / perY;
                p.CropBottom = c;
            }
            p.H -= move / canvasH;
        }
        p.X = Math.Clamp(p.X, 0, 1 - min);
        p.Y = Math.Clamp(p.Y, 0, 1 - min);
        p.W = Math.Clamp(p.W, min, 1 - p.X);
        p.H = Math.Clamp(p.H, min, 1 - p.Y);
        Layout(b);
        resetCropButton.IsEnabled = selected?.Place.IsCropped == true;
    }

    /// <summary>Puts the whole picture back, the box growing by as much as the crop took (as far as the canvas allows).</summary>
    private void ResetCrop(Box? b)
    {
        if (b == null || !b.Place.IsCropped) return;
        var p = b.Place;
        double keptW = 1 - p.CropLeft - p.CropRight, keptH = 1 - p.CropTop - p.CropBottom;
        double fullW = p.W / keptW, fullH = p.H / keptH;
        p.X = Math.Clamp(p.X - p.CropLeft * fullW, 0, 1);
        p.Y = Math.Clamp(p.Y - p.CropTop * fullH, 0, 1);
        p.W = Math.Clamp(fullW, 0.04, 1 - p.X);
        p.H = Math.Clamp(fullH, 0.04, 1 - p.Y);
        p.CropLeft = p.CropTop = p.CropRight = p.CropBottom = 0;
        Layout(b);
        resetCropButton.IsEnabled = false;
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
        // the crop is as it's seen: mirrored, the picture's left edge is the camera's right
        double keptW = Math.Max(CameraPlacement.MinVisible, 1 - p.CropLeft - p.CropRight), keptH = Math.Max(CameraPlacement.MinVisible, 1 - p.CropTop - p.CropBottom);
        b.Picture.Viewbox = new Rect(p.Mirror ? p.CropRight : p.CropLeft, p.CropTop, keptW, keptH);
        b.Picture.RelativeTransform = p.Mirror ? new ScaleTransform(-1, 1, 0.5, 0.5) : Transform.Identity;
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
            resetCropButton.IsEnabled = box.Place.IsCropped;
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
                    b.Picture.ImageSource = b.Bitmap;
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
