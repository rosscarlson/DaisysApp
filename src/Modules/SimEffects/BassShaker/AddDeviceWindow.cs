using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Audio;
using DaisysApp.Theming;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>Add Device: the playback device, the transducer on it (which sets the frequencies it's sent), and its channels.</summary>
internal sealed class AddDeviceWindow : Window
{
    public ShakerDevice? Added { get; private set; }

    public AddDeviceWindow()
    {
        Title = T("Add Device");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        List<DeviceInfo> list;
        using (var service = new DeviceService()) list = service.GetDevices();
        var device = new ComboBox { ItemsSource = list, DisplayMemberPath = nameof(DeviceInfo.Display), SelectedIndex = list.Count > 0 ? 0 : -1 };
        var type = new ComboBox { ItemsSource = TransducerType.All, DisplayMemberPath = nameof(TransducerType.Name) };
        var range = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        range.SetResourceReference(StyleProperty, "SecondaryText");
        var channels = new ComboBox { ItemsSource = ChannelNames, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = Channels.All };
        var add = new Button { Content = T("Add"), MinWidth = 90, IsDefault = true, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        add.SetResourceReference(StyleProperty, "AccentButton");
        type.SelectionChanged += (_, _) =>
        {
            add.IsEnabled = type.SelectedItem != null && device.SelectedItem != null;
            range.Text = type.SelectedItem is TransducerType t ? F("{0}–{1} Hz, strongest at {2} Hz", t.MinHz, t.MaxHz, t.PeakHz) : "";
        };
        device.SelectionChanged += (_, _) => add.IsEnabled = type.SelectedItem != null && device.SelectedItem != null;
        add.Click += (_, _) =>
        {
            if (device.SelectedItem is not DeviceInfo d || type.SelectedItem is not TransducerType t) return;
            Added = new ShakerDevice
            {
                DeviceId = d.Id, Name = d.Name, Type = t.Id, MinHz = t.MinHz, MaxHz = t.MaxHz,
                Channels = channels.SelectedValue is Channels c ? c : Channels.All,
            };
            DialogResult = true;
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Label(T("Playback Device")));
        panel.Children.Add(device);
        panel.Children.Add(Label(T("Transducer")));
        panel.Children.Add(type);
        panel.Children.Add(range);
        panel.Children.Add(Label(T("Channels")));
        panel.Children.Add(channels);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(add);
        buttons.Children.Add(new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true });
        panel.Children.Add(buttons);
        Content = panel;
    }

    public static readonly KeyValuePair<Channels, string>[] ChannelNames =
    {
        new(Channels.All, T("All")), new(Channels.Left, T("Left")), new(Channels.Right, T("Right")), new(Channels.Lfe, T("LFE (subwoofer)")),
    };

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
}
