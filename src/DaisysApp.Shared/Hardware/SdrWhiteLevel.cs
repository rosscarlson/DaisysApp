using System.Runtime.InteropServices;

namespace DaisysApp.Shared.Hardware;

/// <summary>Reads the "SDR content brightness" Windows uses for SDR content on an HDR monitor.</summary>
public static class SdrWhiteLevel
{
    /// <summary>
    /// The SDR white level of the monitor named e.g. "\\.\DISPLAY1", as a multiple of 80 nits (the scRGB 1.0 level),
    /// or 2.5 (200 nits, a common default) if Windows won't say.
    /// </summary>
    public static float ScaleFor(string gdiDeviceName)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return 2.5f;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return 2.5f;

            for (int i = 0; i < pathCount; i++)
            {
                var name = new SourceDeviceName
                {
                    Header = new Header { Type = GET_SOURCE_NAME, Size = (uint)Marshal.SizeOf<SourceDeviceName>(), AdapterLow = paths[i].SourceAdapterLow, AdapterHigh = paths[i].SourceAdapterHigh, Id = paths[i].SourceId },
                };
                if (DisplayConfigGetDeviceInfo(ref name) != 0 || !string.Equals(name.GdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;

                var white = new WhiteLevel
                {
                    Header = new Header { Type = GET_SDR_WHITE_LEVEL, Size = (uint)Marshal.SizeOf<WhiteLevel>(), AdapterLow = paths[i].TargetAdapterLow, AdapterHigh = paths[i].TargetAdapterHigh, Id = paths[i].TargetId },
                };
                if (DisplayConfigGetDeviceInfo(ref white) == 0 && white.SdrWhiteLevel > 0) return white.SdrWhiteLevel / 1000f; // 1000 = 80 nits
            }
        }
        catch { /* older Windows: use the default */ }
        return 2.5f;
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1, GET_SDR_WHITE_LEVEL = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public uint SourceAdapterLow; public int SourceAdapterHigh; public uint SourceId, SourceModeIndex, SourceStatus;
        public uint TargetAdapterLow; public int TargetAdapterHigh; public uint TargetId, TargetModeIndex;
        public int OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator;
        public int ScanLineOrdering, TargetAvailable;
        public uint TargetStatus, Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo { public int InfoType; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public int Type; public uint Size; public uint AdapterLow; public int AdapterHigh; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WhiteLevel { public Header Header; public uint SdrWhiteLevel; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref WhiteLevel info);
}
