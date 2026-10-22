using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Logging;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>
/// The Bluetooth card above Apps: the devices picked with its gear, blue when connected (the text colour when not),
/// each with Connect / Disconnect. Looked at again every couple of seconds while the tab shows.
/// </summary>
internal sealed class BluetoothCard : Border
{
    private static readonly Logger log = Log.Here;
    private static readonly Brush ConnectedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x86, 0xFF)));

    private readonly AudioLevelsSettings settings;
    private readonly StackPanel rows = new();
    private readonly TextBlock empty;
    private readonly HashSet<string> busy = new();
    private List<BtDevice> paired = new();
    private string signature = "";
    private bool looking;

    public BluetoothCard(AudioLevelsSettings settings)
    {
        this.settings = settings;
        SetResourceReference(StyleProperty, "Card");

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var gear = new Button { Padding = new Thickness(7, 3, 7, 3), MinHeight = 0, ToolTip = T("Pick the Bluetooth devices shown here"), VerticalAlignment = VerticalAlignment.Top };
        gear.Content = Icon("\uE713", 13);
        System.Windows.Automation.AutomationProperties.SetName(gear, "Pick Bluetooth devices");
        gear.Click += (_, _) => Pick();
        DockPanel.SetDock(gear, Dock.Right);
        header.Children.Add(gear);
        var title = new TextBlock { Text = T("Bluetooth"), VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(StyleProperty, "CardHeader");
        header.Children.Add(title);

        empty = new TextBlock { Text = T("Pick devices with the gear."), Margin = new Thickness(0, 4, 0, 0) };
        empty.SetResourceReference(StyleProperty, "SecondaryText");

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(rows);
        panel.Children.Add(empty);
        Child = panel;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>Asks Windows for the paired devices (off the UI thread) and redraws if anything changed.</summary>
    public async void Refresh()
    {
        if (looking) return;
        looking = true;
        try { paired = await Task.Run(Bluetooth.Paired); }
        catch (Exception ex) { log.Debug($"Bluetooth devices: {ex.Message}"); }
        finally { looking = false; }
        Render();
    }

    private void Render()
    {
        var shown = settings.Bluetooth.Select(a => paired.FirstOrDefault(d => d.Address == a)).OfType<BtDevice>().ToList();
        string sig = string.Join("|", shown.Select(d => d.Address + d.Name + d.Connected + busy.Contains(d.Address)));
        empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sig == signature) return;
        signature = sig;

        rows.Children.Clear();
        foreach (var d in shown)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            bool working = busy.Contains(d.Address);
            var button = new Button
            {
                Content = working ? T("Working…") : d.Connected ? T("Disconnect") : T("Connect"),
                IsEnabled = !working, MinWidth = 96, Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
            };
            button.Click += (_, _) => Switch(d);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);

            var icon = Icon("\uE702", 14);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            var name = new TextBlock { Text = d.Name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            foreach (var t in new[] { icon, name })
                if (d.Connected) t.Foreground = ConnectedBrush; else t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            name.ToolTip = d.Connected ? T("Connected") : T("Not connected");
            row.Children.Add(icon);
            row.Children.Add(name);
            rows.Children.Add(row);
        }
    }

    private async void Switch(BtDevice d)
    {
        busy.Add(d.Address);
        Render();
        bool ok = await Task.Run(() => { try { return Bluetooth.Set(d, !d.Connected); } catch (Exception ex) { log.Warn($"Bluetooth {d.Name}", ex); return false; } });
        if (!ok) log.Warn($"Windows wouldn't {(d.Connected ? "disconnect" : "connect")} {d.Name}");
        // the connection takes a few seconds to come or go
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(1000);
            try { paired = await Task.Run(Bluetooth.Paired); } catch { }
            if (paired.FirstOrDefault(p => p.Address == d.Address)?.Connected != d.Connected) break;
        }
        busy.Remove(d.Address);
        Render();
    }

    /// <summary>The gear: every paired device, ticked if it's shown.</summary>
    private void Pick()
    {
        var all = Bluetooth.Paired();
        var w = new Window
        {
            Title = T("Bluetooth devices"), Width = 380, SizeToContent = SizeToContent.Height, MaxHeight = 600,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Owner = Window.GetWindow(this),
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);

        var list = new StackPanel();
        foreach (var d in all)
        {
            var box = new CheckBox { Content = d.Name, IsChecked = settings.Bluetooth.Contains(d.Address), Margin = new Thickness(0, 0, 0, 8), ToolTip = d.Address };
            box.Click += (_, _) =>
            {
                settings.Bluetooth.Remove(d.Address);
                if (box.IsChecked == true) settings.Bluetooth.Add(d.Address);
                settings.Save();
                paired = all;
                Render();
            };
            list.Children.Add(box);
        }
        if (all.Count == 0)
        {
            var none = new TextBlock { Text = T("No paired Bluetooth devices (or Bluetooth is off).") };
            none.SetResourceReference(StyleProperty, "SecondaryText");
            list.Children.Add(none);
        }

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsCancel = true };
        close.Click += (_, _) => w.Close();
        var panel = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = panel;
        w.ShowDialog();
    }

    private static TextBlock Icon(string glyph, double size)
    {
        var t = new TextBlock { Text = glyph, FontSize = size };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
