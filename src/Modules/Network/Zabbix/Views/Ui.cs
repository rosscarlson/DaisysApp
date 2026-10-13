using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>Small pieces the module's code-built views share (the app's styles, by their keys).</summary>
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

    /// <summary>A coloured swatch and a name per line; clicking one picks its line out of the graph (again for all).</summary>
    public static WrapPanel Legend(LineGraph graph, IEnumerable<(string Name, string Color)> lines, Action<string?> picked)
    {
        var legend = new WrapPanel();
        var items = new List<(string Name, FrameworkElement Item)>();
        foreach (var (name, color) in lines)
        {
            var swatch = new Border { Width = 12, Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            SetBrush(swatch, Border.BackgroundProperty, color);
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = T("Click to highlight this line; click again to show all"),
            };
            item.Children.Add(swatch);
            item.Children.Add(Secondary(name));
            legend.Children.Add(item);
            items.Add((name, item));
            item.MouseLeftButtonUp += (_, _) =>
            {
                graph.Highlight = graph.Highlight == name ? null : name;
                Show();
                picked(graph.Highlight);
            };
        }
        Show();
        void Show()
        {
            foreach (var (n, el) in items) el.Opacity = graph.Highlight == null || graph.Highlight == n ? 1 : 0.45;
        }
        return legend;
    }

    public static string Ago(DateTime? t)
    {
        if (t is not { } v) return T("never");
        var d = DateTime.Now - v;
        if (d.TotalSeconds < 60) return T("just now");
        if (d.TotalMinutes < 60) return P((long)d.TotalMinutes, "{0} minute ago", "{0} minutes ago");
        if (d.TotalHours < 48) return P((long)d.TotalHours, "{0} hour ago", "{0} hours ago");
        return P((long)d.TotalDays, "{0} day ago", "{0} days ago");
    }

    /// <summary>How long ago, for a problem ("3h 12m").</summary>
    public static string Duration(DateTime since)
    {
        var d = DateTime.Now - since;
        if (d.TotalDays >= 1) return $"{(int)d.TotalDays}d {d.Hours}h";
        if (d.TotalHours >= 1) return $"{d.Hours}h {d.Minutes}m";
        return $"{Math.Max(0, d.Minutes)}m";
    }
}
