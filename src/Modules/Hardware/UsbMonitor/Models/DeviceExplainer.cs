using System.Text.RegularExpressions;
using DaisysApp.Applets.Hardware.UsbMonitor.Capture;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

/// <summary>
/// The details page's "What it probably is", in plain English: what kind of device it is and whose, where it's
/// plugged in (each hub between it and the PC, and which USB controller on the PC), what else is on the same hub so it
/// can be found among many, and for a problem why it fails and which device it most likely is.
/// Made on the UI thread (it copies what the known devices say), then <see cref="Explain"/> runs on any thread: it reads
/// the live USB device tree.
/// </summary>
public sealed class DeviceExplainer
{
    /// <summary>One paragraph: a heading and its text.</summary>
    public sealed record Section(string Heading, string Text);

    private sealed record Hop(DeviceInfo Child, DeviceInfo Hub, int? Port);

    private const string NoContainer = "{00000000-0000-0000-ffff-ffffffffffff}";
    private static readonly Regex PortRegex = new(@"Port_#(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex PathPortRegex = new(@"#USB\((\d+)\)$", RegexOptions.IgnoreCase);
    private static readonly Regex ClassRegex = new(@"Class_([0-9A-F]{2})(&SubClass_([0-9A-F]{2}))?(&Prot_([0-9A-F]{2}))?", RegexOptions.IgnoreCase);
    private static readonly Regex PciRegex = new(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(&SUBSYS_[0-9A-F]{4}([0-9A-F]{4}))?", RegexOptions.IgnoreCase);

    private readonly DeviceInfo info;
    private readonly KnownDevice? known;
    /// <summary>How many events it's had, and since when (from the known devices).</summary>
    private readonly (int Events, DateTime FirstSeen)? knownEvents;
    private readonly bool problem;
    /// <summary>The known devices a problem device most likely is, as text (made on the UI thread).</summary>
    private readonly List<string> likely = new();
    /// <summary>What the known devices last said, by instance ID, for a hub or parent that isn't plugged in now.</summary>
    private readonly Dictionary<string, DeviceInfo> remembered;

    private Dictionary<string, DeviceInfo> tree = new(StringComparer.OrdinalIgnoreCase);
    private ILookup<string, DeviceInfo> children = Array.Empty<DeviceInfo>().ToLookup(d => "");

    public DeviceExplainer(DeviceInfo info, bool problem)
    {
        var store = KnownDeviceStore.Instance;
        this.info = info.Clone();
        this.problem = problem;
        known = store.Devices.FirstOrDefault(d => Same(d.InstanceId, info.InstanceId));
        remembered = store.Devices.ToDictionary(d => d.InstanceId, d => d.LastGood ?? d.Info, StringComparer.OrdinalIgnoreCase);
        if (problem)
            foreach (var c in store.Analyse(info))
                likely.Add(F("{0}{1}: {2}; last working {3}.",
                    c.Device.Name,
                    c.Device.LastGood is { VendorId: { } v, ProductId: { } p }
                        ? (UsbVendors.Name(v) is { } brand && !c.Device.Name.Contains(brand.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ? $" ({brand}, {v}:{p})" : $" ({v}:{p})")
                        : "",
                    string.Join(", ", c.Reasons),
                    c.Device.LastWorking?.ToString("d MMM yyyy HH:mm") ?? T("unknown")));
        if (known != null) knownEvents = (known.Events, known.FirstSeen);
    }

    /// <summary>The explanation. Reads the USB device tree, so call it off the UI thread.</summary>
    public IReadOnlyList<Section> Explain()
    {
        try { tree = DeviceSnapshot.TakeUsbSnapshot(); }
        catch { /* explain from what's known */ }
        children = tree.Values.Where(d => d.StatusKnown && d.ParentInstanceId != null).ToLookup(d => d.ParentInstanceId!, StringComparer.OrdinalIgnoreCase);

        // this device as it is now, if it's plugged in (its parent is only readable while it is)
        var self = info;
        if (tree.TryGetValue(info.InstanceId, out var now) && now.StatusKnown)
        {
            self = now;
            self.ProductName ??= info.ProductName;
        }
        self.ParentInstanceId ??= info.ParentInstanceId ?? Remembered(info.InstanceId)?.ParentInstanceId;

        // up the tree: the USB device (the node on a hub; an interface or a Bluetooth device sits below one), each hub, the controller
        DeviceInfo? device = null, controller = null;
        var hops = new List<Hop>();
        var cur = self;
        for (int i = 0; i < 16 && cur.ParentInstanceId is { } parentId; i++)
        {
            var parent = Node(parentId);
            if (parent == null) break;
            if (IsHub(parent))
            {
                device ??= cur;
                hops.Add(new Hop(cur, parent, Port(cur)));
            }
            else if (device != null)
            {
                controller = parent;
                break;
            }
            cur = parent;
        }
        device ??= self;

        var sections = new List<Section> { new(T("What it is"), What(self, device)) };
        sections.Add(new(T("Where it's plugged in"), Where(device, hops, controller)));
        if (hops.Count > 0 && FindIt(device, hops) is { } find) sections.Add(new(T("To find it"), find));
        if (problem && Why(self) is { } why) sections.Add(new(T("Why it fails"), why));
        return sections;
    }

    // ---------------------------------------------------------------- what

    private string What(DeviceInfo self, DeviceInfo device)
    {
        var parts = new List<string>();
        bool unreadable = IsUnreadable(self);
        if (unreadable)
        {
            parts.Add(T("A USB device that couldn't tell Windows what it is, so Windows doesn't know its name or maker."));
            if (likely.Count > 0)
            {
                parts.Add(T("Most likely your ") + likely[0]);
                foreach (var other in likely.Skip(1)) parts.Add(T("Or your ") + other);
            }
            else
                parts.Add(T("Nothing has worked on that port while Daisy's App was watching, so it can't be named from history: use where it's plugged in, below."));
            return string.Join(" ", parts);
        }

        if (IsBluetooth(self))
            parts.Add(F("A Bluetooth (wireless) device: {0}. It isn't plugged in; it connects through the Bluetooth adapter below.", Name(self)));
        else
        {
            string name = Name(device);
            string? brand = Brand(device);
            string? kind = Kind(device);
            string text = kind != null ? F("{0}: {1}", Capital(kind), name) : name;
            if (brand != null && !name.Contains(brand.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                text += IsHub(device) ? F(", with a {0} chip", brand) : F(", made by {0}", brand);
            parts.Add(text + ".");
            if (!ReferenceEquals(self, device) && !Same(self.InstanceId, device.InstanceId))
                parts.Add(F("This entry ({0}) is one part of it.", self.DisplayName));
            if (brand != null && !IsHub(device) && IsChipMaker(device.VendorId))
                parts.Add(F("{0} makes the chips inside many no-name devices, so the brand on the box may be different.", brand));
        }
        if (problem && likely.Count > 0 && device.VendorId is null or "0000")
            parts.Add(T("Most likely your ") + likely[0]);
        return string.Join(" ", parts);
    }

    /// <summary>The kind of device, from its USB class codes and those of its parts (a composite device's interfaces).</summary>
    private string? Kind(DeviceInfo d)
    {
        if (IsHub(d)) return T("a USB hub");
        var kinds = new HashSet<string>();
        foreach (var n in Parts(d))
        {
            foreach (Match m in ClassRegex.Matches(n.CompatibleIds ?? ""))
            {
                string cls = m.Groups[1].Value.ToUpperInvariant(), sub = m.Groups[3].Value.ToUpperInvariant(), prot = m.Groups[5].Value.ToUpperInvariant();
                switch (cls)
                {
                    case "01": kinds.Add("audio"); break;
                    case "02": kinds.Add(sub == "02" ? "serial" : "network"); break;
                    case "03": kinds.Add(prot == "01" ? "keyboard" : prot == "02" ? "mouse" : "input"); break;
                    case "06": kinds.Add("camera"); break;
                    case "07": kinds.Add("printer"); break;
                    case "08": kinds.Add("storage"); break;
                    case "0B": kinds.Add("smartcard"); break;
                    case "0E": kinds.Add("video"); break;
                    case "10": kinds.Add("video"); kinds.Add("audio"); break;
                    case "E0": kinds.Add(sub == "01" && prot == "01" ? "bluetooth" : "wireless"); break;
                    case "FF" when sub is "5D" or "47": kinds.Add("xbox"); break;
                }
                break; // the first (most specific) class ID of each
            }
            switch (n.DeviceClass?.ToUpperInvariant())
            {
                case "KEYBOARD": kinds.Add("keyboard"); break;
                case "MOUSE": kinds.Add("mouse"); break;
                case "CAMERA": kinds.Add("video"); break;
                case "BLUETOOTH": kinds.Add("bluetooth"); break;
                case "WPD": kinds.Add("phone"); break;
                case "PORTS": kinds.Add("serial"); break;
                case "BIOMETRIC": kinds.Add("biometric"); break;
            }
            if (n.InstanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) && (n.DisplayName.Contains("UPS", StringComparison.OrdinalIgnoreCase) || n.DisplayName.Contains("Battery", StringComparison.OrdinalIgnoreCase)))
                kinds.Add("ups");
        }
        if (kinds.Contains("ups") || (Brand(d) is "CyberPower" or "APC")) return T("a UPS (battery backup) reporting over USB");
        if (kinds.Contains("bluetooth")) return T("a Bluetooth adapter");
        if (kinds.Contains("xbox")) return T("an Xbox-style game controller (or its wireless receiver)");
        if (kinds.Contains("video")) return kinds.Contains("audio") ? T("a webcam or video capture device (with sound)") : T("a webcam or video capture device");
        if (kinds.Contains("audio")) return kinds.Overlaps(new[] { "input", "keyboard" }) ? T("an audio device with buttons (headset, mic, speakers or a sound adapter)") : T("an audio device (sound adapter, mic, headset, speakers or DAC)");
        if (kinds.Contains("storage")) return T("a storage device (USB drive, disk or card reader)");
        if (kinds.Contains("phone")) return T("a phone, tablet or media player");
        if (kinds.Contains("network")) return T("a network adapter");
        if (kinds.Contains("wireless")) return T("a wireless adapter");
        if (kinds.Contains("printer")) return T("a printer");
        if (kinds.Contains("camera")) return T("a camera or scanner");
        if (kinds.Contains("smartcard")) return T("a smart card reader");
        if (kinds.Contains("biometric")) return T("a fingerprint reader");
        if (kinds.Contains("keyboard") && kinds.Contains("mouse")) return T("a keyboard and mouse (or a wireless receiver for them)");
        if (kinds.Contains("keyboard")) return T("a keyboard (or something that types, like a macro pad or receiver)");
        if (kinds.Contains("mouse")) return T("a mouse (or its wireless receiver)");
        if (kinds.Contains("serial")) return T("a serial (COM port) device, such as a microcontroller board or adapter");
        if (kinds.Contains("input")) return T("an input device (game controller, buttons, or a device's controls)");
        return null;
    }

    // ---------------------------------------------------------------- where

    private string Where(DeviceInfo device, List<Hop> hops, DeviceInfo? controller)
    {
        if (hops.Count == 0)
            return IsBluetooth(device) ? T("Through Bluetooth: Windows doesn't say which adapter.")
                : T("Windows doesn't say where it's connected (it isn't on a USB hub Windows can see now).");

        var lines = new List<string>();
        for (int i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            string port = hop.Port is { } p ? F("port {0}", p) : T("a port");
            string line = IsRootHub(hop.Hub)
                ? F("{0} on the PC itself: {1}", port, Controller(controller))
                : F("{0} of {1}", port, HubName(hop.Hub));
            lines.Add(i == 0 ? Capital(line) : "→ " + line);
        }
        int hubs = hops.Count(h => !IsRootHub(h.Hub));
        string summary = hubs == 0 ? T("Plugged straight into the PC (no hub).")
            : P(hubs, "{0} hub between it and the PC.", "{0} hubs between it and the PC.");
        var text = string.Join("\n", lines) + "\n" + summary;
        // a 7-port hub is usually two 4-port chips, one on the other's port 1: its outside numbering won't match
        var first = hops[0].Hub;
        if (!IsRootHub(first) && Twin(first) is { } twin)
            text += "\n" + F("That hub and the one joined to it (port {0}) are the same chip ({1}:{2}): most likely one bigger hub with two chips inside, so the port numbers won't match what's printed on it.",
                Port(Same(twin.ParentInstanceId, first.InstanceId) ? twin : first) is { } tp ? tp.ToString() : "?", first.VendorId, first.ProductId);
        return text;
    }

    /// <summary>A hub of the same make (VID and PID) directly above or below this one: two chips of one physical hub.</summary>
    private DeviceInfo? Twin(DeviceInfo hub)
    {
        if (hub.VendorId == null) return null;
        bool SameChip(DeviceInfo h) => IsHub(h) && Same(h.VendorId, hub.VendorId) && Same(h.ProductId, hub.ProductId);
        if (hub.ParentInstanceId is { } pid && Node(pid) is { } up && SameChip(up)) return up;
        return children[hub.InstanceId].FirstOrDefault(SameChip);
    }

    private string HubName(DeviceInfo hub)
    {
        // a hub that shares its container with another device is built into it (a monitor, dock or keyboard)
        if (hub.ContainerId is { } c && !Same(c, NoContainer)
            && tree.Values.FirstOrDefault(d => d.StatusKnown && !IsHub(d) && Same(d.ContainerId, c) && !d.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) is { } host)
            return F("the hub inside your {0}", Name(host));
        string name = Name(hub);
        string chip = $"{hub.VendorId}:{hub.ProductId}";
        return Brand(hub) is { } brand
            ? (IsGeneric(name) ? F("a USB hub ({0} chip, {1})", brand, chip) : F("a hub called “{0}” ({1} chip, {2})", name, brand, chip))
            : (IsGeneric(name) ? F("a USB hub ({0})", chip) : F("a hub called “{0}” ({1})", name, chip));
    }

    /// <summary>The USB controller on the PC in plain words: built into the motherboard, an extra chip or card, or a graphics card's USB-C.</summary>
    private static string Controller(DeviceInfo? c)
    {
        if (c == null) return T("on a USB controller Windows didn't name");
        var m = PciRegex.Match(c.InstanceId);
        string? ven = m.Success ? UsbVendors.PciName(m.Groups[1].Value) : null;
        string? board = m.Success && m.Groups[4].Success ? UsbVendors.PciName(m.Groups[4].Value) : null;
        string desc = c.DisplayName;
        if (desc.Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase) || desc.Contains("USB4", StringComparison.OrdinalIgnoreCase))
            return F("the Thunderbolt / USB4 controller ({0})", desc);
        return ven switch
        {
            "Intel" or "AMD" => board != null && board != ven
                ? F("the {0} USB controller built into the {1} motherboard ({2})", ven, board, desc)
                : F("the {0} USB controller built into the motherboard ({1})", ven, desc),
            "NVIDIA" => F("the USB-C port on the NVIDIA graphics card ({0})", desc),
            null => F("the USB controller {0}", desc),
            _ => board != null && board != ven
                ? F("a {0} USB controller chip on the {1} motherboard, or on a USB card ({2}); these usually drive a separate group of ports, often USB 3.1/3.2 or USB-C", ven, board, desc)
                : F("a {0} USB controller chip, an extra one on the motherboard or a USB card ({1}); these usually drive a separate group of ports", ven, desc),
        };
    }

    // ---------------------------------------------------------------- find it

    private string? FindIt(DeviceInfo device, List<Hop> hops)
    {
        var hub = hops[0].Hub;
        var parts = new List<string>();
        if (IsRootHub(hub))
        {
            var others = Others(hub, true, device.InstanceId);
            if (others.Count > 0) parts.Add(F("Also plugged straight into the PC on the same controller (the same group of ports): {0}.", string.Join("; ", others.Take(6))));
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        // the other chip of a two-chip hub has its ports on the same box: list what's on it too (its port numbers mean little)
        var twin = Twin(hub);
        var near = Others(hub, true, device.InstanceId, twin?.InstanceId ?? "");
        if (twin != null) near.AddRange(Others(twin, false, hub.InstanceId));
        if (near.Count > 0)
        {
            parts.Add(F("Also on that hub: {0}.", string.Join("; ", near.Take(8))));
            parts.Add(hops[0].Port is { } p
                ? F("Find the hub with those plugged in; this device is the one in its port {0} (go by what's next to it if the hub's numbering differs).", p)
                : T("Find the hub with those plugged in; this device is on it."));
        }
        else
            parts.Add(T("Nothing else is plugged into that hub right now."));

        // one level further up, to find the hub itself (past the hub's own second chip)
        int up = twin != null && hops.Count > 2 && Same(hops[1].Hub.InstanceId, twin.InstanceId) ? 2 : 1;
        if (hops.Count > up)
        {
            var upHub = hops[up].Hub;
            var upNear = Others(upHub, true, hops[up].Child.InstanceId).Take(4).ToList();
            if (upNear.Count > 0)
                parts.Add(IsRootHub(upHub)
                    ? F("That hub's cable goes into the PC, in the same group of ports as: {0}.", string.Join("; ", upNear))
                    : F("That hub's cable goes into another hub, which also has: {0}.", string.Join("; ", upNear)));
        }
        if (problem)
            parts.Add(T("To be sure, unplug what's on that hub one at a time: the one that makes this error go away is it."));
        return string.Join(" ", parts);
    }

    /// <summary>What else is plugged into a hub (its working devices, by port), leaving some out.</summary>
    private List<string> Others(DeviceInfo hub, bool ports, params string[] except) =>
        children[hub.InstanceId]
            .Where(d => !except.Any(e => Same(d.InstanceId, e)))
            .OrderBy(d => Port(d) ?? 99)
            .Select(d =>
            {
                string what = IsHub(d)
                    ? (Behind(d).Select(Name).Take(3).ToList() is { Count: > 0 } kids
                        ? F("a hub (with {0} on it)", string.Join(", ", kids)) : T("a hub"))
                    : Brand(d) is { } b && !Name(d).Contains(b.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ? $"{Name(d)} ({b})" : Name(d);
                return ports && Port(d) is { } p ? F("{0} on port {1}", what, p) : what;
            })
            .ToList();

    /// <summary>The devices plugged in behind a hub, through any hubs below it.</summary>
    private IEnumerable<DeviceInfo> Behind(DeviceInfo hub, int depth = 0)
    {
        if (depth > 6) yield break;
        foreach (var d in children[hub.InstanceId].OrderBy(d => Port(d) ?? 99))
            if (IsHub(d)) foreach (var inner in Behind(d, depth + 1)) yield return inner;
            else yield return d;
    }

    // ---------------------------------------------------------------- why

    private string? Why(DeviceInfo self)
    {
        var parts = new List<string>();
        string ids = (self.HardwareIds ?? "") + " " + self.DeviceDescription;
        bool Has(string s) => ids.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("CONFIGURATION_DESCRIPTOR") || Has("Invalid Configuration Descriptor"))
            parts.Add(T("It answered Windows, but the setup information it sent back was garbled, so Windows couldn't start it."));
        else if (Has("DEVICE_DESCRIPTOR_FAILURE") || Has("Device Descriptor Request Failed"))
            parts.Add(T("It gets power and Windows sees it, but it didn't answer when Windows asked what it is."));
        else if (Has("RESET_FAILURE") || Has("Port Reset Failed"))
            parts.Add(T("Windows saw something plugged in, but the port couldn't reset it to start talking to it."));
        else if (Has("SET_ADDRESS_FAILURE") || Has("Set Address Failed"))
            parts.Add(T("It didn't take the address Windows gave it, the very first step of talking to it."));
        else if (Has("Compliance Mode"))
            parts.Add(T("Its USB 3 connection couldn't be set up (often a poor USB 3 cable, extension or connector)."));
        else if (self.ProblemCode != 0)
            parts.Add(Capital(self.ProblemText ?? "") + ".");
        else if (!self.StatusKnown)
            parts.Add(T("It appeared, but Windows couldn't read its status (it may have dropped off again straight away)."));
        if (IsUnreadable(self) || self.ProblemCode == 43)
            parts.Add(T("Usual causes: a loose, long or damaged cable; a hub without enough power (try a powered hub, or plugging it straight into the PC); or a device that's failing or stuck (unplug it for 10 seconds)."));
        if (knownEvents is { Events: >= 4 } k)
            parts.Add(F("Daisy's App has logged it {0} times since {1:d MMM yyyy}: it keeps dropping and coming back, which points at the cable, the connector or power rather than a driver.", k.Events, k.FirstSeen));
        else if (knownEvents is { } first && IsUnreadable(self))
            parts.Add(F("This failure was first seen on this port on {0:d MMM yyyy}.", first.FirstSeen));
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    // ---------------------------------------------------------------- helpers

    private DeviceInfo? Remembered(string id) => remembered.TryGetValue(id, out var d) ? d : null;

    /// <summary>A device in the tree by instance ID: as it is now, else as last seen, else read from Windows.</summary>
    private DeviceInfo? Node(string id)
    {
        if (tree.TryGetValue(id, out var d))
        {
            if (d.ParentInstanceId == null && Remembered(id) is { } r) d.ParentInstanceId = r.ParentInstanceId;
            return d;
        }
        if (Remembered(id) is { } known) return known;
        try
        {
            if (DeviceSnapshot.TryGetByInstanceId(id) is { } read) { tree[id] = read; return read; }
        }
        catch { }
        return null;
    }

    /// <summary>The device and its parts (a composite device's interfaces and what Windows made of them), for working out what it is.</summary>
    private IEnumerable<DeviceInfo> Parts(DeviceInfo d)
    {
        yield return d;
        foreach (var c in children[d.InstanceId])
        {
            yield return c;
            foreach (var g in children[c.InstanceId]) yield return g;
        }
    }

    /// <summary>The best name: what the device calls itself, or Windows' name, or a telling part's name for a composite device.</summary>
    private string Name(DeviceInfo d)
    {
        static string? Telling(DeviceInfo n) =>
            !string.IsNullOrWhiteSpace(n.ProductName) && !IsGeneric(n.ProductName!) ? n.ProductName : !IsGeneric(n.DisplayName) ? n.DisplayName : null;
        var name = Telling(d) ?? (Remembered(d.InstanceId) is { } r ? Telling(r) : null);
        if (name != null) return name;
        // a composite device's parts often say what it is (a hub's children are other devices)
        if (!IsHub(d))
            foreach (var part in Parts(d).Skip(1))
                if (Telling(part) is { } partName) return partName;
        return d.ProductName ?? d.DisplayName;
    }

    private static readonly string[] GenericNames =
    {
        "USB Composite Device", "USB Input Device", "USB Mass Storage Device", "USB Device", "Generic USB Hub",
        "Generic SuperSpeed USB Hub", "USB Root Hub", "Unknown USB Device", "USB Hub", "HID-compliant device",
        "HID-compliant vendor-defined device", "USB PnP Sound Device", "USB Audio Device", "USB2.0 Hub", "USB 2.0 Hub",
        "USB3.0 Hub", "USB 3.0 Hub", "4-Port USB 2.0 Hub", "4-Port USB 3.0 Hub",
    };

    private static bool IsGeneric(string name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Unknown USB Device", StringComparison.OrdinalIgnoreCase)
        || GenericNames.Any(g => name.StartsWith(g, StringComparison.OrdinalIgnoreCase) && name.Length <= g.Length + 12);

    /// <summary>Whose it is: from its vendor ID, or the maker Windows' driver names when that's a real company.</summary>
    private string? Brand(DeviceInfo d)
    {
        var vid = d.VendorId ?? Remembered(d.InstanceId)?.VendorId;
        if (vid is null or "0000") return null;
        if (UsbVendors.Name(vid) is { } name) return name;
        var mfg = d.Manufacturer;
        return string.IsNullOrWhiteSpace(mfg) || mfg.StartsWith('(') || mfg.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            || mfg.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) || mfg.StartsWith("Compatible", StringComparison.OrdinalIgnoreCase)
            ? null : mfg;
    }

    /// <summary>Vendor IDs mostly seen on chips inside other brands' (or no-name) products.</summary>
    private static bool IsChipMaker(string? vid) => vid?.ToUpperInvariant() is "0C76" or "0D8C" or "1A40" or "05E3" or "2109" or "0BDA"
        or "1A86" or "067B" or "10C4" or "0403" or "04D9" or "1A2C" or "258A" or "1C4F" or "0C45" or "058F" or "1B3F" or "1BCF"
        or "262A" or "0A12" or "093A" or "1130" or "04B4" or "090C" or "152D" or "174C" or "1F75" or "13D3";

    private static bool IsHub(DeviceInfo d) =>
        IsRootHub(d) || (d.CompatibleIds?.Contains("USB\\Class_09", StringComparison.OrdinalIgnoreCase) ?? false)
        || d.Service is { } s && (s.Equals("usbhub", StringComparison.OrdinalIgnoreCase) || s.Equals("USBHUB3", StringComparison.OrdinalIgnoreCase));

    private static bool IsRootHub(DeviceInfo d) => d.InstanceId.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase);

    private static bool IsBluetooth(DeviceInfo d) => d.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)
        || (d.Enumerator?.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>It failed before saying what it is: Windows' "Unknown USB Device (…)" with a made-up VID 0000.</summary>
    private static bool IsUnreadable(DeviceInfo d) =>
        d.InstanceId.StartsWith("USB\\VID_0000&", StringComparison.OrdinalIgnoreCase)
        || (d.HardwareIds?.Contains("_FAILURE", StringComparison.OrdinalIgnoreCase) ?? false)
        || (d.DeviceDescription?.StartsWith("Unknown USB Device", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Which port of its hub it's in: from "Port_#0003.Hub_#0003", or the last #USB(n) of its location path.</summary>
    private static int? Port(DeviceInfo d)
    {
        if (d.LocationInformation is { } loc && PortRegex.Match(loc) is { Success: true } m) return int.Parse(m.Groups[1].Value);
        if (d.LocationPaths?.Split(" ; ")[0] is { } path && PathPortRegex.Match(path) is { Success: true } p) return int.Parse(p.Groups[1].Value);
        return null;
    }

    private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
