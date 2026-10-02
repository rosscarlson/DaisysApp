using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DaisysApp.Tools.AudioLevel.Voicemeeter;

public enum VoicemeeterKind { None = 0, Standard = 1, Banana = 2, Potato = 3 }

public enum InsertState { Off, Running, Busy, NotRunning, Error }

/// <summary>
/// Minimal binding to VoicemeeterRemote64.dll: login, and the bus "output insert" audio callback, which hands us every
/// Voicemeeter bus channel each audio frame so we can apply a per-channel gain in real time (this is how VB-Audio's
/// own 8x8 Matrix tool works). Only one application can hold the output insert at a time.
/// The DLL allows one login per process, so this is static.
/// </summary>
public static unsafe class VoicemeeterRemote
{
    private const int CbStarting = 1, CbChange = 3, CbBufferOut = 11;
    private const int ModeOutputInsert = 2;
    private const int MaxChannels = 128;

    private static IntPtr lib;
    private static delegate* unmanaged[Stdcall]<int> login, logout, cbStart, cbStop, cbUnregister;
    private static delegate* unmanaged[Stdcall]<int*, int> getType;
    private static delegate* unmanaged[Stdcall]<int, delegate* unmanaged[Stdcall]<void*, int, void*, int, int>, void*, byte*, int> cbRegister;

    private static bool loggedIn, registered;
    private static readonly float[] target = new float[MaxChannels];  // written by the UI thread
    private static readonly float[] current = new float[MaxChannels]; // audio thread only
    private static long lastBufferTicks;
    private static volatile bool changeRequested;

    static VoicemeeterRemote()
    {
        Array.Fill(target, 1f);
        Array.Fill(current, 1f);
    }

    public static bool IsRegistered => registered;

    /// <summary>True while Voicemeeter is actually calling us with audio.</summary>
    public static bool IsStreaming => registered && Environment.TickCount64 - Interlocked.Read(ref lastBufferTicks) < 2000;

    public static int SampleRate { get; private set; }

    /// <summary>Location of VoicemeeterRemote64.dll from the Voicemeeter uninstall entry (as in the SDK samples).</summary>
    public static string? FindDll()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}");
                if (key?.GetValue("UninstallString") is string uninstall)
                {
                    string dir = Path.GetDirectoryName(uninstall.Trim('"'))!;
                    string dll = Path.Combine(dir, "VoicemeeterRemote64.dll");
                    if (File.Exists(dll)) return dll;
                }
            }
            catch { }
        }
        string fallback = @"C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll";
        return File.Exists(fallback) ? fallback : null;
    }

    private static bool EnsureLoaded(out string? error)
    {
        error = null;
        if (lib != IntPtr.Zero) return true;
        string? path = FindDll();
        if (path == null)
        {
            error = "Voicemeeter isn't installed (VoicemeeterRemote64.dll not found).";
            return false;
        }
        try
        {
            var h = NativeLibrary.Load(path);
            login = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_Login");
            logout = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_Logout");
            getType = (delegate* unmanaged[Stdcall]<int*, int>)NativeLibrary.GetExport(h, "VBVMR_GetVoicemeeterType");
            cbRegister = (delegate* unmanaged[Stdcall]<int, delegate* unmanaged[Stdcall]<void*, int, void*, int, int>, void*, byte*, int>)
                NativeLibrary.GetExport(h, "VBVMR_AudioCallbackRegister");
            cbStart = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_AudioCallbackStart");
            cbStop = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_AudioCallbackStop");
            cbUnregister = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_AudioCallbackUnregister");
            lib = h;
            return true;
        }
        catch (Exception ex)
        {
            error = "Couldn't load the Voicemeeter Remote API: " + ex.Message;
            return false;
        }
    }

    private static bool Login(out string? error)
    {
        if (!EnsureLoaded(out error)) return false;
        if (loggedIn) return true;
        int r = login(); // 0 = OK, 1 = OK but Voicemeeter not running, < 0 = error
        if (r < 0)
        {
            error = $"Couldn't connect to Voicemeeter (error {r}).";
            return false;
        }
        loggedIn = true;
        return true;
    }

    public static VoicemeeterKind Kind
    {
        get
        {
            if (!loggedIn) return VoicemeeterKind.None;
            int t = 0;
            return getType(&t) == 0 && t is >= 1 and <= 3 ? (VoicemeeterKind)t : VoicemeeterKind.None;
        }
    }

    /// <summary>Bus names in output-insert buffer order (8 channels each).</summary>
    public static IReadOnlyList<string> BusNames(VoicemeeterKind kind) => kind switch
    {
        VoicemeeterKind.Standard => ["A", "B"],
        VoicemeeterKind.Banana => ["A1", "A2", "A3", "B1", "B2"],
        VoicemeeterKind.Potato => ["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"],
        _ => [],
    };

    /// <summary>Connects and starts the output insert. On Busy, <paramref name="detail"/> names the app holding it.</summary>
    public static InsertState Start(out string? detail)
    {
        if (!Login(out detail)) return InsertState.Error;
        if (Kind == VoicemeeterKind.None)
        {
            detail = "Voicemeeter isn't running.";
            return InsertState.NotRunning;
        }
        if (!registered)
        {
            byte* name = stackalloc byte[64];
            new Span<byte>(name, 64).Clear();
            "Daisy's App - Audio Leveler"u8.CopyTo(new Span<byte>(name, 63));
            int r = cbRegister(ModeOutputInsert, &Callback, null, name);
            if (r == 1)
            {
                detail = Marshal.PtrToStringAnsi((IntPtr)name) is { Length: > 0 } other ? other : "another application";
                return InsertState.Busy;
            }
            if (r != 0)
            {
                detail = $"Voicemeeter refused the audio connection (error {r}).";
                return InsertState.Error;
            }
            registered = true;
        }
        int s = cbStart();
        if (s != 0)
        {
            detail = $"Voicemeeter couldn't start the audio stream (error {s}).";
            return InsertState.Error;
        }
        Interlocked.Exchange(ref lastBufferTicks, Environment.TickCount64);
        detail = null;
        return InsertState.Running;
    }

    /// <summary>Voicemeeter asks clients to restart after a sample-rate or buffer change.</summary>
    public static bool TakeChangeRequest()
    {
        if (!changeRequested) return false;
        changeRequested = false;
        return true;
    }

    public static void RestartStream()
    {
        if (!registered) return;
        cbStop();
        cbStart();
    }

    /// <summary>Releases the insert (Voicemeeter passes audio through untouched again).</summary>
    public static void Unregister()
    {
        if (!registered) return;
        cbUnregister();
        registered = false;
    }

    public static void Shutdown()
    {
        Unregister();
        if (loggedIn)
        {
            logout();
            loggedIn = false;
        }
    }

    /// <summary>Sets the gain for one channel of the output-insert buffer (bus index * 8 + channel).</summary>
    public static void SetGain(int bufferChannel, double db)
    {
        if (bufferChannel is < 0 or >= MaxChannels) return;
        Volatile.Write(ref target[bufferChannel], (float)Math.Pow(10, db / 20));
    }

    public static void ResetAllGains()
    {
        for (int i = 0; i < MaxChannels; i++) Volatile.Write(ref target[i], 1f);
    }

    // Real-time audio thread: no allocation, no locks.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Callback(void* user, int command, void* data, int nnn)
    {
        switch (command)
        {
            case CbStarting:
                SampleRate = ((AudioInfo*)data)->SampleRate;
                break;
            case CbChange:
                changeRequested = true;
                break;
            case CbBufferOut:
                Process((AudioBuffer*)data);
                break;
        }
        return 0;
    }

    private static void Process(AudioBuffer* b)
    {
        Interlocked.Exchange(ref lastBufferTicks, Environment.TickCount64);
        int n = b->Samples;
        int channels = Math.Min(Math.Min(b->Inputs, b->Outputs), MaxChannels);
        for (int i = 0; i < channels; i++)
        {
            float* r = (float*)b->Read[i];
            float* w = (float*)b->Write[i];
            if (r == null || w == null) continue;
            float g0 = current[i];
            float g1 = Volatile.Read(ref target[i]);
            if (g0 == 1f && g1 == 1f)
            {
                if (r != w) Buffer.MemoryCopy(r, w, n * sizeof(float), n * sizeof(float));
            }
            else if (g0 == g1)
            {
                for (int k = 0; k < n; k++) w[k] = r[k] * g1;
            }
            else
            {
                // ramp across the frame so knob moves don't click
                float step = (g1 - g0) / n, g = g0;
                for (int k = 0; k < n; k++)
                {
                    g += step;
                    w[k] = r[k] * g;
                }
                current[i] = g1;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioInfo
    {
        public int SampleRate;
        public int SamplesPerFrame;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioBuffer
    {
        public int SampleRate;
        public int Samples;
        public int Inputs;
        public int Outputs;
        public fixed long Read[MaxChannels];
        public fixed long Write[MaxChannels];
    }
}
