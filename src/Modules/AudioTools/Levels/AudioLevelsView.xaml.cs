using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Shared.Audio;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

public partial class AudioLevelsView : UserControl
{
    private readonly DeviceService deviceService = new();
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly AudioLevelsSettings settings;
    private readonly VolumeHotkeyService hotkeys;
    private readonly DispatcherTimer refreshSoon = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // apps start and stop playing without telling anyone: look again every second while the tab is showing
    private readonly DispatcherTimer appTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<DeviceVolumeRow> rows = new();
    private List<AppVolumeRow> appRows = new();
    private string signature = "", appSignature = "";

    internal AudioLevelsView(AudioLevelsSettings settings, VolumeHotkeyService hotkeys)
    {
        this.settings = settings;
        this.hotkeys = hotkeys;
        InitializeComponent();
        StepBox.Text = settings.Step.ToString(CultureInfo.CurrentCulture);
        refreshSoon.Tick += (_, _) => { refreshSoon.Stop(); Refresh(); };
        // raised on a Windows audio thread, often several times in a row when a device connects
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() => { refreshSoon.Stop(); refreshSoon.Start(); });
        appTimer.Tick += (_, _) => RefreshApps();
        hotkeys.Changed += () => { if (IsVisible) RefreshApps(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshApps(); appTimer.Start(); }
            else appTimer.Stop();
        };
    }

    /// <summary>Lists the active playback and recording devices again (keeps the rows if nothing changed).</summary>
    public void Refresh(bool force = false)
    {
        List<DeviceInfo> playback;
        List<CaptureDeviceInfo> recording;
        try
        {
            playback = deviceService.GetDevices();
            recording = deviceService.GetCaptureDevices();
        }
        catch { return; } // audio service busy; the next change notification tries again

        string sig = string.Join("|", playback.Select(d => d.Id + d.IsDefault + d.Name)) + "#" + string.Join("|", recording.Select(d => d.Id + d.IsDefault + d.Name));
        if (!force && sig == signature) return;
        signature = sig;

        foreach (var r in rows) r.Dispose();
        rows = new();
        var play = new List<DeviceVolumeRow>();
        var rec = new List<DeviceVolumeRow>();
        foreach (var d in playback) if (TryRow(d.Id, d.Name, d.IsDefault, false) is { } r) play.Add(r);
        foreach (var d in recording) if (TryRow(d.Id, d.Name, d.IsDefault, true) is { } r) rec.Add(r);
        rows.AddRange(play);
        rows.AddRange(rec);

        PlaybackList.ItemsSource = play.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RecordingList.ItemsSource = rec.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        NoPlayback.Visibility = play.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecording.Visibility = rec.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (force) RefreshApps();
    }

    private DeviceVolumeRow? TryRow(string id, string name, bool isDefault, bool recording)
    {
        try
        {
            var row = new DeviceVolumeRow(deviceService.GetDevice(id), name, isDefault, recording);
            row.HotkeyText = hotkeys.Summary(row.Key);
            return row;
        }
        catch { return null; } // a device without a volume control, or one that just disconnected
    }

    /// <summary>The apps with sound: new rows only when the set of apps changed, otherwise just their volumes.</summary>
    private void RefreshApps()
    {
        List<AppAudio> apps;
        try { apps = AppVolumes.List(enumerator); }
        catch { return; }
        string sig = string.Join("|", apps.Select(a => a.Key));
        if (sig != appSignature)
        {
            appSignature = sig;
            appRows = apps.Select(a => new AppVolumeRow(a) { HotkeyText = hotkeys.Summary(a.Key) }).ToList();
            AppList.ItemsSource = appRows;
            NoApps.Visibility = appRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        for (int i = 0; i < apps.Count && i < appRows.Count; i++)
            if (Mouse.LeftButton != MouseButtonState.Pressed) appRows[i].Refresh(apps[i]); // not mid-drag
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh(force: true);

    /// <summary>The volume number: one click sets it to 100.</summary>
    private void Max_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VolumeRow row) row.Volume = 100;
    }

    /// <summary>The name: its shortcuts.</summary>
    private void Name_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VolumeRow row)
            new HotkeyWindow(settings, hotkeys, row) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider s) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 2;
        s.Value = Math.Clamp(s.Value + Math.Sign(e.Delta) * step, s.Minimum, s.Maximum);
        e.Handled = true;
    }

    private void StepBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyStep();
    }

    private void StepBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyStep();

    private void ApplyStep()
    {
        if (int.TryParse(StepBox.Text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.CurrentCulture, out int step) && step is >= 1 and <= 100)
        {
            settings.Step = step;
            settings.Save();
        }
        StepBox.Text = settings.Step.ToString(CultureInfo.CurrentCulture);
    }

    public void Shutdown()
    {
        appTimer.Stop();
        foreach (var r in rows) r.Dispose();
        rows.Clear();
        deviceService.Dispose();
        enumerator.Dispose();
    }
}
