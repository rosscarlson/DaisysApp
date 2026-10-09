using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DaisysApp.Shell;

/// <summary>
/// Settings save as soon as they change (there's no Save button), so this shows a small "✓ Saved" beside the control
/// you just changed — a checkbox, option, list, slider or text box anywhere under <paramref name="root"/> — and fades
/// it out. Only changes you make count: a page filling in its controls when it loads doesn't flash it.
/// </summary>
internal sealed class SavedIndicator
{
    private readonly Popup popup;
    private readonly Border pill;
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private FrameworkElement? pending;
    private string? textOnFocus;

    public SavedIndicator(FrameworkElement root)
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal };
        var check = new TextBlock { Text = "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        check.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        text.Children.Add(check);
        text.Children.Add(new TextBlock { Text = T("Saved"), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 3), Child = text, Margin = new Thickness(8, 0, 0, 0) };
        pill.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
        pill.SetResourceReference(TextElement.ForegroundProperty, "AccentForegroundBrush");
        popup = new Popup
        {
            Child = pill, AllowsTransparency = true, Placement = PlacementMode.Relative, StaysOpen = true,
            IsHitTestVisible = false, Focusable = false, PopupAnimation = PopupAnimation.None,
        };
        hide.Tick += (_, _) => FadeOut();
        settle.Tick += (_, _) => { settle.Stop(); if (pending != null) Show(pending); };

        root.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(Changed));
        root.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(Changed));
        root.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => Changed(s, e)));
        root.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => Changed(s, e)));
        // a text box saves when you leave it (or press Enter): only if its text changed
        root.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is TextBox box) textOnFocus = box.Text;
        }));
        root.AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.OldFocus is TextBox { IsReadOnly: false } box && textOnFocus != null && box.Text != textOnFocus) Show(box);
            textOnFocus = null;
        }));
        root.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox { IsReadOnly: false } box && textOnFocus != null && box.Text != textOnFocus)
            {
                textOnFocus = box.Text;
                Dispatcher.CurrentDispatcher.BeginInvoke(() => Show(box), DispatcherPriority.Background);
            }
        }));
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element || element is TextBox) return;
        // a list's selection changes inside it (a ComboBox's items): go up to the control itself
        var control = element is ComboBoxItem or ListBoxItem ? ItemsControl.ItemsControlFromItemContainer(element) ?? element : element;
        if (!(control.IsMouseOver || control.IsKeyboardFocusWithin || control is ComboBox { IsDropDownOpen: true })) return; // the page set it, not you
        // a slider fires all the way through a drag: wait until it settles
        pending = control;
        settle.Stop();
        settle.Start();
    }

    private void Show(FrameworkElement target)
    {
        if (!target.IsVisible) return;
        popup.IsOpen = false;
        popup.PlacementTarget = target;
        // just past what the control shows (a checkbox stretches across the card, but its text ends sooner)
        double width = Math.Min(target.ActualWidth, target.DesiredSize.Width > 0 ? target.DesiredSize.Width : target.ActualWidth);
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popup.HorizontalOffset = width + 6;
        popup.VerticalOffset = (target.ActualHeight - pill.DesiredSize.Height) / 2;
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        pill.Opacity = 1;
        popup.IsOpen = true;
        hide.Stop();
        hide.Start();
    }

    private void FadeOut()
    {
        hide.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) => { if (pill.Opacity == 0) popup.IsOpen = false; };
        pill.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
