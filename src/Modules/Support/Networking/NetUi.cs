using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>The Network Tools' own pieces: values, addresses and the selectable text that copies them (the rest is Core's <see cref="Ui"/>).</summary>
internal static class NetUi
{
    /// <summary>The colours of a list of hosts or networks, in order (the first is the theme's accent).</summary>
    public static readonly string[] Colors = ["AccentBrush", "#7FD07A", "#F7A541", "#B48EF0", "#FF7B72", "#F2CC60", "#E58AD8", "#5FD3C6", "#9CC3FF", "#D9A36B"];
    public const string Orange = "#F7A541";

    /// <summary>A button with an icon (and a caption, if there is one).</summary>
    public static Button IconButton(string glyph, string text, string? tip = null, Action? click = null, bool accent = false)
    {
        var b = Ui.IconButton(glyph, text, tip, click, accent);
        b.Padding = text.Length > 0 ? new Thickness(10, 4, 10, 4) : new Thickness(8, 4, 8, 4);
        return b;
    }

    /// <summary>A card's on/off box with its title, as Performance's Network Tests and Speed Test cards have.</summary>
    public static CheckBox OnBox(string title, bool on, string tip)
    {
        var header = Ui.Header(title);
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

        /// <summary>Adds a row (nothing if <paramref name="value"/> is empty), and returns its value's text (selectable).</summary>
        public TextBox? Add(string label, string? value, string? tip = null, Brush? color = null)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int r = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Secondary(label);
            l.Margin = new Thickness(0, 0, 14, 5);
            l.VerticalAlignment = VerticalAlignment.Top;
            SetRow(l, r);
            Children.Add(l);
            var v = Selectable(value);
            v.Margin = new Thickness(0, 0, 0, 5);
            v.ToolTip = tip;
            v.VerticalAlignment = VerticalAlignment.Top;
            if (color != null) v.Foreground = color;
            SetRow(v, r);
            SetColumn(v, 1);
            Children.Add(v);
            return v;
        }
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

    // ---------------------------------------------------------------- copying addresses and names

    private static readonly char[] Separators = [' ', ',', ';', '(', ')', '[', ']', '{', '}', '\n', '\r', '\t', '→', '/', '·', '<', '>', '"', '\'', '=', '|'];
    private static readonly Regex HostName = new(@"^(?=.{4,253}$)(?:[A-Za-z0-9_](?:[A-Za-z0-9_-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z][A-Za-z0-9-]{0,61}[A-Za-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.Compiled);

    /// <summary>The IP addresses and host names in some text ("Router (192.168.1.1)", "TCP example.com:443"…), each once.</summary>
    public static (List<string> Ips, List<string> Hosts) Addresses(IEnumerable<string?> texts)
    {
        var ips = new List<string>();
        var hosts = new List<string>();
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim('.', ':', '…', '%', '*');
                if (t.Length < 2) continue;
                // a port after it (one colon: an IPv6 address has more)
                if (t.Count(c => c == ':') == 1 && t.LastIndexOf(':') is int colon && t[(colon + 1)..].All(char.IsDigit)) t = t[..colon];
                if (Ipv4.IsMatch(t) && System.Net.IPAddress.TryParse(t, out _)
                    || t.Count(c => c == ':') >= 2 && t.Any(Uri.IsHexDigit) && System.Net.IPAddress.TryParse(t, out var v6) && v6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    if (!ips.Contains(t)) ips.Add(t);
                }
                else if (HostName.IsMatch(t) && !hosts.Contains(t, StringComparer.OrdinalIgnoreCase)) hosts.Add(t);
            }
        }
        return (ips, hosts);
    }

    /// <summary>Puts text on the clipboard (tried again if another program has it open).</summary>
    public static void Copy(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetDataObject(text, true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(50); }
        }
    }

    private static readonly object DynamicTag = new();

    private static MenuItem Item(string text, Action click, string? gesture = null)
    {
        var m = new MenuItem { Header = text, InputGestureText = gesture ?? "", Tag = DynamicTag };
        m.Click += (_, _) => click();
        return m;
    }

    /// <summary>Copy IP / Copy Host for each address and name in <paramref name="texts"/>, inserted at <paramref name="at"/>; returns where the next goes.</summary>
    private static int AddCopyItems(ContextMenu menu, IEnumerable<string?> texts, int at)
    {
        var (ips, hosts) = Addresses(texts);
        foreach (var ip in ips) menu.Items.Insert(at++, Item(T("Copy IP"), () => Copy(ip), ip));
        foreach (var h in hosts) menu.Items.Insert(at++, Item(T("Copy Host"), () => Copy(h), h));
        return at;
    }

    /// <summary>The actions and address texts of a list's items, for <see cref="ItemMenu"/> and the selectable text in it.</summary>
    private sealed record ItemMenuInfo(Func<object, IEnumerable<string?>> Texts, (string Text, Action<object> Click)[] Items);

    private static readonly DependencyProperty ItemMenuProperty = DependencyProperty.RegisterAttached("ItemMenu", typeof(ItemMenuInfo), typeof(NetUi));

    /// <summary>
    /// A right-click menu on a list's (or table's) items: <paramref name="items"/> (Details…), then Copy IP / Copy Host for the
    /// addresses and names in the item's <paramref name="texts"/>. Items already in the list's menu (Make Widget) stay under them.
    /// </summary>
    public static void ItemMenu(FrameworkElement list, Func<object, IEnumerable<string?>> texts, params (string Text, Action<object> Click)[] items)
    {
        list.SetValue(ItemMenuProperty, new ItemMenuInfo(texts, items));
        var menu = list.ContextMenu ??= new ContextMenu();
        list.ContextMenuOpening += (_, e) =>
        {
            object? item = (e.OriginalSource as FrameworkElement)?.DataContext ?? (e.OriginalSource as FrameworkContentElement)?.DataContext;
            if (item == list.DataContext) item = null;
            if (list is DataGrid grid && item != null) grid.SelectedItem = item;
            Fill(menu, item, list, null);
            if (menu.Items.Count == 0) e.Handled = true;
        };
    }

    /// <summary>A menu's items for <paramref name="item"/> of <paramref name="list"/> (and the selectable text right-clicked, if it was).</summary>
    private static void Fill(ContextMenu menu, object? item, FrameworkElement? list, TextBox? box)
    {
        foreach (var old in menu.Items.OfType<FrameworkElement>().Where(i => i.Tag == DynamicTag).ToList()) menu.Items.Remove(old);
        int at = 0;
        var info = list?.GetValue(ItemMenuProperty) as ItemMenuInfo;
        if (box != null)
        {
            string selected = box.SelectedText.Length > 0 ? box.SelectedText : box.Text;
            menu.Items.Insert(at++, Item(T("Copy"), () => Copy(selected), "Ctrl+C"));
        }
        if (item != null && info != null)
            foreach (var (text, click) in info.Items) menu.Items.Insert(at++, Item(text, () => click(item)));
        at = AddCopyItems(menu, item != null && info != null ? info.Texts(item) : box != null ? [box.Text] : [], at);
        if (at > 0 && menu.Items.Count > at) menu.Items.Insert(at, new Separator { Tag = DynamicTag });
    }

    private static Style? selectableStyle;

    /// <summary>Selectable text that looks like a TextBlock: right-click for Copy, Copy IP and Copy Host.</summary>
    public static Style SelectableStyle => selectableStyle ??= MakeSelectableStyle();

    private static Style MakeSelectableStyle()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(UIElement.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(TextBox)) { VisualTree = host }));
        style.Setters.Add(new Setter(TextBoxBase.IsReadOnlyProperty, true));
        style.Setters.Add(new Setter(TextBoxBase.IsReadOnlyCaretVisibleProperty, false));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        style.Setters.Add(new Setter(Control.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, new DynamicResourceExtension("AccentBrush")));
        // the colour of the text around it (a row's or a field's), as a TextBlock would have
        style.Setters.Add(new Setter(Control.ForegroundProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(FrameworkElement) } }));
        style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is TextBox b) b.ContextMenu ??= new ContextMenu(); })));
        style.Setters.Add(new EventSetter(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler((s, e) =>
        {
            if (s is not TextBox box) return;
            box.ContextMenu ??= new ContextMenu();
            // inside a list with its own menu: that item's actions and addresses too
            FrameworkElement? list = null;
            for (DependencyObject? p = box; p != null; p = VisualTreeHelper.GetParent(p))
                if (p is FrameworkElement f && f.GetValue(ItemMenuProperty) != null) { list = f; break; }
            Fill(box.ContextMenu, list != null ? box.DataContext : null, list, box);
            e.Handled = false;
        })));
        style.Seal();
        return style;
    }

    /// <summary>Selectable text (see <see cref="SelectableStyle"/>).</summary>
    public static TextBox Selectable(string text, bool wrap = true) => new()
    {
        Style = SelectableStyle, Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    /// <summary>A table column whose text can be selected and copied (addresses, names).</summary>
    public static DataGridTemplateColumn CopyColumn(string header, string path, double width, string? sortBy = null)
    {
        var box = new FrameworkElementFactory(typeof(TextBox));
        box.SetValue(FrameworkElement.StyleProperty, SelectableStyle);
        box.SetValue(TextBox.TextWrappingProperty, TextWrapping.NoWrap);
        box.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });
        return new DataGridTemplateColumn
        {
            Header = header, CellTemplate = new DataTemplate { VisualTree = box }, SortMemberPath = sortBy ?? path,
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(-width, DataGridLengthUnitType.Star),
        };
    }

    /// <summary>The item of a list that an event (a click) came from, or null.</summary>
    public static object? ItemOf(RoutedEventArgs e, FrameworkElement list)
    {
        object? item = (e.OriginalSource as FrameworkElement)?.DataContext ?? (e.OriginalSource as FrameworkContentElement)?.DataContext;
        return item == list.DataContext ? null : item;
    }
}

/// <summary>A graph's values in milliseconds.</summary>
internal sealed class MsUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly MsUnit Instance = new();
    public string Unit => "ms";
    public string Text(double value) => NetUi.Ms(value);
}

/// <summary>A graph's values in Mbit/s.</summary>
internal sealed class MbpsUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly MbpsUnit Instance = new();
    public string Unit => "Mbit/s";
    public string Text(double value) => NetUi.Mbps(value);
}

/// <summary>A graph's values in percent (Wi-Fi signal quality).</summary>
internal sealed class PercentUnit : DaisysApp.Shared.Charts.IGraphUnit
{
    public static readonly PercentUnit Instance = new();
    public string Unit => "%";
    public string Text(double value) => double.IsNaN(value) ? "—" : $"{value:0}%";
}
