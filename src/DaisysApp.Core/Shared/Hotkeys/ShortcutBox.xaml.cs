using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Click, then press a key combination (with Ctrl, Alt or Shift) to set a global shortcut; Esc cancels. With
/// <see cref="AllowControllerButtons"/> a controller / wheel button can be pressed instead. Every shortcut in the app
/// (<see cref="Hotkeys"/>) is paused while listening, so a combination already used elsewhere can still be typed.
/// Give it a <see cref="Hotkey"/> and it does the rest: what may be pressed, storing it, and a line under it when the
/// shortcut is taken by another program or used by another binding too.
/// </summary>
public partial class ShortcutBox : UserControl
{
    private Action? suspend, resume;
    private IDisposable? controllers, paused;
    private string? value;
    private bool listening;
    private Hotkey? hotkey;

    public ShortcutBox()
    {
        InitializeComponent();
        Show();
        Loaded += (_, _) => Watch(true);
        Unloaded += (_, _) =>
        {
            if (listening) StopListening();
            Watch(false);
        };
    }

    public ShortcutBox(Hotkey hotkey) : this() => Hotkey = hotkey;

    /// <summary>The binding it shows and sets.</summary>
    public Hotkey? Hotkey
    {
        get => hotkey;
        set
        {
            Watch(false);
            hotkey = value;
            if (value != null)
            {
                AllowControllerButtons = value.Spec.Buttons;
                AllowPlainKeys = value.Spec.Plain;
                System.Windows.Automation.AutomationProperties.SetName(this, value.Spec.Name);
            }
            if (IsLoaded) Watch(true);
            Reload();
        }
    }

    /// <summary>The × that clears it (Settings → Shortcuts has a Delete instead).</summary>
    public bool ShowClear
    {
        get => ClearButton.Visibility == Visibility.Visible;
        set => ClearButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Starts listening, as a click does (a window that opens to pick one).</summary>
    public void Listen()
    {
        if (!listening) Capture_Click(this, new RoutedEventArgs());
    }

    private bool watching;

    private void Watch(bool on)
    {
        if (hotkey == null || on == watching) return;
        watching = on;
        if (on) hotkey.Changed += Reload; else hotkey.Changed -= Reload;
    }

    private void Reload()
    {
        if (hotkey == null) return;
        if (!listening) value = hotkey.Value;
        Show();
    }

    /// <summary>Raised when the shortcut is set or cleared.</summary>
    public event Action? Changed;

    /// <summary>Also accept a game controller / wheel button press.</summary>
    public bool AllowControllerButtons { get; set; }

    /// <summary>Also accept a key by itself, without Ctrl, Alt or Shift (F9, NumPad5…).</summary>
    public bool AllowPlainKeys { get; set; }

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
        string prompt = AllowPlainKeys
            ? AllowControllerButtons ? T("Press a key, a key combination or a controller button… (Esc to cancel)") : T("Press a key or a key combination… (Esc to cancel)")
            : AllowControllerButtons ? T("Press a key combination or controller button… (Esc to cancel)") : T("Press a key combination… (Esc to cancel)");
        CaptureText.Text = listening ? prompt : value ?? T("Click to set a shortcut");
        CaptureText.Opacity = listening || value != null ? 1 : 0.6;
        ClearButton.IsEnabled = value != null && !listening;
        ShowNote();
    }

    private void ShowNote()
    {
        string? note = null;
        bool bad = false;
        if (hotkey?.Value != null && !listening)
        {
            if (hotkey.Failed) { note = T("Another program has it, so it doesn't work here"); bad = true; }
            else if (hotkey.Clashes is { Count: > 0 } c) note = F("Also: {0}", string.Join(", ", c.Select(h => h.Label)));
        }
        NoteText.Text = note ?? "";
        NoteText.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
        NoteText.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (listening) return;
        listening = true;
        paused = Hotkeys.Suspend();
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
        if (HotkeyManager.FromKeyEvent(e, AllowPlainKeys) is { } shortcut) Set(shortcut);
    }

    private void OnControllerButton(string button)
    {
        if (listening) Set(button);
    }

    private void Set(string shortcut)
    {
        value = shortcut;
        // (bound before listening stops: resuming re-registers and raises Changed, which reloads the stored value)
        hotkey?.Bind(value);
        StopListening();
        Changed?.Invoke();
        Show();
    }

    private void Store()
    {
        hotkey?.Bind(value);
        Changed?.Invoke();
        Show();
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
        paused?.Dispose();
        paused = null;
        Show();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Value = null;
        Store();
    }
}
