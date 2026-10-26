using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shell;
using DaisysApp.Theming;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// HA's step in the app's setup wizard (and its own first-run window) while it isn't set up: the connection form, with
/// Skip for now, and Switch off HA (the module off) for anyone without Home Assistant.
/// </summary>
internal sealed class HaSetupPanel : StackPanel
{
    private readonly TextBlock note = Ui.Secondary("", wrap: true);

    /// <summary>Raised when the user chose Skip, Switch off, or connected.</summary>
    public event Action? Finished;

    public HaSetupPanel(HaService service)
    {
        var form = new ConnectionForm(service) { Margin = new Thickness(0, 0, 0, 12) };
        form.Connected += () => { note.Text = T("Done: your devices are on the HA tab."); Finished?.Invoke(); };
        Children.Add(form);

        var skip = new Button { Content = T("Skip for now"), MinWidth = 120, ToolTip = T("Set it up later in Settings → HA") };
        skip.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            note.Text = T("Skipped: it's in Settings → HA.");
            Finished?.Invoke();
        };
        var off = new Button { Content = T("Switch off HA"), MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("No HA tab (Settings → General → Modules switches it back on)") };
        off.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            AppNavigation.SwitchOff(HaApplet.Id);
            note.Text = T("HA is switched off.");
            Finished?.Invoke();
        };
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { skip, off } });
        note.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(note);
    }

    /// <summary>The panel in its own window, for a first run outside the setup wizard (HA was switched on later).</summary>
    public static void ShowWindow(HaService service, Window owner)
    {
        var panel = new HaSetupPanel(service);
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsCancel = true };
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var w = new Window
        {
            Title = T("Set up HA"), Width = 600, SizeToContent = SizeToContent.Height, Owner = owner, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);
        close.Click += (_, _) => w.Close();
        panel.Finished += () => w.Dispatcher.BeginInvoke(() => { if (w.IsVisible) w.Close(); }, System.Windows.Threading.DispatcherPriority.Background);
        w.Closed += (_, _) =>
        {
            // closed without choosing: don't ask again by itself (Settings → HA has it)
            if (!service.Settings.Configured && !service.Settings.SetupSkipped) { service.Settings.SetupSkipped = true; service.Settings.Save(); }
        };
        w.ShowDialog();
    }
}
