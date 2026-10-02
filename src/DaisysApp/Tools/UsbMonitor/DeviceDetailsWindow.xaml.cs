using System.Windows;
using DaisysApp.Tools.UsbMonitor.Models;
using DaisysApp.Theming;

namespace DaisysApp.Tools.UsbMonitor;

public partial class DeviceDetailsWindow : Window
{
    private readonly List<(string Key, string Value)> _properties;

    public DeviceDetailsWindow(DeviceInfo info)
    {
        InitializeComponent();
        Title = $"Device details — {info.DisplayName}";
        _properties = info.AllProperties().ToList();
        PropertyGrid.ItemsSource = _properties;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var lines = _properties.Select(p => $"{p.Key}\t{p.Value}");
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (PropertyGrid.SelectedItem is not (string key, string value)) return;
        Clipboard.SetText($"{key}\t{value}");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
