using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Binder;

/// <summary>What copying another title's bindings did.</summary>
public sealed class MigrateReport
{
    public required string From { get; init; }
    public int Copied { get; set; }
    /// <summary>Bindings found a place for that the game can't take: the action, the binding, why.</summary>
    public List<(string Action, string Bind, string Why)> Refused { get; } = new();
    /// <summary>Bound actions of the other title with nothing like them here.</summary>
    public List<(string Action, string Bind)> NoMatch { get; } = new();
    /// <summary>Axes moved between SDL's and DirectInput's numbering without the device to check it by.</summary>
    public List<string> Check { get; } = new();
}

/// <summary>
/// Copies one title's bindings into another: each action is matched by its role (what it does: "pitch", "gear.up"), then
/// by the same id, then by the same name; each of its bindings goes in the first slot that takes it. Actions with no
/// match keep the game's own. Controllers are the same devices in both; keys the same keys.
/// </summary>
public static class Migrate
{
    public static MigrateReport Copy(TitleData from, TitleData to, Func<DeviceId, string?> name)
    {
        var report = new MigrateReport { From = from.Def.Name };
        bool fromSdl = from.Format.Input == InputKind.Sdl, toSdl = to.Format.Input == InputKind.Sdl;
        var used = new HashSet<BinderAction>();

        foreach (var t in to.Actions)
        {
            var s = Match(from, t);
            if (s == null) continue;
            used.Add(s);
            var source = from.Get(s).Where(b => !b.IsNone).ToList();
            if (source.Count == 0) continue;
            var placed = new Bind[to.Format.Slots(t)];
            Array.Fill(placed, Bind.None);
            foreach (var original in source)
            {
                var b = original;
                if (b.Kind == BindKind.Axis && fromSdl != toSdl && b.Device is { } dev)
                {
                    var moved = MoveAxis(dev, b.Code, fromSdl);
                    if (moved == null) report.Check.Add(F("{0}: {1}", t.Label, Describe.Text(b, name, fromSdl)));
                    else b = b with { Code = moved.Value };
                }
                if (b.Kind == BindKind.Other) { report.Refused.Add((t.Label, Describe.Text(b, name), T("only its own game knows it"))); continue; }
                string? why = null;
                int slot = -1;
                for (int i = 0; i < placed.Length && slot < 0; i++)
                {
                    if (!placed[i].IsNone) continue;
                    // (each slot asked with what's been put in so far, so a section's one-controller rule holds)
                    var before = to.Binds.TryGetValue(t.Id, out var kept) ? kept : null;
                    to.Binds[t.Id] = placed;
                    var refuse = to.Format.Refuse(to, t, i, b);
                    if (before != null) to.Binds[t.Id] = before; else to.Binds.Remove(t.Id);
                    if (refuse == null) slot = i; else why ??= refuse;
                }
                if (slot < 0) { report.Refused.Add((t.Label, Describe.Text(b, name, toSdl), why ?? T("no free slot"))); continue; }
                placed[slot] = to.Format.CanInvert(t) ? b : b with { Inverted = false };
                if (placed[slot].Inverted) report.Check.Add(F("{0}: inverted, as in {1}", t.Label, from.Def.Name));
                report.Copied++;
            }
            if (!placed.Any(p => !p.IsNone)) continue;
            // (the slots left keep what the game had there: its keys stay beside a controller's button)
            foreach (var mine in to.Get(t).Where(m => !m.IsNone && !placed.Any(p => p.SameInput(m))))
            {
                int free = Array.FindIndex(placed, p => p.IsNone);
                if (free < 0) break;
                to.Binds[t.Id] = placed;
                if (to.Format.Refuse(to, t, free, mine) == null) placed[free] = mine;
            }
            to.Binds[t.Id] = placed;
        }

        foreach (var s in from.Actions)
        {
            if (used.Contains(s)) continue;
            var bound = from.Get(s).Where(b => !b.IsNone).ToList();
            if (bound.Count > 0) report.NoMatch.Add((s.Label, string.Join(", ", bound.Select(b => Describe.Text(b, name, fromSdl)))));
        }
        Logging.Log.Here.Info($"Binder: copied {from.Def.Name} into {to.Def.Name}: {report.Copied} bindings, {report.Refused.Count} refused, {report.NoMatch.Count} with no match");
        return report;
    }

    private static BinderAction? Match(TitleData from, BinderAction t)
    {
        if (t.Role != null && from.Actions.FirstOrDefault(s => s.Role == t.Role) is { } byRole) return byRole;
        return from.Actions.FirstOrDefault(s => s.Id.Equals(t.Id, StringComparison.OrdinalIgnoreCase) && s.IsAxis == t.IsAxis)
            ?? from.Actions.FirstOrDefault(s => s.Label.Equals(t.Label, StringComparison.OrdinalIgnoreCase) && s.IsAxis == t.IsAxis);
    }

    /// <summary>
    /// An axis' number between SDL's (a device's axes in the order DirectInput reports them) and DirectInput's state (X, Y,
    /// Z, the rotations, two sliders), from the plugged-in device itself; null if it isn't plugged in.
    /// </summary>
    public static int? MoveAxis(DeviceId dev, int axis, bool fromSdl)
    {
        var order = SdlOrder(dev);
        if (order == null) return null;
        if (fromSdl) return axis >= 0 && axis < order.Length && order[axis] >= 0 ? order[axis] : null;
        int i = Array.IndexOf(order, axis);
        return i >= 0 ? i : null;
    }

    /// <summary>
    /// For each of SDL's axes, DirectInput's number for it: SDL 2 numbers the axes a device has in DirectInput's order (X, Y, Z,
    /// the rotations, the sliders), leaving out the ones it hasn't. Seen on the rig: a Warthog stick's Axis 1 is X, its
    /// throttle's Axis 3 Z (the same axes Elite has those bindings on).
    /// </summary>
    private static int[]? SdlOrder(DeviceId dev)
    {
        var match = GameControllers.List().Where(c => c.Vid == dev.Vid && c.Pid == dev.Pid).Skip(dev.Nth).FirstOrDefault();
        return match == null ? null : Enumerable.Range(0, 8).Where(i => match.HasAxis[i]).ToArray();
    }
}
