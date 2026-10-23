using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Terminal.Wpf;
using Microsoft.Win32;

namespace DaisysApp.Applets.Tools.Terminal;

public enum ShellKind { Cmd, PowerShell, Wsl }

/// <summary>The three shells (how each is started, its name) and how the terminal looks: Windows' console defaults.</summary>
internal static class Shells
{
    public static readonly ShellKind[] All = [ShellKind.Cmd, ShellKind.PowerShell, ShellKind.Wsl];

    public static string Name(ShellKind k) => k switch
    {
        ShellKind.PowerShell => "PowerShell",
        ShellKind.Wsl => "WSL",
        _ => "CMD",
    };

    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    private static string Exe(ShellKind k) => k switch
    {
        ShellKind.PowerShell => Path.Combine(System32, @"WindowsPowerShell\v1.0\powershell.exe"),
        ShellKind.Wsl => Path.Combine(System32, "wsl.exe"),
        _ => Path.Combine(System32, "cmd.exe"),
    };

    /// <summary>The command line it's started with (WSL in the Linux home folder).</summary>
    public static string CommandLine(ShellKind k) => k == ShellKind.Wsl ? $"\"{Exe(k)}\" ~" : $"\"{Exe(k)}\"";

    /// <summary>Its name if it isn't on this PC (WSL isn't always), else null.</summary>
    public static string? Missing(ShellKind k) => File.Exists(Exe(k)) ? null : Name(k);

    // ---- the look: Windows' console defaults (HKCU\Console, as Console Properties sets them)

    /// <summary>Campbell, Windows' console colours since 2019 (and Windows Terminal's), for any not set.</summary>
    private static readonly uint[] Campbell =
    [
        0x0C0C0C, 0xDA3700, 0x0EA113, 0xDD963A, 0x1F0FC5, 0x981788, 0x009CC1, 0xCCCCCC,
        0x767676, 0xFF783B, 0x0CC616, 0xD6D661, 0x5648E7, 0x9E00B4, 0xA5F1F9, 0xF2F2F2,
    ];

    public sealed record Look(TerminalTheme Theme, string Font, short Size, Color Background);

    public static Look ConsoleLook()
    {
        var table = (uint[])Campbell.Clone();
        int screen = 0x07, cursor = 25, height = 16;
        string face = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Console");
            if (key != null)
            {
                for (int i = 0; i < 16; i++)
                    if (key.GetValue($"ColorTable{i:00}") is int c) table[i] = (uint)c & 0xFFFFFF;
                if (key.GetValue("ScreenColors") is int s) screen = s;
                if (key.GetValue("CursorSize") is int cs) cursor = cs;
                if (key.GetValue("FontSize") is int fs && (fs >> 16) is > 4 and < 100) height = fs >> 16;
                face = key.GetValue("FaceName") as string ?? "";
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Terminal: the console's settings couldn't be read", ex); }

        uint fore = table[screen & 0xF], back = table[(screen >> 4) & 0xF];
        var theme = new TerminalTheme
        {
            DefaultForeground = fore,
            DefaultBackground = back,
            DefaultSelectionBackground = fore,
            CursorStyle = cursor > 50 ? CursorStyle.BlinkingBlock : CursorStyle.BlinkingUnderline,
            ColorTable = table,
        };
        // (the console's "default" font is Cascadia Mono on Windows 11, Consolas before it)
        if (face.Length == 0 || face.StartsWith("__", StringComparison.Ordinal) || !Installed(face))
            face = Installed("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        // (the console keeps the font's height in pixels at 96 dpi; the terminal wants points)
        short points = (short)Math.Max(6, Math.Round(height * 0.75));
        return new Look(theme, face, points, Color.FromRgb((byte)(back & 0xFF), (byte)((back >> 8) & 0xFF), (byte)((back >> 16) & 0xFF)));
    }

    private static bool Installed(string family) =>
        Fonts.SystemFontFamilies.Any(f => f.Source.Equals(family, StringComparison.OrdinalIgnoreCase));

    // ---- the terminal control's own DLL, which is in the module's runtimes folder

    private static bool resolving;

    /// <summary>Points the terminal control at its native DLL (a module's isn't looked for by name otherwise).</summary>
    public static void FindNative()
    {
        if (resolving) return;
        resolving = true;
        var asm = typeof(TerminalControl).Assembly;
        NativeLibrary.SetDllImportResolver(asm, (name, _, _) =>
        {
            if (!name.StartsWith("Microsoft.Terminal.Control", StringComparison.OrdinalIgnoreCase)) return 0;
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                Architecture.X86 => "win-x86",
                _ => "win-x64",
            };
            string folder = Path.GetDirectoryName(asm.Location) ?? AppContext.BaseDirectory;
            foreach (var path in new[] { Path.Combine(folder, "runtimes", arch, "native", "Microsoft.Terminal.Control.dll"), Path.Combine(folder, "Microsoft.Terminal.Control.dll") })
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var lib)) return lib;
            return 0;
        });
    }
}
