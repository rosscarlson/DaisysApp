using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// A rectangle in physical-pixel virtual-desktop coordinates (not WPF's DIPs). Capture, selection and window placement
/// all use these, so DPI conversion only happens where WPF draws.
/// </summary>
public struct PixelRect : IEquatable<PixelRect>
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public PixelRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    [JsonIgnore] public readonly int Right => X + Width;
    [JsonIgnore] public readonly int Bottom => Y + Height;
    [JsonIgnore] public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromLTRB(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public readonly bool IntersectsWith(PixelRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public readonly PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromLTRB(left, top, right, bottom) : default;
    }

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }

    public readonly bool Equals(PixelRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override readonly bool Equals(object? obj) => obj is PixelRect other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public override readonly string ToString() => $"[{X},{Y} {Width}x{Height}]";
}

public readonly record struct PixelPoint(int X, int Y);

public enum MirrorShape { Rectangle, Circle }

/// <summary>One display's physical-pixel geometry and DPI at the time it was asked for.</summary>
public sealed class MonitorInfo
{
    public string DeviceName { get; init; } = "";
    public PixelRect Bounds { get; init; }
    public bool IsPrimary { get; init; }
    public double DpiScale { get; init; } = 1.0;
}

/// <summary>One mirror. <see cref="SourceRect"/> and <see cref="WindowBounds"/> are physical-pixel desktop coordinates.</summary>
public sealed class MirrorDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Mirror";
    public PixelRect SourceRect { get; set; }
    public MirrorShape Shape { get; set; } = MirrorShape.Rectangle;
    public PixelRect WindowBounds { get; set; }
    public int TargetFps { get; set; } = 30;
    public bool AspectLock { get; set; }
    public bool ClickThrough { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool Visible { get; set; } = true;

    /// <summary>Window size relative to the captured region's size (1.0 = same size).</summary>
    public double SizeScale { get; set; } = 1.0;

    /// <summary>
    /// Content magnification around the region's centre: above 1 crops in (magnify), below 1 takes in more of the
    /// surrounding desktop. The window's own size never changes because of it.
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>When true, the mirror window can't be dragged or resized.</summary>
    public bool PositionLocked { get; set; }

    /// <summary>Keyboard shortcut (e.g. "Ctrl+Alt+M") or controller button ("Controller 1 Button 5") that shows / hides it.</summary>
    public string? Shortcut { get; set; }
}

/// <summary>Everything Mini Mirror saves, in MiniMirror.json.</summary>
public sealed class MiniMirrorData
{
    public const string StoreName = "MiniMirror";

    public List<MirrorDefinition> Mirrors { get; set; } = new();

    /// <summary>Leave mirror windows out of screenshots, recordings and streams (and out of other mirrors).</summary>
    public bool HideFromCapture { get; set; }

    /// <summary>Starts a new mirror from anywhere, without clicking into Daisy's App (which can pause a game).</summary>
    public string? NewMirrorShortcut { get; set; } = DefaultNewMirrorShortcut;

    public const string DefaultNewMirrorShortcut = "Ctrl+Shift+F8";

    /// <summary>Convert HDR monitors' pictures to SDR, so mirrors of SDR content don't look washed out.</summary>
    public bool HdrConversion { get; set; } = true;

    public static MiniMirrorData Load() => JsonStore.Load<MiniMirrorData>(StoreName);

    public void Save() => JsonStore.Save(StoreName, this);

    /// <summary>Mirrors saved by the MiniMirror plugin for SimHub (SimHub writes them when it closes).</summary>
    public static class SimHubImport
    {
        public static string? SettingsFile
        {
            get
            {
                var folders = new[]
                {
                    Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SimHub"),
                };
                return folders.Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => Path.Combine(f!, "PluginsData", "Common", "MiniMirrorSettings.json"))
                    .FirstOrDefault(File.Exists);
            }
        }

        /// <summary>
        /// Reads the plugin's mirrors. Its hotkeys were SimHub input names, which mean nothing here, so they're left
        /// unset. Returns an empty list if there's nothing readable.
        /// </summary>
        public static List<MirrorDefinition> Read(string file)
        {
            var result = new List<MirrorDefinition>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("Mirrors", out var mirrors) || mirrors.ValueKind != JsonValueKind.Array) return result;
                foreach (var m in mirrors.EnumerateArray())
                {
                    var d = new MirrorDefinition
                    {
                        Id = m.TryGetProperty("Id", out var id) && id.TryGetGuid(out var g) ? g : Guid.NewGuid(),
                        Name = Str(m, "Name") ?? "Mirror",
                        SourceRect = Rect(m, "SourceRect"),
                        WindowBounds = Rect(m, "WindowBounds"),
                        Shape = Num(m, "Shape", 0) == 1 || Str(m, "Shape") == "Circle" ? MirrorShape.Circle : MirrorShape.Rectangle,
                        TargetFps = (int)Num(m, "TargetFps", 30),
                        AspectLock = Bool(m, "AspectLock"),
                        ClickThrough = Bool(m, "ClickThrough"),
                        Opacity = Num(m, "Opacity", 1),
                        Visible = !m.TryGetProperty("Visible", out var v) || v.ValueKind != JsonValueKind.False,
                        SizeScale = Num(m, "SizeScale", 1),
                        Zoom = Num(m, "Zoom", 1),
                        PositionLocked = Bool(m, "PositionLocked"),
                    };
                    if (!d.SourceRect.IsEmpty && !d.WindowBounds.IsEmpty) result.Add(d);
                }
            }
            catch { /* unreadable: nothing to import */ }
            return result;
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static double Num(JsonElement e, string name, double fallback) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : fallback;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

        private static PixelRect Rect(JsonElement e, string name) =>
            e.TryGetProperty(name, out var r) && r.ValueKind == JsonValueKind.Object
                ? new PixelRect((int)Num(r, "X", 0), (int)Num(r, "Y", 0), (int)Num(r, "Width", 0), (int)Num(r, "Height", 0))
                : default;
    }
}
