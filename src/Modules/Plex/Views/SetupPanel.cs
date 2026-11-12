using System.Windows.Controls;
using System.Windows;
using DaisysApp.Shell;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Plex's step in the setup wizard (and its own window when it was switched on later): the server (this PC's, a plex.tv
/// sign-in, or an address and token), then Tautulli (optional); Skip for now, or Switch off Plex.
/// </summary>
internal sealed class SetupPanel : StackPanel
{
    private readonly TextBlock note = Ui.Note("", 10);

    public event Action? Finished;

    public SetupPanel(PlexService service)
    {
        Children.Add(Ui.Header(T("Plex Media Server")));
        var form = new PlexForm(service) { Margin = new Thickness(0, 0, 0, 14) };
        form.Connected += () => note.Text = T("Done: it's on the Plex tab.");
        Children.Add(form);
        Children.Add(Ui.Header(T("Tautulli (optional)")));
        Children.Add(new TautulliForm(service) { Margin = new Thickness(0, 0, 0, 14) });

        var skip = new Button { Content = T("Skip for now"), MinWidth = 120, ToolTip = T("Set it up later in Settings → Plex") };
        skip.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            note.Text = T("Skipped: it's in Settings → Plex.");
            Finished?.Invoke();
        };
        var off = new Button { Content = T("Switch off Plex"), MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("No Plex tab (Settings → General → Modules switches it back on)") };
        off.Click += (_, _) =>
        {
            service.Settings.SetupSkipped = true;
            service.Settings.Save();
            AppNavigation.SwitchOff(PlexApplet.Id);
            note.Text = T("Plex is switched off.");
            Finished?.Invoke();
        };
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { skip, off } });
        Children.Add(note);
    }

    /// <summary>The panel in its own window, for a first run outside the setup wizard.</summary>
    public static void ShowWindow(PlexService service, Window owner)
    {
        var panel = new SetupPanel(service);
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsCancel = true };
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var w = new Window
        {
            Title = T("Set up Plex"), Width = 640, SizeToContent = SizeToContent.Height, MaxHeight = 760, Owner = owner, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);
        close.Click += (_, _) => w.Close();
        panel.Finished += () => w.Dispatcher.BeginInvoke(() => { if (w.IsVisible) w.Close(); }, System.Windows.Threading.DispatcherPriority.Background);
        w.Closed += (_, _) =>
        {
            if (!service.Settings.Configured && !service.Settings.SetupSkipped) { service.Settings.SetupSkipped = true; service.Settings.Save(); }
        };
        w.ShowDialog();
    }
}

/// <summary>Settings → Plex: the server and Tautulli.</summary>
internal sealed class PlexSettingsView : StackPanel
{
    public PlexSettingsView(PlexService service)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition() } };
        var plex = Ui.Card(new PlexForm(service), T("Plex Media Server"), new Thickness(0));
        var tautulli = Ui.Card(new TautulliForm(service), T("Tautulli"), new Thickness(0));
        tautulli.VerticalAlignment = plex.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(tautulli, 2);
        grid.Children.Add(plex);
        grid.Children.Add(tautulli);
        Children.Add(grid);
    }
}
