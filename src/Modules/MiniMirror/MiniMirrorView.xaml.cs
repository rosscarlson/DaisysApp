using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DaisysApp.Applets.MiniMirror;

public partial class MiniMirrorView : UserControl
{
    public enum RowKind { Mirror, Group, Loose }

    /// <summary>A line of the list: a mirror, a group's heading, or the "Not in a group" heading (shown once there are groups).</summary>
    public sealed record Row(RowKind Kind, MirrorDefinition? Definition, MirrorGroup? Group, string Name, string Meta, bool Visible)
    {
        public bool IsHeader => Kind != RowKind.Mirror;
        public bool IsLoose => Kind == RowKind.Loose;
        public Thickness Indent => Kind == RowKind.Mirror && Definition?.GroupId != null ? new Thickness(18, 0, 0, 0) : new Thickness(0);
        public override string ToString() => Name; // screen readers / UI automation
    }

    private readonly MiniMirrorService service;
    private readonly DispatcherTimer deleteDisarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private MirrorDefinition? current;
    private MirrorGroup? currentGroup;
    private HashSet<Guid>? knownGroups;
    private bool loading;
    private bool deleteArmed;
    private WindowState? restoreState;
    private HashSet<Guid>? knownIds; // null until the list is first shown
    private static string SelectingStatus => T("Drag around what you want to mirror. R or C switches rectangle / circle; Esc cancels.");

    public MiniMirrorView(MiniMirrorService service)
    {
        this.service = service;
        loading = true; // sliders raise ValueChanged while the XAML loads
        InitializeComponent();
        loading = false;
        Shortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        Shortcut.Changed += Shortcut_Changed;
        GroupShortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        GroupShortcut.Changed += GroupShortcut_Changed;
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
            ? F("Drag around any part of the screen to mirror it. Or press {0} from anywhere (e.g. in a game) — Settings → Mini Mirror.", s)
            : T("Drag around any part of the screen to mirror it.");

    private void ShowPaused()
    {
        PausedBanner.Visibility = service.Paused ? Visibility.Visible : Visibility.Collapsed;
        PausedText.Text = service.PausedReason + T(" Details are in the error log (Settings → General → Files).");
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        service.Resume();
        SetStatus(T("Mirrors started."));
    }

    // ---------------------------------------------------------------- list

    private void RebuildList()
    {
        // mirrors outside any group first (under "Not in a group" once there are groups), then each group's
        var rows = new List<Row>();
        if (service.Groups.Count > 0) rows.Add(new Row(RowKind.Loose, null, null, T("Not in a group"), T("New mirrors start here"), false));
        rows.AddRange(service.MirrorsIn(null).Select(MirrorRow));
        foreach (var g in service.Groups)
        {
            var members = service.MirrorsIn(g).ToList();
            string meta = P(members.Count, "{0} mirror", "{0} mirrors") + (g.Shortcut != null ? " · " + g.Shortcut : "");
            rows.Add(new Row(RowKind.Group, null, g, g.Name, meta, members.Any(m => m.Visible)));
            rows.AddRange(members.Select(MirrorRow));
        }

        // a mirror or group that wasn't here before (new, duplicated or imported) gets selected; otherwise keep the selection
        var mirrorIds = rows.Where(r => r.Definition != null).Select(r => r.Definition!.Id).ToHashSet();
        var groupIds = service.Groups.Select(g => g.Id).ToHashSet();
        var added = knownIds == null ? null : rows.FirstOrDefault(r => r.Definition != null && !knownIds.Contains(r.Definition.Id));
        var addedGroup = knownGroups == null ? null : rows.FirstOrDefault(r => r.Group != null && r.Kind == RowKind.Group && !knownGroups.Contains(r.Group.Id));
        knownIds = mirrorIds;
        knownGroups = groupIds;
        if (added != null) SetStatus(F("Made {0}. Drag it wherever you want it.", added.Name));
        if (addedGroup != null) SetStatus(F("Made {0}. Drag mirrors onto it to put them in it, and give it a shortcut.", addedGroup.Name));

        loading = true;
        MirrorList.ItemsSource = rows;
        MirrorList.SelectedItem = added != null ? rows.First(r => r.Definition == added.Definition)
            : addedGroup != null ? rows.First(r => r.Kind == RowKind.Group && r.Group == addedGroup.Group)
            : current != null ? rows.FirstOrDefault(r => r.Definition?.Id == current.Id)
            : currentGroup != null ? rows.FirstOrDefault(r => r.Kind == RowKind.Group && r.Group?.Id == currentGroup.Id)
            : null;
        loading = false;

        int n = mirrorIds.Count;
        CountText.Text = n == 0 ? "" : $"{n}";
        EmptyText.Visibility = n == 0 && service.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AllButtons.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        Select(MirrorList.SelectedItem as Row);
    }

    private static Row MirrorRow(MirrorDefinition d) => new(RowKind.Mirror, d, null, d.Name, Meta(d), d.Visible);
    private static string Meta(MirrorDefinition d)
    {
        var parts = new List<string> { $"{d.SourceRect.Width} × {d.SourceRect.Height}" };
        if (d.Shape == MirrorShape.Circle) parts.Add(T("circle"));
        if (Math.Abs(d.Zoom - 1) > 0.01) parts.Add(F("zoom {0}", Factor(d.Zoom)));
        parts.Add($"{d.TargetFps} fps");
        if (d.Shortcut != null) parts.Add(d.Shortcut);
        return string.Join(" · ", parts);
    }

    private void MirrorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading) Select(MirrorList.SelectedItem as Row);
    }

    private void Select(Row? row)
    {
        var d = row?.Kind == RowKind.Mirror ? row.Definition : null;
        var g = row?.Kind == RowKind.Group ? row.Group : null;
        if (current != null && current != d) CommitName();
        if (currentGroup != null && currentGroup != g) CommitGroupName();
        current = d;
        currentGroup = g;
        DisarmDelete();
        DisarmDeleteGroup();
        NoSelection.Visibility = d == null && g == null ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = d != null ? Visibility.Visible : Visibility.Collapsed;
        GroupEditor.Visibility = g != null ? Visibility.Visible : Visibility.Collapsed;
        if (d != null) LoadEditor();
        if (g != null) LoadGroupEditor();
    }

    // ---------------------------------------------------------------- editor

    private void LoadEditor()
    {
        if (current is not { } d) return;
        loading = true;
        if (!NameBox.IsKeyboardFocused) NameBox.Text = d.Name;
        var r = d.SourceRect;
        var monitors = MonitorService.GetMonitorsIntersecting(r).Select(m => m.DeviceName.Replace(@"\\.\DISPLAY", T("display "))).ToList();
        RegionText.Text = F("Mirrors {0} × {1} px at {2}, {3}", r.Width, r.Height, r.X, r.Y) + (monitors.Count > 0 ? F(" on {0}", string.Join(T(" and "), monitors)) : T(" (that area isn't on any monitor right now)"));
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
        ShortcutWarning.Text = failed ? F("{0} is already used by another program, so it won't work. Pick another.", s) : "";
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
            SetStatus(T("Click-through is on: use this page or the tray menu to change the mirror, since clicks now go past it."));
    }

    private void Shortcut_Changed()
    {
        if (current == null) return;
        service.SetShortcut(current, Shortcut.Value);
        ShowShortcutWarning();
        SetStatus(Shortcut.Value == null ? F("Removed the shortcut from {0}.", current.Name) : F("{0} now shows and hides {1}.", Shortcut.Value, current.Name));
    }

    /// <summary>Refreshes the selected row's summary line without rebuilding the list (keeps slider drags smooth).</summary>
    private void UpdateRowMeta()
    {
        if (current == null || MirrorList.ItemsSource is not List<Row> rows) return;
        int i = rows.FindIndex(r => r.Kind == RowKind.Mirror && r.Definition == current);
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
        MirrorList.SelectedItem = (MirrorList.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Kind == RowKind.Mirror && r.Definition == copy);
        SetStatus(F("Made {0}, a little below and to the right of the original.", copy.Name));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            DeleteButton.Style = (Style)FindResource("DangerButton");
            DeleteText.Text = T("Click again to delete");
            deleteDisarm.Start();
            return;
        }
        string name = current.Name;
        var doomed = current;
        current = null;
        service.Delete(doomed);
        SetStatus(F("Deleted {0}.", name));
    }

    private void DisarmDelete()
    {
        deleteArmed = false;
        deleteDisarm.Stop();
        DeleteButton.ClearValue(StyleProperty);
        DeleteText.Text = T("Delete");
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e) => service.SetAllVisible(true);

    private void HideAll_Click(object sender, RoutedEventArgs e) => service.SetAllVisible(false);

    // ---------------------------------------------------------------- groups

    private readonly DispatcherTimer groupDeleteDisarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool groupDeleteArmed;

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var g = service.CreateGroup();
        currentGroup = g;
        current = null;
        GroupNameBox.Focus();
        GroupNameBox.SelectAll();
    }

    private void LoadGroupEditor()
    {
        if (currentGroup is not { } g) return;
        loading = true;
        if (!GroupNameBox.IsKeyboardFocused) GroupNameBox.Text = g.Name;
        var members = service.MirrorsIn(g).ToList();
        GroupText.Text = members.Count == 0
            ? T("No mirrors in it yet: drag mirrors from the list onto it.")
            : P(members.Count, "{0} mirror", "{0} mirrors") + ": " + string.Join(", ", members.Select(m => m.Name))
              + " · " + P(members.Count(m => m.Visible), "{0} showing", "{0} showing");
        GroupShortcut.Value = g.Shortcut;
        bool failed = g.Shortcut != null && service.FailedShortcuts.Contains(g.Shortcut, StringComparer.OrdinalIgnoreCase);
        GroupShortcutWarning.Text = failed ? F("{0} is already used by another program, so it won't work. Pick another.", g.Shortcut) : "";
        GroupShortcutWarning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
    }

    private void CommitGroupName()
    {
        if (currentGroup != null && GroupNameBox.Text.Trim() != currentGroup.Name) service.RenameGroup(currentGroup, GroupNameBox.Text);
    }

    private void GroupNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitGroupName();

    private void GroupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitGroupName();
        else if (e.Key == Key.Escape && currentGroup != null) GroupNameBox.Text = currentGroup.Name;
    }

    private void GroupShortcut_Changed()
    {
        if (currentGroup == null) return;
        service.SetGroupShortcut(currentGroup, GroupShortcut.Value);
        SetStatus(GroupShortcut.Value == null ? F("Removed the shortcut from {0}.", currentGroup.Name) : F("{0} now shows and hides {1}.", GroupShortcut.Value, currentGroup.Name));
    }

    private void ShowGroup_Click(object sender, RoutedEventArgs e)
    {
        if (currentGroup == null) return;
        service.SetGroupVisible(currentGroup, true);
        LoadGroupEditor();
    }

    private void HideGroup_Click(object sender, RoutedEventArgs e)
    {
        if (currentGroup == null) return;
        service.SetGroupVisible(currentGroup, false);
        LoadGroupEditor();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (currentGroup == null) return;
        if (!groupDeleteArmed)
        {
            groupDeleteArmed = true;
            DeleteGroupButton.Style = (Style)FindResource("DangerButton");
            DeleteGroupText.Text = T("Click again to delete");
            groupDeleteDisarm.Tick -= OnGroupDisarm;
            groupDeleteDisarm.Tick += OnGroupDisarm;
            groupDeleteDisarm.Start();
            return;
        }
        var doomed = currentGroup;
        currentGroup = null;
        service.DeleteGroup(doomed);
        SetStatus(F("Deleted the group {0}; its mirrors are still here.", doomed.Name));
    }

    private void OnGroupDisarm(object? sender, EventArgs e) => DisarmDeleteGroup();

    private void DisarmDeleteGroup()
    {
        groupDeleteArmed = false;
        groupDeleteDisarm.Stop();
        DeleteGroupButton.ClearValue(StyleProperty);
        DeleteGroupText.Text = T("Delete group");
    }

    // ---------------------------------------------------------------- drag and drop

    private Point dragStart;
    private Row? dragRow;
    private ListBoxItem? dropItem;

    private Row? RowAt(DependencyObject? source) =>
        (source != null ? ItemsControl.ContainerFromElement(MirrorList, source) as ListBoxItem : null)?.DataContext as Row;

    private void MirrorList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(MirrorList);
        dragRow = RowAt(e.OriginalSource as DependencyObject);
    }

    private void MirrorList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragRow is not { Kind: not RowKind.Loose } row) return;
        var p = e.GetPosition(MirrorList);
        if (Math.Abs(p.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragRow = null;
        DragDrop.DoDragDrop(MirrorList, row, DragDropEffects.Move);
        ClearDropMark();
    }

    /// <summary>Where a dragged row would go: (group, before this mirror), or null if nowhere.</summary>
    private (MirrorGroup? Group, MirrorDefinition? Before)? Target(Row dragged, Row? target)
    {
        if (target == null || target == dragged) return null;
        if (dragged.Kind == RowKind.Group)
            // groups go before another group (a mirror in a group stands for its group)
            return target.Kind == RowKind.Group ? (target.Group, null)
                : target.Definition?.GroupId is Guid id ? (service.Groups.FirstOrDefault(g => g.Id == id), null) : null;
        return target.Kind switch
        {
            RowKind.Loose => (null, service.MirrorsIn(null).FirstOrDefault()),
            RowKind.Group => (target.Group, service.MirrorsIn(target.Group).FirstOrDefault()),
            _ => (service.Groups.FirstOrDefault(g => g.Id == target.Definition!.GroupId), target.Definition),
        };
    }

    private void MirrorList_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(Row)) as Row;
        var item = ItemsControl.ContainerFromElement(MirrorList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        bool ok = dragged != null && Target(dragged, item?.DataContext as Row) != null;
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        if (item != dropItem) ClearDropMark();
        if (ok && item != null) { item.Tag = "drop"; dropItem = item; }
        e.Handled = true;
    }

    private void MirrorList_DragLeave(object sender, DragEventArgs e) => ClearDropMark();

    private void ClearDropMark()
    {
        if (dropItem != null) dropItem.Tag = null;
        dropItem = null;
    }

    private void MirrorList_Drop(object sender, DragEventArgs e)
    {
        ClearDropMark();
        if (e.Data.GetData(typeof(Row)) is not Row dragged) return;
        var target = RowAt(e.OriginalSource as DependencyObject);
        if (Target(dragged, target) is not { } where) return;
        if (dragged.Kind == RowKind.Group && dragged.Group != null)
        {
            if (where.Group != null) service.MoveGroup(dragged.Group, where.Group);
            return;
        }
        if (dragged.Definition is not { } d) return;
        service.MoveMirror(d, where.Group, where.Before);
        current = d;
        SetStatus(where.Group != null ? F("Moved {0} into {1}.", d.Name, where.Group.Name) : F("Moved {0} out of its group.", d.Name));
    }

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
            if (StatusText.Text == SelectingStatus) SetStatus(T("Ready."));
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
