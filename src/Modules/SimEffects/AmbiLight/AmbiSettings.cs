using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>Saved in %APPDATA%\DaisysApp\AmbiLight.json.</summary>
public sealed class AmbiSettings
{
    private const string FileName = "AmbiLight";

    public bool Enabled { get; set; }
    public List<AmbiLight> Lights { get; set; } = new();
    /// <summary>The light showing on the tab (its address).</summary>
    public string? Shown { get; set; }
    /// <summary>Pictures a second, 10–60.</summary>
    public int Fps { get; set; } = 30;
    /// <summary>0 (none) to 0.95: how much of the last colour each new one keeps.</summary>
    public double Smoothing { get; set; } = 0.5;
    /// <summary>1 as the screen; more is more colourful.</summary>
    public double Saturation { get; set; } = 1.3;
    /// <summary>Colours darker than this (0–60 of 255) go black, so a dark screen leaves the lights off.</summary>
    public int BlackLevel { get; set; } = 8;

    public static AmbiSettings Load() => JsonStore.Load<AmbiSettings>(FileName);

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>Where a light's zone takes its colour from: physical pixels on the virtual screen.</summary>
public sealed class AmbiZone
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public AmbiZone Clone() => new() { X = X, Y = Y, W = W, H = H };
}

/// <summary>How zones are laid out by <see cref="Layouts"/>.</summary>
public enum AmbiLayout { Top, Bottom, Sides, Around }

/// <summary>One Govee light: its address, segments, and the zones on the screens that colour them.</summary>
public sealed class AmbiLight
{
    public string Name { get; set; } = "Govee";
    public string Ip { get; set; } = "";
    /// <summary>Its MAC as Govee's scan gives it (to find it again when its address changes).</summary>
    public string? Mac { get; set; }
    /// <summary>Its model (e.g. H61A2).</summary>
    public string? Sku { get; set; }
    /// <summary>Segments sent in each picture (the light's own count, or fewer to stretch).</summary>
    public int Segments { get; set; } = 10;
    /// <summary>Segments per zone: 1 = a zone each; 10 of 100 = 10 zones of 10.</summary>
    public int PerZone { get; set; } = 1;
    /// <summary>The light spreads what's sent over all its LEDs (Govee's stretch flag).</summary>
    public bool Stretch { get; set; } = true;
    /// <summary>The first segment is at the other end.</summary>
    public bool Reverse { get; set; }
    /// <summary>The light's own brightness, 1–100.</summary>
    public int Brightness { get; set; } = 100;
    public AmbiLayout Layout { get; set; } = AmbiLayout.Top;
    public List<AmbiZone> Zones { get; set; } = new();

    public int ZoneCount => Math.Max(1, (Segments + Math.Max(1, PerZone) - 1) / Math.Max(1, PerZone));

    /// <summary>The zone a segment takes its colour from.</summary>
    public int ZoneOf(int segment)
    {
        int s = Reverse ? Segments - 1 - segment : segment;
        return Math.Min(ZoneCount - 1, s / Math.Max(1, PerZone));
    }

    public string Describe => string.IsNullOrEmpty(Sku) ? $"{Name} ({Ip})" : $"{Name} {Sku} ({Ip})";
}
