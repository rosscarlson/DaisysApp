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
        HdrBox.IsChecked = service.Data.HdrConversion;
        NewMirrorShortcut.Value = service.Data.NewMirrorShortcut;
        NewMirrorShortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        NewMirrorShortcut.Changed += () =>
        {
            service.SetNewMirrorShortcut(NewMirrorShortcut.Value);
            ShowShortcutState();
        };
        loading = false;
        IsVisibleChanged += (_, _) => { if (IsVisible) { ShowImportState(); ShowShortcutState(); ShowHdrState(); } };
        ShowShortcutState();
        ShowImportState();
    }

    private void HdrBox_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        service.SetHdrConversion(HdrBox.IsChecked == true);
        HdrStatus.Text = "";
    }

    private void ShowHdrState() =>
        HdrStatus.Text = service.CapturingHdr ? T("An HDR monitor is being mirrored now, and converted.") : "";

    private void ShowShortcutState()
    {
        string? s = service.Data.NewMirrorShortcut;
        bool failed = s != null && service.FailedShortcuts.Contains(s, StringComparer.OrdinalIgnoreCase);
        NewMirrorWarning.Text = failed ? F("{0} is already used by another program, so it won't work. Pick another.", s) : "";
        NewMirrorWarning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
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
                ? T("Copies the mirrors you made in SimHub's MiniMirror plugin. Their SimHub hotkeys can't come across, so set shortcuts again here. Close SimHub first, or uninstall the plugin, so you don't get two of each mirror.")
                : T("No SimHub MiniMirror settings found. SimHub saves them when it closes, so if you've made mirrors there, close SimHub and come back.");
    }

    private void ImportSimHub_Click(object sender, RoutedEventArgs e)
    {
        if (MiniMirrorData.SimHubImport.SettingsFile is not { } file) return;
        int added = service.ImportFromSimHub(file);
        ImportText.Tag = true;
        ImportText.Text = added == 0
            ? T("No new mirrors found in the SimHub plugin's settings.")
            : P(added, "Imported {0} mirror from SimHub. Set their shortcuts on the Mini Mirror tab.", "Imported {0} mirrors from SimHub. Set their shortcuts on the Mini Mirror tab.");
    }
}
