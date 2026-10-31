using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// The Map card (since 3.31.0): the game's tracks as dots where their races start, over a map image the user picks
/// (copied to %APPDATA%\DaisysApp\AnalysisMaps), lined up by placing two or more tracks on it (<see cref="GameMap"/>'s
/// anchors: each axis fitted on its own, the game's maps being north up); without one, on a plain ground fitted to the
/// tracks. Wheel zooms, a drag pans. Beside it: the game, a search, filters by type and form, raced only, names, the
/// list; the track picked shows its best laps, races and laps under it. The car is a dot while the game sends.
/// </summary>
internal sealed class MapView : Grid
{
    /// <summary>The kinds of race, by their keys in <see cref="Track.Type"/>.</summary>
    public static readonly string[] Kinds = { "Road", "Dirt", "CrossCountry", "Street", "Special", "Other" };

    private static readonly Dictionary<string, Brush> KindBrush = new()
    {
        ["Road"] = Frozen("#4F8EF7"), ["Dirt"] = Frozen("#D08A3C"), ["CrossCountry"] = Frozen("#4CAF50"),
        ["Street"] = Frozen("#B061E0"), ["Special"] = Frozen("#E5484D"), ["Other"] = Frozen("#8A8F98"),
    };
    private static readonly Brush Edge = Frozen("#E0101010"), LabelBack = Frozen("#C0101010"), White = Frozen("#FFFFFFFF"), Ground = Frozen("#FF1B1F24");

    private readonly AnalysisApplet applet;
    private readonly AnalysisSettings settings;
    private readonly Canvas marks = new() { ClipToBounds = true, Background = Ground, Cursor = Cursors.Hand };
    private readonly Image picture = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Ellipse carDot = new() { Width = 14, Height = 14, Fill = White, StrokeThickness = 3, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly List<Mark> dots = new();
    private readonly ComboBox gameBox = Box(T("Game")), typeBox = Box(T("Type")), formBox = Box(T("Form")), kindBox = Box(T("Track type"));
    private readonly TextBox search = new() { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Search") };
    private readonly CheckBox racedOnly = new() { Content = T("Raced only"), Margin = new Thickness(0, 8, 16, 0) };
    private readonly CheckBox names = new() { Content = T("Names"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly ListBox list = new() { Height = 230, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock lineup = Ui.Note(""), count = Ui.Note("", 6);
    private readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock info = Ui.Note("", 2);
    private readonly FrameworkElement kindRow, details;
    private readonly DataGrid bests = Ui.Table(240), races = Ui.Table(240), laps = Ui.Table(240);
    private readonly Button place, clear;
    private string? game;
    private Track? selected;
    private string? pictureOf;
    private BitmapSource? bitmap;
    /// <summary>The map's space (the image's pixels, or the plain ground's), and the view: screen = space × scale + offset.</summary>
    private double w = 1000, h = 1000, scale = 1, ox, oy;
    /// <summary>World to space: u = A + B·x, v = C + D·z (null: not lined up).</summary>
    private (double A, double B, double C, double D)? fit;
    private (float MinX, float MaxX, float MinZ, float MaxZ) bounds;
    private bool fitted, placing, dragging, moved, filling;
    private Point dragFrom;
    private (float X, float Z)? car;

    private sealed record Mark(Track Track, Ellipse Dot, Border? Label, double U, double V);

    /// <summary>A row of the list: the track, its name and its kind's colour.</summary>
    private sealed record Item(Track Track, string Name, Brush Colour, string Line)
    {
        public override string ToString() => Name;
    }

    public MapView(AnalysisApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(640) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        // the toolbar: the image, lining it up, fit
        var image = SimUi.Button(T("Map image…"), PickImage, tip: T("Pick an image of this game's map"));
        place = SimUi.Button(T("Place on map"), StartPlacing, tip: T("Click where the track picked starts"));
        clear = SimUi.Confirm(T("Clear line-up"), ClearLineup, T("Forget the tracks placed on the map"));
        var fitButton = SimUi.Button(T("Fit"), Fit, tip: T("Show the whole map"));
        foreach (var b in new FrameworkElement[] { image, place, clear, fitButton }) b.Margin = new Thickness(0, 0, 6, 0);
        lineup.VerticalAlignment = VerticalAlignment.Center;
        lineup.Margin = new Thickness(6, 0, 0, 0);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Children = { image, place, clear, fitButton, lineup } };
        SetColumnSpan(tools, 2);
        Children.Add(tools);

        // the map
        var frame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = marks, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        carDot.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        carDot.ToolTip = T("You");
        AutomationProperties.SetName(marks, T("Map"));
        SetRow(frame, 1);
        Children.Add(frame);
        marks.SizeChanged += (_, _) => { if (!fitted) Fit(); else Place(); };
        marks.MouseLeftButtonDown += Down;
        marks.MouseMove += Drag;
        marks.MouseLeftButtonUp += Up;
        marks.MouseWheel += Zoom;
        marks.KeyDown += (_, e) => { if (e.Key == Key.Escape && placing) StopPlacing(); };
        marks.Focusable = true;

        // beside it: the game, search, filters, the list, the track picked
        AutomationProperties.SetName(search, T("Search"));
        search.TextChanged += (_, _) => { if (!filling) Rebuild(); };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || list.Items.Count == 0) return;
            list.SelectedIndex = 0;
            if (list.SelectedItem is Item i) Select(i.Track, centre: true);
        };
        typeBox.ItemsSource = new[] { Tuple.Create<string?, string>(null, T("All")) }.Concat(Kinds.Select(k => Tuple.Create<string?, string>(k, KindName(k)))).ToList();
        formBox.ItemsSource = new[] { Tuple.Create<string?, string>(null, T("All")), Tuple.Create<string?, string>("Circuit", T("Circuit")), Tuple.Create<string?, string>("Sprint", T("Sprint")) };
        typeBox.SelectedIndex = formBox.SelectedIndex = 0;
        foreach (var b in new[] { typeBox, formBox }) b.SelectionChanged += (_, _) => { if (!filling) Rebuild(); };
        gameBox.SelectionChanged += (_, _) =>
        {
            if (filling || Picked(gameBox) is not { } g || g == game) return;
            game = g;
            selected = null;
            fitted = false;
            Rebuild();
        };
        racedOnly.Checked += (_, _) => Rebuild();
        racedOnly.Unchecked += (_, _) => Rebuild();
        names.Checked += (_, _) => Rebuild();
        names.Unchecked += (_, _) => Rebuild();
        // a row: its kind's colour and its name
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var dot = new FrameworkElementFactory(typeof(Ellipse));
        dot.SetValue(WidthProperty, 9.0);
        dot.SetValue(HeightProperty, 9.0);
        dot.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        dot.SetBinding(Shape.FillProperty, new Binding(nameof(Item.Colour)));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Name)));
        name.SetBinding(ToolTipProperty, new Binding(nameof(Item.Line)));
        row.AppendChild(dot);
        row.AppendChild(name);
        list.ItemTemplate = new DataTemplate { VisualTree = row };
        AutomationProperties.SetName(list, T("Tracks on the map"));
        list.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
        list.SelectionChanged += (_, _) => { if (!filling && list.SelectedItem is Item i && i.Track != selected) Select(i.Track, centre: true); };
        kindBox.SelectionChanged += (_, _) =>
        {
            if (filling || selected == null) return;
            selected.Type = Picked(kindBox);
            settings.Save();
            Rebuild();
        };
        kindRow = SimUi.Field(T("Type"), kindBox, 70);
        var side = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                SimUi.Field(T("Game"), gameBox, 70), SimUi.Field(T("Search"), search, 70), SimUi.Field(T("Type"), typeBox, 70), SimUi.Field(T("Form"), formBox, 70),
                new WrapPanel { Children = { racedOnly, names } }, count, list, title, info, kindRow,
            },
        };
        SetRow(side, 1);
        SetColumn(side, 1);
        Children.Add(side);

        // under it: the track picked's best laps, races and laps
        static void Columns(DataGrid grid, params (string Header, string Path, double Width, string? SortBy)[] columns)
        {
            foreach (var (header, path, width, sortBy) in columns) grid.Columns.Add(Ui.Column(header, path, width, sortBy));
        }
        Columns(bests, (T("Car"), "Car", -2, null), (T("Class"), "Class", 50, null), (T("Best"), "BestText", 80, "Best"), (T("Laps"), "Laps", 45, null));
        Columns(races, (T("When"), "WhenText", -1.4, "When"), (T("Car"), "Car", -2, null), (T("Finish"), "FinishText", 70, "Finish"), (T("Time"), "TimeText", 80, "Time"));
        Columns(laps, (T("When"), "WhenText", -1.4, "When"), (T("Car"), "Car", -2, null), (T("Lap"), "Lap", 40, null), (T("Time"), "TimeText", 80, "Time"));
        AutomationProperties.SetName(bests, T("Best laps here"));
        AutomationProperties.SetName(races, T("Races here"));
        AutomationProperties.SetName(laps, T("Laps here"));
        StackPanel Part(string head, DataGrid grid) => new() { Margin = new Thickness(0, 0, 12, 0), Children = { new TextBlock { Text = head, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }, grid } };
        details = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, -12, 0), Children = { Part(T("Best laps here"), bests), Part(T("Races here"), races), Part(T("Laps here"), laps) } };
        SetRow(details, 2);
        SetColumnSpan(details, 2);
        Children.Add(details);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private static Brush Frozen(string hex)
    {
        var b = Ui.Brush(hex);
        b.Freeze();
        return b;
    }

    private static ComboBox Box(string name)
    {
        var c = new ComboBox { DisplayMemberPath = "Item2", SelectedValuePath = "Item1" };
        AutomationProperties.SetName(c, name);
        return c;
    }

    private static string? Picked(ComboBox box) => (box.SelectedItem as Tuple<string?, string>)?.Item1;

    public static string FormName(string form) => form == "Circuit" ? T("Circuit") : T("Sprint");

    public static string KindName(string kind) => kind switch
    {
        "Road" => T("Road"), "Dirt" => T("Dirt"), "CrossCountry" => T("Cross Country"), "Street" => T("Street"), "Special" => T("Special"), _ => T("Other"),
    };

    /// <summary>A track's kind: the one set for it, else guessed from its name (Forza's: "… Cross Country", "… Scramble"
    /// / "… Trail" dirt, "… Circuit" / "… Sprint" road, "The …" its specials, other names street races).</summary>
    public static string Kind(Track t) => t.Type is { } k && Kinds.Contains(k) ? k : Guess(t.Name);

    private static string Guess(string name)
    {
        string n = name.Trim().ToLowerInvariant();
        if (n.Length == 0) return "Other";
        if (n.Contains("cross country")) return "CrossCountry";
        if (n.Contains("scramble") || n.Contains("trail")) return "Dirt";
        if (n.Contains("circuit") || n.Contains("sprint")) return "Road";
        if (n.StartsWith("the ")) return "Special";
        return "Street";
    }

    /// <summary>Circuit (laps) or Sprint (A to B): a circuit if a race on it had laps or its name says so, else by the
    /// name, else a sprint once raced; null: not known.</summary>
    public static string? Form(Track t)
    {
        string n = t.Name.ToLowerInvariant();
        if (t.Laps > 1 || n.Contains("circuit") || n.Contains("scramble")) return "Circuit";
        if (n.Length > 0 && Kind(t) != "Other") return "Sprint";
        return t.Laps == 1 ? "Sprint" : null;
    }

    private GameMap? Map => game != null && settings.Maps.TryGetValue(game, out var m) ? m : null;

    private static string MapFolder => System.IO.Path.Combine(AppPaths.SettingsFolder, "AnalysisMaps");

    // ---------------------------------------------------------------- filling

    /// <summary>The tracks, laps or races changed: the games, the dots and the track picked shown again.</summary>
    public void Refresh()
    {
        if (!IsVisible) return;
        var games = settings.Tracks.Select(t => t.Game).Concat(settings.Maps.Keys).Distinct().OrderBy(g => g).ToList();
        if (game == null || !games.Contains(game))
            game = CarNames.Edition is { } e && games.Contains(e) ? e : settings.Tracks.GroupBy(t => t.Game).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? CarNames.Edition ?? LapStore.OldGame;
        if (!games.Contains(game)) games.Add(game);
        filling = true;
        gameBox.ItemsSource = games.Select(g => Tuple.Create<string?, string>(g, g)).ToList();
        gameBox.SelectedIndex = games.IndexOf(game);
        filling = false;
        if (selected != null && !settings.Tracks.Contains(selected)) selected = null;
        Rebuild();
    }

    /// <summary>The dots and the list, for the game and filters (the map's space, its line-up and the image too).</summary>
    private void Rebuild()
    {
        var all = settings.Tracks.Where(t => t.Game == game).ToList();
        bounds = all.Count == 0 ? (-1000, 1000, -1000, 1000) : (all.Min(t => t.X), all.Max(t => t.X), all.Min(t => t.Z), all.Max(t => t.Z));
        LoadPicture();
        double oldW = w, oldH = h;
        if (bitmap != null) { w = bitmap.PixelWidth; h = bitmap.PixelHeight; }
        else
        {
            // a plain ground the tracks' shape, a tenth round them
            double rx = Math.Max(500, bounds.MaxX - bounds.MinX), rz = Math.Max(500, bounds.MaxZ - bounds.MinZ), k = 1000 / Math.Max(rx, rz);
            w = rx * k / 0.9;
            h = rz * k / 0.9;
        }
        if (w != oldW || h != oldH) fitted = false;
        fit = bitmap != null ? Lineup(Map?.Anchors, w, h) : null;
        int placed = Map?.Anchors.Count ?? 0;
        lineup.Text = bitmap == null ? T("No map image: tracks on a plain ground")
            : fit != null ? P(placed, "Lined up from {0} track", "Lined up from {0} tracks")
            : T("Pick a track, Place on map, click where it starts (2 far apart)");
        clear.Visibility = placed > 0 ? Visibility.Visible : Visibility.Collapsed;

        var byTrack = applet.Store.All.ToLookup(l => l.Track);
        var raced = applet.Store.Races.Select(r => r.Track).ToHashSet();
        string text = search.Text.Trim();
        string? type = Picked(typeBox), form = Picked(formBox);
        var shown = all.Where(t =>
            (text.Length == 0 || settings.TrackName(t.Id).Contains(text, StringComparison.CurrentCultureIgnoreCase)) &&
            (type == null || Kind(t) == type) && (form == null || Form(t) == form) &&
            (racedOnly.IsChecked != true || byTrack[t.Id].Any() || raced.Contains(t.Id))).ToList();

        marks.Children.Clear();
        dots.Clear();
        if (bitmap != null) marks.Children.Add(picture);
        foreach (var t in shown.OrderBy(t => t == selected))
        {
            var (u, v) = Space(t.X, t.Z);
            bool picked = t == selected;
            string nm = settings.TrackName(t.Id);
            var e = new Ellipse
            {
                Width = picked ? 18 : 12, Height = picked ? 18 : 12, Fill = KindBrush[Kind(t)], Stroke = picked ? White : Edge, StrokeThickness = picked ? 3 : 1.5,
                Tag = t, ToolTip = $"{nm} · {KindName(Kind(t))}",
            };
            AutomationProperties.SetName(e, nm);
            Border? label = null;
            if (picked || names.IsChecked == true)
            {
                label = new Border
                {
                    Background = LabelBack, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1), IsHitTestVisible = false,
                    Child = new TextBlock { Text = nm, Foreground = White, FontSize = picked ? 13 : 11, FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal },
                };
                marks.Children.Add(label);
            }
            marks.Children.Add(e);
            dots.Add(new Mark(t, e, label, u, v));
        }
        marks.Children.Add(carDot);
        count.Text = F("{0} of {1} tracks", shown.Count, all.Count);

        filling = true;
        var items = shown.Select(t => new Item(t, settings.TrackName(t.Id), KindBrush[Kind(t)], KindName(Kind(t)) + (Form(t) is { } f ? " · " + FormName(f) : "")))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        list.ItemsSource = items;
        list.SelectedItem = items.FirstOrDefault(i => i.Track == selected);
        filling = false;
        place.IsEnabled = bitmap != null && selected != null;
        ShowPicked();
        if (!fitted) Fit(); else Place();
    }

    /// <summary>The map image for the game (read off the UI thread; big ones at 8192 pixels across at most).</summary>
    private void LoadPicture()
    {
        string? file = Map?.Image;
        string key = $"{game}|{file}";
        if (key == pictureOf) return;
        pictureOf = key;
        bitmap = null;
        picture.Source = null;
        fitted = false;
        if (file == null) return;
        string path = System.IO.Path.Combine(MapFolder, file);
        Task.Run(() =>
        {
            try
            {
                var uri = new Uri(path);
                int across = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0].PixelWidth;
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = uri;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                if (across > 8192) b.DecodePixelWidth = 8192;
                b.EndInit();
                b.Freeze();
                return (BitmapSource?)b;
            }
            catch (Exception ex)
            {
                Logging.Log.Here.Warn($"Analysis: reading the map {path}", ex);
                return null;
            }
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (pictureOf != key || t.Result == null) return;
            bitmap = t.Result;
            picture.Source = bitmap;
            fitted = false;
            Rebuild();
        }));
    }

    /// <summary>World to the map's space: by the line-up, else the tracks spread over the ground (north up).</summary>
    private (double U, double V) Space(float x, float z)
    {
        if (fit is { } f) return (f.A + f.B * x, f.C + f.D * z);
        double rx = Math.Max(500, bounds.MaxX - bounds.MinX), rz = Math.Max(500, bounds.MaxZ - bounds.MinZ);
        double cx = (bounds.MinX + bounds.MaxX) / 2, cz = (bounds.MinZ + bounds.MaxZ) / 2;
        return ((0.5 + 0.9 * (x - cx) / rx) * w, (0.5 - 0.9 * (z - cz) / rz) * h);
    }

    /// <summary>The line-up from the tracks placed: each axis a straight fit (least squares) of the image against the
    /// world; null while they're fewer than 2 or not 200 m apart both ways.</summary>
    internal static (double A, double B, double C, double D)? Lineup(IReadOnlyList<MapAnchor>? anchors, double w, double h)
    {
        if (anchors == null || anchors.Count < 2) return null;
        if (anchors.Max(a => a.X) - anchors.Min(a => a.X) < 200 || anchors.Max(a => a.Z) - anchors.Min(a => a.Z) < 200) return null;
        static (double A, double B)? Line(IEnumerable<(double X, double Y)> pts)
        {
            double n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var (x, y) in pts) { n++; sx += x; sy += y; sxx += x * x; sxy += x * y; }
            double den = n * sxx - sx * sx;
            if (n < 2 || Math.Abs(den) < 1e-9) return null;
            double b = (n * sxy - sx * sy) / den;
            return ((sy - b * sx) / n, b);
        }
        if (Line(anchors.Select(a => ((double)a.X, a.U * w))) is not { } u || Line(anchors.Select(a => ((double)a.Z, a.V * h))) is not { } v) return null;
        return (u.A, u.B, v.A, v.B);
    }

    // ---------------------------------------------------------------- the view

    private double FitScale => marks.ActualWidth <= 0 ? 1 : Math.Min(marks.ActualWidth / w, marks.ActualHeight / h);

    /// <summary>The whole map in view.</summary>
    private void Fit()
    {
        if (marks.ActualWidth <= 0 || marks.ActualHeight <= 0) return;
        scale = FitScale;
        ox = (marks.ActualWidth - w * scale) / 2;
        oy = (marks.ActualHeight - h * scale) / 2;
        fitted = true;
        Place();
    }

    /// <summary>Everything where the view puts it (the dots stay their size).</summary>
    private void Place()
    {
        if (bitmap != null)
        {
            Canvas.SetLeft(picture, ox);
            Canvas.SetTop(picture, oy);
            picture.Width = w * scale;
            picture.Height = h * scale;
        }
        foreach (var m in dots)
        {
            double x = ox + m.U * scale, y = oy + m.V * scale;
            Canvas.SetLeft(m.Dot, x - m.Dot.Width / 2);
            Canvas.SetTop(m.Dot, y - m.Dot.Height / 2);
            if (m.Label != null)
            {
                Canvas.SetLeft(m.Label, x + m.Dot.Width / 2 + 3);
                Canvas.SetTop(m.Label, y - 9);
            }
        }
        PlaceCar();
    }

    private void PlaceCar()
    {
        if (car is not { } c) { carDot.Visibility = Visibility.Collapsed; return; }
        var (u, v) = Space(c.X, c.Z);
        Canvas.SetLeft(carDot, ox + u * scale - carDot.Width / 2);
        Canvas.SetTop(carDot, oy + v * scale - carDot.Height / 2);
        carDot.Visibility = Visibility.Visible;
    }

    /// <summary>The car on the map, while the game sends where it is (the tab's tick).</summary>
    public void Tick(Reading now)
    {
        if (!IsVisible) return;
        (float, float)? at = now.Frame is { HasRace: true } f && (f.PosX != 0 || f.PosZ != 0) && now.Game == game ? (f.PosX, f.PosZ) : null;
        if (at == car) return;
        car = at;
        PlaceCar();
    }

    private void Zoom(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(marks);
        double to = Math.Clamp(scale * Math.Pow(1.25, e.Delta / 120.0), FitScale * 0.5, FitScale * 60);
        ox = p.X - (p.X - ox) * to / scale;
        oy = p.Y - (p.Y - oy) * to / scale;
        scale = to;
        Place();
        e.Handled = true;
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        marks.Focus();
        if (!placing && e.OriginalSource is Ellipse { Tag: Track t })
        {
            Select(t, centre: false);
            e.Handled = true;
            return;
        }
        dragging = true;
        moved = false;
        dragFrom = e.GetPosition(marks);
        marks.CaptureMouse();
        e.Handled = true;
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(marks);
        var d = p - dragFrom;
        if (!moved && Math.Abs(d.X) + Math.Abs(d.Y) < 4) return;
        moved = true;
        marks.Cursor = Cursors.SizeAll;
        ox += d.X;
        oy += d.Y;
        dragFrom = p;
        Place();
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        marks.ReleaseMouseCapture();
        marks.Cursor = placing ? Cursors.Cross : Cursors.Hand;
        if (!moved && placing) Anchor(e.GetPosition(marks));
    }

    // ---------------------------------------------------------------- picking and lining up

    /// <summary>A track picked (its dot, the list or a search): shown large, with its times under the map.</summary>
    private void Select(Track t, bool centre)
    {
        selected = t;
        if (centre)
        {
            var (u, v) = Space(t.X, t.Z);
            if (scale < FitScale * 2.5) scale = FitScale * 2.5;
            ox = marks.ActualWidth / 2 - u * scale;
            oy = marks.ActualHeight / 2 - v * scale;
        }
        Rebuild();
    }

    /// <summary>The track picked: its name, kind, form, length, place and how much it's been driven; its best laps by car
    /// and class, its races and its laps.</summary>
    private void ShowPicked()
    {
        var t = selected;
        title.Visibility = info.Visibility = kindRow.Visibility = details.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
        if (t == null) return;
        title.Text = settings.TrackName(t.Id);
        var mine = applet.Store.All.Where(l => l.Track == t.Id).ToList();
        var raced = applet.Store.Races.Where(r => r.Track == t.Id).ToList();
        string length = t.Length > 0 ? (settings.Units == AnalysisUnits.Metric ? $"{t.Length / 1000:0.00} km" : $"{t.Length / 1609.34:0.00} mi") : T("no race yet");
        info.Text = string.Join(" · ", new[] { KindName(Kind(t)), Form(t) is { } f ? FormName(f) : null, length }.Where(x => x != null)) + "\n"
            + F("{0:0}, {1:0}", t.X, t.Z) + " · " + P(mine.Count, "{0} lap", "{0} laps") + " · " + P(raced.Count, "{0} race", "{0} races")
            + (mine.MinBy(l => l.Time) is { } b ? "\n" + F("Best {0} ({1})", Blocks.LapTime(b.Time), settings.CarName(b.Game, b.Car)) : "");
        filling = true;
        var kinds = new List<Tuple<string?, string>> { Tuple.Create<string?, string>(null, F("Auto ({0})", KindName(Guess(t.Name)))) };
        kinds.AddRange(Kinds.Select(k => Tuple.Create<string?, string>(k, KindName(k))));
        kindBox.ItemsSource = kinds;
        kindBox.SelectedIndex = Math.Max(0, kinds.FindIndex(k => k.Item1 == t.Type));
        filling = false;

        bests.ItemsSource = mine.GroupBy(l => (l.Game, l.Car, l.Class)).Select(g =>
        {
            var best = g.MinBy(l => l.Time)!;
            return new { Car = settings.CarName(g.Key.Game, g.Key.Car), Class = Blocks.ClassName(g.Key.Class, g.Key.Game), Best = best.Time, BestText = Blocks.LapTime(best.Time), Laps = g.Count() };
        }).OrderBy(r => r.Best).ToList();
        races.ItemsSource = raced.OrderByDescending(r => r.At).Select(r => new
        {
            When = r.At, WhenText = r.At.ToString("g", CultureInfo.CurrentCulture), Car = settings.CarName(r.Game, r.Car),
            Finish = r.Finish, FinishText = AnalysisView.Place(r.Finish, r.Racers), Time = r.Time, TimeText = Blocks.LapTime(r.Time),
        }).ToList();
        laps.ItemsSource = mine.OrderByDescending(l => l.At).Take(500).Select(l => new
        {
            When = l.At, WhenText = l.At.ToString("g", CultureInfo.CurrentCulture), Car = settings.CarName(l.Game, l.Car), l.Lap, Time = l.Time, TimeText = Blocks.LapTime(l.Time),
        }).ToList();
    }

    private void StartPlacing()
    {
        if (selected == null || bitmap == null) return;
        placing = true;
        marks.Cursor = Cursors.Cross;
        marks.Focus();
        lineup.Text = F("Click where {0} starts (Esc: cancel)", settings.TrackName(selected.Id));
    }

    private void StopPlacing()
    {
        placing = false;
        marks.Cursor = Cursors.Hand;
        Rebuild();
    }

    /// <summary>The track picked placed where the map was clicked (again: moved there).</summary>
    private void Anchor(Point p)
    {
        if (selected is not { } t || game == null || bitmap == null) { StopPlacing(); return; }
        double u = (p.X - ox) / scale, v = (p.Y - oy) / scale;
        if (u < 0 || v < 0 || u > w || v > h) return;
        var map = Map ?? (settings.Maps[game] = new GameMap());
        map.Anchors.RemoveAll(a => a.Track == t.Id);
        map.Anchors.Add(new MapAnchor { Track = t.Id, X = t.X, Z = t.Z, U = u / w, V = v / h });
        settings.Save();
        Logging.Log.Here.Info($"Analysis: {game} map: track {t.Id} placed at {u:0}, {v:0} ({map.Anchors.Count} placed)");
        StopPlacing();
    }

    private void ClearLineup()
    {
        if (Map is not { } m) return;
        m.Anchors.Clear();
        settings.Save();
        Rebuild();
    }

    /// <summary>An image of the game's map, copied beside the settings (the one before it replaced).</summary>
    private void PickImage()
    {
        if (game == null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Images") + " (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            Directory.CreateDirectory(MapFolder);
            string file = game + System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
            foreach (var old in Directory.GetFiles(MapFolder, game + ".*")) File.Delete(old);
            File.Copy(dialog.FileName, System.IO.Path.Combine(MapFolder, file), true);
            var map = Map ?? (settings.Maps[game] = new GameMap());
            map.Image = file;
            settings.Save();
            pictureOf = null;
            Logging.Log.Here.Info($"Analysis: {game} map image {dialog.FileName}");
            Rebuild();
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Analysis: copying the map image", ex);
            DaisysApp.Shell.Notifications.Show(T("Analysis"), T("The map image couldn't be copied"));
        }
    }
}
