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

    /// <summary>A row that's a button: only its background (a module's hover colour) and content.</summary>
    public static readonly ControlTemplate FlatRow = MakeFlatRow();

    private static ControlTemplate MakeFlatRow()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
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

    // ---------------------------------------------------------------- timers

    /// <summary>
    /// A timer that ticks only while <paramref name="element"/> is shown (its tab picked, its window open), and stops
    /// when it's hidden. <paramref name="shown"/> runs each time it's shown or hidden, before the timer starts;
    /// <paramref name="now"/>: also ticks at once when it's shown. <paramref name="when"/>: ticks only while that's true
    /// too (call <see cref="Recheck"/> when it changes). The timer is returned for anything else (its Interval).
    /// (Since Core 0.55: before, about 25 views started and stopped their own.)
    /// </summary>
    public static System.Windows.Threading.DispatcherTimer Every(FrameworkElement element, TimeSpan interval, Action tick,
        bool now = false, Action<bool>? shown = null, Func<bool>? when = null)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        t.Tick += (_, _) => tick();
        conditions.Add(t, new Gate(element, when));
        element.IsVisibleChanged += (_, _) =>
        {
            shown?.Invoke(element.IsVisible);
            Recheck(t);
            if (element.IsVisible && now && t.IsEnabled) tick();
        };
        if (element.IsVisible) { Recheck(t); if (now && t.IsEnabled) tick(); }
        return t;
    }

    public static System.Windows.Threading.DispatcherTimer Every(FrameworkElement element, int ms, Action tick,
        bool now = false, Action<bool>? shown = null, Func<bool>? when = null) =>
        Every(element, TimeSpan.FromMilliseconds(ms), tick, now, shown, when);

    /// <summary>Starts or stops a timer from <see cref="Every"/> by its element being shown and its condition.</summary>
    public static void Recheck(System.Windows.Threading.DispatcherTimer t)
    {
        if (!conditions.TryGetValue(t, out var g)) return;
        bool run = g.Element.IsVisible && (g.When?.Invoke() ?? true);
        if (run && !t.IsEnabled) t.Start();
        else if (!run && t.IsEnabled) t.Stop();
    }

    private sealed record Gate(FrameworkElement Element, Func<bool>? When);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Threading.DispatcherTimer, Gate> conditions = new();

    // ---------------------------------------------------------------- colours

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A colour ("#F7A541", "#80F7A541", "Red") as a brush: frozen, and made once per colour (since Core 0.55),
    /// so it can be used from any thread and costs nothing to ask for again.</summary>
    public static SolidColorBrush Brush(string hex) => brushes.GetOrAdd(hex, h =>
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
        b.Freeze();
        return b;
    });

    /// <summary>A colour as a frozen brush.</summary>
    public static SolidColorBrush Brush(Color c) => Brush(c.ToString());

    /// <summary>A pen in the brush, made once per brush and width (frozen when the brush is): for drawing code that
    /// runs many times a second.</summary>
    public static Pen Pen(Brush brush, double width)
    {
        var key = (brush, width);
        lock (pens)
        {
            if (pens.TryGetValue(key, out var p)) return p;
            p = new Pen(brush, width);
            if (brush.IsFrozen) p.Freeze(); // (a theme's brush isn't frozen for it)
            if (pens.Count > 2000) pens.Clear();
            return pens[key] = p;
        }
    }

    public static Pen Pen(string hex, double width) => Pen(Brush(hex), width);

    private static readonly Dictionary<(Brush, double), Pen> pens = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, FontWeight, FontStyle), Typeface> typefaces = new();

    /// <summary>A typeface, made once per family, weight and style.</summary>
    public static Typeface Typeface(string family = "Segoe UI", FontWeight? weight = null, FontStyle? style = null) =>
        typefaces.GetOrAdd((family, weight ?? FontWeights.Normal, style ?? FontStyles.Normal),
            k => new Typeface(new FontFamily(k.Item1), k.Item3, k.Item2, FontStretches.Normal));

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

    // ---------------------------------------------------------------- programs (games)

    /// <summary>
    /// A list of programs a module watches for (Button2Key's and Button2Key Remote's games, ForzaWheel's): a line each
    /// with ✕ to take it off, a box to type one (or several, by commas) with Add, and Add a running program…, which
    /// lists the programs with a window. <paramref name="changed"/> is called after the list changed (to save it).
    /// <paramref name="removeTip"/>: the ✕'s tooltip, "{0}" the program; <paramref name="none"/>: shown when it's empty.
    /// (Since Core 0.55: four modules each had their own copy.)
    /// </summary>
    public static StackPanel ProgramList(List<string> list, Action changed, string? removeTip = null, string? none = null, string? addName = null)
    {
        var s = new StackPanel();
        var lines = new StackPanel();
        void Fill()
        {
            lines.Children.Clear();
            foreach (var program in list.ToList())
            {
                string name = program;
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var take = new Button { Content = "\uE711", Padding = new Thickness(6, 2, 6, 2), ToolTip = string.Format(removeTip ?? T("Take {0} off the list"), name) };
                take.SetResourceReference(Control.FontFamilyProperty, "IconFont");
                AutomationProperties.SetName(take, F("Remove {0}", name));
                take.Click += (_, _) =>
                {
                    list.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                    changed();
                    Fill();
                };
                DockPanel.SetDock(take, Dock.Right);
                line.Children.Add(take);
                line.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name });
                lines.Children.Add(line);
            }
            if (list.Count == 0 && none != null) lines.Children.Add(Secondary(none, wrap: true));
        }
        Fill();
        s.Children.Add(lines);

        var addLine = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var typed = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(typed, addName ?? T("Game to add"));
        var add = new Button { Content = T("Add"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        void AddTyped()
        {
            if (AddPrograms(list, typed.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))) { changed(); Fill(); }
            typed.Text = "";
        }
        add.Click += (_, _) => AddTyped();
        typed.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { AddTyped(); e.Handled = true; } };
        DockPanel.SetDock(add, Dock.Right);
        addLine.Children.Add(add);
        addLine.Children.Add(typed);
        s.Children.Add(addLine);
        var pick = new Button { Content = T("Add a running program…"), Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) => ProgramMenu(pick, list, n => { if (AddPrograms(list, [n])) { changed(); Fill(); } });
        s.Children.Add(pick);
        return s;
    }

    /// <summary>Adds programs to a list ("game" → "game.exe", a path → its file name), skipping ones it has; true if any were new.</summary>
    public static bool AddPrograms(List<string> list, IEnumerable<string> names)
    {
        bool added = false;
        foreach (var raw in names)
        {
            string n = Path.GetFileName(raw.Trim().Trim('"'));
            if (n.Length == 0) continue;
            if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
            if (list.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(n);
            added = true;
        }
        return added;
    }

    /// <summary>A menu under <paramref name="anchor"/> of the programs with a window (those already in
    /// <paramref name="list"/> greyed out); <paramref name="picked"/> gets the one clicked ("name.exe").</summary>
    public static void ProgramMenu(FrameworkElement anchor, IReadOnlyCollection<string> list, Action<string> picked)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var names = RunningPrograms.Windowed();
        foreach (var n in names)
        {
            var item = new MenuItem { Header = n, IsEnabled = !list.Contains(n, StringComparer.OrdinalIgnoreCase) };
            item.Click += (_, _) => picked(n);
            menu.Items.Add(item);
        }
        if (names.Count == 0) menu.Items.Add(new MenuItem { Header = T("No programs with windows are running"), IsEnabled = false });
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- files and the browser

    /// <summary>Asks for a folder (Windows' folder picker), starting at <paramref name="start"/>; null if cancelled.</summary>
    public static string? PickFolder(string title, string? start)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
        try { if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start)) d.InitialDirectory = start; } catch { }
        return d.ShowDialog(Application.Current?.MainWindow) == true ? d.FolderName : null;
    }

    /// <summary>Opens a file, folder or web page as Explorer would (with <paramref name="arguments"/>, a program);
    /// false (and logged) if it couldn't.</summary>
    public static bool Open(string path, string? arguments = null)
    {
        try
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            if (arguments != null) info.Arguments = arguments;
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Exception ex) { Log.App.Warn($"Couldn't open {path}: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Puts the text on the clipboard, trying again for a moment if another program has it open (Windows' clipboard
    /// is one at a time; a bare SetText throws then); false if it never got it. (Since Core 0.55.)
    /// </summary>
    public static bool Copy(string text)
    {
        for (int i = 0; i < 10; i++)
        {
            try { Clipboard.SetDataObject(text, true); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(30); }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(30); }
        }
        Log.App.Warn("Couldn't copy: another program has the clipboard");
        return false;
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
