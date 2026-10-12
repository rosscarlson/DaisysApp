using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>Network Tests → gear: the hosts to ping, each one's alert level and how long it stays red, and the graph's top.</summary>
public partial class NetSettingsWindow : Window
{
    /// <summary>A host being edited: the numbers as typed, checked on Save.</summary>
    public sealed class HostRow
    {
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string AlertText { get; set; } = "50";
        public string HoldText { get; set; } = "15";
    }

    private readonly PerformanceSettings settings;
    private readonly ObservableCollection<HostRow> hosts;

    public NetSettingsWindow(PerformanceSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        hosts = new(settings.NetHosts.Select(h => new HostRow
        {
            Name = h.Name, Address = h.Address,
            AlertText = h.AlertMs.ToString(CultureInfo.CurrentCulture), HoldText = h.AlertSeconds.ToString(CultureInfo.CurrentCulture),
        }));
        HostList.ItemsSource = hosts;
        GraphMaxBox.Text = settings.PingGraphMaxMs.ToString(CultureInfo.CurrentCulture);

        RouterButton.IsEnabled = NetHost.Gateway() != null;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => hosts.Add(new HostRow());

    private void Router_Click(object sender, RoutedEventArgs e)
    {
        if (NetHost.Gateway() is { } gw && !hosts.Any(h => h.Address.Trim() == gw)) hosts.Insert(0, new HostRow { Name = T("Router"), Address = gw });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HostRow h) hosts.Remove(h);
    }

    private static bool Whole(string text, int min, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= min;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var list = new List<NetHost>();
        foreach (var h in hosts)
        {
            string name = h.Name.Trim(), address = h.Address.Trim();
            if (address.Length == 0 && name.Length == 0) continue;
            if (address.Length == 0) { Fail(F("{0} needs an IP address or hostname.", name)); return; }
            if (address.Contains(' ') || address.Contains("://"))
            {
                Fail(F("\"{0}\" isn't an IP address or hostname (just the name, e.g. www.example.com, without http://).", address));
                return;
            }
            string shown = name.Length > 0 ? name : address;
            if (!Whole(h.AlertText, 1, out int alert)) { Fail(F("{0}: Max is a number of milliseconds, 1 or more.", shown)); return; }
            if (!Whole(h.HoldText, 0, out int hold)) { Fail(F("{0}: Red for is a number of seconds, 0 or more.", shown)); return; }
            list.Add(new NetHost { Name = name, Address = address, AlertMs = alert, AlertSeconds = hold });
        }
        if (!Whole(GraphMaxBox.Text, 0, out int top)) { Fail(T("Top of the graph: enter a number of milliseconds (0 fits the graph to the responses).")); return; }
        settings.NetHosts = list;
        settings.PingGraphMaxMs = top;
        settings.Save();
        DialogResult = true;
    }

    private void Fail(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
