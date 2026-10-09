using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DaisysApp.Settings;

namespace DaisysApp.Applets.AudioTools;

/// <summary>Which tool the Audio Tools tab last showed. Saved in %APPDATA%\DaisysApp\AudioTools.json.</summary>
public sealed class AudioToolsSettings
{
    private const string FileName = "AudioTools";
    public const string Levels = "levels", Tests = "tests", Delay = "delay";

    public string Tool { get; set; } = Levels;

    public static AudioToolsSettings Load() => JsonStore.Load<AudioToolsSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>
/// The Audio Tools tab: a row of sub-tabs (like Settings') over the tools, only the chosen one showing. Every tool
/// stays alive while another is showing, so a test or a meter keeps its state.
/// </summary>
internal sealed class AudioToolsView : UserControl
{
    private readonly AudioToolsSettings settings;
    private readonly List<(string Id, RadioButton Button, FrameworkElement Page)> tools = new();

    public string Current => settings.Tool;

    public AudioToolsView(AudioToolsSettings settings, IEnumerable<(string Id, string Title, FrameworkElement Page)> pages)
    {
        this.settings = settings;
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var host = new Grid();
        foreach (var (id, title, page) in pages)
        {
            var button = new RadioButton { Content = title };
            button.SetResourceReference(StyleProperty, "TabButton");
            AutomationProperties.SetName(button, title);
            button.Checked += (_, _) => Show(id);
            strip.Children.Add(button);
            page.Visibility = Visibility.Collapsed;
            host.Children.Add(page);
            tools.Add((id, button, page));
        }
        var layout = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(host);
        Content = layout;
        var start = tools.FirstOrDefault(t => t.Id == settings.Tool);
        (start.Button ?? tools[0].Button).IsChecked = true;
    }

    private void Show(string id)
    {
        settings.Tool = id;
        foreach (var t in tools) t.Page.Visibility = t.Id == id ? Visibility.Visible : Visibility.Collapsed;
    }
}
