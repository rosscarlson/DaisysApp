using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Template;

/// <summary>The Template tab: says hello and counts clicks, to show text, translation and saved settings.</summary>
public partial class TemplateView : UserControl
{
    private readonly TemplateSettings settings;

    public TemplateView(TemplateSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
    }

    /// <summary>Shows the current settings.</summary>
    public void Refresh()
    {
        // F fills in values ({0}…) and P picks the singular or plural: both translate, like T and {l:Tr} in XAML
        HelloText.Text = F("Hello, {0}!", settings.Name);
        ClicksText.Text = P(settings.Clicks, "Clicked {0} time", "Clicked {0} times");
    }

    private void Click_Click(object sender, RoutedEventArgs e)
    {
        settings.Clicks++;
        settings.Save();
        Refresh();
    }
}
