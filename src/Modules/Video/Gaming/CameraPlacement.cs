namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Where one camera goes on the picture (Record → Add Camera): a box in fractions of the recorded picture (so the same
/// layout fits a monitor, a region or a game's window), its rounded corners, and whether it's mirrored.
/// </summary>
public sealed class CameraPlacement
{
    /// <summary>The most cameras a layout holds (each is a texture the converter's shader samples).</summary>
    public const int Max = 4;

    /// <summary>The camera's id (its symbolic link) and its name when it was added.</summary>
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The box: left, top, width and height as fractions of the picture (0–1).</summary>
    public double X { get; set; } = 0.74;
    public double Y { get; set; } = 0.68;
    public double W { get; set; } = 0.24;
    public double H { get; set; } = 0.28;

    /// <summary>Corner radius as a fraction of the box's shorter side: 0 square, 0.5 a pill or circle.</summary>
    public double Corner { get; set; } = 0.12;

    /// <summary>Flipped left to right, like a mirror (the way people are used to seeing themselves).</summary>
    public bool Mirror { get; set; }

    /// <summary>
    /// How much of the camera's picture is cut off at each edge, as fractions of it, as it's shown (so after mirroring:
    /// left is the left of what you see). Alt + drag an edge or corner in the layout window.
    /// </summary>
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }

    /// <summary>The least of the picture a crop may leave, each way.</summary>
    public const double MinVisible = 0.1;

    public bool IsCropped => CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0;

    /// <summary>The crop made sensible: each edge 0 or more, and at least <see cref="MinVisible"/> left each way.</summary>
    public void ClampCrop()
    {
        CropLeft = Math.Clamp(CropLeft, 0, 1 - MinVisible);
        CropRight = Math.Clamp(CropRight, 0, 1 - MinVisible - CropLeft);
        CropTop = Math.Clamp(CropTop, 0, 1 - MinVisible);
        CropBottom = Math.Clamp(CropBottom, 0, 1 - MinVisible - CropTop);
    }

    public CameraPlacement Clone() => (CameraPlacement)MemberwiseClone();
}
