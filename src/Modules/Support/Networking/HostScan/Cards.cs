using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Pieces Host Scan's report and the port details window share: findings with their level, facts, raw text.</summary>
internal static class Cards
{
    /// <summary>Findings, worst first: a coloured level, the title, what it means, and a link to more.</summary>
    public static Panel Findings(IEnumerable<Finding> findings, Action<Finding, StackPanel>? more = null)
    {
        var panel = new StackPanel();
        foreach (var f in findings.OrderByDescending(f => f.Level))
        {
            var level = new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Width = 66, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
                Child = new TextBlock { Text = f.LevelText, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = f.Level == Severity.Info ? Brushes.White : Brushes.Black },
            };
            level.SetValue(Border.BackgroundProperty, Ui.Brush(panel, f.Color));
            if (f.Color == "AccentBrush") level.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var title = new TextBlock { Text = f.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var text = new StackPanel { Children = { title } };
            if (f.Detail.Length > 0)
            {
                var detail = NetUi.Selectable(f.Detail);
                detail.Opacity = 0.85;
                text.Children.Add(detail);
            }
            if (f.Url != null) text.Children.Add(Ui.Link(T("More about it"), f.Url));
            more?.Invoke(f, text);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(level);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        if (panel.Children.Count == 0) panel.Children.Add(Ui.Secondary(T("Nothing found")));
        return panel;
    }

    /// <summary>A card of facts (nothing if there are none).</summary>
    public static Border? Facts(string title, IEnumerable<Fact> facts, Thickness margin)
    {
        var list = facts.ToList();
        if (list.Count == 0) return null;
        var fields = new NetUi.Fields();
        IpDetailsView.Fill(fields, list);
        return Ui.Card(new StackPanel { Children = { Ui.Header(title), fields } }, margin);
    }

    /// <summary>Text as it came (a banner, a page's headers), selectable, in a fixed-width font.</summary>
    public static FrameworkElement Raw(string text)
    {
        var box = NetUi.Selectable(text);
        box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        box.FontSize = 12;
        return new ScrollViewer { Content = box, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>A small coloured dot.</summary>
    public static Ellipse Dot(Brush fill, double size = 9) => new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
}
