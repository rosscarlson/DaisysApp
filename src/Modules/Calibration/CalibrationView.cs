using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// The controller being calibrated, read about 30 times a second while the tab (or one of its windows) shows: what
/// the tab and its windows draw from. <see cref="Device"/> is null while it isn't plugged in.
/// </summary>
internal sealed class Live
{
    public ControllerInfo? Info { get; set; }
    public IController? Device { get; set; }
    public int[] Raw { get; set; } = Array.Empty<int>();
    public double[] Output { get; set; } = Array.Empty<double>();

    /// <summary>After each reading (and when the device comes or goes).</summary>
    public event Action? Ticked;

    public void Tick() => Ticked?.Invoke();

    /// <summary>The axis at <paramref name="offset"/> on the device as it's open now (it may have been opened again since).</summary>
    public Axis? AxisAt(int offset) => Device?.Axes.FirstOrDefault(a => a.Offset == offset);
}

/// <summary>
/// The tab: the controllers on the left, the one picked on the right with each axis's raw reading (the line, with
/// Windows' min, centre and max as ticks) and what games get (the fill). Clicking an axis opens <see cref="AxisWindow"/>;
/// Wizard opens <see cref="WizardWindow"/>.
/// </summary>
internal sealed class CalibrationView : Grid, IDisposable
{
    private readonly CalibrationSettings settings;
    private readonly IntPtr window;
    private readonly Live live = new();
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock noneText;
    private readonly StackPanel right = new();
    private readonly DispatcherTimer readTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly List<Row> rows = new();
    private List<ControllerInfo> controllers = new();
    private TextBlock? status;
    private Button? wizard;
    private bool listing, filling, modal;

    private sealed record Row(Axis Axis, AxisBar Raw, AxisBar Output, TextBlock RawText, TextBlock OutText, TextBlock CalText);

    public CalibrationView(CalibrationSettings settings, IntPtr window)
    {
        this.settings = settings;
        this.window = window;
        Margin = new Thickness(16);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        ColumnDefinitions.Add(new ColumnDefinition());

        noneText = Secondary(T("No controllers"));
        var left = new StackPanel();
        // (the controllers are listed once; Refresh lists them again, for one plugged in since)
        var head = new DockPanel();
        var refresh = DaisysApp.Controllers.GameControllers.RefreshButton(T("Look for controllers again"));
        refresh.Margin = new Thickness(0);
        DockPanel.SetDock(refresh, Dock.Right);
        head.Children.Add(refresh);
        head.Children.Add(Header(T("Controllers")));
        left.Children.Add(head);
        left.Children.Add(noneText);
        left.Children.Add(list);
        list.SelectionChanged += (_, _) =>
        {
            if (filling || list.SelectedItem is not ListBoxItem { Tag: ControllerInfo c } || c.Instance == live.Info?.Instance) return;
            settings.Selected = c.Instance;
            settings.Save();
            Pick(c);
        };
        Children.Add(Scroll(Card(left)));
        var r = Scroll(Card(right));
        SetColumn(r, 2);
        Children.Add(r);

        readTimer.Tick += (_, _) => Read();
        DaisysApp.Controllers.GameControllers.Changed += Relisted;
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await Relist();
                readTimer.Start();
            }
            else if (!modal)
            {
                readTimer.Stop();
                Close();   // let it go while nobody's looking
            }
        };
        ShowDevice();
    }

    private async void Relisted()
    {
        if (IsVisible) await Relist();
    }

    /// <summary>Reads the controllers plugged in (in the background: DirectInput opens each to ask) and follows the picked one.</summary>
    private async Task Relist()
    {
        if (listing) return;
        listing = true;
        try
        {
            var now = await Task.Run(Controllers.List);
            bool changed = now.Count != controllers.Count || now.Where((c, i) => c != controllers[i]).Any();
            controllers = now;
            if (changed) FillList();
            var pick = now.FirstOrDefault(c => c.Instance == (live.Info?.Instance ?? settings.Selected)) ?? (live.Info == null ? now.FirstOrDefault() : null);
            if (pick != null && (live.Device == null || pick.Instance != live.Info?.Instance)) Pick(pick);
            else if (pick == null && live.Device != null) Lost();
            else if (pick == null && live.Info != null) ShowStatus(T("Not connected"));
        }
        finally { listing = false; }
    }

    private void FillList()
    {
        filling = true;
        list.Items.Clear();
        foreach (var c in controllers)
        {
            var s = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
            s.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            s.Children.Add(Secondary($"{c.Key} · " + P(c.AxisCount, "{0} axis", "{0} axes")));
            var item = new ListBoxItem { Content = s, Tag = c, ToolTip = c.Name };
            AutomationProperties.SetName(item, c.Name);
            list.Items.Add(item);
            if (c.Instance == live.Info?.Instance) list.SelectedItem = item;
        }
        noneText.Visibility = controllers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        filling = false;
    }

    private void Pick(ControllerInfo c)
    {
        Close();
        live.Info = c;
        live.Device = Controllers.Open(c, window, out string? problem);
        if (live.Device != null)
        {
            live.Raw = new int[live.Device.Axes.Count];
            live.Output = new double[live.Device.Axes.Count];
        }
        filling = true;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is ControllerInfo x && x.Instance == c.Instance);
        filling = false;
        ShowDevice();
        if (problem != null) ShowStatus(problem);
        live.Tick();
    }

    private void Lost()
    {
        Logging.Log.Here.Info($"{live.Info?.Name} went (unplugged)");
        live.Device?.Dispose();
        live.Device = null;
        ShowStatus(T("Not connected"));
        live.Tick();
    }

    private void Close()
    {
        live.Device?.Dispose();
        live.Device = null;
    }

    private void ShowDevice()
    {
        right.Children.Clear();
        rows.Clear();
        var info = live.Info;
        if (info == null)
        {
            right.Children.Add(Header(T("Axes")));
            right.Children.Add(Secondary(T("Pick a controller")));
            return;
        }
        wizard = new Button { Content = T("Wizard"), Padding = new Thickness(12, 3, 12, 3), IsEnabled = live.Device != null, ToolTip = T("Each axis in turn: its range found as you move it") };
        wizard.SetResourceReference(StyleProperty, "AccentButton");
        wizard.Click += (_, _) => OpenWizard();
        right.Children.Add(HeaderLine(info.Name, wizard));
        status = Secondary("");
        status.Visibility = Visibility.Collapsed;
        status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        right.Children.Add(status);
        if (live.Device == null) return;

        foreach (var a in live.Device.Axes)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold });
            var cal = Secondary("");
            name.Children.Add(cal);
            SetRowSpan(name, 2);
            g.Children.Add(name);
            var rawBar = new AxisBar { Fill = false, Margin = new Thickness(0, 3, 8, 3) };
            var outBar = new AxisBar { Margin = new Thickness(0, 3, 8, 3) };
            var rawText = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var outText = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Place(g, Secondary(T("Raw")), 0, 1);
            Place(g, rawBar, 0, 2);
            Place(g, rawText, 0, 3);
            Place(g, Secondary(T("Output")), 1, 1);
            Place(g, outBar, 1, 2);
            Place(g, outText, 1, 3);

            var row = new Button { Content = g, Template = FlatRow, Padding = new Thickness(8, 6, 8, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = T("Calibrate"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            row.MouseEnter += (_, _) => row.SetResourceReference(BackgroundProperty, "RowHoverBrush");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            int offset = a.Offset;
            row.Click += (_, _) => OpenAxis(offset);
            AutomationProperties.SetName(row, a.Name);
            right.Children.Add(row);
            rows.Add(new Row(a, rawBar, outBar, rawText, outText, cal));
        }
        ShowCalibrations();
    }

    /// <summary>Each axis's calibration as Windows has it now, under its name and as ticks on its raw bar.</summary>
    private void ShowCalibrations()
    {
        if (live.Device is not { } dev || live.Info is not { } info) return;
        foreach (var r in rows)
        {
            var c = dev.Get(r.Axis);
            bool ignored = settings.Ignored.Contains(CalibrationSettings.KeyOf(info, r.Axis));
            r.CalText.Text = (c == r.Axis.Default ? T("Not calibrated") : c.ToString()) + (ignored ? " · " + T("Ignored") : "");
            double range = Math.Max(1, r.Axis.High - r.Axis.Low);
            r.Raw.Marks = new[] { (c.Min - r.Axis.Low) / range, (c.Center - r.Axis.Low) / range, (c.Max - r.Axis.Low) / range };
        }
    }

    private void ShowStatus(string text)
    {
        if (status == null) return;
        status.Text = text;
        status.Visibility = Visibility.Visible;
        if (wizard != null) wizard.IsEnabled = live.Device != null;
    }

    private void Read()
    {
        if (live.Device is not { } dev) return;
        if (!dev.Read(live.Raw, live.Output))
        {
            Lost();
            return;
        }
        foreach (var r in rows)
        {
            int v = live.Raw[r.Axis.Index];
            r.Raw.Value = (v - r.Axis.Low) / (double)Math.Max(1, r.Axis.High - r.Axis.Low);
            r.RawText.Text = v.ToString();
            r.Output.Value = live.Output[r.Axis.Index];
            r.OutText.Text = $"{live.Output[r.Axis.Index] * 100:0.0}%";
        }
        live.Tick();
    }

    private void OpenAxis(int offset)
    {
        if (live.AxisAt(offset) == null) return;
        Modal(new AxisWindow(live, settings, offset));
    }

    private void OpenWizard()
    {
        if (live.Device == null) return;
        Modal(new WizardWindow(live, settings));
    }

    /// <summary>Shows a window over the app; the readings carry on (the tab stays "visible" underneath).</summary>
    private void Modal(Window w)
    {
        w.Owner = Window.GetWindow(this);
        modal = true;
        try { w.ShowDialog(); }
        finally { modal = false; }
        // a device unplugged and back while it showed is open again by now; either way, show what Windows has
        if (live.Device != null && rows.Count != live.Device.Axes.Count) ShowDevice();
        else ShowCalibrations();
    }

    public void Dispose()
    {
        readTimer.Stop();
        DaisysApp.Controllers.GameControllers.Changed -= Relisted;
        Close();
    }

    // the app's styles, as the other Gaming tabs use them
    internal static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    internal static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "CardHeader");
        return t;
    }

    internal static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    internal static DockPanel HeaderLine(string text, UIElement right)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        d.Children.Add(right);
        var h = Header(text);
        h.Margin = new Thickness(0);
        h.VerticalAlignment = VerticalAlignment.Center;
        h.TextTrimming = TextTrimming.CharacterEllipsis;
        d.Children.Add(h);
        return d;
    }

    /// <summary>An axis row: a button that's only its background (the hover colour) and content.</summary>
    private static readonly ControlTemplate FlatRow = MakeFlatRow();

    private static ControlTemplate MakeFlatRow()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static void Place(Grid g, UIElement e, int row, int column)
    {
        SetRow(e, row);
        SetColumn(e, column);
        g.Children.Add(e);
    }
}
