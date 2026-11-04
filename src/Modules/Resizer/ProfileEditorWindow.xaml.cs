using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Theming;
using DaisysApp.Shared;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.Video.Resizer;

/// <summary>Creates or edits a profile. Saving and deleting go through <see cref="ResizerService"/>.</summary>
public partial class ProfileEditorWindow : Window
{
    private sealed record GroupChoice(Guid? Uuid, string Name);
    private sealed record LayoutLine(string Name, string Place);

    private readonly ResizerService service;
    private readonly ResizeProfile profile; // a copy: nothing changes until Save (but its shortcut, Core's button)
    private readonly bool isNew;
    private List<LayoutWindow>? layout; // the Desktop Layout's windows (taken in this editor, or the profile's)
    private DateTime? layoutTaken;
    private bool loading = true;

    public ProfileEditorWindow(ResizerService service, ResizeProfile? existing, Guid? group = null)
    {
        this.service = service;
        isNew = existing == null;
        profile = existing?.Clone() ?? new ResizeProfile { GroupUuid = group };
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        Title = isNew ? T("New profile") : F("Profile — {0}", profile.Name);
        HeaderText.Text = isNew ? T("New profile") : profile.Name;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Arm(DeleteButton, Delete);

        var groups = new List<GroupChoice> { new(null, T("None (top level)")) };
        groups.AddRange(service.Data.Groups.OrderBy(g => g.Order).Select(g => new GroupChoice(g.Uuid, g.Name)));
        GroupBox.ItemsSource = groups;
        GroupBox.SelectedItem = groups.FirstOrDefault(g => g.Uuid == profile.GroupUuid) ?? groups[0];

        NameBox.Text = isNew ? "" : profile.Name;
        LoadProcesses();
        ProcessBox.Text = profile.ProcessName;
        PresetBox.SelectedIndex = 0;
        WidthBox.Text = profile.WindowWidth?.ToString(CultureInfo.CurrentCulture) ?? "";
        HeightBox.Text = profile.WindowHeight?.ToString(CultureInfo.CurrentCulture) ?? "";
        XBox.Text = profile.WindowPosX.ToString(CultureInfo.CurrentCulture);
        YBox.Text = profile.WindowPosY.ToString(CultureInfo.CurrentCulture);
        BordersBox.IsChecked = profile.RemoveBorders;
        TitlebarBox.IsChecked = profile.ShiftTitlebarOffscreen;
        AutoBox.IsChecked = profile.Auto;
        DelayBox.Text = profile.Delay.ToString(CultureInfo.CurrentCulture);
        layout = profile.Layout;
        layoutTaken = profile.LayoutTaken;
        SourceBox.SelectedIndex = profile.IsLayout ? 1 : 0;
        ShowLayout();
        if (!isNew) ShortcutHost.Content = HotkeyWindow.Button(T("Shortcut"), () => service.ProfileHotkey(profile.Uuid));
        SharedText.Text = isNew ? T("Once it's saved: the joystick button on its row.") : T("Applies this profile from anywhere, even with the app in the tray.");
        loading = false;
        UpdateEnabled();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void LoadProcesses()
    {
        string text = ProcessBox.Text;
        try { ProcessBox.ItemsSource = ProcessFinder.List(ShowAllBox.IsChecked == true); }
        catch { ProcessBox.ItemsSource = null; }
        ProcessBox.Text = text;
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
    {
        if (!loading) LoadProcesses();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading) UpdateEnabled();
    }

    private void Borders_Changed(object sender, RoutedEventArgs e) => UpdateEnabled();
    private void Auto_Changed(object sender, RoutedEventArgs e) => UpdateEnabled();

    private void UpdateEnabled()
    {
        if (loading) return;
        TitlebarBox.IsEnabled = BordersBox.IsChecked == true;
        DelayRow.IsEnabled = AutoBox.IsChecked == true;
        bool isLayout = IsLayout;
        ProgramPanel.Visibility = WindowCard.Visibility = AutoPanel.Visibility = isLayout ? Visibility.Collapsed : Visibility.Visible;
        LayoutCard.Visibility = isLayout ? Visibility.Visible : Visibility.Collapsed;
        bool ready = isLayout ? layout is { Count: > 0 } : ProcessBox.Text.Trim().Length > 0;
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0 && ready;
        TestButton.IsEnabled = ready;
    }

    private bool IsLayout => SourceBox.SelectedIndex == 1;

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        if (IsLayout && layout == null) TakeSnapshot();
        UpdateEnabled();
    }

    private void TakeSnapshot_Click(object sender, RoutedEventArgs e)
    {
        TakeSnapshot();
        ShowStatus(P(layout!.Count, "Took {0} window's place.", "Took {0} windows' places."), false);
        UpdateEnabled();
    }

    private void TakeSnapshot()
    {
        layout = DesktopLayout.Capture();
        layoutTaken = DateTime.Now;
        ShowLayout();
    }

    /// <summary>The layout's windows, a line each: "chrome.exe · Inbox" and "1920 × 1080 at −1920, 0".</summary>
    private void ShowLayout()
    {
        string Px(int v) => v.ToString(CultureInfo.CurrentCulture).Replace('-', '−');
        LayoutList.ItemsSource = layout?.Select(w => new LayoutLine($"{w.Process}  ·  {w.Title}",
            (w.Maximized ? T("maximized") + ", " : w.Minimized ? T("minimized") + ", " : "") +
            (w.Minimized
                ? F("{0} × {1} at {2}, {3}", Px(w.NormalRight - w.NormalLeft), Px(w.NormalBottom - w.NormalTop), Px(w.NormalLeft), Px(w.NormalTop))
                : F("{0} × {1} at {2}, {3}", Px(w.Width), Px(w.Height), Px(w.X), Px(w.Y))))).ToList();
        LayoutSummary.Text = layout == null ? "" : P(layout.Count, "{0} window", "{0} windows") +
            (layoutTaken is DateTime t ? "  ·  " + F("taken {0:g}", t) : "");
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || PresetBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var v = tag.Split(',');
        WidthBox.Text = v[0];
        HeightBox.Text = v[1];
        XBox.Text = v[2];
        YBox.Text = v[3];
        PresetBox.SelectedIndex = 0;
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e)
    {
        string process = ProcessBox.Text.Trim();
        if (process.Length == 0) { ShowStatus(T("Choose the program first."), true); return; }
        if (WindowMover.CurrentRect(process) is not { } r)
        {
            ShowStatus(F("{0} isn't running, or has no visible window.", process), true);
            return;
        }
        WidthBox.Text = r.Width.ToString(CultureInfo.CurrentCulture);
        HeightBox.Text = r.Height.ToString(CultureInfo.CurrentCulture);
        XBox.Text = r.X.ToString(CultureInfo.CurrentCulture);
        YBox.Text = r.Y.ToString(CultureInfo.CurrentCulture);
        ShowStatus(F("Filled in from {0}'s window as it is now.", process), false);
    }

    /// <summary>Reads the form into a profile copy, or shows what's wrong and returns null.</summary>
    private ResizeProfile? Read()
    {
        int? Optional(TextBox box, string what, out bool ok)
        {
            ok = true;
            string t = box.Text.Trim().Replace('−', '-');
            if (t.Length == 0) return null;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out int v) && v > 0) return v;
            ok = false;
            ShowStatus(F("{0} must be a whole number of pixels, or empty to keep the window's size.", what), true);
            return null;
        }
        bool Required(TextBox box, string what, out int v)
        {
            string t = box.Text.Trim().Replace('−', '-');
            if (t.Length == 0) { v = 0; return true; }
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return true;
            ShowStatus(F("{0} must be a whole number (it can be negative).", what), true);
            return false;
        }

        var p = profile.Clone();
        p.Name = NameBox.Text.Trim();
        if (IsLayout)
        {
            p.GroupUuid = (GroupBox.SelectedItem as GroupChoice)?.Uuid;
            p.ProcessName = "";
            p.Layout = layout;
            p.LayoutTaken = layoutTaken;
            p.Auto = false;
            p.Shortcut = service.Data.Profiles.FirstOrDefault(x => x.Uuid == p.Uuid)?.Shortcut;
            return p;
        }
        p.Layout = null;
        p.LayoutTaken = null;
        p.ProcessName = ProcessBox.Text.Trim();
        p.GroupUuid = (GroupBox.SelectedItem as GroupChoice)?.Uuid;
        p.WindowWidth = Optional(WidthBox, T("Width"), out bool okW);
        if (!okW) return null;
        p.WindowHeight = Optional(HeightBox, T("Height"), out bool okH);
        if (!okH) return null;
        if (!Required(XBox, T("Position X"), out int x) || !Required(YBox, T("Position Y"), out int y)) return null;
        p.WindowPosX = x;
        p.WindowPosY = y;
        p.RemoveBorders = BordersBox.IsChecked == true;
        p.ShiftTitlebarOffscreen = p.RemoveBorders && TitlebarBox.IsChecked == true;
        p.Auto = AutoBox.IsChecked == true;
        if (!Required(DelayBox, T("The wait"), out int delay) || delay < 0)
        {
            if (delay < 0) ShowStatus(T("The wait can't be negative."), true);
            return null;
        }
        p.Delay = delay;
        p.Shortcut = service.Data.Profiles.FirstOrDefault(x => x.Uuid == p.Uuid)?.Shortcut;
        return p;
    }

    private void ShowStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        if (error) StatusText.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
        else StatusText.ClearValue(ForegroundProperty);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Read() is not { } p) return;
        TestButton.IsEnabled = false;
        ShowStatus(T("Applying…"), false);
        try
        {
            if (p.IsLayout)
            {
                var r = await DesktopLayout.ApplyAsync(p.Name, p.Layout!);
                ShowStatus(ResizerService.Describe(r, p), r.Moved == 0 || r.Denied > 0);
                return;
            }
            var result = await WindowMover.ApplyAsync(p, retry: false, monitor: false);
            ShowStatus(WindowMover.Describe(result, p), result != ApplyResult.Applied);
        }
        finally { TestButton.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Read() is not { } p) return;
        service.SaveProfile(p);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete()
    {
        service.DeleteProfile(profile);
        DialogResult = true;
    }
}
