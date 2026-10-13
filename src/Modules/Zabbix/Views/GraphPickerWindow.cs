using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Applets.Zabbix.Api;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Zabbix.Views;

/// <summary>
/// New graph: every value the host has that can be drawn, to tick the ones to draw together (e.g. both network cards'
/// traffic in and out, four lines). Once one is ticked, the ones in another unit are greyed out, since they can't share
/// its scale. Gives back the graph's title and its items' keys.
/// </summary>
internal sealed class GraphPickerWindow : Window
{
    private sealed class Row
    {
        public required ZItem Item;
        public required CheckBox Box;
        public required FrameworkElement Element;
    }

    private readonly List<Row> rows = new();
    private readonly TextBox title = new() { MinWidth = 320 };
    private readonly TextBox search = new() { Width = 220 };
    private readonly TextBlock count = Ui.Secondary();
    private readonly Button save = new() { MinWidth = 110, IsDefault = true };
    private bool titleTyped;

    public string GraphTitle => title.Text.Trim();
    public List<string> Keys => rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item.Key).ToList();

    public GraphPickerWindow(string hostName, IReadOnlyList<ZItem> items, CustomGraph? editing = null)
    {
        Title = editing == null ? F("New graph · {0}", hostName) : F("Edit graph · {0}", hostName);
        Width = 720;
        Height = 700;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = editing == null ? T("New graph") : T("Edit graph"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        top.Children.Add(Ui.Secondary(T("Tick the values to draw on one graph. They have to be in the same unit (once one is ticked, the others are greyed out)."), wrap: true));
        var titleLine = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var titleLabel = new TextBlock { Text = T("Title"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(titleLabel, Dock.Left);
        titleLine.Children.Add(titleLabel);
        titleLine.Children.Add(title);
        top.Children.Add(titleLine);
        var searchLine = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var glass = Ui.Icon("", 13);
        glass.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(glass, Dock.Left);
        DockPanel.SetDock(search, Dock.Left);
        searchLine.Children.Add(glass);
        searchLine.Children.Add(search);
        count.Margin = new Thickness(12, 0, 0, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        searchLine.Children.Add(count);
        search.ToolTip = T("Show only values whose name or key contains this");
        top.Children.Add(searchLine);

        var list = new StackPanel();
        foreach (var item in items.Where(i => i.IsNumeric).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
            var name = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            var detail = Ui.Secondary($"{item.Key}" + (item.Units.Length > 0 ? $" · {item.Unit.Unit}" : "") + $" · {item.ValueText}");
            detail.FontSize = 11.5;
            var text = new StackPanel { Children = { name, detail } };
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = System.Windows.Media.Brushes.Transparent };
            DockPanel.SetDock(box, Dock.Left);
            line.Children.Add(box);
            line.Children.Add(text);
            var row = new Row { Item = item, Box = box, Element = line };
            line.MouseLeftButtonUp += (_, e) =>
            {
                if (!box.IsEnabled || e.OriginalSource is CheckBox) return;
                box.IsChecked = box.IsChecked != true;
            };
            box.Checked += (_, _) => Picked();
            box.Unchecked += (_, _) => Picked();
            rows.Add(row);
            list.Children.Add(line);
        }
        var scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var listCard = Ui.Card(scroller);
        listCard.Margin = new Thickness(0);

        save.Content = editing == null ? T("Add graph") : T("Save");
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var dock = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(buttons);
        dock.Children.Add(listCard);
        Content = dock;

        if (editing != null)
        {
            title.Text = editing.Title;
            titleTyped = true;
            foreach (var r in rows) r.Box.IsChecked = editing.ItemKeys.Contains(r.Item.Key);
        }
        title.TextChanged += (_, _) => { if (!settingTitle) titleTyped = title.Text.Length > 0; };
        search.TextChanged += (_, _) => Filter();
        Picked();
        Filter();
        Loaded += (_, _) => search.Focus();
    }

    private bool settingTitle;

    /// <summary>Greys out what can't go with what's ticked, and suggests a title.</summary>
    private void Picked()
    {
        var picked = rows.Where(r => r.Box.IsChecked == true).ToList();
        string? kind = picked.FirstOrDefault()?.Item.Unit.Kind;
        foreach (var r in rows)
        {
            bool fits = kind == null || r.Item.Unit.Kind == kind;
            r.Box.IsEnabled = fits || r.Box.IsChecked == true;
            r.Element.Opacity = r.Box.IsEnabled ? 1 : 0.4;
            r.Element.ToolTip = fits ? null : F("In {0}, not {1}: it can't share this graph's scale", Show(r.Item.Unit.Kind), Show(kind!));
        }
        save.IsEnabled = picked.Count > 0;
        count.Text = picked.Count == 0 ? P(rows.Count, "{0} value", "{0} values") : P(picked.Count, "{0} ticked", "{0} ticked") + (kind!.Length > 0 ? $" · {kind}" : "");
        if (!titleTyped)
        {
            settingTitle = true;
            title.Text = picked.Count == 0 ? "" : picked.Count == 1 ? picked[0].Item.Name
                : CommonStart(picked.Select(p => p.Item.Name).ToList()) is { Length: > 3 } common && common.Contains(' ') ? common
                : picked[0].Item.Unit.Unit switch
                {
                    "bps" or "Bps" => T("Network traffic"),
                    "%" => T("Utilization"),
                    "B" => T("Sizes"),
                    "°C" or "C" => T("Temperatures"),
                    _ => string.Join(", ", picked.Take(2).Select(p => p.Item.Name)) + (picked.Count > 2 ? "…" : ""),
                };
            settingTitle = false;
        }
    }

    private static string Show(string kind) => kind.Length > 0 ? kind : T("no unit");

    private static string CommonStart(List<string> names)
    {
        string first = names[0];
        int n = first.Length;
        foreach (var s in names.Skip(1)) { n = Math.Min(n, s.Length); for (int i = 0; i < n; i++) if (char.ToLowerInvariant(s[i]) != char.ToLowerInvariant(first[i])) { n = i; break; } }
        // whole words only ("Interface eth0: Bits received" and "…eth1…" → "Interface")
        if (n < first.Length && n > 0 && char.IsLetterOrDigit(first[n]) && char.IsLetterOrDigit(first[n - 1]))
            n = first.LastIndexOfAny(new[] { ' ', ':', '-', '/', '(' }, n - 1) is int b and >= 0 ? b : 0;
        return first[..n].TrimEnd(' ', ':', '-', '/', '(', '"');
    }

    private void Filter()
    {
        string q = search.Text.Trim();
        foreach (var r in rows)
            r.Element.Visibility = q.Length == 0 || r.Box.IsChecked == true || r.Item.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || r.Item.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
    }
}
