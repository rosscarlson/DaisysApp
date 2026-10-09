using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>
/// The volume shortcuts: for each device or app, up, down and mute, as keyboard combinations or controller / wheel
/// buttons. They work anywhere, the tab open or not (with the app in the tray too).
/// </summary>
internal sealed class VolumeHotkeyService : IDisposable
{
    private enum Action { Up, Down, Mute }

    private readonly AudioLevelsSettings settings;
    private readonly HotkeyManager hotkeys = new();
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Dictionary<string, List<(string Key, Action Action)>> bound = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? controllers;
    private bool suspended;

    public VolumeHotkeyService(AudioLevelsSettings settings)
    {
        this.settings = settings;
        hotkeys.Pressed += OnPressed;
    }

    /// <summary>Shortcuts another program already has.</summary>
    public IReadOnlyList<string> Failed { get; private set; } = Array.Empty<string>();

    /// <summary>Raised on the UI thread after a shortcut changed a volume (so the tab can show it straight away).</summary>
    public event System.Action? Changed;

    public void Register()
    {
        bound.Clear();
        foreach (var (key, h) in settings.Hotkeys)
            foreach (var (shortcut, action) in new[] { (h.Up, Action.Up), (h.Down, Action.Down), (h.Mute, Action.Mute) })
                if (!string.IsNullOrWhiteSpace(shortcut))
                    (bound.TryGetValue(shortcut, out var list) ? list : bound[shortcut] = new()).Add((key, action));
        if (suspended) return;
        Failed = hotkeys.RegisterAll(bound.Keys.Where(s => !ControllerButtons.IsButton(s)));
        bool wantControllers = bound.Keys.Any(ControllerButtons.IsButton);
        if (wantControllers && controllers == null)
        {
            ControllerButtons.Pressed += OnPressed;
            controllers = ControllerButtons.Listen();
        }
        else if (!wantControllers && controllers != null)
        {
            ControllerButtons.Pressed -= OnPressed;
            controllers.Dispose();
            controllers = null;
        }
    }

    /// <summary>While a shortcut box is listening, so a combination already in use can still be typed.</summary>
    public void Suspend()
    {
        suspended = true;
        hotkeys.UnregisterAll();
    }

    public void Resume()
    {
        suspended = false;
        Register();
    }

    private void OnPressed(string shortcut)
    {
        if (suspended || !bound.TryGetValue(shortcut, out var targets)) return;
        foreach (var (key, action) in targets)
        {
            try { Act(key, action); }
            catch (Exception ex) { ErrorLog.Write("Audio Levels shortcut", ex); }
        }
        Changed?.Invoke();
    }

    private void Act(string key, Action action)
    {
        int step = settings.Step;
        if (key.StartsWith("app:"))
        {
            foreach (var app in AppVolumes.List(enumerator).Where(a => a.Key == key))
            {
                if (action == Action.Mute) AppVolumes.SetMuted(app, !AppVolumes.Muted(app));
                else AppVolumes.SetVolume(app, AppVolumes.Volume(app) + (action == Action.Up ? step : -step));
            }
            return;
        }
        string id = key[(key.IndexOf(':') + 1)..];
        using var device = enumerator.GetDevice(id);
        var endpoint = device.AudioEndpointVolume;
        if (action == Action.Mute) endpoint.Mute = !endpoint.Mute;
        else
        {
            double now = Math.Round(endpoint.MasterVolumeLevelScalar * 100);
            endpoint.MasterVolumeLevelScalar = (float)(Math.Clamp(now + (action == Action.Up ? step : -step), 0, 100) / 100);
        }
    }

    /// <summary>A target's shortcuts in short, for its row.</summary>
    public string Summary(string key) =>
        settings.Hotkeys.TryGetValue(key, out var h) && h.Any
            ? string.Join(" · ", new[] { (h.Up, "▲"), (h.Down, "▼"), (h.Mute, T("mute")) }.Where(x => x.Item1 != null).Select(x => $"{x.Item2} {x.Item1}"))
            : "";

    public void Dispose()
    {
        if (controllers != null)
        {
            ControllerButtons.Pressed -= OnPressed;
            controllers.Dispose();
        }
        hotkeys.Dispose();
        enumerator.Dispose();
    }
}
