using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>Settings → SimEffects → Analysis (since 3.24.0): the units and the tyres' temperature, as on the tab; and
/// (since 3.24.2) where the Tires block's colours start, in the temperature's unit; (since 3.28.2) how many cars the
/// Leaderboard shows.</summary>
internal sealed class AnalysisSettingsView : UserControl
{
    private readonly ComboBox units, temperature;
    private readonly (TextBox Box, Func<float> Get, Action<float> Set)[] bands;
    private bool showing;

    public AnalysisSettingsView(AnalysisApplet applet)
    {
        (units, temperature) = UnitBoxes(applet);
        var s = applet.Settings;
        var panel = new StackPanel { Width = 420 };
        panel.Children.Add(Ui.Header(T("Analysis")));
        panel.Children.Add(Ui.Field(T("Units"), units));
        panel.Children.Add(Ui.Field(T("Temperature"), temperature));
        var tyres = Ui.Header(T("Tyre colours"));
        tyres.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(tyres);
        bands = new (TextBox, Func<float>, Action<float>)[]
        {
            (null!, () => s.TyreGreen, v => s.TyreGreen = v),
            (null!, () => s.TyreOrange, v => s.TyreOrange = v),
            (null!, () => s.TyreRed, v => s.TyreRed = v),
        };
        string[] names = { T("Green from"), T("Orange from"), T("Red from") };
        for (int i = 0; i < bands.Length; i++)
        {
            var (_, get, set) = bands[i];
            var box = Ui.Number(0, -50, 500, n =>
            {
                if (showing) return;
                set(s.Celsius ? n * 1.8f + 32 : n);
                applet.UnitsChanged();
            });
            box.ToolTip = T("Tyre temperature");
            AutomationProperties.SetName(box, names[i]);
            bands[i] = (box, get, set);
            panel.Children.Add(Ui.Field(names[i], box));
        }
        ShowBands();
        var board = Ui.Header(T("Leaderboard"));
        board.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(board);
        var rows = Ui.Number(s.BoardRows, 3, BlockView.MostBoardRows, n => { s.BoardRows = n; s.Save(); });
        rows.ToolTip = T("Cars shown, yours in the middle");
        AutomationProperties.SetName(rows, T("Cars"));
        panel.Children.Add(Ui.Field(T("Cars"), rows));
        var card = Ui.Card(panel);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Top;
        Content = Ui.Scroll(card);
    }

    public void Refresh()
    {
        Show(units, temperature, SettingsOf(units));
        ShowBands();
    }

    /// <summary>The bands' boxes in °F or °C (kept in °F).</summary>
    private void ShowBands()
    {
        var s = SettingsOf(units);
        showing = true;
        foreach (var (box, get, _) in bands)
        {
            string text = $"{(s.Celsius ? (get() - 32) / 1.8f : get()):0}";
            // (the one being typed in keeps what's typed)
            if (box.Text != text && !box.IsKeyboardFocused) box.Text = text;
        }
        showing = false;
    }

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
