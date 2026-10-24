using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// What a module binds: what it's called, where it's kept, and what may be pressed for it. The module keeps the value
/// in its own settings (<see cref="Get"/> / <see cref="Set"/>, which saves), so its exports and old settings carry on.
/// </summary>
public sealed class HotkeySpec
{
    /// <summary>Unique and stable, e.g. "ForzaWheel.Toggle" or "Audio.Up:dev:{id}".</summary>
    public required string Id { get; init; }

    /// <summary>The module it's in, as Settings → Shortcuts groups them ("ForzaWheel").</summary>
    public required string Module { get; init; }

    /// <summary>What pressing it does ("Start / stop").</summary>
    public required string Name { get; init; }

    /// <summary>What it's for, when the module has one per thing (a device, a mirror, a profile).</summary>
    public string? Target { get; init; }

    /// <summary>When it only works somewhere ("Level Wizard"), shown beside it in Settings → Shortcuts.</summary>
    public string? Where { get; init; }

    public required Func<string?> Get { get; init; }

    /// <summary>Stores and saves a new value (null: none).</summary>
    public required Action<string?> Set { get; init; }

    /// <summary>The value it came with, for Reset in Settings → Shortcuts.</summary>
    public string? Default { get; init; }

    /// <summary>A key by itself (F9, NumPad5…) may be picked, not only a combination with Ctrl, Alt or Shift.</summary>
    public bool Plain { get; init; }

    /// <summary>A controller / wheel / button box button may be picked.</summary>
    public bool Buttons { get; init; } = true;
}

/// <summary>A binding, from <see cref="Hotkeys.Add"/>. Dispose it when what it's for goes away.</summary>
public sealed class Hotkey : IDisposable
{
    internal Hotkey(HotkeySpec spec, Action pressed, Logger log)
    {
        Spec = spec;
        Pressed = pressed;
        Log = log;
    }

    /// <summary>The log of the module that added it.</summary>
    internal Logger Log { get; }

    public HotkeySpec Spec { get; internal set; }
    internal Action Pressed { get; set; }
    internal bool Gone { get; private set; }

    public string Id => Spec.Id;

    /// <summary>
    /// Whether it's registered (on by default). One that only works somewhere (the Level Wizard, while it's open) is
    /// off the rest of the time: still listed, but its key is left to other programs.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            Hotkeys.MarkDirty();
        }
    }
    private bool enabled = true;

    /// <summary>The key combination or controller button, or null.</summary>
    public string? Value => Spec.Get() is { } v && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>"ForzaWheel · Start / stop", with its target when it has one.</summary>
    public string Label => Spec.Target is { } t ? $"{Spec.Module} · {t} · {Spec.Name}" : $"{Spec.Module} · {Spec.Name}";

    /// <summary>The key combination is taken by another program, so it doesn't work.</summary>
    public bool Failed
    {
        get
        {
            Hotkeys.Flush();
            return failed;
        }
        internal set => failed = value;
    }
    private bool failed;
    internal bool FailedNow => failed;

    /// <summary>The other bindings on the same key or button (each acts when it's pressed).</summary>
    public IReadOnlyList<Hotkey> Clashes => Value is { } v
        ? Hotkeys.All.Where(h => h != this && string.Equals(h.Value, v, StringComparison.OrdinalIgnoreCase)).ToList()
        : Array.Empty<Hotkey>();

    /// <summary>Raised on the UI thread when its value, or whether it works, changed.</summary>
    public event Action? Changed;
    internal void RaiseChanged() => Changed?.Invoke();

    /// <summary>Binds a new value (null: none): stored through the spec's Set, and registered at once.</summary>
    public void Bind(string? value)
    {
        Spec.Set(string.IsNullOrWhiteSpace(value) ? null : value);
        Hotkeys.Apply();
    }

    /// <summary>After the module changed the value itself (an import, a profile switch).</summary>
    public void Refresh() => Hotkeys.MarkDirty();

    public void Dispose()
    {
        if (Gone) return;
        Gone = true;
        Hotkeys.Remove(this);
    }
}

/// <summary>
/// Every shortcut in the app, whichever module it's for: key combinations registered system-wide (<see
/// cref="HotkeyManager"/>) and controller buttons (<see cref="ControllerButtons"/>, listened for while one is bound).
/// A module adds its bindings with <see cref="Add"/> (or <see cref="Replace"/> for one per thing) and gets called when
/// one is pressed; Core's <see cref="ShortcutBox"/> picks a value, <see cref="HotkeyWindow"/> shows them, and Settings →
/// Shortcuts lists them all. Use it on the UI thread.
/// </summary>
public static class Hotkeys
{
    private static readonly List<Hotkey> all = new();
    private static HotkeyManager? keys;
    private static IDisposable? buttons;
    private static int suspended;
    private static bool dirty, flushQueued;

    /// <summary>Raised on the UI thread when a binding is added, removed or changed.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Hotkey> All => all.ToList();

    /// <summary>One number per key or button press (raised before its bindings act), so bindings that share a key on
    /// purpose (cameras hidden together) can act once between them.</summary>
    public static long Press { get; private set; }

    public static Hotkey? Find(string id) => all.FirstOrDefault(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a binding (one with the same id is updated, and the same handle returned).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Hotkey Add(HotkeySpec spec, Action pressed) => Add(spec, pressed, Assembly.GetCallingAssembly());

    private static Hotkey Add(HotkeySpec spec, Action pressed, Assembly caller)
    {
        if (Find(spec.Id) is { } h)
        {
            h.Spec = spec;
            h.Pressed = pressed;
        }
        else all.Add(h = new Hotkey(spec, pressed, Log.For(Log.NameOf(caller))));
        MarkDirty();
        return h;
    }

    /// <summary>
    /// Puts a module's bindings for its things in place: every binding whose id starts with <paramref name="prefix"/>
    /// that isn't in <paramref name="items"/> goes, the others are added or updated.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<Hotkey> Replace(string prefix, IEnumerable<(HotkeySpec Spec, Action Pressed)> items)
    {
        var caller = Assembly.GetCallingAssembly();
        var list = items.Select(i => Add(i.Spec, i.Pressed, caller)).ToList();
        foreach (var old in all.Where(h => h.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !list.Contains(h)).ToList()) old.Dispose();
        return list;
    }

    internal static void Remove(Hotkey h)
    {
        if (all.Remove(h)) MarkDirty();
    }

    /// <summary>While a shortcut is being picked: nothing acts, so a combination already used can still be pressed.</summary>
    public static IDisposable Suspend()
    {
        suspended++;
        Apply();
        return new Resumer();
    }

    private sealed class Resumer : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            suspended--;
            Apply();
        }
    }

    internal static void MarkDirty()
    {
        dirty = true;
        if (flushQueued) return;
        flushQueued = true;
        // (many added at once, at startup: registered once, after them)
        Application.Current?.Dispatcher.BeginInvoke(Flush);
    }

    internal static void Flush()
    {
        flushQueued = false;
        if (dirty) Apply();
    }

    /// <summary>Registers everything bound, now.</summary>
    internal static void Apply()
    {
        dirty = false;
        keys ??= MakeKeys();
        var bound = all.Where(h => h.Enabled).Select(h => (H: h, V: h.Value)).Where(x => x.V != null).ToList();
        if (suspended > 0)
        {
            keys.UnregisterAll();
            StopButtons();
        }
        else
        {
            var failed = keys.RegisterAll(bound.Select(x => x.V!).Where(v => !ControllerButtons.IsButton(v)));
            foreach (var h in all)
            {
                bool now = h.Enabled && h.Value is { } v && failed.Contains(v, StringComparer.OrdinalIgnoreCase);
                if (now && !h.FailedNow) h.Log.Warn($"The shortcut {h.Value} ({h.Label}) is taken by another program");
                h.Failed = now;
            }
            if (bound.Any(x => ControllerButtons.IsButton(x.V))) buttons ??= StartButtons();
            else StopButtons();
        }
        foreach (var h in all) h.RaiseChanged();
        Changed?.Invoke();
    }

    private static HotkeyManager MakeKeys()
    {
        var m = new HotkeyManager();
        m.Pressed += OnPressed;
        return m;
    }

    private static IDisposable StartButtons()
    {
        ControllerButtons.Pressed += OnPressed;
        return ControllerButtons.Listen();
    }

    private static void StopButtons()
    {
        if (buttons == null) return;
        ControllerButtons.Pressed -= OnPressed;
        buttons.Dispose();
        buttons = null;
    }

    private static void OnPressed(string shortcut)
    {
        if (suspended > 0) return;
        Press++;
        foreach (var h in all.Where(h => h.Enabled && string.Equals(h.Value, shortcut, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            try
            {
                // (Info, not Debug: what a shortcut did shows in an ordinary log)
                h.Log.Info($"Shortcut {shortcut}: {h.Label}");
                h.Pressed();
            }
            catch (Exception ex) { ErrorLog.Write($"Shortcut {h.Label}", ex); }
        }
    }
}
