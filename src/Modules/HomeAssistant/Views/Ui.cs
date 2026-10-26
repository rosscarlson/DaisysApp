using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>Small pieces the module's code-built views share (the app's styles, by their keys; as Zabbix's).</summary>
internal static class Ui
{
    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    public static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }

    public static TextBlock Secondary(string text = "", bool wrap = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 14)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    /// <summary>A button with an icon and a caption, like the history window's.</summary>
    public static Button IconButton(string glyph, string text, string? tip = null, RoutedEventHandler? click = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 13);
        icon.Margin = new Thickness(0, 0, 6, 0);
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = panel, Margin = new Thickness(8, 0, 0, 0), ToolTip = tip };
        if (click != null) b.Click += click;
        return b;
    }

    /// <summary>A theme brush key ("AccentBrush") or a colour ("#F7A541") as a brush.</summary>
    public static Brush Brush(FrameworkElement owner, string color) =>
        color.StartsWith('#') ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) : owner.TryFindResource(color) as Brush ?? Brushes.DodgerBlue;

    public static void SetBrush(FrameworkElement e, DependencyProperty p, string color)
    {
        if (color.StartsWith('#')) e.SetValue(p, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        else e.SetResourceReference(p, color);
    }

    /// <summary>A small ON / OFF chip (the app's ChipToggle), for a quick toggle.</summary>
    public static ToggleButton Chip(string name)
    {
        var t = new ToggleButton { Padding = new Thickness(9, 2, 9, 2), MinWidth = 46, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        t.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        System.Windows.Automation.AutomationProperties.SetName(t, name);
        return t;
    }

    public static void ShowChip(ToggleButton t, bool on, string? onText = null, string? offText = null)
    {
        t.IsChecked = on;
        t.Content = on ? onText ?? T("ON") : offText ?? T("OFF");
    }

    public static Button Small(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(9, 2, 9, 2), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip, Focusable = false };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A button with only an icon (the joystick for shortcuts).</summary>
    public static Button Glyph(string glyph, string name, Action click)
    {
        var b = new Button { Content = Icon(glyph, 14), Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }
}
