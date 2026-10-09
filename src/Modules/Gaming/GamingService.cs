using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming;

/// <summary>The game in front right now.</summary>
internal sealed record CurrentGame(uint Pid, string Exe, string Name, IntPtr Window);

/// <summary>
/// The Gaming module's engine: watches which program is in front, counts its frames (<see cref="FrameMonitor"/>),
/// reads the hardware once a second, keeps the overlay up to date twice a second, logs each game's performance, and
/// runs recordings and the hotkeys. Everything it does is a few cheap calls a second; the frame counting itself is
/// Windows' event tracing.
/// </summary>
internal sealed class GamingService : IDisposable
{
    public GamingSettings Settings { get; } = GamingSettings.Load();
    public FrameMonitor Frames { get; } = new();
    public Recorder Recorder { get; } = new();

    private readonly Telemetry telemetry = new();
    private readonly HotkeyManager hotkeys = new();
    private readonly object gate = new();
    private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
    private readonly uint ownPid = (uint)Environment.ProcessId;
    private Timer? tick;
    private DispatcherTimer? uiTimer;
    private OverlayWindow? overlay;
    private bool disposed;
    private bool settingsDirty;

    // the latest second, written by the 1 s tick, read by the UI
    private volatile CurrentGame? current;
    private volatile HardwareSample hardware = HardwareSample.Empty;
    private readonly Queue<double> fpsHistory = new(), vramHistory = new();
    private uint historyPid;

    // the play session being logged
    private SessionLog? session;
    private readonly List<long> frameBuffer = new();
    private double[] scratch = new double[16384];
    private string? message;
    private DateTime messageUntil;

    /// <summary>The hotkeys that couldn't be registered (another program has them).</summary>
    public List<string> FailedHotkeys { get; private set; } = new();

    /// <summary>Raised on the UI thread when something the tab shows has changed (game, recording, permission).</summary>
    public event Action? Changed;

    public CurrentGame? Game => current;
    public HardwareSample Hardware => hardware;

    public void Start()
    {
        GameLog.Prune(Settings.KeepHistoryMonths);
        hotkeys.Pressed += OnHotkey;
        RegisterHotkeys();
        Recorder.Stopped += error => dispatcher.BeginInvoke(() => OnRecordingStopped(error));
        UpdateFrameMonitor();
        tick = new Timer(_ => Tick(), null, 1000, 1000);
        uiTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        uiTimer.Tick += (_, _) => UiTick();
        uiTimer.Start();
    }

    /// <summary>Frame counting runs only while something needs it: the overlay or the history.</summary>
    public void UpdateFrameMonitor()
    {
        bool need = Settings.OverlayVisible || Settings.LogHistory;
        if (need && Frames.Status is FrameMonitor.State.Off or FrameMonitor.State.Failed) Frames.Start();
        else if (!need && Frames.Status == FrameMonitor.State.Running) Frames.Stop();
        Changed?.Invoke();
    }

    /// <summary>Tries again (e.g. after signing back in with the permission).</summary>
    public void RetryFrameMonitor()
    {
        Frames.Stop();
        Frames.Start();
        if (Frames.Status == FrameMonitor.State.Running && Settings.PermissionPending)
        {
            Settings.PermissionPending = false;
            Settings.Save();
        }
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- once a second (background thread)

    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "dwm.exe", "applicationframehost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "searchhost.exe", "searchapp.exe", "textinputhost.exe", "lockapp.exe", "daisysapp.exe", "taskmgr.exe",
        "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe", "vivaldi.exe", "msedgewebview2.exe",
        "vlc.exe", "mpc-hc64.exe", "mpc-be64.exe", "potplayermini64.exe", "wmplayer.exe", "video.ui.exe", "obs64.exe",
        "discord.exe", "steamwebhelper.exe", "nvidia app.exe", "nvcontainer.exe", "windowsterminal.exe", "code.exe",
    };

    private int ticking;

    private void Tick()
    {
        if (disposed || Interlocked.Exchange(ref ticking, 1) == 1) return; // the last one is still going
        try
        {
            var (hwnd, pid) = Native.Foreground();
            CurrentGame? game = null;
            if (pid != 0 && pid != ownPid && Frames.Status == FrameMonitor.State.Running)
            {
                var (exe, path) = Native.ProcessExe(pid);
                if (exe.Length > 0 && !NotGames.Contains(exe) && Frames.IsPresenting(pid))
                {
                    GameEntry? entry;
                    lock (Settings.Games) Settings.Games.TryGetValue(exe, out entry);
                    if (entry == null && Native.CoversMonitor(hwnd))
                    {
                        entry = new GameEntry { Name = NameFromFile(path, exe), Track = true };
                        lock (Settings.Games) Settings.Games[exe] = entry;
                        settingsDirty = true;
                    }
                    if (entry is { Track: true })
                    {
                        if ((DateTime.Now - entry.LastSeen).TotalMinutes > 10) { entry.LastSeen = DateTime.Now; settingsDirty = true; }
                        game = new CurrentGame(pid, exe, entry.Name.Length > 0 ? entry.Name : Path.GetFileNameWithoutExtension(exe), hwnd);
                    }
                }
            }
            if (game?.Exe != current?.Exe || game?.Pid != current?.Pid)
            {
                current = game;
                dispatcher.BeginInvoke(() => Changed?.Invoke());
            }

            // the hardware: only while a game is in front, the overlay is up, or the tab shows it
            uint statsPid = game?.Pid ?? (Settings.OverlayOnlyInGames ? 0 : pid);
            if (game != null || overlayShown || TabVisible)
                hardware = telemetry.Read(statsPid);

            UpdateHistory(statsPid);
            LogSecond(game);
            Frames.Prune();
        }
        catch (Exception ex) { ErrorLog.Write("Gaming", ex); }
        finally { ticking = 0; }
    }

    /// <summary>Set by the tab: the hardware is read while it's showing.</summary>
    public volatile bool TabVisible;

    private static string NameFromFile(string path, string exe)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            foreach (string? name in new[] { v.FileDescription, v.ProductName })
                if (!string.IsNullOrWhiteSpace(name) && name.Length < 60 && !name.Contains("Launcher", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Unity", StringComparison.OrdinalIgnoreCase) && !name.Contains("Unreal", StringComparison.OrdinalIgnoreCase))
                    return name.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    /// <summary>A value a second for the overlay's frame rate and video memory graphs.</summary>
    private void UpdateHistory(uint pid)
    {
        lock (gate)
        {
            if (pid != historyPid) { fpsHistory.Clear(); vramHistory.Clear(); historyPid = pid; }
            if (pid == 0) return;
            long now = Stopwatch.GetTimestamp();
            Frames.CopyFrames(pid, now - Stopwatch.Frequency * 2, frameBuffer);
            var last = LastSecond(frameBuffer);
            fpsHistory.Enqueue(last.Fps);
            vramHistory.Enqueue(hardware.VramUsedMB);
            while (fpsHistory.Count > 60) fpsHistory.Dequeue();
            while (vramHistory.Count > 60) vramHistory.Dequeue();
        }
    }

    /// <summary>The second up to the newest frame (Windows hands frames over in batches, so "now" may not have arrived).</summary>
    private static FrameStats LastSecond(List<long> frames)
    {
        if (frames.Count < 2) return FrameStats.None;
        long end = frames[^1], start = end - Stopwatch.Frequency;
        int first = frames.FindIndex(t => t >= start);
        if (first > 0) first--; // include the frame before, so the first frame time is counted
        var window = frames.GetRange(Math.Max(0, first), frames.Count - Math.Max(0, first));
        if (Stopwatch.GetTimestamp() - end > 2 * Stopwatch.Frequency) return FrameStats.None; // stopped showing frames
        return FrameStats.From(window, false);
    }

    // ---------------------------------------------------------------- history

    private sealed class SessionLog
    {
        public required CurrentGame Game;
        public DateTime Start = DateTime.Now, LastActive = DateTime.Now;
        public long LastFrame;
        public readonly List<GameSecond> Pending = new();
        public readonly int[] Histogram = new int[20000]; // frame times in 0.05 ms steps up to 1 s
        public long Frames;
        public double FrameMs, ActiveSeconds, GpuSum;
        public int GpuCount;
        public double MaxTemp = double.NaN, MaxVram = double.NaN;
    }

    private void LogSecond(CurrentGame? game)
    {
        if (!Settings.LogHistory) { EndSession(); return; }
        if (session != null && (game == null || game.Pid != session.Game.Pid))
        {
            // alt-tabbed out: the session carries on unless the game closed, another one started, or 5 minutes went by
            bool gone = !Native.IsAlive(session.Game.Pid);
            if (gone || game != null || (DateTime.Now - session.LastActive).TotalMinutes > 5) EndSession();
        }
        if (game == null) return;
        session ??= new SessionLog { Game = game };
        var s = session;
        s.LastActive = DateTime.Now;

        Frames.CopyFrames(game.Pid, s.LastFrame, frameBuffer);
        if (s.LastFrame == 0 && frameBuffer.Count > 0) { s.LastFrame = frameBuffer[^1]; return; }
        if (frameBuffer.Count == 0) return;
        frameBuffer.Insert(0, s.LastFrame);
        s.LastFrame = frameBuffer[^1];
        var stats = FrameStats.From(frameBuffer, true, scratch);
        if (stats.Frames == 0) return;
        double toMs = 1000.0 / Stopwatch.Frequency;
        for (int i = 1; i < frameBuffer.Count; i++)
        {
            double ms = (frameBuffer[i] - frameBuffer[i - 1]) * toMs;
            s.Histogram[Math.Clamp((int)(ms * 20), 0, s.Histogram.Length - 1)]++;
        }
        s.Frames += stats.Frames;
        s.FrameMs += stats.FrametimeMs * stats.Frames;
        s.ActiveSeconds += 1;
        var hw = hardware;
        if (double.IsFinite(hw.GpuPercent)) { s.GpuSum += hw.GpuPercent; s.GpuCount++; }
        if (double.IsFinite(hw.GpuTempC)) s.MaxTemp = double.IsNaN(s.MaxTemp) ? hw.GpuTempC : Math.Max(s.MaxTemp, hw.GpuTempC);
        if (double.IsFinite(hw.VramUsedMB)) s.MaxVram = double.IsNaN(s.MaxVram) ? hw.VramUsedMB : Math.Max(s.MaxVram, hw.VramUsedMB);
        s.Pending.Add(new GameSecond(DateTime.Now, stats.Fps, stats.FrametimeMs, stats.Low1Fps, stats.MaxFrametimeMs,
            hw.GpuPercent, hw.GpuTempC, hw.GpuClockMHz, hw.GpuPowerW, hw.VramUsedMB, hw.CpuPercent, hw.GameCpuPercent, hw.RamUsedMB,
            Recorder.IsRecording));
        if (s.Pending.Count >= 15) Flush(s);
    }

    private static void Flush(SessionLog s)
    {
        if (s.Pending.Count == 0) return;
        GameLog.Append(s.Game.Exe, s.Pending);
        s.Pending.Clear();
    }

    private void EndSession()
    {
        var s = session;
        if (s == null) return;
        session = null;
        Flush(s);
        if (s.ActiveSeconds < 30 || s.Frames == 0) return; // too short to be worth a line
        double Percentile(double p)
        {
            long target = (long)(s.Frames * p), seen = 0;
            for (int i = 0; i < s.Histogram.Length; i++)
                if ((seen += s.Histogram[i]) > target) return 1000.0 / ((i + 0.5) / 20.0);
            return double.NaN;
        }
        double avgMs = s.FrameMs / s.Frames;
        GameLog.AppendSession(new GameSession(s.Start, s.LastActive, s.Game.Exe, s.Game.Name, s.ActiveSeconds, 1000.0 / avgMs,
            Percentile(0.99), Percentile(0.999), avgMs, s.GpuCount > 0 ? s.GpuSum / s.GpuCount : double.NaN, s.MaxTemp, s.MaxVram));
        dispatcher.BeginInvoke(() => SessionLogged?.Invoke());
    }

    /// <summary>Raised on the UI thread after a play session was added to the history.</summary>
    public event Action? SessionLogged;

    // ---------------------------------------------------------------- twice a second (UI thread)

    private int topmostCountdown;
    private volatile bool overlayShown;

    private void UiTick()
    {
        if (settingsDirty)
        {
            settingsDirty = false;
            lock (Settings.Games) Settings.Save();
            GamesChanged?.Invoke();
        }

        var game = current;
        bool show = Settings.OverlayVisible && (game != null || !Settings.OverlayOnlyInGames) && Frames.Status == FrameMonitor.State.Running;
        bool toast = message != null && DateTime.Now < messageUntil;
        if (!toast) message = null;
        if (!show && !toast)
        {
            if (overlay?.IsVisible == true) overlay.Hide();
            overlayShown = false;
            return;
        }
        overlay ??= CreateOverlay();
        overlay.Update(BuildOverlayData(show ? game : null, show));
        if (!overlay.IsVisible) overlay.Show();
        overlayShown = true;
        if (--topmostCountdown <= 0) { topmostCountdown = 4; overlay.KeepOnTop(); }
    }

    /// <summary>Raised on the UI thread when a new game was found (for the Settings list).</summary>
    public event Action? GamesChanged;

    private OverlayWindow CreateOverlay()
    {
        var w = new OverlayWindow(Settings);
        w.Moved += () => Settings.Save();
        return w;
    }

    private OverlayData BuildOverlayData(CurrentGame? game, bool full)
    {
        var d = new OverlayData { Message = message, Full = full };
        if (Recorder.IsRecording) d.Recording = DateTime.Now - Recorder.StartedAt;
        if (!full) return d;
        uint pid = game?.Pid ?? Native.Foreground().Pid;
        d.Game = game?.Name ?? "";
        d.Hardware = hardware;
        if (session != null && game != null && session.Game.Pid == game.Pid) d.Session = DateTime.Now - session.Start;
        var frames = new List<long>();
        long now = Stopwatch.GetTimestamp();
        bool advanced = Settings.Mode == OverlayMode.Advanced;
        Frames.CopyFrames(pid, now - Stopwatch.Frequency * (advanced ? 31 : 4), frames);
        d.Now = LastSecond(frames);
        if (advanced && frames.Count > 1)
        {
            long cutoff = frames[^1] - Stopwatch.Frequency * 30;
            int first = Math.Max(0, frames.FindIndex(t => t >= cutoff));
            d.Recent = FrameStats.From(frames.GetRange(first, frames.Count - first), true, scratchUi);
        }
        if (Settings.Mode != OverlayMode.Simple && Settings.GraphFrametime && frames.Count > 1)
        {
            // the last 3 seconds of frame times (at most 600 points)
            long cutoff = frames[^1] - Stopwatch.Frequency * 3;
            int first = Math.Max(1, frames.FindIndex(t => t >= cutoff));
            int n = frames.Count - first;
            int step = Math.Max(1, n / 600);
            var ft = new List<double>(n / step + 1);
            for (int i = first; i < frames.Count; i += step)
            {
                double worst = 0;
                for (int j = i; j < Math.Min(frames.Count, i + step); j++) worst = Math.Max(worst, (frames[j] - frames[j - 1]) * 1000.0 / Stopwatch.Frequency);
                ft.Add(worst);
            }
            d.Frametimes = ft.ToArray();
        }
        lock (gate)
        {
            d.FpsHistory = fpsHistory.ToArray();
            d.VramHistory = vramHistory.ToArray();
        }
        return d;
    }

    private readonly double[] scratchUi = new double[16384];

    /// <summary>Shows a short note on screen for a couple of seconds (even with the overlay hidden).</summary>
    public void Flash(string text)
    {
        message = text;
        messageUntil = DateTime.Now.AddSeconds(2.5);
        UiTick();
    }

    /// <summary>After the overlay settings changed.</summary>
    public void ApplyOverlaySettings()
    {
        Settings.Save();
        overlay?.ApplySettings();
        UpdateFrameMonitor();
        UiTick();
    }

    public void ResetOverlayPosition()
    {
        overlay?.ResetPosition();
        Settings.PositionSet = overlay != null;
        Settings.Save();
    }

    // ---------------------------------------------------------------- hotkeys

    public void RegisterHotkeys()
    {
        var list = new[] { Settings.RecordHotkey, Settings.OverlayHotkey, Settings.ModeHotkey }.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h!);
        FailedHotkeys = hotkeys.RegisterAll(list);
    }

    public void SuspendHotkeys() => hotkeys.UnregisterAll();

    private void OnHotkey(string shortcut)
    {
        if (string.Equals(shortcut, Settings.RecordHotkey, StringComparison.OrdinalIgnoreCase)) ToggleRecording();
        else if (string.Equals(shortcut, Settings.OverlayHotkey, StringComparison.OrdinalIgnoreCase)) ToggleOverlay();
        else if (string.Equals(shortcut, Settings.ModeHotkey, StringComparison.OrdinalIgnoreCase)) CycleMode();
    }

    public void ToggleOverlay()
    {
        Settings.OverlayVisible = !Settings.OverlayVisible;
        ApplyOverlaySettings();
        if (!Settings.OverlayVisible) Flash(T("FPS overlay hidden"));
        Changed?.Invoke();
    }

    public void CycleMode()
    {
        Settings.Mode = Settings.Mode switch { OverlayMode.Simple => OverlayMode.Medium, OverlayMode.Medium => OverlayMode.Advanced, _ => OverlayMode.Simple };
        if (!Settings.OverlayVisible) Settings.OverlayVisible = true;
        ApplyOverlaySettings();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- recording

    public string? LastRecordingError { get; private set; }
    public string? LastRecordingFile { get; private set; }
    private bool starting;

    public void ToggleRecording()
    {
        if (Recorder.IsRecording) StopRecording();
        else StartRecording();
    }

    public void StartRecording()
    {
        if (Recorder.IsRecording || starting) return;
        var request = BuildRequest(out string? problem);
        if (request == null)
        {
            LastRecordingError = problem;
            Flash(problem ?? T("Couldn't start recording"));
            Changed?.Invoke();
            return;
        }
        starting = true;
        Changed?.Invoke();
        Task.Run(() => Recorder.Start(request)).ContinueWith(t => dispatcher.BeginInvoke(() =>
        {
            starting = false;
            string? error = t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result;
            LastRecordingError = error;
            Flash(error == null ? T("● Recording started") : T("Couldn't start recording"));
            Changed?.Invoke();
        }));
    }

    public bool Starting => starting;

    public void StopRecording()
    {
        if (!Recorder.IsRecording) return;
        Task.Run(() => Recorder.Stop());
    }

    private void OnRecordingStopped(string? error)
    {
        LastRecordingError = error;
        LastRecordingFile = Recorder.FilePath;
        Flash(error == null ? T("Recording saved") : T("Recording stopped: an error"));
        Changed?.Invoke();
    }

    private RecordingRequest? BuildRequest(out string? problem)
    {
        problem = null;
        var s = Settings;
        var game = current;
        IntPtr window = IntPtr.Zero;
        string title = game?.Name ?? "";
        if (s.Source == CaptureSource.App)
        {
            if (s.AppExe.Length > 0)
            {
                window = Native.MainWindowOf(s.AppExe);
                title = s.FriendlyName(s.AppExe);
                if (window == IntPtr.Zero) { problem = F("{0} isn't running.", title); return null; }
            }
            else if (game != null) window = game.Window;
            else
            {
                var (fg, pid) = Native.Foreground();
                if (pid == ownPid || fg == IntPtr.Zero) { problem = T("No game is in front to record. Start it, or choose a program or the whole monitor."); return null; }
                window = fg;
                title = Path.GetFileNameWithoutExtension(Native.ProcessExe(pid).Exe);
            }
        }
        else if (s.Source == CaptureSource.Region)
        {
            if (s.RegionWidth < 16 || s.RegionHeight < 16) { problem = T("Select the region to record first."); return null; }
            if (title.Length == 0) title = T("Region");
        }
        return new RecordingRequest
        {
            Source = s.Source,
            Monitor = s.Monitor,
            Region = new PixelRect(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight),
            Window = window,
            Title = title,
            Profile = s.ActiveProfile.Clone(),
            SystemAudio = s.RecordSystemAudio,
            Microphone = s.RecordMicrophone,
            Cursor = s.RecordCursor,
            Hdr = s.Hdr,
            Folder = s.RecordingFolder,
        };
    }

    // ---------------------------------------------------------------- tray

    public IReadOnlyList<AppletMenuItem> TrayMenu() => new[]
    {
        new AppletMenuItem(Recorder.IsRecording ? T("Stop recording") : T("Start recording"), ToggleRecording) { Hint = Settings.RecordHotkey },
        new AppletMenuItem(T("Show the FPS overlay"), ToggleOverlay) { Checked = Settings.OverlayVisible, Hint = Settings.OverlayHotkey },
        new AppletMenuItem(T("Overlay mode"), null, new[]
        {
            new AppletMenuItem(T("Simple"), () => SetMode(OverlayMode.Simple)) { Checked = Settings.Mode == OverlayMode.Simple },
            new AppletMenuItem(T("Medium"), () => SetMode(OverlayMode.Medium)) { Checked = Settings.Mode == OverlayMode.Medium },
            new AppletMenuItem(T("Advanced"), () => SetMode(OverlayMode.Advanced)) { Checked = Settings.Mode == OverlayMode.Advanced },
        }),
    };

    public void SetMode(OverlayMode mode)
    {
        Settings.Mode = mode;
        ApplyOverlaySettings();
        Changed?.Invoke();
    }

    public void SaveNow()
    {
        lock (Settings.Games) Settings.Save();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        tick?.Dispose();
        uiTimer?.Stop();
        Recorder.Dispose();
        try { EndSession(); } catch { }
        Frames.Dispose();
        hotkeys.Dispose();
        telemetry.Dispose();
        overlay?.Close();
        SaveNow();
    }
}
