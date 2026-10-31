using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using DaisysApp.Logging;
using DaisysApp.Theming;

namespace DaisysApp.Shared;

/// <summary>
/// The small pieces every module's code-built views are made of, in the app's styles (by their keys). A module keeps
/// only what's its own (its name says so: <c>SimUi</c>, <c>NetUi</c>…), built on these.
/// </summary>
public static class Ui
{
    // ---------------------------------------------------------------- text and cards

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    /// <summary>A card with its title over <paramref name="child"/>.</summary>
    public static Border Card(UIElement child, string title, Thickness? margin = null) =>
        Card(new StackPanel { Children = { Header(title), child } }, margin);

    public static TextBlock Header(string text, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        if (margin is { } m) t.Margin = m;
        return t;
    }

    public static TextBlock Secondary(string text = "", bool wrap = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        return t;
    }

    /// <summary>A wrapped line of secondary text, <paramref name="top"/> under what's above it.</summary>
    public static TextBlock Note(string text, double top = 0) => Note(text, new Thickness(0, top, 0, 0));

    public static TextBlock Note(string text, Thickness margin)
    {
        var t = Secondary(text, wrap: true);
        t.Margin = margin;
        return t;
    }

    /// <summary>A wrapped line of secondary text, centred in its row.</summary>
    public static TextBlock NoteCentred(string text)
    {
        var t = Secondary(text, wrap: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 14)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    /// <summary>Text that can be selected and copied (Ctrl+C or right-click), looking like a TextBlock: for errors.</summary>
    public static TextBox CopyText(string text = "", string? brush = "ErrorTextBrush", double maxHeight = 220)
    {
        var t = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        if (brush != null) t.SetResourceReference(Control.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A link (opens in the browser), its address in the tooltip.</summary>
    public static TextBlock Link(string text, string url)
    {
        var link = new Hyperlink(new Run(text)) { ToolTip = url };
        link.Click += (_, _) => Open(url);
        return new TextBlock(link) { Margin = new Thickness(0, 0, 14, 4) };
    }

    // ---------------------------------------------------------------- buttons and choices

    /// <summary>A button with an icon (and a caption, if there is one).</summary>
    public static Button IconButton(string glyph, string text, string? tip = null, Action? click = null, bool accent = false)
    {
        var b = MakeIconButton(glyph, text, tip, accent);
        if (click != null) b.Click += (_, _) => click();
        return b;
    }

    public static Button IconButton(string glyph, string text, string? tip, RoutedEventHandler click, bool accent = false)
    {
        var b = MakeIconButton(glyph, text, tip, accent);
        b.Click += click;
        return b;
    }

    private static Button MakeIconButton(string glyph, string text, string? tip, bool accent)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 13);
        icon.Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0);
        panel.Children.Add(icon);
        if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = panel, ToolTip = tip };
        AutomationProperties.SetName(b, text.Length > 0 ? text : tip ?? "");
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        return b;
    }

    /// <summary>The element, with the gap on its left that a row of buttons has.</summary>
    public static T Spaced<T>(T e) where T : FrameworkElement
    {
        e.Margin = new Thickness(8, 0, 0, 0);
        return e;
    }

    public static Button Button(string text, Action click, string? tip = null, bool accent = false)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 4, 10, 4) };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A rounded on/off chip (the app's ChipToggle style).</summary>
    public static ToggleButton Chip(string text, bool on, string? tip = null)
    {
        var t = new ToggleButton { Content = text, IsChecked = on, ToolTip = tip, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        t.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        return t;
    }

    /// <summary>
    /// Calls <paramref name="changed"/> when the box is ticked or unticked, however (a click, the keyboard, UI
    /// Automation, which raises no Click).
    /// </summary>
    public static void OnToggle(ToggleButton box, Action<bool> changed)
    {
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
    }

    /// <summary>A drop-down of (text, value) items, with <paramref name="current"/> picked (or the first).</summary>
    public static ComboBox Choice<TV>(IEnumerable<(string Text, TV Value)> items, TV current, Action<TV> picked, double width = 120)
    {
        var box = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, current)) ?? (box.Items.Count > 0 ? box.Items[0] : null);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: TV v }) picked(v); };
        return box;
    }

    /// <summary>A progress bar in the app's colours (the accent on the control colour, no border).</summary>
    public static ProgressBar Bar(double height)
    {
        var b = new ProgressBar { Height = height, BorderThickness = new Thickness(0) };
        b.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        b.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        return b;
    }

    // ---------------------------------------------------------------- colours

    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>A theme brush key ("AccentBrush") or a colour ("#F7A541") as a brush.</summary>
    public static Brush Brush(FrameworkElement owner, string color) =>
        color.StartsWith('#') ? Brush(color) : owner.TryFindResource(color) as Brush ?? Brushes.DodgerBlue;

    public static void SetBrush(FrameworkElement e, DependencyProperty p, string color)
    {
        if (color.StartsWith('#')) e.SetValue(p, Brush(color));
        else e.SetResourceReference(p, color);
    }

    // ---------------------------------------------------------------- layout

    /// <summary>The tab's page: everything in it scrolls, with room for the scroll bar.</summary>
    public static ScrollViewer Page(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new Border { Margin = new Thickness(0, 0, 6, 0), Child = content },
    };

    /// <summary>A card's header row: the title on the left, <paramref name="right"/> at the right.</summary>
    public static DockPanel HeaderRow(UIElement title, params UIElement?[] right)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        foreach (var r in (right ?? []).Reverse())
        {
            if (r == null) continue;
            DockPanel.SetDock(r, Dock.Right);
            if (r is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(r);
        }
        if (title is FrameworkElement t) { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0); }
        row.Children.Add(title);
        return row;
    }

    public static DockPanel HeaderRow(string title, params UIElement?[] right) => HeaderRow(Header(title), right);

    /// <summary>A read-only table in the app's look.</summary>
    public static DataGrid Table(double height = double.NaN)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            BorderThickness = new Thickness(0), EnableRowVirtualization = true, Height = height,
            SelectionMode = DataGridSelectionMode.Single, ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
        };
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        // a click on selectable text (which keeps the click to itself) still picks its row
        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(grid, d) is DataGridRow row && !row.IsSelected) grid.SelectedItem = row.Item;
        };
        return grid;
    }

    public static DataGridTextColumn Column(string header, string path, double width, string? sortBy = null, bool right = false)
    {
        var c = new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(-width, DataGridLengthUnitType.Star),
            SortMemberPath = sortBy ?? path,
        };
        if (right)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            c.ElementStyle = style;
        }
        return c;
    }

    // ---------------------------------------------------------------- files and the browser

    /// <summary>Asks for a folder (Windows' folder picker), starting at <paramref name="start"/>; null if cancelled.</summary>
    public static string? PickFolder(string title, string? start)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
        try { if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start)) d.InitialDirectory = start; } catch { }
        return d.ShowDialog(Application.Current?.MainWindow) == true ? d.FolderName : null;
    }

    /// <summary>Opens a file, folder or web page as Explorer would.</summary>
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.App.Warn($"Couldn't open {path}: {ex.Message}"); }
    }

    /// <summary>Opens Explorer with the file or folder selected.</summary>
    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { Log.App.Warn($"Couldn't show {path} in Explorer: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- windows

    /// <summary>A window in the app's theme, centred on <paramref name="owner"/>; closed with Esc.</summary>
    public static Window Dialog(string title, FrameworkElement? owner, double width, double height = double.NaN)
    {
        var w = Themed(title, owner != null ? Window.GetWindow(owner) : null, width);
        if (double.IsNaN(height)) w.SizeToContent = SizeToContent.Height;
        else w.Height = height;
        w.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) w.Close(); };
        return w;
    }

    private static Window Themed(string title, Window? owner, double width)
    {
        var w = new Window
        {
            Title = title, Width = width, ShowInTaskbar = false, Owner = owner,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(w);
        return w;
    }

    /// <summary>Asks for a line of text (e.g. a name); null if cancelled.</summary>
    public static string? Prompt(DependencyObject? near, string title, string text, string initial, string okText)
    {
        var owner = near != null ? Window.GetWindow(near) : Application.Current?.MainWindow;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 18) };
        var ok = new Button { Content = okText, MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var w = Themed(title, owner, 420);
        w.SizeToContent = SizeToContent.Height;
        w.ResizeMode = ResizeMode.NoResize;
        w.Content = new StackPanel
        {
            Margin = new Thickness(22, 18, 22, 18),
            Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, box, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } } },
        };
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
        var w = Themed(AppPaths.DisplayName, owner, 460);
        w.SizeToContent = SizeToContent.Height;
        w.ResizeMode = ResizeMode.NoResize;
        w.Content = new StackPanel { Margin = new Thickness(22, 18, 22, 18), Children = { body, buttons } };
        ok.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true;
    }
}
