using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Resizer;

/// <summary>One line of the list: a group, or a profile (indented when it's in a group).</summary>
public sealed class ResizerRow
{
    public required object Item { get; init; }
    public bool IsGroup => Item is ResizeGroup;
    public required string Title { get; init; }
    public required string Meta { get; init; }
    public bool IsRunning { get; init; }
    public Thickness Indent { get; init; }
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }
    public string ChevronGlyph { get; init; } = "";
    public string DotTip => IsRunning ? T("Running now") : T("Not running");
    public string ApplyTip => IsGroup ? T("Apply every profile in this group whose program is running") : T("Apply now");
}

public partial class ResizerView : UserControl
{
    private readonly ResizerService service;
    private bool updating;

    public ResizerView(ResizerService service)
    {
        this.service = service;
        InitializeComponent();
        service.Changed += Rebuild;
        service.RunningChanged += Rebuild;
        service.Status += ShowStatus;
        Rebuild();
    }

    private static string Px(int v) => v.ToString(CultureInfo.CurrentCulture).Replace('-', '−');

    /// <summary>"AUTOMATIC · 7680 × 1440 at −2560, 0 · Ctrl+Alt+F1"</summary>
    private static string Meta(ResizeProfile p)
    {
        var parts = new List<string> { p.Auto ? (p.Delay > 0 ? F("Automatic (after {0} ms)", p.Delay) : T("Automatic")) : T("Manual"), p.ProcessName };
        string size = p.WindowWidth is int w && p.WindowHeight is int h ? F("{0} × {1} at ", Px(w), Px(h)) : T("keep size, move to ");
        parts.Add($"{size}{Px(p.WindowPosX)}, {Px(p.WindowPosY)}");
        if (p.RemoveBorders) parts.Add(p.ShiftTitlebarOffscreen ? T("no borders or title bar") : T("no borders"));
        if (!string.IsNullOrWhiteSpace(p.Shortcut)) parts.Add(p.Shortcut!);
        return string.Join("  ·  ", parts);
    }

    private void Rebuild()
    {
        var rows = new List<ResizerRow>();
        var top = service.TopLevel();
        for (int i = 0; i < top.Count; i++)
        {
            if (top[i] is ResizeGroup g)
            {
                var members = service.Members(g);
                int running = members.Count(service.IsRunning);
                string meta = P(members.Count, "{0} profile", "{0} profiles") +
                              (running > 0 ? F(", {0} running", running) : "") +
                              (string.IsNullOrWhiteSpace(g.Shortcut) ? "" : $"  ·  {g.Shortcut}");
                rows.Add(new ResizerRow
                {
                    Item = g, Title = g.Name, Meta = meta, CanMoveUp = i > 0, CanMoveDown = i < top.Count - 1,
                    ChevronGlyph = g.Collapsed ? "" : "",
                });
                if (g.Collapsed) continue;
                for (int m = 0; m < members.Count; m++)
                    rows.Add(new ResizerRow
                    {
                        Item = members[m], Title = members[m].Name, Meta = Meta(members[m]), IsRunning = service.IsRunning(members[m]),
                        Indent = new Thickness(36, 0, 0, 0), CanMoveUp = m > 0, CanMoveDown = m < members.Count - 1,
                    });
            }
            else
            {
                var p = (ResizeProfile)top[i];
                rows.Add(new ResizerRow
                {
                    Item = p, Title = p.Name, Meta = Meta(p), IsRunning = service.IsRunning(p),
                    CanMoveUp = i > 0, CanMoveDown = i < top.Count - 1,
                });
            }
        }
        RowList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int profiles = service.Data.Profiles.Count;
        CountText.Text = profiles == 0 ? "" : P(profiles, "{0} profile", "{0} profiles") +
                         (service.Data.Groups.Count > 0 ? P(service.Data.Groups.Count, " in {0} group", " in {0} groups") : "");

        updating = true;
        WatcherBox.IsChecked = service.Data.ProcessWatcherEnabled;
        updating = false;

        HotkeyWarning.Visibility = service.FailedShortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyWarningText.Text = T("Windows wouldn't register ") + string.Join(", ", service.FailedShortcuts) +
                                 T(": another program is using ") + (service.FailedShortcuts.Count == 1 ? T("it") : T("them")) +
                                 T(" (or Resize Rabbit is still running). Choose a different shortcut, or close the other program.");
    }

    private void ShowStatus(string text, bool error)
    {
        StatusText.Text = text;
        if (error) StatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else StatusText.ClearValue(TextBlock.ForegroundProperty);
    }

    private static ResizerRow RowOf(object sender) => (ResizerRow)((FrameworkElement)sender).DataContext;

    private Window? Owner => Window.GetWindow(this);

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        ShowStatus(F("Applying {0}…", row.Title), false);
        if (row.Item is ResizeGroup g) await service.ApplyGroupAsync(g);
        else await service.ApplyAsync((ResizeProfile)row.Item);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => service.Move(RowOf(sender).Item, -1);
    private void Down_Click(object sender, RoutedEventArgs e) => service.Move(RowOf(sender).Item, +1);

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender).Item is ResizeGroup g) service.SetCollapsed(g, !g.Collapsed);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row.Item is ResizeGroup g) new GroupEditorWindow(service, g) { Owner = Owner }.ShowDialog();
        else new ProfileEditorWindow(service, (ResizeProfile)row.Item) { Owner = Owner }.ShowDialog();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (new ProfileEditorWindow(service, null) { Owner = Owner }.ShowDialog() == true)
            ShowStatus(T("Profile saved."), false);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (new GroupEditorWindow(service, null) { Owner = Owner }.ShowDialog() == true)
            ShowStatus(T("Group saved. Open a profile and choose this group to add it."), false);
    }

    private void WatcherBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        service.SetWatcher(WatcherBox.IsChecked == true);
        ShowStatus(service.Data.ProcessWatcherEnabled
            ? T("Process watcher on: Automatic profiles are applied when their program starts.")
            : T("Process watcher off."), false);
    }
}
