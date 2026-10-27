using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Automation;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// SimEffects → Arduino: the Arduinos (each on its COM port, with SimHub's sketch or DaisysSim) and what's on each (features:
/// <see cref="IArduinoFeature"/>). Owns the <see cref="ArduinoHub"/> that keeps them connected; the sub-tabs that
/// drive features (Notify) show while some board has one. Set up in the Arduinos window (<see cref="ArduinoWindow"/>).
/// On / off (since 3.17.0: off disconnects every board) with a shortcut and a title bar bubble, kept per SimEffects
/// profile (since 3.18.0).
/// </summary>
public sealed class ArduinoApplet : IApplet
{
    private readonly ArduinoHub hub;
    private readonly ArduinoView view;
    private readonly Hotkey hotkey;

    public ArduinoApplet(ArduinoHub hub)
    {
        this.hub = hub;
        var s = hub.Settings;
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Arduino.OnOff", Module = T("Arduino"), Name = T("On / off"),
            Get = () => s.OnOffKey, Set = v => { s.OnOffKey = v; s.Save(); },
        }, () => SetEnabled(!s.Enabled));
        SimProfiles.Register(new ToolScope
        {
            Id = "Arduino", Title = T("Arduino"), Settings = s, Props = new[] { nameof(ArduinoSettings.Enabled) },
            IsOn = () => s.Enabled, SetOn = SetEnabled, Loaded = () => { hub.Apply(); view.ShowOnOff(); }, OnWithGames = s.SwitchOnWithGame,
        });
        view = new ArduinoView(this, hub);
        hub.Changed += view.Render;
        hub.Changed += ShowBadge;
    }

    internal Hotkey Hotkey => hotkey;

    /// <summary>On or off (ON / OFF, the shortcut, the tray, the Profiles tab): kept in the profile in use.</summary>
    internal void SetEnabled(bool on)
    {
        hub.Settings.Enabled = on;
        Logging.Log.Here.Info($"Arduino: switched {(on ? "on" : "off")}");
        hub.Apply();
        view.ShowOnOff();
    }

    /// <summary>The title bar's Arduino bubble: while it's on with a board switched on.</summary>
    internal void ShowBadge() =>
        TitleBadges.Set("Arduino", hub.Settings.Enabled && hub.Settings.Boards.Any(b => b.Enabled) ? "Arduino" : null, T("Arduino is on"));

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        hub.Apply();
    }

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Arduino on"), () => SetEnabled(!hub.Settings.Enabled)) { Checked = hub.Settings.Enabled },
    };

    public void SaveSettings() => hub.Settings.Save();

    public void Dispose()
    {
        hotkey.Dispose();
        TitleBadges.Set("Arduino", null);
        hub.Changed -= view.Render;
        hub.Changed -= ShowBadge;
        view.Stop();
        hub.Dispose();
        hub.Settings.Save();
    }

    /// <summary>The sketch's folder next to the module (Arduino\DaisysSim).</summary>
    internal static string SketchFolder => Path.Combine(Path.GetDirectoryName(typeof(ArduinoApplet).Assembly.Location) ?? "", "Arduino", "DaisysSim");
}

/// <summary>The Arduino tab: on / off, Arduinos… (the window), a card per board (what it's doing, its features), the
/// sketch.</summary>
internal sealed class ArduinoView : UserControl
{
    private readonly ArduinoApplet applet;
    private readonly ArduinoHub hub;
    private readonly System.Windows.Controls.Primitives.ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("\uE7FC");
    private readonly StackPanel boards = new();
    private readonly List<(TextBlock Text, ArduinoBoard Board, ArduinoFeature? Feature)> statuses = new();
    private readonly List<(Ellipse Dot, ArduinoBoard Board)> dots = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ArduinoView(ArduinoApplet applet, ArduinoHub hub)
    {
        this.applet = applet;
        this.hub = hub;
        var settings = hub.Settings;
        onOff = Ui.OnOff(T("Arduino on"), settings.Enabled, applet.SetEnabled);
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(onOffKey, T("On / off shortcut"));
        ShowKey();
        onOffKey.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), applet.Hotkey); ShowKey(); };
        var manage = Ui.Button(T("Arduinos…"), () => ArduinoWindow.Show(hub, Window.GetWindow(this)), accent: true);
        manage.Margin = new Thickness(12, 0, 0, 0);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var e in new UIElement[] { onOff, onOffKey, manage })
        {
            DockPanel.SetDock(e, Dock.Left);
            top.Children.Add(e);
        }
        top.Children.Add(new Border());

        var sketch = new StackPanel();
        sketch.Children.Add(Ui.Header(T("Sketch")));
        sketch.Children.Add(Ui.Secondary(T("SimHub's, as set up in SimHub's Arduino setup")));
        sketch.Children.Add(Ui.Secondary(F("Or DaisysSim {0}, with the FastLED library", ArduinoProtocol.Version)));
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
        var left = new StackPanel();
        left.Children.Add(boards);
        var right = new StackPanel();
        right.Children.Add(Ui.Card(sketch));
        page.Children.Add(Ui.Columns(left, right));
        Content = page;

        timer.Tick += (_, _) => ShowStatuses();
        IsVisibleChanged += (_, _) => { if (IsVisible) { ShowStatuses(); timer.Start(); } else timer.Stop(); };
    }

    public void Stop() => timer.Stop();

    /// <summary>On or off changed (here, by its shortcut or a game).</summary>
    public void ShowOnOff()
    {
        onOff.IsChecked = hub.Settings.Enabled;
        onOff.Content = hub.Settings.Enabled ? T("ON") : T("OFF");
    }

    private void ShowKey()
    {
        ((TextBlock)onOffKey.Content).SetResourceReference(TextBlock.ForegroundProperty, applet.Hotkey.Value != null ? "AccentBrush" : "TextBrush");
        onOffKey.ToolTip = applet.Hotkey.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
    }

    public void Render()
    {
        ShowOnOff();
        boards.Children.Clear();
        statuses.Clear();
        dots.Clear();
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
            var head = Ui.HeaderLine($"{b.Name} · {(b.Port.Length > 0 ? b.Port : T("No COM port"))}", edit);
            var dot = Ui.Dot();
            dots.Add((dot, b));
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Insert(0, dot);
            panel.Children.Add(head);
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
        foreach (var (dot, b) in dots) Ui.Light(dot, hub.State(b));
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
