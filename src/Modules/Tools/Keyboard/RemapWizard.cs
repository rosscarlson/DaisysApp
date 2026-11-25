using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Keyboards;

/// <summary>
/// A key remapped in two steps, in a small window: press the key (it's kept from Windows while it waits), see what it
/// sends, then keep it or pick what it sends instead: a key from the list or pressed, with Ctrl, Shift, Alt or Win, or
/// nothing. Editing a remap opens at the second step.
/// </summary>
internal sealed class RemapWizard
{
    private readonly Remapper remapper;
    private readonly Window window;
    private readonly ContentControl body = new();
    private KeyCombo? from;
    private Remap? result;
    private bool editing;

    private ComboBox? keys;
    private readonly Dictionary<Mods, ToggleButton> chips = new();

    private RemapWizard(Remapper remapper, FrameworkElement owner)
    {
        this.remapper = remapper;
        window = Ui.Dialog(T("Remap a key"), owner, 470);
        window.ResizeMode = ResizeMode.NoResize;
        window.Content = new Border { Padding = new Thickness(22, 18, 22, 18), Child = body };
        window.Closed += (_, _) => remapper.CancelCapture();
    }

    /// <summary>A new remap (null: kept as it is, or cancelled).</summary>
    public static Remap? Add(Remapper remapper, FrameworkElement owner)
    {
        var w = new RemapWizard(remapper, owner);
        w.window.Loaded += (_, _) => w.Press();
        w.window.ShowDialog();
        return w.result;
    }

    /// <summary>The remap changed (null: cancelled).</summary>
    public static Remap? Edit(Remapper remapper, FrameworkElement owner, Remap remap)
    {
        var w = new RemapWizard(remapper, owner);
        w.from = remap.From.Copy();
        w.editing = true;
        w.window.Loaded += (_, _) => w.Choose(remap.To);
        w.window.ShowDialog();
        return w.result;
    }

    /// <summary>The first step: waiting for the key.</summary>
    private void Press()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header(T("Press the key to remap")));
        panel.Children.Add(Big("…"));
        panel.Children.Add(Ui.Note(T("Nothing reaches Windows until it's let go"), 4));
        panel.Children.Add(Buttons(Cancel()));
        body.Content = panel;
        remapper.Capture(k =>
        {
            if (!window.IsVisible) return;
            from = k;
            Choose(new KeyCombo { Vk = k.Vk, Ext = k.Ext, Scan = k.Scan });
        });
    }

    /// <summary>The second step: what it sends, and what it should send.</summary>
    private void Choose(KeyCombo to)
    {
        var panel = new StackPanel();
        var again = Ui.Button(T("Press again"), Press);
        panel.Children.Add(Ui.HeaderRow(T("It sends"), again));
        panel.Children.Add(Big(from!.ToString()));

        panel.Children.Add(Ui.Header(T("Make it send"), new Thickness(0, 18, 0, 0)));
        keys = KeyList(to);
        var press = Ui.Spaced(Ui.Button(T("Press it"), PressTarget, T("Press the key it should send")));
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(press, Dock.Right);
        row.Children.Add(press);
        row.Children.Add(keys);
        panel.Children.Add(row);

        var mods = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        chips.Clear();
        foreach (var m in KeyNames.Each)
        {
            var chip = Ui.Chip(m.ToString(), to.Mods.HasFlag(m), F("Held with it: {0}", m));
            chips[m] = chip;
            mods.Children.Add(chip);
        }
        panel.Children.Add(mods);

        var save = Ui.Button(T("Save"), Save, accent: true);
        save.MinWidth = 100;
        save.IsDefault = true;
        var keep = Ui.Spaced(editing ? Ui.Button(T("Cancel"), () => window.Close()) : Ui.Button(T("Keep it"), () => window.Close(), T("Leave the key as it is")));
        keep.MinWidth = 90;
        panel.Children.Add(Buttons(save, keep));
        body.Content = panel;
    }

    /// <summary>The key it sends instead, pressed (kept from Windows too).</summary>
    private void PressTarget()
    {
        if (keys == null) return;
        var was = keys.SelectedItem;
        keys.Items.Insert(0, new ComboBoxItem { Content = T("Press the key…"), IsEnabled = false });
        keys.SelectedIndex = 0;
        remapper.Capture(k =>
        {
            if (!window.IsVisible || keys == null) return;
            keys.Items.RemoveAt(0);
            keys.SelectedItem = was;
            Select(keys, k);
            foreach (var (m, chip) in chips) chip.IsChecked = k.Mods.HasFlag(m);
        });
    }

    private void Save()
    {
        if (from == null || keys?.SelectedItem is not ComboBoxItem { Tag: KeyCombo key }) return;
        var to = key.Copy();
        if (!to.Nothing) foreach (var (m, chip) in chips) if (chip.IsChecked == true) to.Mods |= m;
        result = new Remap { From = from, To = to };
        window.Close();
    }

    /// <summary>Every key, by group, "Nothing" first, with <paramref name="current"/> picked.</summary>
    private static ComboBox KeyList(KeyCombo current)
    {
        var box = new ComboBox { MaxDropDownHeight = 420, IsTextSearchEnabled = true };
        box.Items.Add(new ComboBoxItem { Content = T("Nothing"), Tag = new KeyCombo() });
        string? group = null;
        foreach (var (g, vk, ext, name) in KeyNames.List)
        {
            if (g != group)
            {
                group = g;
                box.Items.Add(new ComboBoxItem { Content = g, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            }
            box.Items.Add(new ComboBoxItem { Content = name, Tag = new KeyCombo { Vk = vk, Ext = ext } });
        }
        Select(box, current);
        return box;
    }

    /// <summary>The key picked in the list (one that isn't in it is added at the top).</summary>
    private static void Select(ComboBox box, KeyCombo k)
    {
        var key = new KeyCombo { Vk = k.Vk, Ext = k.Ext, Scan = k.Scan };
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is KeyCombo c && c.Same(key))
            ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is KeyCombo c && c.Vk == key.Vk && !key.Nothing && key.Vk != KeyNames.NoVk);
        if (item == null)
        {
            item = new ComboBoxItem { Content = key.ToString(), Tag = key };
            box.Items.Insert(1, item);
        }
        box.SelectedItem = item;
    }

    private Button Cancel()
    {
        var b = Ui.Button(T("Cancel"), () => window.Close());
        b.MinWidth = 90;
        return b;
    }

    private static TextBlock Big(string text) =>
        new() { Text = text, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }
}
