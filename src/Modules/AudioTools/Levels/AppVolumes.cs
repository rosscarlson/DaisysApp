using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>One program's sound, as Windows' volume mixer has it: its sessions on every playback device.</summary>
internal sealed record AppAudio(string Key, string Name, IReadOnlyList<AudioSessionControl> Sessions);

/// <summary>
/// Per-app volume: every program playing (or that has played) sound has an audio session per playback device, with its
/// own volume and mute — the volume mixer's sliders. An app's sessions are grouped by its program name, so an app
/// playing to two devices is one row, and setting it sets both.
/// </summary>
internal static class AppVolumes
{
    /// <summary>The apps with sound sessions now, sorted by name. Dispose isn't needed: the sessions belong to the devices.</summary>
    public static List<AppAudio> List(MMDeviceEnumerator enumerator)
    {
        var byKey = new Dictionary<string, (string Name, List<AudioSessionControl> Sessions)>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            SessionCollection sessions;
            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                sessions = manager.Sessions;
            }
            catch { continue; }
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                    var (key, name) = Identify(s);
                    if (key == null) continue;
                    if (!byKey.TryGetValue(key, out var entry)) byKey[key] = entry = (name, new List<AudioSessionControl>());
                    entry.Sessions.Add(s);
                }
                catch { /* the program just ended */ }
            }
        }
        return byKey.Select(kv => new AppAudio(kv.Key, kv.Value.Name, kv.Value.Sessions))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static readonly Dictionary<uint, (string Key, string Name)?> names = new();

    /// <summary>"app:discord" and "Discord" for a session of Discord.exe; system sounds have their own.</summary>
    private static (string? Key, string Name) Identify(AudioSessionControl s)
    {
        if (s.IsSystemSoundsSession) return ("app:#system", T("System sounds"));
        uint pid = s.GetProcessID;
        if (pid == 0) return (null, "");
        lock (names)
        {
            if (names.TryGetValue(pid, out var cached)) return cached is { } c ? c : (null, "");
            (string, string)? found = null;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                string exe = p.ProcessName;
                string name = exe;
                try
                {
                    string? file = p.MainModule?.FileName;
                    if (file != null && FileVersionInfo.GetVersionInfo(file).FileDescription is { Length: > 0 and < 60 } d) name = d.Trim();
                }
                catch { /* no access to another user's or an elevated process: its name will do */ }
                if (!string.IsNullOrWhiteSpace(s.DisplayName) && !s.DisplayName.StartsWith('@')) name = s.DisplayName;
                found = ("app:" + exe.ToLowerInvariant(), name);
            }
            catch { }
            if (names.Count > 300) names.Clear();
            names[pid] = found;
            return found is { } f ? f : (null, "");
        }
    }

    public static double Volume(AppAudio a) => a.Sessions.Count == 0 ? 0 : Math.Round(a.Sessions[0].SimpleAudioVolume.Volume * 100);
    public static bool Muted(AppAudio a) => a.Sessions.Count > 0 && a.Sessions[0].SimpleAudioVolume.Mute;

    public static void SetVolume(AppAudio a, double percent)
    {
        foreach (var s in a.Sessions)
            try { s.SimpleAudioVolume.Volume = (float)(Math.Clamp(percent, 0, 100) / 100); } catch { }
    }

    public static void SetMuted(AppAudio a, bool muted)
    {
        foreach (var s in a.Sessions)
            try { s.SimpleAudioVolume.Mute = muted; } catch { }
    }
}
