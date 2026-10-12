using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// The Gaming code is one tab's worth (GamingView, GamingSettingsView) shown as two sub-tabs, Overlay and Recording.
/// Each XAML file holds both pages; this takes them out of their control so each can go in its own sub-tab. The
/// control stays the code behind them (its named elements, handlers and timers work as before); the pages get its
/// resources and its names, so styles and FindName (Settings jumping to a card) still work.
/// </summary>
internal static class Pages
{
    public static void Detach(UserControl owner, params FrameworkElement[] pages)
    {
        var scope = NameScope.GetNameScope(owner);
        if (owner.Content is Panel host) host.Children.Clear();
        owner.Content = null;
        foreach (var page in pages)
        {
            foreach (var key in owner.Resources.Keys) page.Resources[key] = owner.Resources[key];
            NameScope.SetNameScope(page, scope);
        }
    }
}
