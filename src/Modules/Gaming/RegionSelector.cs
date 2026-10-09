using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Dims every monitor; drag a rectangle on one of them to choose the region to record. Esc cancels. One window per
/// monitor, so each has its own DPI.
/// </summary>
internal static class RegionSelector
{
    public static void Pick(Action<PixelRect?> done)
    {
        var windows = new List<SelectorWindow>();
        bool finished = false;
        void Finish(PixelRect? result)
        {
            if (finished) return;
            finished = true;
            foreach (var w in windows) w.Close();
            done(result);
        }
        foreach (var m in Native.Monitors())
        {
            var w = new SelectorWindow(m.Bounds, Finish);
            windows.Add(w);
            w.Show();
        }
        windows.FirstOrDefault()?.Activate();
    }

    private sealed class SelectorWindow : Window
    {
        private readonly PixelRect monitor;
        private readonly Action<PixelRect?> finish;
        private readonly Canvas canvas = new();
        private readonly Rectangle box = new() { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Visibility = Visibility.Collapsed };
        private (int X, int Y)? start;

        public SelectorWindow(PixelRect monitor, Action<PixelRect?> finish)
        {
            this.monitor = monitor;
            this.finish = finish;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            var hint = new TextBlock
            {
                Text = T("Drag around the area to record. Esc cancels."),
                Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            };
            canvas.Children.Add(box);
            var grid = new Grid();
            grid.Children.Add(canvas);
            grid.Children.Add(hint);
            Content = grid;
            SourceInitialized += (_, _) => Native.SetBounds(this, monitor);
            MouseLeftButtonDown += (_, e) =>
            {
                start = Native.Cursor();
                CaptureMouse();
                box.Visibility = Visibility.Visible;
                e.Handled = true;
            };
            MouseMove += (_, _) =>
            {
                if (start is not { } s) return;
                var r = Current(s);
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Canvas.SetLeft(box, (r.X - monitor.X) / scale);
                Canvas.SetTop(box, (r.Y - monitor.Y) / scale);
                box.Width = r.Width / scale;
                box.Height = r.Height / scale;
            };
            MouseLeftButtonUp += (_, _) =>
            {
                if (start is not { } s) return;
                ReleaseMouseCapture();
                var r = Current(s);
                start = null;
                if (r.Width >= 16 && r.Height >= 16) finish(r);
                else box.Visibility = Visibility.Collapsed;
            };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) finish(null); };
        }

        /// <summary>The rectangle from the drag's start to the cursor, kept on this monitor.</summary>
        private PixelRect Current((int X, int Y) s)
        {
            var c = Native.Cursor();
            int x1 = Math.Clamp(Math.Min(s.X, c.X), monitor.X, monitor.Right), x2 = Math.Clamp(Math.Max(s.X, c.X), monitor.X, monitor.Right);
            int y1 = Math.Clamp(Math.Min(s.Y, c.Y), monitor.Y, monitor.Bottom), y2 = Math.Clamp(Math.Max(s.Y, c.Y), monitor.Y, monitor.Bottom);
            return PixelRect.FromLTRB(x1, y1, x2, y2);
        }
    }
}
