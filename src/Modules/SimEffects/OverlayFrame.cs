using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects;

/// <summary>A SimEffects widget's place and look: how opaque its background is and whether its title shows.</summary>
public interface IOverlayPlace : IWidgetPlace
{
    /// <summary>The background's opacity, 0 (none: only what it shows, drawn over the game) – 1.</summary>
    double Background { get; set; }
    /// <summary>Its title shown (null: the widget's own default).</summary>
    bool? Title { get; set; }
}

/// <summary>
/// What a SimEffects widget (Analysis' blocks, the Shaker's meters) draws in Core's <see cref="Widget"/> made with
/// <c>card: false</c> (since 3.24.2): its own card, a little padding, a background as opaque as
/// <see cref="IOverlayPlace.Background"/> (the text keeps a shadow when it's faint), the title above (if there's one
/// and it's on), and in the edit strip a Title checkbox and a Background slider.
/// </summary>
internal sealed class OverlayFrame : Grid
{
    private readonly IOverlayPlace place;
    private readonly Border back = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
    private readonly Border edge = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2) };
    private readonly Border inner = new() { Padding = new Thickness(8, 4, 8, 6) };
    private readonly FrameworkElement? title;
    private readonly bool titleAtFirst;
    private bool editing;

    /// <param name="title">The title (collapsed when it's off), above <paramref name="body"/>; none: no Title option.</param>
    public OverlayFrame(IOverlayPlace place, UIElement body, FrameworkElement? title = null, bool titleAtFirst = true)
    {
        this.place = place;
        this.title = title;
        this.titleAtFirst = titleAtFirst;
        back.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        back.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        edge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var dock = new DockPanel();
        if (title != null)
        {
            DockPanel.SetDock(title, Dock.Top);
            dock.Children.Add(title);
        }
        dock.Children.Add(body);
        inner.Child = dock;
        Children.Add(back);
        Children.Add(edge);
        Children.Add(inner);
        Show();
    }

    public bool TitleShown => place.Title ?? titleAtFirst;

    /// <summary>Its widget is being edited: the accent edge and the background a little there, to see what's stretched.</summary>
    public bool Editing
    {
        get => editing;
        set { editing = value; Show(); }
    }

    private void Show()
    {
        double b = Math.Clamp(place.Background, 0, 1);
        back.Opacity = editing ? Math.Max(b, 0.35) : b;
        edge.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (title != null) title.Visibility = TitleShown || editing ? Visibility.Visible : Visibility.Collapsed;
        if (title != null) title.Opacity = TitleShown ? 1 : 0.45;
        // (over the game with little behind it: a shadow keeps the text readable)
        inner.Effect = b < 0.5 ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 5, Opacity = 0.9, Color = Colors.Black } : null;
    }

    /// <summary>A widget for <paramref name="body"/> in this frame, with its options in the edit strip.</summary>
    public static (Widget Window, OverlayFrame Frame) Make(IOverlayPlace place, Action save, Action delete, Size firstSize, UIElement body,
        FrameworkElement? title = null, bool titleAtFirst = true)
    {
        var frame = new OverlayFrame(place, body, title, titleAtFirst);
        var window = new Widget(place, save, delete, card: false, firstSize: firstSize) { Body = frame };
        if (title != null)
        {
            var titleBox = new CheckBox { Content = T("Title"), IsChecked = frame.TitleShown, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Show the title") };
            titleBox.Checked += (_, _) => { place.Title = true; frame.Show(); save(); };
            titleBox.Unchecked += (_, _) => { place.Title = false; frame.Show(); save(); };
            window.AddOption(titleBox);
        }
        var label = new TextBlock { Text = T("Background"), VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 90, VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 10,
            Value = Math.Round(place.Background * 100), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Background opacity") };
        AutomationProperties.SetName(slider, T("Background"));
        slider.ValueChanged += (_, _) => { place.Background = slider.Value / 100; frame.Show(); };
        window.AddOption(new StackPanel { Orientation = Orientation.Horizontal, Children = { label, slider } });
        window.EditingChanged += () => frame.Editing = window.Editing;
        return (window, frame);
    }
}
