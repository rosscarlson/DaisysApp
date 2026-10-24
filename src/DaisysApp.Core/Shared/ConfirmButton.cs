using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DaisysApp.Shared;

/// <summary>
/// A button for something that can't be undone (Delete, Remove, a tab's ✕): the first click turns it into a red
/// Confirm?, a second click within 4 seconds does it; left alone, it goes back by itself. No question window, everywhere
/// in the app (see the main README, "Confirming").
/// </summary>
public static class ConfirmButton
{
    /// <summary>How long Confirm? waits for the second click.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(4);

    /// <summary>A new button with this text that asks Confirm? before <paramref name="confirmed"/>.</summary>
    public static Button Make(string text, Action confirmed, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip };
        AutomationProperties.SetName(b, text);
        Arm(b, confirmed);
        return b;
    }

    /// <summary>
    /// Makes an existing button (one from XAML, a tab's ✕) ask Confirm? first. <paramref name="skip"/>: when it says
    /// true, the first click does it at once (an empty tab). <paramref name="armed"/> and <paramref name="reset"/> let a
    /// small button change its own look (font, padding) as Confirm? shows and goes; its content and style are put back
    /// by themselves. Returns what puts it back at once (what it's for changed: another item picked).
    /// </summary>
    public static Action Arm(Button b, Action confirmed, Func<bool>? skip = null, Action? armed = null, Action? reset = null)
    {
        object? content = null;
        object? style = null;
        var timer = new DispatcherTimer { Interval = Wait };
        void Back()
        {
            if (!timer.IsEnabled) return;
            timer.Stop();
            b.Content = content;
            if (style == DependencyProperty.UnsetValue) b.ClearValue(FrameworkElement.StyleProperty);
            else b.SetValue(FrameworkElement.StyleProperty, style);
            reset?.Invoke();
        }
        timer.Tick += (_, _) => Back();
        b.Click += (_, e) =>
        {
            e.Handled = true;
            if (timer.IsEnabled)
            {
                Back();
                confirmed();
                return;
            }
            if (skip?.Invoke() == true)
            {
                confirmed();
                return;
            }
            content = b.Content;
            style = b.ReadLocalValue(FrameworkElement.StyleProperty);
            b.Content = T("Confirm?");
            b.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
            armed?.Invoke();
            timer.Start();
        };
        b.Unloaded += (_, _) => Back();
        return Back;
    }
}
