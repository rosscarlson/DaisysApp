using System.IO;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// Mini Mirror's state and behaviour: the mirrors (saved in MiniMirror.json), their windows, region selection, the
/// show / hide shortcuts and the tray menu. Runs on the UI thread.
/// </summary>
public sealed class MiniMirrorService : IDisposable
{
    private const int DuplicateOffsetPx = 24;

    private readonly Dictionary<Guid, MirrorWindow> windows = new();
    private readonly HotkeyManager hotkeys = new();
    private readonly DispatcherTimer saveTimer;
    private CaptureManager? captureManager;
    private SelectionFlow? selection;
    private IDisposable? controllers;
    private bool hotkeysSuspended;

    public MiniMirrorService()
    {
        Data = MiniMirrorData.Load();
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        saveTimer.Tick += (_, _) => SaveNow();
        hotkeys.Pressed += OnShortcut;
    }

    public MiniMirrorData Data { get; }
    public IReadOnlyList<MirrorDefinition> Mirrors => Data.Mirrors;

    /// <summary>Shortcuts that couldn't be registered (another program has them).</summary>
    public IReadOnlyList<string> FailedShortcuts { get; private set; } = Array.Empty<string>();

    /// <summary>A mirror was added, removed or renamed, or the list was reordered.</summary>
    public event Action? MirrorsChanged;

    /// <summary>A mirror's settings changed outside the editor (shortcut / tray toggle, dragged or resized).</summary>
    public event Action<MirrorDefinition>? MirrorUpdated;

    /// <summary>A region selection started (true) or finished (false).</summary>
    public event Action<bool>? SelectingChanged;

    public bool IsSelecting => selection != null;

    // ---------------------------------------------------------------- safe start
    //
    // Screen capture goes through graphics drivers, where a fault ends the whole process before anything can catch
    // it. So while capture is starting, a marker file says so; it's removed after a minute of running fine, or on a
    // clean exit. If Daisy's App starts and finds it, capture killed the last run: the mirrors aren't started (and HDR
    // conversion is switched off) until the user chooses to, so the app always opens.

    private static string CrashMarker => Path.Combine(AppPaths.SettingsFolder, "MiniMirror.capturing");
    private readonly DispatcherTimer markerTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    /// <summary>True when the mirrors weren't started because capture crashed the last run.</summary>
    public bool Paused { get; private set; }

    /// <summary>What the paused banner says.</summary>
    public string PausedReason { get; private set; } = "";

    /// <summary>Raised when <see cref="Paused"/> changes.</summary>
    public event Action? PausedChanged;

    private void MarkCapturing()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsFolder);
            File.WriteAllText(CrashMarker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch { }
        markerTimer.Stop();
        markerTimer.Start();
    }

    private static void ClearMarker()
    {
        try { File.Delete(CrashMarker); } catch { }
    }

    public void Start()
    {
        markerTimer.Tick += (_, _) => { markerTimer.Stop(); ClearMarker(); };
        if (File.Exists(CrashMarker))
        {
            Paused = true;
            bool hdrWasOn = Data.HdrConversion;
            if (hdrWasOn)
            {
                Data.HdrConversion = false;
                SaveNow();
            }
            PausedReason = T("Daisy's App closed unexpectedly last time while Mini Mirror was capturing the screen, so the mirrors weren't started")
                + (hdrWasOn ? T(" and HDR conversion was switched off (Settings → Mini Mirror).") : ".");
            ErrorLog.Note("Mini Mirror", PausedReason);
            ClearMarker();
        }
        MonitorCapture.HdrConversion = Data.HdrConversion;
        captureManager = new CaptureManager();
        captureManager.DisplaysChanged += () => Application.Current?.Dispatcher.BeginInvoke(OnDisplaysChanged);
        if (!Paused) StartMirrors();
        RegisterHotkeys();
    }

    private void StartMirrors()
    {
        if (Data.Mirrors.Count > 0) MarkCapturing();
        foreach (var d in Data.Mirrors.Where(d => !windows.ContainsKey(d.Id))) SpawnWindow(d);
    }

    /// <summary>Starts the mirrors after a paused start (the banner's button).</summary>
    public void Resume()
    {
        if (!Paused) return;
        Paused = false;
        StartMirrors();
        PausedChanged?.Invoke();
    }

    // ---------------------------------------------------------------- create / edit

    /// <summary>Lets the user drag around a region, then makes a mirror of it.</summary>
    public void BeginCreate() => RunSelection(MirrorShape.Rectangle, result =>
    {
        Resume(); // making a mirror means capturing again anyway
        var d = new MirrorDefinition
        {
            Name = UniqueName("Mirror"),
            SourceRect = result.Rect,
            Shape = result.Shape,
            WindowBounds = result.Rect,
            TargetFps = DetectRefreshRate(result.Rect),
        };
        Data.Mirrors.Add(d);
        SpawnWindow(d);
        SaveNow();
        MirrorsChanged?.Invoke();
    });

    public void BeginReselect(MirrorDefinition d) => RunSelection(d.Shape, result =>
    {
        d.Shape = result.Shape;
        if (windows.TryGetValue(d.Id, out var w))
        {
            w.UpdateSourceRegion(result.Rect);
            w.ApplyDefinitionChanged();
        }
        else d.SourceRect = result.Rect;
        SaveNow();
        MirrorUpdated?.Invoke(d);
    });

    public void CancelSelection() => selection?.Cancel();

    /// <summary>
    /// Copies a mirror (region, shape and every display setting) into a new one, nudged so it isn't hidden behind the
    /// original. The shortcut isn't copied, so the two don't toggle together.
    /// </summary>
    public MirrorDefinition Duplicate(MirrorDefinition source)
    {
        var copy = new MirrorDefinition
        {
            Name = UniqueName(source.Name + T(" copy")),
            SourceRect = source.SourceRect,
            Shape = source.Shape,
            WindowBounds = new PixelRect(source.WindowBounds.X + DuplicateOffsetPx, source.WindowBounds.Y + DuplicateOffsetPx,
                source.WindowBounds.Width, source.WindowBounds.Height),
            TargetFps = source.TargetFps,
            AspectLock = source.AspectLock,
            ClickThrough = source.ClickThrough,
            Opacity = source.Opacity,
            Visible = source.Visible,
            SizeScale = source.SizeScale,
            Zoom = source.Zoom,
            PositionLocked = source.PositionLocked,
        };
        Data.Mirrors.Add(copy);
        SpawnWindow(copy);
        SaveNow();
        MirrorsChanged?.Invoke();
        return copy;
    }

    public void Delete(MirrorDefinition d)
    {
        if (windows.Remove(d.Id, out var w)) w.Close();
        Data.Mirrors.Remove(d);
        SaveNow();
        RegisterHotkeys();
        MirrorsChanged?.Invoke();
    }

    public void Rename(MirrorDefinition d, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == d.Name) return;
        d.Name = name.Trim();
        Apply(d);
        MirrorsChanged?.Invoke();
    }

    public void SetShortcut(MirrorDefinition d, string? shortcut)
    {
        d.Shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut;
        SaveNow();
        RegisterHotkeys();
        MirrorsChanged?.Invoke();
    }

    public void SetSizeScale(MirrorDefinition d, double scale)
    {
        d.SizeScale = scale;
        if (windows.TryGetValue(d.Id, out var w)) w.ApplyScale();
        SaveSoon();
    }

    public void SetVisible(MirrorDefinition d, bool visible)
    {
        d.Visible = visible;
        Apply(d);
        MirrorUpdated?.Invoke(d);
        MirrorsChanged?.Invoke();
    }

    public void ToggleVisible(MirrorDefinition d) => SetVisible(d, !d.Visible);

    public void SetAllVisible(bool visible)
    {
        foreach (var d in Data.Mirrors.Where(m => m.Visible != visible)) SetVisible(d, visible);
    }

    /// <summary>For settings that only need the window refreshed: change the property, then call this.</summary>
    public void Apply(MirrorDefinition d)
    {
        if (windows.TryGetValue(d.Id, out var w)) w.ApplyDefinitionChanged();
        SaveSoon();
    }

    public void SetHdrConversion(bool on)
    {
        Data.HdrConversion = on;
        MonitorCapture.HdrConversion = on;
        captureManager?.RebuildAll();
        SaveNow();
    }

    /// <summary>Whether a monitor is being captured as HDR (and converted) right now.</summary>
    public bool CapturingHdr => captureManager?.AnyHdr == true;

    public void SetNewMirrorShortcut(string? shortcut)
    {
        Data.NewMirrorShortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut;
        SaveNow();
        RegisterHotkeys();
    }

    public void SetHideFromCapture(bool hide)
    {
        Data.HideFromCapture = hide;
        foreach (var w in windows.Values) w.SetHideFromCapture(hide);
        SaveNow();
    }

    /// <summary>Adds the SimHub plugin's mirrors that aren't here yet. Returns how many were added.</summary>
    public int ImportFromSimHub(string file)
    {
        int added = 0;
        foreach (var d in MiniMirrorData.SimHubImport.Read(file))
        {
            if (Data.Mirrors.Any(m => m.Id == d.Id)) continue;
            d.Name = UniqueName(d.Name);
            Data.Mirrors.Add(d);
            SpawnWindow(d);
            added++;
        }
        if (added > 0)
        {
            SaveNow();
            MirrorsChanged?.Invoke();
        }
        return added;
    }

    // ---------------------------------------------------------------- windows

    private void SpawnWindow(MirrorDefinition d)
    {
        if (captureManager == null) return;
        MarkCapturing();
        var w = new MirrorWindow(d, captureManager, () => OtherWindowBounds(d.Id), Data.HideFromCapture);
        w.BoundsChanged += def =>
        {
            SaveSoon();
            MirrorUpdated?.Invoke(def);
        };
        windows[d.Id] = w;
        w.Show();
        w.EnsureOnScreen();
    }

    /// <summary>Where the other visible mirrors are, to snap to while Alt-dragging.</summary>
    private IEnumerable<PixelRect> OtherWindowBounds(Guid exclude) =>
        windows.Where(p => p.Key != exclude && p.Value.IsVisible).Select(p => WindowPlacement.GetPhysicalBounds(p.Value)).ToList();

    private void OnDisplaysChanged()
    {
        foreach (var w in windows.Values)
        {
            w.UpdateSourceRegion(w.Definition.SourceRect);
            w.EnsureOnScreen();
        }
    }

    /// <summary>The fastest refresh rate among the monitors the region is on (so a 144 Hz screen isn't held to 30).</summary>
    private static int DetectRefreshRate(PixelRect rect)
    {
        int best = 30;
        foreach (var m in MonitorService.GetMonitorsIntersecting(rect)) best = Math.Max(best, MonitorService.GetRefreshRateHz(m.DeviceName));
        return Math.Clamp(best, 15, 240);
    }

    private string UniqueName(string baseName)
    {
        if (baseName == "Mirror" || Data.Mirrors.Any(m => m.Name.Equals(baseName, StringComparison.CurrentCultureIgnoreCase)))
        {
            for (int n = baseName == "Mirror" ? 1 : 2; ; n++)
            {
                string candidate = $"{baseName} {n}";
                if (!Data.Mirrors.Any(m => m.Name.Equals(candidate, StringComparison.CurrentCultureIgnoreCase))) return candidate;
            }
        }
        return baseName;
    }

    private void RunSelection(MirrorShape shape, Action<SelectionResult> onConfirmed)
    {
        if (selection != null) return;
        selection = new SelectionFlow(shape, onConfirmed, () =>
        {
            selection = null;
            SelectingChanged?.Invoke(false);
        });
        SelectingChanged?.Invoke(true);
        selection.Start();
    }

    // ---------------------------------------------------------------- shortcuts

    private void RegisterHotkeys()
    {
        var bound = Data.Mirrors.Select(m => m.Shortcut).Append(Data.NewMirrorShortcut).OfType<string>().ToList();
        FailedShortcuts = hotkeysSuspended ? FailedShortcuts : hotkeys.RegisterAll(bound.Where(s => !ControllerButtons.IsButton(s)));

        bool wantControllers = bound.Any(ControllerButtons.IsButton);
        if (wantControllers && controllers == null)
        {
            ControllerButtons.Pressed += OnShortcut;
            controllers = ControllerButtons.Listen();
        }
        else if (!wantControllers && controllers != null)
        {
            ControllerButtons.Pressed -= OnShortcut;
            controllers.Dispose();
            controllers = null;
        }
    }

    /// <summary>While a shortcut is being set, registered hotkeys would swallow the keys: pause them.</summary>
    public void SuspendHotkeys()
    {
        hotkeysSuspended = true;
        hotkeys.UnregisterAll();
    }

    public void ResumeHotkeys()
    {
        hotkeysSuspended = false;
        RegisterHotkeys();
    }

    private void OnShortcut(string shortcut)
    {
        if (hotkeysSuspended) return;
        if (string.Equals(shortcut, Data.NewMirrorShortcut, StringComparison.OrdinalIgnoreCase)) BeginCreate();
        foreach (var d in Data.Mirrors.Where(m => string.Equals(m.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase)).ToList())
            ToggleVisible(d);
    }

    // ---------------------------------------------------------------- tray / saving

    public IReadOnlyList<AppletMenuItem> TrayMenu()
    {
        var items = new List<AppletMenuItem> { new(T("New mirror…"), BeginCreate) { Enabled = selection == null, Hint = Data.NewMirrorShortcut } };
        if (Data.Mirrors.Count > 0)
        {
            items.Add(new AppletMenuItem(T("Show all"), () => SetAllVisible(true)) { Enabled = Data.Mirrors.Any(m => !m.Visible) });
            items.Add(new AppletMenuItem(T("Hide all"), () => SetAllVisible(false)) { Enabled = Data.Mirrors.Any(m => m.Visible) });
            items.Add(AppletMenuItem.Separator);
            items.AddRange(Data.Mirrors.Select(m => new AppletMenuItem(m.Name, () => ToggleVisible(m)) { Checked = m.Visible, Hint = m.Shortcut }));
        }
        return items;
    }

    private void SaveSoon()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void SaveNow()
    {
        saveTimer.Stop();
        Data.Save();
    }

    public void Dispose()
    {
        markerTimer.Stop();
        ClearMarker(); // a clean exit
        if (saveTimer.IsEnabled) SaveNow();
        selection?.Cancel();
        foreach (var w in windows.Values) w.Close();
        windows.Clear();
        captureManager?.Dispose();
        if (controllers != null)
        {
            ControllerButtons.Pressed -= OnShortcut;
            controllers.Dispose();
        }
        hotkeys.Dispose();
    }
}
