using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// A colour: a button showing it, which opens a palette, a box for the colour as #RRGGBB (or #AARRGGBB) and how solid it
/// is (opacity). <see cref="Changed"/> fires as it changes.
/// </summary>
internal sealed class ColorBox : Button
{
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#E6E6E6", "#BDBDBD", "#8C8C8C", "#5E5E5E", "#3A3A3A", "#2D3540", "#1E1E1E", "#15181C", "#000000",
        "#FFCDD2", "#EF9A9A", "#E57373", "#F44336", "#E53935", "#C62828", "#B71C1C", "#880E4F", "#E91E63", "#F48FB1",
        "#FFE0B2", "#FFB74D", "#FF9800", "#F57C00", "#E65100", "#FFF59D", "#FFEB3B", "#FBC02D", "#F9A825", "#FFD54F",
        "#C8E6C9", "#81C784", "#4CAF50", "#388E3C", "#1B5E20", "#B2DFDB", "#4DB6AC", "#009688", "#00796B", "#3DDC84",
        "#BBDEFB", "#64B5F6", "#4CC2FF", "#2196F3", "#1976D2", "#0D47A1", "#B3E5FC", "#03A9F4", "#00BCD4", "#006064",
        "#E1BEE7", "#BA68C8", "#9C27B0", "#7B1FA2", "#4A148C", "#D1C4E9", "#9575CD", "#673AB7", "#512DA8", "#311B92",
    };

    private readonly Border swatch = new() { Width = 22, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontFamily = new FontFamily("Consolas") };
    private Color color;

    public event Action<string>? Changed;

    public string Value => ColorText.Text(color);

    public ColorBox(string value, string name)
    {
        color = ColorText.Parse(value, Colors.White);
        // (the app's look for buttons is keyed to Button, which a class of its own doesn't get by itself)
        SetResourceReference(StyleProperty, typeof(Button));
        Padding = new Thickness(6, 3, 10, 3);
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        swatch.SetResourceReference(Border.BorderBrushProperty, "ControlBorderStrongBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(swatch);
        line.Children.Add(text);
        Content = line;
        AutomationProperties.SetName(this, name);
        ToolTip = name;
        Show();
        Click += (_, _) => Open();
    }

    public void Set(string value)
    {
        color = ColorText.Parse(value, color);
        Show();
    }

    private void Show()
    {
        swatch.Background = new SolidColorBrush(color);
        text.Text = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void Pick(Color c)
    {
        color = c;
        Show();
        Changed?.Invoke(Value);
    }

    private void Open()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        var grid = new UniformGrid { Columns = 10 };
        foreach (var hex in Palette)
        {
            var c = ColorText.Parse(hex, Colors.White);
            var b = new Button { Width = 24, Height = 24, Margin = new Thickness(1), Padding = new Thickness(0), ToolTip = hex };
            b.Content = new Border { Background = new SolidColorBrush(c), Width = 20, Height = 20, CornerRadius = new CornerRadius(3) };
            AutomationProperties.SetName(b, hex);
            b.Click += (_, _) => { Pick(Color.FromArgb(color.A, c.R, c.G, c.B)); hexBox!.Text = text.Text; };
            grid.Children.Add(b);
        }
        panel.Children.Add(grid);

        var hexLine = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var hexLabel = new TextBlock { Text = T("Colour"), Width = 70, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hexLabel, Dock.Left);
        hexLine.Children.Add(hexLabel);
        hexBox = new TextBox { Text = text.Text, FontFamily = new FontFamily("Consolas"), ToolTip = T("#RRGGBB, or #AARRGGBB with how solid it is first") };
        AutomationProperties.SetName(hexBox, T("Colour as #RRGGBB"));
        hexBox.TextChanged += (_, _) =>
        {
            string t = hexBox.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (t.Length is not (7 or 9) || !ColorText.TryParse(t, out var c) || ColorText.Text(c) == Value) return;
            color = c;
            Show();
            Changed?.Invoke(Value);
            alpha!.Value = Math.Round(c.A / 2.55);
        };
        hexLine.Children.Add(hexBox);
        panel.Children.Add(hexLine);

        var alphaLine = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var alphaLabel = new TextBlock { Text = T("Solid"), Width = 70, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(alphaLabel, Dock.Left);
        alphaLine.Children.Add(alphaLabel);
        var alphaText = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(alphaText, Dock.Right);
        alphaLine.Children.Add(alphaText);
        alpha = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(color.A / 2.55), SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(alpha, T("How solid the colour is"));
        alphaText.Text = $"{alpha.Value:0}%";
        alpha.ValueChanged += (_, e) =>
        {
            alphaText.Text = $"{e.NewValue:0}%";
            byte a = (byte)Math.Round(e.NewValue * 2.55);
            if (a == color.A) return;
            Pick(Color.FromArgb(a, color.R, color.G, color.B));
            hexBox.Text = text.Text;
        };
        alphaLine.Children.Add(alpha);
        panel.Children.Add(alphaLine);

        var border = new Border { Child = panel, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Width = 290 };
        border.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        var popup = new Popup { Child = border, PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        popup.IsOpen = true;
        hexBox.Focus();
    }

    private TextBox? hexBox;
    private Slider? alpha;
}
