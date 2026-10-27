using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>Saved in %APPDATA%\DaisysApp\AmbiLight.json.</summary>
public sealed class AmbiSettings
{
    private const string FileName = "AmbiLight";

    public bool Enabled { get; set; }

    /// <summary>On, and the SimEffects profile is playing (<see cref="DaisysApp.Applets.SimEffects.Profiles.SimProfiles.Playing"/>): what runs.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool On => Enabled && DaisysApp.Applets.SimEffects.Profiles.SimProfiles.Playing;
    /// <summary>The on / off shortcut (Core's Hotkeys).</summary>
    public string? OnOffKey { get; set; }
    /// <summary>The games whose programs switch it on (<see cref="GameSwitcher{T}"/>; since 3.17.0).</summary>
    /// <summary>3.17's Switch On With Game: on in every game's profile at first.</summary>
    public bool SwitchOnWithGame { get; set; }

    /// <summary>What each SimEffects profile keeps of these (since 3.18.0).</summary>
    public static readonly string[] Scoped = { nameof(Enabled), nameof(Fps), nameof(Smoothing), nameof(Saturation), nameof(BlackLevel) };
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
    /// <summary>Displays left out (Windows' names, e.g. \\.\DISPLAY4): unticked on the tab.</summary>
    public List<string> DisplaysOff { get; set; } = new();

    public static AmbiSettings Load()
    {
        var s = JsonStore.Load<AmbiSettings>(FileName);
        return s;
    }

    public void Save()
    {
        JsonStore.Save(FileName, this);
        SimProfiles.Saved(this);
    }
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
    /// <summary>What it's called on the tab: the model's name (<see cref="GoveeModels"/>) unless renamed.</summary>
    public string Name { get; set; } = "Govee";
    public string Ip { get; set; } = "";
    /// <summary>Its MAC as Govee's scan gives it (to find it again when its address changes).</summary>
    public string? Mac { get; set; }
    /// <summary>Its model (e.g. H61A2).</summary>
    public string? Sku { get; set; }
    /// <summary>The light's own segments (Govee's groups of LEDs: what Show Segments counts, and what's sent with
    /// Stretch off).</summary>
    public int Segments { get; set; } = 10;
    /// <summary>Colours sent along the light with Stretch on (0: as many as its segments): virtual LEDs, each sampled
    /// from its own spot; the light spreads them over all its LEDs. A light drops pictures with more than it takes
    /// (the H6641 went dark at 84 with 10 segments set), so more is only for lights that take it.</summary>
    public int Leds { get; set; }
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

    /// <summary>Colours sent along the light: its LEDs with Stretch on (its segments until set), its segments with it
    /// off.</summary>
    public int LedCount => Math.Clamp(Stretch && Leds > 0 ? Leds : Segments, 1, 255);

    /// <summary>The segment (in the light's order) an LED of <see cref="LedCount"/> lies in.</summary>
    public int SegmentOfLed(int led) => Math.Min(Math.Clamp(Segments, 1, 255) - 1, (int)((long)led * Math.Clamp(Segments, 1, 255) / LedCount));

    /// <summary>Where an LED is along the zones' path (Reverse: the first LED at the other end).</summary>
    public int PathOf(int led) => Reverse ? LedCount - 1 - led : led;

    /// <summary>The zone an LED takes its colour from.</summary>
    public int ZoneOfLed(int led)
    {
        int at = PathOf(led), segments = Math.Clamp(Segments, 1, 255);
        return Math.Min(ZoneCount - 1, (int)((long)at * segments / LedCount) / Math.Max(1, PerZone));
    }

    public string Describe => string.IsNullOrEmpty(Sku) || Name.Contains(Sku) ? $"{Name} · {Ip}" : $"{Name} ({Sku}) · {Ip}";
}
