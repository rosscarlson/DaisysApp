using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Resizer;

public partial class ResizerSettingsView : UserControl
{
    private readonly ResizerService service;
    private bool loading = true;

    public ResizerSettingsView(ResizerService service)
    {
        this.service = service;
        InitializeComponent();
        PollSlider.Value = Math.Clamp(service.Data.PollRateMs, 250, 5000);
        PollText.Text = $"{PollSlider.Value:0} ms";
        RabbitButton.IsEnabled = ResizerData.Legacy.HasData(ResizerData.Legacy.RabbitFolder);
        RaccoonButton.IsEnabled = ResizerData.Legacy.HasData(ResizerData.Legacy.RaccoonFolder);
        loading = false;
    }

    private void PollSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        PollText.Text = $"{PollSlider.Value:0} ms";
        if (!loading) service.SetPollRate((int)PollSlider.Value);
    }

    private void ImportRabbit_Click(object sender, RoutedEventArgs e) => Import(ResizerData.Legacy.RabbitFolder, "Resize Rabbit");
    private void ImportRaccoon_Click(object sender, RoutedEventArgs e) => Import(ResizerData.Legacy.RaccoonFolder, "Resize Raccoon");

    private void Import(string folder, string from)
    {
        int added = service.Import(folder);
        ImportText.Text = added == 0
            ? $"No new profiles found in {from}."
            : $"Imported {added} profile{(added == 1 ? "" : "s")} from {from}.";
    }
}
