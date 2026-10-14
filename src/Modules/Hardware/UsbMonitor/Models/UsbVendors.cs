namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

/// <summary>
/// Who a USB vendor ID (and a USB controller's PCI vendor ID) belongs to, for the common ones. A device's VID is usually
/// its brand; a hub's, or a cheap no-name device's, is the maker of the chip inside.
/// </summary>
public static class UsbVendors
{
    private static readonly Dictionary<string, string> Usb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["03EB"] = "Atmel", ["03F0"] = "HP", ["0403"] = "FTDI", ["0408"] = "Quanta", ["0409"] = "NEC",
        ["041E"] = "Creative", ["0424"] = "Microchip (SMSC)", ["043E"] = "LG", ["044F"] = "Thrustmaster",
        ["0451"] = "Texas Instruments", ["045B"] = "Renesas", ["045E"] = "Microsoft", ["0461"] = "Primax",
        ["046A"] = "Cherry", ["046D"] = "Logitech", ["0471"] = "Philips", ["047D"] = "Kensington", ["047F"] = "Poly (Plantronics)",
        ["0480"] = "Toshiba", ["0483"] = "STMicroelectronics", ["0489"] = "Foxconn", ["048D"] = "ITE",
        ["0499"] = "Yamaha", ["04A9"] = "Canon", ["04B4"] = "Cypress", ["04B8"] = "Epson", ["04CA"] = "Lite-On",
        ["04D8"] = "Microchip", ["04D9"] = "Holtek", ["04E8"] = "Samsung", ["04F2"] = "Chicony", ["04F3"] = "Elan",
        ["04F9"] = "Brother", ["050D"] = "Belkin", ["0525"] = "Netchip", ["054C"] = "Sony", ["056A"] = "Wacom",
        ["056E"] = "Elecom", ["057E"] = "Nintendo", ["058F"] = "Alcor Micro", ["05A9"] = "OmniVision", ["05AC"] = "Apple",
        ["05C6"] = "Qualcomm", ["05E3"] = "Genesys Logic", ["067B"] = "Prolific", ["06CB"] = "Synaptics",
        ["0738"] = "Mad Catz", ["0763"] = "M-Audio", ["0764"] = "CyberPower", ["0781"] = "SanDisk", ["07CA"] = "AVerMedia",
        ["08BB"] = "Texas Instruments", ["090C"] = "Silicon Motion", ["091E"] = "Garmin", ["0930"] = "Toshiba",
        ["093A"] = "PixArt", ["0951"] = "Kingston", ["0955"] = "NVIDIA", ["095D"] = "Poly (Polycom)",
        ["0A12"] = "Cambridge Silicon Radio", ["0A5C"] = "Broadcom", ["0ACE"] = "ZyDAS", ["0B05"] = "ASUS",
        ["0B0E"] = "Jabra (GN Netcom)", ["0B95"] = "ASIX", ["0BC2"] = "Seagate", ["0BDA"] = "Realtek",
        ["0C45"] = "Microdia (Sonix)", ["0C76"] = "JMTek", ["0CF3"] = "Qualcomm Atheros", ["0D28"] = "Arm (DAPLink)",
        ["0D8C"] = "C-Media", ["0DB0"] = "MSI", ["0E0F"] = "VMware", ["0E6F"] = "PDP", ["0E8D"] = "MediaTek",
        ["0F0D"] = "Hori", ["0FCE"] = "Sony Ericsson", ["0FD9"] = "Elgato", ["1004"] = "LG", ["1038"] = "SteelSeries",
        ["1050"] = "Yubico", ["1058"] = "Western Digital", ["10C4"] = "Silicon Labs", ["10F5"] = "Turtle Beach",
        ["1164"] = "YUAN", ["1199"] = "Sierra Wireless", ["1209"] = "open-source hardware (pid.codes)", ["12D1"] = "Huawei",
        ["1235"] = "Focusrite", ["1366"] = "SEGGER", ["1395"] = "EPOS (Sennheiser)", ["1397"] = "Behringer",
        ["13D3"] = "AzureWave (IMC Networks)", ["13FE"] = "Phison (Kingston)", ["1462"] = "MSI", ["148F"] = "Ralink",
        ["152D"] = "JMicron", ["1532"] = "Razer", ["154B"] = "PNY", ["1689"] = "Razer", ["16C0"] = "Van Ooijen (V-USB / Teensy)",
        ["174C"] = "ASMedia", ["17EF"] = "Lenovo", ["18D1"] = "Google", ["1915"] = "Nordic Semiconductor",
        ["1949"] = "Amazon", ["19F7"] = "RØDE", ["1A2C"] = "China Resource Semico", ["1A40"] = "Terminus Technology",
        ["1A86"] = "WCH (QinHeng)", ["1AB1"] = "Rigol", ["1B1C"] = "Corsair", ["1B3F"] = "Generalplus",
        ["1B4F"] = "SparkFun", ["1BCF"] = "Sunplus", ["1C4F"] = "SiGma Micro", ["1D50"] = "open-source hardware (OpenMoko)",
        ["1D5C"] = "Fresco Logic", ["1D6B"] = "Linux Foundation", ["1E71"] = "NZXT", ["1E7D"] = "ROCCAT",
        ["1EDB"] = "Blackmagic Design", ["1F75"] = "Innostor", ["1FC9"] = "NXP", ["2001"] = "D-Link",
        ["2040"] = "Hauppauge", ["2109"] = "VIA Labs", ["2341"] = "Arduino", ["2357"] = "TP-Link", ["239A"] = "Adafruit",
        ["24C6"] = "PowerA", ["2516"] = "Cooler Master", ["258A"] = "Sino Wealth", ["262A"] = "Savitech",
        ["26CE"] = "ASRock", ["2717"] = "Xiaomi", ["2833"] = "Meta (Oculus)", ["2886"] = "Seeed", ["28DE"] = "Valve",
        ["2972"] = "FiiO", ["2A03"] = "Arduino", ["2A70"] = "OnePlus", ["2C7C"] = "Quectel", ["2CA3"] = "DJI",
        ["2DC8"] = "8BitDo", ["2E8A"] = "Raspberry Pi", ["303A"] = "Espressif", ["3297"] = "ZSA", ["3434"] = "Keychron",
        ["413C"] = "Dell", ["7392"] = "Edimax", ["80EE"] = "VirtualBox",
    };

    private static readonly Dictionary<string, string> Pci = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8086"] = "Intel", ["1022"] = "AMD", ["1B21"] = "ASMedia", ["1912"] = "Renesas", ["1106"] = "VIA",
        ["1B73"] = "Fresco Logic", ["1033"] = "NEC", ["10DE"] = "NVIDIA", ["1002"] = "AMD", ["1B6F"] = "Etron",
        ["1043"] = "ASUS", ["1458"] = "Gigabyte", ["1462"] = "MSI", ["1849"] = "ASRock", ["1028"] = "Dell",
        ["103C"] = "HP", ["17AA"] = "Lenovo", ["106B"] = "Apple", ["1025"] = "Acer", ["144D"] = "Samsung",
        ["15D9"] = "Supermicro", ["1D72"] = "Xiaomi", ["152D"] = "Quanta", ["1179"] = "Toshiba", ["104D"] = "Sony",
        ["1414"] = "Microsoft",
    };

    /// <summary>The company a USB vendor ID belongs to, or null if it isn't one of the common ones.</summary>
    public static string? Name(string? vid) => vid != null && Usb.TryGetValue(vid, out var n) ? n : null;

    /// <summary>The company a PCI vendor ID (a USB controller, or the maker of the board it's on) belongs to.</summary>
    public static string? PciName(string? ven) => ven != null && Pci.TryGetValue(ven, out var n) ? n : null;
}
