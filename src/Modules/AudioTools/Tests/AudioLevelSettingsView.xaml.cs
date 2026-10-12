using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.AudioTools.Tests;

public partial class AudioLevelSettingsView : UserControl
{
    private readonly AudioLevelView view;
    private readonly AudioLevelSettings settings;
    private bool updating;
    private bool resetArmed;
    private readonly System.Windows.Threading.DispatcherTimer resetDisarm = new() { Interval = TimeSpan.FromSeconds(4) };

    public AudioLevelSettingsView(AudioLevelView view, AudioLevelSettings settings)
    {
        this.view = view;
        this.settings = settings;
        InitializeComponent();
        for (int n = SpeakerGrid.MinSize; n <= SpeakerGrid.MaxSize; n++) GridSizeBox.Items.Add(new ComboBoxItem { Content = $"{n} × {n}", Tag = n });
        GridSizeBox.SelectedItem = GridSizeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.SpeakerGridSize);
        foreach (int db in new[] { 0, 2, 3, 4, 6, 8, 10, 12 }) MaxBoostBox.Items.Add(new ComboBoxItem { Content = $"{db} dB", Tag = db });
        MaxBoostBox.SelectedItem = MaxBoostBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == (int)Math.Round(settings.LevelMaxBoostDb))
            ?? MaxBoostBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == 6);
        MatchBox.SelectedIndex = settings.LevelMatch == LevelMatch.VoiceBand ? 1 : 0;
        // (the wizard's window can change it too)
        IsVisibleChanged += (_, _) => { if (IsVisible) MatchBox.SelectedIndex = settings.LevelMatch == LevelMatch.VoiceBand ? 1 : 0; };
        resetDisarm.Tick += (_, _) => DisarmReset();
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

    private void GridSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridSizeBox.SelectedItem is ComboBoxItem { Tag: int n } && IsLoaded) view.SetSpeakerGridSize(n);
    }

    private void MatchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || MatchBox.SelectedIndex < 0) return;
        settings.LevelMatch = MatchBox.SelectedIndex == 1 ? LevelMatch.VoiceBand : LevelMatch.FullRange;
        settings.Save();
    }

    private void MaxBoostBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || MaxBoostBox.SelectedItem is not ComboBoxItem { Tag: int db }) return;
        settings.LevelMaxBoostDb = db;
        settings.Save();
    }

    private void ResetPositions_Click(object sender, RoutedEventArgs e)
    {
        if (!resetArmed)
        {
            resetArmed = true;
            ResetPositionsButton.Style = (Style)FindResource("DangerButton");
            ResetPositionsButton.Content = T("Click again to reset");
            resetDisarm.Start();
            return;
        }
        view.ResetSpeakerPositions();
        DisarmReset();
        ResetPositionsButton.Content = T("Positions reset");
    }

    private void DisarmReset()
    {
        resetArmed = false;
        resetDisarm.Stop();
        ResetPositionsButton.ClearValue(StyleProperty);
        ResetPositionsButton.Content = T("Reset speaker positions");
    }

    private void VoicemeeterBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        view.SetVoicemeeterIntegration(VoicemeeterBox.IsChecked == true);
    }
}
