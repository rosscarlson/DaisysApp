using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Shell;

/// <summary>
/// The tab pattern used for the main tabs and the Settings sub-tabs: a row of "TabButton" RadioButtons over a Grid
/// that holds every page, with only the selected page visible. Pages stay alive when you switch tabs.
/// </summary>
internal sealed class TabStrip(Panel strip, Panel host)
{
    private readonly List<(string Id, RadioButton Button, FrameworkElement Page)> tabs = new();

    /// <summary>Raised with the tab id after a tab is shown.</summary>
    public event Action<string>? Selected;

    public void Add(string id, string title, string? iconGlyph, FrameworkElement page)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (iconGlyph != null)
        {
            header.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = (FontFamily)strip.FindResource("IconFont"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
        }
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });

        // No GroupName: RadioButtons that share a parent panel form their own group.
        var button = new RadioButton { Style = (Style)strip.FindResource("TabButton"), Content = header };
        AutomationProperties.SetName(button, title);
        button.Checked += (_, _) => Show(id);
        strip.Children.Add(button);

        page.Visibility = Visibility.Collapsed;
        host.Children.Add(page);
        tabs.Add((id, button, page));
    }

    /// <summary>Selects the tab with this id, or the first tab if there's no such tab.</summary>
    public void Select(string? id)
    {
        if (tabs.Count == 0) return;
        var tab = tabs.FirstOrDefault(t => t.Id == id);
        (tab.Button ?? tabs[0].Button).IsChecked = true;
    }

    private void Show(string id)
    {
        foreach (var tab in tabs)
            tab.Page.Visibility = tab.Id == id ? Visibility.Visible : Visibility.Collapsed;
        Selected?.Invoke(id);
    }
}
