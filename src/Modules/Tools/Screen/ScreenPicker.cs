using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Black screens' monitors: the desktop's monitors drawn small as they're laid out (as Windows' Display settings
/// shows them), each with its number; a click picks or unpicks one (picked: black with an accent border). Redrawn
/// when the monitors change and when it's resized.
/// </summary>
internal sealed class ScreenPicker : Border, IDisposable
{
    private const double Gap = 3;
    private readonly Canvas canvas = new() { ClipToBounds = true };
    private readonly Func<string, bool> picked;
    private readonly Action<string> toggle;

    public ScreenPicker(Func<string, bool> picked, Action<string> toggle)
    {
        this.picked = picked;
        this.toggle = toggle;
        Child = canvas;
        Height = 110;
        Margin = new Thickness(0, 8, 0, 0);
        canvas.SizeChanged += (_, _) => Draw();
        SystemEvents.DisplaySettingsChanged += OnDisplays;
    }

    private void OnDisplays(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Draw);

    public void Draw()
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        var list = Blackout.Displays();
        if (list.Count == 0 || w <= 0 || h <= 0) return;
        int left = list.Min(d => d.Bounds.X), top = list.Min(d => d.Bounds.Y);
        int right = list.Max(d => d.Bounds.X + d.Bounds.Width), bottom = list.Max(d => d.Bounds.Y + d.Bounds.Height);
        double scale = Math.Min(w / (right - left), h / (bottom - top));
        double x0 = (w - (right - left) * scale) / 2, y0 = (h - (bottom - top) * scale) / 2;
        foreach (var d in list.OrderBy(d => d.Number.PadLeft(3)))
        {
            bool on = picked(d.Name);
            var main = d.Primary ? T("(main)") : "";
            var name = $"{d.Number}: {d.Bounds.Width}×{d.Bounds.Height} {main}".TrimEnd();
            var label = new TextBlock { Text = d.Number, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Width = Math.Max(4, d.Bounds.Width * scale - Gap), Height = Math.Max(4, d.Bounds.Height * scale - Gap),
                CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Child = label,
                ToolTip = name, Focusable = true,
            };
            if (on)
            {
                box.Background = Brushes.Black;
                box.SetResourceReference(BorderBrushProperty, "AccentBrush");
                label.Foreground = Brushes.White;
            }
            else
            {
                box.SetResourceReference(BackgroundProperty, "ControlBrush");
                box.SetResourceReference(BorderBrushProperty, "ControlBorderStrongBrush");
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
            var state = on ? T("goes black") : T("left as it is");
            AutomationProperties.SetName(box, $"{name}: {state}");
            var device = d.Name;
            box.MouseLeftButtonUp += (_, _) => Pick(device);
            box.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Pick(device); e.Handled = true; } };
            Canvas.SetLeft(box, x0 + (d.Bounds.X - left) * scale + Gap / 2);
            Canvas.SetTop(box, y0 + (d.Bounds.Y - top) * scale + Gap / 2);
            canvas.Children.Add(box);
        }
    }

    private void Pick(string device)
    {
        toggle(device);
        Draw();
    }

    public void Dispose() => SystemEvents.DisplaySettingsChanged -= OnDisplays;
}
