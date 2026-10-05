using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Applets.Resizer;

/// <summary>
/// Click, then press a key combination (with Ctrl, Alt or Shift) to set a global shortcut; Esc cancels. Registered
/// hotkeys are paused while listening, so a combination already used elsewhere can still be typed.
/// </summary>
public partial class ShortcutBox : UserControl
{
    private ResizerService? service;
    private string? value;
    private bool listening;

    public ShortcutBox()
    {
        InitializeComponent();
        Show();
    }

    /// <summary>Raised when the shortcut is set or cleared.</summary>
    public event Action? Changed;

    public void Attach(ResizerService service) => this.service = service;

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
        CaptureText.Text = listening ? "Press a key combination… (Esc to cancel)" : value ?? "Click to set a shortcut";
        CaptureText.Opacity = listening || value != null ? 1 : 0.6;
        ClearButton.IsEnabled = value != null && !listening;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (listening) return;
        listening = true;
        service?.SuspendHotkeys();
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
        if (HotkeyManager.FromKeyEvent(e) is { } shortcut)
        {
            value = shortcut;
            StopListening();
            Changed?.Invoke();
        }
    }

    private void CaptureButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (listening) StopListening();
    }

    private void StopListening()
    {
        listening = false;
        service?.ResumeHotkeys();
        Show();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Value = null;
        Changed?.Invoke();
    }
}
