using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Tools.Shared;

/// <summary>The Windows calls the tools use: fast folder listing, copying with progress, the Recycle Bin, a drive's raw reads.</summary>
internal static unsafe class Native
{
    public const int MaxPath = 260;

    // (Pack = 4: the FILETIMEs are two 32-bit halves, not 8-byte aligned)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    public struct FindData
    {
        public uint Attributes;
        public long CreationTime, LastAccessTime, LastWriteTime; // FILETIMEs (UTC)
        public uint SizeHigh, SizeLow;
        public uint Reserved0, Reserved1;
        public fixed char FileName[MaxPath];
        public fixed char AlternateFileName[14];

        public readonly long Size => ((long)SizeHigh << 32) | SizeLow;
        public readonly bool IsDirectory => (Attributes & (uint)FileAttributes.Directory) != 0;
        public readonly bool IsReparsePoint => (Attributes & (uint)FileAttributes.ReparsePoint) != 0;

        /// <summary>A OneDrive (or other cloud) placeholder whose data isn't on this PC: it takes no space.</summary>
        public readonly bool IsOnlineOnly => (Attributes & (RecallOnDataAccess | RecallOnOpen | (uint)FileAttributes.Offline)) != 0;

        public ReadOnlySpan<char> Name
        {
            get
            {
                fixed (char* p = FileName)
                {
                    var span = new ReadOnlySpan<char>(p, MaxPath);
                    int end = span.IndexOf('\0');
                    return new ReadOnlySpan<char>(p, end < 0 ? MaxPath : end);
                }
            }
        }

        /// <summary>"." or "..".</summary>
        public readonly bool IsDots
        {
            get
            {
                fixed (char* p = FileName)
                    return p[0] == '.' && (p[1] == '\0' || (p[1] == '.' && p[2] == '\0'));
            }
        }
    }

    public const uint RecallOnDataAccess = 0x400000, RecallOnOpen = 0x40000;

    private const int FindExInfoBasic = 1, FindExSearchNameMatch = 0, FindFirstExLargeFetch = 2;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstFileExW")]
    private static extern IntPtr FindFirstFileEx(string fileName, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextFileW")]
    public static extern bool FindNextFile(IntPtr find, out FindData data);

    [DllImport("kernel32.dll")]
    public static extern bool FindClose(IntPtr find);

    public static readonly IntPtr Invalid = new(-1);

    /// <summary>Starts listing a folder (path as from <see cref="LongPath"/>); <see cref="Invalid"/> if it can't be read.</summary>
    public static IntPtr FindFirst(string folder, out FindData data) =>
        FindFirstFileEx(folder.EndsWith('\\') ? folder + "*" : folder + "\\*", FindExInfoBasic, out data, FindExSearchNameMatch, IntPtr.Zero, FindFirstExLargeFetch);

    /// <summary>A path Windows takes past 260 characters: \\?\C:\… or \\?\UNC\server\share\….</summary>
    public static string LongPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    public static DateTime FromFileTime(long ft) => ft <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(ft);

    // ---------------------------------------------------------------- copying

    public delegate uint CopyProgress(long total, long transferred, long streamSize, long streamTransferred,
        uint streamNumber, uint reason, IntPtr source, IntPtr destination, IntPtr data);

    public const uint ProgressContinue = 0, ProgressCancel = 1;
    public const uint CopyFailIfExists = 0x1, CopyAllowDecryptedDestination = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CopyFileExW")]
    public static extern bool CopyFileEx(string source, string destination, CopyProgress? progress, IntPtr data, IntPtr cancel, uint flags);

    public const uint MoveReplaceExisting = 0x1, MoveCopyAllowed = 0x2, MoveWriteThrough = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    public static extern bool MoveFileEx(string from, string to, uint flags);

    // ---------------------------------------------------------------- the Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr Window;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        public int AnyAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOp op);

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofNoConfirmMkdir = 0x200;

    /// <summary>Moves files and folders (full paths, not \\?\) to the Recycle Bin, quietly. Returns 0 when it worked.</summary>
    public static int Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return 0;
        var op = new ShFileOp
        {
            Func = FoDelete,
            From = string.Join("\0", paths) + "\0\0",
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofNoConfirmMkdir,
        };
        int r = SHFileOperation(ref op);
        return r != 0 ? r : op.AnyAborted != 0 ? -1 : 0;
    }

    // ---------------------------------------------------------------- a drive's raw contents (as administrator)

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, int inSize, void* outBuffer, int outSize, out int returned, IntPtr overlapped);

    private const uint GenericRead = 0x80000000, ShareReadWrite = 3, OpenExisting = 3;
    private const uint FsctlGetNtfsVolumeData = 0x00090064;

    [StructLayout(LayoutKind.Sequential)]
    public struct NtfsVolumeData
    {
        public long VolumeSerialNumber, NumberSectors, TotalClusters, FreeClusters, TotalReserved;
        public int BytesPerSector, BytesPerCluster, BytesPerFileRecordSegment, ClustersPerFileRecordSegment;
        public long MftValidDataLength, MftStartLcn, Mft2StartLcn, MftZoneStart, MftZoneEnd;
    }

    /// <summary>Opens a drive (e.g. "C:") for reading its sectors; needs administrator rights.</summary>
    public static SafeFileHandle OpenVolume(string drive)
    {
        var h = CreateFile(@"\\.\" + drive.TrimEnd('\\'), GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"Couldn't open {drive} (error {Marshal.GetLastWin32Error()})", Marshal.GetHRForLastWin32Error());
        return h;
    }

    public static NtfsVolumeData VolumeData(SafeFileHandle volume)
    {
        NtfsVolumeData d;
        if (!DeviceIoControl(volume, FsctlGetNtfsVolumeData, IntPtr.Zero, 0, &d, sizeof(NtfsVolumeData), out _, IntPtr.Zero))
            throw new IOException($"Not an NTFS drive (error {Marshal.GetLastWin32Error()})");
        return d;
    }
}
