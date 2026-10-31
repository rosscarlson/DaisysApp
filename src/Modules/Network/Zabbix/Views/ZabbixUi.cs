using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>The module's own pieces (the rest is Core's <see cref="Ui"/>).</summary>
internal static class ZabbixUi
{
    /// <summary>A coloured swatch and a name per line; clicking one picks its line out of the graph (again for all).</summary>
    public static WrapPanel Legend(LineGraph graph, IEnumerable<(string Name, string Color)> lines, Action<string?> picked)
    {
        var legend = new WrapPanel();
        var items = new List<(string Name, FrameworkElement Item)>();
        foreach (var (name, color) in lines)
        {
            var swatch = new Border { Width = 12, Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Ui.SetBrush(swatch, Border.BackgroundProperty, color);
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = T("Click to highlight this line; click again to show all"),
            };
            item.Children.Add(swatch);
            item.Children.Add(Ui.Secondary(name));
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
