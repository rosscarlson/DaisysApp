using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// The shortcut that starts and stops the emulator from anywhere (in the game too), as Start does (and so Auto Hush):
/// a key combination, registered system-wide, or a controller / button box button, listened for while one is set.
/// Kept in <see cref="ForzaWheelSettings.ToggleHotkey"/>.
/// </summary>
internal sealed class EmulatorHotkey : IDisposable
{
    private readonly ForzaWheelSettings settings;
    private readonly Action toggle;
    private readonly HotkeyManager keys = new();
    private IDisposable? buttons;

    /// <summary>True when the key combination is taken by another program, so it doesn't work.</summary>
    public bool Failed { get; private set; }

    public EmulatorHotkey(ForzaWheelSettings settings, Action toggle)
    {
        this.settings = settings;
        this.toggle = toggle;
        keys.Pressed += OnPressed;
    }

    /// <summary>Registers the shortcut as set (none, a key combination, or a controller button).</summary>
    public void Register()
    {
        string? h = settings.ToggleHotkey;
        bool button = ControllerButtons.IsButton(h);
        Failed = !button && !string.IsNullOrWhiteSpace(h) && keys.RegisterAll(new[] { h! }).Count > 0;
        if (button || string.IsNullOrWhiteSpace(h)) keys.UnregisterAll();
        if (button && buttons == null)
        {
            ControllerButtons.Pressed += OnPressed;
            buttons = ControllerButtons.Listen();
        }
        else if (!button) StopButtons();
        if (Failed) Logging.Log.Here.Warn($"ForzaWheel: the shortcut {h} is taken by another program");
    }

    /// <summary>Lets go of it while a new one is being picked (so pressing it doesn't toggle the emulator).</summary>
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
        if (!string.Equals(shortcut, settings.ToggleHotkey, StringComparison.OrdinalIgnoreCase)) return;
        Logging.Log.Here.Info($"ForzaWheel shortcut {shortcut}: start / stop");
        toggle();
    }

    public void Dispose()
    {
        Suspend();
        keys.Pressed -= OnPressed;
        keys.Dispose();
    }

    /// <summary>The window the button beside Start opens: sets the shortcut, saved as it's set.</summary>
    public void ShowWindow(Window? owner)
    {
        var w = new Window
        {
            Title = T("Start / stop shortcut"), Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Owner = owner,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("Start or stop the wheel emulator"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var box = new ShortcutBox { AllowControllerButtons = true, Value = settings.ToggleHotkey, MinWidth = 300 };
        box.Attach(Suspend, Register);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Check()
        {
            warning.Text = Failed ? F("{0} is already used by another program, so it won't work. Pick another.", settings.ToggleHotkey) : "";
            warning.Visibility = Failed ? Visibility.Visible : Visibility.Collapsed;
        }
        box.Changed += () =>
        {
            settings.ToggleHotkey = box.Value;
            settings.Save();
            Register();
            Check();
        };
        panel.Children.Add(box);
        panel.Children.Add(warning);
        var hint = new TextBlock
        {
            Text = T("Click the box, then press a key combination (with Ctrl, Alt or Shift) or a controller / button box button; × clears it. It works anywhere, in the game too, and does what Start does (Auto Hush included). A wheel button that's also bound to a control here is pressed in the game too, so pick one that isn't."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        };
        hint.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        panel.Children.Add(hint);
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => w.Close();
        panel.Children.Add(close);
        w.Content = panel;
        Check();
        w.ShowDialog();
    }
}
