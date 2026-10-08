using System.Windows;
using System.Windows.Threading;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Resizer;

/// <summary>
/// The Resizer's state and behaviour: profiles and groups (saved in Resizer.json), applying them, global hotkeys, the
/// script/Stream Deck pipe and the process watcher. The view, editors and tray menu all go through this.
/// Everything here runs on the UI thread unless noted.
/// </summary>
public sealed class ResizerService : IDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HotkeyManager hotkeys = new();
    private readonly CommandPipe pipe = new();
    private readonly ProcessWatcher watcher;
    private volatile ResizerData snapshot; // a copy the watcher thread can read safely

    public ResizerService()
    {
        Data = ResizerData.Load();
        snapshot = Copy(Data);
        watcher = new ProcessWatcher(() => snapshot);
        watcher.RunningChanged += names => dispatcher.BeginInvoke(() => { Running = names; RunningChanged?.Invoke(); });
        watcher.AutoApplied += (p, r) => dispatcher.BeginInvoke(() =>
            Status?.Invoke(r == ApplyResult.Applied ? F("Applied {0} automatically.", p.Name) : WindowMover.Describe(r, p), r != ApplyResult.Applied));
        hotkeys.Pressed += OnHotkey;
        pipe.Command += (cmd, args) => dispatcher.BeginInvoke(() => OnPipeCommand(cmd, args));
    }

    public ResizerData Data { get; }

    /// <summary>Normalized process names of profiles whose program is running now.</summary>
    public HashSet<string> Running { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shortcuts Windows wouldn't register (already taken by another program).</summary>
    public List<string> FailedShortcuts { get; private set; } = new();

    public event Action? Changed;
    public event Action? RunningChanged;
    /// <summary>A message for the status line: text and whether it's an error.</summary>
    public event Action<string, bool>? Status;

    public void Start()
    {
        RegisterHotkeys();
        pipe.Start();
        watcher.Start();
    }

    public bool IsRunning(ResizeProfile p) => Running.Contains(ProcessFinder.Normalize(p.ProcessName));

    // ---------------------------------------------------------------- applying

    /// <summary>Applies a profile. <paramref name="test"/> (the editor's Apply now): one attempt, no retries or watching.</summary>
    public async Task<ApplyResult> ApplyAsync(ResizeProfile profile, bool test = false)
    {
        var result = await WindowMover.ApplyAsync(profile.Clone(), retry: !test, monitor: !test);
        Status?.Invoke(WindowMover.Describe(result, profile), result != ApplyResult.Applied);
        return result;
    }

    /// <summary>Applies every member of the group whose program is running.</summary>
    public async Task ApplyGroupAsync(ResizeGroup group)
    {
        var running = Members(group).Where(IsRunning).ToList();
        if (running.Count == 0)
        {
            Status?.Invoke(F("None of the programs in {0} are running.", group.Name), true);
            return;
        }
        var results = await Task.WhenAll(running.Select(p => WindowMover.ApplyAsync(p.Clone(), retry: true, monitor: true)));
        int ok = results.Count(r => r == ApplyResult.Applied);
        Status?.Invoke(ok == running.Count
            ? (ok == 1 ? F("Applied {0} profile in {1}.", ok, group.Name) : F("Applied {0} profiles in {1}.", ok, group.Name))
            : F("Applied {0} of {1} running profiles in {2}. ", ok, running.Count, group.Name) +
              string.Join(" ", running.Zip(results).Where(x => x.Second != ApplyResult.Applied).Select(x => WindowMover.Describe(x.Second, x.First))),
            ok < running.Count);
    }

    /// <summary>A hotkey applies every profile that uses it plus the members of every group that uses it; if more than
    /// one is involved, only those whose program is running (so one key can move several games).</summary>
    private void OnHotkey(string shortcut)
    {
        var candidates = Data.Profiles.Where(p => Same(p.Shortcut, shortcut))
            .Concat(Data.Groups.Where(g => Same(g.Shortcut, shortcut)).SelectMany(Members))
            .DistinctBy(p => p.Uuid).ToList();
        if (candidates.Count == 1)
        {
            _ = ApplyAsync(candidates[0]);
            return;
        }
        var running = candidates.Where(p => ProcessFinder.PidsFor(p.ProcessName).Count > 0).ToList();
        if (running.Count == 0) Status?.Invoke(F("{0}: none of its programs are running.", shortcut), true);
        foreach (var p in running) _ = ApplyAsync(p);
    }

    private void OnPipeCommand(string command, IReadOnlyList<string> args)
    {
        string name = string.Join(" ", args);
        switch (command)
        {
            case "apply-profile" when Data.Profiles.FirstOrDefault(p => Same(p.Name, name)) is { } profile:
                _ = ApplyAsync(profile);
                break;
            case "apply-group" when Data.Groups.FirstOrDefault(g => Same(g.Name, name)) is { } group:
                _ = ApplyGroupAsync(group);
                break;
            case "apply-profile":
            case "apply-group":
                Status?.Invoke((command == "apply-group" ? F("A script asked for \"{0}\", but there's no group with that name.", name) : F("A script asked for \"{0}\", but there's no profile with that name.", name)), true);
                break;
            case "show":
                if (Application.Current.MainWindow is { } w)
                {
                    w.Show();
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    w.Activate();
                }
                break;
        }
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- hotkeys

    public void RegisterHotkeys()
    {
        var all = Data.Profiles.Select(p => p.Shortcut).Concat(Data.Groups.Select(g => g.Shortcut))
                      .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!);
        FailedShortcuts = hotkeys.RegisterAll(all);
    }

    /// <summary>While a shortcut is being typed in an editor, registered hotkeys would swallow the keys: pause them.</summary>
    public void SuspendHotkeys() => hotkeys.UnregisterAll();
    public void ResumeHotkeys() => RegisterHotkeys();

    /// <summary>Other profiles and groups using this shortcut (to show "Also used by …").</summary>
    public List<string> SharedWith(string? shortcut, Guid exclude) =>
        string.IsNullOrWhiteSpace(shortcut) ? new()
        : Data.Groups.Where(g => g.Uuid != exclude && Same(g.Shortcut, shortcut)).Select(g => g.Name + T(" (group)"))
              .Concat(Data.Profiles.Where(p => p.Uuid != exclude && Same(p.Shortcut, shortcut)).Select(p => p.Name))
              .ToList();

    // ---------------------------------------------------------------- the list

    /// <summary>Groups and ungrouped profiles, in their shared order.</summary>
    public List<object> TopLevel() =>
        Data.Groups.Cast<object>().Concat(Data.Profiles.Where(p => p.GroupUuid == null))
            .OrderBy(x => x is ResizeGroup g ? g.Order : ((ResizeProfile)x).Order).ToList();

    public List<ResizeProfile> Members(ResizeGroup group) =>
        Data.Profiles.Where(p => p.GroupUuid == group.Uuid).OrderBy(p => p.Order).ToList();

    /// <summary>Moves a group or profile up (−1) or down (+1) among its siblings.</summary>
    public void Move(object item, int delta)
    {
        List<object> siblings = item is ResizeProfile { GroupUuid: Guid gid } && Data.Groups.FirstOrDefault(g => g.Uuid == gid) is { } group
            ? Members(group).Cast<object>().ToList()
            : TopLevel();
        int i = siblings.IndexOf(item), j = i + delta;
        if (i < 0 || j < 0 || j >= siblings.Count) return;
        (siblings[i], siblings[j]) = (siblings[j], siblings[i]);
        Renumber(siblings);
        Save();
    }

    private static void Renumber(List<object> items)
    {
        for (int k = 0; k < items.Count; k++)
            if (items[k] is ResizeGroup g) g.Order = k; else ((ResizeProfile)items[k]).Order = k;
    }

    public void SaveProfile(ResizeProfile edited)
    {
        var existing = Data.Profiles.FirstOrDefault(p => p.Uuid == edited.Uuid);
        bool moved = existing == null || existing.GroupUuid != edited.GroupUuid;
        if (moved) // new, or into another group: goes to the end of its new list
            edited.Order = edited.GroupUuid is Guid gid ? Data.Profiles.Count(p => p.GroupUuid == gid && p.Uuid != edited.Uuid) : TopLevel().Count;
        if (existing == null) Data.Profiles.Add(edited);
        else Data.Profiles[Data.Profiles.IndexOf(existing)] = edited;
        Save();
    }

    public void DeleteProfile(ResizeProfile profile)
    {
        Data.Profiles.RemoveAll(p => p.Uuid == profile.Uuid);
        Save();
    }

    public void SaveGroup(ResizeGroup edited)
    {
        var existing = Data.Groups.FirstOrDefault(g => g.Uuid == edited.Uuid);
        if (existing == null)
        {
            edited.Order = TopLevel().Count;
            Data.Groups.Add(edited);
        }
        else Data.Groups[Data.Groups.IndexOf(existing)] = edited;
        Save();
    }

    /// <summary>Deletes the group; its profiles move to the top level (they aren't deleted).</summary>
    public void DeleteGroup(ResizeGroup group)
    {
        int next = TopLevel().Count;
        foreach (var p in Members(group))
        {
            p.GroupUuid = null;
            p.Order = next++;
        }
        Data.Groups.RemoveAll(g => g.Uuid == group.Uuid);
        Save();
    }

    public void SetCollapsed(ResizeGroup group, bool collapsed)
    {
        group.Collapsed = collapsed;
        Save();
    }

    public void SetWatcher(bool enabled)
    {
        Data.ProcessWatcherEnabled = enabled;
        Save();
    }

    public void SetPollRate(int ms)
    {
        Data.PollRateMs = Math.Clamp(ms, 100, 10000);
        Save();
    }

    /// <summary>Imports profiles and groups from Resize Rabbit or Resize Raccoon's data folder. Returns how many profiles were added.</summary>
    public int Import(string folder)
    {
        int added = ResizerData.Legacy.Import(Data, folder);
        Save();
        return added;
    }

    public void Save()
    {
        Data.Save();
        snapshot = Copy(Data);
        RegisterHotkeys();
        Changed?.Invoke();
    }

    private static ResizerData Copy(ResizerData d) => new()
    {
        ProcessWatcherEnabled = d.ProcessWatcherEnabled,
        PollRateMs = d.PollRateMs,
        Profiles = d.Profiles.Select(p => p.Clone()).ToList(),
        Groups = d.Groups.Select(g => g.Clone()).ToList(),
    };

    // ---------------------------------------------------------------- tray

    /// <summary>The tray submenu: every group (with its members) and ungrouped profile; clicking one applies it.</summary>
    public IReadOnlyList<AppletMenuItem> TrayMenu()
    {
        var items = new List<AppletMenuItem>();
        foreach (var item in TopLevel())
        {
            if (item is ResizeGroup g)
            {
                var children = new List<AppletMenuItem> { new(T("Apply all running"), () => _ = ApplyGroupAsync(g)) { Hint = g.Shortcut } };
                var members = Members(g);
                if (members.Count > 0) children.Add(AppletMenuItem.Separator);
                children.AddRange(members.Select(ProfileItem));
                items.Add(new AppletMenuItem(g.Name, Children: children));
            }
            else items.Add(ProfileItem((ResizeProfile)item));
        }
        if (items.Count == 0) items.Add(new AppletMenuItem(T("No profiles yet")) { Enabled = false });
        return items;

        AppletMenuItem ProfileItem(ResizeProfile p) =>
            new(IsRunning(p) ? F("{0}  (running)", p.Name) : p.Name, () => _ = ApplyAsync(p)) { Hint = p.Shortcut };
    }

    public void Dispose()
    {
        watcher.Dispose();
        pipe.Dispose();
        hotkeys.Dispose();
    }
}
