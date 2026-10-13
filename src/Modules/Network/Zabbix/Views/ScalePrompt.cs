using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>Asks for the top of a unit's graphs: a number with K, M, G or T after it if wanted (1G = a billion; KiB steps for bytes); empty fits the data.</summary>
internal static class ScalePrompt
{
    /// <summary>The new top; 0 to fit the data again; null if cancelled.</summary>
    public static double? Ask(Window owner, ZUnit unit, double? current)
    {
        var w = new Window
        {
            Title = T("Graph scale"), Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);
        var box = new TextBox { Width = 160, Text = current is { } c ? unit.Text(c).Replace(unit.Unit, "").Trim() : "", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var error = Ui.Secondary("", wrap: true);
        var ok = new Button { Content = T("OK"), MinWidth = 80, IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        var cancel = new Button { Content = T("Cancel"), MinWidth = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        double? result = null;
        ok.Click += (_, _) =>
        {
            string s = box.Text.Trim();
            if (s.Length == 0) { result = 0; w.DialogResult = true; return; }
            if (Parse(s, unit) is { } v && v > 0) { result = v; w.DialogResult = true; }
            else error.Text = T("Type a number, e.g. 100, 2.5M or 1G.");
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var p = new StackPanel { Margin = new Thickness(20), MaxWidth = 380 };
        p.Children.Add(new TextBlock { Text = F("The top of every graph in {0} (leave it empty to fit the data):", unit.Unit.Length > 0 ? unit.Unit : T("numbers")), TextWrapping = TextWrapping.Wrap });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        var u = new TextBlock { Text = unit.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 6, 0, 0) };
        line.Children.Add(u);
        p.Children.Add(line);
        p.Children.Add(error);
        p.Children.Add(buttons);
        w.Content = p;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); Keyboard.Focus(box); };
        return w.ShowDialog() == true ? result : null;
    }

    private static double? Parse(string s, ZUnit unit)
    {
        s = s.Replace(" ", "");
        double mult = 1, step = unit.Unit is "B" or "Bps" ? 1024 : 1000;
        char last = char.ToUpperInvariant(s[^1]);
        int power = "KMGTP".IndexOf(last) + 1;
        if (power > 0) { mult = Math.Pow(step, power); s = s[..^1]; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v * mult : null;
    }
}
