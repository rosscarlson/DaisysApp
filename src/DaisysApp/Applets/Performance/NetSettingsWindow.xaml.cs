using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Performance;

/// <summary>Network tests → gear: the hosts to ping, and the speed test schedule.</summary>
public partial class NetSettingsWindow : Window
{
    private static readonly int[] Intervals = [10, 15, 30, 60, 180, 360];
    private static readonly int[] Lengths = [5, 10, 15, 20];

    private readonly PerformanceSettings settings;
    private readonly ObservableCollection<NetHost> hosts;

    public NetSettingsWindow(PerformanceSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        hosts = new(settings.NetHosts.Select(h => new NetHost { Name = h.Name, Address = h.Address }));
        HostList.ItemsSource = hosts;

        SpeedBox.IsChecked = settings.SpeedTestEnabled;
        foreach (int m in Intervals)
            IntervalBox.Items.Add(new ComboBoxItem { Content = m < 60 ? $"{m} minutes" : m == 60 ? "hour" : $"{m / 60} hours", Tag = m });
        foreach (int s in Lengths)
            LengthBox.Items.Add(new ComboBoxItem { Content = $"{s} s each way", Tag = s });
        IntervalBox.SelectedItem = IntervalBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.SpeedTestMinutes) ?? IntervalBox.Items[0];
        LengthBox.SelectedItem = LengthBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.SpeedTestSeconds) ?? LengthBox.Items[1];
        RouterButton.IsEnabled = NetHost.Gateway() != null;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => hosts.Add(new NetHost());

    private void Router_Click(object sender, RoutedEventArgs e)
    {
        if (NetHost.Gateway() is { } gw && !hosts.Any(h => h.Address.Trim() == gw)) hosts.Insert(0, new NetHost { Name = "Router", Address = gw });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NetHost h) hosts.Remove(h);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var list = hosts.Select(h => new NetHost { Name = h.Name.Trim(), Address = h.Address.Trim() })
            .Where(h => h.Address.Length > 0 || h.Name.Length > 0).ToList();
        if (list.FirstOrDefault(h => h.Address.Length == 0) is { } missing)
        {
            ErrorText.Text = $"{missing.Name} needs an IP address or hostname.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (list.FirstOrDefault(h => h.Address.Contains(' ') || h.Address.Contains("://")) is { } bad)
        {
            ErrorText.Text = $"\"{bad.Address}\" isn't an IP address or hostname (just the name, e.g. www.example.com, without http://).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        settings.NetHosts = list;
        settings.SpeedTestEnabled = SpeedBox.IsChecked == true;
        if (IntervalBox.SelectedItem is ComboBoxItem { Tag: int m }) settings.SpeedTestMinutes = m;
        if (LengthBox.SelectedItem is ComboBoxItem { Tag: int s }) settings.SpeedTestSeconds = s;
        settings.Save();
        DialogResult = true;
    }
}
