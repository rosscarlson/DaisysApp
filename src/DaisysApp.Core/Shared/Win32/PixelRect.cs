using System.Text.Json.Serialization;

namespace DaisysApp.Shared.Win32;

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

    public readonly bool Contains(PixelRect o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }

    public readonly bool Equals(PixelRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override readonly bool Equals(object? obj) => obj is PixelRect other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(PixelRect a, PixelRect b) => a.Equals(b);
    public static bool operator !=(PixelRect a, PixelRect b) => !a.Equals(b);
    public override readonly string ToString() => $"{Width}×{Height} at {X},{Y}";
}

public readonly record struct PixelPoint(int X, int Y)
{
    public static implicit operator (int X, int Y)(PixelPoint p) => (p.X, p.Y);
}
