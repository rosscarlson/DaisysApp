using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Sensors;

/// <summary>Settings → Sensors.</summary>
public partial class SensorsSettingsView : UserControl
{
    private static readonly int[] KeepOptions = { 7, 14, 30, 90, 365 };
    private readonly SensorsService service;
    private readonly DispatcherTimer disarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly bool loading;
    private bool armed;

    internal SensorsSettingsView(SensorsService service)
    {
        this.service = service;
        loading = true;
        InitializeComponent();
        var s = service.Settings;
        AddressBox.Text = s.HardwareAddress;
        LogBox.IsChecked = s.LogEnabled;
        foreach (int d in KeepOptions) KeepBox.Items.Add(new ComboBoxItem { Content = d == 365 ? T("1 year") : F("{0} days", d), Tag = d });
        KeepBox.SelectedItem = KeepBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == s.KeepDays) ?? KeepBox.Items[2];
        loading = false;
        service.Updated += _ => { if (IsVisible) ShowState(); };
        disarm.Tick += (_, _) => Disarm();
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowState(); };
    }

    private void ShowState()
    {
        SensorStatus.Text = service.Latest is { } hw ? F("LibreHardwareMonitor: connected, {0} sensors.", hw.Sensors.Count) : T("LibreHardwareMonitor: not answering.");
        var days = SensorLog.Days();
        SizeText.Text = days.Count == 0
            ? T("Nothing logged yet.")
            : P(days.Count, "{0} day logged", "{0} days logged") + F(", from {0:d}, {1:0.0} MB.", days[0], SensorLog.SizeBytes() / 1e6);
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyAddress();
    }

    private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyAddress();

    private void ApplyAddress()
    {
        string address = AddressBox.Text.Trim();
        if (address.Length == 0) address = HardwareMonitor.DefaultAddress;
        if (!address.Contains("://")) address = "http://" + address;
        AddressBox.Text = address;
        if (address == service.Settings.HardwareAddress) return;
        service.SetAddress(address);
        SensorStatus.Text = T("Checking…");
    }

    private void Setup_Click(object sender, RoutedEventArgs e) =>
        new SensorSetupWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void LogBox_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        service.Settings.LogEnabled = LogBox.IsChecked == true;
        service.Settings.Save();
    }

    private void KeepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || KeepBox.SelectedItem is not ComboBoxItem { Tag: int d }) return;
        service.Settings.KeepDays = d;
        service.Settings.Save();
        service.Log.Cleanup();
        ShowState();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SensorLog.Folder);
        Process.Start("explorer.exe", SensorLog.Folder);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!armed)
        {
            armed = true;
            DeleteButton.Style = (Style)FindResource("DangerButton");
            DeleteButton.Content = T("Click again to delete");
            disarm.Start();
            return;
        }
        service.Log.DeleteAll();
        Disarm();
        ShowState();
    }

    private void Disarm()
    {
        armed = false;
        disarm.Stop();
        DeleteButton.ClearValue(StyleProperty);
        DeleteButton.Content = T("Delete the log");
    }
}
