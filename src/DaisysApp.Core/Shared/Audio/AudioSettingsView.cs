using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Shared.Audio;

/// <summary>
/// The Audio card, on Settings → General beside Daisy's Server: the app's output and input (<see cref="AppAudio"/>).
/// The lists are read again each time one opens, so a device plugged in meanwhile shows.
/// </summary>
public sealed class AudioSettingsView : Border
{
    public AudioSettingsView()
    {
        SetResourceReference(StyleProperty, "Card");
        var panel = new StackPanel();
        var header = new TextBlock { Text = T("Audio") };
        header.SetResourceReference(StyleProperty, "CardHeader");
        panel.Children.Add(header);
        panel.Children.Add(Row(T("Output"), Box(false)));
        panel.Children.Add(Row(T("Input"), Box(true)));
        Child = panel;
        ToolTip = T("The app's voice, sounds and microphone (not Voicemeeter's tools)");
    }

    private static ComboBox Box(bool input)
    {
        var box = new ComboBox { MinWidth = 220, MaxWidth = 360 };
        System.Windows.Automation.AutomationProperties.SetName(box, input ? T("Input") : T("Output"));
        bool filling = false;
        void Fill()
        {
            filling = true;
            var s = AppAudio.Settings;
            string id = input ? s.Input : s.Output, name = input ? s.InputName : s.OutputName;
            box.Items.Clear();
            box.Items.Add(new ComboBoxItem { Content = T("Windows default"), Tag = null });
            var devices = AppAudio.Devices(input);
            foreach (var d in devices) box.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d, ToolTip = d.Name });
            if (id.Length > 0 && !devices.Any(d => d.Id == id))
                box.Items.Add(new ComboBoxItem { Content = F("{0} (not connected)", name.Length > 0 ? name : id), Tag = new AppAudio.Device(id, name) });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is AppAudio.Device d && d.Id == id) ?? box.Items[0];
            filling = false;
        }
        Fill();
        box.DropDownOpened += (_, _) => Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (filling || box.SelectedItem is not ComboBoxItem item) return;
            AppAudio.Set(input, item.Tag as AppAudio.Device);
        };
        return box;
    }

    private static StackPanel Row(string label, UIElement item)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(item);
        return p;
    }
}
