using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The effect picked by shortcuts (Next / Previous Effect), outlined in the list and bright in the meters as the Level
/// Wizard's step is, and what can be done to it: Isolate (only it plays; again, every effect's mute as it was) and Mute
/// (it alone). Its shortcuts are Core's Hotkeys (so in Settings → Shortcuts too), set from the Effects card. While the
/// Level Wizard is open they do nothing (it has its own).
/// </summary>
internal sealed class EffectPicker
{
    private const string Prefix = "ShakerPick.";

    private readonly ShakerSettings settings;
    private readonly Func<ShakerProfile?> active;
    private readonly Func<bool> paused;
    private Dictionary<ShakerEffect, bool>? wasMuted;
    private ShakerEffect? current;

    /// <summary>The pick, isolation or a mute changed (on the UI thread).</summary>
    public event Action? Changed;

    public EffectPicker(ShakerSettings settings, Func<ShakerProfile?> active, Func<bool> paused)
    {
        this.settings = settings;
        this.active = active;
        this.paused = paused;
    }

    /// <summary>Previous, Next, Isolate and Mute, in that order.</summary>
    public IReadOnlyList<Hotkey> Keys { get; private set; } = Array.Empty<Hotkey>();

    /// <summary>The effect picked (null: none yet, or it's gone).</summary>
    public ShakerEffect? Current => current is { } c && active()?.Effects.Contains(c) == true ? c : null;

    public bool Isolated => wasMuted != null;

    public void AddKeys()
    {
        var k = settings.EffectKeys;
        Keys = Hotkeys.Replace(Prefix, new (HotkeySpec, Action)[]
        {
            (Spec("Previous", T("Previous Effect"), () => k.Previous, v => k.Previous = v), () => Press(() => Step(-1))),
            (Spec("Next", T("Next Effect"), () => k.Next, v => k.Next = v), () => Press(() => Step(1))),
            (Spec("Isolate", T("Isolate on / off"), () => k.Isolate, v => k.Isolate = v), () => Press(Isolate)),
            (Spec("Mute", T("Mute on / off"), () => k.Mute, v => k.Mute = v), () => Press(Mute)),
        });
    }

    public void RemoveKeys() => Hotkeys.Replace(Prefix, Array.Empty<(HotkeySpec, Action)>());

    /// <summary>The set ones, a line each, for the bindings button's tooltip.</summary>
    public string Shown() => string.Join("\n", Keys.Where(h => h.Text != null).Select(h => $"{h.Spec.Name}: {h.Text}"));

    private HotkeySpec Spec(string id, string name, Func<string?> get, Action<string?> set) => new()
    {
        Id = Prefix + id, Module = T("Shaker"), Name = name, Plain = true,
        Get = get, Set = v => { set(v); settings.Save(); },
    };

    private void Press(Action a)
    {
        if (paused() || active() is not { Effects.Count: > 0 }) return;
        a();
        Changed?.Invoke();
    }

    /// <summary>The next or previous effect, round the end; isolated, the isolation moves with it.</summary>
    private void Step(int dir)
    {
        var effects = active()!.Effects;
        int i = Current is { } c ? effects.IndexOf(c) : -1;
        i = i < 0 ? (dir > 0 ? 0 : effects.Count - 1) : (i + dir + effects.Count) % effects.Count;
        current = effects[i];
        if (Isolated) Apply();
        Logging.Log.Here.Debug(() => $"Shaker: picked {current.Kind} ({i + 1} of {effects.Count})");
    }

    private void Isolate()
    {
        if (Isolated) { Restore(); return; }
        current = Current ?? active()!.Effects[0];
        wasMuted = active()!.Effects.ToDictionary(e => e, e => e.Muted);
        Apply();
        Logging.Log.Here.Info($"Shaker: {current.Kind} isolated");
    }

    /// <summary>Only the picked effect plays.</summary>
    private void Apply()
    {
        foreach (var e in active()!.Effects) e.Muted = e != current;
    }

    private void Mute()
    {
        var e = current = Current ?? active()!.Effects[0];
        e.Muted = !e.Muted;
        // (isolated: it stays so when the isolation ends too)
        if (wasMuted != null) wasMuted[e] = e.Muted;
    }

    /// <summary>The isolation ended: every effect's mute as it was (the Level Wizard opening, another profile, closing).</summary>
    public void Restore()
    {
        if (wasMuted == null) return;
        foreach (var (e, muted) in wasMuted) e.Muted = muted;
        wasMuted = null;
        Logging.Log.Here.Info("Shaker: isolation ended");
        Changed?.Invoke();
    }

    /// <summary>Another profile: isolation ended, nothing picked.</summary>
    public void Reset()
    {
        Restore();
        current = null;
    }
}
