using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>
/// The volume shortcuts: for each device or app, up, down and mute, as keyboard combinations or controller / wheel
/// buttons (Core's Hotkeys, so in Settings → Shortcuts too). They work anywhere, the tab open or not (with the app in
/// the tray too).
/// </summary>
internal sealed class VolumeHotkeyService : IDisposable
{
    private enum Action { Up, Down, Mute }

    private const string Prefix = "Audio.";

    private readonly AudioLevelsSettings settings;
    private readonly MMDeviceEnumerator enumerator = new();

    public VolumeHotkeyService(AudioLevelsSettings settings) => this.settings = settings;

    /// <summary>Raised on the UI thread after a shortcut changed a volume (so the tab can show it straight away).</summary>
    public event System.Action? Changed;

    /// <summary>Every device's and app's shortcuts, as saved.</summary>
    public void Register()
    {
        Hotkeys.Replace(Prefix, settings.Hotkeys.ToList().SelectMany(kv => Specs(kv.Key, kv.Value.Name)));
        Log.Here.Debug($"Volume shortcuts: {settings.Hotkeys.Count} devices and apps");
    }

    /// <summary>One device's or app's three (made if it had none yet), for its shortcuts window.</summary>
    public Hotkey[] For(string key, string name) => Specs(key, name).Select(x => Hotkeys.Add(x.Spec, x.Pressed)).ToArray();

    private IEnumerable<(HotkeySpec Spec, System.Action Pressed)> Specs(string key, string name)
    {
        foreach (var (action, label) in new[] { (Action.Up, T("Volume up")), (Action.Down, T("Volume down")), (Action.Mute, T("Mute on / off")) })
            yield return (new HotkeySpec
            {
                Id = $"{Prefix}{action}:{key}", Module = T("Audio"), Target = name, Name = label,
                Get = () => settings.Hotkeys.TryGetValue(key, out var h) ? Get(h, action) : null,
                Set = v =>
                {
                    if (!settings.Hotkeys.TryGetValue(key, out var h)) settings.Hotkeys[key] = h = new VolumeHotkeys();
                    h.Name = name;
                    if (action == Action.Up) h.Up = v; else if (action == Action.Down) h.Down = v; else h.Mute = v;
                    if (!h.Any) settings.Hotkeys.Remove(key);
                    settings.Save();
                },
            }, () => Pressed(key, action));
    }

    private static string? Get(VolumeHotkeys h, Action a) => a == Action.Up ? h.Up : a == Action.Down ? h.Down : h.Mute;

    private void Pressed(string key, Action action)
    {
        try { Act(key, action); }
        catch (Exception ex) { ErrorLog.Write("Audio Levels shortcut", ex); }
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
        Hotkeys.Replace(Prefix, Array.Empty<(HotkeySpec, System.Action)>());
        enumerator.Dispose();
    }
}
