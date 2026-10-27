using System.Windows;
using System.Windows.Interop;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// SimEffects → Input Overlay (ForzaWheel's input overlay until 3.17.0): the pedals' and wheel's last few seconds as
/// traces over the game, and the speed. Read from the profile's game (its telemetry) or from a controller's axes. On /
/// off with a shortcut and a title bar bubble; its settings kept per SimEffects profile, the game the profile's. Keeps
/// InputOverlay.json. See README.md.
/// </summary>
public sealed class InputOverlayApplet : IApplet
{
    private readonly OverlaySettings settings = OverlaySettings.Load();
    private readonly OverlayReader reader;
    private readonly InputOverlayView view;
    private readonly Hotkey hotkey;
    // a hidden window of its own, which DirectInput reads controllers in the background for
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp Input Overlay") { Width = 0, Height = 0, WindowStyle = 0 });
    private OverlayWindow? overlay;
    private bool moving;

    public InputOverlayApplet()
    {
        reader = new OverlayReader(settings, window.Handle);
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "InputOverlay.OnOff", Module = T("Input Overlay"), Name = T("On / off"),
            Get = () => settings.OnOffKey, Set = v => { settings.OnOffKey = v; settings.Save(); },
        }, () => SetEnabled(!settings.Enabled));
        SimProfiles.Register(new ToolScope
        {
            Id = "InputOverlay", Title = T("Input Overlay"), Settings = settings, Props = OverlaySettings.Scoped,
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = ProfileLoaded, OnWithGames = settings.SwitchOnWithGame,
        });
        view = new InputOverlayView(this);
        SimProfiles.Changed += ShowBadge;
    }

    internal OverlaySettings Settings => settings;
    internal OverlayReader Reader => reader;
    internal Hotkey Hotkey => hotkey;
    internal IntPtr Window => window.Handle;
    internal OverlayWindow? Overlay => overlay;

    /// <summary>On or off (ON / OFF, the shortcut, the tray, the Profiles tab): kept in the profile in use.</summary>
    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        Logging.Log.Here.Info($"Input Overlay: switched {(on ? "on" : "off")}");
        settings.Save();
        ShowOverlay();
        ShowBadge();
        view.ShowOnOff();
    }

    /// <summary>Another SimEffects profile is in use: its settings shown, the overlay on or off as it has it.</summary>
    private void ProfileLoaded()
    {
        reader.Changed();
        ShowOverlay();
        ShowBadge();
        view.Render();
    }

    /// <summary>The source or an axis changed: saved, read again, drawn again.</summary>
    internal void Changed()
    {
        settings.Save();
        reader.Changed();
        ShowBadge();
        view.Render();
    }

    internal void ShowBadge() =>
        TitleBadges.Set("InputOverlay", settings.Enabled ? F("Overlay: {0}", SimProfiles.Active.Name) : null, T("The Input Overlay is on"));

    /// <summary>Shows the overlay while it's on (and while it's being placed), closes it otherwise.</summary>
    internal void ShowOverlay()
    {
        if (settings.Enabled || moving)
        {
            if (overlay == null)
            {
                var made = overlay = new OverlayWindow(settings, reader, settings.Save);
                made.EditingChanged += () =>
                {
                    if (made.Editing) return;
                    moving = false;
                    view.ShowMove();
                    Application.Current?.Dispatcher.BeginInvoke(ShowOverlay);
                };
            }
            if (!overlay.IsVisible) overlay.Show();
        }
        else
        {
            overlay?.Close();
            overlay = null;
        }
        reader.Recording = overlay != null;
        Reading();
        view.ShowMove();
    }

    /// <summary>Move / resize: shown (even while off) and placed until Done.</summary>
    internal void Move()
    {
        if (overlay is { Editing: true }) { overlay.EndEdit(); return; }
        moving = true;
        ShowOverlay();
        overlay?.BeginEdit();
        view.ShowMove();
    }

    internal void ResetPlace()
    {
        overlay?.Close();
        overlay = null;
        settings.X = settings.Y = 80;
        settings.Width = settings.Height = 0;
        settings.Save();
        ShowOverlay();
    }

    /// <summary>The reader runs while the overlay or the tab shows.</summary>
    internal void Reading() => reader.Wanted = overlay != null || view.IsVisible;

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        ShowOverlay();
        ShowBadge();
        view.Render();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Input Overlay on"), () => SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        SimProfiles.Changed -= ShowBadge;
        hotkey.Dispose();
        overlay?.Close();
        overlay = null;
        TitleBadges.Set("InputOverlay", null);
        view.Stop();
        reader.Dispose();
        settings.Save();
        window.Dispose();
    }
}
