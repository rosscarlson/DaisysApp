using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.MiniMirror;

public partial class MiniMirrorSettingsView : UserControl
{
    private readonly MiniMirrorService service;
    private readonly bool loading;

    public MiniMirrorSettingsView(MiniMirrorService service)
    {
        this.service = service;
        loading = true;
        InitializeComponent();
        HideBox.IsChecked = service.Data.HideFromCapture;
        loading = false;
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowImportState(); };
        ShowImportState();
    }

    private void HideBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading) service.SetHideFromCapture(HideBox.IsChecked == true);
    }

    private void ShowImportState()
    {
        bool found = MiniMirrorData.SimHubImport.SettingsFile != null;
        SimHubButton.IsEnabled = found;
        if (ImportText.Tag == null)
            ImportText.Text = found
                ? "Copies the mirrors you made in SimHub's MiniMirror plugin. Their SimHub hotkeys can't come across, so set shortcuts again here. Close SimHub first, or uninstall the plugin, so you don't get two of each mirror."
                : "No SimHub MiniMirror settings found. SimHub saves them when it closes, so if you've made mirrors there, close SimHub and come back.";
    }

    private void ImportSimHub_Click(object sender, RoutedEventArgs e)
    {
        if (MiniMirrorData.SimHubImport.SettingsFile is not { } file) return;
        int added = service.ImportFromSimHub(file);
        ImportText.Tag = true;
        ImportText.Text = added == 0
            ? "No new mirrors found in the SimHub plugin's settings."
            : $"Imported {added} mirror{(added == 1 ? "" : "s")} from SimHub. Set their shortcuts on the Mini Mirror tab.";
    }
}
