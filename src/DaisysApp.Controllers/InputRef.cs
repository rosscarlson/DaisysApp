using System.Text.Json.Serialization;

namespace DaisysApp.Controllers;

/// <summary>A real control: an axis, a button or a hat direction of one controller.</summary>
public sealed class InputRef
{
    public enum InputKind { Axis, Button, Pov }

    /// <summary>DirectInput's eight axes, as vJoy names them.</summary>
    public static readonly string[] AxisNames = { "X", "Y", "Z", "Rx", "Ry", "Rz", "Slider 1", "Slider 2" };

    /// <summary>The controller's DirectInput instance (stable on this PC), product, and name (to find it again if the instance changed).</summary>
    public Guid Device { get; set; }
    public Guid Product { get; set; }
    public string DeviceName { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InputKind Kind { get; set; }
    /// <summary>Axis 0–7, button 0–127, hat 0–3.</summary>
    public int Index { get; set; }
    /// <summary>For a hat: 0 up, 1 right, 2 down, 3 left. For an axis used as a button: 1 pushed up, -1 pushed down.</summary>
    public int Direction { get; set; }

    public string Describe()
    {
        string what = Kind switch
        {
            InputKind.Axis => Direction == 0 ? F("axis {0}", AxisNames[Index]) : F("axis {0} {1}", AxisNames[Index], Direction > 0 ? "+" : "−"),
            InputKind.Button => F("button {0}", Index + 1),
            _ => F("hat {0} {1}", Index + 1, new[] { T("up"), T("right"), T("down"), T("left") }[Math.Clamp(Direction, 0, 3)]),
        };
        return $"{DeviceName} · {what}";
    }

    public bool SameControl(InputRef o) => o.Device == Device && o.Kind == Kind && o.Index == Index && o.Direction == Direction;
}
