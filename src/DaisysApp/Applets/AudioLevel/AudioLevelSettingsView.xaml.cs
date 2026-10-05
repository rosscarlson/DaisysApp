using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.AudioLevel;

public partial class AudioLevelSettingsView : UserControl
{
    private readonly AudioLevelView view;
    private readonly AudioLevelSettings settings;
    private bool updating;

    public AudioLevelSettingsView(AudioLevelView view, AudioLevelSettings settings)
    {
        this.view = view;
        this.settings = settings;
        InitializeComponent();
        view.VoicemeeterChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        updating = true;
        VoicemeeterBox.IsChecked = settings.VoicemeeterIntegration;
        updating = false;

        VoicemeeterStatus.Text = view.VoicemeeterSummary;
        if (view.VoicemeeterProblem) VoicemeeterStatus.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else VoicemeeterStatus.ClearValue(TextBlock.ForegroundProperty);
    }

    private void VoicemeeterBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        view.SetVoicemeeterIntegration(VoicemeeterBox.IsChecked == true);
    }
}
