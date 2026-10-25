namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>
/// Govee's model numbers (what its scan answers with) and the products' names, so a light shows as "RGBIC Neon Rope
/// Light 5M" rather than "H61A2". From openHAB's Govee LAN binding's list of models; H6641 from a shop's listing.
/// A model not here shows as "Govee H…".
/// </summary>
internal static class GoveeModels
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["H6042"] = "TV Light Bar", ["H6043"] = "TV Light Bars", ["H6046"] = "RGBIC TV Light Bars",
        ["H6047"] = "RGBIC Gaming Light Bars", ["H6051"] = "Aura Table Lamp", ["H6052"] = "Table Lamp",
        ["H6056"] = "Flow Plus Light Bars", ["H6059"] = "Night Light", ["H6061"] = "Glide Hexa Light Panels",
        ["H6062"] = "Glide Wall Light", ["H6063"] = "Gaming Wall Light", ["H6065"] = "Glide Y Lights",
        ["H6066"] = "Glide Hexa Pro Light Panels", ["H6067"] = "Glide Triangle Light Panels",
        ["H606A"] = "Glide Hexa Ultra Light Panels", ["H6072"] = "Corner Floor Lamp", ["H6073"] = "Floor Lamp",
        ["H6076"] = "Corner Floor Lamp", ["H6078"] = "Cylinder Floor Lamp", ["H607C"] = "Floor Lamp 2",
        ["H6087"] = "Wall Sconces", ["H6088"] = "Cube Wall Sconces", ["H608A"] = "String Downlights 5M",
        ["H608B"] = "String Downlights 3M", ["H608C"] = "String Downlights 2M", ["H608D"] = "String Downlights 10M",
        ["H60A0"] = "Ceiling Light", ["H60A1"] = "Smart Ceiling Light", ["H610A"] = "Glide Lively Wall Lights",
        ["H610B"] = "Music Wall Lights", ["H6110"] = "Strip Light 2×5M", ["H6117"] = "DreamColor Strip Light 10M",
        ["H6141"] = "Strip Light 5M", ["H6143"] = "Strip Light 5M", ["H6144"] = "Strip Light 2×5M",
        ["H6159"] = "RGB Strip Light", ["H615A"] = "Strip Light 5M", ["H615B"] = "Strip Light 10M",
        ["H615C"] = "Strip Light 15M", ["H615D"] = "Strip Light 20M", ["H615E"] = "Strip Light 30M",
        ["H6163"] = "DreamColor Strip Light 5M", ["H6167"] = "TV Backlight 2.4M", ["H6168"] = "TV Backlight",
        ["H616C"] = "Outdoor Strip Light 10M", ["H616D"] = "Outdoor Strip Light 2×7.5M",
        ["H616E"] = "Outdoor Strip Light 2×10M", ["H6172"] = "Outdoor Strip Light 10M",
        ["H6173"] = "RGBIC Outdoor Strip Light", ["H6175"] = "RGBIC Outdoor Strip Light 10M",
        ["H6176"] = "RGBIC Outdoor Strip Light 30M", ["H6182"] = "TV Strip Light", ["H618A"] = "RGBIC Strip Light 5M",
        ["H618C"] = "RGBIC Strip Light 5M", ["H618E"] = "Strip Light 22M", ["H618F"] = "RGBIC Strip Light",
        ["H619A"] = "Coated Strip Light 5M", ["H619B"] = "Coated Strip Light 7.5M", ["H619C"] = "Coated Strip Light 10M",
        ["H619D"] = "Pro Strip Light 2×7.5M", ["H619E"] = "Coated Strip Light 2×10M", ["H619Z"] = "Pro Strip Light 3M",
        ["H61A0"] = "RGBIC Neon Rope Light 3M", ["H61A1"] = "RGBIC Neon Rope Light 2M",
        ["H61A2"] = "RGBIC Neon Rope Light 5M", ["H61A3"] = "RGBIC Neon Rope Light 4M", ["H61A5"] = "Neon Strip Light 10M",
        ["H61A8"] = "Neon Rope Light", ["H61B1"] = "Strip Light with Cover 5M", ["H61B2"] = "RGBIC Neon TV Backlight 3M",
        ["H61BA"] = "Strip Light 5M", ["H61BC"] = "Strip Light 10M", ["H61BE"] = "Strip Light 2×10M",
        ["H61C2"] = "Neon Strip Light", ["H61D3"] = "Neon Rope Light 2 3M", ["H61D5"] = "Neon Rope Light 2 5M",
        ["H61E0"] = "Strip Light M1", ["H61E1"] = "Strip Light M1", ["H6641"] = "Neon Rope Light 5M",
        ["H7012"] = "Outdoor String Lights", ["H7013"] = "Outdoor String Lights", ["H7021"] = "Outdoor String Lights",
        ["H7028"] = "Lynx Dream Bulb String", ["H7033"] = "Bulb String Lights", ["H7041"] = "Outdoor Bulb String Lights",
        ["H7042"] = "Outdoor Bulb String Lights", ["H7050"] = "Outdoor Ground Lights 11M",
        ["H7051"] = "Outdoor Ground Lights 15M", ["H7052"] = "Outdoor Ground Lights", ["H7055"] = "Pathway Light",
        ["H705A"] = "Permanent Outdoor Lights 30M", ["H705B"] = "Permanent Outdoor Lights 15M",
        ["H705C"] = "Permanent Outdoor Lights 45M", ["H705D"] = "Permanent Outdoor Lights 2 15M",
        ["H705E"] = "Permanent Outdoor Lights 2 30M", ["H705F"] = "Permanent Outdoor Lights 2 45M",
        ["H7060"] = "Flood Lights (2)", ["H7061"] = "Flood Lights (4)", ["H7062"] = "Flood Lights (6)",
        ["H7063"] = "Outdoor Flood Lights", ["H7065"] = "Outdoor Spot Lights", ["H7066"] = "Outdoor Spot Lights",
        ["H706A"] = "Permanent Outdoor Lights Pro 30M", ["H706B"] = "Permanent Outdoor Lights Pro 45M",
        ["H706C"] = "Permanent Outdoor Lights Pro 60M", ["H7070"] = "Outdoor Projector Light",
        ["H7075"] = "Outdoor Wall Light", ["H70B1"] = "Curtain Lights 520", ["H70BC"] = "Curtain Lights 400",
        ["H70C1"] = "RGBIC String Light 10M", ["H70C2"] = "RGBIC String Light 20M",
        ["H805A"] = "Permanent Outdoor Lights Elite 30M", ["H805B"] = "Permanent Outdoor Lights Elite 15M",
        ["H805C"] = "Permanent Outdoor Lights Elite 45M",
    };

    /// <summary>The product's name for a model, or "Govee H…" (or "Govee" without one).</summary>
    public static string Name(string? sku) =>
        string.IsNullOrEmpty(sku) ? "Govee" : Names.TryGetValue(sku, out var n) ? n : $"Govee {sku}";
}
