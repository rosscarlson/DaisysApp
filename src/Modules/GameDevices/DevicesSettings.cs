using DaisysApp.Settings;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.GameDevices;

/// <summary>A device's widget: where it is, and whether it's made.</summary>
public sealed class DeviceWidgetPlace : IWidgetPlace
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool Show { get; set; }
}

/// <summary>A device seen on this PC, plugged in or not: what the user has set for it.</summary>
public sealed class KnownDevice
{
    /// <summary>Its DirectInput instance. Windows makes a new one when a device moves to another USB port, so a new
    /// instance with the VID and PID of a device not plugged in is taken for that device (<see cref="DevicesHub"/>).</summary>
    public string Key { get; set; } = "";
    public int Vid { get; set; }
    public int Pid { get; set; }
    /// <summary>The name Windows gives it.</summary>
    public string WindowsName { get; set; } = "";
    /// <summary>The user's name for it (null: Windows').</summary>
    public string? Name { get; set; }
    /// <summary>The type picked (null: as detected).</summary>
    public DeviceKind? Kind { get; set; }
    /// <summary>Roles the user put on an axis: role id → axis (0–7), -1 for none.</summary>
    public Dictionary<string, int> Roles { get; set; } = new();
    /// <summary>Axes drawn the other way up.</summary>
    public HashSet<int> Inverted { get; set; } = new();
    /// <summary>The button (0 first) that's first gear, or a sequential shifter's up (null: the type's usual).</summary>
    public int? FirstGear { get; set; }
    public DeviceWidgetPlace? Widget { get; set; }
    /// <summary>Its Device Widget (its main control, and the other axes and buttons as they're used).</summary>
    public DeviceWidgetPlace? Activity { get; set; }
    /// <summary>Alert on Change: off, it has no alerts of its own (missing, new, moved) and isn't counted in the others'
    /// order; the others still alert if their numbers change.</summary>
    public bool Alert { get; set; } = true;
}

/// <summary>A device's place in the order the user has said is normal.</summary>
public sealed class NormalSlot
{
    public string Key { get; set; } = "";
    /// <summary>Its joystick number, from 1 (null: it has none).</summary>
    public int? Number { get; set; }
    /// <summary>Its place in DirectInput's list, from 1.</summary>
    public int Position { get; set; }
}

/// <summary>Something that differs from normal. An order change stays until Set as Normal, even if the order goes back;
/// a device missing or new goes by itself when that's over.</summary>
public sealed class DeviceAlert
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTime When { get; set; }
    public string Text { get; set; } = "";
    /// <summary>The order changed (red, and stays), not a device missing or new.</summary>
    public bool Order { get; set; }
}

/// <summary>Saved in %APPDATA%\DaisysApp\GameDevices.json.</summary>
public sealed class DevicesSettings
{
    private const string FileName = "GameDevices";

    /// <summary>The device showing (its key).</summary>
    public string? Selected { get; set; }
    public List<KnownDevice> Known { get; set; } = new();
    /// <summary>The order that's normal; made from what's plugged in the first time.</summary>
    public List<NormalSlot> Normal { get; set; } = new();
    public bool NormalSet { get; set; }
    public List<DeviceAlert> Alerts { get; set; } = new();
    /// <summary>The blocks left open.</summary>
    public HashSet<string> Expanded { get; set; } = new();

    public static DevicesSettings Load() => JsonStore.Load<DevicesSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
