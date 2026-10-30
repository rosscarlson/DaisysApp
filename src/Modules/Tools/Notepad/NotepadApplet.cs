using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// Notepad, a sub-tab of the Tools tab: a note shared between PCs, hosted by one Daisy's App and edited on all of them
/// at once, over the AI module's remote control (Core's RemoteChannels). Its page can be popped out into a window of
/// its own (in the taskbar and Alt+Tab), which opens again at the next start if it was out when the app closed. Files
/// opened with the app (Core's OpenFiles: Windows' default app for text files, set in its Settings page) open in it.
/// </summary>
public sealed class NotepadApplet : IApplet
{
    private readonly NotepadSettings settings = NotepadSettings.Load();
    private readonly NotepadView view;
    private readonly DefaultAppView defaults;
    private readonly StackPanel page;
    private readonly Border holder = new();
    private readonly FrameworkElement poppedHere;
    private Window? window;
    private bool closing;

    public NotepadApplet()
    {
        view = new NotepadView(settings);
        view.PopClicked += () => { if (window == null) PopOut(); else window.Close(); };
        var back = new Button { Content = T("Back in the tab"), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 10, 0, 0) };
        back.Click += (_, _) => window?.Close();
        var popped = new TextBlock { Text = T("Popped out"), HorizontalAlignment = HorizontalAlignment.Center };
        popped.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        poppedHere = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { popped, back } };
        holder.Child = view;
        defaults = new DefaultAppView(settings);
        page = new StackPanel { Children = { new Server.ServerSettingsView(), defaults } };
    }

    public FrameworkElement View => holder;
    /// <summary>Settings → Tools → Notepad: Daisy's Server (Core's card), then the default app.</summary>
    public FrameworkElement? SettingsView => page;

    public void Start()
    {
        RemoteChannels.Serve(NotepadView.Channel, view.ServeAsync);
        view.Connect();
        // (the notepad button at the main window's top right: pops it out, or brings it to the front)
        QuickButtons.Add("Notepad", "", T("Notepad"), Show);
        // (a file opened with the app: in a new tab, popped out)
        OpenFiles.Handler = path =>
        {
            view.Open(path);
            Show();
        };
        defaults.Refresh();
        if (settings.PoppedOut) Application.Current.Dispatcher.BeginInvoke(PopOut, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>The page into its own window (not owned by the main one, so it's in Alt+Tab by itself).</summary>
    private void PopOut()
    {
        if (window != null || closing) return;
        holder.Child = poppedHere;
        var w = new Window { Title = T("Notepad"), Width = 900, Height = 650, Content = view, ShowInTaskbar = true, Icon = Application.Current.MainWindow?.Icon };
        if (settings.PopBounds is [var left, var top, var width, var height] && width > 200 && height > 150
            && left + width > SystemParameters.VirtualScreenLeft + 50 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            && top > SystemParameters.VirtualScreenTop - 20 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            (w.Left, w.Top, w.Width, w.Height) = (left, top, width, height);
        }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        view.Margin = new Thickness(12);
        w.SourceInitialized += (_, _) => Theming.ThemeManager.ApplyTitleBar(w);
        w.Closing += (_, _) =>
        {
            settings.PopBounds = w.WindowState == WindowState.Normal ? [w.Left, w.Top, w.Width, w.Height] : [w.RestoreBounds.Left, w.RestoreBounds.Top, w.RestoreBounds.Width, w.RestoreBounds.Height];
            // (the app closing leaves it out, for the next start)
            if (!closing) settings.PoppedOut = false;
            settings.Save();
        };
        w.Closed += (_, _) =>
        {
            window = null;
            w.Content = null;
            view.Margin = new Thickness(0);
            if (!closing) holder.Child = view;
            view.Popped(false);
        };
        window = w;
        settings.PoppedOut = true;
        settings.Save();
        view.Popped(true);
        w.Show();
        w.Activate();
    }

    private void Show()
    {
        if (window == null) { PopOut(); return; }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void SaveSettings() => view.Save();

    public void Dispose()
    {
        closing = true;
        QuickButtons.Remove("Notepad");
        OpenFiles.Handler = null;
        window?.Close();
        view.Close();
    }
}
