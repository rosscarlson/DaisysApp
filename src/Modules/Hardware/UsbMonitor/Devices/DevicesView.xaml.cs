using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaisysApp.Applets.Hardware.UsbMonitor.Models;
using DaisysApp.Applets.Hardware.UsbMonitor.ViewModels;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Devices;

/// <summary>One known device as the Devices list shows it.</summary>
internal sealed class KnownDeviceRow(KnownDevice device)
{
    public KnownDevice Device { get; } = device;
    private static string When(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

    public string Status => Device.HasProblem ? F("Problem (Code {0})", Device.Info.ProblemCode) : Device.Connected ? T("Connected") : T("Disconnected");
    /// <summary>Disconnected, or connected with a problem: the status shows bold red.</summary>
    public bool IsBad => !Device.Connected || Device.HasProblem;
    public string Name => Device.Name;
    public string Manufacturer => (Device.LastGood?.Manufacturer ?? Device.Info.Manufacturer) ?? "";
    public string VendorId => (Device.Info.VendorId ?? Device.LastGood?.VendorId) ?? "";
    public string ProductId => (Device.Info.ProductId ?? Device.LastGood?.ProductId) ?? "";
    public string SerialNumber => Device.Info.SerialNumber ?? "";
    public string DeviceClass => (Device.Info.DeviceClass ?? Device.LastGood?.DeviceClass) ?? "";
    public string Location => Device.Info.LocationInformation ?? "";
    public string LastConnected => When(Device.LastConnected);
    public string LastDisconnected => When(Device.LastDisconnected);
    public string LastWorking => When(Device.LastWorking);
    public string FirstSeen => When(Device.FirstSeen);
    public string Source => Device.FromWindows ? T("Windows") : T("Daisy's App");
    public string InstanceId => Device.InstanceId;
}

/// <summary>
/// Hardware → Devices: every USB device the USB Monitor has seen (and the ones Windows remembers), with when each last
/// connected, disconnected and worked. Double-click one for its details page.
/// </summary>
public partial class DevicesView : UserControl
{
    private readonly ObservableCollection<KnownDeviceRow> rows = new();
    private readonly GridColumnFilter filter;
    private bool dirty = true;

    public DevicesView()
    {
        InitializeComponent();
        DeviceGrid.ItemsSource = rows;
        filter = new GridColumnFilter(DeviceGrid);
        filter.Changed += () => ResetFiltersButton.Visibility = filter.IsActive ? Visibility.Visible : Visibility.Collapsed;
        // newest first: the last connected at the top
        var view = CollectionViewSource.GetDefaultView(rows);
        view.SortDescriptions.Add(new SortDescription(nameof(KnownDeviceRow.LastConnected), ListSortDirection.Descending));
        DeviceGrid.Columns[5].SortDirection = ListSortDirection.Descending;

        KnownDeviceStore.Instance.Changed += () => { dirty = true; if (IsVisible) Fill(); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true && dirty) Fill(); };
    }

    /// <summary>The list again from the store (sorting and filters stay).</summary>
    private void Fill()
    {
        dirty = false;
        var devices = KnownDeviceStore.Instance.Devices.ToList();
        rows.Clear();
        foreach (var d in devices) rows.Add(new KnownDeviceRow(d));
        int connected = devices.Count(d => d.Connected);
        CountText.Text = P(devices.Count, "{0} device", "{0} devices") + " · " + F("{0} connected", connected);
    }

    private KnownDeviceRow? Selected => DeviceGrid.SelectedItem as KnownDeviceRow;

    private void ShowDetails(KnownDeviceRow row) =>
        new DeviceDetailsWindow(row.Device) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void DeviceGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(DeviceGrid, (DependencyObject)e.OriginalSource) is not DataGridRow { Item: KnownDeviceRow row }) return;
        e.Handled = true;
        ShowDetails(row);
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) ShowDetails(row);
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row) KnownDeviceStore.Instance.Forget(row.Device);
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e) => filter.Reset();
}
