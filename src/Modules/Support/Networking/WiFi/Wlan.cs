using System.Runtime.InteropServices;
using System.Text;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A Wi-Fi adapter.</summary>
internal sealed record WlanAdapter(Guid Id, string Name, bool Connected);

/// <summary>The network a Wi-Fi adapter is connected to.</summary>
internal sealed record WlanLink(string Ssid, string Bssid, string Phy, int Quality, double RxMbps, double TxMbps, string Auth, string Cipher, string Profile);

/// <summary>One access point (a BSS) a scan found.</summary>
internal sealed record WlanBss(string Ssid, string Bssid, int Rssi, int Quality, double FrequencyMHz, int Channel, string Band, int WidthMHz, double CenterChannel, string Phy, string Security)
{
    public string Name => Ssid.Length > 0 ? Ssid : "(" + T("hidden") + ")";
}

/// <summary>
/// Windows' Native Wi-Fi API (wlanapi.dll): the Wi-Fi adapters, what each is connected to, and the access points it
/// hears (a scan), with each one's channel width and security read from its beacon. Since Windows 11 24H2 the list of
/// access points needs "Let desktop apps access your location" (Settings → Privacy → Location); without it Windows
/// refuses (<see cref="AccessDenied"/>).
/// </summary>
internal static class Wlan
{
    public const int AccessDenied = 5;
    private static readonly object gate = new();
    private static IntPtr handle;
    private static int openError = -1;

    /// <summary>The handle, opened once; 0 when there's no Wi-Fi service (a PC without Wi-Fi).</summary>
    private static IntPtr Handle()
    {
        lock (gate)
        {
            if (handle != IntPtr.Zero) return handle;
            try { openError = WlanOpenHandle(2, IntPtr.Zero, out _, out handle); }
            catch (DllNotFoundException) { openError = 1062; }
            if (openError != 0) handle = IntPtr.Zero;
            return handle;
        }
    }

    /// <summary>The PC's Wi-Fi adapters (none on a PC without Wi-Fi).</summary>
    public static List<WlanAdapter> Adapters()
    {
        var list = new List<WlanAdapter>();
        var h = Handle();
        if (h == IntPtr.Zero || WlanEnumInterfaces(h, IntPtr.Zero, out var data) != 0) return list;
        try
        {
            int n = Marshal.ReadInt32(data);
            for (int i = 0; i < n; i++)
            {
                IntPtr item = data + 8 + i * 532;
                var id = Marshal.PtrToStructure<Guid>(item);
                string name = Marshal.PtrToStringUni(item + 16) ?? "";
                int state = Marshal.ReadInt32(item + 528);
                list.Add(new WlanAdapter(id, name, state == 1));
            }
        }
        finally { WlanFreeMemory(data); }
        return list;
    }

    /// <summary>What the adapter is connected to, or null.</summary>
    public static WlanLink? Current(Guid adapter)
    {
        var h = Handle();
        if (h == IntPtr.Zero || WlanQueryInterface(h, ref adapter, 7, IntPtr.Zero, out _, out var data, out _) != 0) return null;
        try
        {
            if (Marshal.ReadInt32(data) != 1) return null; // wlan_interface_state_connected
            string profile = Marshal.PtrToStringUni(data + 8) ?? "";
            string ssid = Ssid(data + 520);
            string bssid = Mac(data + 560);
            int phy = Marshal.ReadInt32(data + 568);
            int quality = Marshal.ReadInt32(data + 576);
            uint rx = (uint)Marshal.ReadInt32(data + 580), tx = (uint)Marshal.ReadInt32(data + 584);
            bool secured = Marshal.ReadInt32(data + 588) != 0;
            int auth = Marshal.ReadInt32(data + 596), cipher = Marshal.ReadInt32(data + 600);
            return new WlanLink(ssid, bssid, PhyName(phy), quality, rx / 1000.0, tx / 1000.0, secured ? AuthName(auth) : T("Open"), secured ? CipherName(cipher) : "", profile);
        }
        finally { WlanFreeMemory(data); }
    }

    /// <summary>Asks the adapter to scan (it takes a few seconds; the next <see cref="Networks"/> has the results).</summary>
    public static void Scan(Guid adapter)
    {
        var h = Handle();
        if (h != IntPtr.Zero) WlanScan(h, ref adapter, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>The access points the adapter heard in its last scan, and Windows' error (0, or e.g. <see cref="AccessDenied"/>).</summary>
    public static (List<WlanBss> List, int Error) Networks(Guid adapter)
    {
        var list = new List<WlanBss>();
        var h = Handle();
        if (h == IntPtr.Zero) return (list, openError);
        int err = WlanGetNetworkBssList(h, ref adapter, IntPtr.Zero, 3, false, IntPtr.Zero, out var data);
        if (err != 0) return (list, err);
        try
        {
            int n = Marshal.ReadInt32(data + 4);
            for (int i = 0; i < n; i++)
            {
                IntPtr e = data + 8 + i * 360;
                string ssid = Ssid(e);
                string bssid = Mac(e + 40);
                int phy = Marshal.ReadInt32(e + 52);
                int rssi = Marshal.ReadInt32(e + 56);
                int quality = Marshal.ReadInt32(e + 60);
                ushort capability = (ushort)Marshal.ReadInt16(e + 88);
                double mhz = (uint)Marshal.ReadInt32(e + 92) / 1000.0;
                int ieOffset = Marshal.ReadInt32(e + 352), ieSize = Marshal.ReadInt32(e + 356);
                var ies = new byte[Math.Max(ieSize, 0)];
                if (ieSize > 0) Marshal.Copy(e + ieOffset, ies, 0, ieSize);
                var (band, channel) = Channel(mhz);
                var (width, center, security) = ReadIes(ies, band, channel, capability);
                list.Add(new WlanBss(ssid, bssid, rssi, quality, mhz, channel, band, width, center, PhyName(phy), security));
            }
        }
        finally { WlanFreeMemory(data); }
        return (list, 0);
    }

    /// <summary>The band ("2.4", "5" or "6") and channel number of a centre frequency.</summary>
    public static (string Band, int Channel) Channel(double mhz) => mhz switch
    {
        2484 => ("2.4", 14),
        >= 2400 and < 2500 => ("2.4", (int)Math.Round((mhz - 2407) / 5)),
        >= 5925 and < 7200 => ("6", (int)Math.Round((mhz - 5950) / 5)),
        >= 4900 and < 5925 => ("5", (int)Math.Round((mhz - 5000) / 5)),
        _ => ("?", 0),
    };

    /// <summary>The channel width and centre (from the HT, VHT and HE operation elements) and the security (RSN / WPA).</summary>
    private static (int Width, double Center, string Security) ReadIes(byte[] ies, string band, int channel, ushort capability)
    {
        int width = 20;
        double center = channel;
        bool rsn = false, wpa = false, psk = false, sae = false, enterprise = false, owe = false;
        for (int i = 0; i + 2 <= ies.Length;)
        {
            int id = ies[i], len = ies[i + 1];
            if (i + 2 + len > ies.Length) break;
            var d = new ReadOnlySpan<byte>(ies, i + 2, len);
            switch (id)
            {
                case 61 when d.Length >= 2: // HT Operation: 40 MHz with the secondary channel above or below
                    int offset = d[1] & 3;
                    if ((d[1] & 4) != 0 && offset is 1 or 3 && width < 40) { width = 40; center = channel + (offset == 1 ? 2 : -2); }
                    break;
                case 192 when d.Length >= 3 && d[0] == 1: // VHT Operation: 80 or 160 MHz
                    if (d[2] != 0 && Math.Abs(d[2] - d[1]) == 8) { width = 160; center = d[2]; }
                    else if (d[2] != 0 && Math.Abs(d[2] - d[1]) > 16) { width = 160; center = d[1]; } // 80+80: drawn as its first 80 widened
                    else { width = Math.Max(width, 80); center = d[1]; }
                    break;
                case 255 when d.Length >= 7 && d[0] == 36: // HE Operation: the 6 GHz band's own width
                    uint parameters = (uint)(d[1] | d[2] << 8 | d[3] << 16);
                    int at = 1 + 3 + 1 + 2 + ((parameters & (1 << 14)) != 0 ? 3 : 0) + ((parameters & (1 << 15)) != 0 ? 1 : 0);
                    if ((parameters & (1 << 17)) != 0 && d.Length >= at + 4)
                    {
                        int w = d[at + 1] & 3;
                        width = w switch { 1 => 40, 2 => 80, 3 => 160, _ => 20 };
                        center = w == 3 && d[at + 3] != 0 ? d[at + 3] : w > 0 ? d[at + 2] : d[at];
                    }
                    break;
                case 48 when d.Length >= 8: // RSN: the key management suites
                    rsn = true;
                    int pairs = d[6] | d[7] << 8;
                    int akmAt = 8 + pairs * 4;
                    if (d.Length >= akmAt + 2)
                    {
                        int akms = d[akmAt] | d[akmAt + 1] << 8;
                        for (int k = 0; k < akms && d.Length >= akmAt + 2 + k * 4 + 4; k++)
                            switch (d[akmAt + 2 + k * 4 + 3])
                            {
                                case 2 or 6: psk = true; break;
                                case 8 or 9 or 24 or 25: sae = true; break;
                                case 1 or 5 or 11 or 12 or 13: enterprise = true; break;
                                case 18: owe = true; break;
                            }
                    }
                    break;
                case 221 when d.Length >= 4 && d[0] == 0x00 && d[1] == 0x50 && d[2] == 0xF2 && d[3] == 1:
                    wpa = true;
                    break;
            }
            i += 2 + len;
        }
        if (band == "2.4" && width > 40) width = 40;
        string security = enterprise ? (sae ? "WPA3-Enterprise" : "WPA2-Enterprise")
            : sae && psk ? "WPA2/WPA3" : sae ? "WPA3" : owe ? "OWE" : psk || rsn ? "WPA2" : wpa ? "WPA"
            : (capability & 0x10) != 0 ? "WEP" : T("Open");
        return (width, center, security);
    }

    private static string Ssid(IntPtr p)
    {
        int len = Math.Clamp(Marshal.ReadInt32(p), 0, 32);
        var b = new byte[len];
        Marshal.Copy(p + 4, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    private static string Mac(IntPtr p)
    {
        var b = new byte[6];
        Marshal.Copy(p, b, 0, 6);
        return string.Join(":", b.Select(x => x.ToString("X2")));
    }

    public static string PhyName(int phy) => phy switch
    {
        4 => "802.11a", 5 => "802.11b", 6 => "802.11g", 7 => "Wi-Fi 4 (802.11n)", 8 => "Wi-Fi 5 (802.11ac)",
        9 => "802.11ad", 10 => "Wi-Fi 6 (802.11ax)", 11 => "Wi-Fi 7 (802.11be)", _ => "",
    };

    private static string AuthName(int a) => a switch
    {
        1 => T("Open"), 2 => "Shared key", 3 => "WPA-Enterprise", 4 => "WPA", 6 => "WPA2-Enterprise", 7 => "WPA2",
        8 or 10 or 13 => "WPA3", 9 => "WPA3-Enterprise", 11 => "OWE", 12 => "WPA3-Enterprise", _ => a.ToString(),
    };

    private static string CipherName(int c) => c switch
    {
        0 => "", 1 => "WEP-40", 2 => "TKIP", 4 => "AES (CCMP)", 5 => "WEP-104", 8 => "AES (GCMP)", 9 => "AES (GCMP-256)", 10 => "AES (CCMP-256)", 0x101 => "WEP",
        _ => c.ToString(),
    };

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);
    [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, out int opcodeValueType);
    [DllImport("wlanapi.dll")] private static extern int WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, IntPtr ieData, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanGetNetworkBssList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, int bssType, [MarshalAs(UnmanagedType.Bool)] bool securityEnabled, IntPtr reserved, out IntPtr bssList);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
