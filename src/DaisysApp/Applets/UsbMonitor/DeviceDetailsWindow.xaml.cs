using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Theming;
using DaisysApp.Applets.UsbMonitor.Models;

namespace DaisysApp.Applets.UsbMonitor;

/// <summary>Every property that could be read for one event's device, on one page, with copy buttons.</summary>
public partial class DeviceDetailsWindow : Window
{
    /// <summary>One row. A class with properties, because WPF can't bind to tuple fields.</summary>
    public sealed record DeviceProperty(string Name, string Value);

    private readonly List<DeviceProperty> _properties;

    public DeviceDetailsWindow(DeviceRecord record)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;

        var info = record.Info;
        Title = $"Device details — {info.DisplayName}";
        HeaderText.Text = info.DisplayName;
        SubText.Text = $"{record.EventLabel} at {record.Timestamp:yyyy-MM-dd HH:mm:ss.fff}";

        _properties = info.AllProperties().Select(p => new DeviceProperty(p.Key, p.Value)).ToList();
        PropertyList.ItemsSource = _properties;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, _properties.Select(p => $"{p.Name}\t{p.Value}")));
        CopiedText.Text = "Copied all properties.";
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceProperty p) return;
        Clipboard.SetText(p.Value);
        CopiedText.Text = $"Copied {p.Name}.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
