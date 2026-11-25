using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Keyboards;

/// <summary>
/// Keyboard, a sub-tab of the Tools tab: any key made to send another (or a key combination, or nothing), whatever
/// it sent: an F key that the keyboard's F Lock turns into a media key, the Copilot key as Right Ctrl. The remaps in a
/// list (each with a box to switch it off, Edit and Delete); Add opens <see cref="RemapWizard"/>; On / Off for them
/// all. The work is <see cref="Remapper"/>'s hook, in only while a remap is on.
/// </summary>
public sealed class KeyboardApplet : IApplet
{
    private readonly KeyboardSettings settings = KeyboardSettings.Load();
    private readonly Logger log = Log.Here;
    private readonly Remapper remapper;
    private readonly ToggleButton onOff;
    private readonly StackPanel list = new();
    private readonly FrameworkElement view;

    public KeyboardApplet()
    {
        remapper = new Remapper(log);
        onOff = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        onOff.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Remaps on"));
        onOff.Click += (_, _) => SetOn(onOff.IsChecked == true);

        var add = Ui.Spaced(Ui.IconButton("", T("Add…"), T("Press a key, then pick what it sends"), Add, accent: true));
        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { onOff, add } };
        var panel = new StackPanel();
        panel.Children.Add(Ui.HeaderRow(T("Remapped keys"), top));
        panel.Children.Add(list);
        view = Ui.Page(Ui.Card(panel));
        Show();
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => Apply();

    private void Apply() => remapper.Set(settings.On ? settings.Remaps : []);

    private void SetOn(bool on)
    {
        settings.On = on;
        settings.Save();
        log.Info(on ? "Keyboard remaps on" : "Keyboard remaps off");
        Apply();
        Show();
    }

    private void Add()
    {
        var r = RemapWizard.Add(remapper, view);
        if (r == null) return;
        var same = settings.Remaps.FindIndex(x => x.From.Same(r.From));
        if (same >= 0) settings.Remaps[same] = r; else settings.Remaps.Add(r);
        Saved($"Keyboard: {r.From} sends {r.To}");
    }

    private void Edit(Remap old)
    {
        var r = RemapWizard.Edit(remapper, view, old);
        if (r == null) return;
        r.On = old.On;
        settings.Remaps[settings.Remaps.IndexOf(old)] = r;
        Saved($"Keyboard: {r.From} sends {r.To}");
    }

    private void Delete(Remap r)
    {
        settings.Remaps.Remove(r);
        Saved($"Keyboard: {r.From} as it was");
    }

    private void Saved(string what)
    {
        settings.Save();
        log.Info(what);
        Apply();
        Show();
    }

    private void Show()
    {
        onOff.IsChecked = settings.On;
        onOff.Content = settings.On ? T("On") : T("Off");
        list.Children.Clear();
        if (settings.Remaps.Count == 0)
        {
            list.Children.Add(Ui.Note(T("No keys remapped"), 10));
            return;
        }
        foreach (var r in settings.Remaps) list.Children.Add(Row(r));
    }

    /// <summary>A remap: on or off, the key, what it sends, Edit and Delete.</summary>
    private FrameworkElement Row(Remap r)
    {
        var box = new CheckBox { IsChecked = r.On, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("On") };
        AutomationProperties.SetName(box, F("{0} on", r.From));
        Ui.OnToggle(box, on =>
        {
            r.On = on;
            Saved($"Keyboard: {r.From} {(on ? "on" : "off")}");
        });
        var from = new TextBlock { Text = r.From.ToString(), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var arrow = Ui.Icon("", 12);
        arrow.Margin = new Thickness(12, 0, 12, 0);
        arrow.VerticalAlignment = VerticalAlignment.Center;
        var to = new TextBlock { Text = r.To.ToString(), VerticalAlignment = VerticalAlignment.Center };
        var edit = Ui.Spaced(Ui.Button(T("Edit"), () => Edit(r)));
        var delete = Ui.Spaced(ConfirmButton.Make(T("Delete"), () => Delete(r)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { edit, delete } };
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0), Opacity = r.On ? 1 : 0.6 };
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { box, from, arrow, to } });
        return row;
    }

    public void SaveSettings() => settings.Save();

    public void Dispose() => remapper.Dispose();
}
