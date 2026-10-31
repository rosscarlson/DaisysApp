using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Applets.SimEffects.Arduino;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects;

/// <summary>The pieces the Arduino, Notify and Fanatec tabs are built of, in the app's styles (as the Shaker's).</summary>
internal static class SimUi
{
    public static Border Card(UIElement child, double bottom = 12) => Ui.Card(child, new Thickness(0, 0, 0, bottom));

    public static Button Button(string text, Action click, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Core's Confirm? button.</summary>
    public static Button Confirm(string text, Action confirmed, string? tip = null)
    {
        var b = DaisysApp.Shared.ConfirmButton.Make(text, confirmed, tip);
        b.Padding = new Thickness(10, 3, 10, 3);
        b.Margin = new Thickness(0, 0, 6, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    /// <summary>The ON / OFF chip at a tab's top left.</summary>
    public static ToggleButton OnOff(string name, bool on, Action<bool> set)
    {
        var t = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(t, name);
        ShowOnOff(t, on);
        t.Click += (_, _) =>
        {
            bool now = t.IsChecked == true;
            ShowOnOff(t, now);
            set(now);
        };
        return t;
    }

    /// <summary>ON or OFF; an ON is greyed while the SimEffects profile is stopped (on, but nothing runs).</summary>
    public static void ShowOnOff(ToggleButton t, bool on)
    {
        t.IsChecked = on;
        t.Content = on ? T("ON") : T("OFF");
        bool dark = on && !DaisysApp.Applets.SimEffects.Profiles.SimProfiles.Playing;
        t.Opacity = dark ? 0.4 : 1;
        t.ToolTip = dark ? T("Profile stopped") : null;
    }

    public static Grid SliderRow(string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string? tip = null)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0), ToolTip = tip };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(s, label);
        var v = new TextBlock { Text = show(value), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) => { v.Text = show(s.Value); set(s.Value); };
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        Grid.SetColumn(v, 2);
        g.Children.Add(v);
        return g;
    }

    public static Grid Field(string label, FrameworkElement control, double labelWidth = 120)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>A number box that sets <paramref name="set"/> when it holds a number in range (on each change).</summary>
    public static TextBox Number(int value, int min, int max, Action<int> set, double width = 80)
    {
        var t = new TextBox { Text = value.ToString(), Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        t.TextChanged += (_, _) => { if (int.TryParse(t.Text, out int n) && n >= min && n <= max) set(n); };
        return t;
    }

    /// <summary>Two columns: a fixed left one and the rest, each scrolling.</summary>
    public static Grid Columns(UIElement left, UIElement right, double leftWidth = 380)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Scroll(left));
        var r = Scroll(right);
        Grid.SetColumn(r, 2);
        g.Children.Add(r);
        return g;
    }

    public static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static readonly Brush Amber = Frozen(Color.FromRgb(0xD2, 0x99, 0x22));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>A board's status light (<see cref="Light"/> colours it).</summary>
    public static Ellipse Dot() => new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Green connected, amber connecting, red a problem, grey off.</summary>
    public static void Light(Ellipse dot, BoardState state)
    {
        if (state == BoardState.Connecting) dot.Fill = Amber;
        else dot.SetResourceReference(Shape.FillProperty, state switch
        {
            BoardState.Connected => "SuccessBrush",
            BoardState.Problem => "ErrorTextBrush",
            _ => "TextSecondaryBrush",
        });
        dot.ToolTip = state switch
        {
            BoardState.Connected => T("Connected"),
            BoardState.Problem => T("Not connected"),
            BoardState.Connecting => T("Connecting…"),
            _ => T("Off"),
        };
    }
}
