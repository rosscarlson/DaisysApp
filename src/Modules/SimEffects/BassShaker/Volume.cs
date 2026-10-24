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
/// and mute, and the on / off shortcut: keys (a key by itself too) or controller / button box buttons, in Core's
/// Hotkeys (so in Settings → Shortcuts too).
/// </summary>
internal sealed class VolumeHotkeys : IDisposable
{
    public const double Step = 0.05;
    private const string Prefix = "Shaker.";

    private readonly ShakerSettings settings;
    private readonly Func<IEnumerable<(string Id, string Title, IVolume Volume)>> targets;
    private readonly Action toggle;
    private readonly Dictionary<IVolume, Hotkey[]> byVolume = new();

    /// <summary>A shortcut changed a volume (on the UI thread).</summary>
    public event Action? Changed;

    /// <summary>The on / off shortcut.</summary>
    public Hotkey OnOff { get; private set; } = null!;

    public VolumeHotkeys(ShakerSettings settings, Func<IEnumerable<(string Id, string Title, IVolume Volume)>> targets, Action toggle)
    {
        this.settings = settings;
        this.targets = targets;
        this.toggle = toggle;
    }

    /// <summary>After volumes came or went (a device added, another profile): their shortcuts put in place.</summary>
    public void Register()
    {
        var items = new List<(HotkeySpec, Action)>
        {
            (Spec("OnOff", null, T("On / off"), () => settings.OnOffKey, k => settings.OnOffKey = k), toggle),
        };
        var volumes = targets().ToList();
        foreach (var (id, title, v) in volumes)
        {
            items.Add((Spec(id + ".Up", title, T("Volume Up"), () => v.Keys.Up, k => v.Keys.Up = k), () => Nudge(() => { v.Volume = Math.Min(1, Math.Round(v.Volume + Step, 2)); v.Muted = false; })));
            items.Add((Spec(id + ".Down", title, T("Volume Down"), () => v.Keys.Down, k => v.Keys.Down = k), () => Nudge(() => v.Volume = Math.Max(0, Math.Round(v.Volume - Step, 2)))));
            items.Add((Spec(id + ".Mute", title, T("Mute"), () => v.Keys.Mute, k => v.Keys.Mute = k), () => Nudge(() => v.Muted = !v.Muted)));
        }
        var all = Hotkeys.Replace(Prefix, items);
        OnOff = all[0];
        byVolume.Clear();
        for (int i = 0; i < volumes.Count; i++) byVolume[volumes[i].Volume] = all.Skip(1 + i * 3).Take(3).ToArray();
    }

    private HotkeySpec Spec(string id, string? target, string name, Func<string?> get, Action<string?> set) => new()
    {
        Id = Prefix + id, Module = T("Shaker"), Target = target, Name = name, Plain = true,
        Get = get, Set = k => { set(k); settings.Save(); },
    };

    private void Nudge(Action change)
    {
        change();
        Changed?.Invoke();
    }

    /// <summary>A volume's Up, Down and Mute.</summary>
    public Hotkey[] For(IVolume v)
    {
        if (!byVolume.ContainsKey(v)) Register();
        return byVolume.TryGetValue(v, out var h) ? h : Array.Empty<Hotkey>();
    }

    public void Dispose() => Hotkeys.Replace(Prefix, Array.Empty<(HotkeySpec, Action)>());
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
            HotkeyWindow.Open(F("{0} Shortcuts", title), Window.GetWindow(this), hotkeys.For(v));
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
