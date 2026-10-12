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
/// Record → Add Camera: lays cameras over the picture. The canvas is the recorded picture: the capture monitor at its
/// own resolution, in the output's shape (<see cref="GamingSettings.OutputAspect"/>; a snapshot of it behind), each
/// camera a live box on it: drag a box to move it; drag a corner or side handle to resize it (keeping its shape while
/// Lock aspect is on), or hold Alt and drag one to crop the camera's picture at that edge (the picture stays put and the
/// box trims to it). Right-click a camera to turn it, flip it, or unlock its shape. The list on the left adds and removes
/// cameras and sets each one's rounded corners, mirroring and crop. Save keeps the layout, which recordings and the
/// virtual webcam then draw (<see cref="GamingSettings.Cameras"/>).
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
        /// <summary>The camera's own picture's shape (width / height), once it has sent one.</summary>
        public double CameraAspect;
        /// <summary>Just added: its box takes the picture's shape when the first one comes.</summary>
        public bool FitToPicture;
        /// <summary>A locked-shape resize: the placement and the pointer when it began.</summary>
        public CameraPlacement? DragFrom;
        public Point DragMouse;
        public byte[] Turned = [];
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
    private readonly Border viewHost;
    /// <summary>Lines showing where a box snapped to (an edge of the picture or of another camera).</summary>
    private readonly System.Windows.Shapes.Rectangle guideX = Guide(), guideY = Guide();

    public CameraLayoutWindow(GamingSettings settings, MonitorInfo monitor)
    {
        this.settings = settings;
        // the picture as it's recorded: the monitor, or the middle of it in the output's shape
        var area = OutputAspects.Fit(new PixelRect(0, 0, monitor.Bounds.Width, monitor.Bounds.Height), settings.OutputAspect);
        canvasW = area.Width;
        canvasH = area.Height;
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
        if (Snapshot(new PixelRect(monitor.Bounds.X + area.X, monitor.Bounds.Y + area.Y, area.Width, area.Height)) is { } shot) canvas.Children.Add(new Image { Source = shot, Width = canvasW, Height = canvasH, Opacity = 0.75 });
        canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == canvas || e.OriginalSource is Image { Parent: Canvas }) Select(null); };
        var view = viewHost = new Border { Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform }, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        canvas.Children.Add(guideX);
        canvas.Children.Add(guideY);
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
            Text = F("Drag a camera to move it, and a corner or side to resize it: it keeps its shape unless you right-click it and untick Lock aspect (right-click also turns and flips it). Edges snap to the picture's edges and to the other cameras, and a resize snaps to another camera's height or width. Hold Alt and drag a corner or side to crop the camera's picture there; Alt and drag back out to uncrop. The picture is {0} at {1} × {2}, {3} (Shape, under Record); the cameras sit in the same place on a region or a game's window. Up to {4} cameras.",
                monitor.Label, canvasW, canvasH, OutputAspects.Label(settings.OutputAspect), CameraPlacement.Max),
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
        var box = AddBox(p);
        box.FitToPicture = true;
        Select(box);
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
            handle.DragStarted += (_, _) => { box.DragFrom = box.Place.Clone(); box.DragMouse = Mouse.GetPosition(canvas); };
            handle.DragDelta += (_, e) =>
            {
                bool crop = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                if (!crop && box.DragFrom != null)
                {
                    var moved = Mouse.GetPosition(canvas) - box.DragMouse;
                    if (box.Place.LockAspect) ResizeLocked(box, edges, moved);
                    else ResizeFree(box, edges, moved);
                }
                else
                {
                    DragEdge(box, edges, e.HorizontalChange, e.VerticalChange, crop);
                    box.DragFrom = box.Place.Clone(); // a resize after this goes on from here
                    box.DragMouse = Mouse.GetPosition(canvas);
                }
            };
            handle.DragCompleted += (_, _) => ShowGuides(null, null);
            handle.PreviewMouseLeftButtonDown += (_, _) => Select(box);
            element.Children.Add(handle);
        }
        element.ContextMenu = new ContextMenu();
        element.ContextMenu.Opened += (_, _) => FillMenu(box);
        element.MouseRightButtonDown += (_, _) => Select(box);
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

    /// <summary>
    /// A resize that keeps the box's shape: from where the drag began, by the pointer's whole movement. A corner scales by
    /// whichever way moved further, with the opposite corner staying put; a side sets that way and the other follows,
    /// centred. It stops at the canvas's edge.
    /// </summary>
    private void ResizeLocked(Box b, Edge edges, Vector delta)
    {
        var s = b.DragFrom!;
        var p = b.Place;
        double x0 = s.X * canvasW, y0 = s.Y * canvasH, w0 = s.W * canvasW, h0 = s.H * canvasH, aspect = w0 / Math.Max(1, h0);
        bool left = edges.HasFlag(Edge.Left), right = edges.HasFlag(Edge.Right), top = edges.HasFlag(Edge.Top), bottom = edges.HasFlag(Edge.Bottom);
        double w = w0 + (left ? -delta.X : right ? delta.X : 0), h = h0 + (top ? -delta.Y : bottom ? delta.Y : 0);
        double nw, nh;
        if ((left || right) && (top || bottom))
        {
            double sx = w / w0, sy = h / h0, k = Math.Abs(sx - 1) > Math.Abs(sy - 1) ? sx : sy;
            nw = w0 * k;
            nh = h0 * k;
        }
        else if (left || right) { nw = w; nh = w / aspect; }
        else { nh = h; nw = h * aspect; }
        // what stays put: the opposite edge, or the middle
        double ax = left ? x0 + w0 : right ? x0 : x0 + w0 / 2, ay = top ? y0 + h0 : bottom ? y0 : y0 + h0 / 2;
        // snapped: the same height or width as another camera, or a moving edge onto an edge (the shape stays)
        double? gx = null, gy = null;
        {
            double best = SnapDistance, bw = nw, bh = nh;
            double? bgx = null, bgy = null;
            void Try(double cw2, double ch2, double? lx, double? ly)
            {
                double d = Math.Min(Math.Abs(cw2 - nw), Math.Abs(ch2 - nh));
                if (d < best && cw2 > 1 && ch2 > 1) { best = d; bw = cw2; bh = ch2; bgx = lx; bgy = ly; }
            }
            foreach (var o in boxes.Where(o => o != b))
            {
                double ow = o.Place.W * canvasW, oh = o.Place.H * canvasH;
                Try(oh * aspect, oh, null, top ? o.Place.Y * canvasH : bottom ? (o.Place.Y + o.Place.H) * canvasH : null);
                Try(ow, ow / aspect, left ? o.Place.X * canvasW : right ? (o.Place.X + o.Place.W) * canvasW : null, null);
            }
            if (left || right)
                foreach (double t in XTargets(b))
                {
                    double cw2 = left ? ax - t : t - ax;
                    if (Math.Abs(cw2 - nw) < best && cw2 > 1) { best = Math.Abs(cw2 - nw); bw = cw2; bh = cw2 / aspect; bgx = t; bgy = null; }
                }
            if (top || bottom)
                foreach (double t in YTargets(b))
                {
                    double ch2 = top ? ay - t : t - ay;
                    if (Math.Abs(ch2 - nh) < best && ch2 > 1) { best = Math.Abs(ch2 - nh); bh = ch2; bw = ch2 * aspect; bgy = t; bgx = null; }
                }
            nw = bw;
            nh = bh;
            gx = bgx;
            gy = bgy;
        }
        double maxW = left ? ax : right ? canvasW - ax : 2 * Math.Min(ax, canvasW - ax);
        double maxH = top ? ay : bottom ? canvasH - ay : 2 * Math.Min(ay, canvasH - ay);
        double fit = Math.Min(1, Math.Min(maxW / Math.Max(1, nw), maxH / Math.Max(1, nh)));
        nw *= fit;
        nh *= fit;
        double minW = 0.04 * canvasW, minH = 0.04 * canvasH;
        if (nw < minW || nh < minH)
        {
            double up = Math.Max(minW / Math.Max(1, nw), minH / Math.Max(1, nh));
            nw *= up;
            nh *= up;
        }
        double nx = left ? ax - nw : right ? ax : ax - nw / 2, ny = top ? ay - nh : bottom ? ay : ay - nh / 2;
        p.W = Math.Clamp(nw / canvasW, 0.04, 1);
        p.H = Math.Clamp(nh / canvasH, 0.04, 1);
        p.X = Math.Clamp(nx / canvasW, 0, 1 - p.W);
        p.Y = Math.Clamp(ny / canvasH, 0, 1 - p.H);
        ShowGuides(gx, gy);
        Layout(b);
    }

    /// <summary>
    /// A resize with the shape free (Lock aspect off): the dragged edges follow the pointer from where the drag began,
    /// snapping onto an edge of the picture or of another camera, or to another camera's width or height.
    /// </summary>
    private void ResizeFree(Box b, Edge edges, Vector delta)
    {
        var s = b.DragFrom!;
        var p = b.Place;
        double l = s.X * canvasW, t = s.Y * canvasH, r = l + s.W * canvasW, bt = t + s.H * canvasH;
        double minW = 0.04 * canvasW, minH = 0.04 * canvasH;
        bool left = edges.HasFlag(Edge.Left), right = edges.HasFlag(Edge.Right), top = edges.HasFlag(Edge.Top), bottom = edges.HasFlag(Edge.Bottom);
        if (left) l = Math.Clamp(l + delta.X, 0, r - minW);
        if (right) r = Math.Clamp(r + delta.X, l + minW, canvasW);
        if (top) t = Math.Clamp(t + delta.Y, 0, bt - minH);
        if (bottom) bt = Math.Clamp(bt + delta.Y, t + minH, canvasH);
        double? gx = null, gy = null;
        if (left || right)
        {
            double edge = left ? l : r, best = SnapDistance, to = edge;
            foreach (double x in XTargets(b))
                if (Math.Abs(x - edge) < best) { best = Math.Abs(x - edge); to = x; gx = x; }
            foreach (var o in boxes.Where(o => o != b))
            {
                double x = left ? r - o.Place.W * canvasW : l + o.Place.W * canvasW; // the same width
                if (Math.Abs(x - edge) < best) { best = Math.Abs(x - edge); to = x; gx = null; }
            }
            if (left) l = Math.Clamp(to, 0, r - minW); else r = Math.Clamp(to, l + minW, canvasW);
        }
        if (top || bottom)
        {
            double edge = top ? t : bt, best = SnapDistance, to = edge;
            foreach (double y in YTargets(b))
                if (Math.Abs(y - edge) < best) { best = Math.Abs(y - edge); to = y; gy = y; }
            foreach (var o in boxes.Where(o => o != b))
            {
                double y = top ? bt - o.Place.H * canvasH : t + o.Place.H * canvasH; // the same height
                if (Math.Abs(y - edge) < best) { best = Math.Abs(y - edge); to = y; gy = null; }
            }
            if (top) t = Math.Clamp(to, 0, bt - minH); else bt = Math.Clamp(to, t + minH, canvasH);
        }
        p.X = l / canvasW;
        p.Y = t / canvasH;
        p.W = (r - l) / canvasW;
        p.H = (bt - t) / canvasH;
        ShowGuides(gx, gy);
        Layout(b);
    }

    // ---------------------------------------------------------------- snapping

    /// <summary>How close (in canvas pixels: about 10 screen pixels) an edge or size has to come to snap.</summary>
    private double SnapDistance => 10 / Math.Max(0.02, Math.Min(viewHost.ActualWidth / canvasW, viewHost.ActualHeight / canvasH));

    /// <summary>The picture's left and right edges and every other camera's.</summary>
    private double[] XTargets(Box b) =>
        [0, canvasW, .. boxes.Where(o => o != b).SelectMany(o => new[] { o.Place.X * canvasW, (o.Place.X + o.Place.W) * canvasW })];

    private double[] YTargets(Box b) =>
        [0, canvasH, .. boxes.Where(o => o != b).SelectMany(o => new[] { o.Place.Y * canvasH, (o.Place.Y + o.Place.H) * canvasH })];

    /// <summary>The smallest move that puts one of <paramref name="mine"/> onto a target, if one is near enough; and where.</summary>
    private (double Delta, double? At) Nearest(double[] mine, double[] targets)
    {
        double best = SnapDistance, delta = 0;
        double? at = null;
        foreach (double m in mine)
            foreach (double t in targets)
                if (Math.Abs(t - m) < best) { best = Math.Abs(t - m); delta = t - m; at = t; }
        return (delta, at);
    }

    private static System.Windows.Shapes.Rectangle Guide()
    {
        var g = new System.Windows.Shapes.Rectangle { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        g.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
        Panel.SetZIndex(g, 10);
        return g;
    }

    /// <summary>Shows a line along the edge snapped to (null hides it).</summary>
    private void ShowGuides(double? x, double? y)
    {
        double thick = Math.Max(2, canvasW / 700.0);
        guideX.Visibility = x != null ? Visibility.Visible : Visibility.Collapsed;
        guideY.Visibility = y != null ? Visibility.Visible : Visibility.Collapsed;
        if (x is double gx) { guideX.Width = thick; guideX.Height = canvasH; Canvas.SetLeft(guideX, Math.Clamp(gx - thick / 2, 0, canvasW - thick)); Canvas.SetTop(guideX, 0); }
        if (y is double gy) { guideY.Width = canvasW; guideY.Height = thick; Canvas.SetLeft(guideY, 0); Canvas.SetTop(guideY, Math.Clamp(gy - thick / 2, 0, canvasH - thick)); }
    }

    /// <summary>The right-click menu: Lock aspect, the turns, the flips, the crop and Remove, as they are now.</summary>
    private void FillMenu(Box b)
    {
        var menu = b.Element.ContextMenu!;
        menu.Items.Clear();
        var p = b.Place;
        MenuItem Item(string text, bool check, Action click, bool enabled = true)
        {
            var m = new MenuItem { Header = text, IsCheckable = false, IsChecked = check, IsEnabled = enabled };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
            return m;
        }
        Item(T("Lock aspect"), p.LockAspect, () => p.LockAspect = !p.LockAspect);
        menu.Items.Add(new Separator());
        foreach (int deg in new[] { 0, 90, 180, 270 })
            Item(deg == 0 ? T("No rotation") : F("Rotate {0}°", deg), p.QuarterTurns * 90 == deg, () => SetRotation(b, deg));
        menu.Items.Add(new Separator());
        Item(T("Flip horizontal"), p.Mirror, () => { Select(b); SetMirror(!p.Mirror); mirrorBox.IsChecked = p.Mirror; });
        Item(T("Flip vertical"), p.FlipVertical, () => SetFlipVertical(b, !p.FlipVertical));
        menu.Items.Add(new Separator());
        Item(T("Reset the crop"), false, () => ResetCrop(b), p.IsCropped);
        Item(T("Remove this camera"), false, () => Remove(b));
    }

    /// <summary>Turns the camera; the crop starts over and the box takes the turned picture's shape (about the same size, in the same place).</summary>
    private void SetRotation(Box b, int degrees)
    {
        var p = b.Place;
        if (p.QuarterTurns * 90 == degrees) return;
        bool swap = (p.QuarterTurns + degrees / 90) % 2 == 1;
        p.Rotation = degrees;
        p.CropLeft = p.CropTop = p.CropRight = p.CropBottom = 0;
        double aspect = b.CameraAspect > 0 ? p.SeenAspect(b.CameraAspect) : swap ? p.H * canvasH / (p.W * canvasW) : p.W * canvasW / (p.H * canvasH);
        FitBox(b, aspect);
        b.Seen = 0; // redraw the preview turned
        resetCropButton.IsEnabled = false;
    }

    private void SetFlipVertical(Box b, bool on)
    {
        var p = b.Place;
        if (p.FlipVertical == on) return;
        p.FlipVertical = on;
        (p.CropTop, p.CropBottom) = (p.CropBottom, p.CropTop); // the same part of the camera stays cut off
        b.Seen = 0;
        Layout(b);
    }

    /// <summary>Gives the box a shape (width / height of what it shows), keeping its area and middle, inside the canvas.</summary>
    private void FitBox(Box b, double aspect)
    {
        var p = b.Place;
        double w = p.W * canvasW, h = p.H * canvasH, area = w * h, cx = p.X * canvasW + w / 2, cy = p.Y * canvasH + h / 2;
        double nw = Math.Sqrt(area * aspect), nh = nw / aspect;
        double fit = Math.Min(1, Math.Min(canvasW * 0.95 / nw, canvasH * 0.95 / nh));
        nw *= fit;
        nh *= fit;
        p.W = Math.Clamp(nw / canvasW, 0.04, 1);
        p.H = Math.Clamp(nh / canvasH, 0.04, 1);
        p.X = Math.Clamp((cx - nw / 2) / canvasW, 0, 1 - p.W);
        p.Y = Math.Clamp((cy - nh / 2) / canvasH, 0, 1 - p.H);
        Layout(b);
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
            // snapped: any edge near an edge of the picture or of another camera goes onto it
            double w = box.Place.W * canvasW, h = box.Place.H * canvasH;
            double x = (x0 + (at.X - start.X) / canvasW) * canvasW, y = (y0 + (at.Y - start.Y) / canvasH) * canvasH;
            var (dx, gx) = Nearest([x, x + w], XTargets(box));
            var (dy, gy) = Nearest([y, y + h], YTargets(box));
            box.Place.X = Math.Clamp((x + dx) / canvasW, 0, 1 - box.Place.W);
            box.Place.Y = Math.Clamp((y + dy) / canvasH, 0, 1 - box.Place.H);
            ShowGuides(gx, gy);
            Layout(box);
        };
        box.Element.MouseLeftButtonUp += (_, _) => { dragging = false; box.Element.ReleaseMouseCapture(); ShowGuides(null, null); };
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
        // the preview's picture is already turned and flipped (see UpdatePreviews), so the crop is where it's seen
        double keptW = Math.Max(CameraPlacement.MinVisible, 1 - p.CropLeft - p.CropRight), keptH = Math.Max(CameraPlacement.MinVisible, 1 - p.CropTop - p.CropBottom);
        b.Picture.Viewbox = new Rect(p.CropLeft, p.CropTop, keptW, keptH);
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
        if (loading || selected == null || selected.Place.Mirror == on) return;
        var p = selected.Place;
        p.Mirror = on;
        (p.CropLeft, p.CropRight) = (p.CropRight, p.CropLeft); // the same part of the camera stays cut off
        selected.Seen = 0;
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
                var p = b.Place;
                b.CameraAspect = (double)w / Math.Max(1, h);
                var (pixels, ow, oh) = Turn(bgra, w, h, p.QuarterTurns, p.Mirror, p.FlipVertical, ref b.Turned);
                if (b.Bitmap == null || b.Bitmap.PixelWidth != ow || b.Bitmap.PixelHeight != oh)
                {
                    b.Bitmap = new WriteableBitmap(ow, oh, 96, 96, PixelFormats.Bgr32, null);
                    b.Picture.ImageSource = b.Bitmap;
                }
                b.Bitmap.WritePixels(new Int32Rect(0, 0, ow, oh), pixels, ow * 4, 0);
                if (b.FitToPicture)
                {
                    // a new camera's box takes its picture's shape
                    b.FitToPicture = false;
                    double keptW = 1 - p.CropLeft - p.CropRight, keptH = 1 - p.CropTop - p.CropBottom;
                    FitBox(b, (double)ow / oh * keptW / keptH);
                }
            });
            if (b.Feed.Error is { } e) problems.Add($"{b.Place.Name}: {e}");
        }
        statusText.Text = string.Join("\n", problems);
    }

    /// <summary>
    /// The camera's picture as it's shown: turned clockwise by quarter turns, then flipped. The same as the recording's
    /// shader does, so the crop (which is where it's seen) means the same in both.
    /// </summary>
    private static (byte[] Pixels, int W, int H) Turn(byte[] bgra, int w, int h, int turns, bool flipH, bool flipV, ref byte[] buffer)
    {
        if (turns == 0 && !flipH && !flipV) return (bgra, w, h);
        int ow = turns % 2 == 1 ? h : w, oh = turns % 2 == 1 ? w : h;
        if (buffer.Length != bgra.Length) buffer = new byte[bgra.Length];
        var src = MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
        var dst = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
        for (int sy = 0; sy < oh; sy++)
        {
            int ry = flipV ? oh - 1 - sy : sy;
            for (int sx = 0; sx < ow; sx++)
            {
                int rx = flipH ? ow - 1 - sx : sx;
                int cx, cy;
                switch (turns)
                {
                    case 1: cx = ry; cy = h - 1 - rx; break;
                    case 2: cx = w - 1 - rx; cy = h - 1 - ry; break;
                    case 3: cx = w - 1 - ry; cy = rx; break;
                    default: cx = rx; cy = ry; break;
                }
                dst[sy * ow + sx] = src[cy * w + cx];
            }
        }
        return (buffer, ow, oh);
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
