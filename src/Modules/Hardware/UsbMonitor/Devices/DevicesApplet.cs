using System.Windows;
using DaisysApp.Applets.Hardware.UsbMonitor.Models;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Devices;

/// <summary>Devices, a sub-tab of the Hardware tab: the USB devices known (<see cref="KnownDeviceStore"/>), fed by the USB Monitor.</summary>
public sealed class DevicesApplet : IApplet
{
    private readonly DevicesView view = new();

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() => KnownDeviceStore.Instance.Save();

    public void Dispose() { }
}
