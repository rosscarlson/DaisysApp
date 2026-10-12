using System.Runtime.InteropServices;

namespace DaisysApp.Applets.AudioTools.Tests.Audio;

/// <summary>
/// A microphone's automatic gain control (AGC), switched off while the wizards measure with it and back on after.
/// Cheap USB audio adapters (the kind with a headset jack) often have AGC in the chip: after a moment of quiet the gain
/// goes to its top, and as soon as there's sound it's pulled down by 10–15 dB. Then the room's background noise reads far
/// louder than it is, every speaker seems barely above it, and the speakers' differences are squeezed. Windows' raw
/// mode doesn't reach it; the adapter's own AGC control does, where the driver shows it (an AGC part in the device's
/// topology, which is also what Sound settings → the mic → Properties → Advanced/Levels → "AGC" switches).
/// </summary>
internal sealed class MicAgc : IDisposable
{
    private readonly List<IAudioAutoGainControl> switchedOff = new();

    /// <summary>What was found: "none" (the driver shows no AGC), "off already", or "switched off" (and how many).</summary>
    public string Status { get; }

    /// <summary>Whether the mic has an AGC control at all.</summary>
    public bool Found { get; }

    /// <summary>Finds every AGC control on the mic's path and switches it off (remembering which to switch back on).</summary>
    public MicAgc(string deviceId)
    {
        int found = 0, wasOn = 0;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(deviceId, out var device));
            var iid = typeof(IDeviceTopology).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var topoObj));
            var topology = (IDeviceTopology)topoObj;
            // the endpoint's one connector leads to the adapter's own topology; its parts lead back to the mic jack
            Marshal.ThrowExceptionForHR(topology.GetConnector(0, out var endpointConnector));
            Marshal.ThrowExceptionForHR(endpointConnector.GetConnectedTo(out var adapterConnector));
            var start = (IPart)adapterConnector;
            var seen = new HashSet<string>();
            var queue = new Queue<IPart>();
            queue.Enqueue(start);
            while (queue.Count > 0 && seen.Count < 64)
            {
                var part = queue.Dequeue();
                part.GetGlobalId(out string id);
                if (!seen.Add(id)) continue;
                var agcIid = typeof(IAudioAutoGainControl).GUID;
                if (part.Activate(1 /* CLSCTX_INPROC_SERVER */, ref agcIid, out var agcObj) >= 0 && agcObj is IAudioAutoGainControl agc)
                {
                    found++;
                    if (agc.GetEnabled(out bool on) >= 0 && on)
                    {
                        var none = Guid.Empty;
                        if (agc.SetEnabled(false, ref none) >= 0)
                        {
                            wasOn++;
                            switchedOff.Add(agc);
                        }
                    }
                }
                // towards the mic (for a capture device the signal comes in from the jack)
                foreach (bool incoming in new[] { true, false })
                {
                    int hr = incoming ? part.EnumPartsIncoming(out var list) : part.EnumPartsOutgoing(out list);
                    if (hr < 0 || list == null) continue;
                    list.GetCount(out uint n);
                    for (uint i = 0; i < n; i++)
                        if (list.GetPart(i, out var next) >= 0) queue.Enqueue(next);
                }
            }
        }
        catch { }
        Found = found > 0;
        Status = found == 0 ? "none" : wasOn > 0 ? $"switched off ({wasOn} of {found})" : "off already";
    }

    /// <summary>Puts back the AGC it switched off.</summary>
    public void Dispose()
    {
        foreach (var agc in switchedOff)
        {
            var none = Guid.Empty;
            try { agc.SetEnabled(true, ref none); } catch { }
        }
        switchedOff.Clear();
    }

    // ---------------------------------------------------------------- Windows' device topology (devicetopology.h)

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector to);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector to);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPart
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetLocalId(out uint id);
        [PreserveSig] int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetPartType(out int type);
        [PreserveSig] int GetSubType(out Guid subType);
        [PreserveSig] int GetControlInterfaceCount(out uint count);
        [PreserveSig] int GetControlInterface(uint index, out IntPtr control);
        [PreserveSig] int EnumPartsIncoming(out IPartsList parts);
        [PreserveSig] int EnumPartsOutgoing(out IPartsList parts);
        [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
        [PreserveSig] int Activate(uint clsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPartsList
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetPart(uint index, out IPart part);
    }

    [ComImport, Guid("85401FD4-6DE4-4b9d-9869-2D6753A82F3C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioAutoGainControl
    {
        [PreserveSig] int GetEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [PreserveSig] int SetEnabled([MarshalAs(UnmanagedType.Bool)] bool enable, ref Guid eventContext);
    }
}
