using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>Settings → HA: the server and its token, with the grid's size beside it; then what's hidden from the tab (each with Show) and whether devices with only sensors are shown.</summary>
internal sealed class HaSettingsView : StackPanel
{
    private readonly HaService service;
    private readonly StackPanel hidden = new();

    public HaSettingsView(HaService service)
    {
        this.service = service;
        var server = new StackPanel();
        server.Children.Add(Ui.Header(T("Home Assistant")));
        server.Children.Add(new ConnectionForm(service));
        var serverCard = Ui.Card(server, new Thickness(0));

        var size = new StackPanel();
        size.Children.Add(Ui.Header(T("Display Grid Size")));
        size.Children.Add(Number(T("Columns"), T("0 = as many as fit"), () => service.Settings.GridColumns, v => service.Settings.GridColumns = v));
        size.Children.Add(Number(T("Rows"), T("Rows to fit the window's height; 0 = as tall as they need"), () => service.Settings.GridRows, v => service.Settings.GridRows = v));
        var sizeCard = Ui.Card(size, new Thickness(0));
        sizeCard.VerticalAlignment = VerticalAlignment.Top;
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition { Width = GridLength.Auto } }, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetColumn(sizeCard, 2);
        top.Children.Add(serverCard);
        top.Children.Add(sizeCard);
        Children.Add(top);

        var shown = new StackPanel();
        shown.Children.Add(Ui.Header(T("Hidden")));
        var sensorsOnly = new CheckBox { Content = T("Show devices with only sensors"), IsChecked = service.Settings.ShowSensorsOnly, Margin = new Thickness(0, 0, 0, 10) };
        sensorsOnly.Click += (_, _) =>
        {
            service.Settings.ShowSensorsOnly = sensorsOnly.IsChecked == true;
            service.Settings.Save();
            service.RaiseChanged();
        };
        shown.Children.Add(sensorsOnly);
        shown.Children.Add(hidden);
        Children.Add(Ui.Card(shown, new Thickness(0)));
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) ShowHidden(); };
    }

    private FrameworkElement Number(string label, string tip, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString(), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) =>
        {
            if (!int.TryParse(box.Text.Trim().Length == 0 ? "0" : box.Text.Trim(), out int v) || v < 0 || v > 20 || v == get()) return;
            set(v);
            service.Settings.Save();
            service.RaiseChanged();
        };
        var l = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { l, box } };
    }

    /// <summary>The devices hidden from the tab (Hide in a device's window, or right-click), each with Show.</summary>
    private void ShowHidden()
    {
        hidden.Children.Clear();
        var list = service.Settings.Hidden;
        if (list.Count == 0) { hidden.Children.Add(Ui.Secondary(T("Nothing is hidden."))); return; }
        foreach (var h in list.OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            var show = new Button { Content = T("Show"), MinWidth = 70 };
            System.Windows.Automation.AutomationProperties.SetName(show, F("Show {0}", h.Name));
            show.Click += (_, _) =>
            {
                service.Settings.Hidden.RemoveAll(x => x.Key == h.Key);
                service.Settings.Save();
                service.RaiseChanged();
                ShowHidden();
            };
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            string area = service.Thing(h.Key) is { } t ? service.Area(t.AreaId)?.Name ?? "" : "";
            var name = new TextBlock { Text = area.Length > 0 ? $"{h.Name}  ·  {area}" : h.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(name);
            hidden.Children.Add(row);
        }
    }
}
