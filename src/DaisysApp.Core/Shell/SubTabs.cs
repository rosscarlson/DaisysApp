using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Shell;

/// <summary>
/// A row of sub-tabs (like Settings') over pages, only the chosen one showing: what an <see cref="AppletGroup"/> shows
/// in its tab and on its Settings page. Every page stays alive while another is showing.
/// </summary>
public sealed class SubTabs : UserControl
{
    private readonly List<(string Id, RadioButton Button, FrameworkElement Page)> pages = new();
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Grid host = new();

    /// <summary>The id of the page showing.</summary>
    public string? Current { get; private set; }

    /// <summary>Raised with the page's id when another is chosen.</summary>
    public event Action<string>? Selected;

    public SubTabs()
    {
        var layout = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(host);
        Content = layout;
    }

    /// <param name="icon">A Segoe Fluent Icons glyph shown before the title, or null.</param>
    public void Add(string id, string title, string? icon, FrameworkElement page)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null)
        {
            var glyph = new TextBlock { Text = icon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            header.Children.Add(glyph);
        }
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var button = new RadioButton { Content = header };
        button.SetResourceReference(StyleProperty, "TabButton");
        AutomationProperties.SetName(button, title);
        button.Checked += (_, _) => Show(id);
        strip.Children.Add(button);
        page.Visibility = Visibility.Collapsed;
        host.Children.Add(page);
        pages.Add((id, button, page));
        if (pages.Count == 1) button.IsChecked = true;
    }

    /// <summary>Shows the page with this id (the first one if there's none).</summary>
    public void Select(string? id)
    {
        if (pages.Count == 0) return;
        var p = pages.FirstOrDefault(t => t.Id == id && t.Button.Visibility == Visibility.Visible);
        (p.Button ?? pages.FirstOrDefault(t => t.Button.Visibility == Visibility.Visible).Button ?? pages[0].Button).IsChecked = true;
    }

    /// <summary>Shows or hides a page's sub-tab (a tool switched off in its group); hiding the one showing shows the first other.</summary>
    public void SetShown(string id, bool shown)
    {
        var p = pages.FirstOrDefault(t => t.Id == id);
        if (p.Button == null) return;
        p.Button.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown && Current == id && pages.FirstOrDefault(t => t.Id != id && t.Button.Visibility == Visibility.Visible) is { Button: { } other })
            other.IsChecked = true;
    }

    /// <summary>Shows the page holding the element with this x:Name and returns the element, or null if none has it.</summary>
    public FrameworkElement? Reveal(string elementName)
    {
        foreach (var (id, _, page) in pages)
        {
            if (Find(page, elementName) is not { } found) continue;
            Select(id);
            return found;
        }
        return null;
    }

    private static FrameworkElement? Find(FrameworkElement page, string name)
    {
        // (pages can share a name scope, e.g. two pages of one control: the element has to be on this page)
        if (page.FindName(name) is FrameworkElement e && (e == page || page.IsAncestorOf(e))) return e;
        if (page is SubTabs nested) return nested.Reveal(name);
        // a page made of several (e.g. a StackPanel of settings cards, each with its own names)
        foreach (var child in LogicalTreeHelper.GetChildren(page).OfType<FrameworkElement>())
            if (Find(child, name) is { } c) return c;
        return null;
    }

    private void Show(string id)
    {
        Current = id;
        foreach (var t in pages) t.Page.Visibility = t.Id == id ? Visibility.Visible : Visibility.Collapsed;
        Selected?.Invoke(id);
    }
}
