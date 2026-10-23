using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// Every volume's shortcuts (the master's, each device's and each of the profile in use's effects): up and down 5%,
/// and mute, as key combinations registered system-wide or controller / button box buttons.
/// </summary>
internal sealed class VolumeHotkeys : IDisposable
{
    public const double Step = 0.05;

    private readonly HotkeyManager keys = new();
    private readonly Func<IEnumerable<IVolume>> targets;
    private readonly Dictionary<string, List<Action>> map = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? buttons;

    /// <summary>A shortcut changed a volume (on the UI thread).</summary>
    public event Action? Changed;

    /// <summary>Key combinations another program has, so they don't work.</summary>
    public List<string> Taken { get; private set; } = new();

    public VolumeHotkeys(Func<IEnumerable<IVolume>> targets)
    {
        this.targets = targets;
        keys.Pressed += OnPressed;
    }

    public void Register()
    {
        map.Clear();
        void Add(string? key, Action a)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(a);
        }
        foreach (var v in targets())
        {
            Add(v.Keys.Up, () => { v.Volume = Math.Min(1, Math.Round(v.Volume + Step, 2)); v.Muted = false; });
            Add(v.Keys.Down, () => v.Volume = Math.Max(0, Math.Round(v.Volume - Step, 2)));
            Add(v.Keys.Mute, () => v.Muted = !v.Muted);
        }
        Taken = keys.RegisterAll(map.Keys.Where(k => !ControllerButtons.IsButton(k)));
        foreach (var t in Taken) Logging.Log.Here.Warn($"Bass Shaker: the shortcut {t} is taken by another program");
        bool anyButton = map.Keys.Any(ControllerButtons.IsButton);
        if (anyButton && buttons == null)
        {
            ControllerButtons.Pressed += OnPressed;
            buttons = ControllerButtons.Listen();
        }
        else if (!anyButton) StopButtons();
    }

    /// <summary>Lets go of them while a new one is being picked.</summary>
    public void Suspend()
    {
        keys.UnregisterAll();
        StopButtons();
    }

    private void StopButtons()
    {
        if (buttons == null) return;
        ControllerButtons.Pressed -= OnPressed;
        buttons.Dispose();
        buttons = null;
    }

    private void OnPressed(string shortcut)
    {
        if (!map.TryGetValue(shortcut, out var actions)) return;
        foreach (var a in actions) a();
        Logging.Log.Here.Debug(() => $"Bass Shaker shortcut {shortcut}");
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Suspend();
        keys.Pressed -= OnPressed;
        keys.Dispose();
    }
}

/// <summary>The small window a volume's shortcut button opens: Volume Up, Volume Down and Mute.</summary>
internal sealed class VolumeKeysWindow : Window
{
    public VolumeKeysWindow(string title, IVolume v, VolumeHotkeys hotkeys, Action saved)
    {
        Title = F("{0} Shortcuts", title);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var grid = new Grid { Margin = new Thickness(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new (string Label, Func<string?> Get, Action<string?> Set)[]
        {
            (T("Volume Up"), () => v.Keys.Up, k => v.Keys.Up = k),
            (T("Volume Down"), () => v.Keys.Down, k => v.Keys.Down = k),
            (T("Mute"), () => v.Keys.Mute, k => v.Keys.Mute = k),
        };
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Check()
        {
            var taken = new[] { v.Keys.Up, v.Keys.Down, v.Keys.Mute }.Where(k => k != null && hotkeys.Taken.Contains(k)).ToList();
            warning.Text = taken.Count > 0 ? F("Another program has {0}: pick another.", string.Join(", ", taken)) : "";
            warning.Visibility = taken.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = r.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8) };
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            var box = new ShortcutBox { AllowControllerButtons = true, Value = r.Get(), Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(box, r.Label);
            box.Attach(hotkeys.Suspend, hotkeys.Register);
            box.Changed += () =>
            {
                r.Set(box.Value);
                saved();
                hotkeys.Register();
                Check();
            };
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        var panel = new StackPanel();
        panel.Children.Add(grid);
        warning.Margin = new Thickness(20, 0, 20, 0);
        panel.Children.Add(warning);
        var done = new Button { Content = T("Done"), MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 4, 20, 20) };
        panel.Children.Add(done);
        Content = panel;
        Check();
    }
}

/// <summary>A volume as it's shown everywhere: mute, the slider, its %, and the button for its shortcuts.</summary>
internal sealed class VolumeControl : DockPanel
{
    private readonly IVolume v;
    private readonly Slider slider = new() { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, SmallChange = 1, LargeChange = 5 };
    private readonly TextBlock percent = new() { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly Button mute = IconButton("");
    private readonly Button keys = IconButton("");
    private bool showing;

    /// <summary>Raised whenever every control should show its volume again (a shortcut changed one).</summary>
    public static event Action? RefreshAll;
    public static void Refresh() => RefreshAll?.Invoke();

    public VolumeControl(string title, IVolume v, VolumeHotkeys hotkeys, Action changed)
    {
        this.v = v;
        LastChildFill = true;
        AutomationProperties.SetName(slider, F("{0} volume", title));
        AutomationProperties.SetName(mute, F("Mute {0}", title));
        AutomationProperties.SetName(keys, F("{0} shortcuts", title));
        mute.Click += (_, _) => { v.Muted = !v.Muted; Show(); changed(); };
        keys.Click += (_, _) =>
        {
            new VolumeKeysWindow(title, v, hotkeys, changed) { Owner = Window.GetWindow(this) }.ShowDialog();
            Show();
        };
        slider.ValueChanged += (_, _) =>
        {
            if (showing) return;
            v.Volume = Math.Round(slider.Value / 100, 2);
            percent.Text = $"{v.Volume * 100:0}%";
            changed();
        };
        SetDock(mute, Dock.Left);
        Children.Add(mute);
        SetDock(keys, Dock.Right);
        Children.Add(keys);
        SetDock(percent, Dock.Right);
        Children.Add(percent);
        Children.Add(slider);
        Show();
        Loaded += (_, _) => { RefreshAll -= Show; RefreshAll += Show; Show(); };
        Unloaded += (_, _) => RefreshAll -= Show;
    }

    private void Show()
    {
        showing = true;
        slider.Value = v.Volume * 100;
        showing = false;
        percent.Text = $"{v.Volume * 100:0}%";
        ((TextBlock)mute.Content).Text = v.Muted ? "" : "";
        mute.ToolTip = v.Muted ? T("Unmute") : T("Mute");
        slider.Opacity = v.Muted ? 0.45 : 1;
        ((TextBlock)keys.Content).SetResourceReference(TextBlock.ForegroundProperty, v.Keys.Any ? "AccentBrush" : "TextBrush");
        keys.ToolTip = v.Keys.Any
            ? string.Join("\n", new[] { (T("Volume Up"), v.Keys.Up), (T("Volume Down"), v.Keys.Down), (T("Mute"), v.Keys.Mute) }.Where(k => k.Item2 != null).Select(k => $"{k.Item1}: {k.Item2}"))
            : T("Shortcuts");
    }

    internal static Button IconButton(string glyph)
    {
        var t = new TextBlock { Text = glyph, FontSize = 14 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return new Button { Content = t, Padding = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    }
}
