using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Shared.Audio;

namespace DaisysApp.Applets.AudioLevels;

public partial class AudioLevelsView : UserControl
{
    private readonly DeviceService deviceService = new();
    private readonly DispatcherTimer refreshSoon = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private List<DeviceVolumeRow> rows = new();
    private string signature = "";

    public AudioLevelsView()
    {
        InitializeComponent();
        refreshSoon.Tick += (_, _) => { refreshSoon.Stop(); Refresh(); };
        // raised on a Windows audio thread, often several times in a row when a device connects
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() => { refreshSoon.Stop(); refreshSoon.Start(); });
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
        foreach (var d in playback) if (TryRow(d.Id, d.Name, d.IsDefault) is { } r) play.Add(r);
        foreach (var d in recording) if (TryRow(d.Id, d.Name, d.IsDefault) is { } r) rec.Add(r);
        rows.AddRange(play);
        rows.AddRange(rec);

        PlaybackList.ItemsSource = play.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RecordingList.ItemsSource = rec.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        NoPlayback.Visibility = play.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecording.Visibility = rec.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private DeviceVolumeRow? TryRow(string id, string name, bool isDefault)
    {
        try { return new DeviceVolumeRow(deviceService.GetDevice(id), name, isDefault); }
        catch { return null; } // a device without a volume control, or one that just disconnected
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh(force: true);

    private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider s) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 2;
        s.Value = Math.Clamp(s.Value + Math.Sign(e.Delta) * step, s.Minimum, s.Maximum);
        e.Handled = true;
    }

    public void Shutdown()
    {
        foreach (var r in rows) r.Dispose();
        rows.Clear();
        deviceService.Dispose();
    }
}
