using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>The module's own pieces (the rest is Core's <see cref="Ui"/>).</summary>
internal static class HaUi
{
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
        var b = new Button { Content = Ui.Icon(glyph, 14), Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }
}
