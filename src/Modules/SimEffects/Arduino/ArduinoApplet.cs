using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// SimEffects → Arduino: the Arduinos (each on its COM port, with the DaisysSim sketch) and what's on each (features:
/// <see cref="IArduinoFeature"/>). Owns the <see cref="ArduinoHub"/> that keeps them connected; the sub-tabs that
/// drive features (Notify) show while some board has one. Set up in the Arduinos window (<see cref="ArduinoWindow"/>).
/// </summary>
public sealed class ArduinoApplet : IApplet
{
    private readonly ArduinoHub hub;
    private readonly ArduinoView view;

    public ArduinoApplet(ArduinoHub hub)
    {
        this.hub = hub;
        view = new ArduinoView(hub);
        hub.Changed += view.Render;
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        hub.Apply();
    }

    public void SaveSettings() => hub.Settings.Save();

    public void Dispose()
    {
        hub.Changed -= view.Render;
        view.Stop();
        hub.Dispose();
        hub.Settings.Save();
    }

    /// <summary>The sketch's folder next to the module (Arduino\DaisysSim).</summary>
    internal static string SketchFolder => Path.Combine(Path.GetDirectoryName(typeof(ArduinoApplet).Assembly.Location) ?? "", "Arduino", "DaisysSim");
}

/// <summary>The Arduino tab: Arduinos… (the window), a card per board (what it's doing, its features), and the sketch.</summary>
internal sealed class ArduinoView : UserControl
{
    private readonly ArduinoHub hub;
    private readonly StackPanel boards = new();
    private readonly List<(TextBlock Text, ArduinoBoard Board, ArduinoFeature? Feature)> statuses = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ArduinoView(ArduinoHub hub)
    {
        this.hub = hub;
        var manage = Ui.Button(T("Arduinos…"), () => ArduinoWindow.Show(hub, Window.GetWindow(this)), accent: true);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(manage, Dock.Left);
        top.Children.Add(manage);
        top.Children.Add(new Border());

        var sketch = new StackPanel();
        sketch.Children.Add(Ui.Header(T("Sketch")));
        sketch.Children.Add(Ui.Secondary(F("DaisysSim {0}, with the FastLED library", ArduinoProtocol.Version)));
        var openSketch = Ui.Button(T("Open Sketch Folder"), () =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ArduinoApplet.SketchFolder}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Logging.Log.Here.Warn("Arduino: sketch folder", ex); }
        });
        openSketch.Margin = new Thickness(0, 8, 0, 0);
        openSketch.HorizontalAlignment = HorizontalAlignment.Left;
        sketch.Children.Add(openSketch);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(Ui.Columns(boards, Ui.Card(sketch)));
        Content = page;

        timer.Tick += (_, _) => ShowStatuses();
        IsVisibleChanged += (_, _) => { if (IsVisible) { ShowStatuses(); timer.Start(); } else timer.Stop(); };
    }

    public void Stop() => timer.Stop();

    public void Render()
    {
        boards.Children.Clear();
        statuses.Clear();
        if (hub.Settings.Boards.Count == 0)
        {
            boards.Children.Add(Ui.Card(Ui.Secondary(T("No Arduinos yet: add one in Arduinos…"))));
            return;
        }
        foreach (var b in hub.Settings.Boards)
        {
            var panel = new StackPanel();
            var edit = Ui.Button(T("Edit"), () => ArduinoWindow.Show(hub, Window.GetWindow(this), b));
            edit.Margin = new Thickness(0);
            panel.Children.Add(Ui.HeaderLine($"{b.Name} · {(b.Port.Length > 0 ? b.Port : T("No COM port"))}", edit));
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            panel.Children.Add(status);
            statuses.Add((status, b, null));
            if (b.Features.Count == 0) panel.Children.Add(Ui.Secondary(T("No features")));
            foreach (var f in b.Features)
            {
                var line = new TextBlock { Text = $"{f.Name} · {f.Info?.Title ?? f.Kind}", Margin = new Thickness(0, 2, 0, 0) };
                panel.Children.Add(line);
                var problem = new TextBlock { TextWrapping = TextWrapping.Wrap };
                problem.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
                panel.Children.Add(problem);
                statuses.Add((problem, b, f));
            }
            boards.Children.Add(Ui.Card(panel));
        }
        ShowStatuses();
    }

    private void ShowStatuses()
    {
        foreach (var (text, b, f) in statuses)
        {
            if (f == null)
            {
                text.Text = hub.Status(b);
                text.SetResourceReference(TextBlock.ForegroundProperty, hub.Connected(b) ? "SuccessBrush" : "TextSecondaryBrush");
            }
            else
            {
                string? p = hub.Problem(b, f);
                text.Text = p ?? "";
                text.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
