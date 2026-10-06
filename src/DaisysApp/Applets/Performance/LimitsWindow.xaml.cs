using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Performance;

/// <summary>Edits the orange and red levels for some limits (and, for the process list, its refresh rate).</summary>
public partial class LimitsWindow : Window
{
    private static readonly int[] RefreshOptions = { 500, 1000, 2000, 5000 };

    private readonly PerfLimits limits;
    private readonly IReadOnlyList<LimitDef> defs;
    private readonly List<(LimitDef Def, TextBox Warn, TextBox Critical)> rows = new();
    private readonly PerformanceSettings? refreshSettings;

    /// <param name="refreshSettings">Pass to also show the process list's refresh rate.</param>
    public LimitsWindow(PerfLimits limits, IReadOnlyList<LimitDef> defs, string title, PerformanceSettings? refreshSettings = null)
    {
        this.limits = limits;
        this.defs = defs;
        this.refreshSettings = refreshSettings;
        InitializeComponent();
        TitleText.Text = title;
        IntroText.Text = refreshSettings != null
            ? "A process at or above a level is highlighted orange or red in the list."
            : "At or above these levels the tile turns orange or red (judged on a 3-second average, so a single spike doesn't), and the graphs show them as dashed lines.";

        AddHeader();
        foreach (var d in defs) AddRow(d);

        if (refreshSettings != null)
        {
            RefreshCard.Visibility = Visibility.Visible;
            foreach (int ms in RefreshOptions)
                RefreshBox.Items.Add(new ComboBoxItem { Content = ms < 1000 ? $"{ms / 1000.0:0.0} seconds" : ms == 1000 ? "1 second" : $"{ms / 1000} seconds", Tag = ms });
            RefreshBox.SelectedItem = RefreshBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == refreshSettings.ProcessRefreshMs) ?? RefreshBox.Items[1];
        }
    }

    private void AddHeader()
    {
        LimitGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(Label("", secondary: true), 0, 0);
        Add(Label("Orange at", secondary: true, swatch: "#F7A541"), 0, 1);
        Add(Label("Red at", secondary: true, swatch: "DangerBrush"), 0, 2);
    }

    private void AddRow(LimitDef d)
    {
        int row = LimitGrid.RowDefinitions.Count;
        LimitGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var (warn, critical) = limits.Get(d.Key);
        var name = new TextBlock { Text = d.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 0) };
        var warnBox = Box(warn, d.Unit);
        var critBox = Box(critical, d.Unit);
        Add(name, row, 0);
        Add(warnBox, row, 1);
        Add(critBox, row, 2);
        rows.Add((d, (TextBox)((DockPanel)warnBox).Children[1], (TextBox)((DockPanel)critBox).Children[1]));
    }

    private static FrameworkElement Box(double value, string unit)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 6, 10, 0) };
        var unitText = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), MinWidth = 34 };
        unitText.SetResourceReference(StyleProperty, "SecondaryText");
        DockPanel.SetDock(unitText, Dock.Right);
        panel.Children.Add(unitText);
        panel.Children.Add(new TextBox { Text = value.ToString("0.##", CultureInfo.CurrentCulture), HorizontalContentAlignment = HorizontalAlignment.Right });
        return panel;
    }

    private static FrameworkElement Label(string text, bool secondary, string? swatch = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        if (swatch != null)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (swatch.StartsWith('#')) dot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatch));
            else dot.SetResourceReference(Border.BackgroundProperty, swatch);
            panel.Children.Add(dot);
        }
        var t = new TextBlock { Text = text };
        if (secondary) t.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(t);
        return panel;
    }

    private void Add(FrameworkElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        LimitGrid.Children.Add(e);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (d, warn, critical) in rows)
        {
            warn.Text = d.Warn.ToString("0.##", CultureInfo.CurrentCulture);
            critical.Text = d.Critical.ToString("0.##", CultureInfo.CurrentCulture);
        }
        if (refreshSettings != null) RefreshBox.SelectedItem = RefreshBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == 1000);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var values = new List<(string, double, double)>();
        foreach (var (d, warnBox, critBox) in rows)
        {
            if (!double.TryParse(warnBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double warn) ||
                !double.TryParse(critBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double critical) || warn < 0 || critical < 0)
            {
                ShowError($"{d.Name}: enter numbers.");
                return;
            }
            if (critical < warn)
            {
                ShowError($"{d.Name}: red has to be at or above orange.");
                return;
            }
            values.Add((d.Key, warn, critical));
        }
        if (refreshSettings != null && RefreshBox.SelectedItem is ComboBoxItem { Tag: int ms }) refreshSettings.ProcessRefreshMs = ms;
        limits.Set(values); // also saves the settings
        DialogResult = true;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
