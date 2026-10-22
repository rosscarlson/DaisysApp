using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.Joy2Key;

/// <summary>
/// The shortcut that switches Button2Key on and off from anywhere (in the game too), as the On / Off button does: a key
/// combination, registered system-wide, or a controller / button box button, listened for while one is set. Kept in
/// <see cref="Joy2KeySettings.Hotkey"/>.
/// </summary>
internal sealed class OnOffHotkey : IDisposable
{
    private readonly Joy2KeySettings settings;
    private readonly Action toggle;
    private readonly HotkeyManager keys = new();
    private IDisposable? buttons;

    /// <summary>True when the key combination is taken by another program, so it doesn't work.</summary>
    public bool Failed { get; private set; }

    public OnOffHotkey(Joy2KeySettings settings, Action toggle)
    {
        this.settings = settings;
        this.toggle = toggle;
        keys.Pressed += OnPressed;
    }

    /// <summary>Registers the shortcut as set (none, a key combination, or a controller button).</summary>
    public void Register()
    {
        string? h = settings.Hotkey;
        bool button = ControllerButtons.IsButton(h);
        Failed = !button && !string.IsNullOrWhiteSpace(h) && keys.RegisterAll(new[] { h! }).Count > 0;
        if (button || string.IsNullOrWhiteSpace(h)) keys.UnregisterAll();
        if (button && buttons == null)
        {
            ControllerButtons.Pressed += OnPressed;
            buttons = ControllerButtons.Listen();
        }
        else if (!button) StopButtons();
        if (Failed) Logging.Log.Here.Warn($"Button2Key: the shortcut {h} is taken by another program");
    }

    /// <summary>Lets go of it while a new one is being picked (so pressing it doesn't switch Button2Key).</summary>
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
        if (!string.Equals(shortcut, settings.Hotkey, StringComparison.OrdinalIgnoreCase)) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Logging.Log.Here.Info($"Button2Key shortcut {shortcut}: on / off");
            toggle();
        });
    }

    public void Dispose()
    {
        Suspend();
        keys.Pressed -= OnPressed;
        keys.Dispose();
    }

    /// <summary>The window the button beside On / Off opens: sets the shortcut, saved as it's set.</summary>
    public void ShowWindow(Window? owner)
    {
        var w = new Window
        {
            Title = T("On / off shortcut"), Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Owner = owner,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("Switch Button2Key on or off"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var box = new ShortcutBox { AllowControllerButtons = true, Value = settings.Hotkey, MinWidth = 300 };
        box.Attach(Suspend, Register);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Check()
        {
            warning.Text = Failed ? F("Another program has {0}: pick another shortcut.", settings.Hotkey) : "";
            warning.Visibility = Failed ? Visibility.Visible : Visibility.Collapsed;
        }
        box.Changed += () =>
        {
            settings.Hotkey = box.Value;
            settings.Save();
            Register();
            Check();
        };
        panel.Children.Add(box);
        panel.Children.Add(warning);
        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => w.Close();
        panel.Children.Add(close);
        w.Content = panel;
        Check();
        w.ShowDialog();
    }
}
