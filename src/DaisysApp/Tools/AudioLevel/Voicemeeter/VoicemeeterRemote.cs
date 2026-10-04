using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DaisysApp.Tools.AudioLevel.Voicemeeter;

public enum VoicemeeterKind { None = 0, Standard = 1, Banana = 2, Potato = 3 }

/// <summary>
/// Minimal binding to VoicemeeterRemote64.dll for reading and writing Voicemeeter parameters (e.g.
/// "Bus[0].EQ.channel[1].cell[4].gain"). Changes are made inside Voicemeeter itself, which saves them with its own
/// settings, so they stay applied whether or not this app is running.
/// The DLL allows one login per process, so this is static.
/// </summary>
public static unsafe class VoicemeeterRemote
{
    private static IntPtr lib;
    private static delegate* unmanaged[Stdcall]<int> login, logout, isDirty;
    private static delegate* unmanaged[Stdcall]<int*, int> getType;
    private static delegate* unmanaged[Stdcall]<byte*, float*, int> getFloat;
    private static delegate* unmanaged[Stdcall]<byte*, char*, int> getStringW;
    private static delegate* unmanaged[Stdcall]<byte*, int> setParameters;

    private static bool loggedIn;

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
            isDirty = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(h, "VBVMR_IsParametersDirty");
            getType = (delegate* unmanaged[Stdcall]<int*, int>)NativeLibrary.GetExport(h, "VBVMR_GetVoicemeeterType");
            getFloat = (delegate* unmanaged[Stdcall]<byte*, float*, int>)NativeLibrary.GetExport(h, "VBVMR_GetParameterFloat");
            getStringW = (delegate* unmanaged[Stdcall]<byte*, char*, int>)NativeLibrary.GetExport(h, "VBVMR_GetParameterStringW");
            setParameters = (delegate* unmanaged[Stdcall]<byte*, int>)NativeLibrary.GetExport(h, "VBVMR_SetParameters");
            lib = h;
            return true;
        }
        catch (Exception ex)
        {
            error = "Couldn't load the Voicemeeter Remote API: " + ex.Message;
            return false;
        }
    }

    /// <summary>Logs in to the Remote API (once per process). False with an explanation if the DLL can't be used.</summary>
    public static bool Connect(out string? error)
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
        // The first parameter read after login needs a refresh; give Voicemeeter a moment to answer.
        for (int i = 0; i < 20 && Refresh() < 0; i++) Thread.Sleep(20);
        return true;
    }

    public static void Disconnect()
    {
        if (!loggedIn) return;
        logout();
        loggedIn = false;
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

    /// <summary>Bus names in Bus[i] index order.</summary>
    public static IReadOnlyList<string> BusNames(VoicemeeterKind kind) => kind switch
    {
        VoicemeeterKind.Standard => ["A", "B"],
        VoicemeeterKind.Banana => ["A1", "A2", "A3", "B1", "B2"],
        VoicemeeterKind.Potato => ["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"],
        _ => [],
    };

    /// <summary>Asks Voicemeeter for fresh parameter values. 1 = something changed, 0 = nothing changed, &lt;0 = error.</summary>
    public static int Refresh() => loggedIn ? isDirty() : -1;

    /// <summary>Reads one parameter; null if it doesn't exist (e.g. not in this Voicemeeter edition) or on error.</summary>
    public static float? Get(string name)
    {
        if (!loggedIn) return null;
        byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
        float value;
        fixed (byte* p = bytes)
            return getFloat(p, &value) == 0 ? value : null;
    }

    /// <summary>Reads a text parameter (e.g. "Bus[0].device.name"); null if it doesn't exist or on error.</summary>
    public static string? GetText(string name)
    {
        if (!loggedIn) return null;
        byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
        char* value = stackalloc char[512];
        new Span<char>(value, 512).Clear();
        fixed (byte* p = bytes)
            if (getStringW(p, value) != 0) return null;
        return new string(value).TrimEnd('\0').Trim();
    }

    /// <summary>Number of physical (hardware output) buses: A1… in this edition.</summary>
    public static int PhysicalBuses(VoicemeeterKind kind) => kind switch
    {
        VoicemeeterKind.Standard => 1,
        VoicemeeterKind.Banana => 3,
        VoicemeeterKind.Potato => 5,
        _ => 0,
    };

    /// <summary>Applies several "Name=value" assignments at once (Voicemeeter's parameter script).</summary>
    public static void Set(IEnumerable<(string Name, double Value)> values)
    {
        if (!loggedIn) throw new InvalidOperationException("Not connected to Voicemeeter.");
        string script = string.Join(";", values.Select(v => v.Name + "=" + v.Value.ToString("0.###", CultureInfo.InvariantCulture)));
        byte[] bytes = Encoding.ASCII.GetBytes(script + "\0");
        int r;
        fixed (byte* p = bytes) r = setParameters(p);
        if (r != 0) throw new InvalidOperationException($"Voicemeeter didn't accept the change (error {r}).");
    }
}
