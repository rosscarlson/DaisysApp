using System.IO;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video.MiniMirror;

/// <summary>
/// Mini Mirror's state and behaviour: the mirrors (saved in MiniMirror.json), their windows, region selection, the
/// show / hide shortcuts and the tray menu. Runs on the UI thread.
/// </summary>
public sealed class MiniMirrorService : IDisposable
{
    private const int DuplicateOffsetPx = 24;
    private static readonly Logger log = Log.Here;

    private readonly Dictionary<Guid, MirrorWindow> windows = new();
    private readonly DispatcherTimer saveTimer;
    private CaptureManager? captureManager;
    private SelectionFlow? selection;

    public MiniMirrorService()
    {
        Data = MiniMirrorData.Load();
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        saveTimer.Tick += (_, _) => SaveNow();
        OnOffHotkey = Hotkeys.Add(OnOffSpec, () => SetEnabled(!Data.Enabled));
    }

    public MiniMirrorData Data { get; }
    public IReadOnlyList<MirrorDefinition> Mirrors => Data.Mirrors;


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
                + (hdrWasOn ? T(" and HDR conversion was switched off (Settings → Video → Mini Mirror).") : ".");
            ErrorLog.Note("Mini Mirror", PausedReason);
            ClearMarker();
        }
        log.Info($"{(Data.Enabled ? "On" : "Off")}; {Data.Mirrors.Count} mirrors ({Data.Mirrors.Count(m => m.Visible)} showing), {Data.Groups.Count} groups; HDR conversion {(Data.HdrConversion ? "on" : "off")}; hidden from capture {(Data.HideFromCapture ? "yes" : "no")}{(Paused ? "; paused after a crash" : "")}");
        log.Debug(() => string.Join("\n", Data.Mirrors.Select(m => $"mirror {m.Name}: {m.Shape} {m.SourceRect} visible {m.Visible} shortcut {m.Shortcut ?? "none"} scale {m.SizeScale}")));
        MonitorCapture.HdrConversion = Data.HdrConversion;
        captureManager = new CaptureManager();
        captureManager.DisplaysChanged += () => Application.Current?.Dispatcher.BeginInvoke(OnDisplaysChanged);
        if (!Paused) StartMirrors();
        RegisterHotkeys();
        programTimer.Tick += async (_, _) => await CheckPrograms();
        programTimer.Start();
        _ = CheckPrograms();
    }

    private void StartMirrors()
    {
        if (!Data.Enabled) return;
        if (Data.Mirrors.Count > 0) MarkCapturing();
        foreach (var d in Data.Mirrors.Where(d => !windows.ContainsKey(d.Id))) SpawnWindow(d);
    }

    /// <summary>Starts the mirrors after a paused start (the banner's button).</summary>
    public void Resume()
    {
        if (!Paused) return;
        log.Info("Mirrors started again after a paused start");
        Paused = false;
        StartMirrors();
        PausedChanged?.Invoke();
    }

    // ---------------------------------------------------------------- on / off

    /// <summary>Raised when Mini Mirror is switched on or off (here, by its shortcut or the tray).</summary>
    public event Action? EnabledChanged;

    /// <summary>Its on / off shortcut (Core's Hotkeys).</summary>
    public Hotkey OnOffHotkey { get; }

    private HotkeySpec OnOffSpec => new()
    {
        Id = HotkeyPrefix + "OnOff", Module = T("Mini Mirror"), Name = T("On / off"),
        Get = () => Data.OnOffShortcut, Set = v => { Data.OnOffShortcut = string.IsNullOrWhiteSpace(v) ? null : v; SaveNow(); },
    };

    /// <summary>Off closes every mirror window (so nothing is captured) and leaves its other shortcuts to other programs.</summary>
    public void SetEnabled(bool on) => SetEnabled(on, null);

    /// <param name="program">The program switching it, or null by hand (which a program closing then leaves as it is).</param>
    private void SetEnabled(bool on, string? program)
    {
        Data.OnByProgram = on && program != null;
        if (Data.Enabled == on) { SaveNow(); return; }
        Data.Enabled = on;
        log.Info($"Switched {(on ? "on" : "off")}{(program != null ? $" ({program})" : "")}");
        if (on) { if (!Paused) StartMirrors(); }
        else
        {
            selection?.Cancel();
            foreach (var w in windows.Values) w.Close();
            windows.Clear();
            markerTimer.Stop();
            ClearMarker();
        }
        SaveNow();
        RegisterHotkeys();
        EnabledChanged?.Invoke();
    }

    // ---------------------------------------------------------------- programs that switch it on

    private readonly DispatcherTimer programTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool? programRunning; // null until the first check
    private bool checkingPrograms, disposed;

    /// <summary>The mirror's or group's program that's running (one of them), or null.</summary>
    public string? RunningProgram { get; private set; }

    /// <summary>Every program a mirror or group switches on with ("game.exe").</summary>
    public IEnumerable<string> AllPrograms() =>
        Data.Mirrors.SelectMany(m => m.Programs).Concat(Data.Groups.SelectMany(g => g.Programs)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One of the programs starting switches Mini Mirror on (mirrors show as they were: none is shown by it); the last
    /// one closing switches it off, if a program switched it on (switched on or off by hand in between, it's left).
    /// </summary>
    private async Task CheckPrograms()
    {
        if (checkingPrograms || disposed) return;
        var programs = AllPrograms().ToArray();
        if (programs.Length == 0 && programRunning != true && !Data.OnByProgram) return;
        checkingPrograms = true;
        try
        {
            string? found = await Task.Run(() => FindRunning(programs));
            if (disposed) return;
            RunningProgram = found;
            bool now = found != null;
            if (now == programRunning) return;
            programRunning = now;
            if (now)
            {
                log.Info($"{found} is running");
                if (!Data.Enabled) SetEnabled(true, found);
            }
            else
            {
                log.Info("No mirror's or group's program is running");
                if (Data.OnByProgram && Data.Enabled) SetEnabled(false, "its programs closed");
                else if (Data.OnByProgram) { Data.OnByProgram = false; SaveNow(); }
            }
            EnabledChanged?.Invoke();
        }
        catch (Exception ex) { ErrorLog.Write("Mini Mirror programs", ex); }
        finally { checkingPrograms = false; }
    }

    /// <summary>"game.exe", "game" or a full path: the bare name, as Windows' process names are.</summary>
    public static string Bare(string program) => Shared.RunningPrograms.Bare(program);

    private static string? FindRunning(string[] programs) => Shared.RunningPrograms.Get().FirstOf(programs);

    /// <summary>Programs with a window that are running now, as "name.exe" (for picking one).</summary>
    public static SortedSet<string> RunningPrograms() => Shared.RunningPrograms.Windowed();

    /// <summary>Sets a mirror's or group's programs (from "a.exe, b.exe"; ".exe" added where it's left off).</summary>
    public void SetPrograms(List<string> list, string? text, string owner)
    {
        var names = (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => Bare(n) + ".exe").Where(n => n != ".exe").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.SequenceEqual(list, StringComparer.OrdinalIgnoreCase)) return;
        list.Clear();
        list.AddRange(names);
        log.Info($"{owner}: switches on with {(names.Count == 0 ? "no program" : string.Join(", ", names))}");
        SaveNow();
        MirrorsChanged?.Invoke();
        _ = CheckPrograms();
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
        Data.Mirrors.Insert(0, d); // new mirrors go at the top, outside any group: drag one into a group
        log.Info($"Mirror created: {d.Name}, {d.Shape} {d.SourceRect} at {d.TargetFps} fps");
        if (Data.Enabled) SpawnWindow(d);
        else SetEnabled(true); // making a mirror switches Mini Mirror on
        SaveNow();
        MirrorsChanged?.Invoke();
    });

    public void BeginReselect(MirrorDefinition d) => RunSelection(d.Shape, result =>
    {
        log.Info($"Mirror {d.Name}: new region {result.Shape} {result.Rect}");
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
            GroupId = source.GroupId,
            Programs = source.Programs.ToList(),
        };
        Data.Mirrors.Insert(Data.Mirrors.IndexOf(source) + 1, copy);
        SpawnWindow(copy);
        SaveNow();
        MirrorsChanged?.Invoke();
        return copy;
    }

    public void Delete(MirrorDefinition d)
    {
        log.Info($"Mirror deleted: {d.Name}");
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
        log.Info($"Mirror {d.Name}: shortcut {d.Shortcut ?? "none"}");
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
        log.Info($"Mirror {d.Name} {(visible ? "shown" : "hidden")}");
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
        log.Info($"HDR conversion {(on ? "on" : "off")}");
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
        log.Info($"Imported {added} mirrors from SimHub ({file})");
        if (added > 0)
        {
            SaveNow();
            MirrorsChanged?.Invoke();
        }
        return added;
    }

    // ---------------------------------------------------------------- groups

    public IReadOnlyList<MirrorGroup> Groups => Data.Groups;

    public IEnumerable<MirrorDefinition> MirrorsIn(MirrorGroup? g) => Data.Mirrors.Where(m => m.GroupId == g?.Id);

    public MirrorGroup CreateGroup()
    {
        var g = new MirrorGroup { Name = UniqueGroupName(T("Group")) };
        Data.Groups.Add(g);
        SaveNow();
        MirrorsChanged?.Invoke();
        return g;
    }

    private string UniqueGroupName(string baseName)
    {
        for (int n = 1; ; n++)
        {
            string candidate = $"{baseName} {n}";
            if (!Data.Groups.Any(g => g.Name.Equals(candidate, StringComparison.CurrentCultureIgnoreCase))) return candidate;
        }
    }

    public void RenameGroup(MirrorGroup g, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == g.Name) return;
        g.Name = name.Trim();
        SaveNow();
        MirrorsChanged?.Invoke();
    }

    /// <summary>Removes the group; its mirrors stay, outside any group.</summary>
    public void DeleteGroup(MirrorGroup g)
    {
        foreach (var m in MirrorsIn(g)) m.GroupId = null;
        Data.Groups.Remove(g);
        SaveNow();
        RegisterHotkeys();
        MirrorsChanged?.Invoke();
    }

    public void SetGroupShortcut(MirrorGroup g, string? shortcut)
    {
        g.Shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut;
        SaveNow();
        RegisterHotkeys();
        MirrorsChanged?.Invoke();
    }

    /// <summary>Hides the group's mirrors if any of them is showing, otherwise shows them all.</summary>
    public void ToggleGroup(MirrorGroup g) => SetGroupVisible(g, !MirrorsIn(g).Any(m => m.Visible));

    public void SetGroupVisible(MirrorGroup g, bool visible)
    {
        foreach (var m in MirrorsIn(g).Where(m => m.Visible != visible).ToList()) SetVisible(m, visible);
    }

    /// <summary>
    /// Moves a mirror into a group (null = none), before another mirror there (null = at the end of that group, or
    /// the top of the list outside any group).
    /// </summary>
    public void MoveMirror(MirrorDefinition d, MirrorGroup? group, MirrorDefinition? before)
    {
        if (before == d) return;
        Data.Mirrors.Remove(d);
        d.GroupId = group?.Id;
        int index;
        if (before != null && Data.Mirrors.IndexOf(before) is int i and >= 0) index = i;
        else if (group == null) index = 0;
        else
        {
            int last = Data.Mirrors.FindLastIndex(m => m.GroupId == group.Id);
            index = last >= 0 ? last + 1 : Data.Mirrors.Count;
        }
        Data.Mirrors.Insert(index, d);
        SaveNow();
        MirrorsChanged?.Invoke();
    }

    /// <summary>Moves a group up or down the list.</summary>
    public void MoveGroup(MirrorGroup g, MirrorGroup? before)
    {
        if (before == g) return;
        Data.Groups.Remove(g);
        int index = before != null ? Data.Groups.IndexOf(before) : -1;
        Data.Groups.Insert(index >= 0 ? index : Data.Groups.Count, g);
        SaveNow();
        MirrorsChanged?.Invoke();
    }

    // ---------------------------------------------------------------- windows

    private void SpawnWindow(MirrorDefinition d)
    {
        if (captureManager == null || !Data.Enabled) return;
        MarkCapturing();
        var w = new MirrorWindow(d, captureManager, () => OtherWindowBounds(d.Id), Data.HideFromCapture);
        w.BoundsChanged += def =>
        {
            SaveSoon();
            MirrorUpdated?.Invoke(def);
        };
        windows[d.Id] = w;
        log.Debug($"Mirror window for {d.Name}: source {d.SourceRect}, visible {d.Visible}");
        w.Show();
        w.EnsureOnScreen();
    }

    /// <summary>Where the other visible mirrors are, to snap to while Alt-dragging.</summary>
    private IEnumerable<PixelRect> OtherWindowBounds(Guid exclude) =>
        windows.Where(p => p.Key != exclude && p.Value.IsVisible).Select(p => WindowTools.GetBounds(p.Value)).ToList();

    private void OnDisplaysChanged()
    {
        log.Info("Monitors changed: checking the mirrors are still on screen");
        foreach (var w in windows.Values)
        {
            w.UpdateSourceRegion(w.Definition.SourceRect);
            w.EnsureOnScreen();
        }
        // Windows can still be rearranging windows for a while after the change: put the locked ones back again
        var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        int ticks = 0;
        again.Tick += (_, _) =>
        {
            foreach (var w in windows.Values) w.RestoreLocked();
            if (++ticks >= 3) again.Stop();
        };
        again.Start();
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

    private const string HotkeyPrefix = "MiniMirror.";

    /// <summary>New mirror, and every mirror's and group's show / hide shortcut, put in place (Core's Hotkeys).</summary>
    private void RegisterHotkeys()
    {
        HotkeySpec Spec(string id, string name, string? target, Func<string?> get, Action<string?> set) => new()
        {
            Id = HotkeyPrefix + id, Module = T("Mini Mirror"), Name = name, Target = target, Get = get, Set = set,
        };
        var items = new List<(HotkeySpec, Action)>
        {
            (OnOffSpec, () => SetEnabled(!Data.Enabled)),
            (Spec("New", T("New mirror"), null, () => Data.NewMirrorShortcut, SetNewMirrorShortcut), BeginCreate),
        };
        foreach (var d in Data.Mirrors)
            items.Add((Spec($"Mirror:{d.Id}", T("Show / hide"), d.Name, () => d.Shortcut, v => SetShortcut(d, v)), () => ToggleVisible(d)));
        foreach (var g in Data.Groups)
            items.Add((Spec($"Group:{g.Id}", T("Show / hide the group"), g.Name, () => g.Shortcut, v => SetGroupShortcut(g, v)), () => ToggleGroup(g)));
        // off: still listed in Settings → Shortcuts, but only on / off is registered
        foreach (var h in Hotkeys.Replace(HotkeyPrefix, items)) h.Enabled = Data.Enabled || h == OnOffHotkey;
    }

    /// <summary>Shortcuts that couldn't be registered (another program has them).</summary>
    public IReadOnlyList<string> FailedShortcuts => Hotkeys.All.Where(h => h.Id.StartsWith(HotkeyPrefix) && h.Failed).Select(h => h.Value!).ToList();

    // ---------------------------------------------------------------- tray / saving

    public IReadOnlyList<AppletMenuItem> TrayMenu()
    {
        var items = new List<AppletMenuItem>
        {
            new(T("On"), () => SetEnabled(!Data.Enabled)) { Checked = Data.Enabled, Hint = Data.OnOffShortcut },
            new(T("New mirror…"), BeginCreate) { Enabled = selection == null, Hint = Data.NewMirrorShortcut },
        };
        if (Data.Mirrors.Count > 0 && Data.Enabled)
        {
            items.Add(new AppletMenuItem(T("Show all"), () => SetAllVisible(true)) { Enabled = Data.Mirrors.Any(m => !m.Visible) });
            items.Add(new AppletMenuItem(T("Hide all"), () => SetAllVisible(false)) { Enabled = Data.Mirrors.Any(m => m.Visible) });
            items.Add(AppletMenuItem.Separator);
            items.AddRange(MirrorsIn(null).Select(m => new AppletMenuItem(m.Name, () => ToggleVisible(m)) { Checked = m.Visible, Hint = m.Shortcut }));
            foreach (var g in Data.Groups)
            {
                var members = MirrorsIn(g).ToList();
                var sub = new List<AppletMenuItem>
                {
                    new(T("Show / hide the group"), () => ToggleGroup(g)) { Hint = g.Shortcut, Enabled = members.Count > 0 },
                    AppletMenuItem.Separator,
                };
                sub.AddRange(members.Select(m => new AppletMenuItem(m.Name, () => ToggleVisible(m)) { Checked = m.Visible, Hint = m.Shortcut }));
                items.Add(new AppletMenuItem(g.Name, null, sub) { Checked = members.Any(m => m.Visible) });
            }
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
        disposed = true;
        programTimer.Stop();
        markerTimer.Stop();
        ClearMarker(); // a clean exit
        if (saveTimer.IsEnabled) SaveNow();
        selection?.Cancel();
        foreach (var w in windows.Values) w.Close();
        windows.Clear();
        captureManager?.Dispose();
        Hotkeys.Replace(HotkeyPrefix, Array.Empty<(HotkeySpec, Action)>()); // on / off too
    }
}
