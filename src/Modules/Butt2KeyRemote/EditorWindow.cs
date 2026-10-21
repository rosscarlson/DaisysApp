using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Theming;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The layout tool for one ButtScreen. Left: the ButtScreen (name, the device's size or shape, the grid, its colours).
/// Middle: the ButtScreen itself, on its grid: double-click an empty cell to add a button, drag a button to move it, drag
/// its corner handle to make it cover more cells, double-click it to choose what it does. Right: the selected button's
/// label, icon, look and action. Everything is saved as it changes (Ctrl+Z undoes).
/// </summary>
internal sealed class EditorWindow : Window
{
    private const double SideWidth = 300;

    private readonly ButtHub hub;
    private readonly ButtScreen screen;
    private readonly ScreenVisual visual;
    private readonly Canvas gridLayer = new() { IsHitTestVisible = false };
    private readonly Canvas handleLayer = new();
    private readonly Grid stage = new();
    private readonly Rectangle ghost = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Rectangle selection = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border handle = new() { Visibility = Visibility.Collapsed, Cursor = Cursors.SizeNWSE };
    private readonly StackPanel left = new(), right = new();
    private readonly TextBlock saved = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button undoButton, redoButton;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stack<string> undo = new(), redo = new();
    private string? lastChangeKey;
    private DateTime lastChangeAt;
    private ButtButton? selected;
    private ButtButton? clipboard;

    // dragging: a button to move (and which of its cells was grabbed), or its handle to resize it
    private enum Drag { None, Move, Resize }
    private Drag drag;
    private Point dragFrom;
    private bool dragStarted;
    private (int Column, int Row) grabbed;
    private (int Column, int Row, int ColumnSpan, int RowSpan)? target;

    public ButtScreen Screen => screen;

    public EditorWindow(ButtHub hub, ButtScreen screen)
    {
        this.hub = hub;
        this.screen = screen;
        Title = F("ButtScreen: {0}", screen.Name);
        Width = 1320;
        Height = 860;
        MinWidth = 1000;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        // the toolbar
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(ToolButton("", T("Add a button"), T("Adds a button in the first free cell (or double-click an empty cell)"), () => AddButton(null)));
        undoButton = ToolButton("", T("Undo"), T("Undo (Ctrl+Z)"), Undo);
        redoButton = ToolButton("", T("Redo"), T("Redo (Ctrl+Y)"), Redo);
        bar.Children.Add(undoButton);
        bar.Children.Add(redoButton);
        bar.Children.Add(ToolButton("", T("Pop out"), T("Opens this ButtScreen in a window of its own, to use with the mouse or a touchscreen"), () => { Flush(); hub.PopOut(screen); }));
        saved.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        bar.Children.Add(saved);
        // (everything is saved as it changes: this saves what's pending and closes)
        var close = new Button { Content = T("Save & Close"), Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        close.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(close, T("Save & Close"));
        close.Click += (_, _) => { Flush(); Close(); };
        var top = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(bar, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(bar);
        top.Children.Add(close);

        // the stage: the ButtScreen with its grid drawn over it, scaled to fit
        visual = new ScreenVisual(screen);
        ghost.StrokeThickness = 6;
        ghost.StrokeDashArray = new DoubleCollection { 2, 1 };
        selection.StrokeThickness = 6;
        selection.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        handle.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        handle.BorderBrush = Brushes.White;
        AutomationProperties.SetName(handle, T("Resize the button"));
        handle.ToolTip = T("Drag to make the button cover more cells, or fewer");
        handleLayer.Children.Add(selection);
        handleLayer.Children.Add(ghost);
        handleLayer.Children.Add(handle);
        stage.Children.Add(visual);
        stage.Children.Add(gridLayer);
        stage.Children.Add(handleLayer);
        stage.Background = Brushes.Transparent;
        var view = new Viewbox { Child = stage, Stretch = Stretch.Uniform, Margin = new Thickness(16) };
        var room = new Border { Child = view, CornerRadius = new CornerRadius(8), ClipToBounds = true };
        room.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        stage.MouseLeftButtonDown += StageDown;
        stage.MouseMove += StageMove;
        stage.MouseLeftButtonUp += StageUp;
        stage.MouseRightButtonUp += StageRightClick;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (selected == null) return;
            drag = Drag.Resize;
            dragFrom = e.GetPosition(visual);
            dragStarted = false;
            stage.CaptureMouse();
            e.Handled = true;
        };

        // the three columns
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideWidth + 40) });
        Grid.SetColumnSpan(top, 5);
        grid.Children.Add(top);
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(leftScroll, 1);
        grid.Children.Add(leftScroll);
        Grid.SetRow(room, 1);
        Grid.SetColumn(room, 2);
        grid.Children.Add(room);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(rightScroll, 1);
        Grid.SetColumn(rightScroll, 4);
        grid.Children.Add(rightScroll);
        Content = grid;

        saveTimer.Tick += (_, _) => Flush();
        Closed += (_, _) => Flush();
        BuildLeft();
        Redraw();
        ShowButton();
        ShowUndo();
    }

    // ---- changes, saving, undo ----

    /// <summary>
    /// Makes a change: what it was before goes on the undo stack (unless it's more of the same change, such as typing a
    /// label, within a couple of seconds), then it's drawn and saved shortly.
    /// </summary>
    private void Change(Action change, string? key = null, bool redrawAll = true)
    {
        if (key == null || key != lastChangeKey || DateTime.Now - lastChangeAt > TimeSpan.FromSeconds(2))
        {
            undo.Push(JsonSerializer.Serialize(screen, ScreenStore.Options));
            redo.Clear();
        }
        lastChangeKey = key;
        lastChangeAt = DateTime.Now;
        change();
        if (redrawAll) Redraw();
        else if (selected != null) { visual.Refresh(selected); ShowSelection(); }
        ShowUndo();
        saved.Text = "";
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>Saves now, if anything changed (and tells devices, pop-outs and the list).</summary>
    private void Flush()
    {
        if (!saveTimer.IsEnabled) return;
        saveTimer.Stop();
        hub.Changed(screen);
        saved.Text = "✓ " + T("Saved");
    }

    private void Undo() => Restore(undo, redo);
    private void Redo() => Restore(redo, undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        to.Push(JsonSerializer.Serialize(screen, ScreenStore.Options));
        var old = JsonSerializer.Deserialize<ButtScreen>(from.Pop(), ScreenStore.Options)!;
        string name = screen.Name;
        screen.Assign(old);
        if (name != screen.Name) hub.Renamed(screen, name);
        lastChangeKey = null;
        selected = selected == null ? null : screen.Buttons.FirstOrDefault(b => b.Id == selected.Id);
        BuildLeft();
        Redraw();
        ShowButton();
        ShowUndo();
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void ShowUndo()
    {
        undoButton.IsEnabled = undo.Count > 0;
        redoButton.IsEnabled = redo.Count > 0;
    }

    // ---- the stage ----

    private void Redraw()
    {
        visual.Render(screen);
        stage.Width = screen.Width;
        stage.Height = screen.Height;
        gridLayer.Width = handleLayer.Width = screen.Width;
        gridLayer.Height = handleLayer.Height = screen.Height;
        gridLayer.Children.Clear();
        double line = Math.Max(2, Math.Min(screen.Width, screen.Height) / 400.0);
        var stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        for (int c = 0; c < screen.Columns; c++)
            for (int r = 0; r < screen.Rows; r++)
            {
                if (screen.At(c, r) != null) continue;
                var rect = screen.RectOf(new ButtButton { Column = c, Row = r });
                var cell = new Rectangle
                {
                    Width = rect.Width, Height = rect.Height, Stroke = stroke, StrokeThickness = line, StrokeDashArray = new DoubleCollection { 4, 4 },
                    RadiusX = screen.CornerRadius * 0.5, RadiusY = screen.CornerRadius * 0.5,
                };
                Canvas.SetLeft(cell, rect.X);
                Canvas.SetTop(cell, rect.Y);
                gridLayer.Children.Add(cell);
            }
        // about 22 points across on the window, whatever the ButtScreen's size
        double size = Math.Max(Math.Max(screen.Width, screen.Height) / 32.0, Math.Min(screen.CellWidth, screen.CellHeight) * 0.22);
        handle.Width = handle.Height = size;
        handle.CornerRadius = new CornerRadius(size / 2);
        handle.BorderThickness = new Thickness(size / 10);
        ShowSelection();
    }

    private void ShowSelection()
    {
        if (selected == null || !screen.Buttons.Contains(selected))
        {
            selection.Visibility = handle.Visibility = Visibility.Collapsed;
            return;
        }
        var r = screen.RectOf(selected);
        double pad = selection.StrokeThickness * 1.5;
        selection.Width = r.Width + pad * 2;
        selection.Height = r.Height + pad * 2;
        selection.RadiusX = selection.RadiusY = selected.Style.Rounded ? screen.CornerRadius + pad : 0;
        Canvas.SetLeft(selection, r.X - pad);
        Canvas.SetTop(selection, r.Y - pad);
        Canvas.SetLeft(handle, r.Right - handle.Width / 2);
        Canvas.SetTop(handle, r.Bottom - handle.Height / 2);
        selection.Visibility = handle.Visibility = Visibility.Visible;
    }

    private void Select(ButtButton? b)
    {
        if (b == selected) return;
        selected = b;
        lastChangeKey = null;
        ShowSelection();
        ShowButton();
    }

    private ButtButton? ButtonAt(Point p) =>
        screen.Buttons.LastOrDefault(b => screen.RectOf(b).Contains(p));

    private void StageDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(stage);
        stage.Focusable = true;
        var p = e.GetPosition(visual);
        // near the selected button's corner handle: resizing (the handle is small on a big ButtScreen)
        if (selected != null && handle.Visibility == Visibility.Visible && e.ClickCount == 1)
        {
            var corner = screen.RectOf(selected).BottomRight;
            if ((p - corner).Length <= handle.Width)
            {
                drag = Drag.Resize;
                dragFrom = p;
                dragStarted = false;
                stage.CaptureMouse();
                e.Handled = true;
                return;
            }
        }
        var b = ButtonAt(p);
        if (e.ClickCount == 2)
        {
            if (b != null) EditAction(b);
            else if (visual.CellAt(p) is { } cell) AddButton(cell);
            e.Handled = true;
            return;
        }
        Select(b);
        if (b == null) return;
        drag = Drag.Move;
        dragFrom = p;
        dragStarted = false;
        var at = visual.CellAt(p, nearest: true)!.Value;
        grabbed = (at.Column - b.Column, at.Row - b.Row);
        stage.CaptureMouse();
        e.Handled = true;
    }

    private void StageMove(object sender, MouseEventArgs e)
    {
        if (drag == Drag.None || selected == null) return;
        var p = e.GetPosition(visual);
        if (!dragStarted && (p - dragFrom).Length < Math.Min(screen.CellWidth, screen.CellHeight) * 0.15) return;
        dragStarted = true;
        var cell = visual.CellAt(p, nearest: true)!.Value;
        var b = selected;
        target = drag == Drag.Move
            ? (Math.Clamp(cell.Column - grabbed.Column, 0, screen.Columns - b.ColumnSpan), Math.Clamp(cell.Row - grabbed.Row, 0, screen.Rows - b.RowSpan), b.ColumnSpan, b.RowSpan)
            : (b.Column, b.Row, Math.Max(1, cell.Column - b.Column + 1), Math.Max(1, cell.Row - b.Row + 1));
        var t = target.Value;
        var r = screen.RectOf(new ButtButton { Column = t.Column, Row = t.Row, ColumnSpan = t.ColumnSpan, RowSpan = t.RowSpan });
        bool fits = screen.Fits(t.Column, t.Row, t.ColumnSpan, t.RowSpan, b);
        ghost.Stroke = fits ? Brushes.LimeGreen : Brushes.OrangeRed;
        ghost.Fill = new SolidColorBrush(fits ? Color.FromArgb(40, 50, 205, 50) : Color.FromArgb(40, 255, 69, 0));
        ghost.Width = r.Width;
        ghost.Height = r.Height;
        ghost.RadiusX = ghost.RadiusY = b.Style.Rounded ? screen.CornerRadius : 0;
        Canvas.SetLeft(ghost, r.X);
        Canvas.SetTop(ghost, r.Y);
        ghost.Visibility = Visibility.Visible;
    }

    private void StageUp(object sender, MouseButtonEventArgs e)
    {
        if (drag == Drag.None) return;
        stage.ReleaseMouseCapture();
        ghost.Visibility = Visibility.Collapsed;
        var b = selected;
        if (dragStarted && b != null && target is { } t && screen.Fits(t.Column, t.Row, t.ColumnSpan, t.RowSpan, b)
            && (t.Column, t.Row, t.ColumnSpan, t.RowSpan) != (b.Column, b.Row, b.ColumnSpan, b.RowSpan))
        {
            Change(() => (b.Column, b.Row, b.ColumnSpan, b.RowSpan) = t);
            ShowButton();
        }
        drag = Drag.None;
        target = null;
    }

    private void StageRightClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(visual);
        var b = ButtonAt(p);
        var menu = new ContextMenu();
        if (b != null)
        {
            Select(b);
            menu.Items.Add(MenuItem(T("What it does…"), () => EditAction(b)));
            menu.Items.Add(MenuItem(T("Copy"), () => clipboard = b.Clone()));
            menu.Items.Add(MenuItem(T("Duplicate"), () => Paste(b)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(T("Delete"), () => Delete(b)));
        }
        else
        {
            var cell = visual.CellAt(p);
            menu.Items.Add(MenuItem(T("Add a button here"), () => AddButton(cell), cell != null));
            menu.Items.Add(MenuItem(T("Paste"), () => Paste(clipboard!, cell), clipboard != null));
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.FocusedElement is TextBox or ComboBox or KeyPicker) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.V && clipboard != null) { Paste(clipboard); e.Handled = true; return; }
        if (selected is not { } b) return;
        if (ctrl && e.Key == Key.C) { clipboard = b.Clone(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D) { Paste(b); e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.Delete: Delete(b); break;
            case Key.Enter: EditAction(b); break;
            case Key.Left: Nudge(b, -1, 0); break;
            case Key.Right: Nudge(b, 1, 0); break;
            case Key.Up: Nudge(b, 0, -1); break;
            case Key.Down: Nudge(b, 0, 1); break;
            case Key.Escape: Select(null); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Nudge(ButtButton b, int dx, int dy)
    {
        if (!screen.Fits(b.Column + dx, b.Row + dy, b.ColumnSpan, b.RowSpan, b)) return;
        Change(() => { b.Column += dx; b.Row += dy; });
    }

    // ---- buttons ----

    private (int Column, int Row)? FreeCell(int columnSpan = 1, int rowSpan = 1)
    {
        for (int r = 0; r < screen.Rows; r++)
            for (int c = 0; c < screen.Columns; c++)
                if (screen.Fits(c, r, columnSpan, rowSpan)) return (c, r);
        return null;
    }

    private void AddButton((int Column, int Row)? cell)
    {
        cell ??= FreeCell();
        if (cell is not { } at || !screen.Fits(at.Column, at.Row, 1, 1))
        {
            MessageBox.Show(this, T("There's no free cell: add rows or columns on the left, or make a button smaller."), T("ButtScreen"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var b = new ButtButton { Column = at.Column, Row = at.Row, Style = screen.ButtonStyle.Clone() };
        Change(() => screen.Buttons.Add(b));
        selected = null;
        Select(b);
    }

    /// <summary>A copy of a button in the cell given, or the first place it fits.</summary>
    private void Paste(ButtButton from, (int Column, int Row)? cell = null)
    {
        var b = from.Clone();
        b.Id = new ButtButton().Id;
        var at = cell is { } c && screen.Fits(c.Column, c.Row, b.ColumnSpan, b.RowSpan) ? c : FreeCell(b.ColumnSpan, b.RowSpan) ?? FreeCell();
        if (at == null) { MessageBox.Show(this, T("There's no free cell for it."), T("ButtScreen"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!screen.Fits(at.Value.Column, at.Value.Row, b.ColumnSpan, b.RowSpan)) b.ColumnSpan = b.RowSpan = 1;
        (b.Column, b.Row) = at.Value;
        Change(() => screen.Buttons.Add(b));
        Select(b);
    }

    private void Delete(ButtButton b)
    {
        Change(() => screen.Buttons.Remove(b));
        Select(null);
    }

    private void EditAction(ButtButton b)
    {
        var others = hub.Screens.Where(s => s.Id != screen.Id).Select(s => s.Name).ToList();
        var w = new BindingWindow(F("What {0} does", Describe(b)), b.Action, others,
            (T("Show a ButtScreen"), T("Show"), T("The device (or pop-out) the button was pressed on shows this ButtScreen instead.")))
        { Owner = this };
        if (w.ShowDialog() != true) return;
        var result = w.Result;
        Change(() => b.Action = result);
        ShowButton();
    }

    private static string Describe(ButtButton b) =>
        b.Label.Trim().Length > 0 ? b.Label.Trim() : b.Icon ?? F("the button at column {0}, row {1}", b.Column + 1, b.Row + 1);

    /// <summary>What a button does, in a few words.</summary>
    public static string Summary(ButtButton b) => b.Action is not { IsEmpty: false } a ? T("Nothing yet")
        : a.Kind == ActionKind.Profile ? F("Shows {0}", a.Profile) : a.Summary();

    // ---- the left column: the ButtScreen ----

    private sealed record SizeChoice(string Name, int Width, int Height);

    private static readonly SizeChoice[] Shapes =
    {
        // devices of our own (a device that connects is listed by itself too, with the size it says)
        new SizeChoice("Google Pixel 10 Pro", 1280, 2856),
        new SizeChoice("Phone 20:9", 1080, 2400), new SizeChoice("Phone 19.5:9", 1080, 2340), new SizeChoice("Phone 18:9 (2:1)", 1080, 2160), new SizeChoice("Phone 16:9", 1080, 1920),
        new SizeChoice("Tablet 4:3", 1536, 2048), new SizeChoice("Tablet 3:2", 1600, 2400), new SizeChoice("Tablet 16:10", 1600, 2560), new SizeChoice("Square 1:1", 1440, 1440),
    };

    private void BuildLeft()
    {
        left.Children.Clear();
        var s = new StackPanel();
        s.Children.Add(Header(T("ButtScreen")));

        var name = new TextBox { Text = screen.Name };
        AutomationProperties.SetName(name, T("Name"));
        name.LostKeyboardFocus += (_, _) => Rename(name);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(name); };
        s.Children.Add(Labeled(T("Name"), name));

        // its size: a device that has connected, a common shape, or pixels typed in
        var shape = new ComboBox();
        AutomationProperties.SetName(shape, T("Device or shape"));
        var choices = new List<(string Text, int W, int H)>();
        foreach (var d in hub.Settings.Devices.Where(d => d.Width > 0 && d.Height > 0).OrderByDescending(d => d.LastSeen))
            choices.Add((F("{0} ({1} × {2})", d.Name, Math.Min(d.Width, d.Height), Math.Max(d.Width, d.Height)), Math.Min(d.Width, d.Height), Math.Max(d.Width, d.Height)));
        foreach (var x in Shapes) choices.Add((T(x.Name), x.Width, x.Height));
        foreach (var c in choices) shape.Items.Add(c.Text);
        shape.Items.Add(T("Your own size (below)"));
        int small = Math.Min(screen.Width, screen.Height), big = Math.Max(screen.Width, screen.Height);
        int match = choices.FindIndex(c => c.W == small && c.H == big);
        shape.SelectedIndex = match >= 0 ? match : shape.Items.Count - 1;
        s.Children.Add(Labeled(T("Size"), shape));

        var turn = new ComboBox();
        turn.Items.Add(T("Portrait (tall)"));
        turn.Items.Add(T("Landscape (wide)"));
        turn.SelectedIndex = screen.Width > screen.Height ? 1 : 0;
        AutomationProperties.SetName(turn, T("Which way round"));
        s.Children.Add(Labeled(T("Held"), turn));

        var width = Number(screen.Width, 100, 10000, v => SetSize(v, screen.Height), T("Width in pixels"));
        var height = Number(screen.Height, 100, 10000, v => SetSize(screen.Width, v), T("Height in pixels"));
        var pixels = new StackPanel { Orientation = Orientation.Horizontal };
        pixels.Children.Add(width);
        pixels.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        pixels.Children.Add(height);
        s.Children.Add(Labeled(T("Pixels"), pixels));
        s.Children.Add(Secondary(T("A device that has connected is in the list with its own size. The buttons keep their shape on any device: it's drawn to fit.")));

        shape.SelectionChanged += (_, _) =>
        {
            if (shape.SelectedIndex < 0 || shape.SelectedIndex >= choices.Count) return;
            var c = choices[shape.SelectedIndex];
            bool wide = turn.SelectedIndex == 1;
            SetSize(wide ? c.H : c.W, wide ? c.W : c.H);
        };
        turn.SelectionChanged += (_, _) =>
        {
            bool wide = turn.SelectedIndex == 1;
            if (wide == screen.Width > screen.Height) return;
            SetSize(screen.Height, screen.Width);
        };
        left.Children.Add(Card(s));

        // the grid
        var g = new StackPanel();
        g.Children.Add(Header(T("Grid")));
        int minColumns = Math.Max(1, screen.Buttons.Select(b => b.Column + b.ColumnSpan).DefaultIfEmpty(1).Max());
        int minRows = Math.Max(1, screen.Buttons.Select(b => b.Row + b.RowSpan).DefaultIfEmpty(1).Max());
        g.Children.Add(Labeled(T("Columns"), Number(screen.Columns, minColumns, 24, v => Change(() => screen.Columns = v), T("Columns"), rebuild: true)));
        g.Children.Add(Labeled(T("Rows"), Number(screen.Rows, minRows, 24, v => Change(() => screen.Rows = v), T("Rows"), rebuild: true)));
        g.Children.Add(Labeled(T("Spacing"), Number((int)screen.Gap, 0, 400, v => Change(() => screen.Gap = v), T("Pixels between buttons and round the edge"), unit: T("pixels"))));
        g.Children.Add(Secondary(T("The grid sets the buttons' size: a button covers one cell or more. Rows and columns with buttons in them can't be taken away (move the buttons first).")));
        left.Children.Add(Card(g));

        // colours
        var k = new StackPanel();
        k.Children.Add(Header(T("Colours")));
        var back = new ColorBox(screen.Background, T("Background"));
        back.Changed += v => Change(() => screen.Background = v, "background");
        k.Children.Add(Labeled(T("Background"), back));
        var icons = new ColorBox(screen.IconColor, T("Icons"));
        icons.Changed += v => Change(() => screen.IconColor = v, "icons");
        k.Children.Add(Labeled(T("Icons"), icons));
        k.Children.Add(Secondary(T("Every icon from the library on this ButtScreen is drawn in the icon colour (your own pictures keep theirs).")));
        left.Children.Add(Card(k));
    }

    private void Rename(TextBox box)
    {
        string name = box.Text.Trim();
        if (name.Length == 0 || name == screen.Name) { box.Text = screen.Name; return; }
        name = ScreenStore.FreeName(name, hub.Screens, screen);
        string old = screen.Name;
        Change(() => screen.Name = name, redrawAll: false);
        box.Text = name;
        Title = F("ButtScreen: {0}", name);
        hub.Renamed(screen, old);
    }

    private void SetSize(int w, int h)
    {
        if (w == screen.Width && h == screen.Height) return;
        Change(() => { screen.Width = w; screen.Height = h; });
        BuildLeft();
    }

    // ---- the right column: the selected button ----

    private void ShowButton()
    {
        right.Children.Clear();
        if (selected is not { } b)
        {
            var none = new StackPanel();
            none.Children.Add(Header(T("Button")));
            none.Children.Add(Secondary(T("Select a button to change it, or double-click an empty cell to add one.")));
            var add = new Button { Content = T("Add a button"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => AddButton(null);
            none.Children.Add(add);
            right.Children.Add(Card(none));
            return;
        }
        string id = b.Id;

        // what it shows and does
        var s = new StackPanel();
        s.Children.Add(Header(T("Button")));
        var label = new TextBox { Text = b.Label, AcceptsReturn = true, MaxHeight = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(label, T("Label"));
        label.TextChanged += (_, _) => Change(() => b.Label = label.Text, "label" + id, redrawAll: false);
        s.Children.Add(Labeled(T("Label"), label));

        var iconLine = new DockPanel();
        var preview = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(6), Background = ColorText.Brush(b.Style.Background, Colors.DimGray), Margin = new Thickness(0, 0, 8, 0) };
        if (b.Image is { } image)
            preview.Child = new Image { Source = TryImage(image), Margin = new Thickness(6) };
        else if (IconLibrary.GeometryOf(b.Icon) is { } geometry)
            preview.Child = new Viewbox { Margin = new Thickness(8), Child = new Path { Data = geometry, Fill = ColorText.Brush(screen.IconColor, Colors.White), Width = 24, Height = 24, Stretch = Stretch.None } };
        DockPanel.SetDock(preview, Dock.Left);
        iconLine.Children.Add(preview);
        var iconButtons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        iconButtons.Children.Add(SmallButton(T("Icon…"), () =>
        {
            var w = new IconPickerWindow(b.Icon, screen.IconColor, b.Style.Background) { Owner = this };
            if (w.ShowDialog() != true) return;
            Change(() => { b.Icon = w.Result; b.Image = null; }, redrawAll: false);
            ShowButton();
        }, T("Pick an icon from the library")));
        iconButtons.Children.Add(SmallButton(T("Your own…"), () =>
        {
            var dlg = new OpenFileDialog { Filter = T("Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff|" + T("All files") + "|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            if (ScreenStore.AddImage(dlg.FileName) is not { } file)
            {
                MessageBox.Show(this, T("That picture couldn't be read."), T("ButtScreen"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Change(() => { b.Image = file; b.Icon = null; }, redrawAll: false);
            ShowButton();
        }, T("Use a picture of your own (PNG, JPEG…)")));
        if (b.Icon != null || b.Image != null)
            iconButtons.Children.Add(SmallButton(T("None"), () => { Change(() => { b.Icon = null; b.Image = null; }, redrawAll: false); ShowButton(); }, T("No icon or picture")));
        iconLine.Children.Add(iconButtons);
        s.Children.Add(Labeled(T("Icon"), iconLine));

        var does = new TextBlock { Text = Summary(b), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var doesLine = new DockPanel();
        var choose = new Button { Content = T("Choose…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        choose.ToolTip = T("Keys, a macro, the mouse, a program, or another ButtScreen: as Button2Key's inputs (Enter or double-click the button does this too)");
        choose.Click += (_, _) => EditAction(b);
        DockPanel.SetDock(choose, Dock.Right);
        doesLine.Children.Add(choose);
        doesLine.Children.Add(does);
        s.Children.Add(Labeled(T("Does"), doesLine));

        var span = new StackPanel { Orientation = Orientation.Horizontal };
        span.Children.Add(SpanBox(b, true));
        span.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        span.Children.Add(SpanBox(b, false));
        span.Children.Add(new TextBlock { Text = T("cells"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Wide × high, in cells of the grid") });
        s.Children.Add(Labeled(T("Covers"), span));
        right.Children.Add(Card(s));

        // how it looks
        var l = new StackPanel();
        l.Children.Add(Header(T("Look")));
        var st = b.Style;
        var corners = new ComboBox();
        corners.Items.Add(T("Rounded"));
        corners.Items.Add(T("Square"));
        corners.SelectedIndex = st.Rounded ? 0 : 1;
        AutomationProperties.SetName(corners, T("Corners"));
        corners.SelectionChanged += (_, _) => StyleChange(b, () => st.Rounded = corners.SelectedIndex == 0, "corners");
        l.Children.Add(Labeled(T("Corners"), corners));
        var bg = new ColorBox(st.Background, T("Background"));
        bg.Changed += v => StyleChange(b, () => st.Background = v, "bg");
        l.Children.Add(Labeled(T("Background"), bg));
        var outline = new ColorBox(st.Outline, T("Outline"));
        outline.Changed += v => StyleChange(b, () => st.Outline = v, "outline");
        var outlineLine = new StackPanel { Orientation = Orientation.Horizontal };
        outlineLine.Children.Add(outline);
        outlineLine.Children.Add(Number((int)st.OutlineWidth, 0, 60, v => StyleChange(b, () => st.OutlineWidth = v, "outlineWidth"), T("Outline width in pixels"), unit: T("px")));
        ((FrameworkElement)outlineLine.Children[1]).Margin = new Thickness(8, 0, 0, 0);
        l.Children.Add(Labeled(T("Outline"), outlineLine));

        var font = new ComboBox { IsEditable = false, MaxDropDownHeight = 400 };
        AutomationProperties.SetName(font, T("Font"));
        foreach (var f in FontNames()) font.Items.Add(new ComboBoxItem { Content = f, FontFamily = new FontFamily(f), Tag = f });
        font.SelectedItem = font.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == st.Font);
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is ComboBoxItem { Tag: string f }) StyleChange(b, () => st.Font = f, "font"); };
        l.Children.Add(Labeled(T("Font"), font));
        var fontLine = new StackPanel { Orientation = Orientation.Horizontal };
        fontLine.Children.Add(Number((int)st.FontSize, 6, 600, v => StyleChange(b, () => st.FontSize = v, "fontSize"), T("Font size in pixels"), unit: T("px")));
        var fontColor = new ColorBox(st.FontColor, T("Text colour"));
        fontColor.Changed += v => StyleChange(b, () => st.FontColor = v, "fontColor");
        var bold = new CheckBox { Content = T("Bold"), IsChecked = st.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        bold.Click += (_, _) => StyleChange(b, () => st.Bold = bold.IsChecked == true, "bold");
        fontLine.Children.Add(bold);
        l.Children.Add(Labeled(T("Text size"), fontLine));
        l.Children.Add(Labeled(T("Text colour"), fontColor));
        l.Children.Add(Secondary(T("Sizes are in the ButtScreen's pixels, so they grow and shrink with it. New buttons look like the last one changed.")));
        var all = new Button { Content = T("Give every button this look"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        all.Click += (_, _) => Change(() => { foreach (var x in screen.Buttons) x.Style = st.Clone(); screen.ButtonStyle = st.Clone(); });
        l.Children.Add(all);
        right.Children.Add(Card(l));

        var del = new Button { Content = T("Delete this button"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        del.SetResourceReference(StyleProperty, "DangerButton");
        del.Click += (_, _) => Delete(b);
        right.Children.Add(del);
    }

    private void StyleChange(ButtButton b, Action change, string key) =>
        Change(() => { change(); screen.ButtonStyle = b.Style.Clone(); }, key + b.Id, redrawAll: false);

    private FrameworkElement SpanBox(ButtButton b, bool wide)
    {
        var box = new ComboBox { MinWidth = 56 };
        AutomationProperties.SetName(box, wide ? T("Cells wide") : T("Cells high"));
        int max = wide ? screen.Columns : screen.Rows;
        for (int i = 1; i <= max; i++) box.Items.Add(i);
        box.SelectedItem = wide ? b.ColumnSpan : b.RowSpan;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not int n) return;
            int cs = wide ? n : b.ColumnSpan, rs = wide ? b.RowSpan : n;
            if (cs == b.ColumnSpan && rs == b.RowSpan) return;
            if (!screen.Fits(b.Column, b.Row, cs, rs, b))
            {
                MessageBox.Show(this, T("It doesn't fit there: another button (or the edge) is in the way. Move it, or drag its corner handle."), T("ButtScreen"), MessageBoxButton.OK, MessageBoxImage.Information);
                box.SelectedItem = wide ? b.ColumnSpan : b.RowSpan;
                return;
            }
            Change(() => { b.ColumnSpan = cs; b.RowSpan = rs; });
        };
        return box;
    }

    private static List<string>? fontNames;

    private static List<string> FontNames() => fontNames ??= Fonts.SystemFontFamilies
        .Select(f => f.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-us"), out var n) ? n : f.Source)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    private static ImageSource? TryImage(string file)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(ScreenStore.ImagePath(file));
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    // ---- small parts ----

    /// <summary>A whole number, used when Enter is pressed or the box is left (so the screen isn't redrawn at every key).</summary>
    private FrameworkElement Number(int value, int min, int max, Action<int> set, string name, string? unit = null, bool rebuild = false)
    {
        var box = new TextBox { Text = value.ToString(), Width = 64, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = F("{0} ({1}–{2})", name, min, max) };
        AutomationProperties.SetName(box, name);
        void Commit()
        {
            if (!int.TryParse(box.Text.Trim(), out int v)) { box.Text = value.ToString(); return; }
            v = Math.Clamp(v, min, max);
            box.Text = v.ToString();
            if (v == value) return;
            value = v;
            set(v);
            if (rebuild) Dispatcher.BeginInvoke(() => { BuildLeft(); ShowButton(); });
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        if (unit == null) return box;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        line.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return line;
    }

    private static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "CardHeader");
        return t;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement content)
    {
        var line = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var l = new TextBlock { Text = label, Width = 84, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        line.Children.Add(l);
        line.Children.Add(content);
        return line;
    }

    private static Button SmallButton(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button ToolButton(string glyph, string text, string tip, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = glyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text });
        var b = new Button { Content = content, Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    private static MenuItem MenuItem(string header, Action click, bool enabled = true)
    {
        var m = new MenuItem { Header = header, IsEnabled = enabled };
        m.Click += (_, _) => click();
        return m;
    }
}
