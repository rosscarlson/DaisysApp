using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>Settings → SimEffects → Analysis (since 3.24.0): the units and the tyres' temperature, as on the tab.</summary>
internal sealed class AnalysisSettingsView : UserControl
{
    private readonly ComboBox units, temperature;

    public AnalysisSettingsView(AnalysisApplet applet)
    {
        (units, temperature) = UnitBoxes(applet);
        var panel = new StackPanel { Width = 420 };
        panel.Children.Add(Ui.Header(T("Analysis")));
        panel.Children.Add(Ui.Field(T("Units"), units));
        panel.Children.Add(Ui.Field(T("Temperature"), temperature));
        var card = Ui.Card(panel);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        Content = Ui.Scroll(card);
    }

    public void Refresh() => Show(units, temperature, SettingsOf(units));

    private static AnalysisSettings SettingsOf(FrameworkElement e) => (AnalysisSettings)e.Tag;

    /// <summary>Units (Imperial / Metric) and Temperature (°F / °C), set on the tab or in Settings.</summary>
    public static (ComboBox Units, ComboBox Temperature) UnitBoxes(AnalysisApplet applet)
    {
        var s = applet.Settings;
        var units = new ComboBox { ItemsSource = new[] { T("Imperial"), T("Metric") }, Width = 110, VerticalAlignment = VerticalAlignment.Center, Tag = s,
            HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(units, T("Units"));
        var temperature = new ComboBox { ItemsSource = new[] { "°F", "°C" }, Width = 70, VerticalAlignment = VerticalAlignment.Center, Tag = s,
            HorizontalAlignment = HorizontalAlignment.Left, ToolTip = T("Tyre temperatures") };
        AutomationProperties.SetName(temperature, T("Temperature"));
        Show(units, temperature, s);
        bool showing = false;
        units.SelectionChanged += (_, _) =>
        {
            if (showing || units.SelectedIndex < 0 || (int)s.Units == units.SelectedIndex) return;
            s.Units = (AnalysisUnits)units.SelectedIndex;
            applet.UnitsChanged();
        };
        temperature.SelectionChanged += (_, _) =>
        {
            if (showing || temperature.SelectedIndex < 0 || (s.Celsius ? 1 : 0) == temperature.SelectedIndex) return;
            s.Temperature = (TemperatureUnit)temperature.SelectedIndex;
            applet.UnitsChanged();
        };
        units.IsVisibleChanged += (_, _) => { showing = true; Show(units, temperature, s); showing = false; };
        return (units, temperature);
    }

    public static void Show(ComboBox units, ComboBox temperature, AnalysisSettings s)
    {
        if (units.SelectedIndex != (int)s.Units) units.SelectedIndex = (int)s.Units;
        int t = s.Celsius ? 1 : 0;
        if (temperature.SelectedIndex != t) temperature.SelectedIndex = t;
    }
}
