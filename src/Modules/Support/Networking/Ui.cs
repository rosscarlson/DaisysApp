using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Small pieces the Networking Tools' code-built views share (the app's styles, by their keys).</summary>
internal static class Ui
{
    /// <summary>The colours of a list of hosts or networks, in order (the first is the theme's accent).</summary>
    public static readonly string[] Colors = ["AccentBrush", "#7FD07A", "#F7A541", "#B48EF0", "#FF7B72", "#F2CC60", "#E58AD8", "#5FD3C6", "#9CC3FF", "#D9A36B"];
    public const string Orange = "#F7A541";

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    public static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }

    public static TextBlock Secondary(string text = "", bool wrap = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 14)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    /// <summary>A button with an icon (and a caption, if there is one).</summary>
    public static Button IconButton(string glyph, string text, string? tip = null, Action? click = null, bool accent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 13);
        icon.Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0);
        panel.Children.Add(icon);
        if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = panel, ToolTip = tip, Padding = text.Length > 0 ? new Thickness(10, 4, 10, 4) : new Thickness(8, 4, 8, 4) };
        System.Windows.Automation.AutomationProperties.SetName(b, text.Length > 0 ? text : tip ?? "");
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        if (click != null) b.Click += (_, _) => click();
        return b;
    }

    public static Button Button(string text, Action click, string? tip = null, bool accent = false)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 4, 10, 4) };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>
    /// Calls <paramref name="changed"/> when the box is ticked or unticked, however (a click, the keyboard, UI
    /// Automation, which raises no Click).
    /// </summary>
    public static void OnToggle(ToggleButton box, Action<bool> changed)
    {
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
    }

    /// <summary>A rounded on/off chip (the app's ChipToggle style).</summary>
    public static ToggleButton Chip(string text, bool on, string? tip = null)
    {
        var t = new ToggleButton { Content = text, IsChecked = on, ToolTip = tip, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        t.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        return t;
    }

    public static ProgressBar Bar(double height)
    {
        var b = new ProgressBar { Height = height, BorderThickness = new Thickness(0) };
        b.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        b.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        return b;
    }

    /// <summary>A theme brush key or a colour.</summary>
    public static Brush BrushOf(FrameworkElement owner, string key) => key.StartsWith('#')
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(key))
        : owner.TryFindResource(key) as Brush ?? Brushes.DodgerBlue;

    /// <summary>The tab's page: everything in it scrolls, with room for the scroll bar.</summary>
    public static ScrollViewer Page(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new Border { Margin = new Thickness(0, 0, 6, 0), Child = content },
    };

    /// <summary>A card's header row: the title on the left, <paramref name="right"/> at the right.</summary>
    public static DockPanel HeaderRow(UIElement title, params UIElement[] right)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        foreach (var r in right.Reverse())
        {
            DockPanel.SetDock(r, Dock.Right);
            if (r is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(r);
        }
        if (title is FrameworkElement t) { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0); }
        row.Children.Add(title);
        return row;
    }

    /// <summary>A card's on/off box with its title, as Performance's Network Tests and Speed Test cards have.</summary>
    public static CheckBox OnBox(string title, bool on, string tip)
    {
        var header = Header(title);
        header.Margin = new Thickness(0);
        var box = new CheckBox { Content = header, IsChecked = on, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(box, title + " on");
        return box;
    }

    /// <summary>The gear button at the right of a card's header.</summary>
    public static Button Gear(string tip, Action click) => IconButton("", "", tip, click);

    /// <summary>Label / value rows, aligned (as Performance's System card).</summary>
    public sealed class Fields : Grid
    {
        public Fields()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public void Clear()
        {
            Children.Clear();
            RowDefinitions.Clear();
        }

        /// <summary>Adds a row (nothing if <paramref name="value"/> is empty), and returns its value's text.</summary>
        public TextBlock? Add(string label, string? value, string? tip = null, Brush? color = null)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int r = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Secondary(label);
            l.Margin = new Thickness(0, 0, 14, 5);
            SetRow(l, r);
            Children.Add(l);
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5), ToolTip = tip };
            if (color != null) v.Foreground = color;
            SetRow(v, r);
            SetColumn(v, 1);
            Children.Add(v);
            return v;
        }
    }

    /// <summary>A read-only table in the app's look.</summary>
    public static DataGrid Table(double height = double.NaN)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            BorderThickness = new Thickness(0), EnableRowVirtualization = true, Height = height,
            SelectionMode = DataGridSelectionMode.Single,
        };
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        return grid;
    }

    public static DataGridTextColumn Column(string header, string path, double width, string? sortBy = null, bool right = false)
    {
        var c = new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(-width, DataGridLengthUnitType.Star),
            SortMemberPath = sortBy ?? path,
        };
        if (right)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            c.ElementStyle = style;
        }
        return c;
    }

    /// <summary>A drop-down of (text, value) items, with <paramref name="current"/> picked (or the first).</summary>
    public static ComboBox Choice<TV>(IEnumerable<(string Text, TV Value)> items, TV current, Action<TV> picked, double width = 120)
    {
        var box = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, current)) ?? (box.Items.Count > 0 ? box.Items[0] : null);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: TV v }) picked(v); };
        return box;
    }

    /// <summary>Opens a web page in the browser.</summary>
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't open {url}: {ex.Message}"); }
    }

    public static string Ms(double v) => double.IsNaN(v) ? "—" : v < 10 ? $"{v:0.#} ms" : $"{v:0} ms";

    public static string Mbps(double v) => double.IsNaN(v) ? "—" : v >= 100 ? $"{v:0} Mbit/s" : $"{v:0.0} Mbit/s";

    /// <summary>A link speed (bits a second): 1 Gbit/s, 866.7 Mbit/s…</summary>
    public static string LinkSpeed(long bps) => bps <= 0 ? "" : bps >= 1_000_000_000
        ? $"{bps / 1e9:0.##} Gbit/s"
        : $"{bps / 1e6:0.#} Mbit/s";

    public static string Duration(TimeSpan t) => t.TotalSeconds < 60 ? F("{0:0} s", t.TotalSeconds)
        : t.TotalMinutes < 60 ? F("{0}m {1:00}s", (int)t.TotalMinutes, t.Seconds)
        : F("{0}h {1:00}m", (int)t.TotalHours, t.Minutes);

    /// <summary>1, 2, 2.5 or 5 × a power of ten, at or above <paramref name="v"/>.</summary>
    public static double NiceCeiling(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    /// <summary>A time, with the date when it isn't today.</summary>
    public static string When(DateTime t) => t.Date == DateTime.Today ? t.ToString("T") : t.ToString("g");
}

/// <summary>A graph's values in milliseconds.</summary>
internal sealed class MsUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly MsUnit Instance = new();
    public string Unit => "ms";
    public string Text(double value) => Ui.Ms(value);
}

/// <summary>A graph's values in Mbit/s.</summary>
internal sealed class MbpsUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly MbpsUnit Instance = new();
    public string Unit => "Mbit/s";
    public string Text(double value) => Ui.Mbps(value);
}

/// <summary>A graph's values in percent (Wi-Fi signal quality).</summary>
internal sealed class PercentUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly PercentUnit Instance = new();
    public string Unit => "%";
    public string Text(double value) => double.IsNaN(value) ? "—" : $"{value:0}%";
}
