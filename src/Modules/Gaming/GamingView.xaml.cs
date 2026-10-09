using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.Gaming;

/// <summary>The Gaming tab: what's running now, the overlay, recording, and each game's history.</summary>
public partial class GamingView : UserControl
{
    private readonly GamingService service;
    private readonly DispatcherTimer timer;
    private bool loading = true;
    private List<GameSession> sessions = new();

    internal GamingView(GamingService service)
    {
        this.service = service;
        InitializeComponent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => RefreshLive();
        IsVisibleChanged += (_, _) =>
        {
            service.TabVisible = IsVisible;
            if (IsVisible) { timer.Start(); Refresh(); LoadHistory(); }
            else timer.Stop();
        };
        service.Changed += () => { if (IsVisible) Refresh(); };
        service.SessionLogged += () => { if (IsVisible) LoadHistory(); };
        LoadControls();
        loading = false;
    }

    private GamingSettings S => service.Settings;

    private void LoadControls()
    {
        loading = true;
        OverlayBox.IsChecked = S.OverlayVisible;
        ModeBox.SelectedIndex = (int)S.Mode;
        LockBox.IsChecked = S.Locked;
        SourceMonitor.IsChecked = S.Source == CaptureSource.Monitor;
        SourceRegion.IsChecked = S.Source == CaptureSource.Region;
        SourceApp.IsChecked = S.Source == CaptureSource.App;
        FillMonitors();
        FillApps();
        FillProfiles();
        ShowRegion();
        ShowSourceRows();
        loading = false;
    }

    /// <summary>After the settings page changed something shown here.</summary>
    internal void SettingsChanged()
    {
        LoadControls();
        Refresh();
    }

    // ---------------------------------------------------------------- live

    private void Refresh()
    {
        loading = true;
        OverlayBox.IsChecked = S.OverlayVisible;
        ModeBox.SelectedIndex = (int)S.Mode;
        LockBox.IsChecked = S.Locked;
        loading = false;

        var status = service.Frames.Status;
        PermissionPanel.Visibility = status is FrameMonitor.State.NoPermission or FrameMonitor.State.Failed ? Visibility.Visible : Visibility.Collapsed;
        if (status == FrameMonitor.State.NoPermission)
        {
            PermissionText.Text = S.PermissionPending
                ? T("Almost there: sign out of Windows and back in (or restart), then click Try again. Windows only gives the permission to new sign-ins.")
                : T("To count frames, Windows needs to let Daisy's App read its graphics events (the way PresentMon and the NVIDIA app's FrameView do). That takes administrator rights, or membership of the \"Performance Log Users\" group. Allow adds you to that group (Windows asks first), once; after that the app never needs to run as administrator.");
            AllowButton.Visibility = S.PermissionPending ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (status == FrameMonitor.State.Failed)
        {
            PermissionText.Text = F("Frame counting couldn't start: {0}", service.Frames.Error ?? "");
            AllowButton.Visibility = Visibility.Collapsed;
        }

        string hotkeys = string.Join("   ", new[]
        {
            S.OverlayHotkey is { Length: > 0 } o ? F("{0} shows or hides it", o) : null,
            S.ModeHotkey is { Length: > 0 } m ? F("{0} changes the mode", m) : null,
        }.Where(x => x != null));
        OverlayHint.Text = (S.Locked
            ? T("Locked: clicks go through it to the game. Untick Lock in place to move or resize it.")
            : T("Drag the overlay to move it; drag a corner to make it bigger or smaller. Lock it so clicks go through to the game."))
            + (hotkeys.Length > 0 ? "  " + hotkeys + "." : "");
        if (status == FrameMonitor.State.Off && !S.OverlayVisible && !S.LogHistory)
            OverlayHint.Text += " " + T("Frame counting is off while the overlay is hidden and the history is off.");

        RefreshRecording();
        RefreshLive();
    }

    private void RefreshLive()
    {
        var game = service.Game;
        GameText.Text = service.Frames.Status != FrameMonitor.State.Running ? T("Frame counting is off.")
            : game != null ? F("Playing: {0}", game.Name) : T("No game in front. Games are recognised when they show frames full screen.");
        uint pid = game?.Pid ?? 0;
        var frames = new List<long>();
        if (pid != 0) service.Frames.CopyFrames(pid, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 31, frames);
        var all = FrameStats.From(frames, true);
        var last = FrameStats.None;
        if (frames.Count > 1)
        {
            long cutoff = frames[^1] - Stopwatch.Frequency;
            int first = Math.Max(0, frames.FindIndex(t => t >= cutoff) - 1);
            last = FrameStats.From(frames.GetRange(first, frames.Count - first), false);
        }
        FpsText.Text = double.IsFinite(last.Fps) ? last.Fps.ToString("0") : "–";
        LowText.Text = double.IsFinite(all.Low1Fps) ? all.Low1Fps.ToString("0") : "–";
        FrametimeText.Text = double.IsFinite(last.FrametimeMs) ? last.FrametimeMs.ToString("0.0") + " ms" : "–";
        var hw = service.Hardware;
        GpuText.Text = double.IsFinite(hw.GpuPercent) ? hw.GpuPercent.ToString("0") + "%" : "–";
        VramText.Text = double.IsFinite(hw.VramUsedMB) ? F("{0:0.0} GB", hw.VramUsedMB / 1024) : "–";
        if (service.Recorder.IsRecording) RefreshRecording();
    }

    // ---------------------------------------------------------------- overlay

    private void Overlay_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.OverlayVisible = OverlayBox.IsChecked == true;
        service.ApplyOverlaySettings();
        Refresh();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ModeBox.SelectedIndex < 0) return;
        service.SetMode((OverlayMode)ModeBox.SelectedIndex);
    }

    private void Lock_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.Locked = LockBox.IsChecked == true;
        service.ApplyOverlaySettings();
        Refresh();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => service.ResetOverlayPosition();

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        string? error = Permission.AddToPerformanceLogUsers();
        if (error != null) { PermissionText.Text = error; return; }
        S.PermissionPending = true;
        S.Save();
        Refresh();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => service.RetryFrameMonitor();

    // ---------------------------------------------------------------- recording

    private void ShowSourceRows()
    {
        MonitorRow.Visibility = S.Source == CaptureSource.Monitor ? Visibility.Visible : Visibility.Collapsed;
        RegionRow.Visibility = S.Source == CaptureSource.Region ? Visibility.Visible : Visibility.Collapsed;
        AppRow.Visibility = S.Source == CaptureSource.App ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.Source = SourceRegion.IsChecked == true ? CaptureSource.Region : SourceApp.IsChecked == true ? CaptureSource.App : CaptureSource.Monitor;
        S.Save();
        ShowSourceRows();
    }

    private void FillMonitors()
    {
        var monitors = Native.Monitors();
        MonitorBox.Items.Clear();
        foreach (var m in monitors) MonitorBox.Items.Add(new ComboBoxItem { Content = m.Label, Tag = m.DeviceName });
        int index = monitors.FindIndex(m => m.DeviceName == S.Monitor);
        if (index < 0) index = Math.Max(0, monitors.FindIndex(m => m.Primary));
        MonitorBox.SelectedIndex = monitors.Count > 0 ? index : -1;
    }

    private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || MonitorBox.SelectedItem is not ComboBoxItem item) return;
        S.Monitor = (string)item.Tag;
        S.Save();
    }

    private void ShowRegion() =>
        RegionText.Text = S.RegionWidth >= 16 ? new PixelRect(S.RegionX, S.RegionY, S.RegionWidth, S.RegionHeight).ToString() : T("None selected");

    private void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        RegionSelector.Pick(r =>
        {
            if (r is not { } rect) return;
            S.RegionX = rect.X; S.RegionY = rect.Y; S.RegionWidth = rect.Width; S.RegionHeight = rect.Height;
            S.Save();
            ShowRegion();
        });
    }

    /// <summary>"The game in front", then the games the app knows, then other open programs.</summary>
    private void FillApps()
    {
        bool was = loading;
        loading = true;
        AppBox.Items.Clear();
        AppBox.Items.Add(new ComboBoxItem { Content = T("The game in front when recording starts"), Tag = "" });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<string, GameEntry>> games;
        lock (S.Games) games = S.Games.Where(g => g.Value.Track).OrderBy(g => g.Value.Name).ToList();
        foreach (var g in games)
        {
            AppBox.Items.Add(new ComboBoxItem { Content = F("{0} ({1})", g.Value.Name, g.Key), Tag = g.Key });
            seen.Add(g.Key);
        }
        foreach (var (_, pid, title) in Native.TopLevelWindows())
        {
            string exe = Native.ProcessExe(pid).Exe;
            if (exe.Length == 0 || exe == "daisysapp.exe" || !seen.Add(exe)) continue;
            AppBox.Items.Add(new ComboBoxItem { Content = F("{0} ({1})", title.Length > 50 ? title[..50] + "…" : title, exe), Tag = exe });
        }
        if (S.AppExe.Length > 0 && !seen.Contains(S.AppExe))
            AppBox.Items.Add(new ComboBoxItem { Content = S.FriendlyName(S.AppExe) + " (" + S.AppExe + ")", Tag = S.AppExe });
        AppBox.SelectedItem = AppBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, S.AppExe, StringComparison.OrdinalIgnoreCase)) ?? AppBox.Items[0];
        loading = was;
    }

    private void AppBox_DropDownOpened(object? sender, EventArgs e) => FillApps();

    private void App_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || AppBox.SelectedItem is not ComboBoxItem item) return;
        S.AppExe = (string)item.Tag;
        S.Save();
    }

    private void FillProfiles()
    {
        ProfileBox.Items.Clear();
        foreach (var p in S.Profiles) ProfileBox.Items.Add(new ComboBoxItem { Content = ProfileName(p.Name), Tag = p.Name });
        ProfileBox.SelectedItem = ProfileBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == S.ActiveProfile.Name);
        ProfileText.Text = Describe(S.ActiveProfile);
    }

    /// <summary>The built-in profiles' names translate; the user's own are as typed.</summary>
    internal static string ProfileName(string name) => name switch { "High" => T("High"), "Medium" => T("Medium"), "Low" => T("Low"), _ => name };

    internal static string Describe(RecordingProfile p) =>
        F("{0}, {1} fps, {2}, {3}", Encoders.Label(p.Codec), p.Fps, p.Height > 0 ? p.Height + "p" : T("full size"),
            p.RateControl == RateControl.Cbr ? F("{0:0} Mbit/s", p.BitrateMbps) : F("{0:0}–{1:0} Mbit/s", p.BitrateMbps, p.MaxBitrateMbps));

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ProfileBox.SelectedItem is not ComboBoxItem item) return;
        S.Profile = (string)item.Tag;
        S.Save();
        ProfileText.Text = Describe(S.ActiveProfile);
    }

    private void Record_Click(object sender, RoutedEventArgs e) => service.ToggleRecording();

    private void RefreshRecording()
    {
        var r = service.Recorder;
        RecordButton.Content = r.IsRecording ? T("Stop recording") : service.Starting ? T("Starting…") : T("Start recording");
        RecordButton.Style = (Style)FindResource(r.IsRecording ? "DangerButton" : "AccentButton");
        RecordButton.IsEnabled = !service.Starting;
        if (r.IsRecording)
        {
            var elapsed = DateTime.Now - r.StartedAt;
            long size = 0;
            try { if (r.FilePath != null) size = new FileInfo(r.FilePath).Length; } catch { }
            RecordStatus.Text = F("● {0} · {1:0} MB · {2}", elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"), size / 1048576.0, r.Description)
                + (r.FramesDropped > 0 ? " · " + P(r.FramesDropped, "{0} frame dropped", "{0} frames dropped") : "");
            RecordStatus.Foreground = (Brush)FindResource("DangerBrush");
        }
        else
        {
            RecordStatus.Text = service.LastRecordingFile is { } file && service.LastRecordingError == null ? F("Saved {0}", Path.GetFileName(file)) : "";
            RecordStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }
        RecordError.Text = service.LastRecordingError ?? "";
        RecordError.Visibility = service.LastRecordingError != null ? Visibility.Visible : Visibility.Collapsed;

        var encoders = Encoders.Available();
        EncoderText.Text = (encoders.Count == 0
            ? T("This PC's graphics card has no video encoder Windows can use, so recording isn't available.")
            : F("Encoded by the graphics card: {0}.", string.Join(", ", encoders.Select(kv => Encoders.Label(kv.Key) + " (" + kv.Value + ")"))))
            + (S.RecordHotkey is { Length: > 0 } h ? " " + F("{0} starts and stops recording.", h) : "");
    }

    /// <summary>Settings → Gaming, at the Recording card.</summary>
    private void RecordingSettings_Click(object sender, RoutedEventArgs e) => DaisysApp.Shell.AppNavigation.OpenSettings("Gaming", "RecordingCard");

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(S.RecordingFolder);
            if (service.LastRecordingFile is { } f && File.Exists(f)) Process.Start("explorer.exe", $"/select,\"{f}\"");
            else Process.Start("explorer.exe", $"\"{S.RecordingFolder}\"");
        }
        catch { }
    }

    // ---------------------------------------------------------------- history

    private void LoadHistory()
    {
        sessions = GameLog.Sessions();
        var games = sessions.GroupBy(s => s.Exe, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Exe: g.Key, Name: S.FriendlyName(g.Key), Last: g.Max(x => x.End)))
            .OrderByDescending(g => g.Last).ToList();
        string? selected = (HistoryGameBox.SelectedItem as ComboBoxItem)?.Tag as string;
        loading = true;
        HistoryGameBox.Items.Clear();
        foreach (var g in games) HistoryGameBox.Items.Add(new ComboBoxItem { Content = g.Name, Tag = g.Exe });
        HistoryGameBox.SelectedItem = HistoryGameBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? HistoryGameBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
        loading = false;
        HistoryEmpty.Text = games.Count == 0
            ? (S.LogHistory ? T("Nothing yet: each game's frame rates are logged while you play, and its sessions appear here.") : T("The history is off (Settings → Gaming)."))
            : "";
        ShowSessions();
    }

    private void HistoryGame_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!loading) ShowSessions();
    }

    private void ShowSessions()
    {
        string? exe = (HistoryGameBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var list = exe == null ? new List<GameSession>() : sessions.Where(s => string.Equals(s.Exe, exe, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Start).ToList();
        SessionsGraphTitle.Text = list.Count > 0 ? T("Each session's average and 1% low frame rate") : "";
        if (list.Count > 0)
            SessionsGraph.Show(new[]
            {
                new HistoryGraph.Series(T("Average FPS"), Color.FromRgb(0x4C, 0xD9, 0x64), list.Select(s => s.AvgFps).ToArray()),
                new HistoryGraph.Series(T("1% low"), Color.FromRgb(0xFF, 0x8A, 0x3D), list.Select(s => s.Low1Fps).ToArray()),
            }, list[0].Start.ToString("d"), list[^1].Start.ToString("d"));
        else SessionsGraph.Clear();

        SessionsList.Items.Clear();
        foreach (var s in Enumerable.Reverse(list))
            SessionsList.Items.Add(new ListBoxItem
            {
                Tag = s,
                Content = F("{0:g} · {1}", s.Start, Length(s.Seconds)) + " · "
                    + F("{0:0} FPS average, {1:0} 1% low, {2:0} 0.1% low", s.AvgFps, s.Low1Fps, s.Low01Fps)
                    + (double.IsFinite(s.AvgGpuPercent) ? " · " + F("GPU {0:0}%, up to {1:0}°C", s.AvgGpuPercent, s.MaxGpuTempC) : ""),
            });
        if (SessionsList.Items.Count > 0) SessionsList.SelectedIndex = 0;
        else { SessionGraph.Clear(); SessionGraphTitle.Text = ""; }
    }

    private static string Length(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? F("{0} h {1} min", (int)t.TotalHours, t.Minutes) : F("{0} min", Math.Max(1, (int)Math.Round(t.TotalMinutes)));
    }

    private void Session_Changed(object sender, SelectionChangedEventArgs e)
    {
        if ((SessionsList.SelectedItem as ListBoxItem)?.Tag is not GameSession s) return;
        var rows = GameLog.Seconds(s.Exe, s.Start.AddSeconds(-1), s.End.AddSeconds(1));
        // at most ~600 points: average neighbouring seconds
        int step = Math.Max(1, rows.Count / 600);
        double[] Avg(Func<GameSecond, double> f) => Enumerable.Range(0, (rows.Count + step - 1) / step)
            .Select(i => rows.Skip(i * step).Take(step).Select(f).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average()).ToArray();
        SessionGraphTitle.Text = rows.Count > 0 ? F("{0:g}: frame rate, 1% low and GPU load over the session", s.Start) : T("No detail logged for this session.");
        if (rows.Count == 0) { SessionGraph.Clear(); return; }
        SessionGraph.Show(new[]
        {
            new HistoryGraph.Series(T("FPS"), Color.FromRgb(0x4C, 0xD9, 0x64), Avg(r => r.Fps)),
            new HistoryGraph.Series(T("1% low"), Color.FromRgb(0xFF, 0x8A, 0x3D), Avg(r => r.Low1Fps)),
            new HistoryGraph.Series(T("GPU %"), Color.FromRgb(0x40, 0xC8, 0xFF), Avg(r => r.GpuPercent), OwnScale: true, Unit: "%"),
        }, rows[0].Time.ToString("t"), rows[^1].Time.ToString("t"));
    }
}
