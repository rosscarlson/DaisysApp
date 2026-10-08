using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Template;

/// <summary>Settings → Template.</summary>
public partial class TemplateSettingsView : UserControl
{
    private readonly TemplateSettings settings;
    private readonly TemplateView view;
    private readonly bool ready;

    public TemplateSettingsView(TemplateSettings settings, TemplateView view)
    {
        this.settings = settings;
        this.view = view;
        InitializeComponent();
        NameBox.Text = settings.Name;
        ready = true;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        settings.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "world";
        settings.Save();
        view.Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        settings.Clicks = 0;
        settings.Save();
        view.Refresh();
    }
}
