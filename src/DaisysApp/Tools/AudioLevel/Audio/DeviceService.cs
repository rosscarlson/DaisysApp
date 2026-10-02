using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DaisysApp.Tools.AudioLevel.Audio;

public sealed class DeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsDefault { get; init; }
    public required WaveFormat MixFormat { get; init; }
    public uint ChannelMask { get; init; }
    public required SpeakerLayoutInfo Layout { get; init; }

    public int Channels => MixFormat.Channels;
    public int SampleRate => MixFormat.SampleRate;
    public string Display => IsDefault ? $"{Name}  (default)" : Name;
    public string Summary => $"{Layout.Name} · {Channels} channel{(Channels == 1 ? "" : "s")} · {SampleRate / 1000.0:0.#} kHz";

    /// <summary>Identifies the speaker layout; if this changes the speaker map must be rebuilt.</summary>
    public string LayoutKey => $"{Id}|{Channels}|{ChannelMask}";
    public string Signature => $"{LayoutKey}|{SampleRate}|{IsDefault}|{Name}";
}

public sealed record CaptureDeviceInfo(string Id, string Name, bool IsDefault)
{
    public string Display => IsDefault ? $"{Name}  (default)" : Name;
}

/// <summary>Enumerates render endpoints and reports changes (plug/unplug, default change, speaker config change).</summary>
public sealed class DeviceService : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly NotificationClient notifications;

    public event Action? DevicesChanged;

    public DeviceService()
    {
        notifications = new NotificationClient(this);
        enumerator.RegisterEndpointNotificationCallback(notifications);
    }

    public List<DeviceInfo> GetDevices()
    {
        string? defaultId = null;
        try
        {
            using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = def.ID;
        }
        catch (COMException) { /* no default device */ }

        var list = new List<DeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try { list.Add(Describe(device, device.ID == defaultId)); }
            catch { /* device could not be opened (busy in exclusive mode, driver issue) - skip it */ }
            finally { device.Dispose(); }
        }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<CaptureDeviceInfo> GetCaptureDevices()
    {
        string? defaultId = null;
        try
        {
            using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            defaultId = def.ID;
        }
        catch (COMException) { /* no default mic */ }

        var list = new List<CaptureDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            try { list.Add(new CaptureDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId)); }
            catch { }
            finally { device.Dispose(); }
        }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public MMDevice GetDevice(string id) => enumerator.GetDevice(id);

    public static WaveFormat GetMixFormat(MMDevice device)
    {
        using var client = device.AudioClient;
        return client.MixFormat;
    }

    private static DeviceInfo Describe(MMDevice device, bool isDefault)
    {
        var format = GetMixFormat(device);
        uint mask = GetChannelMask(format);
        return new DeviceInfo
        {
            Id = device.ID,
            Name = device.FriendlyName,
            IsDefault = isDefault,
            MixFormat = format,
            ChannelMask = mask,
            Layout = SpeakerLayout.Build(format.Channels, mask),
        };
    }

    /// <summary>Reads dwChannelMask (offset 20 of WAVEFORMATEXTENSIBLE), which NAudio doesn't expose publicly.</summary>
    private static uint GetChannelMask(WaveFormat format)
    {
        if (format is not WaveFormatExtensible) return 0;
        int size = Marshal.SizeOf(format);
        if (size < 24) return 0;
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(format, p, false);
            return unchecked((uint)Marshal.ReadInt32(p, 20));
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    public void Dispose()
    {
        try { enumerator.UnregisterEndpointNotificationCallback(notifications); } catch { }
        enumerator.Dispose();
    }

    private sealed class NotificationClient(DeviceService owner) : IMMNotificationClient
    {
        private void Raise() => owner.DevicesChanged?.Invoke();
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise();
        public void OnDeviceAdded(string pwstrDeviceId) => Raise();
        public void OnDeviceRemoved(string deviceId) => Raise();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Raise();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => Raise();
    }
}
