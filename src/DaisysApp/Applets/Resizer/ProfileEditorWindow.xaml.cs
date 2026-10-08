using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Resizer;

/// <summary>Creates or edits a profile. Saving and deleting go through <see cref="ResizerService"/>.</summary>
public partial class ProfileEditorWindow : Window
{
    private sealed record GroupChoice(Guid? Uuid, string Name);

    private readonly ResizerService service;
    private readonly ResizeProfile profile; // a copy: nothing changes until Save
    private readonly bool isNew;
    private readonly DispatcherTimer deleteConfirm = new() { Interval = TimeSpan.FromSeconds(3) };
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
        deleteConfirm.Tick += (_, _) => { deleteConfirm.Stop(); DeleteButton.Content = T("Delete"); };

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
        Shortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        Shortcut.Value = profile.Shortcut;
        Shortcut.Changed += UpdateShared;
        loading = false;
        UpdateEnabled();
        UpdateShared();
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
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0 && ProcessBox.Text.Trim().Length > 0;
        TestButton.IsEnabled = ProcessBox.Text.Trim().Length > 0;
    }

    private void UpdateShared()
    {
        var others = service.SharedWith(Shortcut.Value, profile.Uuid);
        SharedText.Text = others.Count > 0
            ? F("Also used by: {0}. Pressing it applies whichever of these programs are running.", string.Join(", ", others))
            : T("Applies this profile from anywhere, even with the app in the tray. Needs Ctrl, Alt or Shift.");
        if (Shortcut.Value != null && service.FailedShortcuts.Contains(Shortcut.Value, StringComparer.OrdinalIgnoreCase))
            SharedText.Text = T("Another program already uses this shortcut, so Windows won't let the Resizer have it. Choose another.");
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
        p.Shortcut = Shortcut.Value;
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

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!deleteConfirm.IsEnabled)
        {
            DeleteButton.Content = T("Click to confirm");
            deleteConfirm.Start();
            return;
        }
        deleteConfirm.Stop();
        service.DeleteProfile(profile);
        DialogResult = true;
    }
}
