using System.Runtime.InteropServices;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Mends a bug in Windows' DirectInput (dinput.dll, which winmm's joy* calls use, and dinput8.dll): when a HID device
/// has gone, DIHid_CheckHidList closes the gap in its device list by looking from one slot past the list's end, and
/// if that memory isn't zero copies it in and zeroes 0x68 bytes past the end, corrupting the heap. The rig's crashes
/// since 0.6.3 (0xc0000374 and access violations elsewhere, seconds after a controller is switched off) were this:
/// page heap caught it on 2026-11-05 (4.2.4). Patched in this process only, and only where the code is byte for byte
/// the one known (Windows 11 24H2): the search starts at the list's last slot. Since 4.3.1 (the shell calls it).
/// </summary>
public static class DirectInputFix
{
    // movsxd rdx,[rcx+4]; cmp edx,ebx; jl skip; imul rdi,rdx,68h; sub edx,ebx; mov rbp,rdi; lea r14d,[rdx+1];
    // cmp qword ptr [rdi+rcx+28h],0 (-1: the jump's distance, any)
    private static readonly int[] Buggy =
    {
        0x48, 0x63, 0x51, 0x04, 0x3B, 0xD3, 0x0F, 0x8C, -1, -1, -1, -1,
        0x48, 0x6B, 0xFA, 0x68, 0x2B, 0xD3, 0x48, 0x8B, 0xEF, 0x44, 0x8D, 0x72, 0x01,
        0x48, 0x83, 0x7C, 0x0F, 0x28, 0x00,
    };

    // at 7: jle (skip when no slot lies between); at 12: imul edi,edx,68h; sub edi,68h; sub edx,ebx; mov ebp,edi;
    // mov r14d,edx (13 bytes, as the four replaced; from the last slot down to the one after the gap)
    private static readonly byte[] Mended = { 0x6B, 0xFA, 0x68, 0x83, 0xEF, 0x68, 0x2B, 0xD3, 0x8B, 0xEF, 0x44, 0x8B, 0xF2 };

    private static readonly object gate = new();
    private static bool done;

    /// <summary>Patches both DLLs once (before anything uses them: the app's start, and the joystick API's first call).</summary>
    public static void Apply()
    {
        lock (gate)
        {
            if (done) return;
            done = true;
            if (!Environment.Is64BitProcess) return;
            foreach (var dll in new[] { "dinput.dll", "dinput8.dll" })
            {
                try { Logging.Log.App.Info($"DirectInput fix, {dll}: {Mend(dll)}"); }
                catch (Exception ex) { Logging.Log.App.Warn($"DirectInput fix, {dll}: {ex.Message}"); }
            }
        }
    }

    private static string Mend(string dll)
    {
        // loaded and kept (never freed), so winmm and DirectInput8Create later get this patched copy
        IntPtr module = LoadLibraryExW(dll, IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (module == IntPtr.Zero) return "not found";

        var found = new List<IntPtr>();
        foreach (var (start, size) in CodeSections(module))
        {
            var code = new byte[size];
            Marshal.Copy(start, code, 0, size);
            for (int i = 0; i + Buggy.Length <= size; i++)
            {
                int k = 0;
                while (k < Buggy.Length && (Buggy[k] < 0 || code[i + k] == Buggy[k])) k++;
                if (k == Buggy.Length) found.Add(start + i);
            }
        }
        if (found.Count != 1) return found.Count == 0 ? "not needed (code not the known one)" : $"left alone ({found.Count} matches)";

        IntPtr at = found[0];
        if (!VirtualProtect(at, (UIntPtr)Buggy.Length, PAGE_EXECUTE_READWRITE, out uint old)) return $"can't write ({Marshal.GetLastWin32Error()})";
        Marshal.WriteByte(at + 7, 0x8E);
        Marshal.Copy(Mended, 0, at + 12, Mended.Length);
        VirtualProtect(at, (UIntPtr)Buggy.Length, old, out _);
        FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)Buggy.Length);
        return "patched";
    }

    /// <summary>A loaded image's executable sections (address, size), from its PE headers.</summary>
    private static IEnumerable<(IntPtr Start, int Size)> CodeSections(IntPtr module)
    {
        int pe = Marshal.ReadInt32(module, 0x3C);
        int sections = Marshal.ReadInt16(module, pe + 6);
        int optionalSize = Marshal.ReadInt16(module, pe + 20);
        IntPtr header = module + pe + 24 + optionalSize;
        for (int i = 0; i < sections; i++, header += 40)
        {
            int virtualSize = Marshal.ReadInt32(header, 8), address = Marshal.ReadInt32(header, 12);
            uint characteristics = (uint)Marshal.ReadInt32(header, 36);
            if ((characteristics & IMAGE_SCN_MEM_EXECUTE) != 0 && virtualSize > 0) yield return (module + address, virtualSize);
        }
    }

    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800, PAGE_EXECUTE_READWRITE = 0x40, IMAGE_SCN_MEM_EXECUTE = 0x20000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}
