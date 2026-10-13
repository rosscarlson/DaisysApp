using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DaisysApp.Applets.Tools.Shared;

/// <summary>Small pieces the tools' code-built views share (the app's styles, by their keys).</summary>
internal static class Ui
{
    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    public static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }

    public static TextBlock Secondary(string text = "", bool wrap = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 14)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    /// <summary>A button with an icon and a caption.</summary>
    public static Button IconButton(string glyph, string text, string? tip = null, RoutedEventHandler? click = null, bool accent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 13);
        icon.Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0);
        panel.Children.Add(icon);
        if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = panel, ToolTip = tip };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        if (click != null) b.Click += click;
        return b;
    }

    /// <summary>A rounded on/off chip (the app's ChipToggle style).</summary>
    public static ToggleButton Chip(string text, bool on, string? tip = null)
    {
        var t = new ToggleButton { Content = text, IsChecked = on, ToolTip = tip, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        t.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        return t;
    }

    /// <summary>A progress bar in the app's colours (the accent on the control colour, no border).</summary>
    public static ProgressBar Bar(double height)
    {
        var b = new ProgressBar { Height = height, BorderThickness = new Thickness(0) };
        b.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        b.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        return b;
    }

    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>Asks for a folder (Windows' folder picker), starting at <paramref name="start"/>; null if cancelled.</summary>
    public static string? PickFolder(string title, string? start)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
        try { if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start)) d.InitialDirectory = start; } catch { }
        return d.ShowDialog(Application.Current?.MainWindow) == true ? d.FolderName : null;
    }

    /// <summary>Opens a file or folder as Explorer would.</summary>
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't open {path}: {ex.Message}"); }
    }

    /// <summary>Opens Explorer with the file or folder selected.</summary>
    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't show {path} in Explorer: {ex.Message}"); }
    }

    /// <summary>Asks for a line of text (e.g. a name); null if cancelled.</summary>
    public static string? Prompt(DependencyObject? near, string title, string text, string initial, string okText)
    {
        var owner = near != null ? Window.GetWindow(near) : Application.Current?.MainWindow;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 18) };
        var ok = new Button { Content = okText, MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var w = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(22, 18, 22, 18),
                Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, box, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } } },
            },
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => Theming.ThemeManager.ApplyTitleBar(w);
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        ok.Click += (_, _) => { if (box.Text.Trim().Length > 0) w.DialogResult = true; };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }

    /// <summary>A simple themed message window, owned by the window of <paramref name="near"/>.</summary>
    public static bool Ask(DependencyObject? near, string text, string yes, string no, bool danger = false)
    {
        var owner = near != null ? Window.GetWindow(near) : Application.Current?.MainWindow;
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 18) };
        var ok = new Button { Content = yes, MinWidth = 100, IsDefault = !danger };
        ok.SetResourceReference(FrameworkElement.StyleProperty, danger ? "DangerButton" : "AccentButton");
        var cancel = new Button { Content = no, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var w = new Window
        {
            Title = AppPaths.DisplayName, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, ShowInTaskbar = false,
            Content = new StackPanel { Margin = new Thickness(22, 18, 22, 18), Children = { body, buttons } },
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => Theming.ThemeManager.ApplyTitleBar(w);
        ok.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true;
    }
}
