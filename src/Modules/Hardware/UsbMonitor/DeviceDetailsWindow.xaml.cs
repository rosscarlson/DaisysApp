using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using DaisysApp.Theming;
using DaisysApp.Applets.Hardware.UsbMonitor.Models;

namespace DaisysApp.Applets.Hardware.UsbMonitor;

/// <summary>Every property that could be read for one event's device, on one page, with copy buttons.</summary>
public partial class DeviceDetailsWindow : Window
{
    /// <summary>One row. A class with properties, because WPF can't bind to tuple fields.</summary>
    public sealed record DeviceProperty(string Name, string Value);

    private readonly List<DeviceProperty> _properties;
    /// <summary>"What it probably is", as text for Copy all (empty until it's worked out).</summary>
    private string _summary = "";

    public DeviceDetailsWindow(DeviceRecord record)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;

        var info = record.Info;
        Title = F("Device details — {0}", info.DisplayName);
        HeaderText.Text = info.DisplayName;
        SubText.Text = F("{0} at {1:yyyy-MM-dd HH:mm:ss.fff}", record.EventLabel, record.Timestamp);

        _properties = info.AllProperties().Select(p => new DeviceProperty(p.Key, p.Value)).ToList();
        if (record.Analysis != null)
        {
            AnalysisText.Text = record.Analysis;
            AnalysisBox.Visibility = Visibility.Visible;
            _properties.Insert(0, new DeviceProperty(T("Analysis"), record.Analysis));
        }
        PropertyList.ItemsSource = _properties;
        Identify(info, record.IsProblem);
    }

    /// <summary>A known device (the Devices sub-tab): when it came and went, what it is now, and how it differed when it last worked.</summary>
    public DeviceDetailsWindow(KnownDevice device)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        var info = device.Info;
        Title = F("Device details — {0}", device.Name);
        HeaderText.Text = device.Name;
        SubText.Text = device.HasProblem ? F("Connected, with a problem: {0}", info.ProblemText)
            : device.Connected ? T("Connected")
            : device.LastDisconnected is { } off ? F("Disconnected since {0:yyyy-MM-dd HH:mm:ss}", off) : T("Disconnected");
        static string When(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm:ss") ?? T("(never)");
        _properties = new List<DeviceProperty>
        {
            new(T("First seen"), When(device.FirstSeen)),
            new(T("Last connected"), When(device.LastConnected)),
            new(T("Last disconnected"), When(device.LastDisconnected)),
            new(T("Last working"), When(device.LastWorking)),
            new(T("Last problem"), device.LastProblem is { } p ? $"{When(p)} (Code {device.LastProblemCode}: {DeviceInfo.DescribeProblem(device.LastProblemCode)})" : T("(never)")),
            new(T("Events seen"), device.Events.ToString()),
            new(T("Known from"), device.FromWindows ? T("Windows' records (not seen by Daisy's App yet)") : T("Daisy's App")),
        };
        if (device.HasProblem && KnownDeviceStore.Instance.Describe(info) is var (text, _))
        {
            AnalysisText.Text = text;
            AnalysisBox.Visibility = Visibility.Visible;
        }
        _properties.AddRange(info.AllProperties().Select(p => new DeviceProperty(p.Key, p.Value)));
        // what was different when it last worked
        if (device.LastGood is { } good && !ReferenceEquals(good, info))
        {
            var now = info.AllProperties().ToDictionary(p => p.Key, p => p.Value);
            foreach (var (key, value) in good.AllProperties())
                if (now.TryGetValue(key, out var v) && v != value)
                    _properties.Add(new DeviceProperty(F("When it last worked: {0}", key), value));
        }
        PropertyList.ItemsSource = _properties;
        Identify(info, device.HasProblem || info.InstanceId.StartsWith("USB\\VID_0000&", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Fills the Probable Device row once it's worked out: that reads the USB device tree, so off the UI thread.</summary>
    private async void Identify(DeviceInfo info, bool problem)
    {
        DeviceExplainer.Explanation explanation;
        try
        {
            var explainer = new DeviceExplainer(info, problem);
            explanation = await Task.Run(explainer.Explain);
        }
        catch (Exception ex)
        {
            DaisysApp.Logging.ErrorLog.Write("DeviceDetailsWindow.Identify", ex);
            explanation = new(T("Couldn't work it out (see the error log)."), Array.Empty<DeviceExplainer.Section>());
        }
        ProbableText.Text = explanation.Short;
        IdentifyPanel.Children.Clear();
        foreach (var section in explanation.Sections)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            text.Inlines.Add(new Run(section.Heading) { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run(section.Text));
            IdentifyPanel.Children.Add(text);
        }
        _summary = string.Join(Environment.NewLine, explanation.Sections.Select(s => $"{s.Heading}: {s.Text.Replace("\n", " ")}")
            .Prepend(F("Probable Device: {0}", explanation.Short))) + Environment.NewLine + Environment.NewLine;
    }

    /// <summary>Shows or hides the explanation under the Probable Device row.</summary>
    private void ProbableRow_Click(object sender, MouseButtonEventArgs e)
    {
        bool show = IdentifyPanel.Visibility != Visibility.Visible;
        IdentifyPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProbableChevron.Text = show ? "\uE70E" : "\uE70D";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_summary + string.Join(Environment.NewLine, _properties.Select(p => $"{p.Name}\t{p.Value}")));
        CopiedText.Text = T("Copied all properties.");
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceProperty p) return;
        Clipboard.SetText(p.Value);
        CopiedText.Text = F("Copied {0}.", p.Name);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
