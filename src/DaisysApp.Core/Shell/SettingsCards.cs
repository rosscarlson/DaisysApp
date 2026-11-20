using System.Windows;

namespace DaisysApp.Shell;

/// <summary>
/// (since SimEffects Analysis 0.3.0) Cards a module offers for a product's Settings page, by name
/// ("SimEffects/Analysis/Account"): the product's Settings key places them (<see cref="AppPaths.ProductInfo.Settings"/>).
/// A module offers them when it's made; the Settings page makes the ones it shows.
/// </summary>
public static class SettingsCards
{
    private static readonly Dictionary<string, Func<FrameworkElement>> offered = new(StringComparer.OrdinalIgnoreCase);

    public static void Offer(string name, Func<FrameworkElement> make) => offered[name] = make;

    /// <summary>The card offered by that name, made now; null if none is.</summary>
    public static FrameworkElement? Make(string name) => offered.TryGetValue(name, out var make) ? make() : null;
}
