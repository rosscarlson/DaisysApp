namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// "Set": waits for the control the user means, on any controller, as EmuWheel's Configurator does. For a button or
/// the d-pad, the next button pressed or hat pushed; for an axis role, the axis that really moves.
/// <para>
/// Two traps it avoids. DirectInput reports a device's axes centred until the device sends its first report, and some
/// wheel bases (Fanatec) only send one when something changes: touching the wheel then made every axis jump from the
/// middle to where it really rests, and an axis resting at full (the base's own pedal input, with the pedals on their
/// own USB cable) looked like it had moved all the way, so Steering got it and sat at +225°. A device's first change is
/// now taken as it waking up: where it rests is read again from there. And a sensitive wheel (1080°) moves at a touch,
/// so an axis isn't taken the moment it moves: once one has, it watches for 1.5 seconds and takes the axis that
/// travelled furthest in that time, back and forth, so a single jump (or an axis stuck at full) can't win.
/// </para>
/// </summary>
internal static class Capture
{
    /// <summary>What was used, and for an axis whether it rested near its top (a pedal that reads full when released).</summary>
    public sealed record Result(InputRef Input, bool RestsHigh);

    /// <summary>How long an axis is watched once one has started to move, before the one that moved most is taken.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    private sealed class Device
    {
        public required ControllerInfo Info;
        public required GameControllers.Reader Reader;
        public ControllerState Base = new(), Now = new();
        public readonly ControllerState Last = new();
        public bool Awake;
        /// <summary>Per axis: how far it has travelled (summed, back and forth) and its furthest from rest, since watching began.</summary>
        public readonly double[] Travel = new double[8], Excursion = new double[8];
    }

    /// <summary>
    /// The control meant. <paramref name="axis"/>: an axis role, so only axes count (moved at least
    /// <paramref name="minMove"/> of their range from rest); otherwise only buttons and hat directions. Controls in
    /// <paramref name="ignore"/> (bound to something else) are passed over.
    /// </summary>
    public static Task<Result?> NextAsync(IntPtr window, TimeSpan timeout, IReadOnlyCollection<InputRef> ignore, bool axis, double minMove, CancellationToken cancel) => Task.Run(() =>
    {
        var open = new List<Device>();
        bool Ignored(Guid device, InputRef.InputKind kind, int index, int direction) => ignore.Any(r =>
            r.Device == device && r.Kind == kind && r.Index == index && (kind != InputRef.InputKind.Pov || r.Direction == direction));
        InputRef Ref(ControllerInfo info, InputRef.InputKind kind, int index, int direction) => new()
        {
            Device = info.Instance, Product = info.Product, DeviceName = info.Name, Kind = kind, Index = index, Direction = direction,
        };
        try
        {
            foreach (var c in GameControllers.List())
                if (GameControllers.Open(c.Instance, window) is { } r)
                    open.Add(new Device { Info = c, Reader = r });
            // where everything rests: three good readings in a row (a device that never gives them is left out)
            var good = new int[open.Count];
            for (int tries = 0; tries < 25 && good.Any(g => g < 3); tries++)
            {
                for (int k = 0; k < open.Count; k++)
                    if (good[k] < 3) good[k] = open[k].Reader.Read(open[k].Base) ? good[k] + 1 : 0;
                Thread.Sleep(20);
            }
            for (int k = open.Count - 1; k >= 0; k--)
                if (good[k] < 3) { open[k].Reader.Dispose(); open.RemoveAt(k); }
            foreach (var d in open) d.Base.CopyTo(d.Last);

            var end = DateTime.UtcNow + timeout;
            DateTime? watchingUntil = null;
            while (DateTime.UtcNow < end && !cancel.IsCancellationRequested)
            {
                foreach (var d in open)
                {
                    if (!d.Reader.Read(d.Now)) continue;
                    // binding an axis: a device's first change is it waking (its first report), and where it really
                    // rests is read from there; a device that reports all along wakes at its first flicker, which costs
                    // nothing. (Buttons read released before a first report as after, so binding a button needn't wait.)
                    if (axis && !d.Awake)
                    {
                        if (!Changed(d.Base, d.Now)) continue;
                        d.Awake = true;
                        d.Now.CopyTo(d.Base);
                        d.Now.CopyTo(d.Last);
                        continue;
                    }
                    if (!axis)
                    {
                        for (int i = 0; i < d.Now.Buttons.Length; i++)
                            if (d.Now.Buttons[i] && !d.Base.Buttons[i] && !Ignored(d.Info.Instance, InputRef.InputKind.Button, i, 0))
                                return new Result(Ref(d.Info, InputRef.InputKind.Button, i, 0), false);
                        for (int i = 0; i < 4; i++)
                            if (d.Now.Povs[i] >= 0 && d.Base.Povs[i] < 0)
                            {
                                int dir = (int)Math.Round(d.Now.Povs[i] / 9000.0) % 4;
                                if (!Ignored(d.Info.Instance, InputRef.InputKind.Pov, i, dir)) return new Result(Ref(d.Info, InputRef.InputKind.Pov, i, dir), false);
                            }
                        continue;
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        if (double.IsNaN(d.Now.Axes[i]) || double.IsNaN(d.Base.Axes[i]) || Ignored(d.Info.Instance, InputRef.InputKind.Axis, i, 0)) continue;
                        d.Travel[i] += Math.Abs(d.Now.Axes[i] - d.Last.Axes[i]);
                        d.Excursion[i] = Math.Max(d.Excursion[i], Math.Abs(d.Now.Axes[i] - d.Base.Axes[i]));
                        if (watchingUntil == null && d.Excursion[i] >= minMove) watchingUntil = DateTime.UtcNow + Settle;
                    }
                    d.Now.CopyTo(d.Last);
                }
                // after the settling time: the axis that travelled furthest (and moved far enough from rest)
                if (watchingUntil is { } until && DateTime.UtcNow >= until)
                {
                    var best = open.SelectMany(d => Enumerable.Range(0, 8).Where(i => d.Excursion[i] >= minMove).Select(i => (d, i)))
                        .OrderByDescending(x => x.d.Travel[x.i]).FirstOrDefault();
                    if (best.d != null)
                        return new Result(Ref(best.d.Info, InputRef.InputKind.Axis, best.i, 0), best.d.Base.Axes[best.i] > 0.6);
                    watchingUntil = null;
                }
                Thread.Sleep(10);
            }
            return null;
        }
        finally
        {
            foreach (var d in open) d.Reader.Dispose();
        }
    }, cancel);

    private static bool Changed(ControllerState a, ControllerState b)
    {
        for (int i = 0; i < 8; i++)
            if (!double.IsNaN(a.Axes[i]) && !double.IsNaN(b.Axes[i]) && Math.Abs(a.Axes[i] - b.Axes[i]) > 0.0005) return true;
        for (int i = 0; i < a.Buttons.Length; i++) if (a.Buttons[i] != b.Buttons[i]) return true;
        for (int i = 0; i < 4; i++) if (a.Povs[i] != b.Povs[i]) return true;
        return false;
    }
}
