using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DaisysApp.Applets.MiniMirror;

public partial class MiniMirrorView : UserControl
{
    public sealed record Row(MirrorDefinition Definition, string Name, string Meta, bool Visible)
    {
        public override string ToString() => Name; // screen readers / UI automation
    }

    private readonly MiniMirrorService service;
    private readonly DispatcherTimer deleteDisarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private MirrorDefinition? current;
    private bool loading;
    private bool deleteArmed;
    private WindowState? restoreState;
    private HashSet<Guid>? knownIds; // null until the list is first shown
    private const string SelectingStatus = "Drag around what you want to mirror. R or C switches rectangle / circle; Esc cancels.";

    public MiniMirrorView(MiniMirrorService service)
    {
        this.service = service;
        loading = true; // sliders raise ValueChanged while the XAML loads
        InitializeComponent();
        loading = false;
        Shortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        Shortcut.Changed += Shortcut_Changed;
        deleteDisarm.Tick += (_, _) => DisarmDelete();

        service.MirrorsChanged += RebuildList;
        service.MirrorUpdated += d => { if (d == current) LoadEditor(); };
        service.SelectingChanged += OnSelectingChanged;
        service.PausedChanged += ShowPaused;
        Loaded += (_, _) => ShowPaused();
        RebuildList();
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowShortcutHint(); };
        ShowShortcutHint();
    }

    private void ShowShortcutHint() =>
        NewButton.ToolTip = service.Data.NewMirrorShortcut is { } s
            ? $"Drag around any part of the screen to mirror it. Or press {s} from anywhere (e.g. in a game) — Settings → Mini Mirror."
            : "Drag around any part of the screen to mirror it.";

    private void ShowPaused()
    {
        PausedBanner.Visibility = service.Paused ? Visibility.Visible : Visibility.Collapsed;
        PausedText.Text = service.PausedReason + " Details are in the error log (Settings → General → Files).";
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        service.Resume();
        SetStatus("Mirrors started.");
    }

    // ---------------------------------------------------------------- list

    private void RebuildList()
    {
        var rows = service.Mirrors.Select(d => new Row(d, d.Name, Meta(d), d.Visible)).ToList();
        // a mirror that wasn't here before (new, duplicated or imported) gets selected; otherwise keep the selection
        var added = knownIds == null ? null : rows.LastOrDefault(r => !knownIds.Contains(r.Definition.Id));
        knownIds = rows.Select(r => r.Definition.Id).ToHashSet();
        if (added != null) SetStatus($"Made {added.Name}. Drag it wherever you want it.");
        var keep = added?.Definition.Id ?? current?.Id;
        loading = true;
        MirrorList.ItemsSource = rows;
        MirrorList.SelectedItem = rows.FirstOrDefault(r => r.Definition.Id == keep);
        loading = false;

        int n = rows.Count;
        CountText.Text = n == 0 ? "" : $"{n}";
        EmptyText.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        AllButtons.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        Select((MirrorList.SelectedItem as Row)?.Definition);
    }

    private static string Meta(MirrorDefinition d)
    {
        var parts = new List<string> { $"{d.SourceRect.Width} × {d.SourceRect.Height}" };
        if (d.Shape == MirrorShape.Circle) parts.Add("circle");
        if (Math.Abs(d.Zoom - 1) > 0.01) parts.Add($"zoom {Factor(d.Zoom)}");
        parts.Add($"{d.TargetFps} fps");
        if (d.Shortcut != null) parts.Add(d.Shortcut);
        return string.Join(" · ", parts);
    }

    private void MirrorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading) Select((MirrorList.SelectedItem as Row)?.Definition);
    }

    private void Select(MirrorDefinition? d)
    {
        if (current != null && current != d) CommitName();
        current = d;
        DisarmDelete();
        NoSelection.Visibility = d == null ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = d == null ? Visibility.Collapsed : Visibility.Visible;
        if (d != null) LoadEditor();
    }

    // ---------------------------------------------------------------- editor

    private void LoadEditor()
    {
        if (current is not { } d) return;
        loading = true;
        if (!NameBox.IsKeyboardFocused) NameBox.Text = d.Name;
        var r = d.SourceRect;
        var monitors = MonitorService.GetMonitorsIntersecting(r).Select(m => m.DeviceName.Replace(@"\\.\DISPLAY", "display ")).ToList();
        RegionText.Text = $"Mirrors {r.Width} × {r.Height} px at {r.X}, {r.Y}" + (monitors.Count > 0 ? $" on {string.Join(" and ", monitors)}" : " (that area isn't on any monitor right now)");
        VisibleBox.IsChecked = d.Visible;
        RectangleRadio.IsChecked = d.Shape == MirrorShape.Rectangle;
        CircleRadio.IsChecked = d.Shape == MirrorShape.Circle;
        SizeSlider.Value = Math.Log2(Math.Clamp(d.SizeScale, 0.1, 5));
        SizeText.Text = Factor(d.SizeScale);
        ZoomSlider.Value = Math.Log2(Math.Clamp(d.Zoom, MirrorCompositor.MinZoom, MirrorCompositor.MaxZoom));
        ZoomText.Text = Factor(d.Zoom);
        OpacitySlider.Value = Math.Round(d.Opacity * 100);
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        FpsSlider.Value = d.TargetFps;
        FpsText.Text = $"{d.TargetFps} fps";
        LockBox.IsChecked = d.PositionLocked;
        ClickThroughBox.IsChecked = d.ClickThrough;
        AspectBox.IsChecked = d.AspectLock;
        Shortcut.Value = d.Shortcut;
        ShowShortcutWarning();
        loading = false;
    }

    private static string Factor(double v) => v.ToString(v < 0.995 ? "0.00" : "0.0", CultureInfo.CurrentCulture) + "×";

    private void ShowShortcutWarning()
    {
        string? s = current?.Shortcut;
        bool failed = s != null && service.FailedShortcuts.Contains(s, StringComparer.OrdinalIgnoreCase);
        ShortcutWarning.Text = failed ? $"{s} is already used by another program, so it won't work. Pick another." : "";
        ShortcutWarning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CommitName()
    {
        if (current != null && NameBox.Text.Trim() != current.Name) service.Rename(current, NameBox.Text);
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitName();
        else if (e.Key == Key.Escape && current != null) NameBox.Text = current.Name;
    }

    private void VisibleBox_Changed(object sender, RoutedEventArgs e)
    {
        if (loading || current == null) return;
        service.SetVisible(current, VisibleBox.IsChecked == true);
    }

    private void Shape_Checked(object sender, RoutedEventArgs e)
    {
        if (loading || current == null) return;
        current.Shape = CircleRadio.IsChecked == true ? MirrorShape.Circle : MirrorShape.Rectangle;
        service.Apply(current);
        UpdateRowMeta();
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || current == null) return;
        double v = Math.Round(Math.Pow(2, SizeSlider.Value), 2);
        SizeText.Text = Factor(v);
        service.SetSizeScale(current, v);
    }

    private void SizeSlider_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SizeSlider.Value = 0;

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || current == null) return;
        double v = Math.Round(Math.Pow(2, ZoomSlider.Value), 2);
        ZoomText.Text = Factor(v);
        current.Zoom = v;
        service.Apply(current);
        UpdateRowMeta();
    }

    private void ZoomSlider_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ZoomSlider.Value = 0;

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || current == null) return;
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        current.Opacity = Math.Round(OpacitySlider.Value) / 100.0;
        service.Apply(current);
    }

    private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || current == null) return;
        FpsText.Text = $"{FpsSlider.Value:0} fps";
        current.TargetFps = (int)FpsSlider.Value;
        service.Apply(current);
        UpdateRowMeta();
    }

    private void Behaviour_Changed(object sender, RoutedEventArgs e)
    {
        if (loading || current == null) return;
        current.PositionLocked = LockBox.IsChecked == true;
        current.ClickThrough = ClickThroughBox.IsChecked == true;
        current.AspectLock = AspectBox.IsChecked == true;
        service.Apply(current);
        if (sender == ClickThroughBox && current.ClickThrough)
            SetStatus("Click-through is on: use this page or the tray menu to change the mirror, since clicks now go past it.");
    }

    private void Shortcut_Changed()
    {
        if (current == null) return;
        service.SetShortcut(current, Shortcut.Value);
        ShowShortcutWarning();
        SetStatus(Shortcut.Value == null ? $"Removed the shortcut from {current.Name}." : $"{Shortcut.Value} now shows and hides {current.Name}.");
    }

    /// <summary>Refreshes the selected row's summary line without rebuilding the list (keeps slider drags smooth).</summary>
    private void UpdateRowMeta()
    {
        if (current == null || MirrorList.ItemsSource is not List<Row> rows) return;
        int i = rows.FindIndex(r => r.Definition == current);
        if (i < 0) return;
        string meta = Meta(current);
        if (rows[i].Meta == meta) return;
        rows[i] = rows[i] with { Meta = meta };
        loading = true;
        MirrorList.Items.Refresh();
        MirrorList.SelectedIndex = i;
        loading = false;
    }

    // ---------------------------------------------------------------- buttons

    private void New_Click(object sender, RoutedEventArgs e) => service.BeginCreate();

    private void Reselect_Click(object sender, RoutedEventArgs e)
    {
        if (current != null) service.BeginReselect(current);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        var copy = service.Duplicate(current);
        MirrorList.SelectedItem = (MirrorList.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Definition == copy);
        SetStatus($"Made {copy.Name}, a little below and to the right of the original.");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            DeleteButton.Style = (Style)FindResource("DangerButton");
            DeleteText.Text = "Click again to delete";
            deleteDisarm.Start();
            return;
        }
        string name = current.Name;
        var doomed = current;
        current = null;
        service.Delete(doomed);
        SetStatus($"Deleted {name}.");
    }

    private void DisarmDelete()
    {
        deleteArmed = false;
        deleteDisarm.Stop();
        DeleteButton.ClearValue(StyleProperty);
        DeleteText.Text = "Delete";
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e) => service.SetAllVisible(true);

    private void HideAll_Click(object sender, RoutedEventArgs e) => service.SetAllVisible(false);

    // ---------------------------------------------------------------- selection

    /// <summary>Gets this window out of the way while a region is picked, and brings it back afterwards.</summary>
    private void OnSelectingChanged(bool selecting)
    {
        NewButton.IsEnabled = !selecting;
        var window = Window.GetWindow(this);
        if (selecting)
        {
            SetStatus(SelectingStatus);
            // only when it's in front (the New mirror button); from the shortcut a game is in front and keeps the focus
            if (window is { IsVisible: true, IsActive: true } && window.WindowState != WindowState.Minimized)
            {
                restoreState = window.WindowState;
                window.WindowState = WindowState.Minimized;
            }
        }
        else
        {
            if (StatusText.Text == SelectingStatus) SetStatus("Ready.");
            if (restoreState is { } state && window != null)
            {
                window.WindowState = state;
                window.Activate();
            }
            restoreState = null;
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
