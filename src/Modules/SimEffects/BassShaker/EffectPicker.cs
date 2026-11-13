using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The effect picked by shortcuts (Next / Previous Effect), outlined in the list and bright in the meters as the Level
/// Wizard's step is, and what can be done to it: Highlight (on / off; while any effect is highlighted only those play,
/// and with none left every effect's mute is as it was), Mute (it alone), Effect + / - (its volume). Its shortcuts are
/// Core's Hotkeys (so in Settings → Shortcuts too), set from the Effects card. While the Level Wizard is open they do
/// nothing (it has its own).
/// </summary>
internal sealed class EffectPicker
{
    private const string Prefix = "ShakerPick.";

    private readonly ShakerSettings settings;
    private readonly Func<ShakerProfile?> active;
    private readonly Func<bool> paused;
    private readonly HashSet<ShakerEffect> highlighted = new();
    /// <summary>Every effect's mute before the first was highlighted.</summary>
    private Dictionary<ShakerEffect, bool>? wasMuted;
    private ShakerEffect? current;

    /// <summary>The pick, a highlight, a mute or a volume changed (on the UI thread).</summary>
    public event Action? Changed;

    public EffectPicker(ShakerSettings settings, Func<ShakerProfile?> active, Func<bool> paused)
    {
        this.settings = settings;
        this.active = active;
        this.paused = paused;
    }

    /// <summary>Next, Previous, Highlight, Mute, Effect + and Effect -, in that order.</summary>
    public IReadOnlyList<Hotkey> Keys { get; private set; } = Array.Empty<Hotkey>();

    /// <summary>The effect picked (null: none yet, or it's gone).</summary>
    public ShakerEffect? Current => current is { } c && active()?.Effects.Contains(c) == true ? c : null;

    public bool IsHighlighted(ShakerEffect e) => highlighted.Contains(e);

    public void AddKeys()
    {
        var k = settings.EffectKeys;
        Keys = Hotkeys.Replace(Prefix, new (HotkeySpec, Action)[]
        {
            (Spec("Next", T("Next Effect"), () => k.Next, v => k.Next = v), () => Press(() => Step(1))),
            (Spec("Previous", T("Previous Effect"), () => k.Previous, v => k.Previous = v), () => Press(() => Step(-1))),
            (Spec("Highlight", T("Highlight"), () => k.Highlight, v => k.Highlight = v), () => Press(Highlight)),
            (Spec("Mute", T("Mute"), () => k.Mute, v => k.Mute = v), () => Press(Mute)),
            (Spec("Up", T("Effect +"), () => k.Up, v => k.Up = v), () => Press(() => Nudge(VolumeHotkeys.Step))),
            (Spec("Down", T("Effect -"), () => k.Down, v => k.Down = v), () => Press(() => Nudge(-VolumeHotkeys.Step))),
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

    /// <summary>The picked effect, the first when none is.</summary>
    private ShakerEffect Pick() => current = Current ?? active()!.Effects[0];

    /// <summary>The next or previous effect, round the end.</summary>
    private void Step(int dir)
    {
        var effects = active()!.Effects;
        int i = Current is { } c ? effects.IndexOf(c) : -1;
        i = i < 0 ? (dir > 0 ? 0 : effects.Count - 1) : (i + dir + effects.Count) % effects.Count;
        current = effects[i];
        Logging.Log.Here.Debug(() => $"Shaker: picked {current.Kind} ({i + 1} of {effects.Count})");
    }

    /// <summary>The picked effect highlighted or not: the first mutes every other one, the last puts them back.</summary>
    private void Highlight()
    {
        var e = Pick();
        if (highlighted.Remove(e))
        {
            if (highlighted.Count == 0) { Restore(); return; }
            e.Muted = true;
        }
        else
        {
            if (highlighted.Count == 0)
            {
                wasMuted = active()!.Effects.ToDictionary(x => x, x => x.Muted);
                foreach (var x in active()!.Effects) x.Muted = true;
            }
            highlighted.Add(e);
            e.Muted = false;
        }
        Logging.Log.Here.Info($"Shaker: highlighted {string.Join(", ", highlighted.Select(x => x.Kind))}");
    }

    private void Mute()
    {
        var e = Pick();
        e.Muted = !e.Muted;
        // (while highlighting: it stays so when the highlights end too)
        if (wasMuted != null) wasMuted[e] = e.Muted;
    }

    private void Nudge(double step)
    {
        var e = Pick();
        e.Volume = Math.Clamp(Math.Round(e.Volume + step, 2), 0, 1);
    }

    /// <summary>No highlights: every effect's mute as it was (the last one off, the Level Wizard opening, another profile, closing).</summary>
    public void Restore()
    {
        highlighted.Clear();
        if (wasMuted == null) return;
        foreach (var (e, muted) in wasMuted) e.Muted = muted;
        wasMuted = null;
        Logging.Log.Here.Info("Shaker: highlights ended");
        Changed?.Invoke();
    }

    /// <summary>Another profile: no highlights, nothing picked.</summary>
    public void Reset()
    {
        Restore();
        current = null;
    }
}
