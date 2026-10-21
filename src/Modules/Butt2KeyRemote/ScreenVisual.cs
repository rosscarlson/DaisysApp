using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Draws a ButtScreen at its own size in pixels (put it in a Viewbox to fit it anywhere): its background and each button
/// where the grid puts it. The editor, the list's pictures and the pop-outs all use it, and a device draws it the same
/// way (see the README): a button's icon is 45% of its smaller side with a label under it, 55% without one.
/// </summary>
internal sealed class ScreenVisual : Canvas
{
    private readonly Dictionary<string, ButtVisual> visuals = new();

    public ButtScreen Screen { get; private set; }

    public ScreenVisual(ButtScreen screen)
    {
        Screen = screen;
        ClipToBounds = true;
        Render(screen);
    }

    public IEnumerable<ButtVisual> Buttons => visuals.Values;

    public ButtVisual? VisualOf(ButtButton b) => visuals.TryGetValue(b.Id, out var v) ? v : null;

    public void Render(ButtScreen screen)
    {
        Screen = screen;
        Children.Clear();
        visuals.Clear();
        Width = screen.Width;
        Height = screen.Height;
        Background = ColorText.Brush(screen.Background, Color.FromRgb(0x15, 0x18, 0x1C));
        foreach (var b in screen.Buttons)
        {
            var v = new ButtVisual(screen, b);
            visuals[b.Id] = v;
            Children.Add(v);
        }
    }

    /// <summary>Draws one button again (after an edit) without the rest.</summary>
    public void Refresh(ButtButton b)
    {
        if (visuals.TryGetValue(b.Id, out var old)) Children.Remove(old);
        var v = new ButtVisual(Screen, b);
        visuals[b.Id] = v;
        Children.Add(v);
    }

    /// <summary>The cell under a point (in the ButtScreen's pixels), or null between cells or outside the grid.</summary>
    public (int Column, int Row)? CellAt(Point p, bool nearest = false)
    {
        var s = Screen;
        double cw = s.CellWidth + s.Gap, ch = s.CellHeight + s.Gap;
        double x = (p.X - s.Gap) / cw, y = (p.Y - s.Gap) / ch;
        int c = (int)Math.Floor(x), r = (int)Math.Floor(y);
        if (nearest) return (Math.Clamp(c, 0, s.Columns - 1), Math.Clamp(r, 0, s.Rows - 1));
        if (c < 0 || r < 0 || c >= s.Columns || r >= s.Rows) return null;
        // in the gap after the cell
        if ((x - c) * cw > s.CellWidth || (y - r) * ch > s.CellHeight) return null;
        return (c, r);
    }
}

/// <summary>One button, drawn: its shape, outline, icon or picture, and label.</summary>
internal sealed class ButtVisual : Border
{
    private readonly Border pressedShade;

    public ButtButton Button { get; }

    public ButtVisual(ButtScreen screen, ButtButton b)
    {
        Button = b;
        var st = b.Style;
        var r = screen.RectOf(b);
        Canvas.SetLeft(this, r.X);
        Canvas.SetTop(this, r.Y);
        Width = r.Width;
        Height = r.Height;
        double radius = st.Rounded ? screen.CornerRadius : 0;
        CornerRadius = new CornerRadius(radius);
        Background = ColorText.Brush(st.Background, Color.FromRgb(0x2D, 0x35, 0x40));
        BorderBrush = ColorText.Brush(st.Outline, Colors.Transparent);
        BorderThickness = new Thickness(Math.Max(0, st.OutlineWidth));
        SnapsToDevicePixels = true;

        var grid = new Grid();
        double side = Math.Min(r.Width, r.Height);
        bool hasLabel = b.Label.Trim().Length > 0;
        FrameworkElement? picture = null;
        if (b.Image is { } image && System.IO.File.Exists(ScreenStore.ImagePath(image)))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(ScreenStore.ImagePath(image));
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                picture = new Image { Source = bmp, Stretch = Stretch.Uniform };
            }
            catch { }
        }
        else if (IconLibrary.GeometryOf(b.Icon) is { } g)
        {
            picture = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = new System.Windows.Shapes.Path { Data = g, Fill = ColorText.Brush(screen.IconColor, Colors.White), Width = 24, Height = 24, Stretch = Stretch.None },
            };
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (picture != null)
        {
            double size = side * (hasLabel ? 0.45 : 0.55);
            picture.Width = picture.Height = size;
            stack.Children.Add(picture);
        }
        if (hasLabel)
        {
            var label = new TextBlock
            {
                Text = b.Label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Max(4, st.FontSize),
                FontWeight = st.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = ColorText.Brush(st.FontColor, Colors.White),
                MaxWidth = Math.Max(1, r.Width - st.OutlineWidth * 2 - side * 0.08),
                Margin = new Thickness(0, picture != null ? side * 0.04 : 0, 0, 0),
            };
            try { label.FontFamily = new FontFamily(st.Font); } catch { }
            stack.Children.Add(label);
        }
        grid.Children.Add(stack);

        pressedShade = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), CornerRadius = new CornerRadius(Math.Max(0, radius - st.OutlineWidth)), Visibility = Visibility.Collapsed };
        grid.Children.Add(pressedShade);
        Child = grid;
    }

    /// <summary>Shows it pressed (lighter) or not.</summary>
    public bool Pressed
    {
        get => pressedShade.Visibility == Visibility.Visible;
        set => pressedShade.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
}
