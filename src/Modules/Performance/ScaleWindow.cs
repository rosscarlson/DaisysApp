using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Performance;

/// <summary>Sets the top of a metric's graphs (e.g. GPU clock up to 3000 MHz), or puts it back to fitting the data.</summary>
internal sealed class ScaleWindow : Window
{
    private readonly TextBox box = new() { Width = 110, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock error = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private double? result;

    /// <summary>The new top, 0 for "fit to the data", or null if cancelled.</summary>
    public static double? Ask(Window owner, MetricInfo m, double? current)
    {
        var w = new ScaleWindow(m, current) { Owner = owner };
        return w.ShowDialog() == true ? w.result : null;
    }

    private ScaleWindow(MetricInfo m, double? current)
    {
        Title = F("{0} scale", m.Name);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");

        var panel = new StackPanel { Margin = new Thickness(20) };
        var header = new TextBlock { Text = F("Top of the {0} graphs", m.Name) };
        header.SetResourceReference(StyleProperty, "CardHeader");
        panel.Children.Add(header);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        box.Text = current is double c ? c.ToString("0.##", CultureInfo.CurrentCulture) : "";
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = m.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(row);
        var hint = new TextBlock
        {
            Text = T("Its graphs (here, on its tile and in widgets) always go up to this, e.g. the highest clock your processor or graphics card reaches. Leave it empty to fit them to the readings."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(hint);
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var fit = new Button { Content = T("Fit to the data"), Margin = new Thickness(0, 0, 8, 0) };
        fit.Click += (_, _) => { result = 0; DialogResult = true; };
        var ok = new Button { Content = T("Save"), MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = T("Cancel"), MinWidth = 80, IsCancel = true };
        buttons.Children.Add(fit);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    private void Save()
    {
        string text = box.Text.Trim();
        if (text.Length == 0) { result = 0; DialogResult = true; return; }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && v > 0)
        {
            result = v;
            DialogResult = true;
            return;
        }
        error.Text = T("Enter a number above 0, or leave it empty.");
        error.Visibility = Visibility.Visible;
    }
}
