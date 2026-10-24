using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared;

namespace DaisysApp.Applets.AudioTools.Tests;

public partial class AudioLevelSettingsView : UserControl
{
    private readonly AudioLevelView view;
    private readonly AudioLevelSettings settings;
    private bool updating;

    /// <summary>Settings → Audio's cards, two by two, with <paramref name="first"/> (Volume's) first.</summary>
    public AudioLevelSettingsView(AudioLevelView view, AudioLevelSettings settings, UIElement? first = null)
    {
        this.view = view;
        this.settings = settings;
        InitializeComponent();
        if (first != null) Cards.Children.Insert(0, first);
        for (int i = 0; i < Cards.Children.Count; i++)
        {
            if (i % 2 == 0) Cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(Cards.Children[i], i / 2);
            Grid.SetColumn(Cards.Children[i], i % 2 * 2);
        }
        for (int n = SpeakerGrid.MinSize; n <= SpeakerGrid.MaxSize; n++) GridSizeBox.Items.Add(new ComboBoxItem { Content = $"{n} × {n}", Tag = n });
        GridSizeBox.SelectedItem = GridSizeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.SpeakerGridSize);
        foreach (int db in new[] { 0, 2, 3, 4, 6, 8, 10, 12 }) MaxBoostBox.Items.Add(new ComboBoxItem { Content = $"{db} dB", Tag = db });
        MaxBoostBox.SelectedItem = MaxBoostBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == (int)Math.Round(settings.LevelMaxBoostDb))
            ?? MaxBoostBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == 6);
        MatchBox.SelectedIndex = settings.LevelMatch == LevelMatch.VoiceBand ? 1 : 0;
        // (the wizard's window can change it too)
        IsVisibleChanged += (_, _) => { if (IsVisible) MatchBox.SelectedIndex = settings.LevelMatch == LevelMatch.VoiceBand ? 1 : 0; };
        ConfirmButton.Arm(ResetPositionsButton, ResetPositions);
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

    private void ResetPositions()
    {
        view.ResetSpeakerPositions();
        ResetPositionsButton.Content = T("Positions reset");
    }

    private void VoicemeeterBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        view.SetVoicemeeterIntegration(VoicemeeterBox.IsChecked == true);
    }
}
