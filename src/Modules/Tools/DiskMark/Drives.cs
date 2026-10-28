using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>A drive that can be tested: its letter and size, and the disk under it (its model, how it's connected, SSD or HDD).</summary>
internal sealed record DriveItem(string Root, string Label, long Total, long Free, string Format, string Model, string Bus, bool? Ssd, DriveType Type)
{
    /// <summary>"C:".</summary>
    public string Letter => Root.TrimEnd('\\');
    public override string ToString() => string.IsNullOrEmpty(Label) ? Letter : $"{Letter} {Label}";

    public double Used => Total > 0 ? 100.0 * (Total - Free) / Total : 0;

    /// <summary>"NVMe SSD", "SATA HDD", "USB".</summary>
    public string Kind
    {
        get
        {
            if (Type == DriveType.Network) return T("Network");
            string media = Ssd switch { true => "SSD", false => "HDD", _ => "" };
            return string.Join(" ", new[] { Bus, media }.Where(s => s.Length > 0));
        }
    }
}

internal static unsafe class Drives
{
    public static List<DriveItem> List()
    {
        var list = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network or DriveType.Ram) || !d.IsReady) continue;
                var (model, bus, ssd) = d.DriveType == DriveType.Network ? ("", "", (bool?)null) : Describe(d.RootDirectory.FullName);
                list.Add(new DriveItem(d.RootDirectory.FullName, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace, d.DriveFormat, model, bus, ssd, d.DriveType));
            }
            catch { }
        }
        return list;
    }

    /// <summary>The disk's model, bus and whether it seeks (an HDD), asked of the volume itself (no administrator rights needed).</summary>
    private static (string Model, string Bus, bool? Ssd) Describe(string root)
    {
        using var h = CreateFileW(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return ("", "", null);
        string model = "", bus = "";
        bool? ssd = null;
        var buf = new byte[1024];
        fixed (byte* p = buf)
        {
            var query = stackalloc int[3]; // STORAGE_PROPERTY_QUERY: PropertyId, QueryType, AdditionalParameters
            query[0] = 0; query[1] = 0; query[2] = 0; // StorageDeviceProperty
            if (DeviceIoControl(h, QueryProperty, query, 12, p, buf.Length, out int got, IntPtr.Zero) && got >= 32)
            {
                string Text(int at)
                {
                    int off = BitConverter.ToInt32(buf, at);
                    if (off <= 0 || off >= got) return "";
                    int end = Array.IndexOf(buf, (byte)0, off);
                    return System.Text.Encoding.ASCII.GetString(buf, off, (end < 0 ? got : end) - off).Trim();
                }
                string vendor = Text(12), product = Text(16);
                model = vendor.Length > 0 && !product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ? $"{vendor} {product}" : product;
                bus = BitConverter.ToInt32(buf, 28) switch
                {
                    17 => "NVMe", 11 => "SATA", 3 => "ATA", 7 => "USB", 10 => "SAS", 1 => "SCSI", 8 => "RAID", 12 => "SD", 13 => "MMC",
                    14 or 15 => T("Virtual"), 16 => T("Storage Spaces"), 19 => "UFS", _ => "",
                };
            }
            query[0] = 7; // StorageDeviceSeekPenaltyProperty
            if (DeviceIoControl(h, QueryProperty, query, 12, p, buf.Length, out got, IntPtr.Zero) && got >= 9)
                ssd = buf[8] == 0;
        }
        return (model, bus, ssd);
    }

    private const uint QueryProperty = 0x2D1400; // IOCTL_STORAGE_QUERY_PROPERTY

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, IntPtr overlapped);
}
