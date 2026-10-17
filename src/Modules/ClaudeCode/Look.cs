using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>Something in the conversation that follows the look (Settings → Claude Code) when it changes.</summary>
internal interface ILooked
{
    void ApplyLook();
}

/// <summary>
/// The conversation's look as it is now (<see cref="Appearance"/>, from the settings): the colours (an empty one
/// follows the app's theme), fonts and sizes everything in a session tab is drawn with.
/// </summary>
internal static class Look
{
    public static Appearance A { get; private set; } = new();

    /// <summary>Raised when it changes (everything already shown follows it).</summary>
    public static event Action? Changed;

    public static void Set(Appearance a)
    {
        A = a;
        Changed?.Invoke();
    }

    /// <summary>A colour ("#RRGGBB"), or null for the theme's.</summary>
    public static Brush? Brush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    /// <summary>Sets a colour from the look, or the theme's resource when it's left empty.</summary>
    public static void Paint(FrameworkElement e, DependencyProperty property, string? hex, string themeKey)
    {
        if (Brush(hex) is { } b) e.SetValue(property, b);
        else e.SetResourceReference(property, themeKey);
    }

    public static FontFamily Font => new(string.IsNullOrWhiteSpace(A.Font) ? "Segoe UI" : A.Font + ", Segoe UI");
    public static FontFamily Mono => new(string.IsNullOrWhiteSpace(A.CodeFont) ? "Cascadia Mono, Consolas" : A.CodeFont + ", Cascadia Mono, Consolas");
    public static double Size => Math.Clamp(A.FontSize, 8, 40);
    public static double CodeSize => Math.Clamp(A.CodeFontSize, 8, 40);
    public static double LineHeight => Size * Math.Clamp(A.LineSpacing, 1, 3);
    public static double CodeLineHeight => CodeSize * Math.Clamp(A.LineSpacing * 0.95, 1, 3);
    public static double CardSize => Math.Clamp(A.CardFontSize, 8, 40);
    /// <summary>How much bigger the tool cards are than their usual 14.</summary>
    public static double CardScale => CardSize / 14.0;
    public static double MaxWidth => A.MaxTextWidth > 0 ? A.MaxTextWidth : double.PositiveInfinity;
}
