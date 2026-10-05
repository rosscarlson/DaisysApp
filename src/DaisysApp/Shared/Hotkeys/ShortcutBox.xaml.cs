using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Click, then press a key combination (with Ctrl, Alt or Shift) to set a global shortcut; Esc cancels. With
/// <see cref="AllowControllerButtons"/> a controller / wheel button can be pressed instead. Registered hotkeys are
/// paused while listening, so a combination already used elsewhere can still be typed.
/// </summary>
public partial class ShortcutBox : UserControl
{
    private Action? suspend, resume;
    private IDisposable? controllers;
    private string? value;
    private bool listening;

    public ShortcutBox()
    {
        InitializeComponent();
        Show();
        Unloaded += (_, _) => { if (listening) StopListening(); };
    }

    /// <summary>Raised when the shortcut is set or cleared.</summary>
    public event Action? Changed;

    /// <summary>Also accept a game controller / wheel button press.</summary>
    public bool AllowControllerButtons { get; set; }

    /// <summary>Called when listening starts and stops, to pause and restore the owner's registered hotkeys.</summary>
    public void Attach(Action suspend, Action resume)
    {
        this.suspend = suspend;
        this.resume = resume;
    }

    public string? Value
    {
        get => value;
        set
        {
            this.value = string.IsNullOrWhiteSpace(value) ? null : value;
            Show();
        }
    }

    private void Show()
    {
        string prompt = AllowControllerButtons ? "Press a key combination or controller button… (Esc to cancel)" : "Press a key combination… (Esc to cancel)";
        CaptureText.Text = listening ? prompt : value ?? "Click to set a shortcut";
        CaptureText.Opacity = listening || value != null ? 1 : 0.6;
        ClearButton.IsEnabled = value != null && !listening;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (listening) return;
        listening = true;
        suspend?.Invoke();
        if (AllowControllerButtons)
        {
            ControllerButtons.Pressed += OnControllerButton;
            controllers = ControllerButtons.Listen();
        }
        CaptureButton.Focus();
        Show();
    }

    private void CaptureButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!listening) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopListening();
            return;
        }
        if (HotkeyManager.FromKeyEvent(e) is { } shortcut) Set(shortcut);
    }

    private void OnControllerButton(string button)
    {
        if (listening) Set(button);
    }

    private void Set(string shortcut)
    {
        value = shortcut;
        StopListening();
        Changed?.Invoke();
    }

    private void CaptureButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (listening) StopListening();
    }

    private void StopListening()
    {
        listening = false;
        ControllerButtons.Pressed -= OnControllerButton;
        controllers?.Dispose();
        controllers = null;
        resume?.Invoke();
        Show();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Value = null;
        Changed?.Invoke();
    }
}
